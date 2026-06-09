using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MpsMes.Core.Interfaces;
using System.Collections.ObjectModel;

namespace MpsMes.ViewModels;

// =============================================
// 시간대별 생산량 (막대그래프용)
// =============================================
public class HourlyProductionItem
{
    public string Label    { get; set; } = string.Empty;  // "00시"
    public int    Total    { get; set; }
    public int    Metal    { get; set; }
    public int    NonMetal { get; set; }
    public double BarHeight => Total * 2.0;  // 픽셀 환산 (최대 200px 기준)
}

// =============================================
// 사이클타임 항목
// =============================================
public class CycleTimeItem
{
    public DateTime CompletedAt  { get; set; }
    public double   CycleTimeSec { get; set; }
    public string   TimeLabel    => CompletedAt.ToString("HH:mm:ss");
}

// =============================================
// 생산현황 ViewModel
// =============================================
public partial class ProductionStatusViewModel : ObservableObject
{
    private readonly IProductionRepository _repo;

    // ── KPI ──────────────────────────────────────────────────
    [ObservableProperty] private int    _targetQty      = 100;    // 목표 수량 (설정값)
    [ObservableProperty] private string _targetInput    = "100";  // 입력창 임시값
    [ObservableProperty] private int    _totalProduced  = 0;      // 금일 생산수량
    [ObservableProperty] private double _achieveRate    = 0.0;    // 달성률 (%)
    [ObservableProperty] private double _defectRate     = 0.0;    // 불량률 (%)

    // ── 소재별 수량 (파이차트) ────────────────────────────────
    [ObservableProperty] private int    _metalTotal      = 0;
    [ObservableProperty] private int    _nonMetalTotal   = 0;
    // 파이차트: 금속 시작=0, 스윕각
    [ObservableProperty] private double _metalStartAngle  = 0;
    [ObservableProperty] private double _metalSweepAngle  = 180;
    [ObservableProperty] private double _nonMetalStartAngle = 180;
    [ObservableProperty] private double _nonMetalSweepAngle = 180;
    [ObservableProperty] private double _metalRatioPercent    = 50.0;
    [ObservableProperty] private double _nonMetalRatioPercent = 50.0;

    // ── 사이클타임 분석 ──────────────────────────────────────
    [ObservableProperty] private double _avgCycleTime   = 0.0;
    [ObservableProperty] private double _minCycleTime   = 0.0;
    [ObservableProperty] private double _maxCycleTime   = 0.0;

    // ── 최근 갱신 시각 ────────────────────────────────────────
    [ObservableProperty] private string _lastRefresh    = "-";

    // ── 컬렉션 ────────────────────────────────────────────────
    public ObservableCollection<HourlyProductionItem> HourlyItems   { get; } = new();
    public ObservableCollection<CycleTimeItem>        CycleItems    { get; } = new();

    public ProductionStatusViewModel(IProductionRepository repo)
    {
        _repo = repo;
        // 2초마다 자동 새로고침
        var timer = new System.Timers.Timer(2000);
        timer.Elapsed += async (s, e) => { try { await RefreshAsync(); } catch { } };
        timer.Start();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        try
        {
            // 오늘 전체 생산이력 조회
            var today = DateTime.Today;
            var records = (await _repo.SearchAsync(today, today.AddDays(1))).ToList();

            // ── KPI 계산 ─────────────────────────────────────
            TotalProduced = records.Sum(r => r.TotalQty);
            AchieveRate   = TargetQty > 0 ? (double)TotalProduced / TargetQty * 100.0 : 0;

            int ngCount   = records.Count(r => r.VisionResult == "NG");
            int okCount   = records.Count(r => r.VisionResult == "OK");
            int inspected = ngCount + okCount;
            DefectRate    = inspected > 0 ? (double)ngCount / inspected * 100.0 : 0;

            // ── 소재별 수량 ──────────────────────────────────
            MetalTotal    = records.Sum(r => r.MetalQty);
            NonMetalTotal = records.Sum(r => r.NonMetalQty);
            int matTotal  = MetalTotal + NonMetalTotal;
            if (matTotal > 0)
            {
                double metalRatio    = (double)MetalTotal / matTotal;
                double nonMetalRatio = (double)NonMetalTotal / matTotal;
                MetalRatioPercent    = metalRatio    * 100.0;
                NonMetalRatioPercent = nonMetalRatio * 100.0;
                MetalStartAngle      = 0;
                MetalSweepAngle      = metalRatio * 360.0;
                NonMetalStartAngle   = MetalSweepAngle;
                NonMetalSweepAngle   = nonMetalRatio * 360.0;
            }
            else
            {
                MetalRatioPercent    = 0;
                NonMetalRatioPercent = 0;
                MetalStartAngle      = 0;
                MetalSweepAngle      = 180;
                NonMetalStartAngle   = 180;
                NonMetalSweepAngle   = 180;
            }

            // ── 시간대별 생산량 ──────────────────────────────
            var hourlyGroups = records
                .GroupBy(r => r.CompletedAt.Hour)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());

            var tempList = new List<HourlyProductionItem>();
            for (int h = 0; h < 24; h++)
            {
                if (hourlyGroups.TryGetValue(h, out var grp))
                {
                    int t = grp.Sum(r => r.TotalQty);
                    int m = grp.Sum(r => r.MetalQty);
                    int n = grp.Sum(r => r.NonMetalQty);
                    tempList.Add(new HourlyProductionItem { Label = $"{h:D2}시", Total = t, Metal = m, NonMetal = n });
                }
                else
                    tempList.Add(new HourlyProductionItem { Label = $"{h:D2}시", Total = 0, Metal = 0, NonMetal = 0 });
            }

            var ctList = records
                .Where(r => r.CycleTimeSec.HasValue && r.CycleTimeSec > 0)
                .Select(r => (double)r.CycleTimeSec!)
                .ToList();

            var cycleItems = records
                .Where(r => r.CycleTimeSec.HasValue && r.CycleTimeSec > 0)
                .TakeLast(50)
                .Select(r => new CycleTimeItem
                {
                    CompletedAt  = r.CompletedAt,
                    CycleTimeSec = (double)r.CycleTimeSec!
                }).ToList();

            // UI 스레드에서 컬렉션 업데이트
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                HourlyItems.Clear();
                foreach (var item in tempList) HourlyItems.Add(item);

                if (ctList.Any()) { AvgCycleTime = ctList.Average(); MinCycleTime = ctList.Min(); MaxCycleTime = ctList.Max(); }
                else { AvgCycleTime = MinCycleTime = MaxCycleTime = 0; }

                CycleItems.Clear();
                foreach (var item in cycleItems) CycleItems.Add(item);

                LastRefresh = DateTime.Now.ToString("HH:mm:ss");
            });
        }
        catch { /* 무시 */ }
    }

    // 목표수량 변경 시 달성률 재계산
    partial void OnTargetQtyChanged(int value)
    {
        AchieveRate  = value > 0 ? (double)TotalProduced / value * 100.0 : 0;
        TargetInput  = value.ToString();
    }

    [RelayCommand]
    private void SetTarget()
    {
        if (int.TryParse(TargetInput, out int val) && val > 0)
            TargetQty = val;
        else
            TargetInput = TargetQty.ToString(); // 잘못된 입력 원복
    }
}
