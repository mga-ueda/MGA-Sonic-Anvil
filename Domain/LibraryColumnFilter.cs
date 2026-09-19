namespace MgaSonicAnvil.Domain;

/// <summary>プレイリストの列。設定の読み書き。</summary>
internal static class LibraryColumnFilter
{
    public static readonly LibraryFileColumn[] All =
    [
        LibraryFileColumn.Name,
        LibraryFileColumn.Title,
        LibraryFileColumn.Artist,
        LibraryFileColumn.Album,
        LibraryFileColumn.Track,
        LibraryFileColumn.Disc,
        LibraryFileColumn.Year,
        LibraryFileColumn.Genre,
        LibraryFileColumn.Composer,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Comment,
        LibraryFileColumn.AlbumArtist,
        LibraryFileColumn.Kind,
        LibraryFileColumn.SampleRate,
        LibraryFileColumn.BitDepth,
        LibraryFileColumn.Channels,
        LibraryFileColumn.BitRate,
        LibraryFileColumn.Size,
        LibraryFileColumn.Folder,
        LibraryFileColumn.Jacket,
    ];

    public static readonly LibraryFileColumn[] Defaults =
    [
        LibraryFileColumn.Name,
        LibraryFileColumn.Title,
        LibraryFileColumn.Artist,
        LibraryFileColumn.Album,
        LibraryFileColumn.Track,
        LibraryFileColumn.Disc,
        LibraryFileColumn.Year,
        LibraryFileColumn.Genre,
        LibraryFileColumn.Composer,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Comment,
    ];

    public static bool IsLocked(LibraryFileColumn column) => column == LibraryFileColumn.Name;

    public static HashSet<LibraryFileColumn> Resolve(string[]? stored)
    {
        if (stored is not { Length: > 0 })
        {
            return [.. Defaults];
        }

        var set = new HashSet<LibraryFileColumn>();
        foreach (var name in stored)
        {
            if (Enum.TryParse(name, ignoreCase: true, out LibraryFileColumn column)
                && Array.IndexOf(All, column) >= 0)
            {
                set.Add(column);
            }
        }

        if (set.Count == 0)
        {
            return [.. Defaults];
        }

        set.Add(LibraryFileColumn.Name);
        return set;
    }

    public static string[] Serialize(IEnumerable<LibraryFileColumn> columns)
    {
        var set = new HashSet<LibraryFileColumn>();
        foreach (var column in columns)
        {
            if (Array.IndexOf(All, column) >= 0)
            {
                set.Add(column);
            }
        }

        set.Add(LibraryFileColumn.Name);
        var names = new List<string>(All.Length);
        foreach (var column in All)
        {
            if (set.Contains(column))
            {
                names.Add(column.ToString());
            }
        }

        return [.. names];
    }
}
