namespace MgaSonicAnvil.Domain;

/// <summary>プレイリスト列プリセットの種別。</summary>
internal enum LibraryPlaylistColumnKind
{
    Wave,
    Mp3,
    Mixed,
}

/// <summary>Wave / MP3 / 混在それぞれの表示列。</summary>
internal readonly record struct LibraryColumnPresets(
    IReadOnlyList<LibraryFileColumn> Wave,
    IReadOnlyList<LibraryFileColumn> Mp3,
    IReadOnlyList<LibraryFileColumn> Mixed,
    bool PersistMixed = false);

/// <summary>プレイリストの列。設定の読み書き。並びは保存順。</summary>
internal static class LibraryColumnFilter
{
    public static readonly LibraryFileColumn[] All =
    [
        LibraryFileColumn.Name,
        LibraryFileColumn.SampleRate,
        LibraryFileColumn.BitDepth,
        LibraryFileColumn.Channels,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Date,
        LibraryFileColumn.ParentFolder,
        LibraryFileColumn.Waveform,
        LibraryFileColumn.Title,
        LibraryFileColumn.Album,
        LibraryFileColumn.Artist,
        LibraryFileColumn.Composer,
        LibraryFileColumn.Track,
        LibraryFileColumn.Disc,
        LibraryFileColumn.Year,
        LibraryFileColumn.Genre,
        LibraryFileColumn.Comment,
        LibraryFileColumn.AlbumArtist,
        LibraryFileColumn.Kind,
        LibraryFileColumn.BitRate,
        LibraryFileColumn.Size,
        LibraryFileColumn.Folder,
        LibraryFileColumn.Jacket,
    ];

    /// <summary>WAVE / AIFF 向け既定。</summary>
    public static readonly LibraryFileColumn[] WaveDefaults =
    [
        LibraryFileColumn.Name,
        LibraryFileColumn.SampleRate,
        LibraryFileColumn.BitDepth,
        LibraryFileColumn.Channels,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Date,
        LibraryFileColumn.ParentFolder,
        LibraryFileColumn.Waveform,
    ];

