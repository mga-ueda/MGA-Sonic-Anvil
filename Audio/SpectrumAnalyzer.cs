namespace MgaSonicAnvil.Audio;

/// <summary>
/// Layer Music Checker と同じ帯域検波・準ピーク・ピークホールド。
/// 描画（LED セル）は UI 側。ここは数値だけ。
/// </summary>
internal sealed class SpectrumAnalyzer
{
    public const int FftSize = 2048;
    public const float FloorDb = -50f;
    public const float CeilingDb = 0f;
    public const double GridFloorHz = 20;
    public const int MinBarDevicePx = 1;
    public const int BarGutterDevicePx = 1;
    public static int MaxBandCount => LabelTopRow.Length + LabelBotRow.Length;

    /// <summary>1px バー + 1px ガターで 31 帯域を並べた最小プロット幅。</summary>
    public static int RequiredPlotDevicePx =>
        MaxBandCount * MinBarDevicePx + Math.Max(0, MaxBandCount - 1) * BarGutterDevicePx;

    /// <summary>Layer Music Checker のキャンバス余白（px）。</summary>
    public const int OrigPadTopPx = 14;
    public const int OrigPadLeftPx = 44;
    public const int OrigPadRightPx = 44;
    public const int OrigInsetLeftPx = 12;
    public const int OrigInsetRightPx = 4;
    public const int OrigFreqLabelPx = 40;

    /// <summary>
    /// 基準キャンバス幅。body max-width 1300 − 余白 − メーター列 − 枠。
    /// </summary>
    public const int OriginalOuterWidthPx = 1070;

    public const double IdealLedCellPx = 5;
    public const double IdealLedLinePx = 1;

    public static int OriginalPlotHeightPx =>
        LedRowCount(FloorDb) * (int)IdealLedCellPx
        + Math.Max(0, LedRowCount(FloorDb) - 1) * (int)IdealLedLinePx;

    public static int OriginalOuterHeightPx =>
        OrigPadTopPx + OriginalPlotHeightPx + OrigFreqLabelPx;

    public static double OriginalAspect =>
        OriginalOuterWidthPx / (double)OriginalOuterHeightPx;
    public const double BlurSigma = 0.45;
    public const double SkirtNeighborAtten = 1.52;
    public const double SkirtOuterBoost = 4.15;
    public const double SkirtRing3Mult = 1.38;
    public const double SkirtRing4PlusMult = 0.78;
    public const double SkirtMinPeakLin = 1e-14;
    public const double PeakHoldCenterSec = 2.0;
    public const double PeakHoldNeighborSec = 0.38;
    public const double PeakHoldOuterSec = 0.14;
    public const double PeakReleaseDbPerSec = 5.25;
    public const double PeakReleaseMultNeighbor = 1.22;
    public const double PeakReleaseMultOuter = 1.55;
    public const double FftCalDbMax = 12;
    public const double BellCalibMinDominanceDb = 3;
    public const double RiseSeconds = 0.001;
    public const double FallSeconds = 0.7;
    public const double AnalyserSmoothing = 0.14;
    public const float PeakSoftKneeDb = -6f;
    public const double PeakSoftGamma = 1.24;

    public static readonly (double Hz, string Text)[] LabelTopRow =
    [
        (20, "20"), (31.5, "31.5"), (50, "50"), (80, "80"), (125, "125"), (200, "200"),
        (315, "315"), (500, "500"), (800, "800"), (1250, "1k25"), (2000, "2k"),
        (3150, "3k15"), (5000, "5k"), (8000, "8k"), (12500, "12k5"), (20000, "20k"),
    ];

    public static readonly (double Hz, string Text)[] LabelBotRow =
    [
        (25, "25"), (40, "40"), (63, "63"), (100, "100"), (160, "160"), (250, "250"),
        (400, "400"), (630, "630"), (1000, "1k"), (1600, "1k6"), (2500, "2k5"),
        (4000, "4k"), (6300, "6k3"), (10000, "10k"), (16000, "16k"),
    ];

    private readonly float[] _window = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly float[] _binDb = new float[FftSize / 2];
    private readonly float _windowSum;
    private double[] _bandLin = [];
    private double[] _blurredLin = [];
    private double[] _displayDb = [];
    private float[] _envelopeDb = [];
    private float[] _peakHoldDb = [];
    private double[] _peakHoldUntil = [];
    private bool _binsPrimed;
    private Bands _bands;
    private double _nowSec;
    private double _nyquist;

