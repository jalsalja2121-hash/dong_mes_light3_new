using Dapper;
using MySqlConnector;
using MpsMes.Core.Interfaces;

namespace MpsMes.Infrastructure.Database;

// =============================================
// DB 연결 팩토리 (MySQL)
// =============================================
public class DbConnectionFactory
{
    private readonly string _connectionString;

    public DbConnectionFactory(string connectionString)
        => _connectionString = connectionString;

    public MySqlConnection Create() => new(_connectionString);
}

// =============================================
// 생산 이력 Repository
// =============================================
public class ProductionRepository : IProductionRepository
{
    private readonly DbConnectionFactory _factory;

    public ProductionRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(ProductionRecord record)
    {
        const string sql = @"
            INSERT INTO TB_PRODUCTION_HISTORY
                (COMPLETED_AT, CYCLE_TIME_SEC, METAL_QTY, NON_METAL_QTY,
                 VISION_RESULT, DEFECT_TYPE, CONFIDENCE, SHIFT_CODE)
            VALUES
                (@CompletedAt, @CycleTimeSec, @MetalQty, @NonMetalQty,
                 @VisionResult, @DefectType, @Confidence, @ShiftCode);
            SELECT LAST_INSERT_ID();";

        using var con = _factory.Create();
        return await con.ExecuteScalarAsync<long>(sql, record);
    }

    public async Task<IEnumerable<ProductionRecord>> GetRecentAsync(int count = 100)
    {
        const string sql = @"
            SELECT
                SEQ,
                COMPLETED_AT    AS CompletedAt,
                CYCLE_TIME_SEC  AS CycleTimeSec,
                METAL_QTY       AS MetalQty,
                NON_METAL_QTY   AS NonMetalQty,
                VISION_RESULT   AS VisionResult,
                DEFECT_TYPE     AS DefectType,
                CONFIDENCE,
                SHIFT_CODE      AS ShiftCode
            FROM TB_PRODUCTION_HISTORY
            ORDER BY COMPLETED_AT DESC
            LIMIT @Count";

        using var con = _factory.Create();
        return await con.QueryAsync<ProductionRecord>(sql, new { Count = count });
    }

    public async Task<IEnumerable<ProductionRecord>> SearchAsync(
        DateTime? from, DateTime? to,
        string? visionResult = null,
        string? defectType   = null)
    {
        const string sql = @"
            SELECT
                SEQ,
                COMPLETED_AT    AS CompletedAt,
                CYCLE_TIME_SEC  AS CycleTimeSec,
                METAL_QTY       AS MetalQty,
                NON_METAL_QTY   AS NonMetalQty,
                VISION_RESULT   AS VisionResult,
                DEFECT_TYPE     AS DefectType,
                CONFIDENCE,
                SHIFT_CODE      AS ShiftCode
            FROM TB_PRODUCTION_HISTORY
            WHERE 1=1
              AND (@From         IS NULL OR COMPLETED_AT >= @From)
              AND (@To           IS NULL OR COMPLETED_AT <= @To)
              AND (@VisionResult IS NULL OR VISION_RESULT = @VisionResult)
              AND (@DefectType   IS NULL OR DEFECT_TYPE   = @DefectType)
            ORDER BY COMPLETED_AT DESC";

        using var con = _factory.Create();
        return await con.QueryAsync<ProductionRecord>(sql,
            new { From = from, To = to, VisionResult = visionResult, DefectType = defectType });
    }

    public async Task<ProductionSummary> GetTodaySummaryAsync()
    {
        const string sql = @"
            SELECT
                COUNT(*)                                                            AS TotalCount,
                IFNULL(SUM(METAL_QTY), 0)                                          AS MetalCount,
                IFNULL(SUM(NON_METAL_QTY), 0)                                      AS NonMetalCount,
                IFNULL(SUM(CASE WHEN VISION_RESULT='OK' THEN 1 ELSE 0 END), 0)     AS OkCount,
                IFNULL(SUM(CASE WHEN VISION_RESULT='NG' THEN 1 ELSE 0 END), 0)     AS NgCount
            FROM TB_PRODUCTION_HISTORY
            WHERE DATE(COMPLETED_AT) = CURDATE()";

        using var con = _factory.Create();
        return await con.QuerySingleAsync<ProductionSummary>(sql);
    }
}

