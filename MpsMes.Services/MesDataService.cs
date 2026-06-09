using Dapper;
using MpsMes.Core.Interfaces;
using MpsMes.Core.Enums;

namespace MpsMes.Services;

/// <summary>
/// MES 데이터 저장 서비스
/// - 생산이력: D10/D11 증가 감지 시 저장
/// - PLC 스냅샷: 5초마다 저장
/// - 이벤트 로그: 상태 변화/오류 시 저장
/// </summary>
public class MesDataService : IDisposable
{
    private readonly IProductionRepository _prodRepo;
    private readonly IVisionRepository     _visionRepo;
    private readonly IEventLogRepository   _eventRepo;
    private readonly IPlcService           _plc;
    private readonly IVisionService?       _vision;  // 자동 검사용

    // 이전 PLC 데이터 (변화 감지용)
    private PlcData?  _prevData;
    private DateTime? _cycleStartTime;

    // 스냅샷 타이머
    private System.Timers.Timer? _snapshotTimer;
    private PlcData?              _latestData;

    // 마지막 저장된 생산이력 SEQ (비전 연결용)
    public long LastProdSeq { get; private set; } = 0;

    // 마지막 비전 결과 (생산이력과 연동)
    private VisionResult? _lastVisionResult;

    // 자동 검사 제어
    private bool _autoInspecting  = false;   // 검사 진행 중 플래그
    private int  _prevD0          = -1;      // 이전 D0 값
    private bool _autoInspectEnabled = true; // 자동 검사 활성화 여부

    public MesDataService(
        IProductionRepository prodRepo,
        IVisionRepository     visionRepo,
        IEventLogRepository   eventRepo,
        IPlcService           plc,
        IVisionService?       vision = null)
    {
        _prodRepo   = prodRepo;
        _visionRepo = visionRepo;
        _eventRepo  = eventRepo;
        _plc        = plc;
        _vision     = vision;

        // PLC 이벤트 구독
        _plc.DataRefreshed           += OnPlcDataRefreshed;
        _plc.ConnectionStatusChanged += OnPlcStatusChanged;

        // 스냅샷 타이머 (5초마다)
        _snapshotTimer = new System.Timers.Timer(5000);
        _snapshotTimer.Elapsed += async (s, e) => await SaveSnapshotAsync();
        _snapshotTimer.Start();
    }

    // ── PLC 데이터 수신 → 생산이력 저장 판단 ─────────────────────────
    private async void OnPlcDataRefreshed(PlcData data)
    {
        _latestData = data;

        if (_prevData == null)
        {
            _prevData       = data;
            _cycleStartTime = DateTime.Now;
            _prevD0         = data.D0_Mode;
            return;
        }

        var previousData = _prevData;
        // DB 저장을 기다리는 동안 다음 폴링이 와도 같은 증가량을 다시 처리하지 않도록 즉시 갱신한다.
        _prevData = data;

        // D0 = 115 (소재판별) 진입 감지 → 소재별 딜레이 후 촬영
        // T116 타이머 현재값 30 (3.0초) 도달 시 즉시 촬영
        bool timerTriggered = previousData.T116_TimerVal < 34 && data.T116_TimerVal >= 34;

        if (_autoInspectEnabled
            && _vision != null
            && _vision.IsCameraConnected
            && _vision.IsModelLoaded
            && !_autoInspecting
            && timerTriggered)
        {
            _ = TriggerAutoInspectAsync();
        }
        _prevD0 = data.D0_Mode;

        // D10(비금속) 또는 D11(금속) 증가 감지 → 사이클 완료
        int nonMetalDelta = data.D10_NonMetal - previousData.D10_NonMetal;
        int metalDelta    = data.D11_Metal    - previousData.D11_Metal;
        bool nonMetalIncreased = nonMetalDelta > 0;
        bool metalIncreased    = metalDelta > 0;

        if (nonMetalIncreased || metalIncreased)
        {
            bool? completedIsMetal = ResolveMaterialFromCounterDelta(metalDelta, nonMetalDelta);
            await SaveProductionHistoryAsync(data, completedIsMetal);
            _cycleStartTime = DateTime.Now;
        }
    }

