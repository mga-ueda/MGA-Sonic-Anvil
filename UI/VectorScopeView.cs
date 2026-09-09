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
    private const int MaxPoints = 360;
    private const double LayoutPad = 3;
    private const double CorrelationSidePad = 4;
    private const float SilentPeak = 0.0025f;
    private const double CorrelationAttack = 0.38;
    private const double CorrelationRelease = 0.16;
    private const float PersistFade = 0.78f;
    private const float BeamGhostFade = 0.88f;
    private const int BeamGhostHoldFrames = 36;
    private const float BeamHome = 0.07f;
    private const double BeamRadius = 1.4;
    private const double ScopeInset = 4;

    private readonly DispatcherTimer _timer;
    private readonly float[] _left = new float[LevelMeterEngine.WindowFrames];
    private readonly float[] _right = new float[LevelMeterEngine.WindowFrames];
    private readonly Point[] _trail = new Point[MaxPoints];
    private int _trailCount;
    private double _correlation;
    private float _displayGain = 1f;
    private float _paintFade = 1f;
    private float _beamNx;
    private float _beamNy;
    private float _beamFade;
    private float _prevBeamNx;
    private float _prevBeamNy;
    private bool _hasPrevBeam;
    private int _beamGhostHold;
    private bool _idle = true;
    private bool _beamSettled;
    private bool _wasHoming;
    private long _lastTickAt;
    private WriteableBitmap? _persist;
    private WriteableBitmap? _beamGhost;
    private int[] _persistPixels = [];
    private int[] _beamGhostPixels = [];
    private int _persistW;
    private int _persistH;

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

        if (_beamGhost is not null)
        {
            dc.DrawImage(_beamGhost, scope);
        }

        DrawTrail(dc);
        DrawBeam(dc, scope);
        dc.Pop();
    }

    private void DrawBeam(DrawingContext dc, Rect scope)
    {
        if (_beamFade < 0.02f)
        {
            return;
        }

        var trace = Theme.Get("VectorScopeTraceBrush");
        var alpha = (byte)Math.Clamp((int)Math.Round(240 * _beamFade), 0, 255);
        if (alpha < 8)
        {
            return;
        }

        ScopeGeometry(scope, out var cx, out var cy, out var radius);
        dc.DrawEllipse(
            WpfControlHelpers.FrozenBrush(Color.FromArgb(alpha, trace.R, trace.G, trace.B)),
            null,
            new Point(cx + _beamNx * radius, cy - _beamNy * radius),
            BeamRadius,
            BeamRadius);
    }

    private static void ScopeGeometry(Rect scope, out double cx, out double cy, out double radius)
    {
        cx = scope.X + scope.Width * 0.5;
        cy = scope.Y + scope.Height * 0.5;
        radius = Math.Max(4d, Math.Min(scope.Width, scope.Height) * 0.5 - ScopeInset);
    }

    private void DrawTrail(DrawingContext dc)
    {
        var fade = _paintFade;
        if (_trailCount < 2 || fade < 0.04f)
        {
            return;
        }

        var trace = Theme.Get("VectorScopeTraceBrush");
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(_trail[0], false, false);
            for (var i = 1; i < _trailCount; i++)
            {
                ctx.LineTo(_trail[i], true, true);
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
            _beamSettled = false;
            _beamGhostHold = 0;
            _beamFade = 1f;
            if (_wasHoming)
            {
                ClearBeamGhost();
                _wasHoming = false;
            }
        }
        else
        {
            _correlation *= 0.88;
            _displayGain += (1f - _displayGain) * 0.12f;
            _paintFade *= 0.86f;
            _beamFade += (1f - _beamFade) * BeamHome;
            EaseBeamToCenter();
            if (_paintFade < 0.04f)
            {
                _trailCount = 0;
                _correlation = 0;
                _paintFade = 0;
                ClearPersist();
                _idle = true;
            }

            if (_idle && BeamAtRest())
            {
                _beamNx = 0;
                _beamNy = 0;
                _beamFade = 1f;
                if (_beamSettled)
                {
                    return;
                }

                _beamGhostHold++;
                AdvancePersist();
                if (_beamGhostHold >= BeamGhostHoldFrames)
                {
                    ClearBeamGhost();
                    _hasPrevBeam = false;
                    _beamSettled = true;
                }

                InvalidateVisual();
                return;
            }

            _beamSettled = false;
            _beamGhostHold = 0;
            _wasHoming = true;
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

        FadePixels(_persistPixels, PersistFade, fadeColor: true, cutoff: 6);
        if (_trailCount >= 2 && _paintFade > 0.04f)
        {
            StampTrail(layout.Scope, _trail, _trailCount, _paintFade);
        }

        _persist!.WritePixels(new Int32Rect(0, 0, _persistW, _persistH), _persistPixels, _persistW * 4, 0);

        FadePixels(_beamGhostPixels, BeamGhostFade, fadeColor: false, cutoff: 3);
        StampBeam(layout.Scope);
        _beamGhost!.WritePixels(new Int32Rect(0, 0, _persistW, _persistH), _beamGhostPixels, _persistW * 4, 0);
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
        _beamGhostPixels = new int[w * h];
        _persist = new WriteableBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Bgra32, null);
        _beamGhost = new WriteableBitmap(w, h, 96 * dpi, 96 * dpi, PixelFormats.Bgra32, null);
        _hasPrevBeam = false;
        return true;
    }

    private static void FadePixels(int[] pixels, float fade, bool fadeColor, int cutoff)
    {
        for (var i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            if (p == 0)
            {
                continue;
            }

            var a = (int)(((p >> 24) & 0xFF) * fade);
            if (a < cutoff)
            {
                pixels[i] = 0;
                continue;
            }

            var r = (p >> 16) & 0xFF;
            var g = (p >> 8) & 0xFF;
            var b = p & 0xFF;
            if (fadeColor)
            {
                r = (int)(r * fade);
                g = (int)(g * fade);
                b = (int)(b * fade);
            }

            pixels[i] = (a << 24) | (r << 16) | (g << 8) | b;
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

    private void ClearBeamGhost()
    {
        if (_beamGhostPixels.Length == 0)
        {
            return;
        }

        Array.Clear(_beamGhostPixels);
        _beamGhost?.WritePixels(new Int32Rect(0, 0, _persistW, _persistH), _beamGhostPixels, _persistW * 4, 0);
        _hasPrevBeam = false;
    }

    private void StampBeam(Rect scope)
    {
        if (_idle && _beamSettled)
        {
            return;
        }

        if (_beamGhostHold > 0 || _beamFade < 0.04f || BeamAtRest())
        {
            _prevBeamNx = _beamNx;
            _prevBeamNy = _beamNy;
            _hasPrevBeam = true;
            return;
        }

        var player = Player;
        if (player is { IsPlaying: true } or { IsScrubbing: true })
        {
            _prevBeamNx = _beamNx;
            _prevBeamNy = _beamNy;
            _hasPrevBeam = true;
            return;
        }

        var color = Theme.Get("VectorScopeTraceBrush");
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * _beamFade), 0, 255);
        if (alpha < 8)
        {
            return;
        }

        ScopeGeometry(scope, out var cx, out var cy, out var radius);
        var sx = _persistW / Math.Max(1e-6, scope.Width);
        var sy = _persistH / Math.Max(1e-6, scope.Height);
        var x1 = (cx + _beamNx * radius - scope.X) * sx;
        var y1 = (cy - _beamNy * radius - scope.Y) * sy;
        if (_hasPrevBeam)
        {
            var x0 = (cx + _prevBeamNx * radius - scope.X) * sx;
            var y0 = (cy - _prevBeamNy * radius - scope.Y) * sy;
            StampSegment(_beamGhostPixels, x0, y0, x1, y1, color, alpha, glow: true);
        }
        else
        {
            StampDot(_beamGhostPixels, x1, y1, color, alpha, glow: true);
        }

        _prevBeamNx = _beamNx;
        _prevBeamNy = _beamNy;
        _hasPrevBeam = true;
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
            StampSegment(_persistPixels, x0, y0, x1, y1, color, alpha, glow: false);
        }
    }

    private void StampSegment(int[] dest, double x0, double y0, double x1, double y1, Color color, byte alpha, bool glow)
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
            StampDot(dest, x, y, color, alpha, glow);
        }
    }

    private void StampDot(int[] dest, double x, double y, Color color, byte alpha, bool glow)
    {
        var ix = (int)Math.Round(x);
        var iy = (int)Math.Round(y);
        StampPixel(dest, ix, iy, color, alpha);
        if (!glow)
        {
            return;
        }

        var side = (byte)Math.Clamp(alpha * 160 / 255, 0, 255);
        StampPixel(dest, ix - 1, iy, color, side);
        StampPixel(dest, ix + 1, iy, color, side);
        StampPixel(dest, ix, iy - 1, color, side);
        StampPixel(dest, ix, iy + 1, color, side);
    }

    private void StampPixel(int[] dest, int ix, int iy, Color color, byte alpha)
    {
        if ((uint)ix >= (uint)_persistW || (uint)iy >= (uint)_persistH || alpha < 4)
        {
            return;
        }

        var i = iy * _persistW + ix;
        var p = dest[i];
        var a0 = (p >> 24) & 0xFF;
        var r0 = (p >> 16) & 0xFF;
        var g0 = (p >> 8) & 0xFF;
        var b0 = p & 0xFF;
        var a1 = Math.Max(a0, (int)alpha);
        var r1 = Math.Max(r0, (color.R * alpha) / 255);
        var g1 = Math.Max(g0, (color.G * alpha) / 255);
        var b1 = Math.Max(b0, (color.B * alpha) / 255);
        dest[i] = (a1 << 24) | (r1 << 16) | (g1 << 8) | b1;
    }

    private bool BeamAtRest() =>
        Math.Abs(_beamNx) < 0.004f && Math.Abs(_beamNy) < 0.004f && _beamFade >= 0.995f;

    private void EaseBeamToCenter()
    {
        _beamNx += (0f - _beamNx) * BeamHome;
        _beamNy += (0f - _beamNy) * BeamHome;
        if (Math.Abs(_beamNx) < 0.004f)
        {
            _beamNx = 0;
        }

        if (Math.Abs(_beamNy) < 0.004f)
        {
            _beamNy = 0;
        }
    }

    private void SnapBeam(float mid, float side, float scale)
    {
        _beamNx = Math.Clamp(side * scale, -1f, 1f);
        _beamNy = Math.Clamp(mid * scale, -1f, 1f);
        _beamFade = 1f;
    }

    private void CaptureTrail()
    {
        var peak = VectorScopeEngine.PeakMidSide(_left, _right);
        if (peak < SilentPeak)
        {
            _trailCount = 0;
            _displayGain += (1f - _displayGain) * 0.08f;
            EaseBeamToCenter();
            return;
        }

        var targetGain = Math.Clamp(0.88f / peak, 1f, 6f);
        var follow = targetGain > _displayGain ? 0.28f : 0.06f;
        _displayGain += (targetGain - _displayGain) * follow;

        var layout = MeasureLayout(new Rect(RenderSize));
        ScopeGeometry(layout.Scope, out var cx, out var cy, out var radius);
        var scale = Math.Min(_displayGain, 0.88f / peak);

        var available = LevelMeterEngine.WindowFrames;
        var stride = Math.Max(1, available / MaxPoints);
        var dest = _trail;
        var count = 0;
        var minX = layout.Scope.X + ScopeInset;
        var maxX = layout.Scope.Right - ScopeInset;
        var minY = layout.Scope.Y + ScopeInset;
        var maxY = layout.Scope.Bottom - ScopeInset;
        for (var i = 0; i < available && count < MaxPoints; i += stride)
        {
            VectorScopeEngine.MidSide(_left[i], _right[i], out var mid, out var side);
            dest[count] = new Point(
                Math.Clamp(cx + side * radius * scale, minX, maxX),
                Math.Clamp(cy - mid * radius * scale, minY, maxY));
            count++;
        }

        _trailCount = count;
        VectorScopeEngine.MidSide(_left[available - 1], _right[available - 1], out var nowMid, out var nowSide);
        SnapBeam(nowMid, nowSide, scale);
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
