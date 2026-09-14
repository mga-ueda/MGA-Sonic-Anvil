using System.Globalization;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 継ぎ目のプチノイズだけを消す短いフェード。先頭は 0 から、終端は 0 へ。
/// 長い音楽用フェードには使わない。
/// </summary>
internal static class ClickGuard
{
    public const int DefaultFadeMilliseconds = 20;
    public const int MinFadeMilliseconds = 1;
    public const int MaxFadeMilliseconds = 100;

    public static int ClampFadeMs(int value) =>
        Math.Clamp(value, MinFadeMilliseconds, MaxFadeMilliseconds);

    public static bool TryParseFadeMs(string? text, out int ms)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            ms = DefaultFadeMilliseconds;
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            ms = DefaultFadeMilliseconds;
            return false;
        }

        if (parsed < MinFadeMilliseconds || parsed > MaxFadeMilliseconds)
        {
            ms = DefaultFadeMilliseconds;
            return false;
        }

        ms = parsed;
        return true;
    }

    public static int FadeFrames(int sampleRate, int milliseconds = DefaultFadeMilliseconds)
    {
        milliseconds = ClampFadeMs(milliseconds);
        return Math.Max(1, Math.Max(1, sampleRate) * milliseconds / 1000);
    }

    public static void FadeRangeEdges(
        float[] interleaved,
        int channels,
        long bufferStartFrame,
        WaveSelection range,
        int mask,
        bool fadeIn,
        bool fadeOut,
        int fadeFrames)
    {
        if (!fadeIn && !fadeOut || fadeFrames <= 0 || interleaved.Length == 0 || channels < 1 || range.IsEmpty)
        {
            return;
        }

        var length = (int)range.Length;
        var offset = (int)(range.StartFrame - bufferStartFrame);
        if (length <= 0 || offset >= interleaved.Length / channels)
        {
            return;
        }

        var edge = Math.Min(Math.Max(1, fadeFrames), length);
        for (var frame = 0; frame < length; frame++)
        {
            var gain = 1f;
            if (fadeIn && frame < edge)
            {
                gain *= EdgeGain(frame, edge, fadeIn: true);
            }

            if (fadeOut && frame >= length - edge)
            {
                gain *= EdgeGain(frame - (length - edge), edge, fadeIn: false);
            }

            if (gain >= 1f)
            {
                continue;
            }

            var index = (offset + frame) * channels;
            if ((uint)index >= (uint)interleaved.Length)
            {
                break;
            }

            var count = Math.Min(channels, interleaved.Length - index);
            for (var ch = 0; ch < count; ch++)
            {
                if (ChannelSolo.Contains(mask, ch))
                {
                    interleaved[index + ch] *= gain;
                }
            }
        }
    }

    public static void FadePackedRuns(
        float[] packed,
        int channels,
        IReadOnlyList<WaveSelection> runs,
        long rangeStart,
        long rangeEnd,
        int fadeFrames)
    {
        var dest = 0L;
        foreach (var run in runs)
        {
            if (run.IsEmpty)
            {
                continue;
            }

            FadeRangeEdges(
                packed,
                channels,
                bufferStartFrame: 0,
                new WaveSelection(dest, dest + run.Length),
                mask: 0,
                fadeIn: run.StartFrame > rangeStart,
                fadeOut: run.EndFrame < rangeEnd,
                fadeFrames);
            dest += run.Length;
        }
    }

    public static void FadeRecordedAudio(
        float[] take,
        int channels,
        IReadOnlyList<RecordedSpan> spans,
        int takeSampleRate,
        int spanSampleRate,
        bool fadeOpenTail,
        int fadeMilliseconds = DefaultFadeMilliseconds)
    {
        channels = Math.Max(1, channels);
        var takeFrames = take.Length / channels;
        if (takeFrames <= 0 || spans.Count == 0)
        {
            return;
        }

        var fadeFrames = FadeFrames(takeSampleRate, fadeMilliseconds);
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (span.Silent || span.EndFrame <= span.StartFrame)
            {
                continue;
            }

            var start = MapFrame(span.StartFrame, spanSampleRate, takeSampleRate);
            var end = MapFrame(span.EndFrame, spanSampleRate, takeSampleRate);
            var range = new WaveSelection(start, end).Clamp(takeFrames);
            if (range.IsEmpty)
            {
                continue;
            }

            var openTail = !fadeOpenTail && i == spans.Count - 1;
            FadeRangeEdges(
                take,
                channels,
                bufferStartFrame: 0,
                range,
                mask: 0,
                fadeIn: true,
                fadeOut: !openTail,
                fadeFrames);
        }
    }

    private static float EdgeGain(int index, int edgeFrames, bool fadeIn)
    {
        if (edgeFrames <= 1)
        {
            return 0f;
        }

        var t = (float)(index / (double)(edgeFrames - 1));
        return fadeIn ? t : 1f - t;
    }

    private static long MapFrame(long frame, int sourceRate, int destRate) =>
        sourceRate > 0 && destRate > 0 && sourceRate != destRate
            ? (long)Math.Round(frame * (double)destRate / sourceRate)
            : frame;
}
