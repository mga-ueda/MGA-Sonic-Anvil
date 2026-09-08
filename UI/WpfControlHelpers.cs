using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class WpfControlHelpers
{
    public static Typeface UiBoldTypeface { get; } =
        new(new FontFamily("Yu Gothic UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static Typeface UiTypeface { get; } =
        new(new FontFamily("Yu Gothic UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static Typeface MonoTypeface { get; } =
        new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    public static double DeviceHairline(double pixelsPerDip) =>
        pixelsPerDip > 0 ? 1.0 / pixelsPerDip : 1.0;

    public static double SnapDeviceCenter(double x, double pixelsPerDip)
    {
        if (pixelsPerDip <= 0)
        {
            return x;
        }

        return (Math.Round(x * pixelsPerDip) + 0.5) / pixelsPerDip;
    }

    public static Pen FrozenHairline(Color color, double pixelsPerDip)
    {
        var pen = new Pen(FrozenBrush(color), DeviceHairline(pixelsPerDip));
        pen.Freeze();
        return pen;
    }

    public static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        if (brush.CanFreeze)
        {
            brush.Freeze();
        }

        return brush;
    }

    public static StreamGeometry RoundedRectGeometry(Rect bounds, double radius)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            AddRoundedRect(ctx, bounds, radius);
        }

        geometry.Freeze();
        return geometry;
    }

    public static void AddRoundedRect(StreamGeometryContext ctx, Rect bounds, double radius)
    {
        radius = Math.Max(0d, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2d));
        if (radius <= 0d)
        {
            ctx.BeginFigure(bounds.TopLeft, isFilled: true, isClosed: true);
            ctx.LineTo(bounds.TopRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(bounds.BottomRight, isStroked: true, isSmoothJoin: false);
            ctx.LineTo(bounds.BottomLeft, isStroked: true, isSmoothJoin: false);
            return;
        }

        var x = bounds.X;
        var y = bounds.Y;
        var w = bounds.Width;
        var h = bounds.Height;
        var r = radius;
        ctx.BeginFigure(new Point(x + r, y), isFilled: true, isClosed: true);
        ctx.LineTo(new Point(x + w - r, y), isStroked: true, isSmoothJoin: false);
        ctx.ArcTo(new Point(x + w, y + r), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        ctx.LineTo(new Point(x + w, y + h - r), isStroked: true, isSmoothJoin: false);
        ctx.ArcTo(new Point(x + w - r, y + h), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        ctx.LineTo(new Point(x + r, y + h), isStroked: true, isSmoothJoin: false);
        ctx.ArcTo(new Point(x, y + h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
        ctx.LineTo(new Point(x, y + r), isStroked: true, isSmoothJoin: false);
        ctx.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
    }

    public static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (byte)Math.Round(from.A + (to.A - from.A) * amount),
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    public static FormattedText UiBoldText(string text, double fontSize, Brush brush, double pixelsPerDip) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            UiBoldTypeface,
            fontSize,
            brush,
            pixelsPerDip);

    public static FormattedText MonoText(string text, double fontSize, Brush brush, double pixelsPerDip) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            MonoTypeface,
            fontSize,
            brush,
            pixelsPerDip);

    public static void DrawCentered(DrawingContext dc, FormattedText text, Rect bounds) =>
        dc.DrawText(
            text,
            new Point(
                bounds.X + (bounds.Width - text.Width) * 0.5,
                bounds.Y + (bounds.Height - text.Height) * 0.5));

    public static void DrawInkCentered(DrawingContext dc, FormattedText text, Rect bounds)
    {
        var ink = text.BuildGeometry(new Point(0, 0)).Bounds;
        if (ink.IsEmpty)
        {
            DrawCentered(dc, text, bounds);
            return;
        }

        dc.DrawText(
            text,
            new Point(
                bounds.X + (bounds.Width - ink.Width) * 0.5 - ink.X,
                bounds.Y + (bounds.Height - ink.Height) * 0.5 - ink.Y));
    }
}

internal static class Theme
{
    public static Color Get(string key)
    {
        if (Application.Current?.TryFindResource(key) is SolidColorBrush brush)
        {
            return brush.Color;
        }

        throw new InvalidOperationException(UiStrings.ErrUndefinedColorKey(key));
    }
}

/// <summary>Controls.xaml の TipLockFrame 参照用スタブ。</summary>
internal static class TipLockFrame
{
    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.RegisterAttached(
            "IsActive",
            typeof(bool),
            typeof(TipLockFrame),
            new FrameworkPropertyMetadata(false));

    public static void SetIsActive(DependencyObject element, bool value) =>
        element.SetValue(IsActiveProperty, value);

    public static bool GetIsActive(DependencyObject element) =>
        (bool)element.GetValue(IsActiveProperty);
}
