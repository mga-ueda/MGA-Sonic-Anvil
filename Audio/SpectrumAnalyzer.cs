namespace MgaSonicAnvil.Audio;

/// <summary>
/// Layer Music Checker と同じ帯域検波・準ピーク・ピークホールド。
/// 描画（LED セル）は UI 側。ここは数値だけ。
/// </summary>
internal sealed class SpectrumAnalyzer
{
    /// <summary>48 kHz で約 2.9 Hz/ビン。20 / 25 / 31.5 Hz を分けられる長さ。</summary>
    public const int FftSize = 16384;
    /// <summary>
    /// 中高域のライブバー用。48 kHz で 43 ms。
    /// 画面は約 33 ms 間隔なので、これより短い窓は拍を取りこぼす。低域は FftSize のまま。
    /// </summary>
    public const int FastFftSize = 2048;
    /// <summary>この中心周波数以上は短い FFT。帯域幅がビン幅に届くあたり。</summary>
    public const double FastFftMinHz = 250;
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
    /// <summary>1/3oct フィルタの隣帯。中心から約 14 dB 下（DigiCheck の裾）。</summary>
    public const double AdjacentBandDropDb = 14;
    /// <summary>さらに一つ外側。20 Hz 正弦で約 −42 dB になる量。</summary>
    public const double SecondBandDropDb = 24;
    public const double SkirtMinPeakLin = 1e-14;
    /// <summary>DigiCheck の Peak Hold Time。ピークを離れてからこの秒数は保持する。</summary>
    public const double PeakHoldCenterSec = 2.07;
    public const double PeakHoldNeighborSec = 0.38;
    public const double PeakHoldOuterSec = 0.14;
    /// <summary>DigiCheck のホールド解除後の降下。実測は約 21 dB/s。</summary>
    public const double PeakReleaseDbPerSec = 21;
    public const double PeakReleaseMultNeighbor = 1.22;
    public const double PeakReleaseMultOuter = 1.55;
    public const double RiseSeconds = 0.001;
    /// <summary>
    /// ライブバーの立ち下がり。20 dB を 1.5 秒で落とす（20/1.5 dB/s）。
    /// 変更前は 21（20 dB を約 1 秒）。
    /// </summary>
    public const double LiveReleaseDbPerSec = 20d / 1.5d;
    public const double AnalyserSmoothing = 0.14;
    /// <summary>
    /// 長い FFT の帯域パワーは、ビンが十分ある中高域で DigiCheck の準ピークより約 6.5 dB 低い。
    /// 低域は推定のばらつきを包絡が拾うので、同じ量を足すと DigiCheck より高くなる。
    /// </summary>
    public const double BroadbandCrestDb = 6.5;
    public const double BroadbandCrestStartHz = 40;
    /// <summary>主ローブが帯幅より広い低域だけ、隣への漏れを DigiCheck の間隔まで下げる。</summary>
    public const double LowLeakCapHz = 80;
    /// <summary>最も高い帯からこの dB 以内に 8 帯以上あるとき、ピンクのような広帯域とみなす。</summary>
    public const double BroadbandClusterDb = 8;
    public const int BroadbandClusterCount = 8;
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
    /// <summary>ハン窓の等価雑音帯域（ビン数）。振幅正規化したビンパワーの和は正弦で約 +1.76 dB 過大になる。</summary>
    private readonly double _enbwBins;
    private readonly float[] _fastWindow = new float[FastFftSize];
    private readonly double[] _fastRe = new double[FastFftSize];
    private readonly double[] _fastIm = new double[FastFftSize];
    private readonly float[] _fastBinDb = new float[FastFftSize / 2];
    private readonly float _fastWindowSum;
    private readonly double _fastEnbwBins;
    private bool _fastBinsPrimed;
    private double[] _bandLin = [];
    private double[] _displayDb = [];
    private double[] _rawDb = [];
    private float[] _envelopeDb = [];
    private float[] _peakHoldDb = [];
    private double[] _peakHoldUntil = [];
    private double[] _peakSustainSec = [];
    private bool[] _peakFlat = [];
    /// <summary>中心周波数の正弦が自分の帯に落ちる割合。狭い低域は 1 未満。</summary>
    private double[] _bandCapture = [];
    private bool _binsPrimed;
    private Bands _bands;
    private double _nowSec;
    private double _nyquist;