    public SpectrumAnalyzer()
    {
        var sum = 0f;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * (float)Math.Cos(2d * Math.PI * i / (FftSize - 1));
            sum += _window[i];
        }

        _windowSum = sum;
        EnsureBands(22050);
    }

    public Bands CurrentBands => _bands;

    public ReadOnlySpan<float> EnvelopeDb => _envelopeDb;

    public ReadOnlySpan<float> PeakHoldDb => _peakHoldDb;

    public bool HasVisibleLevel
    {
        get
        {
            foreach (var db in _envelopeDb)
            {
                if (DbNorm(db) > 0.004f)
                {
                    return true;
                }
            }

            foreach (var db in _peakHoldDb)
            {
                if (DbNorm(db) > 0.004f)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void Process(float[] samples, int sampleRate, double dtSec, bool active)
    {
        ArgumentNullException.ThrowIfNull(samples);
        dtSec = Math.Clamp(dtSec, 0, 0.12);
        _nowSec += dtSec;
        var nyquist = Math.Max(1000, sampleRate) / 2d;
        EnsureBands(nyquist);
        if (!active)
        {
            DecayIdle(dtSec);
            return;
        }

        var peakLin = 0f;
        var n = Math.Min(FftSize, samples.Length);
        for (var i = 0; i < n; i++)
        {
            var s = samples[i];
            peakLin = Math.Max(peakLin, Math.Abs(s));
            _re[i] = s * _window[i];
            _im[i] = 0d;
        }

        for (var i = n; i < FftSize; i++)
        {
            _re[i] = 0d;
            _im[i] = 0d;
        }

        Fft(_re, _im);
        var half = FftSize / 2;
        var fftBinMax = -300f;
        for (var bin = 0; bin < half; bin++)
        {
            var mag = 2d * Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]) / _windowSum;
            var db = mag > 1e-12 ? (float)(20d * Math.Log10(mag)) : -200f;
            if (_binsPrimed)
            {
                db = (float)(AnalyserSmoothing * _binDb[bin] + (1d - AnalyserSmoothing) * db);
            }

            _binDb[bin] = db;
            if (bin > 0 && db > fftBinMax)
            {
                fftBinMax = db;
            }
        }

        _binsPrimed = true;
        if (peakLin > 1e-8f && fftBinMax > -115f)
        {
            var tdPeakDb = 20d * Math.Log10(peakLin);
            var dCal = Math.Clamp(tdPeakDb - fftBinMax, -2d, FftCalDbMax);
            if (Math.Abs(dCal) > 0.05)
            {
                for (var bin = 1; bin < half; bin++)
                {
                    _binDb[bin] += (float)dCal;
                }
            }
        }

        IntegrateBands(nyquist);
        ShapeBands();
        CalibrateBell(peakLin);
        UpdateEnvelope(dtSec);
        UpdatePeakHold(dtSec);
    }

    public void Reset()
    {
        _binsPrimed = false;
        Array.Fill(_envelopeDb, FloorDb);
        Array.Fill(_peakHoldDb, FloorDb);
        Array.Fill(_peakHoldUntil, -1e9);
        Array.Fill(_binDb, FloorDb);
    }

    public static Bands CreateBands(double nyquist)
    {
        var fHi = nyquist * 0.995;
        var centersList = CollectGridFreqs(nyquist)
            .Where(fc => fc >= GridFloorHz - 1e-9 && fc <= fHi)
            .ToArray();
        var n = centersList.Length;
        var low = new double[n];
        var high = new double[n];
        var gLo = Math.Pow(2, -1d / 6d);
        var gHi = Math.Pow(2, 1d / 6d);
        for (var i = 0; i < n; i++)
        {
            var c = centersList[i];
            var loB = i == 0 ? GridFloorHz : Math.Sqrt(centersList[i - 1] * c);
            var hiB = i == n - 1 ? fHi : Math.Sqrt(c * centersList[i + 1]);
            loB = Math.Max(GridFloorHz, loB);
            hiB = Math.Min(fHi, hiB);
            if (!(loB < hiB))
            {
                low[i] = Math.Max(GridFloorHz, c * gLo);
                high[i] = Math.Min(fHi, c * gHi);
                if (low[i] >= high[i])
                {
                    low[i] = Math.Max(GridFloorHz, high[i] * 0.7);
                }
            }
            else
            {
                low[i] = loB;
                high[i] = hiB;
            }
        }

        return new Bands(centersList, low, high);
    }

    public static double[] CollectGridFreqs(double nyquist)
    {
        var maxF = nyquist * 0.995;
        var set = new SortedSet<double>();
        foreach (var (hz, _) in LabelTopRow)
        {
            if (hz <= maxF)
            {
                set.Add(hz);
            }
        }

        foreach (var (hz, _) in LabelBotRow)
        {
            if (hz <= maxF)
            {
                set.Add(hz);
            }
        }

        return [.. set];
    }

    public static BarRect[] CreateBarRects(int plotX, int plotW, int nBands, int gutterPx)
    {
        var rects = new BarRect[Math.Max(0, nBands)];
        if (nBands <= 0)
        {
            return rects;
        }

        var gutterTotal = (nBands - 1) * gutterPx;
        var avail = Math.Max(0, plotW - gutterTotal);
        var baseW = nBands == 0 ? 0 : avail / nBands;
        var rem = avail - baseW * nBands;
        var x = plotX;
        for (var b = 0; b < nBands; b++)
        {
            var bw = baseW + (rem > 0 ? 1 : 0);
            if (rem > 0)
            {
                rem--;
            }

            rects[b] = new BarRect(x, bw);
            x += bw + (b < nBands - 1 ? gutterPx : 0);
        }

        return rects;
    }

    public static LedCells CreateLedCells(double plotY, double plotH, float floorDb)
    {
        var loInt = (int)Math.Ceiling(floorDb - 1e-9);
        var n = Math.Max(1, -loInt);
        var ideal = n * IdealLedCellPx + Math.Max(0, n - 1) * IdealLedLinePx;
        var scale = plotH > 0 && ideal > 0 ? plotH / ideal : 1;
        var cell = IdealLedCellPx * scale;
        var line = IdealLedLinePx * scale;
        if (cell < 0.75)
        {
            line = 0;
            cell = plotH / n;
        }

        var bot = new double[n];
        var top = new double[n];
        var yBottom = plotY + plotH;
        bot[0] = yBottom;
        top[0] = bot[0] - cell;
        for (var i = 1; i < n; i++)
        {
            bot[i] = top[i - 1] - line;
            top[i] = bot[i] - cell;
        }

        return new LedCells(n, loInt, bot, top);
    }

    public static int LedRowCount(float floorDb) =>
        Math.Max(1, -(int)Math.Ceiling(floorDb - 1e-9));

    public static float DbNorm(float db)
    {
        var range = CeilingDb - FloorDb;
        if (range <= 0)
        {
            return 0;
        }

        return Math.Clamp((db - FloorDb) / range, 0f, 1f);
    }

    public static float SoftenDisplayPeak(float db)
    {
        var x = Math.Clamp(db, FloorDb, CeilingDb);
        if (x <= PeakSoftKneeDb)
        {
            return x;
        }

        var span = CeilingDb - PeakSoftKneeDb;
        var t = (x - PeakSoftKneeDb) / span;
        return PeakSoftKneeDb + span * (float)Math.Pow(Math.Clamp(t, 0, 1), PeakSoftGamma);
    }

    public static (byte R, byte G, byte B) LevelRgb(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        ReadOnlySpan<(float P, byte R, byte G, byte B)> stops =
        [
            (0f, 10, 48, 68),
            (0.26f, 13, 74, 98),
            (0.55f, 58, 184, 232),
            (0.82f, 200, 239, 255),
            (1f, 248, 254, 255),
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
        var w = denom < 1e-9f ? 1f : (t - a.P) / denom;
        return (
            (byte)Math.Round(a.R + (b.R - a.R) * w),
            (byte)Math.Round(a.G + (b.G - a.G) * w),
            (byte)Math.Round(a.B + (b.B - a.B) * w));
    }

    public readonly record struct Bands(double[] Centers, double[] Low, double[] High)
    {
        public int Count => Centers.Length;
    }

    public readonly record struct BarRect(int X1, int BarW);

    public readonly record struct LedCells(int Count, int LoInt, double[] Bot, double[] Top);

    private void EnsureBands(double nyquist)
    {
        var next = CreateBands(nyquist);
        if (_bands.Centers is { Length: > 0 } && _bands.Count == next.Count)
        {
            _nyquist = nyquist;
            _bands = next;
            return;
        }

        _nyquist = nyquist;
        _bands = next;
        var n = next.Count;
        _bandLin = new double[n];
        _blurredLin = new double[n];
        _displayDb = new double[n];
        _envelopeDb = new float[n];
        _peakHoldDb = new float[n];
        _peakHoldUntil = new double[n];
        Array.Fill(_envelopeDb, FloorDb);
        Array.Fill(_peakHoldDb, FloorDb);
        Array.Fill(_peakHoldUntil, -1e9);
    }

    private void IntegrateBands(double nyquist)
    {
        Array.Clear(_bandLin);
        var half = FftSize / 2;
        var binW = nyquist / half;
        var n = _bands.Count;
        for (var i = 1; i < half; i++)
        {
            var fLeft = i * binW;
            var fRight = (i + 1) * binW;
            if (fRight <= GridFloorHz)
            {
                continue;
            }

            var db = _binDb[i];
            if (!float.IsFinite(db))
            {
                continue;
            }

            var pBin = db < -120f ? 0d : Math.Pow(10, db / 10d);
            var bw = fRight - fLeft;
            if (bw <= 0)
            {
                continue;
            }

            for (var b = 0; b < n; b++)
            {
                var o0 = Math.Max(fLeft, _bands.Low[b]);
                var o1 = Math.Min(fRight, _bands.High[b]);
                if (o1 <= o0)
                {
                    continue;
                }

                _bandLin[b] += pBin * ((o1 - o0) / bw);
            }
        }
    }

    private void ShapeBands()
    {
        var n = _bands.Count;
        var mxLin = -1d;
        var bMxLin = 0;
        for (var b = 0; b < n; b++)
        {
            if (_bandLin[b] > mxLin)
            {
                mxLin = _bandLin[b];
                bMxLin = b;
            }
        }

        BlurLinear(_bandLin, _blurredLin);
        var useSkirt = mxLin > SkirtMinPeakLin;
        for (var b = 0; b < n; b++)
        {
            var mult = 1d;
            if (useSkirt)
            {
                var d = Math.Abs(b - bMxLin);
                mult = d switch
                {
                    1 => SkirtNeighborAtten,
                    2 => SkirtOuterBoost,
                    3 => SkirtRing3Mult,
                    _ => SkirtRing4PlusMult,
                };
            }

            var merged = Math.Max(_bandLin[b], _blurredLin[b] * mult);
            _displayDb[b] = merged > 1e-18 ? 10d * Math.Log10(merged) : -200d;
        }
    }

    private static void BlurLinear(double[] src, double[] dest)
    {
        var n = src.Length;
        var r = (int)Math.Ceiling(BlurSigma * 4d);
        for (var b = 0; b < n; b++)
        {
            var s = 0d;
            var w = 0d;
            for (var k = -r; k <= r; k++)
            {
                var bk = b + k;
                if ((uint)bk >= (uint)n)
                {
                    continue;
                }

                var g = Math.Exp(-(k * k) / (2d * BlurSigma * BlurSigma));
                s += src[bk] * g;
                w += g;
            }

            dest[b] = w > 0 ? s / w : 0;
        }
    }

    private void CalibrateBell(float peakLin)
    {
        if (peakLin <= 1e-8f)
        {
            return;
        }

        var n = _bands.Count;
        var bMx = -1;
        var mxDisp = -300d;
        for (var b = 0; b < n; b++)
        {
            if (_displayDb[b] > mxDisp)
            {
                mxDisp = _displayDb[b];
                bMx = b;
            }
        }

        var second = -300d;
        for (var b = 0; b < n; b++)
        {
            if (b != bMx && _displayDb[b] > second)
            {
                second = _displayDb[b];
            }
        }

        if (mxDisp <= -115 || mxDisp - second < BellCalibMinDominanceDb)
        {
            return;
        }

        var tdPeakDb = 20d * Math.Log10(peakLin);
        var dBell = Math.Clamp(tdPeakDb - mxDisp, 0, FftCalDbMax);
        if (dBell <= 0.04)
        {
            return;
        }

        for (var b = 0; b < n; b++)
        {
            if (_displayDb[b] > -199)
            {
                _displayDb[b] = Math.Min(CeilingDb, _displayDb[b] + dBell);
            }
        }
    }

    private void UpdateEnvelope(double dtSec)
    {
        var rise = RiseSeconds > 1e-9 ? Math.Min(1, 1 - Math.Exp(-dtSec / RiseSeconds)) : 1;
        var fall = Math.Min(1, 1 - Math.Exp(-dtSec / FallSeconds));
        for (var b = 0; b < _envelopeDb.Length; b++)
        {
            var raw = Math.Clamp(
                double.IsFinite(_displayDb[b]) ? (float)_displayDb[b] : FloorDb,
                FloorDb,
                CeilingDb);
            var tgt = SoftenDisplayPeak(raw);
            var env = float.IsFinite(_envelopeDb[b]) ? _envelopeDb[b] : FloorDb;
            var k = tgt >= env ? rise : fall;
            env += (tgt - env) * (float)k;
            _envelopeDb[b] = Math.Clamp(env, FloorDb, CeilingDb);
        }
    }

    private void UpdatePeakHold(double dtSec)
    {
        var n = _envelopeDb.Length;
        var bPk = 0;
        var vPk = FloorDb;
        for (var b = 0; b < n; b++)
        {
            if (_envelopeDb[b] > vPk)
            {
                vPk = _envelopeDb[b];
                bPk = b;
            }
        }

        for (var b = 0; b < n; b++)
        {
            var inst = Math.Clamp(_envelopeDb[b], FloorDb, CeilingDb);
            var dist = Math.Abs(b - bPk);
            var holdSec = dist switch
            {
                0 => PeakHoldCenterSec,
                1 => PeakHoldNeighborSec,
                _ => PeakHoldOuterSec,
            };
            var held = _peakHoldDb[b];
            if (inst > held)
            {
                held = inst;
                _peakHoldUntil[b] = _nowSec + holdSec;
            }
            else if (_nowSec >= _peakHoldUntil[b])
            {
                var rel = PeakReleaseDbPerSec * dtSec;
                if (dist == 1)
                {
                    rel *= PeakReleaseMultNeighbor;
                }
                else if (dist >= 2)
                {
                    rel *= PeakReleaseMultOuter;
                }

                held = Math.Max(inst, held - (float)rel);
            }

            _peakHoldDb[b] = Math.Clamp(held, FloorDb, CeilingDb);
        }
    }

    private void DecayIdle(double dtSec)
    {
        if (_envelopeDb.Length == 0)
        {
            return;
        }

        Array.Clear(_displayDb);
        for (var b = 0; b < _displayDb.Length; b++)
        {
            _displayDb[b] = FloorDb;
        }

        UpdateEnvelope(dtSec);
        UpdatePeakHold(dtSec);
        for (var b = 0; b < _envelopeDb.Length; b++)
        {
            if (_envelopeDb[b] <= FloorDb + 0.25f)
            {
                _envelopeDb[b] = FloorDb;
            }
        }

        for (var b = 0; b < _peakHoldDb.Length; b++)
        {
            if (_peakHoldDb[b] <= FloorDb + 0.25f)
            {
                _peakHoldDb[b] = FloorDb;
            }
        }

        _binsPrimed = false;
    }

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }

            j |= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var start = 0; start < n; start += length)
            {
                var curRe = 1d;
                var curIm = 0d;
                for (var k = 0; k < length / 2; k++)
                {
                    var evenIndex = start + k;
                    var oddIndex = start + k + length / 2;
                    var oddRe = re[oddIndex] * curRe - im[oddIndex] * curIm;
                    var oddIm = re[oddIndex] * curIm + im[oddIndex] * curRe;
                    re[oddIndex] = re[evenIndex] - oddRe;
                    im[oddIndex] = im[evenIndex] - oddIm;
                    re[evenIndex] += oddRe;
                    im[evenIndex] += oddIm;
                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
