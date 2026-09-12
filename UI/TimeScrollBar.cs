using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MgaSonicAnvil.UI;

internal sealed class TimeScrollBar : ScrollBar
{
    private enum EdgeDrag
    {
        None,
        Left,
        Right,
    }

    private long _totalFrames;
    private double _viewStart;
    private double _viewSpan = 1;
    private EdgeDrag _edgeDrag;
    private double _dragOriginX;
    private double _dragOriginStart;
    private double _dragOriginSpan;

    public TimeScrollBar()
    {
        Orientation = Orientation.Horizontal;
        Minimum = 0;
        Maximum = 0;
        ViewportSize = 1;
        SmallChange = 1;
        LargeChange = 1;
        Focusable = false;
        IsEnabled = false;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        // 派生型には App.xaml の ScrollBar 暗黙スタイルが当たらない。
        SetResourceReference(StyleProperty, "TimeScrollBarStyle");
        SetResourceReference(BackgroundProperty, "TimelineWellBackBrush");
    }

    public event EventHandler<TimeScrollView>? RangeChanged;

    public bool IsRangeResizing => _edgeDrag != EdgeDrag.None;

    public void Sync(double viewStart, double viewSpan, long totalFrames)
    {
        _totalFrames = totalFrames;
        _viewStart = viewStart;
        _viewSpan = Math.Max(1d, viewSpan);
        if (totalFrames <= 0)
        {
            Minimum = 0;
            Maximum = 1;
            ViewportSize = 1;
            Value = 0;
            IsEnabled = false;
            return;
        }

        var span = Math.Max(1, viewSpan);
        var max = Math.Max(0, totalFrames - span);
        if (max < 1)
        {
            // Track は Maximum==0（つまみ＝全幅）だと Thumb を Hidden にする。
            max = 1;
            span = Math.Max(1, totalFrames);
        }

        IsEnabled = true;
        ViewportSize = span;
        Minimum = 0;
        Maximum = max;
        SmallChange = Math.Max(1, span * TimeScrollRange.ScrollStepFraction);
        LargeChange = Math.Max(1, span * 0.9);
        Value = Math.Clamp(viewStart, 0, Math.Max(0, totalFrames - viewSpan));
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsEnabled
            && _totalFrames > 0
            && TryGetThumbBounds(out var thumb))
        {
            var hit = TimeScrollRange.HitTest(
                e.GetPosition(this).X,
                thumb.X,
                thumb.Width,
                DesignMetrics.WaveformScrollHandleWidth);
            if (hit is not (TimeScrollHit.Left or TimeScrollHit.Right))
            {
                base.OnPreviewMouseLeftButtonDown(e);
                return;
            }

            _edgeDrag = hit == TimeScrollHit.Left ? EdgeDrag.Left : EdgeDrag.Right;
            _dragOriginX = e.GetPosition(this).X;
            _dragOriginStart = _viewStart;
            _dragOriginSpan = _viewSpan;
            SetBarCursor(Cursors.SizeWE);
            CaptureMouse();
            e.Handled = true;
            return;
        }

        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_edgeDrag == EdgeDrag.None)
        {
            UpdateEdgeCursor(e.GetPosition(this).X);
        }

        base.OnPreviewMouseMove(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_edgeDrag == EdgeDrag.None)
        {
            base.OnMouseMove(e);
            return;
        }

        ApplyEdgeDrag(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_edgeDrag != EdgeDrag.None)
        {
            EndEdgeDrag();
            e.Handled = true;
            return;
        }

        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        EndEdgeDrag();
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_edgeDrag == EdgeDrag.None)
        {
            SetBarCursor(Cursors.Arrow);
        }

        base.OnMouseLeave(e);
    }

    private void ApplyEdgeDrag(double mouseX)
    {
        if (!TryGetTrackMetrics(out var trackLeft, out var trackWidth))
        {
            return;
        }

        var delta = TimeScrollRange.FrameAt(mouseX - trackLeft, trackWidth, _totalFrames)
            - TimeScrollRange.FrameAt(_dragOriginX - trackLeft, trackWidth, _totalFrames);
        var next = TimeScrollRange.ResizeByDelta(
            _edgeDrag == EdgeDrag.Left,
            _dragOriginStart,
            _dragOriginSpan,
            _totalFrames,
            delta);
        if (Math.Abs(next.ViewStart - _viewStart) < 0.0001
            && Math.Abs(next.ViewSpan - _viewSpan) < 0.0001)
        {
            return;
        }

        _viewStart = next.ViewStart;
        _viewSpan = next.ViewSpan;
        Sync(_viewStart, _viewSpan, _totalFrames);
        RangeChanged?.Invoke(this, next);
    }

    private void EndEdgeDrag()
    {
        if (_edgeDrag == EdgeDrag.None)
        {
            return;
        }

        _edgeDrag = EdgeDrag.None;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        UpdateEdgeCursor(Mouse.GetPosition(this).X);
    }

    private void UpdateEdgeCursor(double x)
    {
        if (!IsEnabled || _totalFrames <= 0 || !TryGetThumbBounds(out var thumb))
        {
            SetBarCursor(Cursors.Arrow);
            return;
        }

        var hit = TimeScrollRange.HitTest(
            x,
            thumb.X,
            thumb.Width,
            DesignMetrics.WaveformScrollHandleWidth);
        SetBarCursor(hit is TimeScrollHit.Left or TimeScrollHit.Right
            ? Cursors.SizeWE
            : Cursors.Arrow);
    }

    private void SetBarCursor(Cursor cursor)
    {
        Cursor = cursor;
        if (Track?.Thumb is { } thumb)
        {
            thumb.Cursor = cursor;
        }
    }

    private bool TryGetTrackMetrics(out double trackLeft, out double trackWidth)
    {
        ApplyTemplate();
        var track = Track;
        if (track is not null && track.ActualWidth > 0)
        {
            trackLeft = track.TransformToAncestor(this).Transform(new Point(0, 0)).X;
            trackWidth = track.ActualWidth;
            return true;
        }

        trackLeft = 0;
        trackWidth = ActualWidth;
        return trackWidth > 0;
    }

    private bool TryGetThumbBounds(out Rect bounds)
    {
        bounds = default;
        ApplyTemplate();
        var thumb = Track?.Thumb;
        if (thumb is null || thumb.ActualWidth <= 0 || thumb.ActualHeight <= 0)
        {
            return false;
        }

        var origin = thumb.TransformToAncestor(this).Transform(new Point(0, 0));
        bounds = new Rect(origin, thumb.RenderSize);
        return true;
    }
}
