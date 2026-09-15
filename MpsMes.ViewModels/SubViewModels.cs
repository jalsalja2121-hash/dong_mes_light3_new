using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MpsMes.Core.Interfaces;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MpsMes.ViewModels;

// =============================================
// 시퀀스 스텝 아이템 모델
// =============================================
public class SequenceStepItem : System.ComponentModel.INotifyPropertyChanged
{
    public string Name { get; set; } = string.Empty;

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set { _isActive = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsActive))); }
    }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
}

// =============================================
// 카메라 장치 모델
// =============================================
public class CameraDevice
{
    public int    Index { get; set; }
    public string Name  { get; set; } = string.Empty;
    public override string ToString() => Name;
}

// =============================================
// 모니터링 ViewModel
// =============================================
public partial class MonitorViewModel : ObservableObject
{
    [ObservableProperty] private int    _metalQty    = 0;
    [ObservableProperty] private int    _nonMetalQty = 0;
    [ObservableProperty] private int    _totalQty    = 0;
    [ObservableProperty] private int    _servoPos    = 0;
    [ObservableProperty] private string _servoStatus = "정상";
    [ObservableProperty] private bool   _isRunning   = false;
    [ObservableProperty] private string _sequenceName    = "대기";
    [ObservableProperty] private int    _sequenceStep    = 0;
    [ObservableProperty] private double _sequencePercent = 0;

    private static string GetSequenceName(int d0) => d0 switch
    {
        0              => "정지",
        > 0 and < 50   => "리셋 중",
        50             => "대기",
        85             => "시작",
        100            => "공급 전진",
        105            => "공급 후진",
        110            => "가공 / 분배",
        115            => "소재 판별",
        120            => "배출 전진",
        130            => "배출 후진",
        140            => "창고 적재",
        150            => "대기 복귀",
        155            => "완료",
        _              => $"진행중 ({d0})"
    };

    private static double GetSequencePercent(int d0) => d0 switch
    {
        0              => 0,
        > 0 and < 50   => 5,
        50             => 10,
        85             => 15,
        100            => 25,
        105            => 35,
        110            => 45,
        115            => 55,
        120            => 65,
        130            => 75,
        140            => 85,
        150            => 95,
        155            => 100,
        _              => (d0 / 155.0) * 100
    };

    // 센서 상태 - 입력 전체
    [ObservableProperty] private bool _x00SupplyFwd  = false;
    [ObservableProperty] private bool _x01SupplyBwd  = false;
    [ObservableProperty] private bool _x02DistribFwd = false;
    [ObservableProperty] private bool _x03DistribBwd = false;
    [ObservableProperty] private bool _x04MachFwd    = false;
    [ObservableProperty] private bool _x05MachBwd    = false;
    [ObservableProperty] private bool _x06EjectFwd   = false;
    [ObservableProperty] private bool _x07EjectBwd   = false;
    [ObservableProperty] private bool _x08StopFwd    = false;
    [ObservableProperty] private bool _x09StopBwd    = false;
    [ObservableProperty] private bool _x0ASuctionFwd = false;
    [ObservableProperty] private bool _x0BSuctionBwd = false;
    [ObservableProperty] private bool _x0CStoreFwd   = false;
    [ObservableProperty] private bool _x0DStoreBwd   = false;
    [ObservableProperty] private bool _x0EVacuum     = false;
    [ObservableProperty] private bool _x0FSupplyMag  = false;
    [ObservableProperty] private bool _x10DistribMag = false;
    [ObservableProperty] private bool _x11Capacitive = false;
    [ObservableProperty] private bool _x12Inductive  = false;
    [ObservableProperty] private bool _x13StopperFiber = false;

