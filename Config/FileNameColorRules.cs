using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// ファイル名に含まれる文字列と、Wwise の色パレット番号。
/// 上から見て最初に一致した定義を使う。
/// </summary>
internal sealed class FileNameColorRule
{
    public string Pattern { get; set; } = string.Empty;

    /// <summary><see cref="FileNameColorPalette.Count"/> 未満。</summary>
    public int ColorIndex { get; set; }
}

/// <summary>
/// Wwise の色定義。上段 14 色、下段 14 色。左から右。
/// 添付パレット画像の各マスは単色なので、その RGB をそのまま使う。
/// </summary>
internal static class FileNameColorPalette
{
    public const int Count = 28;

    private static readonly string[] Hex =
    [
        "525252", "393CD5", "165FD4", "007580", "008000", "5B8000", "857C00",
        "A37614", "AA6113", "A43F28", "A92D2D", "A611A6", "8F00D6", "6905FF",
        "6E6E6E", "6668D1", "5075C3", "468686", "538D53", "7B9041", "9B942C",
        "A48341", "A57745", "A76658", "AF5A5A", "B34D97", "A344C1", "8462D5",
    ];

    public static ReadOnlySpan<string> Colors => Hex;

    public static string HexLabel(int index) =>
        (uint)index < (uint)Hex.Length ? "#" + Hex[index] : string.Empty;

    public static bool TryRgb(int index, out byte r, out byte g, out byte b)
    {
        if ((uint)index >= (uint)Hex.Length)
        {
            r = g = b = 0;
            return false;
        }

        var hex = Hex[index];
        r = Convert.ToByte(hex[..2], 16);
        g = Convert.ToByte(hex[2..4], 16);
        b = Convert.ToByte(hex[4..6], 16);
        return true;
    }
}

internal static class FileNameColorRules
{
    /// <summary>空の文字列と範囲外の色番号を捨てる。件数の上限は無い。</summary>
    public static FileNameColorRule[] Normalize(IEnumerable<FileNameColorRule>? rules)
    {
        if (rules is null)
        {
            return [];
        }

        var kept = new List<FileNameColorRule>();
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            var pattern = rule.Pattern?.Trim() ?? string.Empty;
            if (pattern.Length == 0 || (uint)rule.ColorIndex >= FileNameColorPalette.Count)
            {
                continue;
            }

            kept.Add(new FileNameColorRule
            {
                Pattern = pattern,
                ColorIndex = rule.ColorIndex,
            });
        }

        return [.. kept];
    }

    /// <summary>ファイル名にパターンが含まれる最初の定義。無ければ -1。大文字小文字は区別しない。</summary>
    public static int Match(string? fileName, IReadOnlyList<FileNameColorRule>? rules) =>
        TryMatch(fileName, rules, out var colorIndex, out _) ? colorIndex : -1;

    /// <summary>最初に一致した定義。pattern は設定に書いた文字列。</summary>
    public static bool TryMatch(
        string? fileName,
        IReadOnlyList<FileNameColorRule>? rules,
        out int colorIndex,
        out string pattern)
    {
        colorIndex = -1;
        pattern = string.Empty;
        if (string.IsNullOrEmpty(fileName) || rules is null || rules.Count == 0)
        {
            return false;
        }

        foreach (var rule in rules)
        {
            if (rule is null)
            {
                continue;
            }

            var text = rule.Pattern?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                continue;
            }

            if ((uint)rule.ColorIndex >= FileNameColorPalette.Count)
            {
                continue;
            }

            if (fileName.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                colorIndex = rule.ColorIndex;
                pattern = text;
                return true;
            }
        }

        return false;
    }
}

/// <summary>ファイルへ手で付けた色。パスが同じならファイル名ルールより優先する。</summary>
internal sealed class FileNameColorAssignment
{
    public string Path { get; set; } = string.Empty;

    public int ColorIndex { get; set; }
}

internal static class FileNameColorAssignments
{
    public static bool TryGet(IReadOnlyList<FileNameColorAssignment>? assignments, string? path, out int colorIndex)
    {
        colorIndex = -1;
        if (assignments is null || !TryKey(path, out var key))
        {
            return false;
        }

        for (var i = assignments.Count - 1; i >= 0; i--)
        {
            var item = assignments[i];
            if (item is null || (uint)item.ColorIndex >= FileNameColorPalette.Count)
            {
                continue;
            }

            if (TryKey(item.Path, out var itemKey)
                && string.Equals(itemKey, key, StringComparison.OrdinalIgnoreCase))
            {
                colorIndex = item.ColorIndex;
                return true;
            }
        }

        return false;
    }

    public static FileNameColorAssignment[] With(
        IReadOnlyList<FileNameColorAssignment>? current,
        IEnumerable<string> paths,
        int colorIndex)
    {
        if ((uint)colorIndex >= FileNameColorPalette.Count)
        {
            return Normalize(current);
        }

        var map = ToMap(current);
        foreach (var path in paths)
        {
            if (!TryKey(path, out var key))
            {
                continue;
            }

            map[key] = colorIndex;
        }

        return FromMap(map);
    }

