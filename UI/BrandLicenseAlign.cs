using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ロゴ画像の実インク下端と、ライセンス文字のベースラインを同じ高さに揃える。
/// </summary>
internal static class BrandLicenseAlign
{
    public static string FirstLine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var end = text.IndexOfAny(['\r', '\n']);
        return end < 0 ? text : text[..end];
    }

    public static int LineCount(string? text) => Lines(text).Length;

    public static string[] Lines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
    }

    public static int FindLineIndex(string? text, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return -1;
        }

        var lines = Lines(text);
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.Equals(lines[i], line, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>上揃えのとき、指定行のベースラインをロゴインク下端へ動かす量。</summary>
    public static double LineBaselineShiftY(
        double logoInkBottom,
        double lineBoxHeight,
        int lineIndex,
        double lineBaseline)
    {
        var box = Math.Max(0, lineBoxHeight);
        var index = Math.Max(0, lineIndex);
        return logoInkBottom - (index * box + lineBaseline);
    }

    /// <summary>上揃えのとき、1行目ベースラインをロゴインク下端へ動かす量。</summary>
    public static double FirstLineShiftY(double logoInkBottom, double firstBaseline) =>
        LineBaselineShiftY(logoInkBottom, 0, 0, firstBaseline);

    /// <summary>
    /// 文字を下へ動かす量。ロゴの余白と文字ボックス下の余白の差。
    /// </summary>
    public static double TextShiftY(
        double imageWidth,
        double imageHeight,
        double sourceWidth,
        double sourceHeight,
        double inkBottomFraction,
        double textBlockHeight,
        double textBaseline)
    {
        if (imageHeight <= 0 || textBlockHeight <= 0)
        {
            return 0;
        }

        var logoInkBottom = LogoInkBottomInBox(
            imageWidth,
            imageHeight,
            sourceWidth,
            sourceHeight,
            inkBottomFraction);
        var logoPadBelow = imageHeight - logoInkBottom;
        var textPadBelow = textBlockHeight - textBaseline;
        return textPadBelow - logoPadBelow;
    }

    public static double LogoInkBottomInBox(
        double imageWidth,
        double imageHeight,
        double sourceWidth,
        double sourceHeight,
        double inkBottomFraction)
    {
        if (imageWidth <= 0 || imageHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
        {
            return imageHeight;
        }

        var scale = Math.Min(imageWidth / sourceWidth, imageHeight / sourceHeight);
        var drawnHeight = sourceHeight * scale;
        var offsetY = (imageHeight - drawnHeight) * 0.5;
        return offsetY + Math.Clamp(inkBottomFraction, 0, 1) * drawnHeight;
    }

    public static bool TryInkFractions(BitmapSource? source, out double topFraction, out double bottomFraction)
    {
        topFraction = 0;
        bottomFraction = 1;
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0)
        {
            return false;
        }

        var width = source.PixelWidth;
        var height = source.PixelHeight;
        var pixels = new byte[width * height * 4];
        source.CopyPixels(pixels, width * 4, 0);
        var top = -1;
        var bottom = -1;
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            var ink = false;
            for (var x = 0; x < width; x++)
            {
                var i = row + x * 4;
                if (IsLogoInk(pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]))
                {
                    ink = true;
                    break;
                }
            }

            if (!ink)
            {
                continue;
            }

            if (top < 0)
            {
                top = y;
            }

            bottom = y;
        }

        if (top < 0)
        {
            return false;
        }

        topFraction = top / (double)height;
        bottomFraction = (bottom + 1) / (double)height;
        return true;
    }

    public static (double Height, double Baseline) MeasureLine(
        TextBlock block,
        string text,
        double pixelsPerDip)
    {
        if (string.IsNullOrEmpty(text) || block.FontSize <= 0)
        {
            return (block.ActualHeight, block.ActualHeight);
        }

        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            block.FlowDirection,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize,
            Brushes.Black,
            new NumberSubstitution(),
            TextOptions.GetTextFormattingMode(block),
            pixelsPerDip <= 0 ? 1 : pixelsPerDip);
        return (formatted.Height, formatted.Baseline);
    }

    public static double TextBaseline(TextBlock block, double pixelsPerDip) =>
        MeasureLine(block, block.Text ?? string.Empty, pixelsPerDip).Baseline;

    internal static bool IsLogoInk(byte blue, byte green, byte red, byte alpha) =>
        alpha >= 16;
}