    // 출력 신호
    [ObservableProperty] private bool _y20Motor       = false;
    [ObservableProperty] private bool _y21Conveyor    = false;
    [ObservableProperty] private bool _y22SupplyCylFwd  = false;
    [ObservableProperty] private bool _y23SupplyCylBwd  = false;
    [ObservableProperty] private bool _y24DistribCylFwd = false;
    [ObservableProperty] private bool _y25DistribCylBwd = false;
    [ObservableProperty] private bool _y26MachCylDown   = false;
    [ObservableProperty] private bool _y27EjectCylFwd   = false;
    [ObservableProperty] private bool _y28StopperDown   = false;
    [ObservableProperty] private bool _y29StopperUp     = false;
    [ObservableProperty] private bool _y2ASuctionCylFwd = false;
    [ObservableProperty] private bool _y2BSuctionCylBwd = false;
    [ObservableProperty] private bool _y2CStoreCylFwd   = false;
    [ObservableProperty] private bool _y2DStoreCylBwd   = false;
    [ObservableProperty] private bool _y2ESuctionOn     = false;

    // 시퀀스 스텝 컬렉션
    public ObservableCollection<SequenceStepItem> SequenceSteps { get; } = new()
    {
        new() { Name = "정지"     },
        new() { Name = "리셋중"   },
        new() { Name = "대기"     },
        new() { Name = "시작"     },
        new() { Name = "공급전진" },
        new() { Name = "공급후진" },
        new() { Name = "가공/분배"},
        new() { Name = "소재판별" },
        new() { Name = "배출전진" },
        new() { Name = "배출후진" },
        new() { Name = "창고적재" },
        new() { Name = "대기복귀" },
        new() { Name = "완료"     },
    };

    public void UpdatePlcData(PlcData data)
    {
        MetalQty       = data.D11_Metal;
        NonMetalQty    = data.D10_NonMetal;
        TotalQty       = data.TotalQty;
        ServoPos       = data.D2000_ServoPos;
        ServoStatus    = data.D2005_ServoStatus == 1 ? "정상" : "이상";
        IsRunning      = data.CurrentMode == Core.Enums.RunMode.Auto;
        SequenceStep    = data.D0_Mode;
        SequenceName    = GetSequenceName(data.D0_Mode);
        SequencePercent = GetSequencePercent(data.D0_Mode);

        // 입력 신호 전체
        X00SupplyFwd   = data.X00_SupplyFwd;
        X01SupplyBwd   = data.X01_SupplyBwd;
        X02DistribFwd  = data.X02_DistribFwd;
        X03DistribBwd  = data.X03_DistribBwd;
        X04MachFwd     = data.X04_MachFwd;
        X05MachBwd     = data.X05_MachBwd;
        X06EjectFwd    = data.X06_EjectFwd;
        X07EjectBwd    = data.X07_EjectBwd;
        X08StopFwd     = data.X08_StopFwd;
        X09StopBwd     = data.X09_StopBwd;
        X0ASuctionFwd  = data.X0A_SuctionFwd;
        X0BSuctionBwd  = data.X0B_SuctionBwd;
        X0CStoreFwd    = data.X0C_StoreFwd;
        X0DStoreBwd    = data.X0D_StoreBwd;
        X0EVacuum      = data.X0E_Vacuum;
        X0FSupplyMag   = data.X0F_SupplyMag;
        X10DistribMag  = data.X10_DistribMag;
        X11Capacitive  = data.X11_Capacitive;
        X12Inductive   = data.X12_Inductive;
        X13StopperFiber = data.X13_StopperFiber;

        // 출력 신호 전체
        Y20Motor        = data.Y20_ProcessMotor;
        Y21Conveyor     = data.Y21_Conveyor;
        Y22SupplyCylFwd  = data.Y22_SupplyCylFwd;
        Y23SupplyCylBwd  = data.Y23_SupplyCylBwd;
        Y24DistribCylFwd = data.Y24_DistribCylFwd;
        Y25DistribCylBwd = data.Y25_DistribCylBwd;
        Y26MachCylDown   = data.Y26_MachCylDown;
        Y27EjectCylFwd   = data.Y27_EjectCylFwd;
        Y28StopperDown   = data.Y28_StopperDown;
        Y29StopperUp     = data.Y29_StopperUp;
        Y2ASuctionCylFwd = data.Y2A_SuctionCylFwd;
        Y2BSuctionCylBwd = data.Y2B_SuctionCylBwd;
        Y2CStoreCylFwd   = data.Y2C_StoreCylFwd;
        Y2DStoreCylBwd   = data.Y2D_StoreCylBwd;
        Y2ESuctionOn     = data.Y2E_SuctionOn;

        foreach (var step in SequenceSteps)
            step.IsActive = step.Name == SequenceName;
    }
}

