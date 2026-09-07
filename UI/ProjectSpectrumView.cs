using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// Wwise IM Importer と同じ小型スペクトラムアナライザ。
/// バー幅・間隔はデバイス px 固定（4px）、ホスト幅もそれに合わせる。
/// </summary>
internal sealed class ProjectSpectrumView : FrameworkElement
{
    private const int FftSize = 2048;
    private const int BarWidthDevicePx = 3;
    private const int BarGapDevicePx = 2;
    private const float FloorDb = -60f;
    private const float CeilingDb = 0f;
    private const double RiseSeconds = 0.001d;
    private const double FallSeconds = 0.7d;
    private const double BlurSigma = 0.45d;
    private const float PeakSoftKneeDb = -6f;
    private const double PeakSoftGamma = 1.24d;

    private static readonly double[] BandCenters =
    [
        20d, 25d, 31.5d, 40d, 50d, 63d, 80d, 100d,
        125d, 160d, 200d, 250d, 315d, 400d, 500d, 630d,
        800d, 1000d, 1250d, 1600d, 2000d, 2500d, 3150d,
        4000d, 5000d, 6300d, 8000d, 10000d, 12500d, 16000d, 20000d,
    ];

    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _bandPower = new double[BandCenters.Length];
    private readonly double[] _blurredPower = new double[BandCenters.Length];
    private readonly float[] _envelopeDb = new float[BandCenters.Length];
    private readonly float[] _levels = new float[BandCenters.Length];
    private readonly float _windowSum;
    private bool _idle = true;
    private long _lastTickAt;

    public static readonly DependencyProperty BackgroundProperty =
        System.Windows.Controls.Control.BackgroundProperty.AddOwner(
            typeof(ProjectSpectrumView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public ProjectSpectrumView()
    {
        MinHeight = DesignMetrics.SpectrumHeight;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ApplyDevicePixelWidth();

        var windowSum = 0f;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * (float)Math.Cos(2d * Math.PI * i / (FftSize - 1));
            windowSum += _window[i];
        }

        _windowSum = windowSum;
        Array.Fill(_envelopeDb, FloorDb);
        // 停止後の減衰用。再生中は Background 優先度がマウス入力に飢餓するため、
        // MainWindow の CompositionTarget.Rendering から Tick() で駆動される。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>
    /// 更新を 1 回試みる。33ms 未満の連続呼び出しは無視するので、
    /// フレーム駆動とタイマーの両方から呼んでも二重更新しない。
    /// </summary>
    public void Tick()
    {
        var now = Environment.TickCount64;
        var elapsed = now - _lastTickAt;
        if (elapsed < 33)
        {
            return;
        }

        _lastTickAt = now;
        UpdateLevels(Math.Min(200d, elapsed));
    }

    public AudioPlayer? Player { get; set; }

    public static int RequiredWidthDevicePx =>
        BandCenters.Length * BarWidthDevicePx
        + (BandCenters.Length - 1) * BarGapDevicePx;

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyDevicePixelWidth();
    }

    private void ApplyDevicePixelWidth()
    {
        var dip = RequiredWidthDevicePx / PixelsPerDip * DesignMetrics.SpectrumWidthScale;
        Width = dip;
        MinWidth = dip;
    }

    private double PixelsPerDip
    {
        get
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            return dpi > 0 ? dpi : 1d;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        var inner = bounds;
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return;
        }

        var px = 1d / PixelsPerDip;
        var barWidth = BarWidthDevicePx * px * DesignMetrics.SpectrumWidthScale;
        var barGap = BarGapDevicePx * px * DesignMetrics.SpectrumWidthScale;
        var barBrush = WpfControlHelpers.FrozenBrush(Theme.Get("SpectrumBarBrush"));
        var bandCount = Math.Min(
            _levels.Length,
            (int)((inner.Width + barGap) / (barWidth + barGap)));
        var graphWidth = bandCount * barWidth + Math.Max(0, bandCount - 1) * barGap;
        var graphLeft = inner.Left + Math.Max(0, (inner.Width - graphWidth) / 2);
        for (var band = 0; band < bandCount; band++)
        {
            var x = graphLeft + band * (barWidth + barGap);
            var barHeight = Math.Round(_levels[band] * inner.Height);
            if (barHeight > 0)
            {
                dc.DrawRectangle(
                    barBrush,
                    null,
                    new Rect(x, inner.Bottom - barHeight, barWidth, barHeight));
            }
        }
    }

