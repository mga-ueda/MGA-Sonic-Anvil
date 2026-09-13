using System.Globalization;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>ピークがしきい値未満の区間を、再生では飛ばし、録音では書き込まない。録音では長い無音を pad に縮める。しきい値は dBFS。</summary>
internal static class SilentSkip
{
    public const double DefaultThresholdDb = -60;
    public const double MinThresholdDb = -120;
    public const double MaxThresholdDb = 0;
    public const int DefaultRecordPadMs = 500;
    public const int MinRecordPadMs = 0;
    public const int MaxRecordPadMs = 10000;

    public static double ClampThresholdDb(double value) =>
        Math.Clamp(value, MinThresholdDb, MaxThresholdDb);

    public static bool TryParseThresholdDb(string? text, out double db)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            db = DefaultThresholdDb;
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            db = DefaultThresholdDb;
            return false;
        }

        if (parsed < MinThresholdDb || parsed > MaxThresholdDb || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            db = DefaultThresholdDb;
            return false;
        }

        db = parsed;
        return true;
    }

    public static int ClampRecordPadMs(int value) =>
        Math.Clamp(value, MinRecordPadMs, MaxRecordPadMs);

    public static bool TryParseRecordPadMs(string? text, out int ms)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            ms = DefaultRecordPadMs;
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            ms = DefaultRecordPadMs;
            return false;
        }

        if (parsed < MinRecordPadMs || parsed > MaxRecordPadMs)
        {
            ms = DefaultRecordPadMs;
            return false;
        }

        ms = parsed;
        return true;
    }

    public static int RecordPadFrames(int sampleRate, int padMs)
    {
        padMs = ClampRecordPadMs(padMs);
        sampleRate = Math.Clamp(sampleRate, 1, 384000);
        return (int)((long)sampleRate * padMs / 1000);
    }

    public static float LinearFromDb(double db)
    {
        db = ClampThresholdDb(db);
        return (float)Math.Pow(10, db / 20d);
    }

    public static string FormatPeakDb(double db)
    {
        if (!double.IsFinite(db) || db <= MinThresholdDb)
        {
            return MinThresholdDb.ToString("0.0", CultureInfo.InvariantCulture);
        }

        return Math.Min(MaxThresholdDb, db).ToString("0.0", CultureInfo.InvariantCulture);
    }

    /// <summary>しきい値メーター用。短区間ピークの最小を集めるブロック長。</summary>
    public const int FloorBlockFrames = 256;

    public static void NoteFloorAbs(
        float abs,
        ref float blockPeak,
        ref int blockFrames,
        ref float intervalMin,
        ref bool hasMin)
    {
        if (abs > blockPeak)
        {
            blockPeak = abs;
        }

        blockFrames++;
        if (blockFrames >= FloorBlockFrames)
        {
            CommitFloorBlock(ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin);
        }
    }

    public static void CommitFloorBlock(
        ref float blockPeak,
        ref int blockFrames,
        ref float intervalMin,
        ref bool hasMin)
    {
        if (blockFrames <= 0)
        {
            return;
        }

        if (!hasMin || blockPeak < intervalMin)
        {
            intervalMin = blockPeak;
        }

        hasMin = true;
        blockPeak = 0;
        blockFrames = 0;
    }

    public static float TakeFloor(
        ref float blockPeak,
        ref int blockFrames,
        ref float intervalMin,
        ref bool hasMin)
    {
        CommitFloorBlock(ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin);
        var value = hasMin ? intervalMin : 0f;
        intervalMin = 0;
        hasMin = false;
        return value;
    }

    public static bool IsFrameSilent(
        float[] interleaved,
        int channels,
        long frame,
        float thresholdLinear,
        int soloMask)
    {
        channels = Math.Max(1, channels);
        var start = frame * channels;
        if (interleaved.Length == 0 || start < 0 || start + channels > interleaved.Length)
        {
            return true;
        }

        var floor = Math.Max(0f, thresholdLinear);
        for (var ch = 0; ch < channels; ch++)
        {
            if (!ChannelSolo.Contains(soloMask, ch))
            {
                continue;
            }

            if (Math.Abs(interleaved[start + ch]) >= floor)
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsSpanSilent(ReadOnlySpan<float> frame, float thresholdLinear)
    {
        var floor = Math.Max(0f, thresholdLinear);
        for (var i = 0; i < frame.Length; i++)
        {
            if (Math.Abs(frame[i]) >= floor)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>しきい値以上のフレームだけ dest へ詰める。書いたサンプル数。</summary>
    public static int CopyAudibleFrames(
        ReadOnlySpan<float> interleaved,
        Span<float> dest,
        int channels,
        float thresholdLinear)
    {
        var hasWritten = false;
        return CopyAudibleFrames(
            interleaved,
            dest,
            channels,
            thresholdLinear,
            padFrames: 0,
            ref hasWritten);
    }

    /// <summary>
    /// 録音ゲート。しきい値を下回った時点から無音を書き、pad で止める。
    /// 書いたサンプル数。
    /// </summary>
    public static int CopyAudibleFrames(
        ReadOnlySpan<float> interleaved,
        Span<float> dest,
        int channels,
        float thresholdLinear,
        int padFrames,
        ref bool hasWritten)
    {
        channels = Math.Max(1, channels);
        var gate = new SilentSkipRecordGate();
        gate.Configure(enabled: true, thresholdLinear, padFrames, channels);
        gate.Reset(hasWritten);
        var frames = interleaved.Length / channels;
        var written = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            if (dest.Length - written < channels)
            {
                break;
            }

            var src = interleaved.Slice(frame * channels, channels);
            written += gate.ProcessFrame(src, dest[written..]);
        }

        written = Math.Max(0, written - gate.CloseTake());
        hasWritten = gate.HasWritten;
        return written;
    }

    /// <summary>[startFrame, endFrame) で最初の可聴フレーム。無ければ endFrame。</summary>
    public static long FindNextAudible(
        float[] interleaved,
        int channels,
        long startFrame,
        long endFrame,
        float thresholdLinear,
        int soloMask)
    {
        channels = Math.Max(1, channels);
        var frameCount = interleaved.Length / channels;
        startFrame = Math.Clamp(startFrame, 0, frameCount);
        endFrame = Math.Clamp(endFrame, startFrame, frameCount);
        for (var frame = startFrame; frame < endFrame; frame++)
        {
            if (!IsFrameSilent(interleaved, channels, frame, thresholdLinear, soloMask))
            {
                return frame;
            }
        }

        return endFrame;
    }

    /// <summary>Silent Skip 録音で、今回書いた区間。無ければ null。</summary>
    public static WaveSelection? RecordTakeRegion(long takeStartFrame, long frameCount)
    {
        if (frameCount <= takeStartFrame)
        {
            return null;
        }

        return new WaveSelection(takeStartFrame, frameCount);
    }

    /// <summary>pad を挟み切った可聴／無音。短い谷は可聴に残す。</summary>
    public static RecordedSpan[] SplitInsertedSpans(
        ReadOnlySpan<float> interleaved,
        int channels,
        float thresholdLinear,
        int padFrames,
        bool hasWritten = false)
    {
        channels = Math.Max(1, channels);
        var gate = new SilentSkipRecordGate();
        gate.Configure(enabled: true, thresholdLinear, padFrames, channels);
        gate.Reset(hasWritten);
        var dest = new float[interleaved.Length];
        var frames = interleaved.Length / channels;
        var written = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            if (dest.Length - written < channels)
            {
                break;
            }

            written += gate.ProcessFrame(interleaved.Slice(frame * channels, channels), dest.AsSpan(written));
        }

        _ = gate.CloseTake();
        return gate.SnapshotWrittenSpans();
    }

    /// <summary>挟んだ pad と、それに区切られた可聴それぞれをリージョンにする。</summary>
    public static WaveRegion[] RecordPartRegions(
        IReadOnlyList<RecordedSpan> spans,
        long takeStartFrame)
    {
        if (spans.Count == 0)
        {
            return [];
        }

        var regions = new WaveRegion[spans.Count];
        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            regions[i] = new WaveRegion(
                takeStartFrame + span.StartFrame,
                takeStartFrame + span.EndFrame,
                span.Silent ? UiStrings.RegionNameRecordSilence : UiStrings.RegionNameRecordAudio);
        }

        return regions;
    }
}

internal readonly record struct RecordedSpan(long StartFrame, long EndFrame, bool Silent);