// =============================================
// 비전검사 ViewModel
// =============================================
public partial class VisionInspectionViewModel : ObservableObject
{
    private readonly IVisionService    _vision;
    private readonly IVisionRepository _visionRepo;
    private readonly IPlcService        _plc;
    private readonly string? _defaultModelPath;
    private readonly int _defaultCameraIndex;
    private int? _previousMetalQty;
    private int? _previousNonMetalQty;
    private bool _pendingMaterialCorrection;

    [ObservableProperty] private BitmapSource? _currentFrame;
    [ObservableProperty] private string        _visionResult    = "대기중";
    [ObservableProperty] private string?       _defectType;
    [ObservableProperty] private double        _confidence      = 0.0;
    [ObservableProperty] private int           _okCount         = 0;
    [ObservableProperty] private int           _ngCount         = 0;
    [ObservableProperty] private bool          _isInspecting    = false;
    [ObservableProperty] private bool          _isCameraRunning = false;
    [ObservableProperty] private bool          _isModelLoaded   = false;
    [ObservableProperty] private string        _modelStatusText = "모델 미로드";
    [ObservableProperty] private string        _selectedModelPath = "선택된 모델 없음";
    [ObservableProperty] private string        _currentMaterialType = "D10/D11 증가 대기";
    [ObservableProperty] private string        _lastInspectionMaterialType = "-";
    [ObservableProperty] private int           _currentMetalQty = 0;
    [ObservableProperty] private int           _currentNonMetalQty = 0;
    [ObservableProperty] private int           _selectedCameraIndex = 0;
    [ObservableProperty] private string        _selectedCameraName  = "카메라 선택";

    [ObservableProperty] private bool _hsvEnabled = true;
    [ObservableProperty] private bool _cannyEnabled = true;
    [ObservableProperty] private bool _applyFiltersToInspection = false;
    [ObservableProperty] private int _hueMin = 0;
    [ObservableProperty] private int _hueMax = 179;
    [ObservableProperty] private int _saturationMin = 0;
    [ObservableProperty] private int _saturationMax = 255;
    [ObservableProperty] private int _valueMin = 0;
    [ObservableProperty] private int _valueMax = 255;
    [ObservableProperty] private int _cannyLow = 50;
    [ObservableProperty] private int _cannyHigh = 150;

    partial void OnHsvEnabledChanged(bool value) => UpdateCameraFilters();
    partial void OnCannyEnabledChanged(bool value) => UpdateCameraFilters();
    partial void OnApplyFiltersToInspectionChanged(bool value) => UpdateCameraFilters();
    partial void OnHueMinChanged(int value) => UpdateCameraFilters();
    partial void OnHueMaxChanged(int value) => UpdateCameraFilters();
    partial void OnSaturationMinChanged(int value) => UpdateCameraFilters();
    partial void OnSaturationMaxChanged(int value) => UpdateCameraFilters();
    partial void OnValueMinChanged(int value) => UpdateCameraFilters();
    partial void OnValueMaxChanged(int value) => UpdateCameraFilters();
    partial void OnCannyLowChanged(int value) => UpdateCameraFilters();
    partial void OnCannyHighChanged(int value) => UpdateCameraFilters();

