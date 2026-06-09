using MpsMes.Core.Enums;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace MpsMes.WPF.Converters;

/// <summary>bool → Visibility</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

/// <summary>bool → LED 색상 (초록 / 회색)</summary>
public class BoolToLedBrushConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x8A, 0x3C))  // OK 초록
            : new SolidColorBrush(Color.FromRgb(0xC0, 0xCC, 0xDA)); // 비활성 회색
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>PLC 연결 상태 → LED 색상</summary>
public class PlcStatusToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return (PlcConnectionStatus)value switch
        {
            PlcConnectionStatus.Connected  => new SolidColorBrush(Color.FromRgb(0x1B, 0x8A, 0x3C)),
            PlcConnectionStatus.Connecting => new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD4)),
            PlcConnectionStatus.Error      => new SolidColorBrush(Color.FromRgb(0xD4, 0x28, 0x28)),
            _                              => new SolidColorBrush(Color.FromRgb(0xC0, 0xCC, 0xDA))
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>PLC 연결 상태 → 텍스트</summary>
public class PlcStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return (PlcConnectionStatus)value switch
        {
            PlcConnectionStatus.Connected  => "연결됨",
            PlcConnectionStatus.Connecting => "연결중...",
            PlcConnectionStatus.Error      => "오류",
            _                              => "미연결"
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>OK/NG → 배경색 (라이트 테마)</summary>
public class ResultToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value?.ToString() switch
        {
            "OK" => new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xED)),  // 연초록
            "NG" => new SolidColorBrush(Color.FromRgb(0xFD, 0xE8, 0xE8)),  // 연빨강
            _    => new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB))   // 연회
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>OK/NG → 전경색</summary>
public class ResultToForegroundConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value?.ToString() switch
        {
            "OK" => new SolidColorBrush(Color.FromRgb(0x1B, 0x8A, 0x3C)),  // 초록
            "NG" => new SolidColorBrush(Color.FromRgb(0xD4, 0x28, 0x28)),  // 빨강
            _    => new SolidColorBrush(Color.FromRgb(0x6A, 0x85, 0xA8))   // 회
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

public class RunModeToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return (RunMode)value switch
        {
            RunMode.Auto   => new SolidColorBrush(Color.FromRgb(0x1B, 0x8A, 0x3C)),
            RunMode.Manual => new SolidColorBrush(Color.FromRgb(0x2E, 0x6F, 0xD4)),
            _              => new SolidColorBrush(Color.FromRgb(0x8A, 0x9A, 0xB5))
        };
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is bool b ? !b : true;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is bool b ? !b : false;
}

public class CameraButtonTextConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? "카메라 해제" : "카메라 연결";
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

public class PercentageConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is double d ? $"{d:F1}%" : "0.0%";
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>파이차트 PathGeometry</summary>
public class AngleToArcConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 3) return Geometry.Empty;
        double start  = values[0] is double s  ? s  : 0;
        double sweep  = values[1] is double sw ? sw : 0;
        double radius = values[2] is double r  ? r  : 75;
        double cx = radius, cy = radius;

        if (Math.Abs(sweep - 360) < 0.01)
            return new EllipseGeometry(new Point(cx, cy), radius, radius);

        double startRad = (start - 90) * Math.PI / 180;
        double endRad   = (start + sweep - 90) * Math.PI / 180;
        var startPt = new Point(cx + radius * Math.Cos(startRad), cy + radius * Math.Sin(startRad));
        var endPt   = new Point(cx + radius * Math.Cos(endRad),   cy + radius * Math.Sin(endRad));

        var geo = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(cx, cy), IsClosed = true };
        fig.Segments.Add(new LineSegment(startPt, true));
        fig.Segments.Add(new ArcSegment(endPt, new Size(radius, radius), 0,
                                         sweep > 180, SweepDirection.Clockwise, true));
        geo.Figures.Add(fig);
        return geo;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

public class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 2) return 0.0;
        double pct   = values[0] is double d ? d : 0;
        double total = values[1] is double w ? w : 0;
        return Math.Max(0, Math.Min(total, total * pct / 100.0));
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>IsActive → 스텝 배경 (파란 테마)</summary>
public class StepBgConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(0x1A, 0x54, 0xAF))
            : new SolidColorBrush(Color.FromRgb(0xF4, 0xF7, 0xFB));
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>IsActive → 스텝 텍스트 색</summary>
public class StepFgConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(0x6A, 0x85, 0xA8));
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>IsActive → 폰트 굵기</summary>
public class StepFwConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? FontWeights.Bold : FontWeights.Normal;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>막대 높이 정규화</summary>
public class BarHeightConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 2) return 4.0;
        int val = values[0] is int i ? i : 0;
        if (values[1] is not System.Collections.IEnumerable items) return 4.0;
        int max = 1;
        foreach (var item in items)
        {
            // Total 또는 OkCount+NgCount 중 최대값
            var totalProp = item.GetType().GetProperty("Total");
            if (totalProp?.GetValue(item) is int tv && tv > max) max = tv;
            var okProp = item.GetType().GetProperty("OkCount");
            var ngProp = item.GetType().GetProperty("NgCount");
            if (okProp?.GetValue(item) is int ov && ov > max) max = ov;
            if (ngProp?.GetValue(item) is int nv && nv > max) max = nv;
        }
        return val == 0 ? 2.0 : Math.Max(2.0, (double)val / max * 120.0);
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

/// <summary>
/// 도넛차트용 Ring Arc (링/환형 호) PathGeometry
/// values[0]=startAngle(deg)  values[1]=sweepAngle(deg)
/// values[2]=outerRadius      values[3]=innerRadius
/// cx, cy 는 outerRadius 값으로 자동 계산
/// </summary>
public class RingArcConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type t, object p, CultureInfo c)
    {
        if (values.Length < 4) return Geometry.Empty;
        double start  = values[0] is double s  ? s  : 0;
        double sweep  = values[1] is double sw ? sw : 0;
        double outerR = values[2] is double o  ? o  : 70;
        double innerR = values[3] is double i  ? i  : 46;
        double cx = outerR, cy = outerR;

        if (Math.Abs(sweep) < 0.1) return Geometry.Empty;
        if (Math.Abs(sweep - 360) < 0.5)
        {
            // 완전한 원 → 두 반원으로 구성된 링
            var fullGeo = new PathGeometry();
            // 바깥 원
            var outer = new EllipseGeometry(new Point(cx, cy), outerR, outerR);
            // 안쪽 원 (잘라내기)
            var inner = new EllipseGeometry(new Point(cx, cy), innerR, innerR);
            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            return combined;
        }

        double startRad = (start - 90) * Math.PI / 180;
        double endRad   = (start + sweep - 90) * Math.PI / 180;
        bool isLarge = sweep > 180;

        var p1 = new Point(cx + outerR * Math.Cos(startRad), cy + outerR * Math.Sin(startRad));
        var p2 = new Point(cx + outerR * Math.Cos(endRad),   cy + outerR * Math.Sin(endRad));
        var p3 = new Point(cx + innerR * Math.Cos(endRad),   cy + innerR * Math.Sin(endRad));
        var p4 = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));

        var fig = new PathFigure { StartPoint = p1, IsClosed = true };
        fig.Segments.Add(new ArcSegment(p2, new Size(outerR, outerR), 0,
                                         isLarge, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(p3, true));
        fig.Segments.Add(new ArcSegment(p4, new Size(innerR, innerR), 0,
                                         isLarge, SweepDirection.Counterclockwise, true));

        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        return geo;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c)
        => throw new NotImplementedException();
}
