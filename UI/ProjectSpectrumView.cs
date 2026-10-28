using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// Layer Music Checker の LED スペアナを、トランスポート横の狭い枠に収めたもの。
/// </summary>
internal sealed class ProjectSpectrumView : FrameworkElement
{
    /// <summary>Courier で読める下限。メーター目盛の 8 より一段小さい。</summary>
    private const double ChromeFontSize = 7;

    /// <summary>Hz 2 行ラベルの行送り（FormattedText.Height に対する比率）。</summary>
    private const double FreqLabelRowStride = 0.72;

    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[SpectrumAnalyzer.FftSize];
    private LinearGradientBrush? _barGradient;
    private bool _idle = true;
    private long _lastTickAt;

    // サイズ／DPI／帯域数／ラベル色が同じあいだ再利用（毎 OnRender の割り当てを避ける）。
    private int _cacheWPx = -1;
    private int _cacheHPx = -1;
    private double _cacheDpi;
    private int _cacheBandCount = -1;
    private Color _cacheLabelColor;
    private PlotGeometry _cachePlot;
    private SpectrumAnalyzer.BarRect[] _cacheRects = [];
    private SpectrumAnalyzer.LedCells _cacheCells;
    private FormattedText[] _cacheDbLabels = [];
    private int[] _cacheDbValues = [];
    private FormattedText[] _cacheHzTop = [];
    private FormattedText[] _cacheHzBot = [];
    private FormattedText? _cacheZeroHeight;

