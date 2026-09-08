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
    private static readonly Typeface ChromeTypeface = new(
        new FontFamily("Courier New"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private readonly SpectrumAnalyzer _analyzer = new();
    private readonly DispatcherTimer _timer;
    private readonly float[] _samples = new float[SpectrumAnalyzer.FftSize];
    private LinearGradientBrush? _barGradient;
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
        ClipToBounds = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

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
        var g = ComputePlot(wPx, hPx);
        var px = 1d / dpi;

        var chrome = WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush"));
        var plotBack = WpfControlHelpers.FrozenBrush(Theme.Get("VectorScopeBackBrush"));
        dc.DrawRectangle(chrome, null, bounds);
        var plot = new Rect(g.PlotX * px, g.PlotY * px, g.PlotW * px, g.PlotH * px);
        if (plot.Width <= 0 || plot.Height <= 0)
        {
            return;
        }

        dc.DrawRectangle(plotBack, null, plot);
        DrawDbGrid(dc, g, px);
        EnsureGradient();

        var bands = _analyzer.CurrentBands;
        var n = bands.Count;
        var rects = SpectrumAnalyzer.CreateBarRects(
            g.PlotX,
            g.PlotW,
            n,
            SpectrumAnalyzer.BarGutterDevicePx);
        var cells = SpectrumAnalyzer.CreateLedCells(g.PlotY, g.PlotH, SpectrumAnalyzer.FloorDb);
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
            var color = LevelMeterEngine.LevelColor(pkDb);
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

    private static Color LabelColor()
    {
        try
        {
            return Theme.Get("MutedForeBrush");
        }
        catch (InvalidOperationException)
        {
            return Color.FromRgb(0x96, 0x96, 0x96);
        }
    }

    private PlotGeometry ComputePlot(int wPx, int hPx)
    {
        var s = hPx / (double)SpectrumAnalyzer.OriginalOuterHeightPx;
        var padL = MeasureDbPadDevicePx();
        var padR = 0;
        var padT = 0;
        var minLabelH = Math.Max(
            (int)Math.Ceiling(ChromeFontSize * 2 + 2),
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

    private static void DrawDbGrid(DrawingContext dc, PlotGeometry g, double px)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("VectorScopeGridBrush")), 0.6);
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
        var brush = WpfControlHelpers.FrozenBrush(LabelColor());
        var font = ChromeFontSize;
        if (g.ShowDb)
        {
            var labels = new SortedSet<int> { (int)SpectrumAnalyzer.FloorDb };
            for (var db = -10; db >= (int)SpectrumAnalyzer.FloorDb; db -= 10)
            {
                labels.Add(db);
            }

            foreach (var db in labels)
            {
                var t = SpectrumAnalyzer.DbNorm(db);
                var y = (g.PlotY + g.PlotH * (1 - t)) * px;
                var ft = ChromeText(db.ToString(CultureInfo.InvariantCulture), font, brush);
                dc.DrawText(ft, new Point(g.PlotX * px - 3 - ft.Width, y - ft.Height * 0.5));
            }
        }

        if (!g.ShowHz)
        {
            return;
        }

        var maxF = bands.Count == 0 ? 0 : bands.Centers[^1] * 1.001;
        var row1y = (g.PlotY + g.PlotH + 1) * px;
        var row2y = row1y + ChromeText("0", font, brush).Height + 2;
        DrawFreqRow(dc, SpectrumAnalyzer.LabelTopRow, bands, rects, maxF, row1y, font, brush, px, g);
        DrawFreqRow(dc, SpectrumAnalyzer.LabelBotRow, bands, rects, maxF, row2y, font, brush, px, g);
    }

    private void DrawFreqRow(
        DrawingContext dc,
        (double Hz, string Text)[] row,
        SpectrumAnalyzer.Bands bands,
        SpectrumAnalyzer.BarRect[] rects,
        double maxF,
        double y,
        double font,
        Brush brush,
        double px,
        PlotGeometry g)
    {
        foreach (var (hz, text) in row)
        {
            if (hz > maxF)
            {
                continue;
            }

            var cx = BarCenterX(bands, rects, hz);
            if (cx is null)
            {
                continue;
            }

            var ft = ChromeText(text, font, brush);
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
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            ChromeTypeface,
            fontSize,
            brush,
            PixelsPerDip);

    private void EnsureGradient()
    {
        if (_barGradient is not null)
        {
            return;
        }

        // プロットは dB 直線。色はレベルメーターと同じ DbToNorm（-20 dB ニー）で取る。
        var stops = new GradientStopCollection();
        for (var db = SpectrumAnalyzer.FloorDb; db <= SpectrumAnalyzer.CeilingDb + 1e-4f; db += 5f)
        {
            var c = LevelMeterEngine.LevelColor(db);
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
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
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
