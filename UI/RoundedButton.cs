using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

internal sealed class RoundedButton : Button
{
    private bool _hover;
    private bool _pressed;

    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(double), typeof(RoundedButton),
            new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverBackColorProperty =
        DependencyProperty.Register(nameof(HoverBackColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PressedBackColorProperty =
        DependencyProperty.Register(nameof(PressedBackColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisabledBackColorProperty =
        DependencyProperty.Register(nameof(DisabledBackColor), typeof(Color), typeof(RoundedButton),
            new FrameworkPropertyMetadata(Color.FromRgb(0x1A, 0x1B, 0x26), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisabledForeColorProperty =
        DependencyProperty.Register(nameof(DisabledForeColor), typeof(Color), typeof(RoundedButton),
            new FrameworkPropertyMetadata(Color.FromRgb(0x96, 0x96, 0x96), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderColorProperty =
        DependencyProperty.Register(nameof(BorderColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverBorderColorProperty =
        DependencyProperty.Register(nameof(HoverBorderColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PressedBorderColorProperty =
        DependencyProperty.Register(nameof(PressedBorderColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DisabledBorderColorProperty =
        DependencyProperty.Register(nameof(DisabledBorderColor), typeof(Color?), typeof(RoundedButton),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BorderSizeProperty =
        DependencyProperty.Register(nameof(BorderSize), typeof(double), typeof(RoundedButton),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    static RoundedButton()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RoundedButton),
            new FrameworkPropertyMetadata(typeof(RoundedButton)));
        FocusableProperty.OverrideMetadata(typeof(RoundedButton), new FrameworkPropertyMetadata(false));
        FocusVisualStyleProperty.OverrideMetadata(typeof(RoundedButton), new FrameworkPropertyMetadata(null));
    }

    public RoundedButton()
    {
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(8, 2, 8, 2);
        Cursor = Cursors.Hand;
        FontWeight = FontWeights.Bold;
        FontSize = 12;
        OverridesDefaultStyle = true;
        FocusVisualStyle = null;
        Effect = null;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Template = new ControlTemplate(typeof(Button));
        IsEnabledChanged += (_, _) => InvalidateVisual();
        Loaded += (_, _) => InvalidateVisual();
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Color? HoverBackColor
    {
        get => (Color?)GetValue(HoverBackColorProperty);
        set => SetValue(HoverBackColorProperty, value);
    }

    public Color? PressedBackColor
    {
        get => (Color?)GetValue(PressedBackColorProperty);
        set => SetValue(PressedBackColorProperty, value);
    }

    public Color DisabledBackColor
    {
        get => (Color)GetValue(DisabledBackColorProperty);
        set => SetValue(DisabledBackColorProperty, value);
    }

    public Color DisabledForeColor
    {
        get => (Color)GetValue(DisabledForeColorProperty);
        set => SetValue(DisabledForeColorProperty, value);
    }

    public Color? BorderColor
    {
        get => (Color?)GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public Color? HoverBorderColor
    {
        get => (Color?)GetValue(HoverBorderColorProperty);
        set => SetValue(HoverBorderColorProperty, value);
    }

    public Color? PressedBorderColor
    {
        get => (Color?)GetValue(PressedBorderColorProperty);
        set => SetValue(PressedBorderColorProperty, value);
    }

    public Color? DisabledBorderColor
    {
        get => (Color?)GetValue(DisabledBorderColorProperty);
        set => SetValue(DisabledBorderColorProperty, value);
    }

    public double BorderSize
    {
        get => (double)GetValue(BorderSizeProperty);
        set => SetValue(BorderSizeProperty, value);
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        _hover = true;
        InvalidateVisual();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _hover = false;
        _pressed = false;
        InvalidateVisual();
        base.OnMouseLeave(e);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (new Rect(RenderSize).Contains(hitTestParameters.HitPoint))
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }

        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _pressed = true;
        InvalidateVisual();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        _pressed = false;
        InvalidateVisual();
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0d || height <= 0d)
        {
            return;
        }

        var stroke = Math.Max(0d, BorderSize);
        var inset = stroke / 2d;
        var bounds = new Rect(inset, inset, Math.Max(0d, width - stroke), Math.Max(0d, height - stroke));
        var radius = Math.Max(0d, CornerRadius - inset);
        var geometry = WpfControlHelpers.RoundedRectGeometry(bounds, radius);

        Pen? pen = null;
        if (stroke > 0d && ResolveBorderColor() is Color border)
        {
            pen = new Pen(WpfControlHelpers.FrozenBrush(border), stroke)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            if (pen.CanFreeze)
            {
                pen.Freeze();
            }
        }

        dc.DrawGeometry(WpfControlHelpers.FrozenBrush(ResolveFillColor()), pen, geometry);

        var text = Content as string ?? Content?.ToString() ?? string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var textColor = IsEnabled
            ? Foreground is SolidColorBrush solid ? solid.Color : Theme.Get("PrimaryForeBrush")
            : DisabledForeColor;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection,
            new Typeface(FontFamily, FontStyle, FontWeight, FontStretch),
            FontSize,
            WpfControlHelpers.FrozenBrush(textColor),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1d, ActualWidth - Padding.Left - Padding.Right),
            Trimming = TextTrimming.CharacterEllipsis,
        };
        var textX = Padding.Left + Math.Max(0d, (ActualWidth - Padding.Left - Padding.Right - formatted.Width) / 2d);
        var textY = (ActualHeight - formatted.Height) / 2d;
        dc.DrawText(formatted, new Point(textX, textY));
    }

    private Color ResolveFillColor()
    {
        if (!IsEnabled)
        {
            return DisabledBackColor;
        }

        if (_pressed && PressedBackColor is Color pressed)
        {
            return pressed;
        }

        if (_hover && HoverBackColor is Color hover)
        {
            return hover;
        }

        if (Background is SolidColorBrush back)
        {
            return back.Color;
        }

        return Theme.Get("ChromeBackBrush");
    }

    private Color? ResolveBorderColor()
    {
        if (!IsEnabled)
        {
            return DisabledBorderColor ?? BorderColor;
        }

        if (_pressed)
        {
            return PressedBorderColor ?? BorderColor;
        }

        if (_hover)
        {
            return HoverBorderColor ?? BorderColor;
        }

        return BorderColor;
    }
}

internal static class ActionButtonLooks
{
    public static void ApplyClear(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("ClearButtonFillBrush"),
            Theme.Get("ClearButtonHoverFillBrush"),
            Theme.Get("ClearButtonBackBrush"),
            Theme.Get("ClearButtonHoverBackBrush"),
            Theme.Get("ClearButtonPressedBackBrush"),
            Theme.Get("ClearButtonForeBrush"));

    public static void ApplyAccent(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("ExportButtonFillBrush"),
            Theme.Get("ExportButtonHoverFillBrush"),
            Theme.Get("ExportButtonBackBrush"),
            Theme.Get("ExportButtonHoverBackBrush"),
            Theme.Get("ExportButtonPressedBackBrush"),
            Theme.Get("ExportButtonForeBrush"));

    public static void ApplyReload(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("ReloadButtonFillBrush"),
            Theme.Get("ReloadButtonHoverFillBrush"),
            Theme.Get("ReloadButtonBackBrush"),
            Theme.Get("ReloadButtonHoverBackBrush"),
            Theme.Get("ReloadButtonPressedBackBrush"),
            Theme.Get("ReloadButtonForeBrush"));

    public static void ApplyRender(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("RenderButtonFillBrush"),
            Theme.Get("RenderButtonHoverFillBrush"),
            Theme.Get("RenderButtonBackBrush"),
            Theme.Get("RenderButtonHoverBackBrush"),
            Theme.Get("RenderButtonPressedBackBrush"),
            Theme.Get("RenderButtonForeBrush"));

    public static void ApplyProxy(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("ProxyButtonFillBrush"),
            Theme.Get("ProxyButtonHoverFillBrush"),
            Theme.Get("ProxyButtonBackBrush"),
            Theme.Get("ProxyButtonHoverBackBrush"),
            Theme.Get("ProxyButtonPressedBackBrush"),
            Theme.Get("ProxyButtonForeBrush"));

    public static void ApplyChrome(RoundedButton button) =>
        Apply(
            button,
            Theme.Get("ChromeBackBrush"),
            Theme.Get("TransportHoverBackBrush"),
            Theme.Get("ChromeBorderBrush"),
            Theme.Get("ChromeMidBrush"),
            Theme.Get("TransportPressedBackBrush"),
            Theme.Get("PrimaryForeBrush"),
            borderSize: 1);

    private static void Apply(
        RoundedButton button,
        Color fill,
        Color hoverFill,
        Color border,
        Color hoverBorder,
        Color pressedBorder,
        Color fore,
        double borderSize = 2)
    {
        button.Background = WpfControlHelpers.FrozenBrush(fill);
        button.HoverBackColor = hoverFill;
        button.PressedBackColor = fill;
        button.BorderColor = border;
        button.HoverBorderColor = hoverBorder;
        button.PressedBorderColor = pressedBorder;
        button.BorderSize = borderSize;
        button.Foreground = WpfControlHelpers.FrozenBrush(fore);
        button.InvalidateVisual();
    }
}