    // ── 자동 비전 검사 ────────────────────────────────────────────────
    private async Task TriggerAutoInspectAsync()
    {
        _autoInspecting = true;
        try
        {
            // T116=30 시점 즉시 촬영
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var inspectTask = _vision!.InspectWithCaptureAsync();
            var completed   = await Task.WhenAny(inspectTask, Task.Delay(10000, cts.Token));

            if (completed != inspectTask)
            {
                await LogErrorAsync("AUTO_INSPECT_TIMEOUT", "비전 검사 타임아웃 (10초 초과)");
                return;
            }

            var result = await inspectTask;
            _lastVisionResult = result;

            await _eventRepo.InsertAsync(new EventLog
            {
                EventAt   = DateTime.Now,
                EventType = "INFO",
                EventCode = "AUTO_INSPECT",
                Message   = $"자동 비전 검사 [재질 판정 대기]: {(result.IsOk ? "OK" : "NG")} " +
                            $"{(result.DefectType != null ? $"({result.DefectType}) " : "")}" +
                            $"신뢰도:{result.Confidence:F1}%",
                Source    = "MesDataService"
            });
        }
        catch (Exception ex)
        {
            await LogErrorAsync("AUTO_INSPECT_FAIL", $"자동 검사 실패: {ex.Message}");
        }
        finally
        {
            // 예외/타임아웃 어떤 경우든 반드시 플래그 해제 → 다음 사이클 정상 진행
            _autoInspecting = false;
        }
    }

    // ── 생산이력 저장 ─────────────────────────────────────────────────
    private async Task SaveProductionHistoryAsync(PlcData data, bool? completedIsMetal)
    {
        try
        {
            // 사이클 시간 계산
            decimal? cycleTime = null;
            if (_cycleStartTime.HasValue)
                cycleTime = (decimal)(DateTime.Now - _cycleStartTime.Value).TotalSeconds;

            // 교대조 계산 (8시간 기준)
            var shiftCode = GetShiftCode(DateTime.Now);

            var record = new ProductionRecord
            {
                CompletedAt  = DateTime.Now,
                CycleTimeSec = cycleTime,
                MetalQty     = data.D11_Metal,
                NonMetalQty  = data.D10_NonMetal,
                ShiftCode    = shiftCode,
                // 비전 결과 연동 (최근 검사 결과 반영)
                VisionResult = _lastVisionResult?.IsOk == true ? "OK"
                             : _lastVisionResult != null       ? "NG"
                             : null,
                DefectType   = _lastVisionResult?.DefectType,
                Confidence   = _lastVisionResult != null
                             ? (decimal)_lastVisionResult.Confidence
                             : (decimal?)null,
            };

            LastProdSeq = await _prodRepo.InsertAsync(record);

            // 비전 결과가 있으면 TB_VISION_INSPECTION에도 PROD_SEQ 연결
            if (_lastVisionResult != null && completedIsMetal.HasValue)
            {
                await _visionRepo.InsertAsync(new VisionInspection
                {
                    InspectedAt = _lastVisionResult.InspectedAt,
                    Result      = _lastVisionResult.IsOk ? "OK" : "NG",
                    DefectType  = _lastVisionResult.DefectType,
                    Confidence  = (decimal)_lastVisionResult.Confidence,
                    ImagePath   = _lastVisionResult.ImagePath,
                    IsMetalYn   = completedIsMetal.Value,
                    ProdSeq     = LastProdSeq
                });
            }
            else if (_lastVisionResult != null)
            {
                await LogErrorAsync(
                    "MATERIAL_DELTA_AMBIGUOUS",
                    "D10과 D11이 동시에 증가하여 비전검사 재질을 확정하지 못했습니다.");
            }
            _lastVisionResult = null; // 사용 완료 후 초기화

            // 이벤트 로그 기록
            await _eventRepo.InsertAsync(new EventLog
            {
                EventAt   = DateTime.Now,
                EventType = "INFO",
                EventCode = "PROD_SAVE",
                Message   = $"생산이력 저장 - 금속:{data.D11_Metal} 비금속:{data.D10_NonMetal} 합계:{data.TotalQty} 사이클:{cycleTime:F1}초",
                Source    = "MesDataService"
            });
        }
        catch (Exception ex)
        {
            await LogErrorAsync("PROD_SAVE_FAIL", $"생산이력 저장 실패: {ex.Message}");
        }
    }

