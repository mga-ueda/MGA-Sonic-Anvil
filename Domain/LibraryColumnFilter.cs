namespace MgaSonicAnvil.Domain;

/// <summary>プレイリストの列。設定の読み書き。並びは保存順。</summary>
internal static class LibraryColumnFilter
{
    public static readonly LibraryFileColumn[] All =
    [
        LibraryFileColumn.Name,
        LibraryFileColumn.Title,
        LibraryFileColumn.Album,
        LibraryFileColumn.Artist,
        LibraryFileColumn.Composer,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Track,
        LibraryFileColumn.Disc,
        LibraryFileColumn.Year,
        LibraryFileColumn.Genre,
        LibraryFileColumn.Date,
        LibraryFileColumn.Comment,
        LibraryFileColumn.ParentFolder,
        LibraryFileColumn.Waveform,
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
        LibraryFileColumn.Album,
        LibraryFileColumn.Artist,
        LibraryFileColumn.Composer,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Track,
        LibraryFileColumn.Disc,
        LibraryFileColumn.Year,
        LibraryFileColumn.Genre,
        LibraryFileColumn.Date,
        LibraryFileColumn.Comment,
        LibraryFileColumn.ParentFolder,
        LibraryFileColumn.Waveform,
    ];

    public static bool IsLocked(LibraryFileColumn column) => column == LibraryFileColumn.Name;

    /// <summary>
    /// プレイリストに値が1件でもある列。空リストは null（設定どおり全部出す）。
    /// ジャケットは値が無くても、埋め込み可能な形式（MP3 / M4A）が1件あれば出す。
    /// Wave / AIFF だけでは隠し、混在したら出す。
    /// 波形は曲が1件でもあれば出す（中身は後から埋める）。
    /// MP3 のみのとき、設定に応じて親フォルダ／波形を used から外す。
    /// </summary>
    public static HashSet<LibraryFileColumn>? UsedColumns(
        IReadOnlyList<LibraryFileRow> rows,
        bool hideParentFolderForMp3Only = false,
        bool hideWaveformForMp3Only = false)
    {
        if (rows.Count == 0)
        {
            return null;
        }

        var used = new HashSet<LibraryFileColumn>
        {
            LibraryFileColumn.Name,
            LibraryFileColumn.Waveform,
        };
        var jacketEligible = false;
        foreach (var row in rows)
        {
            if (!jacketEligible && JacketEligibleKind(row.Kind))
            {
                jacketEligible = true;
                used.Add(LibraryFileColumn.Jacket);
            }

            foreach (var column in All)
            {
                if (column is LibraryFileColumn.Jacket or LibraryFileColumn.Waveform
                    || used.Contains(column))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(LibraryFileList.CellText(row, column)))
                {
                    used.Add(column);
                }
            }

            if (used.Count >= All.Length)
            {
                break;
            }
        }

        if (IsMp3Only(rows))
        {
            if (hideParentFolderForMp3Only)
            {
                used.Remove(LibraryFileColumn.ParentFolder);
            }

            if (hideWaveformForMp3Only)
            {
                used.Remove(LibraryFileColumn.Waveform);
            }
        }

