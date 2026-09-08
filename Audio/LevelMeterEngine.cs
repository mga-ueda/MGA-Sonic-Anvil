namespace MgaSonicAnvil.Audio;

/// <summary>MGA Layer Music Checker のマスターメーター計算を移植。</summary>
internal sealed class LevelMeterEngine
{
    public const int WindowFrames = 1024;
    public const double DbMax = 0;
    public const double DbMin = -60;
    public const double KneeDb = -20;
    /// <summary>-20 dB の高さ比（下端 = 0）。0〜-20 は均等、それより下は徐々に圧縮。</summary>
    public const double KneeNorm = 0.48;
    /// <summary>バー塗り（レベルメーター / スペアナ）の不透明度。</summary>
    public const double BarFillOpacity = 0.68;
    public const double BelowKneeGamma = 1.55;
    public const double BarInstTrack = 0.48;
    public const double BarAttackSec = 0.018;
    public const double BarReleaseSec = 0.10;
    public const double PeakHoldSec = 1.0;
    public const double PeakReleaseDbPerSec = 10;
    public const double RmsHoldMarkUpSmooth = 0.010;
    public const double RmsHoldMarkDnSmooth = 0.034;
    public const double HoldLineEpsilonDb = 0.05;
    public const double ClipHoldSec = 2.0;

    private readonly ChannelState _left = new();
    private readonly ChannelState _right = new();
    private DateTime _clipLeftUntil;
    private DateTime _clipRightUntil;

    public LevelMeterSnapshot Snapshot { get; private set; } = LevelMeterSnapshot.Idle;

    public void Reset()
    {
        _left.Reset();
        _right.Reset();
        _clipLeftUntil = default;
        _clipRightUntil = default;
        Snapshot = LevelMeterSnapshot.Idle;
    }

    public void Extinguish()
    {
        Reset();
    }

    public LevelMeterSnapshot Update(ReadOnlySpan<float> left, ReadOnlySpan<float> right, double nowSeconds)
    {
        MeasureSpan(left, out var peakL, out var rmsL);
        MeasureSpan(right, out var peakR, out var rmsR);
        return Update(peakL, rmsL, peakR, rmsR, nowSeconds, hasSamples: left.Length > 0 || right.Length > 0);
    }

    public LevelMeterSnapshot Update(
        float peakLeft,
        float rmsLeft,
        float peakRight,
        float rmsRight,
        double nowSeconds,
        bool hasSamples)
    {
        if (!hasSamples)
        {
            peakLeft = _left.LastPeak;
            rmsLeft = _left.LastRms;
            peakRight = _right.LastPeak;
            rmsRight = _right.LastRms;
        }

        var l = Measure(_left, peakLeft, rmsLeft, nowSeconds);
        var r = Measure(_right, peakRight, rmsRight, nowSeconds);
        if (l.InstPeakDb >= 0)
        {
            _clipLeftUntil = DateTime.UtcNow.AddSeconds(ClipHoldSec);
        }

        if (r.InstPeakDb >= 0)
        {
            _clipRightUntil = DateTime.UtcNow.AddSeconds(ClipHoldSec);
        }

        Snapshot = new LevelMeterSnapshot(l, r, DateTime.UtcNow < _clipLeftUntil, DateTime.UtcNow < _clipRightUntil);
        return Snapshot;
    }

    public static double ToDb(double linear) =>
        20d * Math.Log10(Math.Max(linear, 1e-8));

    public static double DbToNorm(double db)
    {
        if (!double.IsFinite(db))
        {
            return 0;
        }

        var c = Math.Clamp(db, DbMin, DbMax);
        if (c >= KneeDb)
        {
            return KneeNorm + (c - KneeDb) / (DbMax - KneeDb) * (1 - KneeNorm);
        }

        var u = (c - DbMin) / (KneeDb - DbMin);
        return KneeNorm * Math.Pow(Math.Max(0, u), BelowKneeGamma);
    }

    public static double DbToHeightPct(double db) => DbToNorm(db) * 100d;

