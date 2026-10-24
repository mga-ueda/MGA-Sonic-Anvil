using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// モニター列用のラッチボタン。角丸 3、オン時は指定色。高さは低め・幅は列で統一。
/// </summary>
internal sealed class MonitorLatchButton : Button
{
    public static readonly DependencyProperty IsLatchedProperty = DependencyProperty.Register(
        nameof(IsLatched),
        typeof(bool),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LatchLabelProperty = DependencyProperty.Register(
        nameof(LatchLabel),
        typeof(string),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LatchColorProperty = DependencyProperty.Register(
        nameof(LatchColor),
        typeof(Color),
        typeof(MonitorLatchButton),
        new FrameworkPropertyMetadata(Colors.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

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
    public bool WashThrough { get; set; }

    public MonitorLatchButton()
    {
        Height = DesignMetrics.MonitorButtonHeight;
        MinHeight = DesignMetrics.MonitorButtonHeight;
        MaxHeight = DesignMetrics.MonitorButtonHeight;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = Cursors.Hand;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TransportHover.Attach(this);
    }

    /// <summary>ラベルがはみ出さない最小幅（DIP）。</summary>
    public double MeasureLabelWidth()
    {
        var ft = CreateLabelText(LatchLabel ?? string.Empty, Brushes.Black);
        // 面の左右パッド 2 + 文字余白 4*2
        return Math.Ceiling(ft.Width + 12);
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        if (!WashThrough)
        {
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        }

        const double pad = 2d;
        var face = new Rect(
            pad,
            pad,
            Math.Max(1, bounds.Width - pad * 2),
            Math.Max(1, bounds.Height - pad * 2));
        var radius = DesignMetrics.ControlCornerRadius;
        var on = IsLatched;
        var hover = IsEnabled && IsMouseOver && !IsPressed;
        var pressed = IsEnabled && IsPressed;
        var fill = FaceFill(on, hover, pressed);
        dc.DrawRoundedRectangle(WpfControlHelpers.FrozenBrush(fill), null, face, radius, radius);

        var fore = on
            ? Theme.Get("WaapiToggleOnForeBrush")
            : Theme.Get("WaapiToggleOffForeBrush");
        var formatted = CreateLabelText(LatchLabel ?? string.Empty, WpfControlHelpers.FrozenBrush(fore));
        dc.DrawText(
            formatted,
            new Point(
                (bounds.Width - formatted.Width) * 0.5,
                (bounds.Height - formatted.Height) * 0.5));
    }

    private FormattedText CreateLabelText(string label, Brush brush) =>
        new(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily("Segoe UI Semibold"),
                FontStyles.Normal,
                FontWeights.Bold,
                FontStretches.Normal),
            DesignMetrics.MonitorButtonFontSize,
            brush,
            UiDpi.Get(this).PixelsPerDip);

    private Color FaceFill(bool on, bool hover, bool pressed)
    {
        if (on)
        {
            var c = LatchColor;
            if (pressed)
            {
                return Darken(c, 0.18);
            }

            if (hover)
            {
                return Lighten(c, 0.12);
            }

            return c;
        }

        // オフ面は WAAPI トグル無効／オフと同じブラシ（ホバー含む）。
        if (WashThrough)
        {
            return PlayerChrome.Get(hover && !pressed
                ? "PlayerWaapiToggleOffHoverBackBrush"
                : "PlayerWaapiToggleOffBackBrush");
        }

        return Theme.Get(hover && !pressed
            ? "WaapiToggleOffHoverBackBrush"
            : "WaapiToggleOffBackBrush");
    }

    private static Color Lighten(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));
    }

    private static Color Darken(Color c, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.B * (1 - amount), 0, 255));
    }
}