    public static FileNameColorAssignment[] Without(
        IReadOnlyList<FileNameColorAssignment>? current,
        IEnumerable<string> paths)
    {
        var map = ToMap(current);
        foreach (var path in paths)
        {
            if (TryKey(path, out var key))
            {
                map.Remove(key);
            }
        }

        return FromMap(map);
    }

    /// <summary>名前変更で手動色を新しいパスへ移す。対象が無ければ false。</summary>
    public static bool TryRetarget(
        IReadOnlyList<FileNameColorAssignment>? current,
        string? source,
        string? destination,
        out FileNameColorAssignment[] next)
    {
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
        {
            next = Normalize(current);
            return false;
        }

        var moved = MoveTree(current, source, destination);
        next = moved ?? Normalize(current);
        return moved is not null && TryGet(next, destination, out _);
    }

    /// <summary>
    /// 移動・名前変更。操作したパスの先にファイルがあるときだけ色を移す。
    /// 操作したパスが既に無い記録は孤立なので捨てる。操作外のパスは存在を見ない。
    /// </summary>
    public static FileNameColorAssignment[]? MoveTree(
        IReadOnlyList<FileNameColorAssignment>? current,
        string source,
        string destination)
    {
        if (current is null || current.Count == 0 || !TryKey(source, out _) || !TryKey(destination, out _))
        {
            return null;
        }

        var kept = new Dictionary<string, (string Path, int Color)>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var item in current)
        {
            if (!TryRead(item, out var path, out var color) || !TryKey(path, out var key))
            {
                continue;
            }

            if (!LibraryFileTransfer.IsSameOrUnder(path, source))
            {
                kept.Remove(key);
                kept[key] = (path, color);
                continue;
            }

            if (LibraryFileTransfer.TryMapPath(path, source, destination, out var mapped)
                && TryKey(mapped, out var mappedKey)
                && PathExists(mapped))
            {
                if (!string.Equals(path, mapped, StringComparison.Ordinal))
                {
                    changed = true;
                }

                kept.Remove(mappedKey);
                kept[mappedKey] = (mapped, color);
                continue;
            }

            if (PathExists(path))
            {
                kept.Remove(key);
                kept[key] = (path, color);
                continue;
            }

            changed = true;
        }