    private void UpdateCameraFilters() => _vision.SetCameraFilters(new CameraFilterSettings
    {
        HsvEnabled = HsvEnabled,
        CannyEnabled = CannyEnabled,
        ApplyToInspection = ApplyFiltersToInspection,
        HueMin = HueMin,
        HueMax = HueMax,
        SaturationMin = SaturationMin,
        SaturationMax = SaturationMax,
        ValueMin = ValueMin,
        ValueMax = ValueMax,
        CannyLow = CannyLow,
        CannyHigh = CannyHigh,
    });

    // 연결된 카메라 목록
    public ObservableCollection<CameraDevice>  AvailableCameras { get; } = new();
    public ObservableCollection<VisionInspection> RecentResults { get; } = new();

    public VisionInspectionViewModel(
        IVisionService vision,
        IVisionRepository visionRepo,
        IPlcService plc,
        IConfiguration config)
    {
        _vision     = vision;
        _visionRepo = visionRepo;
        _plc        = plc;
        _defaultModelPath = PathHelper.ResolveFile(config["Vision:ModelPath"]);
        _defaultCameraIndex = int.TryParse(config["Vision:CameraIndex"], out var cameraIndex)
            ? cameraIndex
            : 0;

        _vision.FrameCaptured       += OnFrameCaptured;
        _vision.InspectionCompleted += OnInspectionCompleted;
        _plc.DataRefreshed           += OnPlcDataRefreshed;

        // 카메라 목록 초기 로드
        LoadCameraList();
    }

    // 카메라 목록 스캔 (OpenCV DirectShow)
    [RelayCommand]
    private void ScanCameras()
    {
        LoadCameraList();
    }

    private void LoadCameraList()
    {
        AvailableCameras.Clear();
        // DirectShow로 연결 가능한 카메라 스캔 (최대 10개)
        for (int i = 0; i < 10; i++)
        {
            try
            {
                using var cap = new OpenCvSharp.VideoCapture(i, OpenCvSharp.VideoCaptureAPIs.DSHOW);
                if (cap.IsOpened())
                {
                    AvailableCameras.Add(new CameraDevice
                    {
                        Index = i,
                        Name  = $"카메라 {i} (장치 #{i})"
                    });
                    cap.Release();
                }
                else break;
            }
            catch { break; }
        }

        if (AvailableCameras.Count == 0)
            AvailableCameras.Add(new CameraDevice { Index = 0, Name = "카메라 0 (기본)" });

        // 첫 번째 카메라 자동 선택
        var selected = AvailableCameras.FirstOrDefault(c => c.Index == _defaultCameraIndex)
                       ?? AvailableCameras.First();
        SelectedCameraIndex = selected.Index;
        SelectedCameraName  = selected.Name;
    }

    [RelayCommand]
    private void SelectCamera(CameraDevice cam)
    {
        if (IsCameraRunning)
        {
            System.Windows.MessageBox.Show("카메라 실행 중에는 변경할 수 없습니다.\n먼저 카메라를 해제해주세요.", "알림");
            return;
        }
        SelectedCameraIndex = cam.Index;
        SelectedCameraName  = cam.Name;
    }

    [RelayCommand]
    private async Task LoadModelAsync()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "PyTorch 모델 파일 선택",
            Filter = "PyTorch 모델 (*.pt;*.pth)|*.pt;*.pth|모든 파일 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(_defaultModelPath))
        {
            var initialDirectory = System.IO.Path.GetDirectoryName(_defaultModelPath);
            if (!string.IsNullOrWhiteSpace(initialDirectory) && System.IO.Directory.Exists(initialDirectory))
                dlg.InitialDirectory = initialDirectory;
        }

        if (dlg.ShowDialog() != true) return;