    public static string FormatReadout(double db)
    {
        if (!double.IsFinite(db) || db <= DbMin)
        {
            return "-60.0";
        }

        return Math.Min(DbMax, db).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<double> ScaleLabels { get; } =
        [0, -5, -10, -15, -20, -25, -30, -35, -40, -45, -50, -60];

    public static ColorRgb LevelColor(double db)
    {
        var t = Math.Clamp(DbToNorm(db), 0, 1);
        ReadOnlySpan<(double P, byte R, byte G, byte B)> stops =
        [
            (0, 10, 48, 68),
            (0.26, 13, 74, 98),
            (0.55, 58, 184, 232),
            (0.82, 200, 239, 255),
            (1, 248, 254, 255),
        ];
        var i = 0;
        for (; i < stops.Length - 2; i++)
        {
            if (t <= stops[i + 1].P)
            {
                break;
            }
        }

        var a = stops[i];
        var b = stops[i + 1];
        var denom = b.P - a.P;
        var w = denom < 1e-9 ? 1 : (t - a.P) / denom;
        return new ColorRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * w),
            (byte)Math.Round(a.G + (b.G - a.G) * w),
            (byte)Math.Round(a.B + (b.B - a.B) * w));
    }

    private static void MeasureSpan(ReadOnlySpan<float> samples, out float peak, out float rms)
    {
        peak = 0f;
        var sumSquares = 0d;
        var n = Math.Max(1, samples.Length);
        for (var i = 0; i < samples.Length; i++)
        {
            var val = Math.Abs(samples[i]);
            if (val > peak)
            {
                peak = val;
            }

            sumSquares += val * (double)val;
        }

        rms = (float)Math.Sqrt(sumSquares / n);
    }

    private static ChannelMeter Measure(ChannelState st, float peak, float rms, double now)
    {
        var dt = st.LastT > 0 ? Math.Min(0.12, Math.Max(0, now - st.LastT)) : 1d / 60d;
        st.LastT = now;
        st.LastPeak = peak;
        st.LastRms = rms;

        var instPeakDb = ToDb(peak);
        var instRmsDb = ToDb(rms);
        TrackBar(ref st.VisPeakDb, instPeakDb, dt);
        TrackBar(ref st.VisRmsDb, instRmsDb, dt);

        var peakHeldDb = st.PeakHeldDb;
        if (instPeakDb > peakHeldDb)
        {
            peakHeldDb = instPeakDb;
            st.PeakHoldUntil = now + PeakHoldSec;
        }
        else if (now >= st.PeakHoldUntil)
        {
            peakHeldDb = Math.Max(instPeakDb, peakHeldDb - PeakReleaseDbPerSec * dt);
        }

        peakHeldDb = Math.Clamp(peakHeldDb, DbMin, DbMax);
        st.PeakHeldDb = peakHeldDb;

        var rmsHeldDb = st.RmsHeldDb;
        if (instRmsDb > rmsHeldDb)
        {
            rmsHeldDb = instRmsDb;
            st.RmsHoldUntil = now + PeakHoldSec;
        }
        else if (now >= st.RmsHoldUntil)
        {
            rmsHeldDb = Math.Max(instRmsDb, rmsHeldDb - PeakReleaseDbPerSec * dt);
        }

        rmsHeldDb = Math.Clamp(rmsHeldDb, DbMin, DbMax);
        st.RmsHeldDb = rmsHeldDb;

        var lineDb = st.RmsHoldLineDb;
        if (!double.IsFinite(lineDb))
        {
            lineDb = rmsHeldDb;
        }

        var lineTau = rmsHeldDb > lineDb + 1e-6 ? 1.66 : 0.48;
        lineDb += (rmsHeldDb - lineDb) * (1d - Math.Exp(-dt / lineTau));
        st.RmsHoldLineDb = Math.Clamp(lineDb, DbMin, DbMax);

        return new ChannelMeter(
            DbToHeightPct(st.VisPeakDb),
            DbToHeightPct(st.VisRmsDb),
            DbToHeightPct(peakHeldDb),
            DbToHeightPct(st.RmsHoldLineDb),
            instPeakDb,
            instRmsDb,
            peakHeldDb,
            rmsHeldDb,
            st.RmsHoldLineDb,
            peakHeldDb > instPeakDb + HoldLineEpsilonDb,
            rmsHeldDb > instRmsDb + HoldLineEpsilonDb);
    }

    private static void TrackBar(ref double visDb, double instDb, double dt)
    {
        var tau = instDb >= visDb ? BarAttackSec : BarReleaseSec;
        var coeff = 1d - Math.Exp(-dt / Math.Max(1e-4, tau));
        visDb += (instDb - visDb) * coeff;
        visDb = Math.Clamp(visDb, DbMin, DbMax);
    }

    private sealed class ChannelState
    {
        public double LastT;
        public float LastPeak;
        public float LastRms;
        public double VisPeakDb = DbMin;
        public double VisRmsDb = DbMin;
        public double PeakHeldDb = DbMin;
        public double PeakHoldUntil = -1e9;
        public double RmsHeldDb = DbMin;
        public double RmsHoldUntil = -1e9;
        public double RmsHoldLineDb = DbMin;

        public void Reset()
        {
            LastT = 0;
            LastPeak = 0;
            LastRms = 0;
            VisPeakDb = DbMin;
            VisRmsDb = DbMin;
            PeakHeldDb = DbMin;
            PeakHoldUntil = -1e9;
            RmsHeldDb = DbMin;
            RmsHoldUntil = -1e9;
            RmsHoldLineDb = DbMin;
        }
    }
}

internal readonly record struct ColorRgb(byte R, byte G, byte B);

internal readonly record struct ChannelMeter(
    double PeakPct,
    double RmsPct,
    double PeakHoldPct,
    double RmsHoldPct,
    double InstPeakDb,
    double InstRmsDb,
    double PeakHeldDb,
    double RmsHeldDb,
    double RmsHoldLineDb,
    bool ShowPeakHold,
    bool ShowRmsHold);

internal readonly record struct LevelMeterSnapshot(
    ChannelMeter Left,
    ChannelMeter Right,
    bool ClipLeft,
    bool ClipRight)
{
    public static LevelMeterSnapshot Idle { get; } = new(
        new ChannelMeter(0, 0, 0, 0, LevelMeterEngine.DbMin, LevelMeterEngine.DbMin,
            LevelMeterEngine.DbMin, LevelMeterEngine.DbMin, LevelMeterEngine.DbMin, false, false),
        new ChannelMeter(0, 0, 0, 0, LevelMeterEngine.DbMin, LevelMeterEngine.DbMin,
            LevelMeterEngine.DbMin, LevelMeterEngine.DbMin, LevelMeterEngine.DbMin, false, false),
        false,
        false);
}
