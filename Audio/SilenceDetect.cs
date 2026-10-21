using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>無音検出のゲート設定（Cubase の無音部分の検出に相当）。Silent Skip／設定のプチノイズ防止とは独立。</summary>
internal readonly record struct SilenceDetectOptions(
    double OpenThresholdDb,
    double CloseThresholdDb,
    bool LinkThresholds,
    int MinGateMs,
    int MinCloseMs,
    int PreRollMs,
    int PostRollMs,
    bool EdgesOnly,
    int FadeInMs = ClickGuard.DefaultFadeMilliseconds,
    int FadeOutMs = ClickGuard.DefaultFadeMilliseconds,
    // 既定は DefaultHitRisePercent（20）。プライマリ ctor 既定値からは型内 const を参照できない。
    int HitRisePercent = 20)
{
    /// <summary>無音検出のオープンスレッショルド既定（dBFS）。Silent Skip の -60 とは別。</summary>
    public const double DefaultOpenThresholdDb = -5;
    public const int DefaultMinGateMs = 1000;
    public const int DefaultMinCloseMs = 100;
    public const int MinTimingMs = 0;
    public const int MaxTimingMs = 60000;
    public const int MinFadeMs = 0;
    public const int MaxFadeMs = ClickGuard.MaxFadeMilliseconds;
    public const int DefaultFadeMs = ClickGuard.DefaultFadeMilliseconds;
    /// <summary>ヒット検出: 谷からピークへの最小上昇比率（%）。既定 20（旧ハードコード相当）。</summary>
    public const int DefaultHitRisePercent = 20;
    public const int MinHitRisePercent = 5;
    public const int MaxHitRisePercent = 100;

    /// <summary>旧来の無音削除互換。ピークカバー境界そのまま（ゲート結合なし、クローズ即時）。</summary>
    public static SilenceDetectOptions FromThreshold(
        double thresholdDb,
        bool edgesOnly = false,
        int fadeMs = DefaultFadeMs)
    {
        var db = SilentSkip.ClampThresholdDb(thresholdDb);
        fadeMs = ClampFadeMs(fadeMs);
        return new SilenceDetectOptions(
            db,
            db,
            LinkThresholds: true,
            MinGateMs: 0,
            MinCloseMs: 0,
            PreRollMs: 0,
            PostRollMs: 0,
            edgesOnly,
            fadeMs,
            fadeMs);
    }

    public SilenceDetectOptions Normalized()
    {
        var open = SilentSkip.ClampThresholdDb(OpenThresholdDb);
        var close = LinkThresholds
            ? open
            : SilentSkip.ClampThresholdDb(CloseThresholdDb);
        if (!LinkThresholds && close > open)
        {
            close = open;
        }

        return this with
        {
            OpenThresholdDb = open,
            CloseThresholdDb = close,
            MinGateMs = ClampTimingMs(MinGateMs),
            MinCloseMs = ClampTimingMs(MinCloseMs),
            PreRollMs = ClampTimingMs(PreRollMs),
            PostRollMs = ClampTimingMs(PostRollMs),
            FadeInMs = ClampFadeMs(FadeInMs),
            FadeOutMs = ClampFadeMs(FadeOutMs),
            HitRisePercent = ClampHitRisePercent(HitRisePercent),
        };
    }

    public static int ClampTimingMs(int value) =>
        Math.Clamp(value, MinTimingMs, MaxTimingMs);

    public static int ClampFadeMs(int value) =>
        Math.Clamp(value, MinFadeMs, MaxFadeMs);

    public static int ClampHitRisePercent(int value) =>
        Math.Clamp(value, MinHitRisePercent, MaxHitRisePercent);

    /// <summary>ヒット検出に渡す線形比率（0.05〜1.00）。</summary>
    public float HitRiseRatio => ClampHitRisePercent(HitRisePercent) / 100f;

    public static int TimingFrames(int sampleRate, int ms) =>
        (int)((long)Math.Max(1, sampleRate) * ClampTimingMs(ms) / 1000);

    public static int FadeFrames(int sampleRate, int ms)
    {
        ms = ClampFadeMs(ms);
        if (ms <= 0)
        {
            return 0;
        }

        return Math.Max(1, Math.Max(1, sampleRate) * ms / 1000);
    }
}

