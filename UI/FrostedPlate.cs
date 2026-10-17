using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 背後の見た目をぼかして載せるすりガラス。ライブブラシのソースに自分を含めないこと（取り込みが循環する）。
/// 合成取り込みは祖先を撮るので、その間だけ自分を隠す。
/// </summary>
internal sealed class FrostedPlate : System.Windows.Controls.Grid
{
    /// <summary>明るい背面でも文字が残るよう、BusyGlass より濃いテーマ色のヴェール。</summary>
    internal const byte TintAlpha = 0xB0;

    /// <summary>合成ぼかしの上に載せる薄いヴェール。背面の色が残る濃さ。</summary>
    internal const byte GlassScrimAlpha = 0x48;

    internal const double BlurRadiusDip = 18;

    /// <summary>合成ぼかし。縮小して引き伸ばす。BusyGlass と同じ考え方。</summary>
    private const int CompositedDivisor = 6;

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source),
        typeof(FrameworkElement),
        typeof(FrostedPlate),
        new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius),
        typeof(CornerRadius),
        typeof(FrostedPlate),
        new PropertyMetadata(default(CornerRadius), OnChromeChanged));

    public static readonly DependencyProperty SampleCompositedProperty = DependencyProperty.Register(
        nameof(SampleComposited),
        typeof(bool),
        typeof(FrostedPlate),
        new PropertyMetadata(false, OnSampleChanged));

    public static readonly DependencyProperty ScrimAlphaProperty = DependencyProperty.Register(
        nameof(ScrimAlpha),
        typeof(byte),
        typeof(FrostedPlate),
        new PropertyMetadata(TintAlpha, OnScrimChanged));

    private readonly Backdrop _backdrop;
    private readonly System.Windows.Controls.Border _tint;
    private FrameworkElement? _hookedSource;
    private Rect _mapped = Rect.Empty;
    private bool _rendering;
    private bool _capturing;
    private int _captureTicket;
    private ImageBrush? _snapshot;

    public FrostedPlate()
    {
        IsHitTestVisible = false;
        _backdrop = new Backdrop(this)
        {
            IsHitTestVisible = false,
            Margin = new Thickness(-BlurRadiusDip),
            Visibility = Visibility.Collapsed,
            Effect = CreateBlur(),
        };
        RenderOptions.SetBitmapScalingMode(_backdrop, BitmapScalingMode.HighQuality);
        _tint = new System.Windows.Controls.Border
        {
            IsHitTestVisible = false,
            BorderThickness = new Thickness(1),
        };
        Children.Add(_backdrop);
        Children.Add(_tint);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, _) =>
        {
            UpdateClip();
            if (SampleComposited && _backdrop.Visibility == Visibility.Visible)
            {
                RequestCapture();
            }
        };
        ApplyTint();
    }

    public FrameworkElement? Source
    {
        get => (FrameworkElement?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// 祖先ごと撮って縮小ぼかしする。透明なソースの向こうに塗られた色もガラスに入る。
    /// </summary>
    public bool SampleComposited
    {
        get => (bool)GetValue(SampleCompositedProperty);
        set => SetValue(SampleCompositedProperty, value);
    }

    public byte ScrimAlpha
    {
        get => (byte)GetValue(ScrimAlphaProperty);
        set => SetValue(ScrimAlphaProperty, value);
    }

    internal ImageBrush? Snapshot => _snapshot;

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrostedPlate plate)
        {
            plate.HookSource();
            plate._backdrop.InvalidateVisual();
        }
    }

    private static void OnChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrostedPlate plate)
        {
            plate.UpdateClip();
        }
    }

    private static void OnSampleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrostedPlate plate)
        {
            return;
        }

        // 縮小ぼかしの上に BlurEffect を足すと、描画ごと消えることがある。
        plate._backdrop.Effect = plate.SampleComposited ? null : CreateBlur();
        plate.ApplyTint();
        plate.RequestCapture();
    }

    private static BlurEffect CreateBlur() => new()
    {
        Radius = BlurRadiusDip,
        KernelType = KernelType.Gaussian,
        RenderingBias = RenderingBias.Quality,
    };

    private static void OnScrimChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrostedPlate plate)
        {
            plate.ApplyTint();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        HookSource();
        ApplyTint();
        if (_rendering)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        UiThemeService.Changed += OnThemeChanged;
        _rendering = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hookedSource is not null)
        {
            _hookedSource.LayoutUpdated -= OnSourceLayoutUpdated;
            _hookedSource = null;
        }

        if (!_rendering)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        UiThemeService.Changed -= OnThemeChanged;
        _rendering = false;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTint();

    private void HookSource()
    {
        var next = IsLoaded ? Source : null;
        if (ReferenceEquals(_hookedSource, next))
        {
            return;
        }

        if (_hookedSource is not null)
        {
            _hookedSource.LayoutUpdated -= OnSourceLayoutUpdated;
        }

        _hookedSource = next;
        if (_hookedSource is not null)
        {
            _hookedSource.LayoutUpdated += OnSourceLayoutUpdated;
        }
    }

    private void OnSourceLayoutUpdated(object? sender, EventArgs e)
    {
        if (_backdrop.Visibility != Visibility.Visible || !TryMapBackdrop(out var mapped) || !Moved(_mapped, mapped))
        {
            return;
        }

        _mapped = mapped;
        if (SampleComposited)
        {
            RequestCapture();
            return;
        }

        _backdrop.InvalidateVisual();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_capturing)
        {
            return;
        }
        var show = IsVisible && EffectiveOpacity(this) > 0.02 && ActualWidth > 1 && ActualHeight > 1 && Source is not null;
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_backdrop.Visibility == visibility)
        {
            return;
        }

        _backdrop.Visibility = visibility;
        _mapped = Rect.Empty;
        if (!show)
        {
            return;
        }

        if (SampleComposited)
        {
            RequestCapture();
            return;
        }

        _backdrop.InvalidateVisual();
    }

    private void ApplyTint()
    {
        Color surface;
        Color edge;
        try
        {
            surface = Theme.Get("SurfaceBackBrush");
            edge = Theme.Get("ChromeBorderBrush");
        }
        catch (InvalidOperationException)
        {
            surface = Color.FromRgb(0x1E, 0x1E, 0x1E);
            edge = Color.FromRgb(0x37, 0x37, 0x3A);
        }

        _tint.Background = WpfControlHelpers.FrozenBrush(Color.FromArgb(ScrimAlpha, surface.R, surface.G, surface.B));
        var border = SampleComposited
            ? Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0x88, edge.R, edge.G, edge.B);
        _tint.BorderBrush = WpfControlHelpers.FrozenBrush(border);
        _tint.CornerRadius = CornerRadius;
    }

    private void RequestCapture()
    {
        if (_capturing || !SampleComposited || !IsLoaded)
        {
            return;
        }

        var ticket = ++_captureTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ticket != _captureTicket || _capturing || !SampleComposited)
            {
                return;
            }

            CaptureComposited();
        });
    }

    /// <summary>
    /// 見えている祖先を、自分を消した状態で撮る。縮小がぼかしになる。
    /// </summary>
    private void CaptureComposited()
    {
        var root = FindCaptureRoot();
        if (root is null)
        {
            return;
        }

        _capturing = true;
        var opacity = Opacity;
        Opacity = 0;
        try
        {
            UpdateLayout();
            root.UpdateLayout();
            if (_backdrop.ActualWidth <= 1 || _backdrop.ActualHeight <= 1 || !TryMapTo(root, out var viewbox))
            {
                return;
            }

            viewbox.Intersect(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            if (viewbox.Width < 1 || viewbox.Height < 1 || viewbox.Width > 4000 || viewbox.Height > 4000)
            {
                return;
            }

            var suspended = SuspendBitmapCache(root);
            ImageSource? bitmap;
            try
            {
                bitmap = RenderFrost(root, viewbox, Underpaint());
            }
            finally
            {
                RestoreBitmapCache(suspended);
            }
            if (bitmap is null)
            {
                return;
            }

            var brush = new ImageBrush(bitmap)
            {
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            brush.Freeze();
            _snapshot = brush;
            _backdrop.InvalidateVisual();
        }
        finally
        {
            Opacity = opacity;
            _capturing = false;
        }
    }

    /// <summary>
    /// ウィンドウ内容の一番上。ライブラリの後ろに敷いたウォッシュも入る。
    /// </summary>
    private FrameworkElement? FindCaptureRoot()
    {
        FrameworkElement? top = Source;
        for (DependencyObject? current = Source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is Window)
            {
                break;
            }

            if (current is FrameworkElement element)
            {
                top = element;
            }
        }

        return top;
    }

    private Brush Underpaint()
    {
        Color surface;
        try
        {
            surface = Theme.Get("SurfaceBackBrush");
        }
        catch (InvalidOperationException)
        {
            surface = Color.FromRgb(0x1E, 0x1E, 0x1E);
        }

        return WpfControlHelpers.FrozenBrush(surface);
    }

    private static List<(UIElement Element, CacheMode Mode)> SuspendBitmapCache(DependencyObject root)
    {
        var suspended = new List<(UIElement, CacheMode)>();
        SuspendBitmapCache(root, suspended);
        return suspended;
    }

    private static void SuspendBitmapCache(DependencyObject node, List<(UIElement Element, CacheMode Mode)> suspended)
    {
        if (node is UIElement element && element.CacheMode is CacheMode mode)
        {
            suspended.Add((element, mode));
            element.CacheMode = null;
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            SuspendBitmapCache(VisualTreeHelper.GetChild(node, i), suspended);
        }
    }

    private static void RestoreBitmapCache(List<(UIElement Element, CacheMode Mode)> suspended)
    {
        foreach (var (element, mode) in suspended)
        {
            element.CacheMode = mode;
        }
    }

    private static ImageSource? RenderFrost(FrameworkElement root, Rect viewbox, Brush underpaint)
    {
        try
        {
            var width = Math.Max(1, (int)Math.Ceiling(viewbox.Width / CompositedDivisor));
            var height = Math.Max(1, (int)Math.Ceiling(viewbox.Height / CompositedDivisor));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bounds = new Rect(0, 0, width, height);
                dc.DrawRectangle(underpaint, null, bounds);
                var brush = new VisualBrush(root)
                {
                    Stretch = Stretch.Fill,
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Viewbox = viewbox,
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                };
                dc.DrawRectangle(brush, null, bounds);
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private void UpdateClip()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            Clip = null;
            return;
        }

        var radius = CornerRadius.TopLeft;
        Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), radius, radius);
        _tint.CornerRadius = CornerRadius;
    }

    private bool TryMapBackdrop(out Rect mapped) => TryMapTo(Source, out mapped);

    private bool TryMapTo(Visual? target, out Rect mapped)
    {
        mapped = Rect.Empty;
        if (target is null || _backdrop.ActualWidth <= 1 || _backdrop.ActualHeight <= 1)
        {
            return false;
        }

        try
        {
            var transform = _backdrop.TransformToVisual(target);
            var a = transform.Transform(new Point(0, 0));
            var b = transform.Transform(new Point(_backdrop.ActualWidth, _backdrop.ActualHeight));
            mapped = new Rect(a, b);
            return mapped.Width >= 1 && mapped.Height >= 1;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool Moved(Rect a, Rect b) =>
        Math.Abs(a.X - b.X) > 0.5
        || Math.Abs(a.Y - b.Y) > 0.5
        || Math.Abs(a.Width - b.Width) > 0.5
        || Math.Abs(a.Height - b.Height) > 0.5;

    private static double EffectiveOpacity(DependencyObject start)
    {
        var opacity = 1d;
        DependencyObject? current = start;
        while (current is UIElement element)
        {
            opacity *= element.Opacity;
            if (opacity <= 0.02)
            {
                return opacity;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return opacity;
    }

    private sealed class Backdrop : FrameworkElement
    {
        private readonly FrostedPlate _plate;

        public Backdrop(FrostedPlate plate) => _plate = plate;

        protected override void OnRender(DrawingContext dc)
        {
            var source = _plate.Source;
            if (source is null || ActualWidth <= 1 || ActualHeight <= 1)
            {
                return;
            }

            GeneralTransform transform;
            try
            {
                transform = TransformToVisual(source);
            }
            catch (InvalidOperationException)
            {
                return;
            }

            var a = transform.Transform(new Point(0, 0));
            var b = transform.Transform(new Point(ActualWidth, ActualHeight));
            var viewbox = new Rect(a, b);
            if (viewbox.Width < 1 || viewbox.Height < 1)
            {
                return;
            }

            var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
            if (_plate.SampleComposited)
            {
                if (_plate.Snapshot is ImageBrush snapshot)
                {
                    dc.DrawRectangle(snapshot, null, bounds);
                }

                return;
            }

            // 透明なソースをぼかすと、空の画素が黒に寄って半透明の黒板になる。
            // 見えている面色を先に敷いてから、ソースの中身を載せる。
            if (TryFindOpaqueFill(source) is Brush fill)
            {
                dc.DrawRectangle(fill, null, bounds);
            }

            var brush = new VisualBrush(source)
            {
                AutoLayoutContent = false,
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = viewbox,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = bounds,
                Stretch = Stretch.Fill,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            dc.DrawRectangle(brush, null, bounds);
        }

        /// <summary>
        /// ソース自身は透明でも、祖先が塗っている面色が実際の背面。ぼかしの下地にする。
        /// </summary>
        private static Brush? TryFindOpaqueFill(DependencyObject source)
        {
            for (DependencyObject? current = source; current is not null; current = VisualTreeHelper.GetParent(current))
            {
                var background = current switch
                {
                    System.Windows.Controls.Panel panel => panel.Background,
                    System.Windows.Controls.Control control => control.Background,
                    System.Windows.Controls.Border border => border.Background,
                    _ => null,
                };
                if (background is null || background.Opacity <= 0.02)
                {
                    continue;
                }

                if (background is SolidColorBrush solid && solid.Color.A < 250)
                {
                    continue;
                }

                return background.IsFrozen ? background : background.Clone();
            }

            return null;
        }
    }
}