    public static readonly DependencyProperty BackgroundProperty =
        System.Windows.Controls.Control.BackgroundProperty.AddOwner(
            typeof(ProjectSpectrumView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>プレイヤー中はクロム塗りをせず、ジャケットウォッシュを透かす。</summary>
    internal bool WashThrough { get; set; }

    /// <summary>動画本再生／ミニ限定。dB／Hz ラベルをファイル名と同じ白系にする。</summary>
    internal bool VideoOverlayLabels
    {
        get => _videoOverlayLabels;
        set
        {
            if (_videoOverlayLabels == value)
            {
                return;
            }

            _videoOverlayLabels = value;
            InvalidateLayoutCache();
            InvalidateVisual();
        }
    }

    private bool _videoOverlayLabels;

    public ProjectSpectrumView()
    {
        MinHeight = DesignMetrics.SpectrumHeight;
        Focusable = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public void StopTicks() => _timer.Stop();

    public void RefreshAppearance()
    {
        _barGradient = null;
        InvalidateLayoutCache();
        InvalidateVisual();
    }

    private void InvalidateLayoutCache() => _cacheWPx = -1;

    protected override Size MeasureOverride(Size availableSize)
    {
        var height = DesignMetrics.SpectrumHeight;
        var width = DesignMetrics.LevelMeterWidth * SpectrumAnalyzer.OriginalAspect
            + DesignMetrics.SpectrumExtraWidth;
        return new Size(width, height);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateLayoutCache();
        InvalidateMeasure();
    }

    public AudioPlayer? Player { get; set; }

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

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        var dpi = PixelsPerDip;
        var wPx = Math.Max(1, (int)Math.Floor(bounds.Width * dpi));
        var hPx = Math.Max(1, (int)Math.Floor(bounds.Height * dpi));
        var bands = _analyzer.CurrentBands;
        EnsureLayoutCache(wPx, hPx, dpi, bands);
        var g = _cachePlot;
        var px = 1d / dpi;
        var rects = _cacheRects;
        var cells = _cacheCells;

        if (!WashThrough)
        {
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        }
        var plot = new Rect(g.PlotX * px, g.PlotY * px, g.PlotW * px, g.PlotH * px);
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        if (!WashThrough)
        {
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("VectorScopeBackBrush")), null, plot);
        }
        DrawDbGrid(dc, g, px, WashThrough);
        EnsureGradient();

        var n = bands.Count;
        var env = _analyzer.EnvelopeDb;
        var hold = _analyzer.PeakHoldDb;

        for (var b = 0; b < n && b < rects.Length; b++)
        {
            var barDb = b < env.Length ? env[b] : SpectrumAnalyzer.FloorDb;
            var lastLit = LastLitRow(cells, barDb);
            if (lastLit < 0 || _barGradient is null)
            {
                continue;
            }

            var lit = DipRect(
                rects[b].X1,
                cells.Top[lastLit],
                rects[b].BarW,
                cells.Bot[0] - cells.Top[lastLit],
                px);
            if (lit.Height > 0)
            {
                dc.PushClip(new RectangleGeometry(lit));
                dc.PushOpacity(LevelMeterEngine.BarFillOpacity);
                dc.DrawRectangle(_barGradient, null, plot);
                dc.Pop();
                dc.Pop();
            }
        }

        for (var b = 0; b < n && b < rects.Length && b < hold.Length; b++)
        {
            var pkDb = hold[b];
            if (pkDb <= SpectrumAnalyzer.FloorDb + 1e-4f)
            {
                continue;
            }

            var iPk = Math.Clamp((int)Math.Floor(pkDb) - cells.LoInt, 0, cells.Count - 1);
            var color = LevelColorTheme.PeakHold(pkDb);
            var peak = DipRect(
                rects[b].X1,
                cells.Top[iPk],
                rects[b].BarW,
                cells.Bot[iPk] - cells.Top[iPk],
                px);
            if (peak.Height > 0)
            {
                dc.PushOpacity(LevelMeterEngine.BarFillOpacity);
                dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Color.FromRgb(color.R, color.G, color.B)), null, peak);
                dc.Pop();
            }
        }

        DrawChromeLabels(dc, g, bands, rects, px);
    }

    private void EnsureLayoutCache(int wPx, int hPx, double dpi, SpectrumAnalyzer.Bands bands)
    {
        var labelColor = LabelColor();
        if (_cacheWPx == wPx
            && _cacheHPx == hPx
            && Math.Abs(_cacheDpi - dpi) <= 0.001
            && _cacheBandCount == bands.Count
            && _cacheLabelColor == labelColor
            && _cacheRects.Length == bands.Count)
        {
            return;
        }

        _cacheWPx = wPx;
        _cacheHPx = hPx;
        _cacheDpi = dpi;
        _cacheBandCount = bands.Count;
        _cacheLabelColor = labelColor;
        _cachePlot = ComputePlot(wPx, hPx);
        _cacheRects = SpectrumAnalyzer.CreateBarRects(
            _cachePlot.PlotX,
            _cachePlot.PlotW,
            bands.Count,
            SpectrumAnalyzer.BarGutterDevicePx);
        _cacheCells = SpectrumAnalyzer.CreateLedCells(
            _cachePlot.PlotY,
            _cachePlot.PlotH,
            SpectrumAnalyzer.FloorDb);

        var brush = WpfControlHelpers.FrozenBrush(labelColor);
        var labels = new SortedSet<int> { (int)SpectrumAnalyzer.FloorDb };
        for (var db = -10; db >= (int)SpectrumAnalyzer.FloorDb; db -= 10)
        {
            labels.Add(db);
        }

        _cacheDbValues = [.. labels];
        _cacheDbLabels = new FormattedText[_cacheDbValues.Length];
        for (var i = 0; i < _cacheDbValues.Length; i++)
        {
            _cacheDbLabels[i] = ChromeText(
                _cacheDbValues[i].ToString(CultureInfo.InvariantCulture),
                ChromeFontSize,
                brush);
        }

        _cacheZeroHeight = ChromeText("0", ChromeFontSize, brush);
        _cacheHzTop = new FormattedText[SpectrumAnalyzer.LabelTopRow.Length];
        for (var i = 0; i < SpectrumAnalyzer.LabelTopRow.Length; i++)
        {
            _cacheHzTop[i] = ChromeText(SpectrumAnalyzer.LabelTopRow[i].Text, ChromeFontSize, brush);
        }

        _cacheHzBot = new FormattedText[SpectrumAnalyzer.LabelBotRow.Length];
        for (var i = 0; i < SpectrumAnalyzer.LabelBotRow.Length; i++)
        {
            _cacheHzBot[i] = ChromeText(SpectrumAnalyzer.LabelBotRow[i].Text, ChromeFontSize, brush);
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
            _analyzer.Process(_samples, player.OutputSampleRate, dtMs / 1000d, active: true);
            _idle = false;
        }
        else
        {
            _analyzer.Process(_samples, player?.OutputSampleRate ?? 48000, dtMs / 1000d, active: false);
            if (!_analyzer.HasVisibleLevel)
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

    private int MeasureDbPadDevicePx()
    {
        var ft = ChromeText("-50", ChromeFontSize, WpfControlHelpers.FrozenBrush(LabelColor()));
        return Math.Max(1, (int)Math.Ceiling((ft.Width + 3) * PixelsPerDip));
    }

    private Color LabelColor() =>
        VideoOverlayLabels
            ? LibraryPlayerMode.VideoOverlayLabelColor
            : Theme.Get("MutedForeBrush");

    private PlotGeometry ComputePlot(int wPx, int hPx)
    {
        var s = hPx / (double)SpectrumAnalyzer.OriginalOuterHeightPx;
        var padL = MeasureDbPadDevicePx();
        var padR = 0;
        var padT = 0;
        // 2 行 Hz ラベルは行間を詰めた実寸で予約（FormattedText.Height はフォントより大きい）。
        var lineH = _cacheZeroHeight?.Height ?? (ChromeFontSize * 1.25);
        var rowStride = Math.Max(ChromeFontSize, lineH * FreqLabelRowStride);
        var minLabelH = Math.Max(
            (int)Math.Ceiling(1 + rowStride + lineH),
            (int)Math.Round(SpectrumAnalyzer.OrigFreqLabelPx * s));
        var insetL = Math.Max(1, (int)Math.Round(SpectrumAnalyzer.OrigInsetLeftPx * s));
        var insetR = 0;
        var plotX = padL + insetL;
        var plotY = padT;
        var meterH = Math.Max(1, (int)Math.Round(DesignMetrics.LevelMeterWidth * PixelsPerDip));
        var plotH = Math.Max(1, Math.Min(hPx - minLabelH, meterH));
        var plotW = Math.Max(1, wPx - padL - padR - insetL - insetR);
        return new PlotGeometry(plotX, plotY, plotW, plotH, padL, padR, ShowDb: true, ShowHz: true, wPx, hPx);
    }

    private static void DrawDbGrid(DrawingContext dc, PlotGeometry g, double px, bool player)
    {
        var pen = new Pen(
            WpfControlHelpers.FrozenBrush(PlayerMeterChrome.Grid("VectorScopeGridBrush", player)),
            0.6);
        pen.Freeze();
        var x0 = g.PlotX * px;
        var x1 = (g.PlotX + g.PlotW) * px;
        for (var db = -10; db >= (int)SpectrumAnalyzer.FloorDb; db -= 10)
        {
            var y = (g.PlotY + g.PlotH * (1 - SpectrumAnalyzer.DbNorm(db))) * px;
            dc.DrawLine(pen, new Point(x0, y), new Point(x1, y));
        }
    }

    private void DrawChromeLabels(
        DrawingContext dc,
        PlotGeometry g,
        SpectrumAnalyzer.Bands bands,
        SpectrumAnalyzer.BarRect[] rects,
        double px)
    {
        if (g.ShowDb)
        {
            for (var i = 0; i < _cacheDbLabels.Length && i < _cacheDbValues.Length; i++)
            {
                var db = _cacheDbValues[i];
                var ft = _cacheDbLabels[i];
                var t = SpectrumAnalyzer.DbNorm(db);
                var y = (g.PlotY + g.PlotH * (1 - t)) * px;
                dc.DrawText(ft, new Point(g.PlotX * px - 3 - ft.Width, y - ft.Height * 0.5));
            }
        }

        if (!g.ShowHz || _cacheZeroHeight is null)
        {
            return;
        }

        var maxF = bands.Count == 0 ? 0 : bands.Centers[^1] * 1.001;
        var row1y = (g.PlotY + g.PlotH + 1) * px;
        var row2y = row1y + Math.Max(ChromeFontSize, _cacheZeroHeight.Height * FreqLabelRowStride);
        DrawFreqRow(dc, SpectrumAnalyzer.LabelTopRow, _cacheHzTop, bands, rects, maxF, row1y, px, g);
        DrawFreqRow(dc, SpectrumAnalyzer.LabelBotRow, _cacheHzBot, bands, rects, maxF, row2y, px, g);
    }

    private static void DrawFreqRow(
        DrawingContext dc,
        (double Hz, string Text)[] row,
        FormattedText[] texts,
        SpectrumAnalyzer.Bands bands,
        SpectrumAnalyzer.BarRect[] rects,
        double maxF,
        double y,
        double px,
        PlotGeometry g)
    {
        for (var i = 0; i < row.Length && i < texts.Length; i++)
        {
            var (hz, _) = row[i];
            if (hz > maxF)
            {
                continue;
            }

            var cx = BarCenterX(bands, rects, hz);
            if (cx is null)
            {
                continue;
            }

            var ft = texts[i];
            var x = cx.Value * px - ft.Width * 0.5;
            var maxX = Math.Max(0, g.WidthPx * px - ft.Width);
            dc.DrawText(ft, new Point(Math.Clamp(x, 0, maxX), y));
        }
    }

    private static double? BarCenterX(
        SpectrumAnalyzer.Bands bands,
        SpectrumAnalyzer.BarRect[] rects,
        double hz)
    {
        var tol = Math.Max(5e-4, Math.Abs(hz) * 1e-12);
        for (var b = 0; b < bands.Count && b < rects.Length; b++)
        {
            if (Math.Abs(bands.Centers[b] - hz) <= tol)
            {
                return rects[b].X1 + rects[b].BarW * 0.5;
            }
        }

        return null;
    }

    private static int LastLitRow(SpectrumAnalyzer.LedCells cells, float barDb)
    {
        var last = -1;
        for (var i = 0; i < cells.Count; i++)
        {
            var segLo = cells.LoInt + i;
            var segHi = segLo + 1;
            if (segLo < barDb && segHi > SpectrumAnalyzer.FloorDb)
            {
                last = i;
            }
        }

        return last;
    }

    private static Rect DipRect(double x, double y, double w, double h, double px) =>
        new(x * px, y * px, Math.Max(0, w) * px, Math.Max(0, h) * px);

    private FormattedText ChromeText(string text, double fontSize, Brush brush) =>
        WpfControlHelpers.MonoRegularText(text, fontSize, brush, PixelsPerDip);

    private void EnsureGradient()
    {
        if (_barGradient is not null)
        {
            return;
        }

        // プロットは dB 直線。色も同じ軸で一本につなぐ。
        var stops = new GradientStopCollection();
        for (var db = SpectrumAnalyzer.FloorDb; db <= SpectrumAnalyzer.CeilingDb + 1e-4f; db += LevelColorTheme.GradientStepDb)
        {
            var c = LevelColorTheme.Of(db);
            stops.Add(new GradientStop(Color.FromRgb(c.R, c.G, c.B), SpectrumAnalyzer.DbNorm(db)));
        }

        _barGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops = stops,
        };
        _barGradient.Freeze();
    }

    private double PixelsPerDip
    {
        get
        {
            var dpi = UiDpi.Get(this).PixelsPerDip;
            return dpi > 0 ? dpi : 1d;
        }
    }

    private readonly record struct PlotGeometry(
        int PlotX,
        int PlotY,
        int PlotW,
        int PlotH,
        int PadL,
        int PadR,
        bool ShowDb,
        bool ShowHz,
        int WidthPx,
        int HeightPx);
}
