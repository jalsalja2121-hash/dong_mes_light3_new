using MpsMes.Core.Enums;

namespace MpsMes.Core.Interfaces;

// =============================================
// PLC 서비스 인터페이스
// =============================================
public interface IPlcService : IDisposable
{
    PlcConnectionStatus ConnectionStatus { get; }
    event Action<PlcConnectionStatus>? ConnectionStatusChanged;
    event Action<PlcData>?             DataRefreshed;

    Task<bool> ConnectAsync(string ipAddress, int port);
    Task        DisconnectAsync();

    // 읽기
    Task<int>   ReadWordAsync(string address);
    Task<int[]> ReadWordsAsync(string startAddress, int count);
    Task<bool>  ReadBitAsync(string address);

    // 쓰기
    Task<bool>  WriteWordAsync(string address, int value);
    Task<bool>  WriteBitAsync(string address, bool value);

    // 일괄 읽기 (폴링)
    Task<PlcData> ReadAllAsync();
}

// =============================================
// 비전 서비스 인터페이스
// =============================================
public interface IVisionService : IDisposable
{
    bool IsCameraConnected { get; }
    bool IsModelLoaded     { get; }

    event Action<VisionFrame>?  FrameCaptured;
    event Action<VisionResult>? InspectionCompleted;

    Task<bool> ConnectCameraAsync(int deviceIndex = 0);
    Task        DisconnectCameraAsync();
    Task<bool> LoadModelAsync(string modelPath);

    void StartPreview();
    void StopPreview();
    void SetCameraFilters(CameraFilterSettings settings);
    Task<VisionResult> InspectAsync();
    Task<VisionResult> InspectWithCaptureAsync(); // 딜레이 없이 즉시 캡처+검사
}

// =============================================
// 생산이력 Repository 인터페이스
// =============================================
public interface IProductionRepository
{
    Task<long>                      InsertAsync(ProductionRecord record);
    Task<IEnumerable<ProductionRecord>> GetRecentAsync(int count = 100);
    Task<IEnumerable<ProductionRecord>> SearchAsync(
        DateTime? from, DateTime? to,
        string? visionResult = null,
        string? defectType   = null);
    Task<ProductionSummary>         GetTodaySummaryAsync();
}

// =============================================
// 비전검사 Repository 인터페이스
// =============================================
public interface IVisionRepository
{
    Task<long>                           InsertAsync(VisionInspection record);
    Task<IEnumerable<VisionInspection>>  GetRecentAsync(int count = 100);
    Task<IEnumerable<VisionInspection>>  SearchAsync(
        DateTime? from, DateTime? to,
        string? result     = null,
        string? defectType = null);
    Task<VisionDailySummary>             GetTodaySummaryAsync();
    Task<IEnumerable<HourlyInspection>>  GetHourlyTodayAsync();
    Task<IEnumerable<DefectRatio>>       GetDefectRatioAsync(DateTime date);
}

// =============================================
// 이벤트 로그 Repository 인터페이스
// =============================================
public interface IEventLogRepository
{
    Task        InsertAsync(EventLog log);
    Task<IEnumerable<EventLog>> GetRecentAsync(int count = 200);
    Task<IEnumerable<EventLog>> SearchAsync(
        DateTime? from, DateTime? to,
        string? eventType = null);
}
