using System.Globalization;

namespace MgaSonicAnvil.Audio;

/// <summary>ITU-R BS.1770 / EBU R128 の表示用計測。再生出力をリアルタイムに積む。</summary>
internal sealed class LoudnessMeterEngine
{
    public const double DefaultTargetLufs = -24;
    public const double MinTargetLufs = -70;
    public const double MaxTargetLufs = 0;

    private const double AbsoluteGateLufs = -70;
    private const double RelativeGateLu = -10;
    private const double MomentarySeconds = 0.4;
    private const double ShortTermSeconds = 3.0;
    private const double MomentaryHopSeconds = 0.1;

    private readonly Biquad _preL = new();
    private readonly Biquad _preR = new();
    private readonly Biquad _rlbL = new();
    private readonly Biquad _rlbR = new();
    private readonly List<double> _integratedBlocks = [];
    private readonly List<double> _shortTermHistory = [];
    private double[] _momentaryRing = [0];
    private double[] _shortRing = [0];
    private int _momWrite;
    private int _momCount;
    private int _shortWrite;
    private int _shortCount;

    private int _sampleRate = 48000;
    private int _momentaryLen = 1;
    private int _shortLen = 1;
    private int _hopLen = 1;
    private int _hopLeft = 1;
    private double _momentarySum;
    private double _shortSum;
    private double _blockSum;
    private int _blockCount;
    private float _momentaryMax = float.NegativeInfinity;
    private float _truePeak;
    private float _prevL;
    private float _prevR;
    private float _prev2L;
    private float _prev2R;

    public double TargetLufs { get; set; } = DefaultTargetLufs;

    public static double ClampTargetLufs(double value) =>
        Math.Clamp(value, MinTargetLufs, MaxTargetLufs);

    public static bool TryParseTargetLufs(string? text, out double lufs)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            lufs = DefaultTargetLufs;
            return false;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
        {
            lufs = DefaultTargetLufs;
            return false;
        }

        if (parsed < MinTargetLufs || parsed > MaxTargetLufs || double.IsNaN(parsed) || double.IsInfinity(parsed))
        {
            lufs = DefaultTargetLufs;
            return false;
        }

