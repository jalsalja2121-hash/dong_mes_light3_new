using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MpsMes.Core.Enums;
using MpsMes.Core.Interfaces;

namespace MpsMes.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IPlcService    _plc;
    private readonly IVisionService _vision;
    private readonly string _plcIpAddress;
    private readonly int _plcPort;

    // ── 현재 시각 ──────────────────────────────────────
    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ── PLC 연결 상태 ──────────────────────────────────
    [ObservableProperty] private PlcConnectionStatus _plcStatus = PlcConnectionStatus.Disconnected;
    [ObservableProperty] private string _plcConnectLabel = "PLC 연결 / 해제";

    // ── 운전 모드 ──────────────────────────────────────
    [ObservableProperty] private bool _isStop    = true;
    [ObservableProperty] private bool _isReset   = false;
    [ObservableProperty] private bool _isAuto    = false;
    [ObservableProperty] private int  _modeD0    = 0;
    [ObservableProperty] private int  _signalD1  = 0;

    // ── 운전 제어 ──────────────────────────────────────
    [ObservableProperty] private string _control1 = string.Empty;
    [ObservableProperty] private string _control2 = string.Empty;
    [ObservableProperty] private string _control3 = string.Empty;

    // ── 생산 수량 ──────────────────────────────────────
    [ObservableProperty] private int _metalQty    = 0;   // D11
    [ObservableProperty] private int _nonMetalQty = 0;   // D10
    [ObservableProperty] private int _totalQty    = 0;

    // ── 서보모터 ──────────────────────────────────────
    [ObservableProperty] private int    _servoPos    = 0;   // D2000
    [ObservableProperty] private string _servoStatus = "정상";

    // ── 비전 검사 ─────────────────────────────────────
    [ObservableProperty] private bool   _isCameraConnected = false;

    // 카메라 상태를 주기적으로 동기화 (폴링)
    private void StartCameraStatusSync()
    {
        var timer = new System.Timers.Timer(500);
        timer.Elapsed += (s, e) =>
        {
            var connected = _vision.IsCameraConnected;
            if (IsCameraConnected != connected)
                System.Windows.Application.Current?.Dispatcher.Invoke(
                    () => IsCameraConnected = connected);
        };
        timer.Start();
    }
    [ObservableProperty] private string _visionResult      = "대기중";
    [ObservableProperty] private int    _visionOkCount     = 0;
    [ObservableProperty] private int    _visionNgCount     = 0;

    // ── 탭 인덱스 ─────────────────────────────────────
    [ObservableProperty] private int _selectedTabIndex = 0;

    // ── 서브 ViewModel ────────────────────────────────
    public MonitorViewModel           MonitorVm             { get; }
    public VisionInspectionViewModel  VisionInspectionVm    { get; }
    public ManualControlViewModel     ManualControlVm       { get; }
    public QualityAnalysisViewModel   QualityAnalysisVm     { get; }
    public ProductionStatusViewModel  ProductionStatusVm    { get; }
    public DbSearchViewModel          DbSearchVm            { get; }
    public ProductionHistoryViewModel ProductionHistoryVm   { get; }
    public EventLogViewModel          EventLogVm            { get; }

    public MainViewModel(
        IPlcService plc, IVisionService vision, IConfiguration config,
        MonitorViewModel monitorVm,
        VisionInspectionViewModel visionVm,
        ManualControlViewModel manualVm,
        QualityAnalysisViewModel qualityVm,
        ProductionStatusViewModel productionStatusVm,
        DbSearchViewModel dbSearchVm,
        ProductionHistoryViewModel prodHistVm,
        EventLogViewModel eventLogVm)
    {
        _plc    = plc;
        _vision = vision;
        _plcIpAddress = config["Plc:IpAddress"] ?? "192.168.3.39";
        _plcPort = int.TryParse(config["Plc:Port"], out var plcPort) ? plcPort : 5007;

        MonitorVm            = monitorVm;
        VisionInspectionVm   = visionVm;
        ManualControlVm      = manualVm;
        QualityAnalysisVm    = qualityVm;
        ProductionStatusVm   = productionStatusVm;
        DbSearchVm           = dbSearchVm;
        ProductionHistoryVm  = prodHistVm;
        EventLogVm           = eventLogVm;

        // PLC 이벤트
        _plc.ConnectionStatusChanged += HandlePlcStatusChanged;
        _plc.DataRefreshed           += OnPlcDataRefreshed;

        // 비전 이벤트
        _vision.InspectionCompleted += OnInspectionCompleted;

        // 카메라 상태 동기화 (VisionInspectionVm → MainViewModel)
        VisionInspectionVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(VisionInspectionViewModel.IsCameraRunning))
                IsCameraConnected = VisionInspectionVm.IsCameraRunning;
        };

        // 시계 타이머
        var timer = new System.Timers.Timer(1000);
        timer.Elapsed += (s, e) => CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        timer.Start();

        // 카메라 상태 동기화 타이머
        StartCameraStatusSync();
    }

    // ── PLC 연결 / 해제 ────────────────────────────────
    [RelayCommand]
    private async Task TogglePlcConnectionAsync()
    {
        if (PlcStatus == PlcConnectionStatus.Connected)
        {
            await _plc.DisconnectAsync();
        }
        else
        {
            await _plc.ConnectAsync(_plcIpAddress, _plcPort);
        }
    }

    // ── 서보 초기화 (M2005 상승펄스) ───────────────────
    [RelayCommand]
    private async Task ServoInitAsync()
    {
        await _plc.WriteBitAsync("M2005", true);
        await Task.Delay(200);
        await _plc.WriteBitAsync("M2005", false);
    }

    private bool _pulseBusy = false;

    // ── L0 정지 (L0 상승펄스) ──────────────────────────
    [RelayCommand]
    private async Task StopAsync()
    {
        if (_pulseBusy) return;
        _pulseBusy = true;
        try
        {
            await _plc.WriteBitAsync("L0", true);
            await Task.Delay(200);
            await _plc.WriteBitAsync("L0", false);
        }
        finally { _pulseBusy = false; }
    }

    // ── L1 리셋 (L1 상승펄스) ──────────────────────────
    [RelayCommand]
    private async Task ResetAsync()
    {
        if (_pulseBusy) return;
        _pulseBusy = true;
        try
        {
            await _plc.WriteBitAsync("L1", true);
            await Task.Delay(200);
            await _plc.WriteBitAsync("L1", false);
        }
        finally { _pulseBusy = false; }
    }

    // ── L2 스타트 (L2 상승펄스) ────────────────────────
    [RelayCommand]
    private async Task StartAsync()
    {
        if (_pulseBusy) return;
        _pulseBusy = true;
        try
        {
            await _plc.WriteBitAsync("L2", true);
            await Task.Delay(200);
            await _plc.WriteBitAsync("L2", false);
        }
        finally { _pulseBusy = false; }
    }

    // ── 카메라 연결 / 해제 ─────────────────────────────
    [RelayCommand]
    private async Task ToggleCameraAsync()
    {
        if (IsCameraConnected)
            await _vision.DisconnectCameraAsync();
        else
            await _vision.ConnectCameraAsync(0);

        IsCameraConnected = _vision.IsCameraConnected;
    }

    // ── PLC 이벤트 핸들러 ─────────────────────────────
    private void HandlePlcStatusChanged(PlcConnectionStatus status)
    {
        PlcStatus = status;
        PlcConnectLabel = status == PlcConnectionStatus.Connected
            ? "PLC 해제" : "PLC 연결 / 해제";
    }

    private void OnPlcDataRefreshed(PlcData data)
    {
        MetalQty    = data.D11_Metal;
        NonMetalQty = data.D10_NonMetal;
        TotalQty    = data.TotalQty;
        ServoPos    = data.D2000_ServoPos;
        ServoStatus = data.D2005_ServoStatus == 1 ? "정상" : "이상";
        ModeD0      = data.D0_Mode;
        SignalD1    = data.D1_Signal;

        IsStop   = data.D0_Mode == 0;
        IsReset  = data.D0_Mode >= 1 && data.D0_Mode <= 49;
        IsAuto   = data.D0_Mode >= 50;

        // 하위 VM에도 전달
        MonitorVm.UpdatePlcData(data);
    }

    private void OnInspectionCompleted(VisionResult result)
    {
        VisionResult = result.IsOk ? "OK" : "NG";
        if (result.IsOk) VisionOkCount++;
        else             VisionNgCount++;
    }
}
