namespace MgaSonicAnvil.Domain;

internal enum LibraryFileColumn
{
    Name,
    Title,
    Artist,
    AlbumArtist,
    Album,
    Track,
    Disc,
    Year,
    Genre,
    Composer,
    Comment,
    Duration,
    Kind,
    SampleRate,
    BitDepth,
    Channels,
    BitRate,
    Size,
    Folder,
    Jacket,
}

internal enum LibraryFileGroup
{
    None,
    Title,
    Artist,
    Album,
    Genre,
    Year,
    Kind,
    SampleRate,
    BitDepth,
    Channels,
    Folder,
}

internal enum LibrarySortDirection
{
    Ascending,
    Descending,
}

/// <summary>F10 ファイルリストの 1 行。並べ替えとグルーピング用。</summary>
internal sealed class LibraryFileRow
{
    public object? Tag { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Artist { get; init; } = string.Empty;

    public string AlbumArtist { get; init; } = string.Empty;

    public string Album { get; init; } = string.Empty;

    public string Track { get; init; } = string.Empty;

    public int TrackNumber { get; init; }

    public string Disc { get; init; } = string.Empty;

    public int DiscNumber { get; init; }

    public string Year { get; init; } = string.Empty;

    public int YearNumber { get; init; }

    public string Genre { get; init; } = string.Empty;

    public string Composer { get; init; } = string.Empty;

    public string Comment { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public double DurationSeconds { get; init; }

    public string DurationText { get; init; } = string.Empty;

    public int SampleRate { get; init; }

    public string SampleRateText { get; init; } = string.Empty;

    public int BitDepth { get; init; }

    public string BitDepthText { get; init; } = string.Empty;

    public int Channels { get; init; }

    public string ChannelsText { get; init; } = string.Empty;

    public int BitRateKbps { get; init; }

    public string BitRateText { get; init; } = string.Empty;

    public long FileBytes { get; init; }

    public string SizeText { get; init; } = string.Empty;

    public string Folder { get; init; } = string.Empty;

    public bool HasArtwork { get; init; }

    public string JacketText { get; init; } = string.Empty;

    public string GroupKey { get; set; } = string.Empty;
}

/// <summary>読み込み済みファイルの列ソートとグルーピング。</summary>
internal static class LibraryFileList
{
    public static string GroupLabel(LibraryFileRow row, LibraryFileGroup group) =>
        group switch
        {
            LibraryFileGroup.Title => Blank(row.Title),
            LibraryFileGroup.Artist => Blank(row.Artist),
            LibraryFileGroup.Album => Blank(row.Album),
            LibraryFileGroup.Genre => Blank(row.Genre),
            LibraryFileGroup.Year => Blank(row.Year),
            LibraryFileGroup.Kind => row.Kind,
            LibraryFileGroup.SampleRate => row.SampleRateText,
            LibraryFileGroup.BitDepth => row.BitDepthText,
            LibraryFileGroup.Channels => row.ChannelsText,
            LibraryFileGroup.Folder => row.Folder.Length == 0
                ? UiStrings.LibraryGroupUntitled
                : row.Folder,
            _ => string.Empty,
        };

    public static IReadOnlyList<LibraryFileRow> Sort(
        IReadOnlyList<LibraryFileRow> rows,
        LibraryFileColumn column,
        LibrarySortDirection direction)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var copy = new LibraryFileRow[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            copy[i] = rows[i];
        }

        Array.Sort(copy, (left, right) => Compare(left, right, column, direction));
        return copy;
    }

    public static void ApplyGroupKeys(IReadOnlyList<LibraryFileRow> rows, LibraryFileGroup group)
    {
        foreach (var row in rows)
        {
            row.GroupKey = GroupLabel(row, group);
        }
    }

    public static LibraryFileGroup ParseGroup(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return LibraryFileGroup.Album;
        }

        return Enum.TryParse(value, ignoreCase: true, out LibraryFileGroup group)
            ? group
            : LibraryFileGroup.Album;
    }

    public static string SerializeGroup(LibraryFileGroup group) => group.ToString();

    public static bool SameContent(LibraryFileRow left, LibraryFileRow right) =>
        left.Name == right.Name
        && left.Title == right.Title
        && left.Artist == right.Artist
        && left.AlbumArtist == right.AlbumArtist
        && left.Album == right.Album
        && left.Track == right.Track
        && left.Disc == right.Disc
        && left.Year == right.Year
        && left.Genre == right.Genre
        && left.Composer == right.Composer
        && left.Comment == right.Comment
        && left.Kind == right.Kind
        && left.DurationText == right.DurationText
        && left.SampleRateText == right.SampleRateText
        && left.BitDepthText == right.BitDepthText
        && left.ChannelsText == right.ChannelsText
        && left.BitRateText == right.BitRateText
        && left.SizeText == right.SizeText
        && left.Folder == right.Folder
        && left.JacketText == right.JacketText
        && left.GroupKey == right.GroupKey;

    public static int Compare(
        LibraryFileRow left,
        LibraryFileRow right,
        LibraryFileColumn column,
        LibrarySortDirection direction)
    {
        var sign = direction == LibrarySortDirection.Descending ? -1 : 1;
        var compared = column switch
        {
            LibraryFileColumn.Title => CompareText(left.Title, right.Title),
            LibraryFileColumn.Artist => CompareText(left.Artist, right.Artist),
            LibraryFileColumn.AlbumArtist => CompareText(left.AlbumArtist, right.AlbumArtist),
            LibraryFileColumn.Album => CompareText(left.Album, right.Album),
            LibraryFileColumn.Track => left.TrackNumber.CompareTo(right.TrackNumber),
            LibraryFileColumn.Disc => left.DiscNumber.CompareTo(right.DiscNumber),
            LibraryFileColumn.Year => left.YearNumber.CompareTo(right.YearNumber),
            LibraryFileColumn.Genre => CompareText(left.Genre, right.Genre),
            LibraryFileColumn.Composer => CompareText(left.Composer, right.Composer),
            LibraryFileColumn.Comment => CompareText(left.Comment, right.Comment),
            LibraryFileColumn.Kind => string.Compare(left.Kind, right.Kind, StringComparison.OrdinalIgnoreCase),
            LibraryFileColumn.Duration => left.DurationSeconds.CompareTo(right.DurationSeconds),
            LibraryFileColumn.SampleRate => left.SampleRate.CompareTo(right.SampleRate),
            LibraryFileColumn.BitDepth => left.BitDepth.CompareTo(right.BitDepth),
            LibraryFileColumn.Channels => left.Channels.CompareTo(right.Channels),
            LibraryFileColumn.BitRate => left.BitRateKbps.CompareTo(right.BitRateKbps),
            LibraryFileColumn.Size => left.FileBytes.CompareTo(right.FileBytes),
            LibraryFileColumn.Folder => string.Compare(left.Folder, right.Folder, StringComparison.CurrentCultureIgnoreCase),
            LibraryFileColumn.Jacket => left.HasArtwork.CompareTo(right.HasArtwork),
            _ => string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase),
        };
        if (compared != 0)
        {
            return compared * sign;
        }

        var name = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
        return name == 0 ? 0 : name * sign;
    }

    private static int CompareText(string left, string right) =>
        string.Compare(left, right, StringComparison.CurrentCultureIgnoreCase);

    private static string Blank(string value) =>
        value.Length == 0 ? UiStrings.LibraryGroupBlank : value;
}
