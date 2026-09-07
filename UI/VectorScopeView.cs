using System.Globalization;
using System.Windows;
using System.Windows.Media;
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
    private const double LayoutPad = 1;
    private const float SilentPeak = 0.0025f;
    private const double CorrelationAttack = 0.38;
    private const double CorrelationRelease = 0.16;

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

        var layout = MeasureLayout(bounds);
        DrawScope(dc, layout.Scope);
        DrawCorrelation(dc, layout.Correlation);
    }

    private static (Rect Scope, Rect Correlation) MeasureLayout(Rect bounds)
    {
        var corrHeight = DesignMetrics.VectorScopeCorrelationHeight;
        var correlation = new Rect(
            bounds.Left,
            bounds.Top,
            Math.Max(8d, bounds.Width),
            corrHeight);
        var side = Math.Max(8d, Math.Min(bounds.Width, bounds.Height - corrHeight));
        var scope = new Rect(
            bounds.Left + (bounds.Width - side) * 0.5,
            correlation.Bottom,
            side,
            side);
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
        var trace = Theme.Get("VectorScopeTraceBrush");
        for (var layer = 0; layer < HistoryLayers; layer++)
        {
            var index = (_trailWrite + layer) % HistoryLayers;
            var count = _trailCounts[index];
            if (count < 2)
            {
                continue;
            }

            var age = layer / (double)Math.Max(1, HistoryLayers - 1);
            var alpha = (byte)Math.Round(255 * _paintFade * (0.16 + 0.84 * age));
            if (alpha < 8)
            {
                continue;
            }

            var pen = new Pen(
                WpfControlHelpers.FrozenBrush(Color.FromArgb(alpha, trace.R, trace.G, trace.B)),
                layer == HistoryLayers - 1 ? 0.7 : 0.5);
            pen.Freeze();
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
            dc.DrawGeometry(null, pen, geometry);
        }

        dc.Pop();
    }

    private void DrawCorrelation(DrawingContext dc, Rect area)
    {
        var trackHeight = 5d;
        var track = new Rect(area.X, area.Y + 2, area.Width, trackHeight);
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
        dc.DrawText(left, new Point(area.X, labelY));
        dc.DrawText(right, new Point(area.Right - right.Width, labelY));
        dc.DrawText(value, new Point(area.X + (area.Width - value.Width) * 0.5, labelY));
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
                if (_idle)
                {
                    return;
                }

                _idle = true;
            }
        }

        InvalidateVisual();
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
