using Microsoft.Extensions.Configuration;
using MpsMes.Core.Enums;
using MpsMes.Core.Interfaces;
using OpenCvSharp;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.IO;

namespace MpsMes.Infrastructure.Vision;

/// <summary>
/// 비전 검사 서비스
/// - 카메라: OpenCvSharp4 DirectShow (x86 호환)
/// - 추론:   별도 Python 프로세스(x64)와 TCP 소켓 통신
///           → x86/x64 비트 충돌 완전 해결
/// </summary>
public class VisionService : IVisionService
{
    // ── 카메라 ──────────────────────────────────────────────────────
    private VideoCapture? _capture;
    private CancellationTokenSource? _previewCts;
    private readonly string _saveFolder;
    private readonly string? _pythonExePath;
    private readonly bool _autoStartServer;
    private readonly string _serverHost;
    private readonly int _serverPort;
    private string _modelPath = string.Empty;
    private CameraFilterSettings _cameraFilters = new();

    public void SetCameraFilters(CameraFilterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Volatile.Write(ref _cameraFilters, settings);
        _showAnnotation = false;
    }

    private Mat ApplyCameraFilters(Mat source, bool inspection = false)
    {
        var settings = Volatile.Read(ref _cameraFilters);
        using var corrected = ApplyAntiGlareFilter(source);
        return inspection && !settings.ApplyToInspection
            ? corrected.Clone()
            : CameraFrameFilter.Apply(corrected, settings);
    }

    // 마지막 검사 어노테이션 이미지 (프리뷰에 덮어씌워지지 않도록 유지)
    private byte[]? _lastAnnotatedFrame = null;
    private bool    _showAnnotation     = false;  // 검사 후 true, 다음 검사 시작 전까지 유지

    // ── 추론 서버 프로세스 ──────────────────────────────────────────
    private Process?    _serverProcess;
    private TcpClient?  _tcpClient;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _tcpLock = new(1, 1);

    private const int    CONNECT_TIMEOUT = 10000; // 10초

    // 프로토콜 CMD
    private const byte CMD_LOAD  = 0x01;
    private const byte CMD_INFER = 0x02;
    private const byte CMD_PING  = 0xFF;

    public bool IsCameraConnected => _capture?.IsOpened() ?? false;
    private bool _modelActuallyLoaded = false;

    public bool IsModelLoaded => _modelActuallyLoaded
                              && _tcpClient?.Connected == true;

    public event Action<VisionFrame>?  FrameCaptured;
    public event Action<VisionResult>? InspectionCompleted;

    public VisionService(IConfiguration config)
    {
        _saveFolder = ResolvePath(config["Vision:ImageSavePath"] ?? "Images");
        _pythonExePath = config["Vision:PythonExePath"];
        _autoStartServer = bool.TryParse(config["Vision:AutoStartServer"], out var autoStart)
            ? autoStart
            : true;
        _serverHost = config["Vision:ServerHost"] ?? "127.0.0.1";
        _serverPort = int.TryParse(config["Vision:ServerPort"], out var serverPort)
            ? serverPort
            : 9999;
    }

    // ── 카메라 연결 ─────────────────────────────────────────────────
    public async Task<bool> ConnectCameraAsync(int deviceIndex = 0)
    {
        return await Task.Run(() =>
        {
            _capture = new VideoCapture(deviceIndex, VideoCaptureAPIs.DSHOW);
            _capture.Set(VideoCaptureProperties.FrameWidth,  1280);
            _capture.Set(VideoCaptureProperties.FrameHeight, 720);
            _capture.Set(VideoCaptureProperties.Fps, 30);
            return _capture.IsOpened();
        });
    }

    public async Task DisconnectCameraAsync()
    {
        StopPreview();
        await Task.Run(() =>
        {
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        });
    }