// =============================================
// 비전검사 Repository
// =============================================
public class VisionRepository : IVisionRepository
{
    private readonly DbConnectionFactory _factory;

    public VisionRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task<long> InsertAsync(VisionInspection record)
    {
        const string sql = @"
            INSERT INTO TB_VISION_INSPECTION
                (INSPECTED_AT, RESULT, DEFECT_TYPE, CONFIDENCE,
                 IMAGE_PATH, METAL_YN, PROD_SEQ)
            VALUES
                (@InspectedAt, @Result, @DefectType, @Confidence,
                 @ImagePath, @MetalYn, @ProdSeq);
            SELECT LAST_INSERT_ID();";

        using var con = _factory.Create();
        return await con.ExecuteScalarAsync<long>(sql, new
        {
            record.InspectedAt,
            record.Result,
            record.DefectType,
            record.Confidence,
            record.ImagePath,
            MetalYn = record.IsMetalYn ? "Y" : "N",
            record.ProdSeq
        });
    }

    public async Task<IEnumerable<VisionInspection>> GetRecentAsync(int count = 100)
    {
        const string sql = @"
            SELECT
                SEQ,
                INSPECTED_AT    AS InspectedAt,
                RESULT,
                DEFECT_TYPE     AS DefectType,
                CONFIDENCE,
                IMAGE_PATH      AS ImagePath,
                CASE WHEN METAL_YN='Y' THEN TRUE ELSE FALSE END AS IsMetalYn,
                PROD_SEQ        AS ProdSeq
            FROM TB_VISION_INSPECTION
            ORDER BY INSPECTED_AT DESC
            LIMIT @Count";

        using var con = _factory.Create();
        return await con.QueryAsync<VisionInspection>(sql, new { Count = count });
    }

    public async Task<IEnumerable<VisionInspection>> SearchAsync(
        DateTime? from, DateTime? to,
        string? result     = null,
        string? defectType = null)
    {
        const string sql = @"
            SELECT
                SEQ,
                INSPECTED_AT    AS InspectedAt,
                RESULT,
                DEFECT_TYPE     AS DefectType,
                CONFIDENCE,
                IMAGE_PATH      AS ImagePath,
                CASE WHEN METAL_YN='Y' THEN TRUE ELSE FALSE END AS IsMetalYn,
                PROD_SEQ        AS ProdSeq
            FROM TB_VISION_INSPECTION
            WHERE 1=1
              AND (@From       IS NULL OR INSPECTED_AT >= @From)
              AND (@To         IS NULL OR INSPECTED_AT <= @To)
              AND (@Result     IS NULL OR RESULT = @Result)
              AND (@DefectType IS NULL OR DEFECT_TYPE = @DefectType)
            ORDER BY INSPECTED_AT DESC";

        using var con = _factory.Create();
        return await con.QueryAsync<VisionInspection>(sql,
            new { From = from, To = to, Result = result, DefectType = defectType });
    }

    public async Task<VisionDailySummary> GetTodaySummaryAsync()
    {
        const string sql = @"
            SELECT
                COUNT(*)                                                            AS TotalCount,
                IFNULL(SUM(CASE WHEN RESULT='OK' THEN 1 ELSE 0 END), 0)             AS OkCount,
                IFNULL(SUM(CASE WHEN RESULT='NG' THEN 1 ELSE 0 END), 0)             AS NgCount,
                IFNULL(SUM(CASE WHEN METAL_YN='Y' THEN 1 ELSE 0 END), 0)            AS MetalCount,
                IFNULL(SUM(CASE WHEN METAL_YN='N' THEN 1 ELSE 0 END), 0)            AS NonMetalCount,
                IFNULL(SUM(CASE WHEN RESULT='NG' AND METAL_YN='Y' THEN 1 ELSE 0 END), 0)
                                                                                   AS MetalDamageCount,
                IFNULL(SUM(CASE WHEN RESULT='NG' AND METAL_YN='N' THEN 1 ELSE 0 END), 0)
                                                                                   AS NonMetalDamageCount,
                IFNULL(AVG(CAST(CONFIDENCE AS DECIMAL(5,2))), 0)                   AS AvgConf
            FROM TB_VISION_INSPECTION
            WHERE DATE(INSPECTED_AT) = CURDATE()";

        using var con = _factory.Create();
        return await con.QuerySingleAsync<VisionDailySummary>(sql);
    }

