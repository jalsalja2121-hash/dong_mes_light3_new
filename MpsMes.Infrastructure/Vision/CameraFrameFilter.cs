using MpsMes.Core.Interfaces;
using OpenCvSharp;

namespace MpsMes.Infrastructure.Vision;

public static class CameraFrameFilter
{
    // Project starting values, not universal OpenCV defaults.
    // Canny ratio 2.5:1 follows the documented 2:1-3:1 guidance.
    // https://docs.opencv.org/4.x/da/d5c/tutorial_canny_detector.html
    private const double CannyLow = 60;
    private const double CannyHigh = 150;
    private const int GlareValueMin = 245;
    private const int GlareSaturationMax = 40;
    private const double MaxInpaintAreaRatio = 0.03;

    public static Mat Apply(Mat source, CameraFilterSettings settings)
    {
        var output = settings.HsvEnabled ? ApplyAntiGlareFilter(source) : source.Clone();
        try
        {
            if (settings.CannyEnabled)
            {
                using var gray = new Mat();
                using var edges = new Mat();
                Cv2.CvtColor(output, gray, ColorConversionCodes.BGR2GRAY);
                Cv2.GaussianBlur(gray, gray, new Size(5, 5), 1.0);
                // L2 uses Euclidean gradient magnitude; keep weak connected contours.
                Cv2.Canny(gray, edges, CannyLow, CannyHigh, apertureSize: 3, L2gradient: true);
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
    // ── 빛반사 방지 필터 ─────────────────────────────────────────────
    // 강한 백색 반사광을 Telea Inpaint로 복원한 뒤 CLAHE와 감마 보정을 적용한다.
    private static Mat ApplyAntiGlareFilter(Mat src)
    {
        var dst = new Mat();
        try
        {
            using var inpainted = RemoveSpecularGlare(src);

            // LAB 색공간 → L채널에만 CLAHE 적용
            using var lab = new Mat();
            Cv2.CvtColor(inpainted, lab, ColorConversionCodes.BGR2Lab);
            var channels = Cv2.Split(lab);

            // clipLimit 1.5: 반사 억제와 화질 균형
            using var clahe = Cv2.CreateCLAHE(
                clipLimit: 1.5,
                tileGridSize: new OpenCvSharp.Size(8, 8));
            clahe.Apply(channels[0], channels[0]);

            Cv2.Merge(channels, lab);
            Cv2.CvtColor(lab, dst, ColorConversionCodes.Lab2BGR);

            // 감마 1.10: Inpaint 후 남은 밝은 반사를 약하게 억제한다.
            ApplyGamma(dst, dst, gamma: 1.10);

            foreach (var ch in channels) ch.Dispose();
            return dst;
        }
        catch
        {
            src.CopyTo(dst);
            return dst;
        }
    }

    private static Mat RemoveSpecularGlare(Mat src)
    {
        var result = new Mat();
        using var hsv = new Mat();
        using var glareMask = new Mat();
        using var kernel = Cv2.GetStructuringElement(
            MorphShapes.Ellipse, new OpenCvSharp.Size(3, 3));

        Cv2.CvtColor(src, hsv, ColorConversionCodes.BGR2HSV);

        // Low saturation and high value select near-white highlights, not proven glare.
        // Bright defects can also match; restrict the inpaint area conservatively.
        Cv2.InRange(
            hsv,
            new Scalar(0, 0, GlareValueMin),
            new Scalar(179, GlareSaturationMax, 255),
            glareMask);

        Cv2.MorphologyEx(
            glareMask, glareMask, MorphTypes.Close, kernel,
            iterations: 1);
        Cv2.Dilate(glareMask, glareMask, kernel, iterations: 1);

        var maskRatio = (double)Cv2.CountNonZero(glareMask)
            / (glareMask.Rows * glareMask.Cols);

        // 넓은 흰 배경이나 제품 자체가 마스크로 잡히면 원본을 유지한다.
        if (maskRatio > 0 && maskRatio <= MaxInpaintAreaRatio)
            Cv2.Inpaint(src, glareMask, result, 3.0, InpaintMethod.Telea);
        else
            src.CopyTo(result);

        return result;
    }

    private static void ApplyGamma(Mat src, Mat dst, double gamma)
    {
        // LUT (Look-Up Table) 방식으로 빠른 감마 적용
        var lut = new byte[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (byte)Math.Min(255, Math.Pow(i / 255.0, gamma) * 255.0);

        using var lutMat = new Mat(1, 256, MatType.CV_8UC1, lut);
        Cv2.LUT(src, lutMat, dst);
    }

}