    // ── 모델 로드 (Python 서버 시작 → 모델 로드 명령 전송) ───────────
    public async Task<bool> LoadModelAsync(string modelPath)
    {
        try
        {
            _modelPath = ResolvePath(modelPath);

            // TCP 연결 (이미 연결된 경우 재사용)
            if (_tcpClient == null || !_tcpClient.Connected)
            {
                bool connected = await TryConnectTcpAsync();
                if (!connected)
                {
                    if (!_autoStartServer)
                        throw new VisionException("Inference server is not connected. Start inference_server.py first or enable Vision:AutoStartServer.");

                    await StartInferenceServerAsync();
                    await ConnectTcpAsync();
                }
                if (!connected && false)
                    throw new VisionException(
                        "추론 서버에 연결할 수 없습니다.\n\n" +
                        "Anaconda Prompt에서 서버를 먼저 시작하세요:\n" +
                        $"cd C:\\project\\새로\\dong_mes_orange\n" +
                        "python inference_server.py");
            }

            // 모델 로드 명령 전송
            // 모델 로드 명령 전송
            JsonElement result;
            try
            {
                result = await SendCommandAsync(CMD_LOAD,
                    Encoding.UTF8.GetBytes(_modelPath));
            }
            catch (Exception tcpEx)
            {
                // 서버가 모델 로드 중 죽은 경우
                var exitCode = _serverProcess?.HasExited == true
                    ? $" (서버 종료코드: {_serverProcess.ExitCode})" : "";
                throw new VisionException(
                    $"모델 로드 중 서버 연결 끊김{exitCode}\n\n" +
                    $"가능한 원인:\n" +
                    $"• 모델 파일 형식이 맞지 않음 (.pt 파일 확인)\n" +
                    $"• 메모리 부족\n" +
                    $"• PyTorch 버전 호환성 문제\n\n" +
                    $"VS 출력창의 [PY ERR] 메시지를 확인하세요.", tcpEx);
            }

            if (result.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                _modelActuallyLoaded = true;
                return true;
            }

            var err = result.TryGetProperty("error", out var e)
                      ? e.GetString() : "알 수 없는 오류";
            throw new VisionException($"모델 로드 실패:\n{err}");
        }
        catch (VisionException) { throw; }
        catch (Exception ex)
        {
            throw new VisionException($"추론 서버 오류: {ex.Message}", ex);
        }
    }

    /// <summary>빠른 연결 시도 (1초 타임아웃) - 외부 서버 감지용</summary>
    private async Task<bool> TryConnectTcpAsync()
    {
        try
        {
            var client = new TcpClient();
            var task   = client.ConnectAsync(_serverHost, _serverPort);
            if (await Task.WhenAny(task, Task.Delay(1000)) == task && client.Connected)
            {
                _tcpClient = client;
                _stream    = client.GetStream();
                _stream.ReadTimeout  = 15_000;   // 15초 (추론 최대 대기)
                _stream.WriteTimeout = 5_000;    // 5초
                System.Diagnostics.Debug.WriteLine("[CS] 외부 서버에 연결됨");
                return true;
            }
            client.Dispose();
            return false;
        }
        catch { return false; }
    }

    /// <summary>포트가 이미 열려있는지 확인</summary>
    private static bool IsPortOpen(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var result  = client.BeginConnect(host, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(500);
            if (success) client.EndConnect(result);
            return success;
        }
        catch { return false; }
    }