        SelectedModelPath = dlg.FileName;
        IsModelLoaded = false;
        ModelStatusText = "로드 중...";
        try
        {
            var ok = await _vision.LoadModelAsync(dlg.FileName);
            IsModelLoaded   = ok;
            ModelStatusText = ok
                ? $"로드됨: {System.IO.Path.GetFileName(dlg.FileName)}"
                : "로드 실패";
        }
        catch (Exception ex)
        {
            IsModelLoaded   = false;
            ModelStatusText = "로드 실패";
            System.Windows.MessageBox.Show(
                $"모델 로드 실패:\n{ex.Message}",
                "오류", System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ToggleCameraAsync()
    {
        if (IsCameraRunning)
        {
            _vision.StopPreview();
            await _vision.DisconnectCameraAsync();
            IsCameraRunning = false;
        }
        else
        {
            var ok = await _vision.ConnectCameraAsync(SelectedCameraIndex);
            if (ok)
            {
                _vision.StartPreview();
                IsCameraRunning = true;
            }
            else
            {
                System.Windows.MessageBox.Show(
                    $"카메라 {SelectedCameraIndex}번 연결 실패\n장치가 연결되어 있는지 확인하세요.", "카메라 오류");
            }
        }
    }

    [RelayCommand]
    private async Task InspectAsync()
    {
        if (!_vision.IsCameraConnected) return;
        IsInspecting = true;
        try
        {
            await _vision.InspectAsync();
        }
        finally { IsInspecting = false; }
    }

    [RelayCommand]
    private async Task LoadRecentAsync()
    {
        var list = await _visionRepo.GetRecentAsync(50);
        RecentResults.Clear();
        foreach (var item in list) RecentResults.Add(item);
    }

    private void OnFrameCaptured(VisionFrame frame)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                using var ms = new System.IO.MemoryStream(frame.ImageData);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption  = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze(); // UI 스레드 안전
                CurrentFrame = bmp;
            }
            catch { /* 프레임 오류 무시 */ }
        });
    }

    private void OnInspectionCompleted(VisionResult result)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            VisionResult = result.IsOk ? "OK" : "NG";
            DefectType   = result.DefectType;
            Confidence   = result.Confidence;
            LastInspectionMaterialType = "판정 대기 (D10/D11)";
            _pendingMaterialCorrection = true;
            if (result.IsOk) OkCount++; else NgCount++;

            // 실시간 검사 목록 상단에 추가 (최대 50건)
            RecentResults.Insert(0, new VisionInspection
            {
                InspectedAt = result.InspectedAt,
                Result      = result.IsOk ? "OK" : "NG",
                DefectType  = result.DefectType,
                Confidence  = (decimal)result.Confidence,
                IsMaterialConfirmed = false,
            });
            while (RecentResults.Count > 50)
                RecentResults.RemoveAt(RecentResults.Count - 1);

            // 캡처 이미지 업데이트
            if (result.ImageData?.Length > 0)
            {
                try
                {
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.StreamSource  = new System.IO.MemoryStream(result.ImageData);
                    bmp.CacheOption   = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    CurrentFrame = bmp;
                }
                catch { }
            }
        });
    }

    private void OnPlcDataRefreshed(PlcData data)
    {
        int metalDelta = _previousMetalQty.HasValue
            ? data.D11_Metal - _previousMetalQty.Value
            : 0;
        int nonMetalDelta = _previousNonMetalQty.HasValue
            ? data.D10_NonMetal - _previousNonMetalQty.Value
            : 0;
        bool countersIncreased = metalDelta > 0 || nonMetalDelta > 0;
        bool? completedIsMetal = ResolveMaterialFromCounterDelta(metalDelta, nonMetalDelta);

        _previousMetalQty = data.D11_Metal;
        _previousNonMetalQty = data.D10_NonMetal;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (completedIsMetal.HasValue)
            {
                CurrentMaterialType = completedIsMetal.Value ? "금속" : "비금속";
            }
            else if (countersIncreased)
            {
                CurrentMaterialType = "판정 불가 (D10/D11 동시 증가)";
            }

            if (_pendingMaterialCorrection && countersIncreased)
            {
                if (completedIsMetal.HasValue)
                {
                    LastInspectionMaterialType = completedIsMetal.Value ? "금속" : "비금속";
                    if (RecentResults.Count > 0)
                        RecentResults[0] = RecentResults[0] with
                        {
                            IsMetalYn = completedIsMetal.Value,
                            IsMaterialConfirmed = true
                        };
                }
                else
                {
                    LastInspectionMaterialType = "판정 불가";
                }
                _pendingMaterialCorrection = false;
            }
            CurrentMetalQty = data.D11_Metal;
            CurrentNonMetalQty = data.D10_NonMetal;
        });
    }

    private static bool? ResolveMaterialFromCounterDelta(int metalDelta, int nonMetalDelta)
    {
        if (metalDelta > 0 && nonMetalDelta <= 0) return true;
        if (nonMetalDelta > 0 && metalDelta <= 0) return false;
        return null;
    }
}

