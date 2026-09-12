namespace MgaSonicAnvil.UI;

internal readonly record struct TimeScrollView(double ViewStart, double ViewSpan);

internal enum TimeScrollHit
{
    None,
    Left,
    Thumb,
    Right,
}

/// <summary>波形タイムスクロールのヒットと両端拡縮。</summary>
internal static class TimeScrollRange
{
    public const double ScrollStepFraction = 0.05;

    public static double HandleWidth(double thumbWidth, double preferred) =>
        Math.Max(1d, Math.Min(preferred, Math.Max(0d, thumbWidth) * 0.5));

    public static TimeScrollHit HitTest(double x, double thumbLeft, double thumbWidth, double preferredHandle)
    {
        if (thumbWidth <= 0 || x < thumbLeft || x > thumbLeft + thumbWidth)
        {
            return TimeScrollHit.None;
        }

        var handle = HandleWidth(thumbWidth, preferredHandle);
        if (x <= thumbLeft + handle)
        {
            return TimeScrollHit.Left;
        }

        if (x >= thumbLeft + thumbWidth - handle)
        {
            return TimeScrollHit.Right;
        }

        return TimeScrollHit.Thumb;
    }

    public static double FrameAt(double x, double trackWidth, long totalFrames)
    {
        if (trackWidth <= 0 || totalFrames <= 0)
        {
            return 0;
        }

        return Math.Clamp(x / trackWidth * totalFrames, 0, totalFrames);
    }

    public static double MinSpan(long totalFrames) =>
        totalFrames <= 0
            ? 1d
            : Math.Max(1d, totalFrames / WaveformView.TimeZoomMax);

    public static TimeScrollView ResizeLeft(
        double viewStart,
        double viewSpan,
        long totalFrames,
        double newStart)
    {
        if (totalFrames <= 0)
        {
            return new TimeScrollView(0, 1);
        }

        var minSpan = MinSpan(totalFrames);
        var end = Math.Clamp(viewStart + Math.Max(minSpan, viewSpan), minSpan, totalFrames);
        var start = Math.Clamp(newStart, 0, end - minSpan);
        var span = Math.Clamp(end - start, minSpan, (double)totalFrames);
        start = Math.Clamp(end - span, 0, Math.Max(0d, totalFrames - span));
        return new TimeScrollView(start, span);
    }

    public static TimeScrollView ResizeByDelta(
        bool leftEdge,
        double originStart,
        double originSpan,
        long totalFrames,
        double frameDelta)
    {
        return leftEdge
            ? ResizeLeft(originStart, originSpan, totalFrames, originStart + frameDelta)
            : ResizeRight(originStart, originSpan, totalFrames, originStart + originSpan + frameDelta);
    }

    public static TimeScrollView ResizeRight(
        double viewStart,
        double viewSpan,
        long totalFrames,
        double newEnd)
    {
        if (totalFrames <= 0)
        {
            return new TimeScrollView(0, 1);
        }

        var minSpan = MinSpan(totalFrames);
        var start = Math.Clamp(viewStart, 0, Math.Max(0d, totalFrames - minSpan));
        var end = Math.Clamp(newEnd, start + minSpan, totalFrames);
        var span = Math.Clamp(end - start, minSpan, (double)totalFrames);
        start = Math.Clamp(end - span, 0, Math.Max(0d, totalFrames - span));
        return new TimeScrollView(start, span);
    }
}
