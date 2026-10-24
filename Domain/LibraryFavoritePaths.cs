using System.IO;

namespace MgaSonicAnvil.Domain;

/// <summary>F10 お気に入り。フォルダのパス登録のみ。ディスク上の実体は消さない。</summary>
internal static class LibraryFavoritePaths
{
    public static string[] Resolve(string[]? saved)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (saved is not { Length: > 0 })
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
                var full = Path.GetFullPath(raw.Trim());
                // 存在するファイル単体は登録しない（フォルダのみ）。
                if (File.Exists(full) && !Directory.Exists(full))
                {
                    continue;
                }

                // 無いパスも登録として残す（UI で赤字表示）。
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

        return [.. result];
    }

    public static string[] Serialize(IEnumerable<string> paths) => Resolve(paths.ToArray());

    public static string[] Merge(IEnumerable<string> current, IEnumerable<string> add) =>
        Serialize(current.Concat(add));

    public static string DisplayName(string path) => LibraryExplorerPaths.DisplayName(path);

    /// <summary>登録フォルダが無いか。</summary>
    public static bool IsMissing(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        try
        {
            var full = Path.GetFullPath(path.Trim());
            return !Directory.Exists(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return true;
        }
    }
}
