using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// MP3 の LAME delay／padding を再生から外す。ヘッダが無いファイルはそのまま。
/// デコーダが既に削っているときは二重に切らない。
/// </summary>
internal static class Mp3Gapless
{
    /// <summary>設定のギャップレス。既定オン。</summary>
    public static bool Enabled { get; set; } = true;

    public static bool TryGetTrim(
        AudioFileTags tags,
        int sampleRate,
        long decodedFrames,
        out long delay,
        out long padding)
    {
        delay = Math.Max(0, tags.EncoderDelayFrames);
        padding = Math.Max(0, tags.EncoderPaddingFrames);
        if ((delay <= 0 && padding <= 0) || decodedFrames <= delay + padding + 1)
        {
            delay = 0;
            padding = 0;
            return false;
        }

        if (sampleRate > 0 && tags.DurationSeconds > 0)
        {
            var tagged = (long)Math.Round(tags.DurationSeconds * sampleRate);
            var stripped = tagged - delay - padding;
            if (tagged > 0
                && Math.Abs(decodedFrames - stripped) <= 64
                && Math.Abs(decodedFrames - tagged) > 64)
            {
                delay = 0;
                padding = 0;
                return false;
            }
        }

        return true;
    }

    public static WaveSelection? ResolveWindow(
        AudioDocument? document,
        long frameCount,
        WaveSelection? playRange)
    {
        if (!Enabled
            || document is null
            || !TryGetTrim(document.Tags, document.SampleRate, frameCount, out var delay, out var padding))
        {
            return playRange;
        }

        var start = delay;
        var end = frameCount - padding;
        if (end <= start)
        {
            return playRange;
        }

        if (playRange is { IsEmpty: false } range)
        {
            var a = Math.Max(range.StartFrame, start);
            var b = Math.Min(range.EndFrame, end);
            if (b > a)
            {
                return new WaveSelection(a, b);
            }
        }

        return new WaveSelection(start, end);
    }

    public static long ClampStart(long startFrame, WaveSelection? window, long frameCount)
    {
        var begin = 0L;
        var end = Math.Max(0, frameCount);
        if (window is { IsEmpty: false } range)
        {
            begin = range.StartFrame;
            end = range.EndFrame;
        }

        if (end <= begin)
        {
            return Math.Clamp(startFrame, 0, Math.Max(0, frameCount));
        }

        if (startFrame < begin)
        {
            return begin;
        }

        return startFrame >= end ? Math.Max(begin, end - 1) : startFrame;
    }
}
