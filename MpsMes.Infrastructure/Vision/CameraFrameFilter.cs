using MpsMes.Core.Interfaces;
using OpenCvSharp;

namespace MpsMes.Infrastructure.Vision;

public static class CameraFrameFilter
{
    public static Mat Apply(Mat source, CameraFilterSettings settings)
    {
        var output = source.Clone();
        try
        {
            using var mask = new Mat();
            if (settings.HsvEnabled)
            {
                using var hsv = new Mat();
                Cv2.CvtColor(source, hsv, ColorConversionCodes.BGR2HSV);
                int hMin = Math.Clamp(settings.HueMin, 0, 179);
                int hMax = Math.Clamp(settings.HueMax, 0, 179);
                int sMin = Math.Clamp(Math.Min(settings.SaturationMin, settings.SaturationMax), 0, 255);
                int sMax = Math.Clamp(Math.Max(settings.SaturationMin, settings.SaturationMax), 0, 255);
                int vMin = Math.Clamp(Math.Min(settings.ValueMin, settings.ValueMax), 0, 255);
                int vMax = Math.Clamp(Math.Max(settings.ValueMin, settings.ValueMax), 0, 255);
                if (hMin <= hMax)
                    Cv2.InRange(hsv, new Scalar(hMin, sMin, vMin), new Scalar(hMax, sMax, vMax), mask);
                else
                {
                    // Hue wraps around red: e.g. 170..10 selects both ends.
                    using var upper = new Mat();
                    Cv2.InRange(hsv, new Scalar(hMin, sMin, vMin), new Scalar(179, sMax, vMax), mask);
                    Cv2.InRange(hsv, new Scalar(0, sMin, vMin), new Scalar(hMax, sMax, vMax), upper);
                    Cv2.BitwiseOr(mask, upper, mask);
                }
                output.SetTo(Scalar.Black);
                source.CopyTo(output, mask);
            }
            if (settings.CannyEnabled)
            {
                using var gray = new Mat();
                using var edges = new Mat();
                Cv2.CvtColor(source, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new Size(5, 5), 0);
                int low = Math.Clamp(Math.Min(settings.CannyLow, settings.CannyHigh), 0, 255);
                int high = Math.Clamp(Math.Max(settings.CannyLow, settings.CannyHigh), 0, 255);
                Cv2.Canny(gray, edges, low, high);
                if (settings.HsvEnabled) Cv2.BitwiseAnd(edges, mask, edges);
                output.SetTo(new Scalar(0, 255, 0), edges);
            }
            return output;
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }
}
