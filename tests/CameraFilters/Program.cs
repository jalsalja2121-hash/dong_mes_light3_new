using MpsMes.Core.Interfaces;
using MpsMes.Infrastructure.Vision;
using OpenCvSharp;

static void Check(bool ok, string name)
{
    if (!ok) throw new Exception(name);
    Console.WriteLine("PASS: " + name);
}

using var src = new Mat(100, 100, MatType.CV_8UC3, new Scalar(60, 60, 60));
Cv2.Rectangle(src, new Rect(20, 20, 60, 60), new Scalar(0, 0, 180), -1);
Cv2.Circle(src, new Point(50, 50), 3, Scalar.White, -1);
using var original = src.Clone();
using var disabled = CameraFrameFilter.Apply(src, new() { HsvEnabled = false, ClaheEnabled = false, GammaEnabled = false, CannyEnabled = false });
Check(Cv2.Norm(src, disabled) == 0, "OFF preserves original frame");
using var corrected = CameraFrameFilter.Apply(src, new() { HsvEnabled = true, CannyEnabled = false });
var spot = corrected.At<Vec3b>(50, 50);
Check(Math.Max(spot.Item0, Math.Max(spot.Item1, spot.Item2)) < 248, "HSV reduces small white glare");
var red = corrected.At<Vec3b>(35, 35);
Check(red.Item2 > red.Item0 && red.Item2 > red.Item1, "HSV preserves red material");
using var edges = CameraFrameFilter.Apply(src, new() { HsvEnabled = false, ClaheEnabled = false, GammaEnabled = false, CannyEnabled = true });
using var green = new Mat();
Cv2.InRange(edges, new Scalar(0, 255, 0), new Scalar(0, 255, 0), green);
Check(Cv2.CountNonZero(green) > 0, "Canny ON displays contours");
using var combined = CameraFrameFilter.Apply(src, new() { HsvEnabled = true, CannyEnabled = true });
Check(combined.Size() == src.Size() && combined.Type() == src.Type(), "combined filters preserve format");
using var white = new Mat(100, 100, MatType.CV_8UC3, Scalar.White);
using var whiteResult = CameraFrameFilter.Apply(white, new());
Check(whiteResult.At<Vec3b>(50, 50).Item0 > 240, "large white surface is not inpainted");
Check(Cv2.Norm(src, original) == 0, "source is unchanged");
Check(new CameraFilterSettings().HsvEnabled && !new CameraFilterSettings().CannyEnabled, "defaults leave Canny disabled");

// Near-saturated glare below the former V=248 cutoff.
using var nearGlare = new Mat(160, 160, MatType.CV_8UC3, new Scalar(90, 90, 90));
Cv2.Circle(nearGlare, new Point(80, 80), 4, new Scalar(246, 246, 246), -1);
using var nearCorrected = CameraFrameFilter.Apply(nearGlare, new());
Check(nearCorrected.At<Vec3b>(80, 80).Item0 < 220, "near-saturated white glare is reduced");

// A colored highlight should retain its color instead of being filled from the gray background.
using var colored = new Mat(160, 160, MatType.CV_8UC3, new Scalar(90, 90, 90));
Cv2.Circle(colored, new Point(80, 80), 4, new Scalar(100, 100, 250), -1);
using var coloredResult = CameraFrameFilter.Apply(colored, new());
var colorSpot = coloredResult.At<Vec3b>(80, 80);
Check(colorSpot.Item2 > colorSpot.Item0 + 50, "colored highlight is preserved");

// Broad white features must not be replaced by neighboring dark material.
using var broad = new Mat(160, 160, MatType.CV_8UC3, new Scalar(60, 60, 60));
Cv2.Rectangle(broad, new Rect(60, 60, 35, 35), Scalar.White, -1);
using var broadResult = CameraFrameFilter.Apply(broad, new());
Check(broadResult.At<Vec3b>(75, 75).Item0 > 240, "broad white feature is protected");

using var subtle = new Mat(160, 160, MatType.CV_8UC3, new Scalar(80, 80, 80));
Cv2.Rectangle(subtle, new Rect(40, 40, 80, 80), new Scalar(150, 150, 150), -1);
using var subtleResult = CameraFrameFilter.Apply(subtle, new() { HsvEnabled = false, ClaheEnabled = false, GammaEnabled = false, CannyEnabled = true });
using var subtleEdges = new Mat();
Cv2.InRange(subtleResult, new Scalar(0, 255, 0), new Scalar(0, 255, 0), subtleEdges);
Check(Cv2.CountNonZero(subtleEdges) > 200, "moderate-contrast contour is retained");

using var noise = new Mat(160, 160, MatType.CV_8UC3);
var random = new Random(42);
for (int y = 0; y < noise.Rows; y++)
for (int x = 0; x < noise.Cols; x++)
{
    byte value = (byte)(100 + random.Next(-8, 9));
    noise.Set(y, x, new Vec3b(value, value, value));
}
using var noiseResult = CameraFrameFilter.Apply(noise, new() { HsvEnabled = false, ClaheEnabled = false, GammaEnabled = false, CannyEnabled = true });
using var noiseEdges = new Mat();
Cv2.InRange(noiseResult, new Scalar(0, 255, 0), new Scalar(0, 255, 0), noiseEdges);
Check(Cv2.CountNonZero(noiseEdges) == 0, "low-amplitude noise does not create contours");

var hsvOnly = new CameraFilterSettings { ClaheEnabled = false, GammaEnabled = false };
using var hsvResult = CameraFrameFilter.Apply(nearGlare, hsvOnly);
Check(hsvResult.At<Vec3b>(80, 80).Item0 < 220, "HSV works independently");
using var gammaOnly = CameraFrameFilter.Apply(nearGlare, new() { HsvEnabled = false, ClaheEnabled = false, GammaEnabled = true });
Check(gammaOnly.At<Vec3b>(0, 0).Item0 < 90, "gamma works while HSV is OFF");
using var claheOnly = CameraFrameFilter.Apply(nearGlare, new() { HsvEnabled = false, ClaheEnabled = true, GammaEnabled = false });
Check(Cv2.Norm(nearGlare, claheOnly) > 0, "CLAHE works while HSV is OFF");
using var hsvOff = CameraFrameFilter.Apply(nearGlare, hsvOnly with { HsvEnabled = false });
Check(Cv2.Norm(nearGlare, hsvOff) == 0, "HSV OFF preserves original when other filters are OFF");
using var hsvOnAgain = CameraFrameFilter.Apply(nearGlare, hsvOnly);
Check(Cv2.Norm(hsvResult, hsvOnAgain) == 0, "HSV ON restores fixed correction after OFF");
