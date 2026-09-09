namespace MgaSonicAnvil.Audio;

/// <summary>
/// 一定ゲインを掛けたあとの波形全体 Peak / RMS / Integrated LKFS を先に出す。
/// RMS・Peak は厳密。LKFS は 0.1 秒ブロックをゲイン比で伸ばして再ゲートする。
/// </summary>
internal sealed class WaveformGainAnalyzer
{
    public const double MinGainDb = -60;
    public const double MaxGainDb = 60;
    public const double GainEpsilonDb = 0.0005;

    private const double HopSeconds = 0.1;

    private readonly double[] _blockMs;
    private readonly double[] _blockInRange;
    private readonly double _sumSqOutside;
    private readonly double _sumSqInside;
    private readonly long _sampleCount;
    private readonly float _peakOutside;
    private readonly float _peakInside;

    private WaveformGainAnalyzer(
        double[] blockMs,
        double[] blockInRange,
        double sumSqOutside,
        double sumSqInside,
        long sampleCount,
        float peakOutside,
        float peakInside,
        WaveformGainReading current)
    {
        _blockMs = blockMs;
        _blockInRange = blockInRange;
        _sumSqOutside = sumSqOutside;
        _sumSqInside = sumSqInside;
        _sampleCount = sampleCount;
        _peakOutside = peakOutside;
        _peakInside = peakInside;
        Current = current;
    }

    public WaveformGainReading Current { get; }

    public static double LinearFromDb(double gainDb) =>
        Math.Pow(10d, gainDb / 20d);

    public static double SnapGainDb(double gainDb)
    {
        if (!double.IsFinite(gainDb))
        {
            return 0;
        }

        return Math.Clamp(
            Math.Round(gainDb, 1, MidpointRounding.AwayFromZero),
            MinGainDb,
            MaxGainDb);
    }

    public static bool IsNoOp(double gainDb) =>
        !double.IsFinite(gainDb) || Math.Abs(gainDb) < GainEpsilonDb;

    public static float LinearToDb(double linear)
    {
        if (linear <= 1e-12)
        {
            return float.NegativeInfinity;
        }

        return (float)(20d * Math.Log10(linear));
    }

    public WaveformGainReading Predict(double gainDb)
    {
        if (IsNoOp(gainDb))
        {
            return Current;
        }

        var linear = LinearFromDb(gainDb);
        var gainSq = linear * linear;
        var peak = Math.Max(_peakOutside, (float)(_peakInside * linear));
        var sumSq = _sumSqOutside + _sumSqInside * gainSq;
        var rms = _sampleCount <= 0 ? 0 : Math.Sqrt(sumSq / _sampleCount);
        if (_blockMs.Length == 0)
        {
            return new WaveformGainReading(
                Current.IntegratedLufs,
                LinearToDb(rms),
                LinearToDb(peak));
        }

        var scaled = new double[_blockMs.Length];
        for (var i = 0; i < _blockMs.Length; i++)
        {
            var mix = _blockInRange[i];
            scaled[i] = _blockMs[i] * ((1d - mix) + mix * gainSq);
        }

        return new WaveformGainReading(
            LoudnessMeterEngine.IntegratedFromMeanSquares(scaled),
            LinearToDb(rms),
            LinearToDb(peak));
    }

    public static WaveformGainAnalyzer Build(
        float[] interleaved,
        int channels,
        int sampleRate,
        WaveSelection range)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1000, sampleRate);
        range = range.Clamp(interleaved.Length / channels);
        var frames = interleaved.Length / channels;
        var hopLen = Math.Max(1, (int)Math.Round(sampleRate * HopSeconds));
        var preL = new LoudnessMeterEngine.Biquad();
        var preR = new LoudnessMeterEngine.Biquad();
        var rlbL = new LoudnessMeterEngine.Biquad();
        var rlbR = new LoudnessMeterEngine.Biquad();
        LoudnessMeterEngine.DesignKWeighting(sampleRate, preL, rlbL);
        LoudnessMeterEngine.DesignKWeighting(sampleRate, preR, rlbR);

        var blockMs = new List<double>(Math.Max(1, frames / hopLen + 1));
        var blockMix = new List<double>(blockMs.Capacity);
        var sumSqOutside = 0d;
        var sumSqInside = 0d;
        var peakOutside = 0f;
        var peakInside = 0f;
        var blockSum = 0d;
        var blockCount = 0;
        var inRangeCount = 0;
        var hopLeft = hopLen;

        for (var i = 0; i < frames; i++)
        {
            var origin = i * channels;
            var inside = range.ContainsFrame(i);
            for (var ch = 0; ch < channels; ch++)
            {
                var sample = interleaved[origin + ch];
                var abs = Math.Abs(sample);
                var sq = (double)sample * sample;
                if (inside)
                {
                    if (abs > peakInside)
                    {
                        peakInside = abs;
                    }

                    sumSqInside += sq;
                }
                else
                {
                    if (abs > peakOutside)
                    {
                        peakOutside = abs;
                    }

                    sumSqOutside += sq;
                }
            }

            var left = interleaved[origin];
            var right = channels > 1 ? interleaved[origin + 1] : left;
            var wl = rlbL.Process(preL.Process(left));
            var wr = rlbR.Process(preR.Process(right));
            blockSum += wl * wl + wr * wr;
            blockCount++;
            if (inside)
            {
                inRangeCount++;
            }

            hopLeft--;
            if (hopLeft > 0)
            {
                continue;
            }

            PushBlock(blockMs, blockMix, blockSum, blockCount, inRangeCount);
            blockSum = 0;
            blockCount = 0;
            inRangeCount = 0;
            hopLeft = hopLen;
        }

        if (blockCount > 0)
        {
            PushBlock(blockMs, blockMix, blockSum, blockCount, inRangeCount);
        }

        var sampleCount = (long)interleaved.Length;
        var rms = sampleCount <= 0 ? 0 : Math.Sqrt((sumSqOutside + sumSqInside) / sampleCount);
        var peak = Math.Max(peakOutside, peakInside);
        var current = new WaveformGainReading(
            LoudnessMeterEngine.IntegratedFromMeanSquares(blockMs),
            LinearToDb(rms),
            LinearToDb(peak));
        return new WaveformGainAnalyzer(
            [.. blockMs],
            [.. blockMix],
            sumSqOutside,
            sumSqInside,
            sampleCount,
            peakOutside,
            peakInside,
            current);
    }

    private static void PushBlock(
        List<double> blockMs,
        List<double> blockMix,
        double sum,
        int count,
        int inRangeCount)
    {
        if (count <= 0)
        {
            return;
        }

        blockMs.Add(sum / count);
        blockMix.Add(inRangeCount / (double)count);
    }
}

internal readonly record struct WaveformGainReading(
    float IntegratedLufs,
    float RmsDb,
    float PeakDb);