// =============================================
// 수동조작 ViewModel
// =============================================
public partial class ManualControlViewModel : ObservableObject
{
    private readonly IPlcService _plc;

    // PLC 연결 상태 직접 참조
    [ObservableProperty] private bool _isConnected = false;

    // 각 M비트 로컬 상태 (읽기 없이 로컬에서 토글)
    private readonly Dictionary<string, bool> _coilStates = new();

    public ManualControlViewModel(IPlcService plc)
    {
        _plc = plc;
        // 초기 상태 반영
        IsConnected = _plc.ConnectionStatus == Core.Enums.PlcConnectionStatus.Connected;
        _plc.ConnectionStatusChanged += s =>
        {
            IsConnected = s == Core.Enums.PlcConnectionStatus.Connected;
            if (!IsConnected) _coilStates.Clear();
        };
    }

    // 펄스 신호 (ON → 200ms → OFF) - 중복 클릭 방지 포함
    [RelayCommand]
    private async Task ToggleCoilAsync(string address)
    {
        if (!IsConnected)
        {
            System.Windows.MessageBox.Show("PLC가 연결되지 않았습니다.", "알림");
            return;
        }

        // 해당 주소 처리 중이면 무시
        if (_coilStates.TryGetValue(address + "_busy", out bool busy) && busy)
            return;

        _coilStates[address + "_busy"] = true;
        try
        {
            await _plc.WriteBitAsync(address, true);
            await Task.Delay(200);
            await _plc.WriteBitAsync(address, false);
        }
        finally
        {
            _coilStates[address + "_busy"] = false;
        }
    }

    // 컨베이어 ON/OFF (M116 → Y21)
    [RelayCommand]
    private async Task ToggleConveyorAsync()
    {
        await ToggleCoilAsync("M116");
    }
}

// =============================================
// 품질분석 ViewModel
// =============================================
public partial class QualityAnalysisViewModel : ObservableObject
{
    private readonly IVisionRepository     _visionRepo;
    private readonly IProductionRepository _prodRepo;
    private System.Timers.Timer?           _autoTimer;

    [ObservableProperty] private int    _totalCount  = 0;
    [ObservableProperty] private int    _okCount     = 0;
    [ObservableProperty] private int    _ngCount     = 0;
    [ObservableProperty] private double _ngRate      = 0.0;
    [ObservableProperty] private double _okRate      = 0.0;
    [ObservableProperty] private double _avgConf     = 0.0;
    [ObservableProperty] private string _peakNgHour  = "-";
    [ObservableProperty] private string _peakOkHour  = "-";
    [ObservableProperty] private string _lastRefresh = "-";

    // 2클래스 파손별 카운트
    [ObservableProperty] private int _metalDamageCount    = 0;
    [ObservableProperty] private int _nonMetalDamageCount = 0;
    [ObservableProperty] private int _metalCount          = 0;
    [ObservableProperty] private int _nonMetalCount       = 0;

    // StrokeDashArray 도넛차트 — Ellipse W=180 T=30, C_units=18.85
    [ObservableProperty] private DoubleCollection _okDashArray  = new();
    [ObservableProperty] private DoubleCollection _ngDashArray  = new();
    [ObservableProperty] private double            _ngDashOffset = 0;

