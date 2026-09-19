namespace MgaSonicAnvil.Domain;

/// <summary>プレイリストの列。設定の読み書き。並びは保存順。</summary>
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

    public static LibraryFileColumn[] Resolve(string[]? stored)
    {
        if (stored is not { Length: > 0 })
        {
            return [.. Defaults];
        }

        var result = new List<LibraryFileColumn>(stored.Length + 1);
        var seen = new HashSet<LibraryFileColumn>();
        foreach (var name in stored)
        {
            if (Enum.TryParse(name, ignoreCase: true, out LibraryFileColumn column)
                && Array.IndexOf(All, column) >= 0
                && seen.Add(column))
            {
                result.Add(column);
            }
        }

        if (result.Count == 0)
        {
            return [.. Defaults];
        }

        if (!seen.Contains(LibraryFileColumn.Name))
        {
            result.Insert(0, LibraryFileColumn.Name);
        }

        return [.. result];
    }

    public static string[] Serialize(IEnumerable<LibraryFileColumn> columns)
    {
        var names = new List<string>(All.Length);
        var seen = new HashSet<LibraryFileColumn>();
        foreach (var column in columns)
        {
            if (Array.IndexOf(All, column) >= 0 && seen.Add(column))
            {
                names.Add(column.ToString());
            }
        }

        if (!seen.Contains(LibraryFileColumn.Name))
        {
            names.Insert(0, LibraryFileColumn.Name.ToString());
        }

        return names.Count == 0 ? Serialize(Defaults) : [.. names];
    }

    /// <summary>チェックの増減は既存の並びを保ち、新規は既定順の末尾へ。</summary>
    public static LibraryFileColumn[] Merge(
        IEnumerable<LibraryFileColumn>? previous,
        IEnumerable<LibraryFileColumn> selected)
    {
        var want = new HashSet<LibraryFileColumn> { LibraryFileColumn.Name };
        foreach (var column in selected)
        {
            if (Array.IndexOf(All, column) >= 0)
            {
                want.Add(column);
            }
        }

        var result = new List<LibraryFileColumn>(want.Count);
        var seen = new HashSet<LibraryFileColumn>();
        void Add(LibraryFileColumn column)
        {
            if (want.Contains(column) && seen.Add(column))
            {
                result.Add(column);
            }
        }

        if (previous is not null)
        {
            foreach (var column in previous)
            {
                Add(column);
            }
        }

        foreach (var column in All)
        {
            Add(column);
        }

        return [.. result];
    }
}
