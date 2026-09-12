using System.Windows;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>再生ヘッド後方の残光。波形とオーバービューで同じ帯を描く。</summary>
internal static class SeekPlaybackTrailPaint
{
    public const float TargetLengthPx = 360f;
    public const int SampleRetainMs = 10400;
    public const float PeakAlpha = 0.15f;
    public const float PlayheadGapPx = 2f;

    public static void Draw(
        DrawingContext dc,
        Rect band,
        double playheadX,
        double contentLeft,
        IReadOnlyList<(long Frame, long TickMs)> samples,
        Func<long, double> frameToX,
        double fadeMs,
        Color color)
    {
        var now = Environment.TickCount64;
        if (samples.Count < 2 || band.Width <= 0)
        {
            return;
        }

        var trailRightX = playheadX - PlayheadGapPx;
        var trailLeftLimit = playheadX - TargetLengthPx;
        if (trailRightX <= contentLeft || trailRightX <= trailLeftLimit)
        {
            return;
        }

        double? coveredLeft = null;
        foreach (var sample in samples)
        {
            if (now - sample.TickMs >= fadeMs)
            {
                continue;
            }

            var x = frameToX(sample.Frame);
            if (x > trailRightX)
            {
                continue;
            }

            coveredLeft = coveredLeft is double left ? Math.Min(left, x) : x;
        }

        if (coveredLeft is null)
        {
            return;
        }

        var drawLeft = Math.Max(contentLeft, Math.Max(trailLeftLimit, coveredLeft.Value));
        var drawRight = Math.Min(band.Right, trailRightX);
        var drawW = drawRight - drawLeft;
        if (drawW < 1)
        {
            return;
        }

        var peak = Color.FromArgb(ToByteAlpha(PeakAlpha), color.R, color.G, color.B);
        var mid = Color.FromArgb(ToByteAlpha(PeakAlpha * 0.25f), color.R, color.G, color.B);
        var soft = Color.FromArgb(ToByteAlpha(PeakAlpha * 0.06f), color.R, color.G, color.B);
        var clear = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(playheadX - TargetLengthPx, 0),
            EndPoint = new Point(playheadX, 0),
            GradientStops =
            [
                new GradientStop(clear, 0),
                new GradientStop(clear, 0.14),
                new GradientStop(soft, 0.42),
                new GradientStop(mid, 0.72),
                new GradientStop(peak, 1),
            ],
        };
        brush.Freeze();
        dc.DrawRectangle(brush, null, new Rect(drawLeft, band.Y, drawW, band.Height));
    }

    public static double FadeMs(double contentWidth, double viewDurationSec)
    {
        if (viewDurationSec <= 0 || contentWidth <= 1)
        {
            return SampleRetainMs;
        }

        var fadeSec = TargetLengthPx / contentWidth * viewDurationSec;
        fadeSec = Math.Clamp(fadeSec, 0.2, 60.0);
        return fadeSec * 1000.0;
    }

    public static byte ToByteAlpha(float a) =>
        (byte)Math.Clamp((int)MathF.Round(a * 255f), 0, 255);
}
