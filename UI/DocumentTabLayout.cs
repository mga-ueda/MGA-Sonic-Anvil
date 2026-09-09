using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

internal readonly record struct DocumentTabSlot(double Preferred, double Min, bool KeepWide);

/// <summary>
/// タブバーの幅配分。矢印の前にアクティブ以外を縮め、
/// タイトルが 6 文字を切るところまで縮めても足りないときだけ送る。
/// </summary>
internal static class DocumentTabLayout
{
    public const int MinVisibleChars = 6;
    public const double MaxTabWidth = 220;
    public const double TitleFontSize = 11;
    public const double CloseWidth = 14;
    public const double GridPadLeft = 10;
    public const double GridPadRight = 4;
    public const double TitleMarginRight = 6;
    public const double BorderRight = 1;

    private static readonly Typeface TitleTypeface =
        new(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static double ChromeWidth =>
        BorderRight + GridPadLeft + GridPadRight + TitleMarginRight + CloseWidth;

    public static string MinVisibleText(string? title)
    {
        title ??= "";
        return title.Length <= MinVisibleChars ? title : title[..MinVisibleChars];
    }

    public static double MeasureTitle(string text, double pixelsPerDip)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            TitleTypeface,
            TitleFontSize,
            Brushes.Black,
            Math.Max(1e-6, pixelsPerDip));
        return formatted.WidthIncludingTrailingWhitespace;
    }

    public static double WidthFor(double titleWidth) =>
        Math.Clamp(ChromeWidth + Math.Max(0, titleWidth), ChromeWidth, MaxTabWidth);

    public static double PreferredWidth(string? title, double pixelsPerDip) =>
        WidthFor(MeasureTitle(title ?? "", pixelsPerDip));

    public static double MinWidth(string? title, double pixelsPerDip) =>
        WidthFor(MeasureTitle(MinVisibleText(title), pixelsPerDip));

    public static bool FitsWithoutArrows(ReadOnlySpan<DocumentTabSlot> tabs, double hostWidth)
    {
        if (hostWidth <= 0)
        {
            return true;
        }

        var need = 0d;
        foreach (var tab in tabs)
        {
            need += tab.KeepWide ? tab.Preferred : tab.Min;
        }

        return need <= hostWidth + 0.5;
    }

    public static void Allocate(ReadOnlySpan<DocumentTabSlot> tabs, double available, Span<double> widths)
    {
        if (widths.Length < tabs.Length)
        {
            throw new ArgumentException("widths is shorter than tabs.", nameof(widths));
        }

        var sum = 0d;
        for (var i = 0; i < tabs.Length; i++)
        {
            var min = Math.Max(0, tabs[i].Min);
            var preferred = Math.Clamp(tabs[i].Preferred, min, MaxTabWidth);
            widths[i] = preferred;
            sum += preferred;
        }

        var overflow = sum - available;
        while (overflow > 0.5)
        {
            var shrinkable = 0;
            for (var i = 0; i < tabs.Length; i++)
            {
                if (!tabs[i].KeepWide && widths[i] > tabs[i].Min + 1e-3)
                {
                    shrinkable++;
                }
            }

            if (shrinkable == 0)
            {
                break;
            }

            var cut = overflow / shrinkable;
            var shrunk = 0d;
            for (var i = 0; i < tabs.Length; i++)
            {
                if (tabs[i].KeepWide || widths[i] <= tabs[i].Min + 1e-3)
                {
                    continue;
                }

                var take = Math.Min(cut, widths[i] - tabs[i].Min);
                widths[i] -= take;
                shrunk += take;
            }

            if (shrunk <= 1e-6)
            {
                break;
            }

            overflow -= shrunk;
        }
    }
}
