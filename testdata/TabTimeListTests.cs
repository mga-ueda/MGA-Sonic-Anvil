using System.IO;
using System.Text;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TabTimeListTests
{
    [Fact]
    public void FormatLine_JoinsFileNameTabAndTimecode()
    {
        Assert.Equal("kick.wav\t00:01.000", TabTimeList.FormatLine("kick.wav", 48000, 48000));
        Assert.Equal("untitled\t00:00.000", TabTimeList.FormatLine("untitled", 0, 48000));
    }

    [Fact]
    public void Format_JoinsRowsWithCrlf()
    {
        var text = TabTimeList.Format(
        [
            ("a.wav", 48000, 48000),
            ("b.wav", 96000, 48000),
        ]);
        Assert.Equal("a.wav\t00:01.000\r\nb.wav\t00:02.000", text);
    }

    [Fact]
    public void Format_EmptyTabs_IsEmpty()
    {
        Assert.Equal(string.Empty, TabTimeList.Format([]));
    }

    [Fact]
    public void FormatRange_CopiesSelectedCellsAsTsv()
    {
        var rows = TabTimeList.FromTabs(
        [
            ("a.wav", 48000, 48000),
            ("b.wav", 96000, 48000),
        ]);
        Assert.Equal("a.wav", TabTimeList.FormatRange(rows, new TabTimeSelection(0, 0, 0, 0)));
        Assert.Equal("00:01.000\r\n00:02.000", TabTimeList.FormatRange(rows, new TabTimeSelection(0, 1, 1, 1)));
        Assert.Equal("a.wav\t00:01.000\r\nb.wav\t00:02.000", TabTimeList.FormatRange(rows, new TabTimeSelection(1, 0, 1, 0)));
    }

    [Fact]
    public void FormatCopy_UsesHeadersWhenNothingOrEverythingSelected()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            var rows = TabTimeList.FromTabs([("a.wav", 48000, 48000)]);
            Assert.Equal("File\tTime\r\na.wav\t00:01.000", TabTimeList.FormatCopy(rows, null));
            Assert.Equal(
                "File\tTime\r\na.wav\t00:01.000",
                TabTimeList.FormatCopy(rows, new TabTimeSelection(0, 0, 0, 1)));
            Assert.Equal("a.wav", TabTimeList.FormatCopy(rows, new TabTimeSelection(0, 0, 0, 0)));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FormatCsv_EscapesCommaAndQuote_AndWriteCsvAddsUtf8Bom()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            var rows = TabTimeList.FromTabs([("a,b \"c\".wav", 48000, 48000)]);
            Assert.Equal("File,Time\r\n\"a,b \"\"c\"\".wav\",00:01.000", TabTimeList.FormatCsv(rows));
            using var stream = new MemoryStream();
            TabTimeList.WriteCsv(stream, rows);
            var bytes = stream.ToArray();
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Equal(
                "File,Time\r\n\"a,b \"\"c\"\".wav\",00:01.000",
                Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void CsvEscape_QuotesSpecials()
    {
        Assert.Equal("plain", TabTimeList.CsvEscape("plain"));
        Assert.Equal("\"a,b\"", TabTimeList.CsvEscape("a,b"));
        Assert.Equal("\"a\"\"b\"", TabTimeList.CsvEscape("a\"b"));
        Assert.Equal("\"a\r\nb\"", TabTimeList.CsvEscape("a\r\nb"));
    }
}
