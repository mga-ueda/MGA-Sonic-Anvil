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

    [Fact]
    public void FromDurations_FormatsSecondsLikePlaylist()
    {
        var rows = TabTimeList.FromDurations(
        [
            ("a.wav", 93.722),
            ("b.wav", 3.048),
            ("c.wav", 0),
        ]);
        Assert.Equal(3, rows.Count);
        Assert.Equal("a.wav", rows[0].FileName);
        Assert.Equal(UiStrings.FormatDuration(93.722), rows[0].Duration);
        Assert.Equal(UiStrings.FormatDuration(3.048), rows[1].Duration);
        Assert.Equal("00:00.000", rows[2].Duration);
    }

    [Fact]
    public void SumDurationSeconds_AddsPositiveRows()
    {
        var rows = TabTimeList.FromDurations(
        [
            ("a.wav", 93.722),
            ("b.wav", 3.048),
            ("c.wav", 0),
        ]);
        Assert.Equal(96.77, TabTimeList.SumDurationSeconds(rows), 3);
        Assert.Equal(UiStrings.FormatDuration(96.77), TabTimeList.FormatTotalDuration(rows));
        Assert.Equal(0, TabTimeList.SumDurationSeconds([]));
    }

    [Fact]
    public void WithTotalRow_AppendsTotalForCopyCsvPdf()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var rows = TabTimeList.WithTotalRow(TabTimeList.FromDurations(
            [
                ("a.wav", 93.722),
                ("b.wav", 3.048),
            ]));
            Assert.Equal(3, rows.Count);
            Assert.False(rows[0].IsTotal);
            Assert.Equal("a.wav", rows[0].FileName);
            Assert.True(rows[2].IsTotal);
            Assert.Equal(UiStrings.TabTimeTotalLabel, rows[2].FileName);
            Assert.Equal(UiStrings.FormatDuration(96.77), rows[2].Duration);
            Assert.Equal(96.77, TabTimeList.SumDurationSeconds(rows), 3);
            Assert.EndsWith(
                UiStrings.TabTimeTotalLabel + "\t" + UiStrings.FormatDuration(96.77),
                TabTimeList.FormatTsv(rows, headers: false));
            Assert.EndsWith(
                TabTimeList.CsvEscape(UiStrings.TabTimeTotalLabel) + "," + TabTimeList.CsvEscape(UiStrings.FormatDuration(96.77)),
                TabTimeList.FormatCsv(rows));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }
}
