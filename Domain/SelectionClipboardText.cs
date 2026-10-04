namespace MgaSonicAnvil.Domain;

internal enum SelectionClipboardPathKind
{
    FileName,
    FullPath,
}

/// <summary>波形選択の開始〜終了をクリップボード用テキストにする。</summary>
internal static class SelectionClipboardText
{
    public static string RangeSeparator =>
        UiStrings.IsJapanese ? " ～ " : " - ";

    public static bool CanCopy(
        IReadOnlyList<(string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)> items)
    {
        foreach (var item in items)
        {
            if (!item.Selection.IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 「パス見出し\t開始 ～ 終了」を行でつなぐ。選択が空の項目は飛ばす。
    /// 区切りは日本語「 ～ 」、英語「 - 」。見出しはファイル名のみ／フルパス。
    /// </summary>
    public static string Format(
        IReadOnlyList<(string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)> items,
        SelectionClipboardPathKind pathKind,
        bool asSamples)
    {
        if (items.Count == 0)
        {
            return string.Empty;
        }

        var separator = RangeSeparator;
        List<string>? lines = null;
        foreach (var item in items)
        {
            if (item.Selection.IsEmpty)
            {
                continue;
            }

            var start = UiStrings.FormatStatusTime(item.Selection.StartFrame, item.SampleRate, asSamples);
            var end = UiStrings.FormatStatusTime(item.Selection.EndFrame, item.SampleRate, asSamples);
            var label = PathLabel(item.SourcePath, item.DisplayName, pathKind);
            lines ??= [];
            lines.Add(label + "\t" + start + separator + end);
        }

        return lines is null || lines.Count == 0
            ? string.Empty
            : string.Join("\r\n", lines);
    }

    internal static string PathLabel(string? sourcePath, string displayName, SelectionClipboardPathKind pathKind)
    {
        if (pathKind == SelectionClipboardPathKind.FileName)
        {
            return DocumentFileNames.NameForEdit(sourcePath, displayName);
        }

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return displayName ?? string.Empty;
        }

        try
        {
            return Path.GetFullPath(sourcePath);
        }
        catch (Exception)
        {
            return sourcePath;
        }
    }
}