    public SpectrumAnalyzer()
    {
        var sum = 0d;
        var sumSq = 0d;
        for (var i = 0; i < FftSize; i++)
        {
            var w = 0.5 - 0.5 * Math.Cos(2d * Math.PI * i / (FftSize - 1));
            _window[i] = (float)w;
            sum += w;
            sumSq += w * w;
        }

        _windowSum = (float)sum;
        _enbwBins = FftSize * sumSq / (sum * sum);
        sum = 0d;
        sumSq = 0d;
        for (var i = 0; i < FastFftSize; i++)
        {
            var w = 0.5 - 0.5 * Math.Cos(2d * Math.PI * i / (FastFftSize - 1));
            _fastWindow[i] = (float)w;
            sum += w;
            sumSq += w * w;
        }

        _fastWindowSum = (float)sum;
        _fastEnbwBins = FastFftSize * sumSq / (sum * sum);
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

        var n = Math.Min(FftSize, samples.Length);
        for (var i = 0; i < n; i++)
        {
            var s = samples[i];
            _re[i] = s * _window[i];
            _im[i] = 0d;
        }

        for (var i = n; i < FftSize; i++)
        {
            _re[i] = 0d;
            _im[i] = 0d;
        }

        Fft(_re, _im);
        WriteBinDb(_re, _im, _binDb, _windowSum, (float)AnalyserSmoothing, ref _binsPrimed);
        AnalyzeFast(samples, n);
        IntegrateBands(nyquist);
        ShapeBands();
        ApplyBroadbandCrest();
        UpdateEnvelope(dtSec);
        UpdatePeakHold(dtSec);
    }

    public void Reset()
    {
        _binsPrimed = false;
        Array.Fill(_envelopeDb, FloorDb);
        Array.Fill(_peakHoldDb, FloorDb);
        Array.Fill(_peakHoldUntil, -1e9);
        Array.Clear(_peakSustainSec);
        Array.Clear(_peakFlat);
        Array.Fill(_binDb, FloorDb);
        Array.Fill(_fastBinDb, FloorDb);
        _fastBinsPrimed = false;
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
            (0f, 0, 92, 140),
            (0.26f, 0, 113, 172),
            (0.55f, 58, 184, 232),
            (0.82f, 200, 239, 255),
            (1f, 200, 239, 255),
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
            var nyquistChanged = Math.Abs(_nyquist - nyquist) > 1;
            _nyquist = nyquist;
            _bands = next;
            if (nyquistChanged || _bandCapture.Length != next.Count)
            {
                BuildCaptureFractions(nyquist * 2d);
            }

            return;
        }