        lufs = parsed;
        return true;
    }

    public LoudnessSnapshot Snapshot { get; private set; } = LoudnessSnapshot.Idle;

    public LoudnessMeterEngine() => Configure(48000);

    public void Reset()
    {
        _preL.Reset();
        _preR.Reset();
        _rlbL.Reset();
        _rlbR.Reset();
        _integratedBlocks.Clear();
        _shortTermHistory.Clear();
        Array.Clear(_momentaryRing);
        Array.Clear(_shortRing);
        _momWrite = 0;
        _momCount = 0;
        _shortWrite = 0;
        _shortCount = 0;
        _momentarySum = 0;
        _shortSum = 0;
        _blockSum = 0;
        _blockCount = 0;
        _momentaryMax = float.NegativeInfinity;
        _truePeak = 0;
        _prevL = 0;
        _prevR = 0;
        _prev2L = 0;
        _prev2R = 0;
        _hopLeft = _hopLen;
        Snapshot = LoudnessSnapshot.Idle;
    }

    public LoudnessSnapshot Process(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int frames, int sampleRate)
    {
        if (sampleRate < 1000)
        {
            return Snapshot;
        }

        if (sampleRate != _sampleRate)
        {
            Configure(sampleRate);
        }

        var n = Math.Min(frames, Math.Min(left.Length, right.Length));
        for (var i = 0; i < n; i++)
        {
            var l = left[i];
            var r = right[i];
            UpdateTruePeak(l, r);
            var wl = _rlbL.Process(_preL.Process(l));
            var wr = _rlbR.Process(_preR.Process(r));
            var ms = wl * wl + wr * wr;
            PushMeanSquare(ms);
        }

        var momentary = MomentaryLufs();
        if (momentary > _momentaryMax)
        {
            _momentaryMax = momentary;
        }

        Snapshot = new LoudnessSnapshot(
            momentary,
            ShortTermLufs(),
            IntegratedLufs(),
            _momentaryMax,
            LoudnessRange(),
            TruePeakDb(),
            TargetLufs);
        return Snapshot;
    }

    private void Configure(int sampleRate)
    {
        _sampleRate = sampleRate;
        DesignKWeighting(sampleRate, _preL, _rlbL);
        DesignKWeighting(sampleRate, _preR, _rlbR);
        _momentaryLen = Math.Max(1, (int)Math.Round(sampleRate * MomentarySeconds));
        _shortLen = Math.Max(1, (int)Math.Round(sampleRate * ShortTermSeconds));
        _hopLen = Math.Max(1, (int)Math.Round(sampleRate * MomentaryHopSeconds));
        _hopLeft = _hopLen;
        _momentaryRing = new double[_momentaryLen];
        _shortRing = new double[_shortLen];
        _momWrite = 0;
        _momCount = 0;
        _shortWrite = 0;
        _shortCount = 0;
        _momentarySum = 0;
        _shortSum = 0;
        _momentaryMax = float.NegativeInfinity;
    }

    private void PushMeanSquare(double ms)
    {
        PushRing(ref _momentaryRing, ref _momWrite, ref _momCount, ref _momentarySum, _momentaryLen, ms);
        PushRing(ref _shortRing, ref _shortWrite, ref _shortCount, ref _shortSum, _shortLen, ms);

        _blockSum += ms;
        _blockCount++;
        _hopLeft--;
        if (_hopLeft > 0)
        {
            return;
        }

        _hopLeft = _hopLen;
        if (_blockCount > 0)
        {
            var block = _blockSum / _blockCount;
            _integratedBlocks.Add(block);
            _blockSum = 0;
            _blockCount = 0;
        }

        if (_shortCount >= _shortLen)
        {
            _shortTermHistory.Add(_shortSum / _shortCount);
        }
    }

    private static void PushRing(
        ref double[] ring,
        ref int write,
        ref int count,
        ref double sum,
        int length,
        double value)
    {
        if (count == length)
        {
            sum -= ring[write];
        }
        else
        {
            count++;
        }

        ring[write] = value;
        sum += value;
        write++;
        if (write >= length)
        {
            write = 0;
        }
    }

    private void UpdateTruePeak(float left, float right)
    {
        ConsiderPeak(left);
        ConsiderPeak(right);
        for (var t = 1; t <= 3; t++)
        {
            var u = t / 4f;
            ConsiderPeak(Hermite(_prev2L, _prevL, left, left, u));
            ConsiderPeak(Hermite(_prev2R, _prevR, right, right, u));
        }

        _prev2L = _prevL;
        _prev2R = _prevR;
        _prevL = left;
        _prevR = right;
    }

    private void ConsiderPeak(float sample)
    {
        var abs = Math.Abs(sample);
        if (abs > _truePeak)
        {
            _truePeak = abs;
        }
    }

    private float MomentaryLufs() =>
        MeanSquareToLufs(_momCount == 0 ? 0 : _momentarySum / _momCount);

    private float ShortTermLufs() =>
        MeanSquareToLufs(_shortCount == 0 ? 0 : _shortSum / _shortCount);

    private float IntegratedLufs()
    {
        if (_integratedBlocks.Count == 0)
        {
            return float.NegativeInfinity;
        }

        var absMs = LufsToMeanSquare(AbsoluteGateLufs);
        var passed = 0;
        var sum = 0d;
        foreach (var block in _integratedBlocks)
        {
            if (block < absMs)
            {
                continue;
            }

            sum += block;
            passed++;
        }

        if (passed == 0)
        {
            return float.NegativeInfinity;
        }

        var relative = MeanSquareToLufs(sum / passed) + RelativeGateLu;
        var relMs = LufsToMeanSquare(relative);
        sum = 0;
        passed = 0;
        foreach (var block in _integratedBlocks)
        {
            if (block < relMs)
            {
                continue;
            }

            sum += block;
            passed++;
        }

        return passed == 0 ? float.NegativeInfinity : MeanSquareToLufs(sum / passed);
    }

    private float LoudnessRange()
    {
        if (_shortTermHistory.Count < 2)
        {
            return float.NaN;
        }

        var absMs = LufsToMeanSquare(AbsoluteGateLufs);
        var gated = new List<double>(_shortTermHistory.Count);
        foreach (var ms in _shortTermHistory)
        {
            if (ms >= absMs)
            {
                gated.Add(ms);
            }
        }

        if (gated.Count < 2)
        {
            return float.NaN;
        }

        var integrated = IntegratedLufs();
        if (float.IsInfinity(integrated))
        {
            return float.NaN;
        }

        var relMs = LufsToMeanSquare(integrated - 20);
        gated.RemoveAll(ms => ms < relMs);
        if (gated.Count < 2)
        {
            return float.NaN;
        }

        gated.Sort();
        var lo = gated[(int)Math.Floor((gated.Count - 1) * 0.10)];
        var hi = gated[(int)Math.Floor((gated.Count - 1) * 0.95)];
        return MeanSquareToLufs(hi) - MeanSquareToLufs(lo);
    }

    private float TruePeakDb() =>
        _truePeak <= 1e-12f ? float.NegativeInfinity : (float)(20d * Math.Log10(_truePeak));

    /// <summary>ファイル全体の Short Term（3 秒窓、0.1 秒 hop）。</summary>
    public static LoudnessProfile BuildShortTermProfile(
        float[] interleaved,
        int channels,
        int sampleRate)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1000, sampleRate);
        var hop = Math.Max(1, (int)Math.Round(sampleRate * MomentaryHopSeconds));
        var shortLen = Math.Max(1, (int)Math.Round(sampleRate * ShortTermSeconds));
        var frames = interleaved.Length / channels;
        var count = Math.Max(1, (int)Math.Ceiling(Math.Max(frames, 1) / (double)hop));
        var values = new float[count];
        Array.Fill(values, float.NegativeInfinity);
        if (frames <= 0)
        {
            return new LoudnessProfile(sampleRate, hop, values);
        }

        var preL = new Biquad();
        var preR = new Biquad();
        var rlbL = new Biquad();
        var rlbR = new Biquad();
        DesignKWeighting(sampleRate, preL, rlbL);
        DesignKWeighting(sampleRate, preR, rlbR);

        var ring = new double[shortLen];
        var write = 0;
        var filled = 0;
        var sum = 0d;
        var hopLeft = hop;
        var outIndex = 0;

        for (var i = 0; i < frames; i++)
        {
            var origin = i * channels;
            var left = interleaved[origin];
            var right = channels > 1 ? interleaved[origin + 1] : left;
            var ms = rlbL.Process(preL.Process(left));
            var rs = rlbR.Process(preR.Process(right));
            var power = ms * ms + rs * rs;
            if (filled == shortLen)
            {
                sum -= ring[write];
            }
            else
            {
                filled++;
            }

            ring[write] = power;
            sum += power;
            write++;
            if (write >= shortLen)
            {
                write = 0;
            }

            hopLeft--;
            if (hopLeft > 0)
            {
                continue;
            }

            hopLeft = hop;
            if (outIndex < values.Length)
            {
                values[outIndex] = filled == 0
                    ? float.NegativeInfinity
                    : MeanSquareToLufs(sum / filled);
                outIndex++;
            }
        }

        if (outIndex < values.Length && filled > 0)
        {
            values[outIndex] = MeanSquareToLufs(sum / filled);
        }

        return new LoudnessProfile(sampleRate, hop, values);
    }

    public static float MeanSquareToLufs(double meanSquare)
    {
        if (meanSquare <= 1e-20)
        {
            return float.NegativeInfinity;
        }

        return (float)(-0.691 + 10d * Math.Log10(meanSquare));
    }

    public static double LufsToMeanSquare(double lufs) =>
        Math.Pow(10d, (lufs + 0.691) / 10d);

    private static float Hermite(float p0, float p1, float p2, float p3, float t)
    {
        var c0 = p1;
        var c1 = 0.5f * (p2 - p0);
        var c2 = p0 - 2.5f * p1 + 2f * p2 - 0.5f * p3;
        var c3 = 0.5f * (p3 - p0) + 1.5f * (p1 - p2);
        return ((c3 * t + c2) * t + c1) * t + c0;
    }

    private static void DesignKWeighting(int sampleRate, Biquad pre, Biquad rlb)
    {
        var fs = (double)sampleRate;
        var db = 3.999843853973347;
        var f0 = 1681.974450955533;
        var q = 0.7071752369554196;
        var k = Math.Tan(Math.PI * f0 / fs);
        var vh = Math.Pow(10d, db / 20d);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1d + k / q + k * k;
        pre.Set(
            (vh + vb * k / q + k * k) / a0,
            2d * (k * k - vh) / a0,
            (vh - vb * k / q + k * k) / a0,
            2d * (k * k - 1d) / a0,
            (1d - k / q + k * k) / a0);

        f0 = 38.13547087602444;
        q = 0.5003270373238773;
        k = Math.Tan(Math.PI * f0 / fs);
        a0 = 1d + k / q + k * k;
        rlb.Set(1d / a0, -2d / a0, 1d / a0, 2d * (k * k - 1d) / a0, (1d - k / q + k * k) / a0);
    }

    private sealed class Biquad
    {
        private double _b0 = 1, _b1, _b2, _a1, _a2, _z1, _z2;

        public void Set(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
            Reset();
        }

        public void Reset()
        {
            _z1 = 0;
            _z2 = 0;
        }

        public float Process(float x)
        {
            var y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return (float)y;
        }
    }
}