/// <summary>オープンスレッショルド／クローズ／ゲート時間／プリ・ポストロールで可聴区間を検出する。</summary>
internal static class SilenceDetect
{
    public static List<WaveSelection> CollectKeepSpans(
        float[] interleaved,
        int channels,
        long startFrame,
        long endFrame,
        int sampleRate,
        int soloMask,
        SilenceDetectOptions options)
    {
        options = options.Normalized();
        channels = Math.Max(1, channels);
        var frameCount = interleaved.Length / channels;
        startFrame = Math.Clamp(startFrame, 0, frameCount);
        endFrame = Math.Clamp(endFrame, startFrame, frameCount);
        if (endFrame <= startFrame)
        {
            return [];
        }

        var openLinear = SilentSkip.LinearFromDb(options.OpenThresholdDb);
        var closeLinear = SilentSkip.LinearFromDb(options.CloseThresholdDb);
        var holdFrames = SilentSkip.PeakWindowRadiusFrames(sampleRate);
        var minCloseFrames = Math.Max(1, SilenceDetectOptions.TimingFrames(sampleRate, Math.Max(1, options.MinCloseMs)));
        var minGateFrames = SilenceDetectOptions.TimingFrames(sampleRate, options.MinGateMs);
        var preFrames = SilenceDetectOptions.TimingFrames(sampleRate, options.PreRollMs);
        var postFrames = SilenceDetectOptions.TimingFrames(sampleRate, options.PostRollMs);

        var openCover = SilentSkip.BuildPeakCover(
            interleaved, channels, startFrame, endFrame, openLinear, soloMask, holdFrames);
        var closeCover = options.OpenThresholdDb == options.CloseThresholdDb
            ? openCover
            : SilentSkip.BuildPeakCover(
                interleaved, channels, startFrame, endFrame, closeLinear, soloMask, holdFrames);

        var raw = GateSpans(openCover, closeCover, startFrame, minCloseFrames);
        var merged = MergeByMinGate(raw, minGateFrames);
        // ピーク窓は谷をまたぐためだけに使い、解析範囲の外縁へは広げない。
        // 余白はプリロール／ポストロールだけが足す。
        var snapped = SnapOuterEdgesToSampleThreshold(
            merged,
            interleaved,
            channels,
            soloMask,
            openLinear,
            closeLinear);
        var padded = ApplyRoll(snapped, startFrame, endFrame, preFrames, postFrames);
        if (padded.Count == 0)
        {
            return [];
        }

        if (!options.EdgesOnly)
        {
            return padded;
        }

        return [new WaveSelection(padded[0].StartFrame, padded[^1].EndFrame)];
    }

    public static List<WaveSelection> CollectSilentSpans(
        float[] interleaved,
        int channels,
        long startFrame,
        long endFrame,
        int sampleRate,
        int soloMask,
        SilenceDetectOptions options)
    {
        var keeps = CollectKeepSpans(
            interleaved,
            channels,
            startFrame,
            endFrame,
            sampleRate,
            soloMask,
            options);
        if (keeps.Count == 0)
        {
            return [new WaveSelection(startFrame, endFrame)];
        }

        var silents = new List<WaveSelection>();
        var cursor = startFrame;
        foreach (var keep in keeps)
        {
            if (keep.StartFrame > cursor)
            {
                silents.Add(new WaveSelection(cursor, keep.StartFrame));
            }

            cursor = Math.Max(cursor, keep.EndFrame);
        }

        if (cursor < endFrame)
        {
            silents.Add(new WaveSelection(cursor, endFrame));
        }

        return silents;
    }

    public static float[] CopyKeepRanges(
        float[] interleaved,
        int channels,
        IReadOnlyList<WaveSelection> keeps)
    {
        channels = Math.Max(1, channels);
        if (keeps.Count == 0)
        {
            return [];
        }

        long total = 0;
        foreach (var keep in keeps)
        {
            total += Math.Max(0, keep.Length);
        }

        var dest = new float[total * channels];
        var written = 0;
        foreach (var keep in keeps)
        {
            if (keep.IsEmpty)
            {
                continue;
            }

            var src = (int)(keep.StartFrame * channels);
            var count = (int)(keep.Length * channels);
            if (src < 0 || src + count > interleaved.Length || written + count > dest.Length)
            {
                break;
            }

            Array.Copy(interleaved, src, dest, written, count);
            written += count;
        }

        if (written == dest.Length)
        {
            return dest;
        }

        var trimmed = new float[written];
        Array.Copy(dest, trimmed, written);
        return trimmed;
    }

    private static List<WaveSelection> GateSpans(
        byte[] openCover,
        byte[] closeCover,
        long startFrame,
        int minCloseFrames)
    {
        var spans = new List<WaveSelection>();
        long? openStart = null;
        var below = 0;
        for (var i = 0; i < openCover.Length; i++)
        {
            if (openStart is null)
            {
                if (openCover[i] != 0)
                {
                    openStart = startFrame + i;
                    below = 0;
                }

                continue;
            }

            if (closeCover[i] != 0)
            {
                below = 0;
                continue;
            }

            below++;
            if (below < minCloseFrames)
            {
                continue;
            }

            var end = startFrame + i - minCloseFrames + 1;
            if (end > openStart.Value)
            {
                spans.Add(new WaveSelection(openStart.Value, end));
            }

            openStart = null;
            below = 0;
        }

        if (openStart is long start)
        {
            spans.Add(new WaveSelection(start, startFrame + openCover.Length));
        }

        return spans;
    }

