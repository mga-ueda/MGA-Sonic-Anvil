using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace MgaSonicAnvil.UI;

/// <summary>
/// モニター列／オプション用のラッチボタン。角丸 3、オン時は指定色。高さは低め。
/// オプション系（Silent Skip 等）と WAAPI チップは <see cref="UseLatchToggleTheme"/> で
/// <c>LatchToggle*</c> を共有。BPM／MONO／DIM／LC は <see cref="LatchColor"/> のアクセント色。
/// 半透明面は OnRender ではなく Border.Background で塗り、ジャケットウォッシュを確実に透かす。
/// </summary>
internal sealed class MonitorLatchButton : Button
{
    public static readonly DependencyProperty IsLatchedProperty = DependencyProperty.Register(
        nameof(IsLatched),
        typeof(bool),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(false, OnVisualStateChanged));

    public static readonly DependencyProperty LatchLabelProperty = DependencyProperty.Register(
        nameof(LatchLabel),
        typeof(string),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty LatchColorProperty = DependencyProperty.Register(
        nameof(LatchColor),
        typeof(Color),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(Colors.Gray, OnVisualStateChanged));

    private readonly Border _face = new()
    {
        Margin = new Thickness(2),
        SnapsToDevicePixels = true,
        IsHitTestVisible = false,
    };

    private readonly TextBlock _label = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Segoe UI Semibold"),
        FontWeight = FontWeights.Bold,
        IsHitTestVisible = false,
    };

    private readonly Rectangle _slot = new()
    {
        Fill = Brushes.Transparent,
        IsHitTestVisible = false,
    };

    private bool _washThrough;
    private bool _useLatchToggleTheme;

    public bool IsLatched
    {
        get => (bool)GetValue(IsLatchedProperty);
        set => SetValue(IsLatchedProperty, value);
    }

    public string LatchLabel
    {
        get => (string)GetValue(LatchLabelProperty);
        set => SetValue(LatchLabelProperty, value);
    }

    public Color LatchColor
    {
        get => (Color)GetValue(LatchColorProperty);
        set => SetValue(LatchColorProperty, value);
    }

    /// <summary>プレイヤー中はスロット塗りをせず、ジャケットウォッシュを透かす。</summary>
    public bool WashThrough
    {
        get => _washThrough;
        set
        {
            if (_washThrough == value)
            {
                return;
            }

            _washThrough = value;
            Paint();
        }
    }

    /// <summary>
    /// オン／オフとも <c>LatchToggle*</c>（WAAPI チップと共通）を使う。
    /// BPM 等のモニターボタンでは false のまま <see cref="LatchColor"/> を使う。
    /// </summary>
    public bool UseLatchToggleTheme
    {
        get => _useLatchToggleTheme;
        set
        {
            if (_useLatchToggleTheme == value)
            {
                return;
            }

            _useLatchToggleTheme = value;
            _label.FontSize = LabelFontSize;
            Paint();
        }
    }

    public MonitorLatchButton()
    {
        Height = DesignMetrics.MonitorButtonHeight;
        MinHeight = DesignMetrics.MonitorButtonHeight;
        MaxHeight = DesignMetrics.MonitorButtonHeight;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = null;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(0);
        OverridesDefaultStyle = true;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(_label, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(_label, TextRenderingMode.Grayscale);

        var root = new Grid();
        root.Children.Add(_slot);
        root.Children.Add(_face);
        root.Children.Add(_label);
        Content = root;
        Template = ContentOnlyTemplate();

        TransportHover.Attach(this);
        MouseEnter += (_, _) => Paint();
        MouseLeave += (_, _) => Paint();
        PreviewMouseLeftButtonDown += (_, _) => Paint();
        PreviewMouseLeftButtonUp += (_, _) => Paint();
        LostMouseCapture += (_, _) => Paint();
        IsEnabledChanged += (_, _) => Paint();
        Loaded += (_, _) => Paint();
        SizeChanged += (_, _) => SyncCornerRadius();

        _label.FontSize = LabelFontSize;
        SyncCornerRadius();
        Paint();
    }

    private double LabelFontSize =>
        UseLatchToggleTheme ? DesignMetrics.OptionLatchFontSize : DesignMetrics.MonitorButtonFontSize;

    /// <summary>ラベルがはみ出さない最小幅（DIP）。</summary>
    public double MeasureLabelWidth()
    {
        _label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var pad = UseLatchToggleTheme
            ? DesignMetrics.OptionLatchLabelWidthPad
            : 12d;
        return Math.Ceiling(_label.DesiredSize.Width + pad);
    }

    /// <summary>ラベル幅に合わせて Width を決める（列統一しない単体ボタン向け）。</summary>
    public void FitToLabel()
    {
        _label.Text = LatchLabel ?? string.Empty;
        _label.FontSize = LabelFontSize;
        if (UseLatchToggleTheme)
        {
            ApplyOptionSize();
            Width = Math.Max(DesignMetrics.OptionLatchMinWidth, MeasureLabelWidth());
            return;
        }

        Width = Math.Max(DesignMetrics.MonitorToggleWidth, MeasureLabelWidth());
    }

    /// <summary>オプションラッチ用の高さ・上限を適用する。</summary>
    public void ApplyOptionSize()
    {
        var h = DesignMetrics.OptionLatchButtonHeight;
        Height = h;
        MinHeight = h;
        MaxHeight = h;
    }

    /// <summary>オプション系（LatchToggle テーマ）のラッチボタンを作る。</summary>
    public static MonitorLatchButton CreateOption(string label, string? tooltip = null)
    {
        var button = new MonitorLatchButton
        {
            LatchLabel = label,
            UseLatchToggleTheme = true,
            WashThrough = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, DesignMetrics.TransportGroupGap, 0),
        };
        button.FitToLabel();
        if (!string.IsNullOrEmpty(tooltip))
        {
            TransportToolTip.Attach(button, tooltip);
        }

        return button;
    }

