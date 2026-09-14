namespace MgaSonicAnvil.Domain;

/// <summary>全タブのファイル名と長さ。クリップボードは名前と時間をタブで区切った行。</summary>
internal static class TabTimeList
{
    public static string FormatLine(string fileName, long frameCount, int sampleRate) =>
        (fileName ?? string.Empty) + "\t" + UiStrings.FormatTimecode(frameCount, sampleRate);

    public static string Format(IReadOnlyList<(string FileName, long FrameCount, int SampleRate)> tabs)
    {
        if (tabs.Count == 0)
        {
            return string.Empty;
        }

        var lines = new string[tabs.Count];
        for (var i = 0; i < tabs.Count; i++)
        {
            var tab = tabs[i];
            lines[i] = FormatLine(tab.FileName, tab.FrameCount, tab.SampleRate);
        }

        return string.Join("\r\n", lines);
    }
}