    public async Task<IEnumerable<HourlyInspection>> GetHourlyTodayAsync()
    {
        const string sql = @"
            SELECT
                HOUR(INSPECTED_AT)                              AS Hour,
                SUM(CASE WHEN RESULT='OK' THEN 1 ELSE 0 END)   AS OkCount,
                SUM(CASE WHEN RESULT='NG' THEN 1 ELSE 0 END)   AS NgCount
            FROM TB_VISION_INSPECTION
            WHERE DATE(INSPECTED_AT) = CURDATE()
            GROUP BY HOUR(INSPECTED_AT)
            ORDER BY Hour";

        using var con = _factory.Create();
        return await con.QueryAsync<HourlyInspection>(sql);
    }

    public async Task<IEnumerable<DefectRatio>> GetDefectRatioAsync(DateTime date)
    {
        const string sql = @"
            SELECT
                DEFECT_TYPE                                     AS DefectType,
                COUNT(*)                                        AS Count,
                COUNT(*) * 100.0 / SUM(COUNT(*)) OVER()        AS Ratio
            FROM TB_VISION_INSPECTION
            WHERE RESULT = 'NG'
              AND DATE(INSPECTED_AT) = DATE(@Date)
              AND DEFECT_TYPE IS NOT NULL
            GROUP BY DEFECT_TYPE
            ORDER BY Count DESC";

        using var con = _factory.Create();
        return await con.QueryAsync<DefectRatio>(sql, new { Date = date });
    }
}

// =============================================
// 이벤트 로그 Repository
// =============================================
public class EventLogRepository : IEventLogRepository
{
    private readonly DbConnectionFactory _factory;

    public EventLogRepository(DbConnectionFactory factory) => _factory = factory;

    public async Task InsertAsync(EventLog log)
    {
        const string sql = @"
            INSERT INTO TB_EVENT_LOG
                (EVENT_AT, EVENT_TYPE, EVENT_CODE, MESSAGE, SOURCE)
            VALUES
                (@EventAt, @EventType, @EventCode, @Message, @Source)";

        using var con = _factory.Create();
        await con.ExecuteAsync(sql, log);
    }

    public async Task<IEnumerable<EventLog>> GetRecentAsync(int count = 200)
    {
        const string sql = @"
            SELECT
                SEQ,
                EVENT_AT    AS EventAt,
                EVENT_TYPE  AS EventType,
                EVENT_CODE  AS EventCode,
                MESSAGE     AS Message,
                SOURCE      AS Source
            FROM TB_EVENT_LOG
            ORDER BY EVENT_AT DESC
            LIMIT @Count";

        using var con = _factory.Create();
        return await con.QueryAsync<EventLog>(sql, new { Count = count });
    }

    public async Task<IEnumerable<EventLog>> SearchAsync(
        DateTime? from, DateTime? to, string? eventType = null)
    {
        const string sql = @"
            SELECT
                SEQ,
                EVENT_AT    AS EventAt,
                EVENT_TYPE  AS EventType,
                EVENT_CODE  AS EventCode,
                MESSAGE     AS Message,
                SOURCE      AS Source
            FROM TB_EVENT_LOG
            WHERE 1=1
              AND (@From      IS NULL OR EVENT_AT  >= @From)
              AND (@To        IS NULL OR EVENT_AT  <= @To)
              AND (@EventType IS NULL OR EVENT_TYPE = @EventType)
            ORDER BY EVENT_AT DESC";

        using var con = _factory.Create();
        return await con.QueryAsync<EventLog>(sql,
            new { From = from, To = to, EventType = eventType });
    }
}