    public ObservableCollection<HourlyInspection> HourlyData  { get; } = new();
    public ObservableCollection<DefectRatio>      DefectRatios { get; } = new();

    public QualityAnalysisViewModel(IVisionRepository visionRepo, IProductionRepository prodRepo)
    {
        _visionRepo = visionRepo;
        _prodRepo   = prodRepo;
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _autoTimer = new System.Timers.Timer(2000);
        _autoTimer.Elapsed += async (s, e) => await SafeRefreshAsync();
        _autoTimer.Start();
    }

    private async Task SafeRefreshAsync()
    {
        try { await RefreshAsync(); } catch { }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var summary = await _visionRepo.GetTodaySummaryAsync();
        TotalCount         = summary.TotalCount;
        OkCount            = summary.OkCount;
        NgCount            = summary.NgCount;
        NgRate             = summary.NgRate;
        OkRate             = TotalCount > 0 ? (double)OkCount / TotalCount * 100.0 : 0;
        AvgConf            = summary.AvgConf;
        MetalCount         = summary.MetalCount;
        NonMetalCount      = summary.NonMetalCount;
        MetalDamageCount   = summary.MetalDamageCount;
        NonMetalDamageCount = summary.NonMetalDamageCount;
        LastRefresh        = DateTime.Now.ToString("HH:mm:ss");

        // StrokeDashArray 도넛차트 계산 (circumference units = 18.8496)
        const double C = 18.8496;
        if (TotalCount > 0)
        {
            double okFrac  = (double)OkCount / TotalCount;
            double ngFrac  = (double)NgCount / TotalCount;
            double okDash  = Math.Max(0.001, okFrac * C);
            double ngDash  = Math.Max(0.001, ngFrac * C);
            // Freeze() 필수 — 타이머 백그라운드 스레드에서 생성한 DoubleCollection은
            // UI 스레드 바인딩 연결 전 반드시 Freeze해야 크로스스레드 예외 방지
            var okDC = new DoubleCollection(new[] { okDash, ngDash }); okDC.Freeze();
            var ngDC = new DoubleCollection(new[] { ngDash, okDash }); ngDC.Freeze();
            OkDashArray  = okDC;
            NgDashArray  = ngDC;
            NgDashOffset = -okDash;
        }
        else
        {
            var okZ = new DoubleCollection(new[] { 0.0, C }); okZ.Freeze();
            var ngZ = new DoubleCollection(new[] { 0.0, C }); ngZ.Freeze();
            OkDashArray  = okZ;
            NgDashArray  = ngZ;
            NgDashOffset = 0;
        }

        var hourly = await _visionRepo.GetHourlyTodayAsync();
        var hourlyDict = hourly.ToDictionary(h => h.Hour);

        // 0~23시 전체 채우기 (데이터 없는 시간 = 0)
        var allHours = Enumerable.Range(0, 24)
            .Select(h => hourlyDict.TryGetValue(h, out var v) ? v
                       : new HourlyInspection { Hour = h, OkCount = 0, NgCount = 0 })
            .ToList();

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            HourlyData.Clear();
            foreach (var h in allHours) HourlyData.Add(h);
        });

        var peak = HourlyData.OrderByDescending(h => h.NgCount).FirstOrDefault();
        var best = HourlyData.OrderByDescending(h => h.OkCount).FirstOrDefault();
        PeakNgHour = peak != null ? $"{peak.Hour}시 ({(peak.Total > 0 ? (int)((double)peak.NgCount/peak.Total*100) : 0)}%)" : "-";
        PeakOkHour = best != null ? $"{best.Hour}시 ({(best.Total > 0 ? (int)((double)best.OkCount/best.Total*100) : 0)}% OK)" : "-";

        var ratios = await _visionRepo.GetDefectRatioAsync(DateTime.Today);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            DefectRatios.Clear();
            foreach (var r in ratios) DefectRatios.Add(r);
        });
    }
}

