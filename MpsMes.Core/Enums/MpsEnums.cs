namespace MpsMes.Core.Enums;

/// <summary>PLC 연결 상태</summary>
public enum PlcConnectionStatus
{
    Disconnected = 0,
    Connecting   = 1,
    Connected    = 2,
    Error        = 3
}

/// <summary>MPS 운전 모드 (D0 레지스터)</summary>
public enum RunMode
{
    Stop    = 0,
    Reset   = 1,   // D0: 1~49
    Manual  = 50,  // 이송 대기
    Auto    = 2    // 운전중 (기존 값 유지)
}

/// <summary>비전 검사 결과</summary>
public enum VisionResultType
{
    Pending = 0,
    OK      = 1,
    NG      = 2
}

/// <summary>이벤트 로그 유형</summary>
public enum EventType
{
    Info    = 0,
    Warn    = 1,
    Error   = 2,
    Plc     = 3,
    Vision  = 4
}

/// <summary>출력 코일 M 비트 주소 (래더 수동 입력신호 M3000번대)</summary>
public enum OutputMBit
{
    SupplyCylFwd    = 3000,  // M3000 → Y22 공급 실린더 전진
    SupplyCylBwd    = 3001,  // M3001 → Y23 공급 실린더 후진
    DistribCylFwd   = 3002,  // M3002 → Y24 분배 실린더 전진
    DistribCylBwd   = 3003,  // M3003 → Y25 분배 실린더 후진
    MachCylDown     = 3004,  // M3004 → Y26 가공 실린더 하강
    EjectCylFwd     = 3006,  // M3006 → Y27 배출 실린더 전진
    StopperCylDown  = 3008,  // M3008 → Y28 스토퍼 실린더 하강
    StopperCylUp    = 3009,  // M3009 → Y29 스토퍼 실린더 상승
    SuctionCylFwd   = 3010,  // M3010 → Y2A 흡착 실린더 전진
    SuctionCylBwd   = 3011,  // M3011 → Y2B 흡착 실린더 후진
    StoreCylFwd     = 3012,  // M3012 → Y2C 저장 실린더 전진
    StoreCylBwd     = 3013,  // M3013 → Y2D 저장 실린더 후진
    SuctionOn       = 3014,  // M3014 → Y2E 흡착 ON
    ProcessMotor    = 3015,  // M3015 → Y20 PROCESS 모터
    ConveyorMotor   = 3016,  // M3016 → Y21 CONVEYOR 모터
}

/// <summary>출력 코일 Y 어드레스 (모니터링용)</summary>
public enum OutputCoil
{
    ProcessMotor        = 0x20,  // Y20
    ConveyorMotor       = 0x21,  // Y21
    SupplyCylFwd        = 0x22,  // Y22 공급 실린더 전진
    SupplyCylBwd        = 0x23,  // Y23 공급 실린더 후진
    DistribCylFwd       = 0x24,  // Y24 분배 실린더 전진
    DistribCylBwd       = 0x25,  // Y25 분배 실린더 후진
    MachCylDown         = 0x26,  // Y26 가공 실린더 하강
    EjectCylFwd         = 0x27,  // Y27 배출 실린더 전진
    StopperCylDown      = 0x28,  // Y28 스토퍼 실린더 하강
    StopperCylUp        = 0x29,  // Y29 스토퍼 실린더 상승
    SuctionCylFwd       = 0x2A,  // Y2A 흡착 실린더 전진
    SuctionCylBwd       = 0x2B,  // Y2B 흡착 실린더 후진
    StoreCylFwd         = 0x2C,  // Y2C 저장 실린더 전진
    StoreCylBwd         = 0x2D,  // Y2D 저장 실린더 후진
    SuctionOn           = 0x2E   // Y2E 흡착 ON
}

/// <summary>입력 센서 (X 어드레스)</summary>
public enum InputSensor
{
    SupplyFwdSen        = 0x00,  // X00 공급 전진 SEN
    SupplyBwdSen        = 0x01,  // X01 공급 후진 SEN
    DistribFwdSen       = 0x02,  // X02 분배 전진 SEN
    DistribBwdSen       = 0x03,  // X03 분배 후진 SEN
    MachFwdSen          = 0x04,  // X04 가공 전진 SEN
    MachBwdSen          = 0x05,  // X05 가공 후진 SEN
    EjectFwdSen         = 0x06,  // X06 배출 전진 SEN
    EjectBwdSen         = 0x07,  // X07 배출 후진 SEN
    StopFwdSen          = 0x08,  // X08 정지 전진 SEN
    StopBwdSen          = 0x09,  // X09 정지 후진 SEN
    SuctionFwdSen       = 0x0A,  // X0A 흡착 전진 SEN
    SuctionBwdSen       = 0x0B,  // X0B 흡착 후진 SEN
    StoreFwdSen         = 0x0C,  // X0C 저장 전진 SEN
    StoreBwdSen         = 0x0D,  // X0D 저장 후진 SEN
    VacuumSw            = 0x0E,  // X0E 진공 SW
    SupplyMagFiber      = 0x0F,  // X0F 공급 매거진 화이버
    DistribMagFiber     = 0x10,  // X10 분배 매거진 화이버
    CapacitiveSen       = 0x11,  // X11 용량형 SEN (비금속)
    InductiveSen        = 0x12,  // X12 유도형 SEN (금속)
    StopperFiber        = 0x13   // X13 정지 스토퍼 화이버
}
