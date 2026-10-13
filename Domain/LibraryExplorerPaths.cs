using System.IO;

namespace MgaSonicAnvil.Domain;

/// <summary>F10 左のフォルダツリー。ルート複数と、最後に選んだフォルダ。</summary>
internal static class LibraryExplorerPaths
{
    /// <summary>設定が空のときのルート。マイミュージック。</summary>
    public static string DefaultRootFolder()
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (!string.IsNullOrWhiteSpace(music) && Directory.Exists(music))
        {
            return Path.GetFullPath(music);
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
        {
            return Path.GetFullPath(documents);
        }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(profile) ? string.Empty : Path.GetFullPath(profile);
    }

    /// <summary>最後に開いたフォルダが空／欠損のときの退避先（マイミュージック → ドキュメント → プロファイル）。</summary>
    public static string DefaultFolder() => DefaultRootFolder();

    public static string Resolve(string? saved)
    {
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                var full = Path.GetFullPath(saved.Trim());
                if (Directory.Exists(full))
                {
                    return full;
                }
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return DefaultRootFolder();
    }

    public static string[] ResolveRoots(string[]? saved)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (saved is { Length: > 0 })
        {
            foreach (var raw in saved)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                try
                {
                    var full = Path.GetFullPath(raw.Trim())
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    // 無いフォルダも登録として残す（UI で赤字表示）。空の登録だけ既定ルートへ。
                    if (!seen.Add(full))
                    {
                        continue;
                    }

                    result.Add(full);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                {
                }
            }
        }

        if (result.Count == 0)
        {
            var fallback = DefaultRootFolder();
            if (!string.IsNullOrEmpty(fallback))
            {
                result.Add(fallback.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
        }

        return [.. result];
    }

    /// <summary>登録パスがディスク上に無いか。</summary>
    public static bool IsMissing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return !Directory.Exists(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }

    public static string[] SerializeRoots(IEnumerable<string> roots) => ResolveRoots(roots.ToArray());

    /// <summary>ドロップなどから存在するフォルダだけを取る（ファイルは無視）。</summary>
    public static string[] ExistingFolders(IEnumerable<string>? paths)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (paths is null)
        {
            return [];
        }

        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(raw.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(full) || !seen.Add(full))
                {
                    continue;
                }

                result.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return [.. result];
    }

    /// <summary>展開していたフォルダ。無いパスは落とす。並びはフルパス。</summary>
    public static string[] ResolveExpanded(IEnumerable<string>? saved)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (saved is null)
        {
            return [];
        }

        foreach (var raw in saved)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            try
            {
                var full = Path.GetFullPath(raw.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!Directory.Exists(full) || !seen.Add(full))
                {
                    continue;
                }

                result.Add(full);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        return [.. result];
    }

    public static string[] SerializeExpanded(IEnumerable<string> paths) => ResolveExpanded(paths);

    /// <summary>選択を1段動かす。並びの相対順は保つ。端では動かない。</summary>
    public static void MoveSelected<T>(IList<T> items, IReadOnlySet<T> selected, int direction)
        where T : notnull
    {
        if (items.Count < 2 || selected.Count == 0 || direction == 0)
        {
            return;
        }

        if (direction < 0)
        {
            for (var i = 1; i < items.Count; i++)
            {
                if (selected.Contains(items[i]) && !selected.Contains(items[i - 1]))
                {
                    (items[i - 1], items[i]) = (items[i], items[i - 1]);
                }
            }

            return;
        }

        for (var i = items.Count - 2; i >= 0; i--)
        {
            if (selected.Contains(items[i]) && !selected.Contains(items[i + 1]))
            {
                (items[i], items[i + 1]) = (items[i + 1], items[i]);
            }
        }
    }

    /// <summary>
    /// 選択を insertIndex（移動前の「この位置の前へ」）へまとめて移す。相対順は保つ。
    /// </summary>
    public static void MoveSelectedToIndex<T>(IList<T> items, IReadOnlySet<T> selected, int insertIndex)
        where T : notnull
    {
        if (items.Count < 2 || selected.Count == 0)
        {
            return;
        }

        var moved = new List<T>();
        for (var i = 0; i < items.Count; i++)
        {
            if (selected.Contains(items[i]))
            {
                moved.Add(items[i]);
            }
        }

        if (moved.Count == 0 || moved.Count == items.Count)
        {
            return;
        }

        var removedBefore = 0;
        var limit = Math.Clamp(insertIndex, 0, items.Count);
        for (var i = 0; i < limit; i++)
        {
            if (selected.Contains(items[i]))
            {
                removedBefore++;
            }
        }

        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (selected.Contains(items[i]))
            {
                items.RemoveAt(i);
            }
        }

        var to = Math.Clamp(limit - removedBefore, 0, items.Count);
        for (var i = 0; i < moved.Count; i++)
        {
            items.Insert(to + i, moved[i]);
        }
    }

    public static string DisplayName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(full);
            return string.IsNullOrEmpty(name) ? full : name;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
