using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>
/// E|S 継ぎ目の拡大表示。左＝終点手前、右＝始点直後。
/// 継ぎ目線は中央固定。左右は独立パン。横ゼロライン付き。
/// </summary>
internal sealed class LoopSpliceView : FrameworkElement
{
    private AudioDocument? _document;
    private CrossfadeEditState? _state;
    private double _timeZoom = DefaultTimeZoom;
    private double _ampZoom = 1;
    /// <summary>左（エンド側）。正＝より手前（早い位置）を見る。</summary>
    private long _panLeftFrames;
    /// <summary>右（スタート側）。正＝より後ろ（遅い位置）を見る。</summary>
    private long _panRightFrames;
    private bool _dragging;
    private bool _dragLeftSide;
    private DragKind _dragKind;
    private Point _dragStartPoint;
    private long _dragStartPan;
    private long _dragStartLoopFrame;
    private double _dragStartAmpZoom = 1;
    private const int BaseHalfFrames = 256;
    /// <summary>片側の最小表示サンプル数。1 まで落とさないと拡大が途中で止まる。</summary>
    private const int MinVisibleHalfFrames = 1;
    private const double DefaultTimeZoom = 0.25;
    private const double TimeZoomMin = 0.125;
    /// <summary>片側 1 サンプルまで拡大（BaseHalfFrames / MinVisibleHalfFrames）。</summary>
    private const double TimeZoomMax = BaseHalfFrames / (double)MinVisibleHalfFrames;
    private const double DragLockThresholdPx = 4;
    /// <summary>このピクセル上下で TimeZoomStep 1 段分の振幅ズーム。</summary>
    private const double AmpDragPixelsPerStep = 48;

    private enum DragKind
    {
        LoopEnds,
        /// <summary>Shift 押下直後。動きの主軸でパンか振幅かを決める。</summary>
        ShiftPending,
        Pan,
        Amp,
    }

    public LoopSpliceView()
    {
        Focusable = true;
        PreviewMouseWheel += OnPreviewMouseWheel;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += (_, _) => EndDrag();
    }

