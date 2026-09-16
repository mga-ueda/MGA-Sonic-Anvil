using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// タブ時間表の PDF。日本語を確実に出すため、ページを描画して JPEG で埋め込む。
/// </summary>
internal static class TabTimePdf
{
    internal const int PageWidthPt = 595;
    internal const int PageHeightPt = 842;
    internal const double RenderScale = 2;

    private const double Margin = 40;
    private const double TitleSize = 14;
    private const double BodySize = 10;
    private const double TimeColumnWidth = 108;
    private const double CellPad = 6;

    private static readonly Typeface BodyTypeface = new(
        new FontFamily("Yu Gothic UI"),
        FontStyles.Normal,
        FontWeights.Normal,
        FontStretches.Normal);

    private static readonly Typeface TitleTypeface = new(
        new FontFamily("Yu Gothic UI"),
        FontStyles.Normal,
        FontWeights.SemiBold,
        FontStretches.Normal);

    private static readonly SolidColorBrush PageBack = Frozen(Colors.White);
    private static readonly SolidColorBrush TextBrush = Frozen(Color.FromRgb(0x1A, 0x1C, 0x1E));
    private static readonly SolidColorBrush HeaderBack = Frozen(Color.FromRgb(0xE8, 0xEA, 0xED));
    private static readonly Pen GridPen = CreatePen(Color.FromRgb(0xC4, 0xC7, 0xCA), 0.75);

    public static void Write(
        Stream stream,
        IReadOnlyList<TabTimeRow> rows,
        string title,
        string fileHeader,
        string timeHeader)
    {
        ArgumentNullException.ThrowIfNull(stream);
        WritePdf(stream, RenderPages(rows, title, fileHeader, timeHeader));
    }

    internal static void WritePdf(Stream stream, IReadOnlyList<(byte[] Jpeg, int Width, int Height)> pages)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (pages.Count == 0)
        {
            throw new ArgumentException("PDF needs at least one page.", nameof(pages));
        }

        var count = 2 + (pages.Count * 3);
        var bodies = new byte[count + 1][];
        bodies[1] = Ascii("<< /Type /Catalog /Pages 2 0 R >>\n");
        var kids = new StringBuilder();
        kids.Append("<< /Type /Pages /Count ").Append(pages.Count).Append(" /Kids [");
        for (var i = 0; i < pages.Count; i++)
        {
            if (i > 0)
            {
                kids.Append(' ');
            }

            kids.Append(PageId(i)).Append(" 0 R");
        }

