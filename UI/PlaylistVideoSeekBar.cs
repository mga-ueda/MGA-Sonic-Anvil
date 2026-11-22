using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 動画本再生で A がアナライザを隠しているとき、画面下に出すシークバー。T は無関係。
/// 波形は描かず、再生位置とドラッグ／クリックのシークだけを受け持つ。
/// 影はバーと同じ要素の子なので、Opacity のフェードで同時に消える。
/// </summary>
internal sealed class PlaylistVideoSeekBar : Grid
{
    private readonly Border _shadow;
    private readonly TrackMark _track = new();

    private double _ratio;
    private bool _dragging;

    public PlaylistVideoSeekBar()
    {
        Focusable = false;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Background = Brushes.Transparent;
        Cursor = Cursors.Hand;
        _shadow = new Border
        {
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Center,
            Height = DesignMetrics.PlaylistVideoSimpleSeekTrackHeight,
            Opacity = 0.55,
            Effect = new BlurEffect
            {
                Radius = 12,
                KernelType = KernelType.Gaussian,
                RenderingBias = RenderingBias.Quality,
            },
        };
        Children.Add(_shadow);
        Children.Add(_track);
        RefreshAppearance();
    }

    /// <summary>配色変更時。トラック／影をテーマから取り直す。</summary>
    public void RefreshAppearance()
    {
        _shadow.Background = WpfControlHelpers.FrozenBrush(Theme.Get("UiDropShadowBrush"));
        _track.InvalidateVisual();
    }

    /// <summary>再生位置（0〜1）。ドラッグ中はポインタ側を優先する。</summary>
    public double Ratio
    {
        get => _ratio;
        set
        {
            var next = double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Clamp(value, 0, 1);
            if (Math.Abs(_ratio - next) < 0.0000001)
            {
                return;
            }

            _ratio = next;
            _track.Ratio = next;
        }
    }

    public bool IsDragging => _dragging;

    public event EventHandler<double>? RatioSeeked;

    /// <summary>ドラッグを離した。表示維持の判定をやり直す。</summary>
    public event EventHandler? InteractionEnded;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragging = true;
        CaptureMouse();
        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        SeekAt(e.GetPosition(this).X);
        EndDrag();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        EndDrag();
        base.OnLostMouseCapture(e);
    }

    private void EndDrag()
    {
        var wasDragging = _dragging;
        _dragging = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        if (wasDragging)
        {
            InteractionEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SeekAt(double x)
    {
        var width = ActualWidth;
        var ratio = width <= 1 ? 0 : Math.Clamp(x / width, 0, 1);
        Ratio = ratio;
        RatioSeeked?.Invoke(this, ratio);
    }

    private sealed class TrackMark : FrameworkElement
    {
        private double _ratio;

        public TrackMark()
        {
            IsHitTestVisible = false;
            SnapsToDevicePixels = true;
        }

        public double Ratio
        {
            get => _ratio;
            set
            {
                _ratio = value;
                InvalidateVisual();
            }
        }

        protected override void OnRender(DrawingContext dc)
        {
            var width = ActualWidth;
            var height = ActualHeight;
            if (width < 1 || height < 1)
            {
                return;
            }

            var track = WpfControlHelpers.FrozenBrush(Theme.Get("PlayerVideoSeekTrackBrush"));
            var playedBrush = WpfControlHelpers.FrozenBrush(Theme.Get("PlayerVideoSeekPlayedBrush"));
            var trackHeight = Math.Min(DesignMetrics.PlaylistVideoSimpleSeekTrackHeight, height);
            var y = (height - trackHeight) * 0.5;
            var radius = trackHeight * 0.5;
            dc.DrawRoundedRectangle(track, null, new Rect(0, y, width, trackHeight), radius, radius);
            var played = width * _ratio;
            if (played > 0.5)
            {
                dc.DrawRoundedRectangle(
                    playedBrush,
                    null,
                    new Rect(0, y, Math.Min(width, played), trackHeight),
                    radius,
                    radius);
            }
        }
    }
}
