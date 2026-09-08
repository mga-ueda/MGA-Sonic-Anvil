using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 再生出力のベクターオーディオスコープ（Mid/Side）と位相相関。
/// </summary>
internal sealed class VectorScopeView : FrameworkElement
{
    private const int HistoryLayers = 6;
    private const int MaxPoints = 360;
    private const double LayoutPad = 3;
    private const double CorrelationSidePad = 4;
    private const float SilentPeak = 0.0025f;
    private const double CorrelationAttack = 0.38;
    private const double CorrelationRelease = 0.16;
    private const float PersistFade = 0.78f;

    private readonly DispatcherTimer _timer;
    private readonly float[] _left = new float[LevelMeterEngine.WindowFrames];
    private readonly float[] _right = new float[LevelMeterEngine.WindowFrames];
    private readonly Point[][] _trails = new Point[HistoryLayers][];
    private readonly int[] _trailCounts = new int[HistoryLayers];
    private int _trailWrite;
    private double _correlation;
    private float _displayGain = 1f;
    private float _paintFade = 1f;
    private bool _idle = true;
    private long _lastTickAt;
    private WriteableBitmap? _persist;
    private int[] _persistPixels = [];
    private int _persistW;
    private int _persistH;

    public static readonly DependencyProperty BackgroundProperty =
        System.Windows.Controls.Control.BackgroundProperty.AddOwner(
            typeof(VectorScopeView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public VectorScopeView()
    {
        Width = DesignMetrics.LevelMeterWidth;
        Height = DesignMetrics.VectorScopeHeight;
        MinWidth = DesignMetrics.LevelMeterWidth;
        MinHeight = DesignMetrics.VectorScopeHeight;
        MaxWidth = DesignMetrics.LevelMeterWidth;
        MaxHeight = DesignMetrics.VectorScopeHeight;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Top;
        Focusable = false;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        for (var i = 0; i < HistoryLayers; i++)
        {
            _trails[i] = new Point[MaxPoints];
        }

        // 停止後の減衰用。再生中は Background 優先度がマウス入力に飢餓するため、
        // MainWindow の CompositionTarget.Rendering から Tick() で駆動される。
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public AudioPlayer? Player { get; set; }

    /// <summary>
    /// 更新を 1 回試みる。33ms 未満の連続呼び出しは無視するので、
    /// フレーム駆動とタイマーの両方から呼んでも二重更新しない。
    /// </summary>
    public void Tick()
    {
        var now = Environment.TickCount64;
        if (now - _lastTickAt < 33)
        {
            return;
        }

        _lastTickAt = now;
        UpdateScope();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        var layout = MeasureLayout(bounds);
        DrawScope(dc, layout.Scope);
        DrawCorrelation(dc, layout.Correlation);
    }

    private static (Rect Scope, Rect Correlation) MeasureLayout(Rect bounds)
    {
        var corrHeight = DesignMetrics.VectorScopeCorrelationHeight;
        var side = Math.Max(8d, Math.Min(bounds.Width, bounds.Height - corrHeight));
        var scope = new Rect(
            bounds.Left + (bounds.Width - side) * 0.5,
            bounds.Top,
            side,
            side);
        var correlation = new Rect(
            bounds.Left,
            scope.Bottom,
            Math.Max(8d, bounds.Width),
            corrHeight);
        return (scope, correlation);
    }

    private void DrawScope(DrawingContext dc, Rect scope)
    {
        var back = Theme.Get("VectorScopeBackBrush");
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(back), null, scope);

        var grid = Theme.Get("VectorScopeGridBrush");
        var gridPen = new Pen(WpfControlHelpers.FrozenBrush(grid), 0.6);
        gridPen.Freeze();
        var midX = scope.X + scope.Width * 0.5;
        var midY = scope.Y + scope.Height * 0.5;
        dc.DrawLine(gridPen, new Point(midX, scope.Y + 2), new Point(midX, scope.Bottom - 2));
        dc.DrawLine(gridPen, new Point(scope.X + 2, midY), new Point(scope.Right - 2, midY));

        var clip = new RectangleGeometry(scope, 3, 3);
        dc.PushClip(clip);
        if (_persist is not null)
        {
            dc.DrawImage(_persist, scope);
        }

        DrawTrail(dc, LatestTrailIndex(), _paintFade);
        dc.Pop();
    }

    private int LatestTrailIndex() =>
        (_trailWrite + HistoryLayers - 1) % HistoryLayers;

    private void DrawTrail(DrawingContext dc, int index, float fade)
    {
        var count = _trailCounts[index];
        if (count < 2 || fade < 0.04f)
        {
            return;
        }

        var trace = Theme.Get("VectorScopeTraceBrush");
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_trails[index][0], false, false);
            for (var i = 1; i < count; i++)
            {
                ctx.LineTo(_trails[index][i], true, true);
            }
        }

        geometry.Freeze();
        var alpha = (byte)Math.Round(255 * fade);
        if (alpha < 8)
        {
            return;
        }

        var pen = new Pen(
            WpfControlHelpers.FrozenBrush(Color.FromArgb(alpha, trace.R, trace.G, trace.B)),
            0.7);
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawCorrelation(DrawingContext dc, Rect area)
    {
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, area);
        var trackHeight = 5d;
        var track = new Rect(
            area.X + CorrelationSidePad,
            area.Y + LayoutPad,
            Math.Max(8d, area.Width - CorrelationSidePad * 2),
            trackHeight);
        var muted = Theme.Get("MutedForeBrush");
        dc.DrawRoundedRectangle(
            WpfControlHelpers.FrozenBrush(Color.FromArgb(70, muted.R, muted.G, muted.B)),
            null,
            track,
            1.5,
            1.5);

        var t = (_correlation + 1d) * 0.5;
        var needleX = track.X + Math.Clamp(t, 0d, 1d) * track.Width;
        var needle = Theme.Get("VectorScopeCorrelationBrush");
        if (_correlation < 0)
        {
            needle = Theme.Get("DirtyAccentBrush");
        }

        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(needle),
            null,
            new Rect(needleX - 1.5, track.Y, 3, track.Height));

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dim = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var left = Measure("-1", 8, dim, dpi);
        var right = Measure("+1", 8, dim, dpi);
        var value = Measure(VectorScopeEngine.FormatCorrelation(_correlation), 8, dim, dpi);
        var labelY = track.Bottom + 1;
        dc.DrawText(left, new Point(track.X, labelY));
        dc.DrawText(right, new Point(track.Right - right.Width, labelY));
        dc.DrawText(value, new Point(track.X + (track.Width - value.Width) * 0.5, labelY));
    }

    private void UpdateScope()
    {
        if (!IsVisible || ActualWidth < 8 || ActualHeight < 8)
        {
            return;
        }

        var player = Player;
        var active = player is { IsPlaying: true } or { IsScrubbing: true };
        if (active)
        {
            player!.CopyMeterWindow(_left, _right);
            CaptureTrail();
            var raw = VectorScopeEngine.Correlation(_left, _right);
            var mix = raw >= _correlation ? CorrelationAttack : CorrelationRelease;
            _correlation += (raw - _correlation) * mix;
            _paintFade = 1f;
            _idle = false;
        }
        else
        {
            _correlation *= 0.88;
            _displayGain += (1f - _displayGain) * 0.12f;
            _paintFade *= 0.86f;
            if (_paintFade < 0.04f)
            {
                Array.Clear(_trailCounts);
                _correlation = 0;
                _paintFade = 0;
                ClearPersist();
                if (_idle)
                {
                    return;
                }

                _idle = true;
            }
        }

        AdvancePersist();
        InvalidateVisual();
    }

    private void AdvancePersist()
    {
        var layout = MeasureLayout(new Rect(RenderSize));
        if (!EnsurePersist(layout.Scope))
        {
            return;
        }

        FadePersist();
        var latest = LatestTrailIndex();
        if (_trailCounts[latest] >= 2 && _paintFade > 0.04f)
        {
            StampTrail(layout.Scope, _trails[latest], _trailCounts[latest], _paintFade);
        }

        _persist!.WritePixels(new Int32Rect(0, 0, _persistW, _persistH), _persistPixels, _persistW * 4, 0);
    }

    private bool EnsurePersist(Rect scope)
    {
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var w = Math.Max(8, (int)Math.Round(scope.Width * dpi));
        var h = Math.Max(8, (int)Math.Round(scope.Height * dpi));
        if (_persist is not null && _persistW == w && _persistH == h)
        {
            return true;
        }

        _persistW = w;
        _persistH = h;
        _persistPixels = new int[w * h];
        _persist = new WriteableBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Bgra32, null);
        return true;
    }

    private void FadePersist()
    {
        for (var i = 0; i < _persistPixels.Length; i++)
        {
            var p = _persistPixels[i];
            if (p == 0)
            {
                continue;
            }

            var a = (int)(((p >> 24) & 0xFF) * PersistFade);
            if (a < 6)
            {
                _persistPixels[i] = 0;
                continue;
            }

            var r = (int)(((p >> 16) & 0xFF) * PersistFade);
            var g = (int)(((p >> 8) & 0xFF) * PersistFade);
            var b = (int)((p & 0xFF) * PersistFade);
            _persistPixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
        }
    }

    private void ClearPersist()
    {
        if (_persistPixels.Length == 0)
        {
            return;
        }

        Array.Clear(_persistPixels);
        _persist?.WritePixels(new Int32Rect(0, 0, _persistW, _persistH), _persistPixels, _persistW * 4, 0);
    }

    private void StampTrail(Rect scope, Point[] points, int count, float fade)
    {
        var color = Theme.Get("VectorScopeTraceBrush");
        var alpha = (byte)Math.Clamp((int)Math.Round(210 * fade), 0, 255);
        if (alpha < 8)
        {
            return;
        }

        var sx = _persistW / Math.Max(1e-6, scope.Width);
        var sy = _persistH / Math.Max(1e-6, scope.Height);
        for (var i = 1; i < count; i++)
        {
            var x0 = (points[i - 1].X - scope.X) * sx;
            var y0 = (points[i - 1].Y - scope.Y) * sy;
            var x1 = (points[i].X - scope.X) * sx;
            var y1 = (points[i].Y - scope.Y) * sy;
            StampSegment(x0, y0, x1, y1, color, alpha);
        }
    }

    private void StampSegment(double x0, double y0, double x1, double y1, Color color, byte alpha)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dy))));
        var inv = 1d / steps;
        for (var i = 0; i <= steps; i++)
        {
            var t = i * inv;
            var x = x0 + dx * t;
            var y = y0 + dy * t;
            StampDot(x, y, color, alpha);
        }
    }

    private void StampDot(double x, double y, Color color, byte alpha)
    {
        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);
        if ((uint)ix >= (uint)_persistW || (uint)iy >= (uint)_persistH || alpha < 4)
        {
            return;
        }

        var i = iy * _persistW + ix;
        var p = _persistPixels[i];
        var a0 = (p >> 24) & 0xFF;
        var r0 = (p >> 16) & 0xFF;
        var g0 = (p >> 8) & 0xFF;
        var b0 = p & 0xFF;
        var a1 = Math.Max(a0, alpha);
        var r1 = Math.Max(r0, (color.R * alpha) / 255);
        var g1 = Math.Max(g0, (color.G * alpha) / 255);
        var b1 = Math.Max(b0, (color.B * alpha) / 255);
        _persistPixels[i] = (a1 << 24) | (r1 << 16) | (g1 << 8) | b1;
    }

    private void CaptureTrail()
    {
        var peak = VectorScopeEngine.PeakMidSide(_left, _right);
        if (peak < SilentPeak)
        {
            _trailCounts[_trailWrite] = 0;
            _trailWrite = (_trailWrite + 1) % HistoryLayers;
            _displayGain += (1f - _displayGain) * 0.08f;
            return;
        }

        var targetGain = Math.Clamp(0.88f / peak, 1f, 6f);
        var follow = targetGain > _displayGain ? 0.28f : 0.06f;
        _displayGain += (targetGain - _displayGain) * follow;

        var layout = MeasureLayout(new Rect(RenderSize));
        var cx = layout.Scope.X + layout.Scope.Width * 0.5;
        var cy = layout.Scope.Y + layout.Scope.Height * 0.5;
        var inset = 4d;
        var radius = Math.Max(4d, Math.Min(layout.Scope.Width, layout.Scope.Height) * 0.5 - inset);
        var scale = Math.Min(_displayGain, 0.88f / peak);

        var available = LevelMeterEngine.WindowFrames;
        var stride = Math.Max(1, available / MaxPoints);
        var dest = _trails[_trailWrite];
        var count = 0;
        var minX = layout.Scope.X + inset;
        var maxX = layout.Scope.Right - inset;
        var minY = layout.Scope.Y + inset;
        var maxY = layout.Scope.Bottom - inset;
        for (var i = 0; i < available && count < MaxPoints; i += stride)
        {
            VectorScopeEngine.MidSide(_left[i], _right[i], out var mid, out var side);
            dest[count] = new Point(
                Math.Clamp(cx + side * radius * scale, minX, maxX),
                Math.Clamp(cy - mid * radius * scale, minY, maxY));
            count++;
        }

        _trailCounts[_trailWrite] = count;
        _trailWrite = (_trailWrite + 1) % HistoryLayers;
    }

    private static FormattedText Measure(string text, double size, Brush brush, double dpi) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            size,
            brush,
            dpi);
}