    // ── Python 추론 서버 프로세스 시작 ──────────────────────────────
    private async Task StartInferenceServerAsync()
    {
        var pythonExe    = FindPythonExe(_pythonExePath);
        var serverScript = FindServerScript();

        var errorOutput = new System.Text.StringBuilder();
        var readyFlag   = false;

        _serverProcess = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = pythonExe,
                Arguments              = $"\"{serverScript}\" --host {_serverHost} --port {_serverPort}",
                WorkingDirectory       = Path.GetDirectoryName(serverScript),
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            }
        };

        _serverProcess.OutputDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            System.Diagnostics.Debug.WriteLine($"[PY] {e.Data}");
            if (e.Data.Contains("[READY]")) readyFlag = true;
        };

        _serverProcess.ErrorDataReceived += (s, e) =>
        {
            if (e.Data == null) return;
            System.Diagnostics.Debug.WriteLine($"[PY ERR] {e.Data}");
            errorOutput.AppendLine(e.Data);
        };

        _serverProcess.Start();
        _serverProcess.BeginOutputReadLine();
        _serverProcess.BeginErrorReadLine();

        // [READY] 또는 프로세스 종료 대기 (최대 20초)
        var deadline = DateTime.Now.AddSeconds(20);
        while (!readyFlag && DateTime.Now < deadline)
        {
            if (_serverProcess.HasExited) break;
            await Task.Delay(100);
        }

        if (!readyFlag)
        {
            var errMsg = errorOutput.ToString().Trim();
            if (errMsg.Contains("ModuleNotFoundError") || errMsg.Contains("No module named"))
            {
                var missing = "";
                if (errMsg.Contains("torch"))       missing += "torch ";
                if (errMsg.Contains("torchvision")) missing += "torchvision ";
                if (errMsg.Contains("PIL") || errMsg.Contains("pillow")) missing += "pillow";
                if (errMsg.Contains("ultralytics")) missing += "ultralytics";
                throw new VisionException(
                    $"Python 패키지 누락: {missing.Trim()}\n\n" +
                    $"cmd에서 실행:\npip install ultralytics torch torchvision pillow");
            }
            throw new VisionException(
                $"Python 추론 서버 시작 실패.\n\nPython: {pythonExe}\n\n" +
                (string.IsNullOrWhiteSpace(errMsg) ? "cmd에서 직접 실행하여 확인:\npython inference_server.py" : $"오류:\n{errMsg}"));
        }

        // [READY] 후 500ms 대기 (서버 accept 루프 준비 시간)
        await Task.Delay(500);
    }

    // ── TCP 연결 ─────────────────────────────────────────────────────
    private async Task ConnectTcpAsync()
    {
        _tcpClient?.Dispose();
        _tcpClient = new TcpClient();

        var deadline = DateTime.Now.AddMilliseconds(CONNECT_TIMEOUT);
        while (DateTime.Now < deadline)
        {
            try
            {
                await _tcpClient.ConnectAsync(_serverHost, _serverPort);
                _stream = _tcpClient.GetStream();
                // 모델 로드는 오래 걸릴 수 있어 넉넉하게 설정
                _stream.ReadTimeout  = 15_000;   // 15초
                _stream.WriteTimeout = 5_000;    // 5초
                return;
            }
            catch { await Task.Delay(200); }
        }
        throw new VisionException($"Inference server({_serverHost}:{_serverPort}) connection failed.");
    }

    // ── 프리뷰 ──────────────────────────────────────────────────────
    public void StartPreview()
    {
        if (_capture == null || !_capture.IsOpened()) return;
        _previewCts = new CancellationTokenSource();
        var token = _previewCts.Token;

        Task.Run(async () =>
        {
            using var mat = new Mat();
            while (!token.IsCancellationRequested)
            {
                if (_capture.Read(mat) && !mat.Empty())
                {
                    byte[] bytes;

                    if (_showAnnotation && _lastAnnotatedFrame != null)
                    {
                        // 검사 완료 후 → 어노테이션 이미지 고정 표시
                        bytes = _lastAnnotatedFrame;
                    }
                    else
                    {
                        // 일반 프리뷰 → 빛반사 필터 적용
                        using var filtered = ApplyCameraFilters(mat);
                        bytes = filtered.ToBytes(".jpg",
                            new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));
                    }

                    FrameCaptured?.Invoke(new VisionFrame
                    {
                        ImageData  = bytes,
                        Width      = mat.Width,
                        Height     = mat.Height,
                        CapturedAt = DateTime.Now
                    });
                }
                await Task.Delay(33, token);
            }
        }, token);
    }

    public void StopPreview() => _previewCts?.Cancel();

    // ── 즉시 캡처+검사 (버퍼 플러시 없음 — 타이밍 정확) ──────────
    public async Task<VisionResult> InspectWithCaptureAsync()
    {
        if (_capture == null || !_capture.IsOpened())
            throw new VisionException("카메라가 연결되지 않았습니다.");
        if (_tcpClient == null || !_tcpClient.Connected)
        {
            bool ok = await TryConnectTcpAsync();
            if (!ok) throw new VisionException("추론 서버 연결이 끊겼습니다.");
        }
        if (!_modelActuallyLoaded)
            throw new VisionException("모델이 로드되지 않았습니다.");

        // 버퍼 플러시 — 웹캠 내부에 쌓인 오래된 프레임 제거 후 최신 프레임 캡처
        // 30fps 기준 버퍼 최대 4~6장 쌓임 → 5장 버리면 ~170ms 이내 최신 프레임
        using var dummy = new Mat();
        for (int i = 0; i < 5; i++) _capture.Read(dummy);

        var mat = new Mat();
        _capture.Read(mat);
        if (mat.Empty()) throw new VisionException("프레임 캡처 실패");

        return await InspectWithFrameAsync(mat);
    }

    // ── 즉시 프레임 캡처 (필터 없음) ──────────────────────────────
    // 딜레이 직후 타이밍 정확한 캡처용 — 필터/추론은 별도로 처리
    public Mat? CaptureRaw()
    {
        if (_capture == null || !_capture.IsOpened()) return null;
        var mat = new Mat();
        _capture.Read(mat);
        return mat.Empty() ? null : mat;
    }

    // ── 검사 실행 (캡처된 프레임 사용) ────────────────────────────
    public async Task<VisionResult> InspectWithFrameAsync(Mat capturedMat)
    {
        if (_tcpClient == null || !_tcpClient.Connected)
        {
            bool reconnected = await TryConnectTcpAsync();
            if (!reconnected)
                throw new VisionException("추론 서버 연결이 끊겼습니다.");
        }
        if (!_modelActuallyLoaded)
            throw new VisionException("모델이 로드되지 않았습니다.");

        return await Task.Run(async () =>
        {
            _showAnnotation = false;

            using var filtered = ApplyCameraFilters(capturedMat, inspection: true);

            Directory.CreateDirectory(_saveFolder);
            var fileName  = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
            var imagePath = Path.Combine(_saveFolder, fileName);
            filtered.SaveImage(imagePath);

            var jpgBytes = filtered.ToBytes(".jpg",
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));

            var json = await SendCommandAsync(CMD_INFER, jpgBytes);

            var resultType  = VisionResultType.OK;
            string? defect  = null;
            double  conf    = 0;
            byte[]  overlayBytes = jpgBytes;

            if (json.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                var r = json.GetProperty("result").GetString();
                resultType = r == "NG" ? VisionResultType.NG : VisionResultType.OK;
                defect     = json.TryGetProperty("defect", out var d) && d.ValueKind != JsonValueKind.Null
                             ? d.GetString() : null;
                conf       = json.TryGetProperty("confidence", out var c) ? c.GetDouble() : 0;

                if (json.TryGetProperty("polygons", out var polys)
                    && polys.ValueKind == JsonValueKind.Array
                    && polys.GetArrayLength() > 0)
                {
                    overlayBytes = DrawPolygonOverlay(filtered, polys, resultType, conf);
                }
            }

            _lastAnnotatedFrame = overlayBytes;
            _showAnnotation     = true;

            var result = new VisionResult
            {
                ResultType  = resultType,
                DefectType  = defect,
                Confidence  = conf,
                ImageData   = overlayBytes,
                ImagePath   = imagePath,
                InspectedAt = DateTime.Now
            };
            InspectionCompleted?.Invoke(result);
            return result;
        });
    }

    // ── 검사 실행 ───────────────────────────────────────────────────
    public async Task<VisionResult> InspectAsync()
    {
        if (_capture == null || !_capture.IsOpened())
            throw new VisionException("카메라가 연결되지 않았습니다.");

        // TCP 연결 끊겼으면 재연결 시도
        if (_tcpClient == null || !_tcpClient.Connected)
        {
            bool reconnected = await TryConnectTcpAsync();
            if (!reconnected)
                throw new VisionException(
                    "추론 서버 연결이 끊겼습니다.\n" +
                    "Anaconda Prompt에서 서버가 실행 중인지 확인하세요.");
        }

        if (!_modelActuallyLoaded)
            throw new VisionException("모델이 로드되지 않았습니다.");

        return await Task.Run(async () =>
        {
            // 새 검사 시작 → 어노테이션 해제
            _showAnnotation = false;

            // 카메라 버퍼 플러시 — 쌓인 오래된 프레임 버리고 최신 프레임 촬영
            using var dummy = new Mat();
            for (int i = 0; i < 3; i++) _capture.Read(dummy);

            using var mat = new Mat();
            _capture.Read(mat);
            if (mat.Empty()) throw new VisionException("프레임 캡처 실패");

            // 빛반사 필터 적용 후 저장/전송
            using var filtered = ApplyCameraFilters(mat, inspection: true);

            Directory.CreateDirectory(_saveFolder);
            var fileName  = $"{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
            var imagePath = Path.Combine(_saveFolder, fileName);
            filtered.SaveImage(imagePath);

            var jpgBytes = filtered.ToBytes(".jpg",
                new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));

            // TCP로 이미지 전송 → 결과 수신
            var json = await SendCommandAsync(CMD_INFER, jpgBytes);

            var resultType  = VisionResultType.OK;
            string? defect  = null;
            double  conf    = 0;
            byte[]  overlayBytes = jpgBytes; // 기본값: 원본 이미지

            if (json.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                var r = json.GetProperty("result").GetString();
                resultType = r == "NG" ? VisionResultType.NG : VisionResultType.OK;
                defect     = json.TryGetProperty("defect", out var d)
                             && d.ValueKind != JsonValueKind.Null
                             ? d.GetString() : null;
                conf       = json.TryGetProperty("confidence", out var c)
                             ? c.GetDouble() : 0;

                // 폴리곤 오버레이
                if (json.TryGetProperty("polygons", out var polys)
                    && polys.ValueKind == JsonValueKind.Array
                    && polys.GetArrayLength() > 0)
                {
                    overlayBytes = DrawPolygonOverlay(filtered, polys, resultType, conf);
                }
            }

            // 어노테이션 이미지 고정 (프리뷰가 덮어쓰지 않도록)
            _lastAnnotatedFrame = overlayBytes;
            _showAnnotation     = true;

            var result = new VisionResult
            {
                ResultType  = resultType,
                DefectType  = defect,
                Confidence  = conf,
                ImageData   = overlayBytes,
                ImagePath   = imagePath,
                InspectedAt = DateTime.Now
            };

            InspectionCompleted?.Invoke(result);
            return result;
        });
    }

    // ── 빛반사 방지 필터 ─────────────────────────────────────────────
    // 강한 백색 반사광을 Telea Inpaint로 복원한 뒤 CLAHE와 감마 보정을 적용한다.
    private static Mat ApplyAntiGlareFilter(Mat src)
    {
        var dst = new Mat();
        try
        {
            using var inpainted = RemoveSpecularGlare(src);

            // LAB 색공간 → L채널에만 CLAHE 적용
            using var lab = new Mat();
            Cv2.CvtColor(inpainted, lab, ColorConversionCodes.BGR2Lab);
            var channels = Cv2.Split(lab);

            // clipLimit 2.0: 반사 억제와 화질 균형
            using var clahe = Cv2.CreateCLAHE(
                clipLimit: 2.0,
                tileGridSize: new OpenCvSharp.Size(8, 8));
            clahe.Apply(channels[0], channels[0]);

            Cv2.Merge(channels, lab);
            Cv2.CvtColor(lab, dst, ColorConversionCodes.Lab2BGR);

            // 감마 1.10: Inpaint 후 남은 밝은 반사를 약하게 억제한다.
            ApplyGamma(dst, dst, gamma: 1.10);

            foreach (var ch in channels) ch.Dispose();
            return dst;
        }
        catch
        {
            src.CopyTo(dst);
            return dst;
        }
    }

    private static Mat RemoveSpecularGlare(Mat src)
    {
        var result = new Mat();
        using var hsv = new Mat();
        using var glareMask = new Mat();
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));

        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);

        // 거의 흰색으로 포화된 영역만 선택하여 밝은 결함선이 지워지는 것을 방지한다.
        Cv2.InRange(
            hsv,
            new Scalar(0, 0, 248),
            new Scalar(180, 45, 255),
            glareMask);

        Cv2.MorphologyEx(
            glareMask, glareMask, MorphTypes.Close, kernel,
            iterations: 1);
        Cv2.Dilate(glareMask, glareMask, kernel, iterations: 1);

        var maskRatio = (double)Cv2.CountNonZero(glareMask)
            / (glareMask.Rows * glareMask.Cols);

        // 넓은 흰 배경이나 제품 자체가 마스크로 잡히면 원본을 유지한다.
        if (maskRatio > 0 && maskRatio <= 0.08)
            Cv2.Inpaint(src, glareMask, result, 3.0, InpaintMethod.Telea);
        else
            src.CopyTo(result);

        return result;
    }

    private static void ApplyGamma(Mat src, Mat dst, double gamma)
    {
        // LUT (Look-Up Table) 방식으로 빠른 감마 적용
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Min(255, Math.Pow(i / 255.0, gamma) * 255.0);

        using var lutMat = new Mat(1, 256, MatType.CV_8UC1, lut);
        Cv2.LUT(src, lutMat, dst);
    }

    // ── 폴리곤 오버레이 (두껍고 선명하게) ─────────────────────────
    private static byte[] DrawPolygonOverlay(
        Mat src, JsonElement polys, VisionResultType resultType, double conf)
    {
        try
        {
            using var dst = src.Clone();
            int w = dst.Width, h = dst.Height;

            // OK=초록, NG=빨강 (더 밝고 선명한 색상)
            var color = resultType == VisionResultType.OK
                ? new Scalar(0, 230, 0)     // 선명한 초록
                : new Scalar(0, 0, 255);    // 선명한 빨강

            foreach (var poly in polys.EnumerateArray())
            {
                if (!poly.TryGetProperty("points", out var pts)) continue;

                var points = pts.EnumerateArray()
                    .Select(p =>
                    {
                        var arr = p.EnumerateArray().ToArray();
                        return new Point(
                            (int)(arr[0].GetDouble() * w),
                            (int)(arr[1].GetDouble() * h));
                    })
                    .ToArray();

                if (points.Length < 3) continue;

                // 반투명 채우기 (alpha 0.4 → 더 잘 보임)
                using var overlay = dst.Clone();
                Cv2.FillPoly(overlay, new[] { points }, color);
                Cv2.AddWeighted(overlay, 0.4, dst, 0.6, 0, dst);

                // 외곽선 두껍게 (thickness 3)
                Cv2.Polylines(dst, new[] { points }, true, color, 3);

                // 신뢰도 텍스트 (더 크게)
                var polyConf = poly.TryGetProperty("confidence", out var pc)
                    ? pc.GetDouble() : conf;
                var className = poly.TryGetProperty("class_name", out var cn)
                    ? cn.GetString() ?? "" : "";
                var label = string.IsNullOrEmpty(className)
                    ? $"{polyConf:F1}%"
                    : $"{className}  {polyConf:F1}%";

                // 텍스트 위치: 폴리곤 최상단 중앙
                var centerX = (int)points.Average(p => p.X);
                var topY    = points.Min(p => p.Y);
                var tp      = new Point(centerX, Math.Max(topY - 10, 25));

                double fontScale = 0.7;
                int    thickness = 2;
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex,
                    fontScale, thickness, out int baseline);

                // 텍스트 배경 박스 (불투명)
                var bgPt1 = new Point(tp.X - textSize.Width / 2 - 4, tp.Y - textSize.Height - 4);
                var bgPt2 = new Point(tp.X + textSize.Width / 2 + 4, tp.Y + baseline + 2);
                Cv2.Rectangle(dst, bgPt1, bgPt2, color, -1);

                // 텍스트 (흰색)
                Cv2.PutText(dst, label,
                    new Point(tp.X - textSize.Width / 2, tp.Y),
                    HersheyFonts.HersheySimplex, fontScale, Scalar.White, thickness);
            }

            return dst.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        }
        catch
        {
            return src.ToBytes(".jpg", new ImageEncodingParam(ImwriteFlags.JpegQuality, 95));
        }
    }

    // ── TCP 명령 전송/수신 (직렬화) ──────────────────────────────────
    private async Task<JsonElement> SendCommandAsync(byte cmd, byte[] payload)
    {
        await _tcpLock.WaitAsync();
        try
        {
            if (_stream == null) throw new VisionException("TCP 연결 없음");

            // 전송: CMD(1) + LENGTH(4 big-endian) + PAYLOAD
            var header = new byte[5];
            header[0] = cmd;
            var lenBytes = BitConverter.GetBytes(payload.Length);
            if (BitConverter.IsLittleEndian) Array.Reverse(lenBytes);
            Buffer.BlockCopy(lenBytes, 0, header, 1, 4);

            await _stream.WriteAsync(header);
            if (payload.Length > 0)
                await _stream.WriteAsync(payload);

            // 수신: LENGTH(4) + JSON
            var lenBuf = new byte[4];
            await ReadExactAsync(_stream, lenBuf, 4);
            if (BitConverter.IsLittleEndian) Array.Reverse(lenBuf);
            int respLen = BitConverter.ToInt32(lenBuf, 0);

            var respBuf = new byte[respLen];
            await ReadExactAsync(_stream, respBuf, respLen);

            return JsonDocument.Parse(respBuf).RootElement;
        }
        finally
        {
            _tcpLock.Release();
        }
    }

    private static async Task ReadExactAsync(NetworkStream stream, byte[] buf, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buf.AsMemory(offset, count - offset));
            if (read == 0) throw new VisionException("서버 연결 끊김");
            offset += read;
        }
    }

    // ── python.exe (x64 실제 실행파일) 탐색 ────────────────────────
    // Microsoft Store Python(WindowsApps 별칭)은 제외
    private static string FindPythonExe(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolved = ResolvePath(configuredPath);
            if (IsRealPythonExe(resolved)) return resolved;
        }
        // 1. 환경변수 PYTHON_EXE 직접 지정
        var env = Environment.GetEnvironmentVariable("PYTHON_EXE");
        if (!string.IsNullOrEmpty(env) && IsRealPythonExe(env)) return env;

        // 2. PATH에서 탐색 (WindowsApps 제외)
        var paths = Environment.GetEnvironmentVariable("PATH")
            ?.Split(';', StringSplitOptions.RemoveEmptyEntries) ?? [];
        foreach (var dir in paths)
        {
            var exe = Path.Combine(dir.Trim(), "python.exe");
            if (IsRealPythonExe(exe)) return exe;
        }

        // 3. py 런처로 실제 경로 조회
        var fromLauncher = GetPythonPathFromLauncher();
        if (fromLauncher != null) return fromLauncher;

        // 4. 레지스트리
        var fromReg = GetPythonPathFromRegistry();
        if (fromReg != null) return fromReg;

        // 5. 일반 설치 경로
        var user = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(userProfile, @"anaconda3\envs\mpsmes\python.exe"),
            Path.Combine(userProfile, @"miniconda3\envs\mpsmes\python.exe"),
            @"C:\ProgramData\anaconda3\envs\mpsmes\python.exe",
            @"C:\ProgramData\miniconda3\envs\mpsmes\python.exe",
            Path.Combine(user, @"Programs\Python\Python312\python.exe"),
            Path.Combine(user, @"Programs\Python\Python311\python.exe"),
            Path.Combine(user, @"Programs\Python\Python310\python.exe"),
            Path.Combine(user, @"Programs\Python\Python39\python.exe"),
            @"C:\Python312\python.exe",
            @"C:\Python311\python.exe",
            @"C:\Python310\python.exe",
            @"C:\Python39\python.exe",
        };
        foreach (var c in candidates)
            if (IsRealPythonExe(c)) return c;

        return ThrowPythonNotFound();
    }

    private static string ThrowPythonNotFound()
        => throw new VisionException(
            "Python runtime was not found.\n\n" +
            "Install Python 3.10 or newer from https://www.python.org/downloads/windows/.\n" +
            "Enable 'Add python.exe to PATH' during installation, then run setup_python_env.bat.\n\n" +
            "Expected Conda environment: anaconda3\\envs\\mpsmes\\python.exe\n" +
            "You can also set Vision:PythonExePath in appsettings.json or PYTHON_EXE.");

    /// <summary>실제 실행 가능한 python.exe인지 확인 (WindowsApps 별칭 제외)</summary>
    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
            Path.Combine(baseDir, "..", path),
            Path.Combine(baseDir, "..", "..", path),
            Path.Combine(baseDir, "..", "..", "..", path),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full) || Directory.Exists(full))
                return full;
        }

        return Path.GetFullPath(Path.Combine(baseDir, path));
    }

    private static bool IsRealPythonExe(string path)
    {
        if (!File.Exists(path)) return false;

        // WindowsApps 경로는 앱 별칭(alias) → 실제 실행 불가
        if (path.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))
            return false;

        // 파일 크기가 너무 작으면 별칭 (alias .exe는 보통 0~수백 bytes)
        var info = new FileInfo(path);
        if (info.Length < 1024) return false;

        return true;
    }

    /// <summary>py 런처를 통해 실제 python.exe 경로 획득</summary>
    private static string? GetPythonPathFromLauncher()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName               = "py",
                Arguments              = "-c \"import sys; print(sys.executable)\"",
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var line = p.StandardOutput.ReadLine()?.Trim();
            p.WaitForExit(3000);
            return (line != null && IsRealPythonExe(line)) ? line : null;
        }
        catch { return null; }
    }

    /// <summary>레지스트리에서 Python 설치 경로 획득</summary>
    private static string? GetPythonPathFromRegistry()
    {
        try
        {
            var roots   = new[] { Microsoft.Win32.Registry.LocalMachine,
                                  Microsoft.Win32.Registry.CurrentUser };
            var subKeys = new[] { @"SOFTWARE\Python\PythonCore",
                                  @"SOFTWARE\WOW6432Node\Python\PythonCore" };

            foreach (var root in roots)
            foreach (var sub  in subKeys)
            {
                using var key = root.OpenSubKey(sub);
                if (key == null) continue;

                foreach (var ver in key.GetSubKeyNames().OrderByDescending(v => v))
                {
                    using var install = key.OpenSubKey($@"{ver}\InstallPath");
                    var exePath = install?.GetValue("ExecutablePath") as string;
                    if (exePath != null && IsRealPythonExe(exePath)) return exePath;

                    var dir = install?.GetValue("") as string;
                    if (dir != null)
                    {
                        var exe = Path.Combine(dir.TrimEnd('\\'), "python.exe");
                        if (IsRealPythonExe(exe)) return exe;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    // ── inference_server.py 경로 탐색 ───────────────────────────────
    private static string FindServerScript()
    {
        // 실행파일 기준 탐색
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "inference_server.py"),
            Path.Combine(baseDir, "..", "inference_server.py"),
            Path.Combine(baseDir, "..", "..", "inference_server.py"),
            Path.Combine(baseDir, "..", "..", "..", "inference_server.py"),
        };

        foreach (var p in candidates)
        {
            var full = Path.GetFullPath(p);
            if (File.Exists(full)) return full;
        }

        throw new VisionException(
            "inference_server.py를 찾을 수 없습니다.\n" +
            $"실행파일 폴더({baseDir}) 또는 상위 폴더에 파일을 복사해주세요.");
    }

    public void Dispose()
    {
        StopPreview();
        _capture?.Release();
        _capture?.Dispose();
        _stream?.Dispose();
        _tcpClient?.Dispose();
        try { _serverProcess?.Kill(); } catch { }
        _serverProcess?.Dispose();
        _tcpLock.Dispose();
    }
}

public class VisionException : Exception
{
    public VisionException(string message, Exception? inner = null)
        : base(message, inner) { }
}