internal readonly record struct LoudnessSnapshot(
    float MomentaryLufs,
    float ShortTermLufs,
    float IntegratedLufs,
    float MomentaryMaxLufs,
    float LoudnessRangeLu,
    float TruePeakDb,
    double TargetLufs)
{
    public static LoudnessSnapshot Idle { get; } = new(
        float.NegativeInfinity,
        float.NegativeInfinity,
        float.NegativeInfinity,
        float.NegativeInfinity,
        float.NaN,
        float.NegativeInfinity,
        LoudnessMeterEngine.DefaultTargetLufs);
}

internal enum LoudnessTraffic
{
    Idle,
    Safe,
    Caution,
    Danger,
}

/// <summary>表示色用。安全＝余裕、注意＝接近、危険＝超過。</summary>
internal static class LoudnessTrafficLight
{
    public const double LufsApproachLu = 1;
    public const double TruePeakCautionDb = -1;
    public const double TruePeakLimitDb = 0;
    public const double LraCautionLu = 20;
    public const double LraLimitLu = 25;

    public const byte SafeR = 0x3A;
    public const byte SafeG = 0xB8;
    public const byte SafeB = 0xE8;
    public const byte CautionR = 0xFF;
    public const byte CautionG = 0x8A;
    public const byte CautionB = 0x1A;
    public const byte DangerR = 0xFF;
    public const byte DangerG = 0x6E;
    public const byte DangerB = 0x6E;

