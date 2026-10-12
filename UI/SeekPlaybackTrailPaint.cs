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

        var (peak, mid, soft, clear) = TrailColors(color);
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

    /// <summary>早戻し用。ヘッド右側（通過済み）へ前方と同じ帯を鏡像で描く。</summary>
    public static void DrawReverse(
        DrawingContext dc,
        Rect band,
        double playheadX,
        double contentRight,
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

        var trailLeftX = playheadX + PlayheadGapPx;
        var trailRightLimit = playheadX + TargetLengthPx;
        if (trailLeftX >= contentRight || trailLeftX >= trailRightLimit)
        {
            return;
        }

        double? coveredRight = null;
        foreach (var sample in samples)
        {
            if (now - sample.TickMs >= fadeMs)
            {
                continue;
            }

            var x = frameToX(sample.Frame);
            if (x < trailLeftX)
            {
                continue;
            }

            coveredRight = coveredRight is double right ? Math.Max(right, x) : x;
        }

        if (coveredRight is null)
        {
            return;
        }

        var drawLeft = Math.Max(band.Left, trailLeftX);
        var drawRight = Math.Min(contentRight, Math.Min(trailRightLimit, coveredRight.Value));
        var drawW = drawRight - drawLeft;
        if (drawW < 1)
        {
            return;
        }

        var (peak, mid, soft, clear) = TrailColors(color);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(playheadX, 0),
            EndPoint = new Point(playheadX + TargetLengthPx, 0),
            GradientStops =
            [
                new GradientStop(peak, 0),
                new GradientStop(mid, 0.28),
                new GradientStop(soft, 0.58),
                new GradientStop(clear, 0.86),
                new GradientStop(clear, 1),
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

    private static (Color Peak, Color Mid, Color Soft, Color Clear) TrailColors(Color color) =>
    (
        Color.FromArgb(ToByteAlpha(PeakAlpha), color.R, color.G, color.B),
        Color.FromArgb(ToByteAlpha(PeakAlpha * 0.25f), color.R, color.G, color.B),
        Color.FromArgb(ToByteAlpha(PeakAlpha * 0.06f), color.R, color.G, color.B),
        Color.FromArgb(0, color.R, color.G, color.B)
    );
}