    /// <summary>MP3 / M4A 向け既定（波形表示はオフ。列をオンにすれば MP3 だけでも出す）。</summary>
    public static readonly LibraryFileColumn[] Mp3Defaults =
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
    ];

    /// <summary>Wave 設定で選べる列（Name 以外）。タグ／ジャケット／ビットレートは出さない。</summary>
    public static readonly LibraryFileColumn[] WaveSettingsColumns =
    [
        LibraryFileColumn.SampleRate,
        LibraryFileColumn.BitDepth,
        LibraryFileColumn.Channels,
        LibraryFileColumn.Duration,
        LibraryFileColumn.Date,
        LibraryFileColumn.ParentFolder,
        LibraryFileColumn.Waveform,
        LibraryFileColumn.Kind,
        LibraryFileColumn.Size,
        LibraryFileColumn.Folder,
        LibraryFileColumn.Comment,
    ];

    /// <summary>MP3 設定で選べる列（Name 以外）。ビット深度は出さない。</summary>
    public static readonly LibraryFileColumn[] Mp3SettingsColumns =
    [
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
        LibraryFileColumn.Channels,
        LibraryFileColumn.BitRate,
        LibraryFileColumn.Size,
        LibraryFileColumn.Folder,
        LibraryFileColumn.Jacket,
    ];

    public static bool IsLocked(LibraryFileColumn column) => column == LibraryFileColumn.Name;

    /// <summary>設定のチェック並び。既定順のあと、その形式で選べる残りの列。</summary>
    public static IEnumerable<LibraryFileColumn> SettingsCheckOrder(
        IReadOnlyList<LibraryFileColumn> defaults,
        IReadOnlyList<LibraryFileColumn> settingsColumns)
    {
        var allowed = new HashSet<LibraryFileColumn>(settingsColumns);
        var seen = new HashSet<LibraryFileColumn>();
        foreach (var column in defaults)
        {
            if (!IsLocked(column) && allowed.Contains(column) && seen.Add(column))
            {
                yield return column;
            }
        }

        foreach (var column in settingsColumns)
        {
            if (!IsLocked(column) && seen.Add(column))
            {
                yield return column;
            }
        }
    }

    /// <summary>
    /// プレイリストに値が1件でもある列。空リストは空集合（列をすべて隠す）。
    /// null は使用フィルタ無し（設定どおり全部出す）。
    /// ジャケットは実際に画像がある行が1件でもあれば出す。
    /// 無いときは列も枠も出さない。
    /// 波形は曲が1件でもあれば出す（中身は後から埋める）。
    /// 親フォルダ／波形を出すかは Wave / MP3 の列プリセット側。
    /// </summary>
    public static HashSet<LibraryFileColumn>? UsedColumns(IReadOnlyList<LibraryFileRow> rows)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var used = new HashSet<LibraryFileColumn>
        {
            LibraryFileColumn.Name,
            LibraryFileColumn.Waveform,
        };
        var jacketEligible = false;
        foreach (var row in rows)
        {
            if (!jacketEligible && row.HasArtwork)
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

        return used;
    }

    public static bool IsPcmFamilyKind(string kind) =>
        kind.Equals("WAVE", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("AIFF", StringComparison.OrdinalIgnoreCase);

    public static bool IsTagFamilyKind(string kind) =>
        kind.Equals("MP3", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("M4A", StringComparison.OrdinalIgnoreCase);

    public static bool IsVideoFamilyKind(string kind) =>
        kind.Equals("MOV", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("MP4", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("AVI", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("MKV", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("WEBM", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("MPG", StringComparison.OrdinalIgnoreCase)
        || kind.Equals("MPEG", StringComparison.OrdinalIgnoreCase);

    public static bool IsPdfFamilyKind(string kind) =>
        kind.Equals("PDF", StringComparison.OrdinalIgnoreCase);

    public static bool IsVisualFamilyKind(string kind) =>
        IsVideoFamilyKind(kind) || IsPdfFamilyKind(kind);

    /// <summary>
    /// 列判定に使う形式。ファイル名の拡張子を優先し、無ければ Kind 文字列。
    /// Kind が WAVE のままでも .mp3 なら MP3 とみなす。
    /// </summary>
    public static string EffectiveKind(LibraryFileRow row)
    {
        var fromName = KindFromFileName(row.Name);
        if (fromName is not null)
        {
            return fromName;
        }

        return string.IsNullOrWhiteSpace(row.Kind) ? "WAVE" : row.Kind;
    }

    public static string? KindFromFileName(string name)
    {
        var ext = Path.GetExtension(name);
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return "MP3";
        }

        if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
        {
            return "M4A";
        }

        if (ext.Equals(".aif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
        {
            return "AIFF";
        }

        if (ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".wave", StringComparison.OrdinalIgnoreCase))
        {
            return "WAVE";
        }

        if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return "PDF";
        }

        if (ext.Equals(".mov", StringComparison.OrdinalIgnoreCase))
        {
            return "MOV";
        }

        if (ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            return "MP4";
        }

        if (ext.Equals(".avi", StringComparison.OrdinalIgnoreCase))
        {
            return "AVI";
        }

        if (ext.Equals(".mkv", StringComparison.OrdinalIgnoreCase))
        {
            return "MKV";
        }

        if (ext.Equals(".webm", StringComparison.OrdinalIgnoreCase))
        {
            return "WEBM";
        }

        if (LibraryPlaylistDocuments.IsMpgExtension(ext))
        {
            return "MPG";
        }

        return null;
    }

    /// <summary>空・混在は Mixed。WAVE/AIFF のみ Wave。MP3/M4A のみ Mp3。</summary>
    public static LibraryPlaylistColumnKind ClassifyPlaylistColumns(IReadOnlyList<LibraryFileRow> rows)
    {
        if (rows.Count == 0)
        {
            return LibraryPlaylistColumnKind.Mixed;
        }

        var pcm = true;
        var tag = true;
        foreach (var row in rows)
        {
            var kind = EffectiveKind(row);
            if (!IsPcmFamilyKind(kind))
            {
                pcm = false;
            }

            if (!IsTagFamilyKind(kind))
            {
                tag = false;
            }

            if (!pcm && !tag)
            {
                return LibraryPlaylistColumnKind.Mixed;
            }
        }

        if (pcm)
        {
            return LibraryPlaylistColumnKind.Wave;
        }

        if (tag)
        {
            return LibraryPlaylistColumnKind.Mp3;
        }

        return LibraryPlaylistColumnKind.Mixed;
    }

    public static bool IsEffectivelyVisible(
        LibraryFileColumn column,
        IReadOnlyCollection<LibraryFileColumn> enabled,
        HashSet<LibraryFileColumn>? used)
    {
        if (used is { Count: 0 })
        {
            return false;
        }

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

    /// <summary>混在の初期並び。Wave 既定のあと MP3 側だけの列。</summary>
    public static LibraryFileColumn[] MixedDefaults => Union(WaveDefaults, Mp3Defaults);

    public static LibraryFileColumn[] ResolveWave(string[]? stored) =>
        Resolve(stored, WaveDefaults, migratePreviousDefaults: true);

    public static LibraryFileColumn[] ResolveMp3(string[]? stored) =>
        Resolve(stored, Mp3Defaults, migratePreviousDefaults: true);

    /// <summary>
    /// 混在用。空ならいまの Wave / MP3 設定の和集合。
    /// 一度でも保存されていればその並びを使う。
    /// </summary>
    public static LibraryFileColumn[] ResolveMixed(
        string[]? stored,
        IReadOnlyList<LibraryFileColumn> waveColumns,
        IReadOnlyList<LibraryFileColumn> mp3Columns)
    {
        if (stored is not { Length: > 0 })
        {
            return Union(waveColumns, mp3Columns);
        }

        return Resolve(stored, MixedDefaults, migratePreviousDefaults: false);
    }

    public static LibraryFileColumn[] ResolveActive(
        IReadOnlyList<LibraryFileRow> rows,
        IReadOnlyList<LibraryFileColumn> waveColumns,
        IReadOnlyList<LibraryFileColumn> mp3Columns,
        IReadOnlyList<LibraryFileColumn>? mixedColumns = null)
    {
        var wave = Normalize(waveColumns, WaveDefaults);
        var mp3 = Normalize(mp3Columns, Mp3Defaults);
        return ClassifyPlaylistColumns(rows) switch
        {
            LibraryPlaylistColumnKind.Wave => wave,
            LibraryPlaylistColumnKind.Mp3 => mp3,
            _ => mixedColumns is null || mixedColumns.Count == 0
                ? Union(wave, mp3)
                : Normalize(mixedColumns, MixedDefaults),
        };
    }

    /// <summary>Wave 順を先に、MP3 側だけの列を後ろへ。</summary>
    public static LibraryFileColumn[] Union(
        IReadOnlyList<LibraryFileColumn> waveColumns,
        IReadOnlyList<LibraryFileColumn> mp3Columns)
    {
        var result = new List<LibraryFileColumn>(waveColumns.Count + mp3Columns.Count);
        var seen = new HashSet<LibraryFileColumn>();
        void Add(LibraryFileColumn column)
        {
            if (Array.IndexOf(All, column) >= 0 && seen.Add(column))
            {
                result.Add(column);
            }
        }

        Add(LibraryFileColumn.Name);
        foreach (var column in waveColumns)
        {
            Add(column);
        }

        foreach (var column in mp3Columns)
        {
            Add(column);
        }

        return result.Count == 0 ? [.. WaveDefaults] : [.. result];
    }

    /// <summary>
    /// ヘッダー変更をプリセットへ書き戻す。
    /// Wave / Mp3 を変えたときは混在を和集合へ戻す。混在の操作は混在だけ更新する。
    /// </summary>
    public static void ApplyVisibleChange(
        LibraryPlaylistColumnKind kind,
        IReadOnlyList<LibraryFileColumn> previousWave,
        IReadOnlyList<LibraryFileColumn> previousMp3,
        IReadOnlyList<LibraryFileColumn> previousMixed,
        IReadOnlyList<LibraryFileColumn> nextVisible,
        out LibraryFileColumn[] wave,
        out LibraryFileColumn[] mp3,
        out LibraryFileColumn[] mixed)
    {
        var prevWave = Normalize(previousWave, WaveDefaults);
        var prevMp3 = Normalize(previousMp3, Mp3Defaults);
        _ = previousMixed;
        switch (kind)
        {
            case LibraryPlaylistColumnKind.Wave:
                wave = Normalize(nextVisible, WaveDefaults);
                mp3 = prevMp3;
                mixed = Union(wave, mp3);
                return;
            case LibraryPlaylistColumnKind.Mp3:
                wave = prevWave;
                mp3 = Normalize(nextVisible, Mp3Defaults);
                mixed = Union(wave, mp3);
                return;
            default:
                wave = prevWave;
                mp3 = prevMp3;
                mixed = Normalize(nextVisible, MixedDefaults);
                return;
        }
    }

    /// <summary>旧単一設定が既定並び（または空）なら true。カスタムは false。</summary>
    public static bool IsLegacyDefaultStored(string[]? stored)
    {
        if (stored is not { Length: > 0 })
        {
            return true;
        }

        var parsed = ParseStored(stored);
        if (parsed.Count == 0)
        {
            return true;
        }

        if (!parsed.Contains(LibraryFileColumn.Name))
        {
            parsed.Insert(0, LibraryFileColumn.Name);
        }

        if (IsPreviousDefaultColumnOrder(parsed))
        {
            return true;
        }

        return parsed.Count == WaveDefaults.Length
            && parsed.SequenceEqual(WaveDefaults);
    }

    public static string[] Serialize(IEnumerable<LibraryFileColumn> columns) =>
        Serialize(columns, WaveDefaults);

    public static string[] Serialize(IEnumerable<LibraryFileColumn> columns, LibraryFileColumn[] fallback)
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

        return names.Count == 0 ? Serialize(fallback, fallback) : [.. names];
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

    private static LibraryFileColumn[] Resolve(
        string[]? stored,
        LibraryFileColumn[] defaults,
        bool migratePreviousDefaults)
    {
        if (stored is not { Length: > 0 })
        {
            return [.. defaults];
        }

        var result = ParseStored(stored);
        if (result.Count == 0)
        {
            return [.. defaults];
        }

        var seen = new HashSet<LibraryFileColumn>(result);
        if (!seen.Contains(LibraryFileColumn.Name))
        {
            result.Insert(0, LibraryFileColumn.Name);
            seen.Add(LibraryFileColumn.Name);
        }

        if (migratePreviousDefaults && IsPreviousDefaultColumnOrder(result))
        {
            // MP3 で波形をオンにした並びは、旧既定（タグ列＋波形）と同じ形になる。消さない。
            if (!ReferenceEquals(defaults, Mp3Defaults)
                || !seen.Contains(LibraryFileColumn.Waveform))
            {
                return [.. defaults];
            }
        }

        if (ReferenceEquals(defaults, WaveDefaults))
        {
            if (!seen.Contains(LibraryFileColumn.Date) && IsLegacyDefaultSet(seen, defaults))
            {
                InsertDateAtDefaultPlace(result);
                seen.Add(LibraryFileColumn.Date);
            }

            if (!seen.Contains(LibraryFileColumn.ParentFolder)
                && IsLegacyDefaultWithoutParentFolder(seen, defaults))
            {
                InsertParentFolderAtDefaultPlace(result);
                seen.Add(LibraryFileColumn.ParentFolder);
            }

            if (!seen.Contains(LibraryFileColumn.Waveform)
                && IsLegacyDefaultWithoutWaveform(seen, defaults))
            {
                InsertWaveformAtDefaultPlace(result);
            }
        }

        return [.. result];
    }

    private static List<LibraryFileColumn> ParseStored(string[] stored)
    {
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

        return result;
    }

    private static LibraryFileColumn[] Normalize(
        IReadOnlyList<LibraryFileColumn> columns,
        LibraryFileColumn[] fallback)
    {
        if (columns.Count == 0)
        {
            return [.. fallback];
        }

        return Resolve(Serialize(columns, fallback), fallback, migratePreviousDefaults: false);
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
        ],
    ];

    private static bool IsLegacyDefaultWithoutParentFolder(
        HashSet<LibraryFileColumn> seen,
        LibraryFileColumn[] defaults)
    {
        if (seen.Count != defaults.Length - 2)
        {
            return false;
        }

        foreach (var column in defaults)
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

    private static bool IsLegacyDefaultWithoutWaveform(
        HashSet<LibraryFileColumn> seen,
        LibraryFileColumn[] defaults)
    {
        if (seen.Count != defaults.Length - 1)
        {
            return false;
        }

        foreach (var column in defaults)
        {
            if (column != LibraryFileColumn.Waveform && !seen.Contains(column))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLegacyDefaultSet(
        HashSet<LibraryFileColumn> seen,
        LibraryFileColumn[] defaults)
    {
        var expected = defaults.Length - 3;
        if (seen.Count != expected)
        {
            return false;
        }

        foreach (var column in defaults)
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
        var duration = result.IndexOf(LibraryFileColumn.Duration);
        if (duration >= 0)
        {
            result.Insert(duration + 1, LibraryFileColumn.Date);
            return;
        }

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