    private static bool? ResolveMaterialFromCounterDelta(int metalDelta, int nonMetalDelta)
    {
        if (metalDelta > 0 && nonMetalDelta <= 0) return true;
        if (nonMetalDelta > 0 && metalDelta <= 0) return false;
        return null;
    }

    // ── PLC 스냅샷 저장 (5초마다) ────────────────────────────────────
    private async Task SaveSnapshotAsync()
    {
        if (_latestData == null) return;
        try
        {
            // Repository에 스냅샷 저장 메서드 직접 호출
            using var con = GetDbConnection();
            const string sql = @"
                INSERT INTO TB_PLC_SNAPSHOT
                    (SNAP_AT, D10_NON_METAL, D11_METAL,
                     D2000_SERVO_POS, D2005_SERVO_STS,
                     MODE_D0, SIGNAL_D1)
                VALUES
                    (@SnapAt, @D10, @D11, @ServoPos, @ServoSts, @Mode, @Signal)";

            await con.ExecuteAsync(sql, new
            {
                SnapAt   = _latestData.SnapAt,
                D10      = _latestData.D10_NonMetal,
                D11      = _latestData.D11_Metal,
                ServoPos = _latestData.D2000_ServoPos,
                ServoSts = _latestData.D2005_ServoStatus,
                Mode     = _latestData.D0_Mode,
                Signal   = _latestData.D1_Signal
            });
        }
        catch { /* 스냅샷 오류는 무시 */ }
    }

    // ── PLC 상태 변화 로그 ────────────────────────────────────────────
    private async void OnPlcStatusChanged(PlcConnectionStatus status)
    {
        var msg = status switch
        {
            PlcConnectionStatus.Connected    => "PLC 연결됨",
            PlcConnectionStatus.Disconnected => "PLC 연결 해제",
            PlcConnectionStatus.Error        => "PLC 오류 발생",
            _                                => $"PLC 상태: {status}"
        };

        var eventType = status == PlcConnectionStatus.Error ? "ERROR" : "PLC";

        try
        {
            await _eventRepo.InsertAsync(new EventLog
            {
                EventAt   = DateTime.Now,
                EventType = eventType,
                EventCode = $"PLC_{status.ToString().ToUpper()}",
                Message   = msg,
                Source    = "PlcService"
            });
        }
        catch { /* 무시 */ }
    }

    // ── 비전 결과 수신 (외부에서 호출) ───────────────────────────────
    public void SetLastVisionResult(VisionResult result)
    {
        _lastVisionResult = result;
    }

    // ── 이벤트 로그 공개 메서드 ───────────────────────────────────────
    public async Task LogInfoAsync(string code, string message, string source = "MES")
    {
        try
        {
            await _eventRepo.InsertAsync(new EventLog
            {
                EventAt   = DateTime.Now,
                EventType = "INFO",
                EventCode = code,
                Message   = message,
                Source    = source
            });
        }
        catch { }
    }

    public async Task LogErrorAsync(string code, string message, string source = "MES")
    {
        try
        {
            await _eventRepo.InsertAsync(new EventLog
            {
                EventAt   = DateTime.Now,
                EventType = "ERROR",
                EventCode = code,
                Message   = message,
                Source    = source
            });
        }
        catch { }
    }

    // ── 교대조 계산 ───────────────────────────────────────────────────
    private static string GetShiftCode(DateTime dt)
    {
        int hour = dt.Hour;
        return hour switch
        {
            >= 6  and < 14 => "1조",   // 06:00 ~ 14:00
            >= 14 and < 22 => "2조",   // 14:00 ~ 22:00
            _               => "3조"    // 22:00 ~ 06:00
        };
    }

    // ── DB 연결 (스냅샷용 직접 접근) ─────────────────────────────────
    private MySqlConnector.MySqlConnection? _dbCon;
    private string _connStr = string.Empty;

    public void SetConnectionString(string connStr) => _connStr = connStr;

    private MySqlConnector.MySqlConnection GetDbConnection()
    {
        var con = new MySqlConnector.MySqlConnection(_connStr);
        return con;
    }

    public void Dispose()
    {
        _snapshotTimer?.Stop();
        _snapshotTimer?.Dispose();
    }
}
