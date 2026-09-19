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

    public static string[] SerializeRoots(IEnumerable<string> roots) => ResolveRoots(roots.ToArray());

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