    /// <summary>継ぎ目波形ドラッグで S/E が動いたとき（パン時は呼ばない）。</summary>
    public event Action? LoopEndsChanged;

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            InvalidateVisual();
        }
    }

    public CrossfadeEditState? State
    {
        get => _state;
        set
        {
            _state = value;
            InvalidateVisual();
        }
    }

    public double TimeZoom
    {
        get => _timeZoom;
        set => SetTimeZoom(value);
    }

    public double AmpZoom
    {
        get => _ampZoom;
        set => SetAmpZoom(value);
    }

    public void ZoomTimeIn() => SetTimeZoom(_timeZoom * WaveformView.TimeZoomStep);

    public void ZoomTimeOut() => SetTimeZoom(_timeZoom / WaveformView.TimeZoomStep);

    public void ZoomTimeToMax()
    {
        var next = _timeZoom + 1e-9 < WaveformView.TimeZoomStepMax
            ? WaveformView.TimeZoomStepMax
            : TimeZoomMax;
        SetTimeZoom(next);
    }

    public void ResetTimeZoom()
    {
        _panLeftFrames = 0;
        _panRightFrames = 0;
        SetTimeZoom(DefaultTimeZoom);
    }

    public void ZoomAmpIn() => SetAmpZoom(_ampZoom * WaveformView.TimeZoomStep);

    public void ZoomAmpOut() => SetAmpZoom(_ampZoom / WaveformView.TimeZoomStep);

    public void ZoomAmpToMax() => SetAmpZoom(WaveformView.AmpZoomMax);

    public void ResetAmpZoom() => SetAmpZoom(1);

    public void WheelTimeZoom(int delta)
    {
        var factor = delta > 0 ? WaveformView.WheelTimeStep : 1d / WaveformView.WheelTimeStep;
        SetTimeZoom(_timeZoom * factor);
    }

    public void WheelAmpZoom(int delta) =>
        SetAmpZoom(delta > 0 ? _ampZoom * WaveformView.TimeZoomStep : _ampZoom / WaveformView.TimeZoomStep);

    public bool TryHandleZoomKey(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Up && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ZoomAmpToMax();
            return true;
        }

        if (key == Key.Down && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ResetAmpZoom();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Control)
        {
            ZoomTimeToMax();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Control)
        {
            ResetTimeZoom();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Shift)
        {
            ZoomAmpIn();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Shift)
        {
            ZoomAmpOut();
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.None)
        {
            ZoomTimeIn();
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.None)
        {
            ZoomTimeOut();
            return true;
        }

        return false;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        // エディタ同様: Ctrl+ホイール＝振幅。あわせて Shift+ホイールも振幅（縦ドラッグと揃える）。
        if (modifiers is ModifierKeys.Control or ModifierKeys.Shift)
        {
            WheelAmpZoom(e.Delta);
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None)
        {
            WheelTimeZoom(e.Delta);
            e.Handled = true;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
        if (_state is null || _document is null)
        {
            return;
        }

        _dragging = true;
        _dragStartPoint = e.GetPosition(this);
        _dragLeftSide = _dragStartPoint.X < ActualWidth * 0.5;
        _dragStartAmpZoom = _ampZoom;
        _dragStartPan = _dragLeftSide ? _panLeftFrames : _panRightFrames;
        _dragStartLoopFrame = _dragLeftSide ? _state.EndFrame : _state.StartFrame;
        // Shift＝縦で振幅／横でパン。通常＝左で終点／右で始点。
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            _dragKind = DragKind.ShiftPending;
            Cursor = Cursors.SizeAll;
        }
        else
        {
            _dragKind = DragKind.LoopEnds;
            Cursor = Cursors.SizeWE;
        }

        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed || _state is null || _document is null)
        {
            return;
        }

        var pos = e.GetPosition(this);
        var dx = pos.X - _dragStartPoint.X;
        var dy = pos.Y - _dragStartPoint.Y;
        if (_dragKind == DragKind.ShiftPending)
        {
            if (Math.Abs(dx) < DragLockThresholdPx && Math.Abs(dy) < DragLockThresholdPx)
            {
                e.Handled = true;
                return;
            }

            if (Math.Abs(dy) > Math.Abs(dx))
            {
                _dragKind = DragKind.Amp;
                Cursor = Cursors.SizeNS;
            }
            else
            {
                _dragKind = DragKind.Pan;
                Cursor = Cursors.SizeWE;
            }
        }

        if (_dragKind == DragKind.Amp)
        {
            // 上へ＝振幅拡大、下へ＝縮小（エディタの Shift+↑／↓ と同方向）。
            var steps = -dy / AmpDragPixelsPerStep;
            SetAmpZoom(_dragStartAmpZoom * Math.Pow(WaveformView.TimeZoomStep, steps));
            e.Handled = true;
            return;
        }

        var halfWidth = Math.Max(1d, ActualWidth * 0.5);
        var half = VisibleHalfFrames();
        if (_dragKind == DragKind.Pan)
        {
            // パンは内容が指に追従する向き
            var panDelta = (long)Math.Round(-dx / halfWidth * half);
            var next = _dragStartPan + panDelta;
            if (_dragLeftSide)
            {
                _panLeftFrames = next;
                ClampPan(ref _panLeftFrames, half);
            }
            else
            {
                _panRightFrames = next;
                ClampPan(ref _panRightFrames, half);
            }

            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // 右へドラッグ＝時間を後ろへ（エンド／スタートが増える）
        var deltaFrames = (long)Math.Round(dx / halfWidth * half);
        var frames = _document.FrameCount;
        var target = _dragStartLoopFrame + deltaFrames;
        if (_dragLeftSide)
        {
            _state.SetEnd(target, frames);
        }
        else
        {
            _state.SetStart(target, frames);
        }

        InvalidateVisual();
        LoopEndsChanged?.Invoke();
        e.Handled = true;
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        EndDrag();
        e.Handled = true;
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        Cursor = Cursors.Arrow;
    }

    private int VisibleHalfFrames() =>
        Math.Max(MinVisibleHalfFrames, (int)(BaseHalfFrames / Math.Max(_timeZoom, 1e-9)));

    private static void ClampPan(ref long pan, int half)
    {
        var limit = Math.Max(half * 4L, 64);
        pan = Math.Clamp(pan, -limit, limit);
    }

    private void SetTimeZoom(double zoom)
    {
        var next = Math.Clamp(zoom, TimeZoomMin, TimeZoomMax);
        if (Math.Abs(next - _timeZoom) < 1e-12)
        {
            return;
        }

        _timeZoom = next;
        var half = VisibleHalfFrames();
        ClampPan(ref _panLeftFrames, half);
        ClampPan(ref _panRightFrames, half);
        InvalidateVisual();
    }

    private void SetAmpZoom(double zoom)
    {
        var next = Math.Clamp(zoom, 1d, WaveformView.AmpZoomMax);
        if (Math.Abs(next - _ampZoom) < 1e-12)
        {
            return;
        }

        _ampZoom = next;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush")),
            null,
            bounds);

        var midX = bounds.Left + bounds.Width * 0.5;

        if (_document is null
            || _state is null
            || !_state.IsActive
            || _document.FrameCount <= 0
            || bounds.Width < 4
            || bounds.Height < 4)
        {
            var midY = bounds.Top + bounds.Height * 0.5;
            DrawZeroLine(dc, bounds.Left, bounds.Right, midY);
            DrawSeamLine(dc, bounds, midX);
            return;
        }

        var half = VisibleHalfFrames();
        var channels = Math.Max(1, _document.Channels);
        LoopCrossfade.BuildSplicePreviewChannels(
            _document.Interleaved,
            channels,
            _document.FrameCount,
            _state,
            half,
            _panLeftFrames,
            _panRightFrames,
            out var leftByCh,
            out var rightByCh);

        // 3ch 以上だけチャンネル色。モノ／ステレオは WaveFill 一色（本体波形と同じ）。
        // レーン間ギャップなし・各レーン高さフルで振幅を描く（上下余白なし）。
        var tintLanes = ChannelColors.UsesLaneTint(channels);
        var monoBrush = WpfControlHelpers.FrozenBrush(Theme.Get("WaveFillBrush"));
        var laneHeight = bounds.Height / channels;
        for (var ch = 0; ch < channels; ch++)
        {
            var laneTop = bounds.Top + ch * laneHeight;
            var laneMidY = laneTop + laneHeight * 0.5;
            DrawZeroLine(dc, bounds.Left, bounds.Right, laneMidY);
            var brush = tintLanes ? ChannelSwatch.Brush(ch) : monoBrush;
            var pen = new Pen(brush, 1.2);
            pen.Freeze();
            DrawHalf(dc, leftByCh[ch], bounds.Left, midX, laneMidY, laneHeight, pen);
            DrawHalf(dc, rightByCh[ch], midX, bounds.Right, laneMidY, laneHeight, pen);
        }

        DrawSeamLine(dc, bounds, midX);

        var label = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var typeface = new Typeface("Segoe UI");
        var dip = UiDpi.Get(this).PixelsPerDip;
        // 中心線から左右とも同じ余白（6px）。End は右端基準、Start は左端基準。
        const double seamLabelGap = 6;
        DrawLabel(
            dc,
            UiStrings.CrossfadeMarkerEnd,
            midX - seamLabelGap,
            bounds.Top + 2,
            typeface,
            dip,
            label,
            alignRight: true);
        DrawLabel(
            dc,
            UiStrings.CrossfadeMarkerStart,
            midX + seamLabelGap,
            bounds.Top + 2,
            typeface,
            dip,
            label,
            alignRight: false);
    }

    private void DrawHalf(
        DrawingContext dc,
        float[] samples,
        double x0,
        double x1,
        double midY,
        double height,
        Pen pen)
    {
        if (samples.Length < 1 || x1 <= x0)
        {
            return;
        }

        var halfH = height * 0.5;
        double SampleY(float sample)
        {
            var y = midY - sample * halfH * _ampZoom;
            return Math.Clamp(y, midY - halfH, midY + halfH);
        }

        // 1 サンプル時は中央に縦線（点だけだと見えない）。
        if (samples.Length == 1)
        {
            var x = (x0 + x1) * 0.5;
            var y = SampleY(samples[0]);
            dc.DrawLine(pen, new Point(x, midY), new Point(x, y));
            return;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var first = true;
            for (var i = 0; i < samples.Length; i++)
            {
                var t = i / (double)(samples.Length - 1);
                var x = x0 + (x1 - x0) * t;
                // ±1.0 でレーン上下端まで使い切る（余白係数なし）。
                var y = SampleY(samples[i]);
                if (first)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    first = false;
                }
                else
                {
                    ctx.LineTo(new Point(x, y), true, false);
                }
            }
        }

        geo.Freeze();
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawZeroLine(DrawingContext dc, double x0, double x1, double midY)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("WaveZeroLineBrush")), 1);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x0, midY), new Point(x1, midY));
    }

    private static void DrawSeamLine(DrawingContext dc, Rect bounds, double midX)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")), 1);
        pen.Freeze();
        dc.DrawLine(pen, new Point(midX, bounds.Top), new Point(midX, bounds.Bottom));
    }

    private static void DrawLabel(
        DrawingContext dc,
        string text,
        double x,
        double y,
        Typeface typeface,
        double dip,
        Brush brush,
        bool alignRight = false)
    {
        var ft = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            10,
            brush,
            dip);
        var drawX = alignRight ? x - ft.WidthIncludingTrailingWhitespace : x;
        dc.DrawText(ft, new Point(drawX, y));
    }
}
