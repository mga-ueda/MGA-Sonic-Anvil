using System.IO;

namespace MgaSonicAnvil.Domain;

/// <summary>F10 お気に入り。パス登録のみ。ディスク上のファイルは消さない。</summary>
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
                if (!seen.Add(full))
                {
                    continue;
                }

                if (File.Exists(full) || Directory.Exists(full))
                {
                    result.Add(full);
                }
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
}
