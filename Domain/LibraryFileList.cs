using System.ComponentModel;

namespace MgaSonicAnvil.Domain;

internal enum LibraryFileColumn
{
    Name,
    ParentFolder,
    Waveform,
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
    Date,
    Folder,
    Jacket,
    VideoSize,
    FrameRate,
    VideoCodec,
    AudioCodec,
    Loudness,
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
internal sealed class LibraryFileRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _cutPending;

    /// <summary>切り取り中。名前を暗い色で出す。</summary>
    public bool CutPending
    {
        get => _cutPending;
        set
        {
            if (_cutPending == value)
            {
                return;
            }

            _cutPending = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CutPending)));
        }
    }

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

    /// <summary>既定 Wave フォーマットのサンプルレートと違う（プレイヤーでオレンジ表示）。</summary>
    public bool SampleRateNonDefault { get; init; }

    /// <summary>既定 Wave フォーマットのビット深度と違う（プレイヤーでオレンジ表示）。</summary>
    public bool BitDepthNonDefault { get; init; }

    /// <summary>既定 Wave フォーマットのチャンネル数と違う（プレイヤーでオレンジ表示）。</summary>
    public bool ChannelsNonDefault { get; init; }

    public int BitRateKbps { get; init; }

    public string BitRateText { get; init; } = string.Empty;

    public long FileBytes { get; init; }

    public string SizeText { get; init; } = string.Empty;

    public DateTime FileDate { get; init; }

    public string DateText { get; init; } = string.Empty;

    public string Folder { get; init; } = string.Empty;

    public string ParentFolder { get; init; } = string.Empty;

    public bool HasArtwork { get; init; }

    public string JacketText { get; init; } = string.Empty;

    public int VideoWidth { get; init; }

    public int VideoHeight { get; init; }

    public string VideoSizeText { get; init; } = string.Empty;

    public double FrameRate { get; init; }

    public string FrameRateText { get; init; } = string.Empty;

    public string VideoCodec { get; init; } = string.Empty;

    public string AudioCodec { get; init; } = string.Empty;

    /// <summary>Integrated LUFS。未計測は null。</summary>
    public float? IntegratedLufs { get; init; }

    public string LoudnessText { get; init; } = string.Empty;

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
            LibraryFileGroup.Kind => Blank(row.Kind),
            LibraryFileGroup.SampleRate => Blank(row.SampleRateText),
            LibraryFileGroup.BitDepth => Blank(row.BitDepthText),
            LibraryFileGroup.Channels => Blank(row.ChannelsText),
            LibraryFileGroup.Folder => row.ParentFolder.Length == 0
                ? UiStrings.LibraryGroupUntitled
                : row.ParentFolder,
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
            return LibraryFileGroup.Folder;
        }

        return Enum.TryParse(value, ignoreCase: true, out LibraryFileGroup group)
            ? group
            : LibraryFileGroup.Folder;
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
        && left.SampleRateNonDefault == right.SampleRateNonDefault
        && left.BitDepthNonDefault == right.BitDepthNonDefault
        && left.ChannelsNonDefault == right.ChannelsNonDefault
        && left.BitRateText == right.BitRateText
        && left.SizeText == right.SizeText
        && left.DateText == right.DateText
        && left.Folder == right.Folder
        && left.ParentFolder == right.ParentFolder
        && left.JacketText == right.JacketText
        && left.VideoSizeText == right.VideoSizeText
        && left.FrameRateText == right.FrameRateText
        && left.VideoCodec == right.VideoCodec
        && left.AudioCodec == right.AudioCodec
        && left.LoudnessText == right.LoudnessText
        && left.GroupKey == right.GroupKey;

    public static string CellText(LibraryFileRow row, LibraryFileColumn column) =>
        column switch
        {
            LibraryFileColumn.Title => row.Title,
            LibraryFileColumn.Artist => row.Artist,
            LibraryFileColumn.AlbumArtist => row.AlbumArtist,
            LibraryFileColumn.Album => row.Album,
            LibraryFileColumn.Track => row.Track,
            LibraryFileColumn.Disc => row.Disc,
            LibraryFileColumn.Year => row.Year,
            LibraryFileColumn.Genre => row.Genre,
            LibraryFileColumn.Composer => row.Composer,
            LibraryFileColumn.Comment => row.Comment,
            LibraryFileColumn.Duration => row.DurationText,
            LibraryFileColumn.Kind => row.Kind,
            LibraryFileColumn.SampleRate => row.SampleRateText,
            LibraryFileColumn.BitDepth => row.BitDepthText,
            LibraryFileColumn.Channels => row.ChannelsText,
            LibraryFileColumn.BitRate => row.BitRateText,
            LibraryFileColumn.Size => row.SizeText,
            LibraryFileColumn.Date => row.DateText,
            LibraryFileColumn.Folder => row.Folder,
            LibraryFileColumn.ParentFolder => row.ParentFolder,
            LibraryFileColumn.Waveform => string.Empty,
            LibraryFileColumn.Jacket => row.JacketText,
            LibraryFileColumn.VideoSize => row.VideoSizeText,
            LibraryFileColumn.FrameRate => row.FrameRateText,
            LibraryFileColumn.VideoCodec => row.VideoCodec,
            LibraryFileColumn.AudioCodec => row.AudioCodec,
            LibraryFileColumn.Loudness => row.LoudnessText,
            _ => row.Name,
        };

    /// <summary>フルパスから親フォルダ名だけ取る。ルートはドライブ文字など。</summary>
    public static string ParentFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var folder = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder))
            {
                return string.Empty;
            }

            var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name)
                ? folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : name;
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            return string.Empty;
        }
    }

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
            LibraryFileColumn.Date => left.FileDate.CompareTo(right.FileDate),
            LibraryFileColumn.Folder => string.Compare(left.Folder, right.Folder, StringComparison.CurrentCultureIgnoreCase),
            LibraryFileColumn.ParentFolder => string.Compare(
                left.ParentFolder,
                right.ParentFolder,
                StringComparison.CurrentCultureIgnoreCase),
            LibraryFileColumn.Waveform => 0,
            LibraryFileColumn.Jacket => left.HasArtwork.CompareTo(right.HasArtwork),
            LibraryFileColumn.VideoSize => CompareVideoSize(left, right),
            LibraryFileColumn.FrameRate => left.FrameRate.CompareTo(right.FrameRate),
            LibraryFileColumn.VideoCodec => string.Compare(
                left.VideoCodec,
                right.VideoCodec,
                StringComparison.OrdinalIgnoreCase),
            LibraryFileColumn.AudioCodec => string.Compare(
                left.AudioCodec,
                right.AudioCodec,
                StringComparison.OrdinalIgnoreCase),
            LibraryFileColumn.Loudness => CompareNullableFloat(left.IntegratedLufs, right.IntegratedLufs),
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

    private static int CompareVideoSize(LibraryFileRow left, LibraryFileRow right)
    {
        var pixels = ((long)left.VideoWidth * left.VideoHeight)
            .CompareTo((long)right.VideoWidth * right.VideoHeight);
        if (pixels != 0)
        {
            return pixels;
        }

        var width = left.VideoWidth.CompareTo(right.VideoWidth);
        return width != 0 ? width : left.VideoHeight.CompareTo(right.VideoHeight);
    }

    private static int CompareNullableFloat(float? left, float? right)
    {
        if (left is null && right is null)
        {
            return 0;
        }

        if (left is null)
        {
            return -1;
        }

        if (right is null)
        {
            return 1;
        }

        return left.Value.CompareTo(right.Value);
    }

    private static string Blank(string value) =>
        value.Length == 0 ? UiStrings.LibraryGroupBlank : value;
}