    /// <summary>色設定変更後など、面と文字を描き直す。</summary>
    public void RefreshAppearance() => Paint();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    private void SyncCornerRadius()
    {
        var radius = DesignMetrics.ControlCornerRadius;
        _face.CornerRadius = new CornerRadius(radius);
    }

    private void Paint()
    {
        SyncCornerRadius();
        _label.Text = LatchLabel ?? string.Empty;
        _label.FontSize = LabelFontSize;

        // オプション／ステータスの LatchToggle はスロット塗りなし。BPM 列だけ Transport 下地。
        if (!WashThrough && !UseLatchToggleTheme)
        {
            _slot.Fill = WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush"));
        }
        else
        {
            _slot.Fill = Brushes.Transparent;
        }

        var on = IsLatched;
        var hover = IsEnabled && IsMouseOver && !IsPressed;
        var pressed = IsEnabled && IsPressed;
        var fill = FaceFill(on, hover, pressed);
        // 無効時は面をさらに落とす（エディタ専用ラッチのプレイヤー表示など）。
        var faceOpacity = IsEnabled ? fill.A / 255d : (fill.A / 255d) * 0.45;
        _face.Background = FaceBrush(fill, faceOpacity);

        var fore = on
            ? Theme.Get("LatchToggleOnForeBrush")
            : Theme.Get("LatchToggleOffForeBrush");
        if (!IsEnabled)
        {
            fore = Theme.Get("LatchToggleOffForeBrush");
        }

        _label.Foreground = WpfControlHelpers.FrozenBrush(fore);
        _label.Opacity = IsEnabled ? 1 : 0.55;
    }

    /// <summary>
    /// Color.A ではなく Brush.Opacity で透かす。
    /// OnRender の FromArgb 塗りは親の不透明中間面に潰されることがある。
    /// </summary>
    private static Brush FaceBrush(Color fill, double opacity)
    {
        var brush = new SolidColorBrush(Color.FromRgb(fill.R, fill.G, fill.B))
        {
            Opacity = Math.Clamp(opacity, 0, 1),
        };
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    private Color FaceFill(bool on, bool hover, bool pressed)
    {
        if (on)
        {
            if (UseLatchToggleTheme)
            {
                var onKey = hover && !pressed
                    ? "LatchToggleOnHoverBackBrush"
                    : "LatchToggleOnBackBrush";
                var c = Theme.Get(onKey);
                if (pressed)
                {
                    c = Darken(c, 0.18);
                }

                return WashThrough ? PlayerChrome.WashLatchToggleFace(c) : c;
            }

            // BPM／MONO／DIM／LC のオンはアクセント色のまま（ウォッシュしない）。
            var accent = LatchColor;
            if (pressed)
            {
                return Darken(accent, 0.18);
            }

            if (hover)
            {
                return Lighten(accent, 0.12);
            }

            return accent;
        }

        var offKey = hover && !pressed
            ? "LatchToggleOffHoverBackBrush"
            : "LatchToggleOffBackBrush";
        var off = Theme.Get(offKey);
        return WashThrough ? PlayerChrome.WashLatchToggleFace(off) : off;
    }

    private static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            c.A,
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));
    }

    private static Color Darken(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(
            c.A,
            (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.B * (1 - amount), 0, 255));
    }

    private static void OnVisualStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonitorLatchButton button)
        {
            button.Paint();
        }
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MonitorLatchButton button)
        {
            button._label.Text = button.LatchLabel ?? string.Empty;
            button.Paint();
        }
    }

    private static ControlTemplate ContentOnlyTemplate()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        presenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Stretch);
        return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
    }
}
