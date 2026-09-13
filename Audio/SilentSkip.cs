using System.Globalization;

namespace MgaSonicAnvil.Audio;

/// <summary>再生中にピークがしきい値未満の区間を飛ばす。しきい値は dBFS。</summary>
internal static class SilentSkip
{
    public const double DefaultThresholdDb = -60;
    public const double MinThresholdDb = -120;
    public const double MaxThresholdDb = 0;

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

    public static float LinearFromDb(double db)
    {
        db = ClampThresholdDb(db);
        return (float)Math.Pow(10, db / 20d);
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
}
