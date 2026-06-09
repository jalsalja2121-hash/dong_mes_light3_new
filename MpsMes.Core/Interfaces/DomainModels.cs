using MpsMes.Core.Enums;

namespace MpsMes.Core.Interfaces;

// =============================================
// PLC 전체 데이터 스냅샷
// =============================================
public record PlcData
{
    public DateTime SnapAt        { get; init; } = DateTime.Now;

    // D 레지스터
    public int  D10_NonMetal      { get; init; }   // 비금속 수량
    public int  D11_Metal         { get; init; }   // 금속 수량
    public int  D2000_ServoPos    { get; init; }   // 서보 위치 (mm)
    public int  D2005_ServoStatus { get; init; }   // 서보 상태
    public int  D0_Mode           { get; init; }   // 운전 모드
    public int  D1_Signal         { get; init; }   // 시퀀스 D1
    public int  T116_TimerVal     { get; init; }   // T116 타이머 현재값 (×0.1s)

    // 입력 비트 (X00~X13)
    public bool X00_SupplyFwd     { get; init; }
    public bool X01_SupplyBwd     { get; init; }
    public bool X02_DistribFwd    { get; init; }
    public bool X03_DistribBwd    { get; init; }
    public bool X04_MachFwd       { get; init; }
    public bool X05_MachBwd       { get; init; }
    public bool X06_EjectFwd      { get; init; }
    public bool X07_EjectBwd      { get; init; }
    public bool X08_StopFwd       { get; init; }
    public bool X09_StopBwd       { get; init; }
    public bool X0A_SuctionFwd    { get; init; }
    public bool X0B_SuctionBwd    { get; init; }
    public bool X0C_StoreFwd      { get; init; }
    public bool X0D_StoreBwd      { get; init; }
    public bool X0E_Vacuum        { get; init; }
    public bool X0F_SupplyMag     { get; init; }
    public bool X10_DistribMag    { get; init; }
    public bool X11_Capacitive    { get; init; }   // 용량형(비금속)
    public bool X12_Inductive     { get; init; }   // 유도형(금속)
    public bool X13_StopperFiber  { get; init; }

    // 출력 비트 (Y20~Y2E)
    public bool Y20_ProcessMotor  { get; init; }
    public bool Y21_Conveyor      { get; init; }
    public bool Y22_SupplyCylFwd  { get; init; }
    public bool Y23_SupplyCylBwd  { get; init; }
    public bool Y24_DistribCylFwd { get; init; }
    public bool Y25_DistribCylBwd { get; init; }
    public bool Y26_MachCylDown   { get; init; }
    public bool Y27_EjectCylFwd   { get; init; }
    public bool Y28_StopperDown   { get; init; }
    public bool Y29_StopperUp     { get; init; }
    public bool Y2A_SuctionCylFwd { get; init; }
    public bool Y2B_SuctionCylBwd { get; init; }
    public bool Y2C_StoreCylFwd   { get; init; }
    public bool Y2D_StoreCylBwd   { get; init; }
    public bool Y2E_SuctionOn     { get; init; }

    public RunMode CurrentMode => (RunMode)D0_Mode;
    public int TotalQty        => D10_NonMetal + D11_Metal;
}

// =============================================
// 생산 이력
// =============================================
public record ProductionRecord
{
    public long     Seq           { get; init; }
    public DateTime CompletedAt   { get; set; }  = DateTime.Now;
    public decimal? CycleTimeSec  { get; set; }
    public int      MetalQty      { get; set; }
    public int      NonMetalQty   { get; set; }
    public int      TotalQty      => MetalQty + NonMetalQty;
    public string?  VisionResult  { get; set; }   // "OK" / "NG"
    public string?  DefectType    { get; set; }
    public decimal? Confidence    { get; set; }
    public string?  ShiftCode     { get; set; }
}

// =============================================
// 비전 검사 결과
// =============================================
public record VisionInspection
{
    public long     Seq          { get; init; }
    public DateTime InspectedAt  { get; set; }  = DateTime.Now;
    public string   Result       { get; set; }  = "NG";   // "OK" / "NG"
    public string?  DefectType   { get; set; }
    public decimal? Confidence   { get; set; }
    public string?  ImagePath    { get; set; }
    public bool     IsMetalYn    { get; set; }
    public long?    ProdSeq      { get; set; }
    public bool     IsMaterialConfirmed { get; set; } = true;
    public string   MaterialType => !IsMaterialConfirmed ? "판정 대기" : IsMetalYn ? "금속" : "비금속";
}

// =============================================
// 이벤트 로그
// =============================================
public record EventLog
{
    public long     Seq        { get; init; }
    public DateTime EventAt    { get; set; }   = DateTime.Now;
    public string   EventType  { get; set; }   = "INFO";
    public string?  EventCode  { get; set; }
    public string   Message    { get; set; }   = string.Empty;
    public string?  Source     { get; set; }
}

// =============================================
// 비전 프레임 (카메라 → UI 실시간)
// =============================================
public record VisionFrame
{
    public byte[]   ImageData   { get; init; } = Array.Empty<byte>();
    public int      Width       { get; init; }
    public int      Height      { get; init; }
    public DateTime CapturedAt  { get; init; } = DateTime.Now;
}

// =============================================
// 비전 검사 결과 (추론 완료)
// =============================================
public record VisionResult
{
    public VisionResultType ResultType  { get; init; }
    public string?          DefectType  { get; init; }
    public double           Confidence  { get; init; }
    public byte[]           ImageData   { get; init; } = Array.Empty<byte>();
    public string?          ImagePath   { get; init; }
    public DateTime         InspectedAt { get; init; } = DateTime.Now;
    public bool IsOk => ResultType == VisionResultType.OK;
}

// =============================================
// 통계 요약 모델
// =============================================
public record ProductionSummary
{
    public int TotalCount    { get; init; }
    public int MetalCount    { get; init; }
    public int NonMetalCount { get; init; }
    public int OkCount       { get; init; }
    public int NgCount       { get; init; }
    public double NgRate     => TotalCount > 0 ? (double)NgCount / TotalCount * 100 : 0;
}

public record VisionDailySummary
{
    public int     TotalCount        { get; init; }
    public int     OkCount           { get; init; }
    public int     NgCount           { get; init; }
    public int     MetalCount        { get; init; }
    public int     NonMetalCount     { get; init; }
    public int     MetalDamageCount    { get; init; } // 금속 NG 건수
    public int     NonMetalDamageCount { get; init; } // 비금속 NG 건수
    public double  NgRate      => TotalCount > 0 ? (double)NgCount / TotalCount * 100 : 0;
    public double  AvgConf     { get; init; }
}

public record HourlyInspection
{
    public int Hour     { get; init; }
    public int OkCount  { get; init; }
    public int NgCount  { get; init; }
    public int Total    => OkCount + NgCount;
}

public record DefectRatio
{
    public string DefectType { get; init; } = string.Empty;
    public int    Count      { get; init; }
    public double Ratio      { get; init; }
}