// =============================================
// DB 조회 ViewModel
// =============================================
public partial class DbSearchViewModel : ObservableObject
{
    private readonly IProductionRepository _prodRepo;
    private readonly IVisionRepository     _visionRepo;
    private readonly IEventLogRepository   _eventRepo;

    [ObservableProperty] private DateTime?    _searchFrom;
    [ObservableProperty] private DateTime?    _searchTo;
    [ObservableProperty] private string?      _searchResult;
    [ObservableProperty] private string?      _searchDefect;
    [ObservableProperty] private int          _selectedTab = 0;

    public ObservableCollection<ProductionRecord> ProdResults    { get; } = new();
    public ObservableCollection<VisionInspection> VisionResults  { get; } = new();
    public ObservableCollection<EventLog>         EventResults   { get; } = new();

    public DbSearchViewModel(
        IProductionRepository prodRepo,
        IVisionRepository visionRepo,
        IEventLogRepository eventRepo)
    {
        _prodRepo   = prodRepo;
        _visionRepo = visionRepo;
        _eventRepo  = eventRepo;
    }

    [RelayCommand]
    private async Task SearchProductionAsync()
    {
        var list = await _prodRepo.SearchAsync(SearchFrom, SearchTo, SearchResult, SearchDefect);
        ProdResults.Clear();
        foreach (var item in list) ProdResults.Add(item);
    }

    [RelayCommand]
    private async Task SearchVisionAsync()
    {
        var list = await _visionRepo.SearchAsync(SearchFrom, SearchTo, SearchResult, SearchDefect);
        VisionResults.Clear();
        foreach (var item in list) VisionResults.Add(item);
    }

    [RelayCommand]
    private async Task SearchEventAsync()
    {
        var list = await _eventRepo.SearchAsync(SearchFrom, SearchTo);
        EventResults.Clear();
        foreach (var item in list) EventResults.Add(item);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SearchFrom   = null;
        SearchTo     = null;
        SearchResult = null;
        SearchDefect = null;
    }
}

// =============================================
// 생산 이력 ViewModel
// =============================================
public partial class ProductionHistoryViewModel : ObservableObject
{
    private readonly IProductionRepository _repo;
    private System.Timers.Timer?           _autoTimer;

    public ObservableCollection<ProductionRecord> Records { get; } = new();

    public ProductionHistoryViewModel(IProductionRepository repo)
    {
        _repo = repo;
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _autoTimer = new System.Timers.Timer(2000);
        _autoTimer.Elapsed += async (s, e) => { try { await LoadAsync(); } catch { } };
        _autoTimer.Start();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var list = await _repo.GetRecentAsync(100);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Records.Clear();
            foreach (var item in list) Records.Add(item);
        });
    }
}

// =============================================
// 이벤트 로그 ViewModel
// =============================================
public partial class EventLogViewModel : ObservableObject
{
    private readonly IEventLogRepository _repo;
    private System.Timers.Timer?         _autoTimer;

    public ObservableCollection<EventLog> Logs { get; } = new();

    public EventLogViewModel(IEventLogRepository repo)
    {
        _repo = repo;
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _autoTimer = new System.Timers.Timer(2000);
        _autoTimer.Elapsed += async (s, e) => { try { await LoadAsync(); } catch { } };
        _autoTimer.Start();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        var list = await _repo.GetRecentAsync(200);
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            Logs.Clear();
            foreach (var item in list) Logs.Add(item);
        });
    }

    public async Task AddLogAsync(string type, string message, string? source = null)
    {
        var log = new EventLog
        {
            EventAt   = DateTime.Now,
            EventType = type,
            Message   = message,
            Source    = source
        };
        await _repo.InsertAsync(log);
        System.Windows.Application.Current.Dispatcher.Invoke(()
            => Logs.Insert(0, log));
    }
}