    private static List<WaveSelection> MergeByMinGate(List<WaveSelection> spans, int minGateFrames)
    {
        if (spans.Count == 0)
        {
            return [];
        }

        spans.Sort((a, b) => a.StartFrame.CompareTo(b.StartFrame));
        var merged = new List<WaveSelection>(spans.Count) { spans[0] };
        for (var i = 1; i < spans.Count; i++)
        {
            var last = merged[^1];
            var next = spans[i];
            var gap = next.StartFrame - last.EndFrame;
            if (gap < minGateFrames)
            {
                merged[^1] = new WaveSelection(last.StartFrame, Math.Max(last.EndFrame, next.EndFrame));
            }
            else
            {
                merged.Add(next);
            }
        }

        return merged;
    }

    /// <summary>
    /// 最初の区間の先頭を、そのフレーム自身がオープン以上の位置まで進める。
    /// 最後の区間の末尾を、そのフレーム自身がクローズ以上の位置まで戻す。
    /// 中間の区間境界はピーク窓のまま残す。
    /// </summary>
    private static List<WaveSelection> SnapOuterEdgesToSampleThreshold(
        List<WaveSelection> spans,
        float[] interleaved,
        int channels,
        int soloMask,
        float openLinear,
        float closeLinear)
    {
        if (spans.Count == 0)
        {
            return spans;
        }

        var next = new List<WaveSelection>(spans);
        while (next.Count > 0)
        {
            var first = next[0];
            var start = FirstFrameAtOrAbove(
                interleaved, channels, soloMask, first.StartFrame, first.EndFrame, openLinear);
            if (start < 0)
            {
                next.RemoveAt(0);
                continue;
            }

            if (next.Count == 1)
            {
                var end = LastExclusiveAbove(
                    interleaved, channels, soloMask, start, first.EndFrame, closeLinear);
                if (end <= start)
                {
                    return [];
                }

                next[0] = new WaveSelection(start, end);
                return next;
            }

            next[0] = new WaveSelection(start, first.EndFrame);
            break;
        }

        while (next.Count > 1)
        {
            var last = next[^1];
            var end = LastExclusiveAbove(
                interleaved, channels, soloMask, last.StartFrame, last.EndFrame, closeLinear);
            if (end <= last.StartFrame)
            {
                next.RemoveAt(next.Count - 1);
                continue;
            }

            next[^1] = new WaveSelection(last.StartFrame, end);
            break;
        }

        if (next.Count == 1)
        {
            var only = next[0];
            var end = LastExclusiveAbove(
                interleaved, channels, soloMask, only.StartFrame, only.EndFrame, closeLinear);
            if (end <= only.StartFrame)
            {
                return [];
            }

            next[0] = new WaveSelection(only.StartFrame, end);
        }

        return next;
    }

    private static long FirstFrameAtOrAbove(
        float[] interleaved,
        int channels,
        int soloMask,
        long start,
        long end,
        float threshold)
    {
        for (var frame = start; frame < end; frame++)
        {
            if (SilentSkip.FrameAbsPeak(interleaved, channels, frame, soloMask) >= threshold)
            {
                return frame;
            }
        }

        return -1;
    }

    private static long LastExclusiveAbove(
        float[] interleaved,
        int channels,
        int soloMask,
        long start,
        long end,
        float threshold)
    {
        for (var frame = end - 1; frame >= start; frame--)
        {
            if (SilentSkip.FrameAbsPeak(interleaved, channels, frame, soloMask) >= threshold)
            {
                return frame + 1;
            }
        }

        return start;
    }

    private static List<WaveSelection> ApplyRoll(
        List<WaveSelection> spans,
        long rangeStart,
        long rangeEnd,
        int preFrames,
        int postFrames)
    {
        if (spans.Count == 0)
        {
            return [];
        }

        var rolled = new List<WaveSelection>(spans.Count);
        foreach (var span in spans)
        {
            var start = Math.Max(rangeStart, span.StartFrame - preFrames);
            var end = Math.Min(rangeEnd, span.EndFrame + postFrames);
            if (end > start)
            {
                rolled.Add(new WaveSelection(start, end));
            }
        }

        return MergeByMinGate(rolled, minGateFrames: 0);
    }
}