        kids.Append("] >>\n");
        bodies[2] = Ascii(kids.ToString());

        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            var pageId = PageId(i);
            var imageId = ImageId(i);
            var contentId = ContentId(i);
            bodies[pageId] = Ascii(
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 "
                + PageWidthPt + " " + PageHeightPt
                + "] /Resources << /XObject << /Im0 " + imageId
                + " 0 R >> >> /Contents " + contentId + " 0 R >>\n");
            bodies[imageId] = ImageObject(page.Jpeg, page.Width, page.Height);
            var content = "q\n" + PageWidthPt + " 0 0 " + PageHeightPt + " 0 0 cm\n/Im0 Do\nQ\n";
            bodies[contentId] = Ascii(
                "<< /Length " + content.Length + " >>\nstream\n" + content + "endstream\n");
        }

        WriteFile(stream, bodies, count);
    }

    private static IReadOnlyList<(byte[] Jpeg, int Width, int Height)> RenderPages(
        IReadOnlyList<TabTimeRow> rows,
        string title,
        string fileHeader,
        string timeHeader)
    {
        rows ??= [];
        title ??= string.Empty;
        fileHeader ??= string.Empty;
        timeHeader ??= string.Empty;
        var pages = new List<(byte[] Jpeg, int Width, int Height)>();
        var index = 0;
        while (true)
        {
            var visual = new DrawingVisual();
            int consumed;
            using (var dc = visual.RenderOpen())
            {
                consumed = DrawPage(dc, rows, index, title, fileHeader, timeHeader);
            }

            var bitmap = RenderVisual(visual);
            pages.Add((EncodeJpeg(bitmap), bitmap.PixelWidth, bitmap.PixelHeight));
            if (consumed <= 0)
            {
                break;
            }

            index += consumed;
            if (index >= rows.Count)
            {
                break;
            }
        }

        return pages;
    }

    private static int DrawPage(
        DrawingContext dc,
        IReadOnlyList<TabTimeRow> rows,
        int start,
        string title,
        string fileHeader,
        string timeHeader)
    {
        dc.DrawRectangle(PageBack, null, new Rect(0, 0, PageWidthPt, PageHeightPt));
        var titleText = Measure(title, TitleSize, TitleTypeface);
        var y = Margin;
        dc.DrawText(titleText, new Point(Margin, y));
        y += titleText.Height + 12;

        var contentWidth = PageWidthPt - (Margin * 2);
        var nameWidth = Math.Max(80, contentWidth - TimeColumnWidth);
        var headerHeight = Math.Max(
            Measure(fileHeader, BodySize, TitleTypeface, nameWidth - (CellPad * 2)).Height,
            Measure(timeHeader, BodySize, TitleTypeface).Height) + (CellPad * 2);
        DrawRow(dc, y, nameWidth, headerHeight, fileHeader, timeHeader, HeaderBack, TitleTypeface);
        y += headerHeight;

        var consumed = 0;
        var bottom = PageHeightPt - Margin;
        for (var i = start; i < rows.Count; i++)
        {
            var row = rows[i];
            var nameText = Measure(row.FileName, BodySize, BodyTypeface, nameWidth - (CellPad * 2));
            var timeText = Measure(row.Duration, BodySize, BodyTypeface);
            var height = Math.Max(nameText.Height, timeText.Height) + (CellPad * 2);
            if (consumed > 0 && y + height > bottom)
            {
                break;
            }

            if (height > bottom - Margin && consumed == 0)
            {
                height = Math.Max(22, bottom - y);
            }

            DrawRow(dc, y, nameWidth, height, row.FileName, row.Duration, Brushes.Transparent, BodyTypeface);
            y += height;
            consumed++;
            if (y >= bottom)
            {
                break;
            }
        }

        return Math.Max(0, consumed);
    }

    private static void DrawRow(
        DrawingContext dc,
        double y,
        double nameWidth,
        double height,
        string fileName,
        string duration,
        Brush fill,
        Typeface typeface)
    {
        var nameRect = new Rect(Margin, y, nameWidth, height);
        var timeRect = new Rect(Margin + nameWidth, y, TimeColumnWidth, height);
        if (!ReferenceEquals(fill, Brushes.Transparent))
        {
            dc.DrawRectangle(fill, null, new Rect(Margin, y, nameWidth + TimeColumnWidth, height));
        }

        dc.DrawRectangle(null, GridPen, nameRect);
        dc.DrawRectangle(null, GridPen, timeRect);
        DrawCellText(dc, fileName, nameRect, typeface, TextAlignment.Left);
        DrawCellText(dc, duration, timeRect, typeface, TextAlignment.Right);
    }

    private static void DrawCellText(
        DrawingContext dc,
        string text,
        Rect rect,
        Typeface typeface,
        TextAlignment align)
    {
        var formatted = Measure(text, BodySize, typeface, Math.Max(8, rect.Width - (CellPad * 2)));
        var x = align == TextAlignment.Right
            ? rect.Right - CellPad - formatted.Width
            : rect.X + CellPad;
        var y = rect.Y + Math.Max(0, (rect.Height - formatted.Height) / 2);
        dc.DrawText(formatted, new Point(x, y));
    }

    private static FormattedText Measure(string text, double size, Typeface typeface, double maxWidth = 0)
    {
        var formatted = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            TextBrush,
            1);
        if (maxWidth > 0)
        {
            formatted.MaxTextWidth = maxWidth;
            formatted.MaxLineCount = 4;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }

        return formatted;
    }

    private static BitmapSource RenderVisual(Visual visual)
    {
        var dpi = 96 * RenderScale;
        var bmp = new RenderTargetBitmap(
            (int)Math.Ceiling(PageWidthPt * RenderScale),
            (int)Math.Ceiling(PageHeightPt * RenderScale),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    internal static byte[] EncodeJpeg(BitmapSource bitmap)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 90 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }

    private static byte[] ImageObject(byte[] jpeg, int width, int height)
    {
        var header = Ascii(
            "<< /Type /XObject /Subtype /Image /Width " + width
            + " /Height " + height
            + " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length "
            + jpeg.Length + " >>\nstream\n");
        var footer = Ascii("\nendstream\n");
        var result = new byte[header.Length + jpeg.Length + footer.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(jpeg, 0, result, header.Length, jpeg.Length);
        Buffer.BlockCopy(footer, 0, result, header.Length + jpeg.Length, footer.Length);
        return result;
    }

    private static void WriteFile(Stream stream, byte[][] bodies, int count)
    {
        var header = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34, 0x0A, 0x25, 0xE2, 0xE3, 0xCF, 0xD3, 0x0A };
        stream.Write(header);
        var offsets = new long[count + 1];
        for (var i = 1; i <= count; i++)
        {
            offsets[i] = stream.Position;
            var start = Ascii(i.ToString(CultureInfo.InvariantCulture) + " 0 obj\n");
            stream.Write(start);
            stream.Write(bodies[i]);
            stream.Write(Ascii("endobj\n"));
        }

        var xref = stream.Position;
        var trailer = new StringBuilder();
        trailer.Append("xref\n0 ").Append(count + 1).Append('\n');
        trailer.Append("0000000000 65535 f \n");
        for (var i = 1; i <= count; i++)
        {
            trailer.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
        }

        trailer.Append("trailer\n<< /Size ").Append(count + 1).Append(" /Root 1 0 R >>\nstartxref\n");
        trailer.Append(xref).Append("\n%%EOF\n");
        stream.Write(Ascii(trailer.ToString()));
    }

    private static int PageId(int index) => 3 + (index * 3);

    private static int ImageId(int index) => 4 + (index * 3);

    private static int ContentId(int index) => 5 + (index * 3);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