        return used;
    }

    /// <summary>MP3 だけなら true。空リストは false。</summary>
    public static bool IsMp3Only(IReadOnlyList<LibraryFileRow> rows)
    {
        if (rows.Count == 0)
        {
            return false;
        }

        foreach (var row in rows)
        {
            if (!string.Equals(row.Kind, "MP3", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>ジャケット列を出し得る形式。Wave / AIFF は埋め込みできない。</summary>
    public static bool JacketEligibleKind(string kind) =>
        kind is "MP3" or "M4A";

    public static bool IsEffectivelyVisible(
        LibraryFileColumn column,
        IReadOnlyCollection<LibraryFileColumn> enabled,
        HashSet<LibraryFileColumn>? used)
    {
        if (IsLocked(column))
        {
            return true;
        }

        if (!enabled.Contains(column))
        {
            return false;
        }

        return used is null || used.Contains(column);
    }

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
            seen.Add(LibraryFileColumn.Name);
        }

        if (IsPreviousDefaultColumnOrder(result))
        {
            return [.. Defaults];
        }

        if (!seen.Contains(LibraryFileColumn.Date) && IsLegacyDefaultSet(seen))
        {
            InsertDateAtDefaultPlace(result);
            seen.Add(LibraryFileColumn.Date);
        }

        if (!seen.Contains(LibraryFileColumn.ParentFolder) && IsLegacyDefaultWithoutParentFolder(seen))
        {
            InsertParentFolderAtDefaultPlace(result);
            seen.Add(LibraryFileColumn.ParentFolder);
        }

        if (!seen.Contains(LibraryFileColumn.Waveform) && IsLegacyDefaultWithoutWaveform(seen))
        {
            InsertWaveformAtDefaultPlace(result);
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

    private static readonly LibraryFileColumn[][] PreviousDefaultOrders =
    [
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.Album,
            LibraryFileColumn.Artist,
            LibraryFileColumn.Composer,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Track,
            LibraryFileColumn.Disc,
            LibraryFileColumn.Year,
            LibraryFileColumn.Genre,
            LibraryFileColumn.Comment,
            LibraryFileColumn.Date,
        ],
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.Album,
            LibraryFileColumn.Artist,
            LibraryFileColumn.Composer,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Track,
            LibraryFileColumn.Disc,
            LibraryFileColumn.Year,
            LibraryFileColumn.Date,
            LibraryFileColumn.Genre,
            LibraryFileColumn.Comment,
        ],
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.Album,
            LibraryFileColumn.Artist,
            LibraryFileColumn.Composer,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Track,
            LibraryFileColumn.Disc,
            LibraryFileColumn.Year,
            LibraryFileColumn.Genre,
            LibraryFileColumn.Date,
            LibraryFileColumn.Comment,
        ],
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.ParentFolder,
            LibraryFileColumn.Title,
            LibraryFileColumn.Album,
            LibraryFileColumn.Artist,
            LibraryFileColumn.Composer,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Track,
            LibraryFileColumn.Disc,
            LibraryFileColumn.Year,
            LibraryFileColumn.Genre,
            LibraryFileColumn.Date,
            LibraryFileColumn.Comment,
        ],
        [
            LibraryFileColumn.Name,
            LibraryFileColumn.Title,
            LibraryFileColumn.Album,
            LibraryFileColumn.Artist,
            LibraryFileColumn.Composer,
            LibraryFileColumn.Duration,
            LibraryFileColumn.Track,
            LibraryFileColumn.Disc,
            LibraryFileColumn.Year,
            LibraryFileColumn.Genre,
            LibraryFileColumn.Date,
            LibraryFileColumn.Comment,
            LibraryFileColumn.ParentFolder,
        ],
    ];

    /// <summary>親フォルダ追加前の既定セット（日付あり）。</summary>
    private static bool IsLegacyDefaultWithoutParentFolder(HashSet<LibraryFileColumn> seen)
    {
        if (seen.Count != Defaults.Length - 2)
        {
            return false;
        }

        foreach (var column in Defaults)
        {
            if (column is LibraryFileColumn.ParentFolder or LibraryFileColumn.Waveform)
            {
                continue;
            }

            if (!seen.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>波形追加前の既定セット（親フォルダあり）。</summary>
    private static bool IsLegacyDefaultWithoutWaveform(HashSet<LibraryFileColumn> seen)
    {
        if (seen.Count != Defaults.Length - 1)
        {
            return false;
        }

        foreach (var column in Defaults)
        {
            if (column != LibraryFileColumn.Waveform && !seen.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLegacyDefaultSet(HashSet<LibraryFileColumn> seen)
    {
        // 日付・親フォルダ・波形追加前の既定。
        var expected = Defaults.Length - 3;
        if (seen.Count != expected)
        {
            return false;
        }

        foreach (var column in Defaults)
        {
            if (column is LibraryFileColumn.Date
                or LibraryFileColumn.ParentFolder
                or LibraryFileColumn.Waveform)
            {
                continue;
            }

            if (!seen.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPreviousDefaultColumnOrder(List<LibraryFileColumn> stored)
    {
        foreach (var previous in PreviousDefaultOrders)
        {
            if (stored.Count != previous.Length)
            {
                continue;
            }

            var match = true;
            for (var i = 0; i < previous.Length; i++)
            {
                if (stored[i] != previous[i])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }

    private static void InsertParentFolderAtDefaultPlace(List<LibraryFileColumn> result) =>
        result.Add(LibraryFileColumn.ParentFolder);

    private static void InsertWaveformAtDefaultPlace(List<LibraryFileColumn> result)
    {
        var parent = result.IndexOf(LibraryFileColumn.ParentFolder);
        if (parent >= 0)
        {
            result.Insert(parent + 1, LibraryFileColumn.Waveform);
            return;
        }

        result.Add(LibraryFileColumn.Waveform);
    }

    private static void InsertDateAtDefaultPlace(List<LibraryFileColumn> result)
    {
        var genre = result.IndexOf(LibraryFileColumn.Genre);
        if (genre >= 0)
        {
            result.Insert(genre + 1, LibraryFileColumn.Date);
            return;
        }

        var comment = result.IndexOf(LibraryFileColumn.Comment);
        if (comment >= 0)
        {
            result.Insert(comment, LibraryFileColumn.Date);
            return;
        }

        result.Add(LibraryFileColumn.Date);
    }
}