    private void UpdateLevels(double dtMs)
    {
        if (!IsVisible)
        {
            return;
        }

        var player = Player;
        var active = player is { IsPlaying: true };
        if (active)
        {
            _ = player!.ReadRecentOutputSamples(_samples);
            ComputeBandTargets(player.OutputSampleRate, dtMs);
            _idle = false;
        }
        else
        {
            var anyVisible = false;
            var fall = 1d - Math.Exp(-dtMs / 1000d / FallSeconds);
            for (var i = 0; i < _levels.Length; i++)
            {
                _envelopeDb[i] += (FloorDb - _envelopeDb[i]) * (float)fall;
                _levels[i] = DbToLevel(_envelopeDb[i]);
                if (_levels[i] > 0.004f)
                {
                    anyVisible = true;
                }
                else
                {
                    _levels[i] = 0f;
                }
            }

            if (!anyVisible)
            {
                if (_idle)
                {
                    return;
                }

                _idle = true;
            }
        }

        InvalidateVisual();
    }

    private void ComputeBandTargets(int sampleRate, double dtMs)
    {
        if (sampleRate <= 0)
        {
            sampleRate = 48000;
        }

        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _samples[i] * _window[i];
            _im[i] = 0d;
        }

        Fft(_re, _im);
        Array.Clear(_bandPower);
        var binHz = sampleRate / (double)FftSize;
        var nyquist = sampleRate / 2d;

        for (var bin = 1; bin < FftSize / 2; bin++)
        {
            var binLow = bin * binHz;
            var binHigh = Math.Min(nyquist, (bin + 1) * binHz);
            var magnitude = 2d
                * Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin])
                / _windowSum;
            var power = magnitude * magnitude;
            for (var band = 0; band < BandCenters.Length; band++)
            {
                GetBandEdges(band, nyquist, out var bandLow, out var bandHigh);
                var overlap = Math.Min(binHigh, bandHigh) - Math.Max(binLow, bandLow);
                if (overlap > 0d)
                {
                    _bandPower[band] += power * overlap / binHz;
                }
            }
        }

        BlurBandPower();

        var dt = dtMs / 1000d;
        var rise = 1d - Math.Exp(-dt / RiseSeconds);
        var fall = 1d - Math.Exp(-dt / FallSeconds);
        for (var band = 0; band < BandCenters.Length; band++)
        {
            var rawDb = _blurredPower[band] > 1e-18
                ? (float)(10d * Math.Log10(_blurredPower[band]))
                : FloorDb;
            var targetDb = SoftenDisplayPeak(Math.Clamp(rawDb, FloorDb, CeilingDb));
            var coefficient = targetDb >= _envelopeDb[band] ? rise : fall;
            _envelopeDb[band] += (targetDb - _envelopeDb[band]) * (float)coefficient;
            _levels[band] = DbToLevel(_envelopeDb[band]);
        }
    }

    private static void GetBandEdges(int band, double nyquist, out double low, out double high)
    {
        low = band == 0
            ? BandCenters[0]
            : Math.Sqrt(BandCenters[band - 1] * BandCenters[band]);
        high = band == BandCenters.Length - 1
            ? Math.Min(nyquist * 0.995d, BandCenters[^1] * Math.Pow(2d, 1d / 6d))
            : Math.Sqrt(BandCenters[band] * BandCenters[band + 1]);
        high = Math.Min(high, nyquist * 0.995d);
    }

    private void BlurBandPower()
    {
        var radius = (int)Math.Ceiling(BlurSigma * 4d);
        for (var band = 0; band < BandCenters.Length; band++)
        {
            var weighted = 0d;
            var weightSum = 0d;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var source = band + offset;
                if (source < 0 || source >= BandCenters.Length)
                {
                    continue;
                }

                var weight = Math.Exp(-(offset * offset) / (2d * BlurSigma * BlurSigma));
                weighted += _bandPower[source] * weight;
                weightSum += weight;
            }

            _blurredPower[band] = weightSum > 0d ? weighted / weightSum : 0d;
        }
    }

    private static float SoftenDisplayPeak(float db)
    {
        if (db <= PeakSoftKneeDb)
        {
            return db;
        }

        var span = CeilingDb - PeakSoftKneeDb;
        var normalized = (db - PeakSoftKneeDb) / span;
        return PeakSoftKneeDb + span * (float)Math.Pow(normalized, PeakSoftGamma);
    }

    private static float DbToLevel(float db) =>
        Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);

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