    public static void Rgb(LoudnessTraffic traffic, out byte r, out byte g, out byte b)
    {
        switch (traffic)
        {
            case LoudnessTraffic.Caution:
                r = CautionR;
                g = CautionG;
                b = CautionB;
                return;
            case LoudnessTraffic.Danger:
                r = DangerR;
                g = DangerG;
                b = DangerB;
                return;
            default:
                r = SafeR;
                g = SafeG;
                b = SafeB;
                return;
        }
    }

    public static LoudnessTraffic ForLufs(float value, double target)
    {
        if (!IsMeasurable(value))
        {
            return LoudnessTraffic.Idle;
        }

        if (value > target)
        {
            return LoudnessTraffic.Danger;
        }

        if (value > target - LufsApproachLu && value < target)
        {
            return LoudnessTraffic.Caution;
        }

        return LoudnessTraffic.Safe;
    }

    public static LoudnessTraffic ForTruePeak(float valueDb)
    {
        if (!IsMeasurable(valueDb))
        {
            return LoudnessTraffic.Idle;
        }

        if (valueDb >= TruePeakLimitDb)
        {
            return LoudnessTraffic.Danger;
        }

        if (valueDb >= TruePeakCautionDb)
        {
            return LoudnessTraffic.Caution;
        }

        return LoudnessTraffic.Safe;
    }

    public static LoudnessTraffic ForLra(float valueLu)
    {
        if (!IsMeasurable(valueLu))
        {
            return LoudnessTraffic.Idle;
        }

        if (valueLu > LraLimitLu)
        {
            return LoudnessTraffic.Danger;
        }

        if (valueLu > LraCautionLu)
        {
            return LoudnessTraffic.Caution;
        }

        return LoudnessTraffic.Safe;
    }

    private static bool IsMeasurable(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);
}
