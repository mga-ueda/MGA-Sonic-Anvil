using System.Text;

namespace MgaSonicAnvil.Domain;

/// <summary>全タブのファイル名と長さ。</summary>
internal readonly record struct TabTimeRow(int Index, string FileName, string Duration)
{
    public static TabTimeRow Create(int index, string fileName, long frameCount, int sampleRate) =>
        new(index, fileName ?? string.Empty, UiStrings.FormatTimecode(frameCount, sampleRate));
}

/// <summary>表上のセル範囲（両端含む）。</summary>
internal readonly record struct TabTimeSelection(int RowStart, int RowEnd, int ColStart, int ColEnd);

/// <summary>全タブのファイル名と長さ。クリップボードは名前と時間をタブで区切った行。</summary>
internal static class TabTimeList
{
    public const int ColumnCount = 2;

    public static string FormatLine(string fileName, long frameCount, int sampleRate) =>
        (fileName ?? string.Empty) + "\t" + UiStrings.FormatTimecode(frameCount, sampleRate);

    public static IReadOnlyList<TabTimeRow> FromTabs(
        IReadOnlyList<(string FileName, long FrameCount, int SampleRate)> tabs)
    {
        if (tabs.Count == 0)
        {
            return [];
        }

        var rows = new TabTimeRow[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            rows[i] = TabTimeRow.Create(i, tab.FileName, tab.FrameCount, tab.SampleRate);
        }

        return rows;
    }

    public static string Cell(in TabTimeRow row, int column) =>
        column == 0 ? row.FileName : row.Duration;

    public static string Format(IReadOnlyList<(string FileName, long FrameCount, int SampleRate)> tabs) =>
        FormatTsv(FromTabs(tabs), headers: false);

    public static string FormatTsv(IReadOnlyList<TabTimeRow> rows, bool headers)
    {
        var extra = headers ? 1 : 0;
        if (rows.Count + extra == 0)
        {
            return string.Empty;
        }

        var lines = new string[rows.Count + extra];
        var i = 0;
        if (headers)
        {
            lines[i++] = UiStrings.TabTimeColumnFile + "\t" + UiStrings.TabTimeColumnTime;
        }

        foreach (var row in rows)
        {
            lines[i++] = row.FileName + "\t" + row.Duration;
        }

        return string.Join("\r\n", lines);
    }

    public static string FormatCsv(IReadOnlyList<TabTimeRow> rows)
    {
        var lines = new string[rows.Count + 1];
        lines[0] = CsvEscape(UiStrings.TabTimeColumnFile) + "," + CsvEscape(UiStrings.TabTimeColumnTime);
        for (var i = 0; i < rows.Count; i++)
        {
            lines[i + 1] = CsvEscape(rows[i].FileName) + "," + CsvEscape(rows[i].Duration);
        }

        return string.Join("\r\n", lines);
    }

    public static void WriteCsv(Stream stream, IReadOnlyList<TabTimeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        stream.Write(encoding.GetPreamble());
        stream.Write(encoding.GetBytes(FormatCsv(rows)));
    }

    public static string FormatRange(IReadOnlyList<TabTimeRow> rows, TabTimeSelection selection)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var rowStart = Math.Clamp(Math.Min(selection.RowStart, selection.RowEnd), 0, rows.Count - 1);
        var rowEnd = Math.Clamp(Math.Max(selection.RowStart, selection.RowEnd), 0, rows.Count - 1);
        var colStart = Math.Clamp(Math.Min(selection.ColStart, selection.ColEnd), 0, ColumnCount - 1);
        var colEnd = Math.Clamp(Math.Max(selection.ColStart, selection.ColEnd), 0, ColumnCount - 1);
        var lines = new string[rowEnd - rowStart + 1];
        for (var r = rowStart; r <= rowEnd; r++)
        {
            var width = colEnd - colStart + 1;
            var cells = new string[width];
            for (var c = colStart; c <= colEnd; c++)
            {
                cells[c - colStart] = Cell(rows[r], c);
            }

            lines[r - rowStart] = string.Join("\t", cells);
        }

        return string.Join("\r\n", lines);
    }

    public static bool CoversAll(int rowCount, TabTimeSelection selection)
    {
        if (rowCount <= 0)
        {
            return false;
        }

        var rowStart = Math.Min(selection.RowStart, selection.RowEnd);
        var rowEnd = Math.Max(selection.RowStart, selection.RowEnd);
        var colStart = Math.Min(selection.ColStart, selection.ColEnd);
        var colEnd = Math.Max(selection.ColStart, selection.ColEnd);
        return rowStart <= 0
            && rowEnd >= rowCount - 1
            && colStart <= 0
            && colEnd >= ColumnCount - 1;
    }

    public static string FormatCopy(IReadOnlyList<TabTimeRow> rows, TabTimeSelection? selection)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        if (selection is not { } selected || CoversAll(rows.Count, selected))
        {
            return FormatTsv(rows, headers: true);
        }

        return FormatRange(rows, selected);
    }

    internal static string CsvEscape(string? value)
    {
        value ??= string.Empty;
        var quote = false;
        foreach (var c in value)
        {
            if (c is ',' or '"' or '\r' or '\n')
            {
                quote = true;
                break;
            }
        }

        return quote
            ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : value;
    }
}