        return changed ? ToArray(kept) : null;
    }

    /// <summary>
    /// コピー。元のファイルがある記録だけ新しいパスへ写す。
    /// 操作したパスが既に無い記録は孤立なので捨てる。操作外のパスは存在を見ない。
    /// </summary>
    public static FileNameColorAssignment[]? CopyTree(
        IReadOnlyList<FileNameColorAssignment>? current,
        string source,
        string destination)
    {
        if (current is null || current.Count == 0 || !TryKey(source, out _) || !TryKey(destination, out _))
        {
            return null;
        }

        var kept = Snapshot(current, out var changed);
        foreach (var item in current)
        {
            if (!TryRead(item, out var path, out var color)
                || !TryKey(path, out var key)
                || !LibraryFileTransfer.IsSameOrUnder(path, source))
            {
                continue;
            }

            if (!PathExists(path))
            {
                kept.Remove(key);
                changed = true;
                continue;
            }

            if (!LibraryFileTransfer.TryMapPath(path, source, destination, out var mapped)
                || !TryKey(mapped, out var mappedKey))
            {
                continue;
            }

            if (kept.TryGetValue(mappedKey, out var existing)
                && existing.Color == color
                && string.Equals(existing.Path, mapped, StringComparison.Ordinal))
            {
                continue;
            }

            kept.Remove(mappedKey);
            kept[mappedKey] = (mapped, color);
            changed = true;
        }

        return changed ? ToArray(kept) : null;
    }

    /// <summary>
    /// 削除の対象パス。そのパスがもう無い記録だけ捨てる。
    /// ファイルが残っている記録と、操作外のパスは残す。
    /// </summary>
    public static FileNameColorAssignment[]? ForgetTree(
        IReadOnlyList<FileNameColorAssignment>? current,
        string path)
    {
        if (current is null || current.Count == 0 || !TryKey(path, out _))
        {
            return null;
        }

        var kept = new Dictionary<string, (string Path, int Color)>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var item in current)
        {
            if (!TryRead(item, out var itemPath, out var color) || !TryKey(itemPath, out var key))
            {
                continue;
            }

            if (LibraryFileTransfer.IsSameOrUnder(itemPath, path) && !PathExists(itemPath))
            {
                changed = true;
                continue;
            }

            kept.Remove(key);
            kept[key] = (itemPath, color);
        }

        return changed ? ToArray(kept) : null;
    }

    /// <summary>複数パスを一度に捨てる。一つも無ければ null。</summary>
    public static FileNameColorAssignment[]? ForgetTree(
        IReadOnlyList<FileNameColorAssignment>? current,
        IEnumerable<string> paths)
    {
        FileNameColorAssignment[]? latest = null;
        var working = current;
        foreach (var path in paths)
        {
            var next = ForgetTree(working, path);
            if (next is null)
            {
                continue;
            }

            latest = next;
            working = next;
        }

        return latest;
    }

    private static Dictionary<string, int> ToMap(IReadOnlyList<FileNameColorAssignment>? current)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (current is null)
        {
            return map;
        }

        foreach (var item in current)
        {
            if (item is null
                || (uint)item.ColorIndex >= FileNameColorPalette.Count
                || !TryKey(item.Path, out var key))
            {
                continue;
            }

            map[key] = item.ColorIndex;
        }

        return map;
    }

    private static FileNameColorAssignment[] FromMap(Dictionary<string, int> map)
    {
        var items = new FileNameColorAssignment[map.Count];
        var i = 0;
        foreach (var pair in map)
        {
            items[i++] = new FileNameColorAssignment { Path = pair.Key, ColorIndex = pair.Value };
        }

        return items;
    }

    private static bool TryRead(FileNameColorAssignment? item, out string path, out int color)
    {
        path = string.Empty;
        color = -1;
        if (item is null
            || (uint)item.ColorIndex >= FileNameColorPalette.Count
            || !TryKey(item.Path, out path))
        {
            return false;
        }

        color = item.ColorIndex;
        return true;
    }

    private static Dictionary<string, (string Path, int Color)> Snapshot(
        IReadOnlyList<FileNameColorAssignment> current,
        out bool droppedInvalid)
    {
        var kept = new Dictionary<string, (string Path, int Color)>(StringComparer.OrdinalIgnoreCase);
        droppedInvalid = false;
        foreach (var item in current)
        {
            if (!TryRead(item, out var path, out var color) || !TryKey(path, out var key))
            {
                droppedInvalid = true;
                continue;
            }

            kept.Remove(key);
            kept[key] = (path, color);
        }

        return kept;
    }

    private static FileNameColorAssignment[] ToArray(Dictionary<string, (string Path, int Color)> kept)
    {
        var items = new FileNameColorAssignment[kept.Count];
        var i = 0;
        foreach (var pair in kept)
        {
            items[i++] = new FileNameColorAssignment { Path = pair.Value.Path, ColorIndex = pair.Value.Color };
        }

        return items;
    }

    private static FileNameColorAssignment[] Normalize(IReadOnlyList<FileNameColorAssignment>? current) =>
        FromMap(ToMap(current));

    private static bool TryKey(string? path, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            key = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            key = path.Trim();
        }

        return key.Length > 0;
    }

    private static bool PathExists(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>表示する色。手動の指定がファイル名ルールに勝つ。</summary>
internal static class FileNameColorResolve
{
    public static bool TryResolve(string? fileName, string? path, out int colorIndex, out string pattern) =>
        TryResolve(
            fileName,
            path,
            AppStorage.Settings.FileNameColorRules,
            AppStorage.Settings.FileNameColorAssignments,
            out colorIndex,
            out pattern);

    public static bool TryResolve(
        string? fileName,
        string? path,
        IReadOnlyList<FileNameColorRule>? rules,
        IReadOnlyList<FileNameColorAssignment>? manual,
        out int colorIndex,
        out string pattern)
    {
        if (FileNameColorAssignments.TryGet(manual, path, out colorIndex))
        {
            pattern = FileNameColorPalette.HexLabel(colorIndex);
            return pattern.Length > 0;
        }

        return FileNameColorRules.TryMatch(fileName, rules, out colorIndex, out pattern);
    }
}

/// <summary>アプリ内の移動・コピー・削除を、手動色の保存へ反映する。</summary>
internal static class FileNameColorAssignmentSync
{
    public static bool Move(string source, string destination) =>
        Apply(FileNameColorAssignments.MoveTree(AppStorage.Settings.FileNameColorAssignments, source, destination));

    public static bool Copy(string source, string destination) =>
        Apply(FileNameColorAssignments.CopyTree(AppStorage.Settings.FileNameColorAssignments, source, destination));

    public static bool Forget(string path) =>
        Apply(FileNameColorAssignments.ForgetTree(AppStorage.Settings.FileNameColorAssignments, path));

    public static bool Forget(IEnumerable<string> paths) =>
        Apply(FileNameColorAssignments.ForgetTree(AppStorage.Settings.FileNameColorAssignments, paths));

    private static bool Apply(FileNameColorAssignment[]? next)
    {
        if (next is null)
        {
            return false;
        }

        AppStorage.Settings.FileNameColorAssignments = next;
        AppStorage.Save();
        return true;
    }
}
