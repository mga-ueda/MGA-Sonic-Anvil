using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// ヒットポイント（トランジェント）検出。
/// 無音ゲートの可聴区間先頭とは別で、しきい値を超え、手前の谷から持ち上がった山に打点を置く。
/// </summary>
internal static class HitPointDetect
{
    /// <summary>同じ打点の二重検出を避ける下限（ミリ秒）。指定間隔がこれより短くてもこれ未満にはしない。</summary>
    public const int FloorGapMs = 10;

    public static List<long> CollectFrames(
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
        sampleRate = Math.Max(1, sampleRate);
        var frameCount = interleaved.Length / channels;
        startFrame = Math.Clamp(startFrame, 0, frameCount);
        endFrame = Math.Clamp(endFrame, startFrame, frameCount);
        if (endFrame <= startFrame)
        {
            return [];
        }

        var hop = Math.Max(1, sampleRate / 1000);
        var hops = (int)((endFrame - startFrame + hop - 1) / hop);
        if (hops <= 0)
        {
            return [];
        }

        var env = new float[hops];
        var peakFrame = new long[hops];
        for (var i = 0; i < hops; i++)
        {
            var hopStart = startFrame + (long)i * hop;
            var hopEnd = Math.Min(endFrame, hopStart + hop);
            float peak = 0;
            var at = hopStart;
            for (var frame = hopStart; frame < hopEnd; frame++)
            {
                var abs = PeakAbs(interleaved, channels, frame, soloMask);
                if (abs >= peak)
                {
                    peak = abs;
                    at = frame;
                }
            }

            env[i] = peak;
            peakFrame[i] = at;
        }

        var open = SilentSkip.LinearFromDb(options.OpenThresholdDb);
        var riseRatio = options.HitRiseRatio;
        var lookback = Math.Max(1, (sampleRate / 25) / hop);
        var minGap = Math.Max(
            SilenceDetectOptions.TimingFrames(sampleRate, options.MinGateMs),
            SilenceDetectOptions.TimingFrames(sampleRate, FloorGapMs));
        var hits = new List<long>();
        long last = long.MinValue / 4;
        for (var i = 0; i < hops; i++)
        {
            if (env[i] < open || !IsLocalPeak(env, i) || !RisesFromDip(env, i, lookback, riseRatio))
            {
                continue;
            }

            var frame = peakFrame[i];
            if (frame < startFrame || frame >= endFrame || frame - last < minGap)
            {
                continue;
            }

            hits.Add(frame);
            last = frame;
        }

        return hits;
    }

    private static float PeakAbs(float[] interleaved, int channels, long frame, int soloMask)
    {
        var index = (int)(frame * channels);
        if (index < 0 || index + channels > interleaved.Length)
        {
            return 0;
        }

        float peak = 0;
        for (var ch = 0; ch < channels; ch++)
        {
            if (!ChannelSolo.Contains(soloMask, ch))
            {
                continue;
            }

            var abs = Math.Abs(interleaved[index + ch]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        return peak;
    }

    private static bool IsLocalPeak(float[] env, int i)
    {
        var prev = i == 0 ? 0f : env[i - 1];
        if (i + 1 >= env.Length)
        {
            return env[i] > prev;
        }

        return env[i] >= prev && env[i] > env[i + 1];
    }

    /// <summary>平坦な持続音は谷と山がほぼ同じなので捨て、打点らしい立ち上がりだけ残す。</summary>
    private static bool RisesFromDip(float[] env, int i, int lookback, float riseRatio)
    {
        var from = Math.Max(0, i - lookback);
        var dip = env[i];
        for (var j = from; j < i; j++)
        {
            if (env[j] < dip)
            {
                dip = env[j];
            }
        }

        if (i == 0)
        {
            dip = 0;
        }

        return env[i] - dip >= env[i] * riseRatio;
    }
}
