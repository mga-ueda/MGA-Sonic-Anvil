using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>設定のコンボを、項目の文字＋クローム＋少しの余白で幅決めする。</summary>
internal static class ComboBoxFit
{
    /// <summary>DarkComboBoxStyle の矢印列。</summary>
    public const double ArrowColumnWidth = 22;

    /// <summary>文字の左右に足す余白。</summary>
    public const double ExtraMargin = 8;

    public static double WidthFor(double textWidth, Thickness padding, Thickness border, double scrollBar = 0) =>
        Math.Ceiling(Math.Max(0, textWidth))
        + padding.Left + padding.Right
        + border.Left + border.Right
        + ArrowColumnWidth
        + ExtraMargin
        + Math.Max(0, scrollBar);

    public static void Apply(ComboBox combo, double maxWidth = double.PositiveInfinity) =>
        Fit(combo, maxWidth, selectedOnly: false);

    public static void ApplySelected(ComboBox combo, double maxWidth = double.PositiveInfinity) =>
        Fit(combo, maxWidth, selectedOnly: true);

    private static void Fit(ComboBox combo, double maxWidth, bool selectedOnly)
    {
        var text = selectedOnly ? SelectedItemWidth(combo) : MaxItemWidth(combo);
        var width = WidthFor(text, combo.Padding, combo.BorderThickness);
        if (maxWidth is > 0 and < double.PositiveInfinity)
        {
            width = Math.Min(width, maxWidth);
        }

        combo.HorizontalAlignment = HorizontalAlignment.Left;
        var fitted = Math.Max(width, ArrowColumnWidth + ExtraMargin);
        combo.MinWidth = fitted;
        combo.MaxWidth = fitted;
        combo.Width = fitted;
    }

    public static double MeasureText(
        FrameworkElement host,
        string text,
        double fontSize,
        FontWeight? weight = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var family = host.GetValue(TextBlock.FontFamilyProperty) as FontFamily
            ?? new FontFamily("Yu Gothic UI");
        var style = (FontStyle)host.GetValue(TextBlock.FontStyleProperty);
        var fontWeight = weight ?? (FontWeight)host.GetValue(TextBlock.FontWeightProperty);
        var stretch = (FontStretch)host.GetValue(TextBlock.FontStretchProperty);
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(family, style, fontWeight, stretch),
            fontSize,
            Brushes.Black,
            PixelsPerDip(host));
        return formatted.WidthIncludingTrailingWhitespace;
    }

    public static double MaxItemWidth(ComboBox combo)
    {
        var max = 0d;
        foreach (var item in combo.Items)
        {
            max = Math.Max(max, MeasureItem(combo, item));
        }

        return max;
    }

    public static double SelectedItemWidth(ComboBox combo)
    {
        var selected = MeasureItem(combo, combo.SelectedItem);
        return selected > 0 ? selected : MaxItemWidth(combo);
    }

    private static double MeasureItem(ComboBox combo, object? item)
    {
        var text = ItemText(item);
        if (text.Length == 0)
        {
            return 0;
        }

        var fontSize = combo.FontSize > 0 ? combo.FontSize : 11;
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(combo.FontFamily, combo.FontStyle, combo.FontWeight, combo.FontStretch),
            fontSize,
            Brushes.Black,
            PixelsPerDip(combo));
        return formatted.WidthIncludingTrailingWhitespace;
    }

    internal static string ItemText(object? item) =>
        item switch
        {
            null => string.Empty,
            string text => text,
            ComboBoxItem box => box.Content?.ToString() ?? string.Empty,
            _ => item.ToString() ?? string.Empty,
        };

    private static double PixelsPerDip(Visual visual)
    {
        if (visual is FrameworkElement { IsLoaded: true })
        {
            var dpi = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
            if (dpi >= 0.01)
            {
                return dpi;
            }
        }

        if (Application.Current?.MainWindow is { } window)
        {
            var dpi = VisualTreeHelper.GetDpi(window).PixelsPerDip;
            if (dpi >= 0.01)
            {
                return dpi;
            }
        }

        return 1d;
    }
}