        _nyquist = nyquist;
        _bands = next;
        var n = next.Count;
        _bandLin = new double[n];
        _displayDb = new double[n];
        _rawDb = new double[n];
        _envelopeDb = new float[n];
        _peakHoldDb = new float[n];
        _peakHoldUntil = new double[n];
        _peakSustainSec = new double[n];
        _peakFlat = new bool[n];
        Array.Fill(_envelopeDb, FloorDb);
        Array.Fill(_peakHoldDb, FloorDb);
        Array.Fill(_peakHoldUntil, -1e9);
        BuildCaptureFractions(nyquist * 2d);
    }

    /// <summary>20→25 Hz で約 8 dB。上側の 31.5 Hz は約 22 dB、下側は約 18 dB。</summary>
    private static double LowLeakDropDb(double oct, bool higherNeighbor)
    {
        var near = Math.Log(25d / 20d) / Math.Log(2);
        var far = Math.Log(31.5 / 20d) / Math.Log(2);
        if (oct <= near)
        {
            return 8.4d * (oct / near);
        }

        var t = Math.Min(1d, (oct - near) / (far - near));
        var farDrop = higherNeighbor ? 22.4d : 18.4d;
        return 8.4d + (farDrop - 8.4d) * t;
    }

    private void BuildCaptureFractions(double sampleRate)
    {
        var n = _bands.Count;
        _bandCapture = new double[n];
        Array.Fill(_bandCapture, 1d);
        if (sampleRate < 1000 || n == 0)
        {
            return;
        }

        const double amp = 0.1;
        var target = amp * amp;
        var nyquist = sampleRate / 2d;
        var sine = new float[FftSize];
        for (var b = 0; b < n; b++)
        {
            if (_bands.Centers[b] >= 100)
            {
                continue;
            }

            var hz = _bands.Centers[b];
            for (var i = 0; i < FftSize; i++)
            {
                sine[i] = (float)(amp * Math.Sin(2d * Math.PI * hz * i / sampleRate));
            }

            var nSamp = Math.Min(FftSize, sine.Length);
            for (var i = 0; i < nSamp; i++)
            {
                _re[i] = sine[i] * _window[i];
                _im[i] = 0d;
            }

            for (var i = nSamp; i < FftSize; i++)
            {
                _re[i] = 0d;
                _im[i] = 0d;
            }

            Fft(_re, _im);
            var primed = false;
            WriteBinDb(_re, _im, _binDb, _windowSum, smooth: 0f, ref primed);
            Array.Clear(_bandLin);
            Accumulate(_binDb, _enbwBins, nyquist, fast: false);
            var rho = _bandLin[b] / target;
            if (rho is > 0.2 and < 0.98)
            {
                _bandCapture[b] = rho;
            }
        }

        Array.Clear(_bandLin);
        Array.Fill(_binDb, FloorDb);
    }

    private void AnalyzeFast(float[] samples, int validCount)
    {
        var take = Math.Min(validCount, FastFftSize);
        var pad = FastFftSize - take;
        for (var i = 0; i < pad; i++)
        {
            _fastRe[i] = 0d;
            _fastIm[i] = 0d;
        }

        var src0 = validCount - take;
        for (var i = 0; i < take; i++)
        {
            _fastRe[pad + i] = samples[src0 + i] * _fastWindow[pad + i];
            _fastIm[pad + i] = 0d;
        }

        Fft(_fastRe, _fastIm);
        // 短い窓は 1 フレームで拍を拾う。dB の平滑は無音（−200 dB）に頂点を引きずられる。
        WriteBinDb(_fastRe, _fastIm, _fastBinDb, _fastWindowSum, smooth: 0f, ref _fastBinsPrimed);
    }

    private static void WriteBinDb(double[] re, double[] im, float[] binDb, float windowSum, float smooth, ref bool primed)
    {
        var half = re.Length / 2;
        for (var bin = 0; bin < half; bin++)
        {
            var mag = 2d * Math.Sqrt(re[bin] * re[bin] + im[bin] * im[bin]) / windowSum;
            var db = mag > 1e-12 ? (float)(20d * Math.Log10(mag)) : -200f;
            if (primed && smooth > 0f)
            {
                db = smooth * binDb[bin] + (1f - smooth) * db;
            }

            binDb[bin] = db;
        }

        primed = true;
    }

    private void IntegrateBands(double nyquist)
    {
        Array.Clear(_bandLin);
        Accumulate(_binDb, _enbwBins, nyquist, fast: false);
        Accumulate(_fastBinDb, _fastEnbwBins, nyquist, fast: true);
        var n = _bandLin.Length;
        for (var b = 0; b < n; b++)
        {
            var rho = b < _bandCapture.Length ? _bandCapture[b] : 1d;
            if (rho is > 0.2 and < 0.98)
            {
                _bandLin[b] /= rho;
            }
        }
    }

    private void Accumulate(float[] binDb, double enbwBins, double nyquist, bool fast)
    {
        var half = binDb.Length;
        var binW = nyquist / half;
        var n = _bands.Count;
        for (var i = 0; i < half; i++)
        {
            var fLeft = i * binW;
            var fRight = (i + 1) * binW;
            var db = binDb[i];
            if (!float.IsFinite(db))
            {
                continue;
            }

            var pBin = db < -120f ? 0d : Math.Pow(10, db / 10d) / enbwBins;
            var bw = fRight - fLeft;
            if (bw <= 0)
            {
                continue;
            }

            // 20 Hz 帯は DC〜20 Hz を含む（DigiCheck の Show DC in 20 Hz band）。長い FFT だけ。
            if (!fast && n > 0 && fLeft < GridFloorHz)
            {
                var dcHi = Math.Min(fRight, GridFloorHz);
                _bandLin[0] += pBin * ((dcHi - fLeft) / bw);
            }

            if (fRight <= GridFloorHz)
            {
                continue;
            }

            for (var b = 0; b < n; b++)
            {
                var useFast = _bands.Centers[b] >= FastFftMinHz;
                if (useFast != fast)
                {
                    continue;
                }

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
        if (_rawDb.Length != n)
        {
            _rawDb = new double[n];
        }

        for (var b = 0; b < n; b++)
        {
            var lin = _bandLin[b];
            var db = lin > 1e-18 ? 10d * Math.Log10(lin) : -200d;
            _rawDb[b] = db;
            _displayDb[b] = db;
        }

        // 各帯の実エネルギーから、1/3oct 離れるごとに約 14 dB 下がる裾を足す。
        // 最大帯の隣を固定で塗ると、スイープが帯の間にいるとき低域も高域も同じ形に潰れる。
        var third = 1d / 3d;
        for (var b = 0; b < n; b++)
        {
            if (_rawDb[b] < -115)
            {
                continue;
            }

            var cb = _bands.Centers[b];
            if (cb < 1)
            {
                continue;
            }

            for (var k = 0; k < n; k++)
            {
                if (k == b)
                {
                    continue;
                }

                var oct = Math.Abs(Math.Log(_bands.Centers[k] / cb) / Math.Log(2));
                if (oct >= 1.05)
                {
                    continue;
                }

                var leak = _rawDb[b] - AdjacentBandDropDb * (oct / third);
                if (leak > _displayDb[k])
                {
                    _displayDb[k] = leak;
                }
            }
        }

        // 正弦の裾は 1/3oct あたり 14 dB。FFT の漏れがそれより高い帯はそこまで下げる。
        var globalPeak = -200d;
        var peakBand = 0;
        for (var i = 0; i < n; i++)
        {
            if (_rawDb[i] > globalPeak)
            {
                globalPeak = _rawDb[i];
                peakBand = i;
            }
        }

        if (globalPeak > -80)
        {
            var thirdOct = 1d / 3d;
            for (var k = 0; k < n; k++)
            {
                if (k == peakBand || _bands.Centers[k] < 1)
                {
                    continue;
                }

                var oct = Math.Abs(Math.Log(_bands.Centers[k] / _bands.Centers[peakBand]) / Math.Log(2));
                if (oct >= 1.05)
                {
                    continue;
                }

                var cap = globalPeak - AdjacentBandDropDb * (oct / thirdOct);
                if (cap < _displayDb[k])
                {
                    _displayDb[k] = cap;
                }
            }

            // 20〜40 Hz は DigiCheck の隣帯が 14 dB より近い。40 Hz は直下の 31.5 Hz だけ。
            if (_bands.Centers[peakBand] <= 40)
            {
                for (var k = 0; k < n; k++)
                {
                    if (k == peakBand || _bands.Centers[k] > 40)
                    {
                        continue;
                    }

                    var oct = Math.Abs(Math.Log(_bands.Centers[k] / _bands.Centers[peakBand]) / Math.Log(2));
                    if (oct < 0.05 || oct >= 1.05 || (_bands.Centers[peakBand] > 31.5 && oct > 0.45))
                    {
                        continue;
                    }

                    var higher = _bands.Centers[k] > _bands.Centers[peakBand];
                    var drop = LowLeakDropDb(oct, higher);
                    if (!higher
                        && Math.Abs(_bands.Centers[peakBand] - 40) < 0.1
                        && Math.Abs(_bands.Centers[k] - 31.5) < 0.1)
                    {
                        drop = 11.4;
                    }

                    _displayDb[k] = globalPeak - drop;
                }
            }

            // DigiCheck の 8 kHz 正弦は中心が −20.9 dB。他の帯は −20。
            if (Math.Abs(_bands.Centers[peakBand] - 8000) < 1)
            {
                _displayDb[peakBand] -= 0.9;
            }
        }
    }

    private void ApplyBroadbandCrest()
    {
        var n = _bands.Count;
        if (n < BroadbandClusterCount)
        {
            return;
        }

        var loud = new double[n];
        for (var i = 0; i < n; i++)
        {
            loud[i] = _rawDb[i];
        }

        Array.Sort(loud);
        var loudest = loud[^1];
        var cluster = loud[n - BroadbandClusterCount];
        if (loudest < -80 || loudest - cluster > BroadbandClusterDb)
        {
            return;
        }

        var stable = new List<double>();
        for (var i = 0; i < n; i++)
        {
            var hz = _bands.Centers[i];
            if (hz >= 400 && hz <= 4000 && _rawDb[i] > -80)
            {
                stable.Add(_rawDb[i]);
            }
        }

        if (stable.Count == 0)
        {
            return;
        }

        stable.Sort();
        var cap = stable[stable.Count / 2] + BroadbandCrestDb;
        for (var b = 0; b < n; b++)
        {
            if (_displayDb[b] <= -115 || _bands.Centers[b] < BroadbandCrestStartHz)
            {
                continue;
            }

            var raised = _displayDb[b] + BroadbandCrestDb;
            _displayDb[b] = Math.Min(CeilingDb, Math.Min(raised, cap));
        }
    }

    private void UpdateEnvelope(double dtSec)
    {
        var rise = RiseSeconds > 1e-9 ? Math.Min(1, 1 - Math.Exp(-dtSec / RiseSeconds)) : 1;
        for (var b = 0; b < _envelopeDb.Length; b++)
        {
            var raw = Math.Clamp(
                double.IsFinite(_displayDb[b]) ? (float)_displayDb[b] : FloorDb,
                FloorDb,
                CeilingDb);
            var tgt = SoftenDisplayPeak(raw);
            var env = float.IsFinite(_envelopeDb[b]) ? _envelopeDb[b] : FloorDb;
            if (tgt >= env)
            {
                env += (tgt - env) * (float)rise;
            }
            else
            {
                env = Math.Max(tgt, env - (float)(LiveReleaseDbPerSec * dtSec));
            }

            _envelopeDb[b] = Math.Clamp(env, FloorDb, CeilingDb);
        }
    }

    private void UpdatePeakHold(double dtSec)
    {
        var n = _envelopeDb.Length;
        for (var b = 0; b < n; b++)
        {
            var inst = Math.Clamp(_envelopeDb[b], FloorDb, CeilingDb);
            // 頂点にいる間は期限を延ばす。離れてから 2.07 秒残す（速いスイープでも DigiCheck と同じ）。
            var held = _peakHoldDb[b];
            if (inst > held)
            {
                held = inst;
                _peakHoldUntil[b] = _nowSec + PeakHoldCenterSec;
            }
            else if (inst >= held - 0.4f && _nowSec < _peakHoldUntil[b])
            {
                _peakHoldUntil[b] = _nowSec + PeakHoldCenterSec;
            }
            else if (_nowSec >= _peakHoldUntil[b])
            {
                held = Math.Max(inst, held - (float)(PeakReleaseDbPerSec * dtSec));
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
        _fastBinsPrimed = false;
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
