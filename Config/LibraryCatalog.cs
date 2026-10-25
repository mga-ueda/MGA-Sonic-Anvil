using Microsoft.Data.Sqlite;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Config;

/// <summary>
/// ライブラリ用の薄いローカル索引。path + size + mtime でタグを再利用する。
/// ファイル発見（プレイリスト追加・Explorer 検索）の正は常にディスクで、F5 は必須ではない。
/// </summary>
internal static class LibraryCatalog
{
    public const string DatabaseFileName = "library-catalog.sqlite";

    private static readonly object Gate = new();
    private static SqliteConnection? _connection;
    private static string? _directoryOverride;

    public static string DatabasePath =>
        Path.Combine(DirectoryPath, DatabaseFileName);

    private static string DirectoryPath =>
        _directoryOverride ?? AppStorage.RootDirectory;

    /// <summary>起動時。失敗してもアプリは続行（毎回ディスク走査に戻る）。</summary>
    public static void Initialize()
    {
        lock (Gate)
        {
            try
            {
                EnsureOpen_NoLock();
            }
            catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
            {
                Close_NoLock();
            }
        }
    }

    /// <summary>テスト用。既存接続を閉じ、指定フォルダの DB を使う。</summary>
    internal static void UseDirectoryForTests(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        lock (Gate)
        {
            Close_NoLock();
            _directoryOverride = Path.GetFullPath(directory);
            Directory.CreateDirectory(_directoryOverride);
            EnsureOpen_NoLock();
        }
    }

    internal static void ResetForTests()
    {
        lock (Gate)
        {
            Close_NoLock();
            _directoryOverride = null;
        }
    }

    /// <summary>
    /// size/mtime が一致するタグがあれば適用用に返す。
    /// </summary>
    public static bool TryGetFreshTags(string path, out AudioFileTags tags)
    {
        tags = AudioFileTags.Empty;
        if (!TryNormalizePath(path, out var full) || !TryStat(full, out var size, out var mtime))
        {
            return false;
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return false;
            }

            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT size, mtime_utc, probed,
                           title, artist, album, album_artist, track, track_number,
                           disc, disc_number, year, year_number, genre, comment, composer,
                           duration_seconds, sample_rate, bits_per_sample, channels, bit_rate_kbps,
                           video_width, video_height, frame_rate, video_codec, audio_codec,
                           encoder_delay, encoder_padding, encoder_original, has_artwork
                    FROM files
                    WHERE path = $path
                    LIMIT 1;
                    """;
                cmd.Parameters.AddWithValue("$path", full);
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    return false;
                }

                var cachedSize = reader.GetInt64(0);
                var cachedMtime = reader.GetInt64(1);
                var probed = reader.GetInt64(2) != 0;
                if (!probed || cachedSize != size || cachedMtime != mtime)
                {
                    return false;
                }

                tags = ReadTags(reader, startOrdinal: 3);
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// ライブラリ検索用。プローブ済みのタグ文字列だけを返す。未構築のファイルは含めない。
    /// </summary>
    public static Dictionary<string, string[]> LoadProbedTagTexts(IReadOnlyList<string> roots)
    {
        var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var normalized = NormalizeRoots(roots);
        if (normalized.Count == 0)
        {
            return result;
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return result;
            }

            try
            {
                foreach (var root in normalized)
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText =
                        """
                        SELECT path, size, mtime_utc,
                               title, artist, album, album_artist, track, disc, year, genre, comment, composer,
                               duration_seconds, sample_rate, bits_per_sample, channels, bit_rate_kbps,
                               video_width, video_height, frame_rate, video_codec, audio_codec, has_artwork
                        FROM files
                        WHERE probed != 0
                          AND (path = $root OR path LIKE $prefix ESCAPE '\');
                        """;
                    cmd.Parameters.AddWithValue("$root", root);
                    cmd.Parameters.AddWithValue("$prefix", LikePrefix(root));
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        var texts = ReadSearchTagTexts(reader);
                        if (texts.Length == 0)
                        {
                            continue;
                        }

                        result[path] = texts;
                    }
                }
            }
            catch (SqliteException)
            {
            }
        }

        AppendCachedLoudness(result);
        return result;
    }

    /// <summary>本体と WAL／SHM を合わせたディスク上の容量。無いファイルは 0。</summary>
    public static long GetUsageBytes()
    {
        return FileLength(DatabasePath)
            + FileLength(DatabasePath + "-wal")
            + FileLength(DatabasePath + "-shm");
    }

    /// <summary>索引を捨てる。次の参照で空のデータベースを作り直す。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Close_NoLock();
            SqliteConnection.ClearAllPools();
            TryDeleteFile(DatabasePath);
            TryDeleteFile(DatabasePath + "-wal");
            TryDeleteFile(DatabasePath + "-shm");
        }
    }

    /// <summary>プローブ結果を保存。ファイルが消えていれば何もしない。</summary>
    public static void UpsertTags(string path, AudioFileTags tags)
    {
        if (!tags.Probed || !TryNormalizePath(path, out var full) || !TryStat(full, out var size, out var mtime))
        {
            return;
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                UpsertRow_NoLock(full, size, mtime, tags);
            }
            catch (SqliteException)
            {
            }
        }
    }

    /// <summary>
    /// 索引上のファイル一覧（保守／テスト用）。UI の発見経路では使わない。
    /// </summary>
    public static bool TryListFilesUnderRoots(IReadOnlyList<string> roots, out string[] files)
    {
        files = [];
        var normalized = NormalizeRoots(roots);
        if (normalized.Count == 0)
        {
            return false;
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return false;
            }

            try
            {
                foreach (var root in normalized)
                {
                    if (!IsRootCovered_NoLock(root))
                    {
                        return false;
                    }
                }

                var found = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in normalized)
                {
                    using var cmd = _connection!.CreateCommand();
                    cmd.CommandText =
                        """
                        SELECT path FROM files
                        WHERE path = $root OR path LIKE $prefix ESCAPE '\'
                        ORDER BY path COLLATE NOCASE;
                        """;
                    cmd.Parameters.AddWithValue("$root", root);
                    cmd.Parameters.AddWithValue("$prefix", LikePrefix(root));
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (seen.Add(path))
                        {
                            found.Add(path);
                        }
                    }
                }

                files = found.ToArray();
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 任意の掃除。ルート配下をディスク走査し、削除・mtime 変化を索引へ反映する。
    /// タグの再プローブはしない。ファイル発見の正ではないので押さなくても欠落しない。
    /// </summary>
    public static void ReconcileRoots(IReadOnlyList<string> roots)
    {
        var normalized = NormalizeRoots(roots);
        if (normalized.Count == 0)
        {
            return;
        }

        foreach (var root in normalized)
        {
            string[] diskFiles;
            try
            {
                if (!Directory.Exists(root))
                {
                    ClearRoot_NoLockSafe(root);
                    continue;
                }

                diskFiles = AudioCodec.CollectPlayerOpenableFromDirectory(root, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            ReplaceInventory(root, diskFiles);
        }

        PruneUnreferenced(normalized);
    }

    /// <summary>
    /// 今のルートから外れた行と、ルートが存在するのにファイルが無い行を消す。
    /// ルート自体が無い（取り外したドライブなど）ときは、その配下は残す。
    /// </summary>
    public static void PruneUnreferenced(IReadOnlyList<string> roots)
    {
        var normalized = NormalizeRoots(roots);
        var paths = ListIndexedPaths();
        if (paths.Count == 0 && normalized.Count == 0)
        {
            DropRootsExcept(normalized);
            return;
        }

        var rootPresent = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var drop = new List<string>();
        foreach (var path in paths)
        {
            var covering = CoveringRoot(path, normalized);
            if (covering is null)
            {
                drop.Add(path);
                continue;
            }

            if (!rootPresent.TryGetValue(covering, out var present))
            {
                present = Directory.Exists(covering);
                rootPresent[covering] = present;
            }

            if (present && IsConfirmedMissing(path))
            {
                drop.Add(path);
            }
        }

        if (drop.Count > 0)
        {
            DeleteFiles(drop);
        }

        DropRootsExcept(normalized);
    }

    /// <summary>
    /// 索引上のフォルダ配下一覧（保守／テスト用）。UI の発見経路では使わない。
    /// </summary>
    public static bool TryListFilesUnderPath(string folder, out string[] files)
    {
        files = [];
        if (!TryNormalizePath(folder, out var full))
        {
            return false;
        }

        full = TrimTrailingSeparators(full);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return false;
            }

            try
            {
                if (!IsPathCovered_NoLock(full))
                {
                    return false;
                }

                var found = new List<string>();
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText =
                    """
                    SELECT path FROM files
                    WHERE path = $root OR path LIKE $prefix ESCAPE '\'
                    ORDER BY path COLLATE NOCASE;
                    """;
                cmd.Parameters.AddWithValue("$root", full);
                cmd.Parameters.AddWithValue("$prefix", LikePrefix(full));
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    found.Add(reader.GetString(0));
                }

                files = found.ToArray();
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }
    }

    private static List<string> ListIndexedPaths()
    {
        var paths = new List<string>();
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return paths;
            }

            try
            {
                using var cmd = _connection!.CreateCommand();
                cmd.CommandText = "SELECT path FROM files;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    paths.Add(reader.GetString(0));
                }
            }
            catch (SqliteException)
            {
            }
        }

        return paths;
    }

    private static string? CoveringRoot(string path, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (IsUnderRoot(path, root))
            {
                return root;
            }
        }

        return null;
    }

    private static bool IsConfirmedMissing(string path)
    {
        try
        {
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>索引にある path とその配下。ルート外でも返す。ディスク走査はしない。</summary>
    public static string[] ListIndexedFiles(string path)
    {
        if (!TryNormalizePath(path, out var full))
        {
            return [];
        }

        full = TrimTrailingSeparators(full);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return [];
            }

            try
            {
                return ListPrefix_NoLock(full).ToArray();
            }
            catch (SqliteException)
            {
                return [];
            }
        }
    }

    /// <summary>ファイルの行を新しいパスへ複製する。元の行が無ければ何もしない。</summary>
    public static void CloneEntry(string from, string to)
    {
        if (!TryNormalizePath(from, out var source) || !TryNormalizePath(to, out var dest))
        {
            return;
        }

        if (!TryStat(dest, out var size, out var mtime))
        {
            return;
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                CloneRow_NoLock(source, dest, size, mtime);
            }
            catch (SqliteException)
            {
            }
        }
    }

    /// <summary>ファイルまたはフォルダ配下の path を新しい場所へ付け替える。</summary>
    public static void RetargetPrefix(string from, string to)
    {
        if (!TryNormalizePath(from, out var source) || !TryNormalizePath(to, out var dest))
        {
            return;
        }

        source = TrimTrailingSeparators(source);
        dest = TrimTrailingSeparators(dest);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                var paths = ListPrefix_NoLock(source);
                paths.Sort((a, b) => b.Length.CompareTo(a.Length));
                using var tx = _connection!.BeginTransaction();
                foreach (var path in paths)
                {
                    if (!LibraryFileTransfer.TryMapPath(path, source, dest, out var mapped))
                    {
                        continue;
                    }

                    if (!TryStat(mapped, out var size, out var mtime))
                    {
                        size = 0;
                        mtime = 0;
                    }

                    RewritePath_NoLock(path, mapped, size, mtime, tx);
                }

                RewriteRoot_NoLock(source, dest, tx);
                tx.Commit();
            }
            catch (SqliteException)
            {
            }
        }
    }

    /// <summary>ファイルまたはフォルダ配下の行を消す。</summary>
    public static void RemovePrefix(string path)
    {
        if (!TryNormalizePath(path, out var full))
        {
            return;
        }

        full = TrimTrailingSeparators(full);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                using var tx = _connection!.BeginTransaction();
                using var del = _connection.CreateCommand();
                del.Transaction = tx;
                del.CommandText =
                    """
                    DELETE FROM files
                    WHERE path = $root OR path LIKE $prefix ESCAPE '\';
                    """;
                del.Parameters.AddWithValue("$root", full);
                del.Parameters.AddWithValue("$prefix", LikePrefix(full));
                del.ExecuteNonQuery();
                using var root = _connection.CreateCommand();
                root.Transaction = tx;
                root.CommandText = "DELETE FROM roots WHERE path = $root;";
                root.Parameters.AddWithValue("$root", full);
                root.ExecuteNonQuery();
                tx.Commit();
            }
            catch (SqliteException)
            {
            }
        }
    }

    private static List<string> ListPrefix_NoLock(string full)
    {
        var found = new List<string>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            """
            SELECT path FROM files
            WHERE path = $root OR path LIKE $prefix ESCAPE '\'
            ORDER BY path COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$root", full);
        cmd.Parameters.AddWithValue("$prefix", LikePrefix(full));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }

    private static void RewritePath_NoLock(
        string from,
        string to,
        long size,
        long mtime,
        SqliteTransaction tx)
    {
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(from, to, StringComparison.Ordinal))
        {
            var hop = from + "\u0001mga";
            RewritePath_NoLock(from, hop, size, mtime, tx);
            from = hop;
        }

        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            """
            UPDATE files SET
                path = $path,
                size = CASE WHEN $size > 0 THEN $size ELSE size END,
                mtime_utc = CASE WHEN $mtime > 0 THEN $mtime ELSE mtime_utc END,
                file_name = $name,
                parent_dir = $parent
            WHERE path = $from;
            """;
        cmd.Parameters.AddWithValue("$path", to);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$mtime", mtime);
        cmd.Parameters.AddWithValue("$name", Path.GetFileName(to));
        cmd.Parameters.AddWithValue("$parent", Path.GetDirectoryName(to) ?? string.Empty);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.ExecuteNonQuery();
    }

    private static void RewriteRoot_NoLock(string from, string to, SqliteTransaction tx)
    {
        if (string.Equals(from, to, StringComparison.Ordinal))
        {
            return;
        }

        using var cmd = _connection!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE roots SET path = $to WHERE path = $from;";
        cmd.Parameters.AddWithValue("$to", to);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.ExecuteNonQuery();
    }

    private static void CloneRow_NoLock(string from, string to, long size, long mtime)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO files(
                path, size, mtime_utc, file_name, parent_dir, probed,
                title, artist, album, album_artist, track, track_number,
                disc, disc_number, year, year_number, genre, comment, composer,
                duration_seconds, sample_rate, bits_per_sample, channels, bit_rate_kbps,
                video_width, video_height, frame_rate, video_codec, audio_codec,
                encoder_delay, encoder_padding, encoder_original, has_artwork)
            SELECT
                $path, $size, $mtime, $name, $parent, probed,
                title, artist, album, album_artist, track, track_number,
                disc, disc_number, year, year_number, genre, comment, composer,
                duration_seconds, sample_rate, bits_per_sample, channels, bit_rate_kbps,
                video_width, video_height, frame_rate, video_codec, audio_codec,
                encoder_delay, encoder_padding, encoder_original, has_artwork
            FROM files WHERE path = $from
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                mtime_utc = excluded.mtime_utc,
                file_name = excluded.file_name,
                parent_dir = excluded.parent_dir,
                probed = excluded.probed,
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                album_artist = excluded.album_artist,
                track = excluded.track,
                track_number = excluded.track_number,
                disc = excluded.disc,
                disc_number = excluded.disc_number,
                year = excluded.year,
                year_number = excluded.year_number,
                genre = excluded.genre,
                comment = excluded.comment,
                composer = excluded.composer,
                duration_seconds = excluded.duration_seconds,
                sample_rate = excluded.sample_rate,
                bits_per_sample = excluded.bits_per_sample,
                channels = excluded.channels,
                bit_rate_kbps = excluded.bit_rate_kbps,
                video_width = excluded.video_width,
                video_height = excluded.video_height,
                frame_rate = excluded.frame_rate,
                video_codec = excluded.video_codec,
                audio_codec = excluded.audio_codec,
                encoder_delay = excluded.encoder_delay,
                encoder_padding = excluded.encoder_padding,
                encoder_original = excluded.encoder_original,
                has_artwork = excluded.has_artwork;
            """;
        cmd.Parameters.AddWithValue("$path", to);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$mtime", mtime);
        cmd.Parameters.AddWithValue("$name", Path.GetFileName(to));
        cmd.Parameters.AddWithValue("$parent", Path.GetDirectoryName(to) ?? string.Empty);
        cmd.Parameters.AddWithValue("$from", from);
        cmd.ExecuteNonQuery();
    }

    private static void DeleteFiles(IReadOnlyList<string> paths)
    {
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                using var tx = _connection!.BeginTransaction();
                foreach (var path in paths)
                {
                    using var del = _connection.CreateCommand();
                    del.Transaction = tx;
                    del.CommandText = "DELETE FROM files WHERE path = $path;";
                    del.Parameters.AddWithValue("$path", path);
                    del.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (SqliteException)
            {
            }
        }
    }

    private static void DropRootsExcept(IReadOnlyList<string> roots)
    {
        var keep = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                var stale = new List<string>();
                using (var list = _connection!.CreateCommand())
                {
                    list.CommandText = "SELECT path FROM roots;";
                    using var reader = list.ExecuteReader();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (!keep.Contains(path))
                        {
                            stale.Add(path);
                        }
                    }
                }

                if (stale.Count == 0)
                {
                    return;
                }

                foreach (var path in stale)
                {
                    using var del = _connection.CreateCommand();
                    del.CommandText = "DELETE FROM roots WHERE path = $path;";
                    del.Parameters.AddWithValue("$path", path);
                    del.ExecuteNonQuery();
                }
            }
            catch (SqliteException)
            {
            }
        }
    }

    private static bool IsPathCovered_NoLock(string folder)
    {
        if (IsRootCovered_NoLock(folder))
        {
            return true;
        }

        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT path FROM roots;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var root = reader.GetString(0);
            if (IsUnderRoot(folder, root))
            {
                return true;
            }
        }

        return false;
    }

    private static void ReplaceInventory(string root, IReadOnlyList<string> files)
    {
        if (!TryNormalizePath(root, out var rootFull))
        {
            return;
        }

        rootFull = TrimTrailingSeparators(rootFull);
        var rows = new List<(string Path, long Size, long Mtime)>(files.Count);
        foreach (var file in files)
        {
            if (!TryNormalizePath(file, out var full) || !TryStat(full, out var size, out var mtime))
            {
                continue;
            }

            if (!IsUnderRoot(full, rootFull))
            {
                continue;
            }

            rows.Add((full, size, mtime));
        }

        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                using var tx = _connection!.BeginTransaction();
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in rows)
                {
                    keep.Add(row.Path);
                    UpsertInventoryRow_NoLock(row.Path, row.Size, row.Mtime, tx);
                }

                using (var list = _connection.CreateCommand())
                {
                    list.Transaction = tx;
                    list.CommandText =
                        """
                        SELECT path FROM files
                        WHERE path = $root OR path LIKE $prefix ESCAPE '\';
                        """;
                    list.Parameters.AddWithValue("$root", rootFull);
                    list.Parameters.AddWithValue("$prefix", LikePrefix(rootFull));
                    using var reader = list.ExecuteReader();
                    var stale = new List<string>();
                    while (reader.Read())
                    {
                        var path = reader.GetString(0);
                        if (!keep.Contains(path))
                        {
                            stale.Add(path);
                        }
                    }

                    reader.Close();
                    foreach (var path in stale)
                    {
                        using var del = _connection.CreateCommand();
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM files WHERE path = $path;";
                        del.Parameters.AddWithValue("$path", path);
                        del.ExecuteNonQuery();
                    }
                }

                using (var cover = _connection.CreateCommand())
                {
                    cover.Transaction = tx;
                    cover.CommandText =
                        """
                        INSERT INTO roots(path, reconciled_utc)
                        VALUES ($path, $utc)
                        ON CONFLICT(path) DO UPDATE SET reconciled_utc = excluded.reconciled_utc;
                        """;
                    cover.Parameters.AddWithValue("$path", rootFull);
                    cover.Parameters.AddWithValue("$utc", DateTime.UtcNow.Ticks);
                    cover.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (SqliteException)
            {
            }
        }
    }

    private static void ClearRoot_NoLockSafe(string root)
    {
        if (!TryNormalizePath(root, out var rootFull))
        {
            return;
        }

        rootFull = TrimTrailingSeparators(rootFull);
        lock (Gate)
        {
            if (!TryEnsureOpen_NoLock())
            {
                return;
            }

            try
            {
                using var tx = _connection!.BeginTransaction();
                using (var del = _connection.CreateCommand())
                {
                    del.Transaction = tx;
                    del.CommandText =
                        """
                        DELETE FROM files
                        WHERE path = $root OR path LIKE $prefix ESCAPE '\';
                        """;
                    del.Parameters.AddWithValue("$root", rootFull);
                    del.Parameters.AddWithValue("$prefix", LikePrefix(rootFull));
                    del.ExecuteNonQuery();
                }

                using (var cover = _connection.CreateCommand())
                {
                    cover.Transaction = tx;
                    cover.CommandText =
                        """
                        INSERT INTO roots(path, reconciled_utc)
                        VALUES ($path, $utc)
                        ON CONFLICT(path) DO UPDATE SET reconciled_utc = excluded.reconciled_utc;
                        """;
                    cover.Parameters.AddWithValue("$path", rootFull);
                    cover.Parameters.AddWithValue("$utc", DateTime.UtcNow.Ticks);
                    cover.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch (SqliteException)
            {
            }
        }
    }

    private static void UpsertInventoryRow_NoLock(
        string path,
        long size,
        long mtime,
        SqliteTransaction tx)
    {
        long? oldSize = null;
        long? oldMtime = null;
        using (var select = _connection!.CreateCommand())
        {
            select.Transaction = tx;
            select.CommandText = "SELECT size, mtime_utc FROM files WHERE path = $path LIMIT 1;";
            select.Parameters.AddWithValue("$path", path);
            using var reader = select.ExecuteReader();
            if (reader.Read())
            {
                oldSize = reader.GetInt64(0);
                oldMtime = reader.GetInt64(1);
            }
        }

        if (oldSize == size && oldMtime == mtime)
        {
            return;
        }

        using var cmd = _connection.CreateCommand();
        cmd.Transaction = tx;
        if (oldSize is null)
        {
            cmd.CommandText =
                """
                INSERT INTO files(path, size, mtime_utc, file_name, parent_dir, probed)
                VALUES ($path, $size, $mtime, $name, $parent, 0);
                """;
        }
        else
        {
            // size/mtime が変わったらタグは捨てる（次回 Ensure で再プローブ）。
            cmd.CommandText =
                """
                UPDATE files SET
                    size = $size,
                    mtime_utc = $mtime,
                    file_name = $name,
                    parent_dir = $parent,
                    probed = 0,
                    title = '', artist = '', album = '', album_artist = '',
                    track = '', track_number = 0, disc = '', disc_number = 0,
                    year = '', year_number = 0, genre = '', comment = '', composer = '',
                    duration_seconds = 0, sample_rate = 0, bits_per_sample = 0,
                    channels = 0, bit_rate_kbps = 0,
                    video_width = 0, video_height = 0, frame_rate = 0,
                    video_codec = '', audio_codec = '',
                    encoder_delay = 0, encoder_padding = 0, encoder_original = 0,
                    has_artwork = 0
                WHERE path = $path;
                """;
        }

        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$mtime", mtime);
        cmd.Parameters.AddWithValue("$name", Path.GetFileName(path));
        cmd.Parameters.AddWithValue("$parent", Path.GetDirectoryName(path) ?? string.Empty);
        cmd.ExecuteNonQuery();
    }

    private static void UpsertRow_NoLock(string path, long size, long mtime, AudioFileTags tags)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO files(
                path, size, mtime_utc, file_name, parent_dir, probed,
                title, artist, album, album_artist, track, track_number,
                disc, disc_number, year, year_number, genre, comment, composer,
                duration_seconds, sample_rate, bits_per_sample, channels, bit_rate_kbps,
                video_width, video_height, frame_rate, video_codec, audio_codec,
                encoder_delay, encoder_padding, encoder_original, has_artwork)
            VALUES (
                $path, $size, $mtime, $name, $parent, 1,
                $title, $artist, $album, $album_artist, $track, $track_number,
                $disc, $disc_number, $year, $year_number, $genre, $comment, $composer,
                $duration, $rate, $bits, $channels, $bitrate,
                $vw, $vh, $fps, $vcodec, $acodec,
                $delay, $padding, $original, $art)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                mtime_utc = excluded.mtime_utc,
                file_name = excluded.file_name,
                parent_dir = excluded.parent_dir,
                probed = 1,
                title = excluded.title,
                artist = excluded.artist,
                album = excluded.album,
                album_artist = excluded.album_artist,
                track = excluded.track,
                track_number = excluded.track_number,
                disc = excluded.disc,
                disc_number = excluded.disc_number,
                year = excluded.year,
                year_number = excluded.year_number,
                genre = excluded.genre,
                comment = excluded.comment,
                composer = excluded.composer,
                duration_seconds = excluded.duration_seconds,
                sample_rate = excluded.sample_rate,
                bits_per_sample = excluded.bits_per_sample,
                channels = excluded.channels,
                bit_rate_kbps = excluded.bit_rate_kbps,
                video_width = excluded.video_width,
                video_height = excluded.video_height,
                frame_rate = excluded.frame_rate,
                video_codec = excluded.video_codec,
                audio_codec = excluded.audio_codec,
                encoder_delay = excluded.encoder_delay,
                encoder_padding = excluded.encoder_padding,
                encoder_original = excluded.encoder_original,
                has_artwork = excluded.has_artwork;
            """;
        cmd.Parameters.AddWithValue("$path", path);
        cmd.Parameters.AddWithValue("$size", size);
        cmd.Parameters.AddWithValue("$mtime", mtime);
        cmd.Parameters.AddWithValue("$name", Path.GetFileName(path));
        cmd.Parameters.AddWithValue("$parent", Path.GetDirectoryName(path) ?? string.Empty);
        cmd.Parameters.AddWithValue("$title", tags.Title);
        cmd.Parameters.AddWithValue("$artist", tags.Artist);
        cmd.Parameters.AddWithValue("$album", tags.Album);
        cmd.Parameters.AddWithValue("$album_artist", tags.AlbumArtist);
        cmd.Parameters.AddWithValue("$track", tags.Track);
        cmd.Parameters.AddWithValue("$track_number", tags.TrackNumber);
        cmd.Parameters.AddWithValue("$disc", tags.Disc);
        cmd.Parameters.AddWithValue("$disc_number", tags.DiscNumber);
        cmd.Parameters.AddWithValue("$year", tags.Year);
        cmd.Parameters.AddWithValue("$year_number", tags.YearNumber);
        cmd.Parameters.AddWithValue("$genre", tags.Genre);
        cmd.Parameters.AddWithValue("$comment", tags.Comment);
        cmd.Parameters.AddWithValue("$composer", tags.Composer);
        cmd.Parameters.AddWithValue("$duration", tags.DurationSeconds);
        cmd.Parameters.AddWithValue("$rate", tags.SampleRate);
        cmd.Parameters.AddWithValue("$bits", tags.BitsPerSample);
        cmd.Parameters.AddWithValue("$channels", tags.Channels);
        cmd.Parameters.AddWithValue("$bitrate", tags.BitRateKbps);
        cmd.Parameters.AddWithValue("$vw", tags.VideoWidth);
        cmd.Parameters.AddWithValue("$vh", tags.VideoHeight);
        cmd.Parameters.AddWithValue("$fps", tags.FrameRate);
        cmd.Parameters.AddWithValue("$vcodec", tags.VideoCodec);
        cmd.Parameters.AddWithValue("$acodec", tags.AudioCodec);
        cmd.Parameters.AddWithValue("$delay", tags.EncoderDelayFrames);
        cmd.Parameters.AddWithValue("$padding", tags.EncoderPaddingFrames);
        cmd.Parameters.AddWithValue("$original", tags.EncoderOriginalFrames);
        cmd.Parameters.AddWithValue("$art", tags.HasArtwork ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    private static string[] ReadSearchTagTexts(SqliteDataReader reader)
    {
        var i = 1;
        var size = reader.GetInt64(i++);
        var mtime = reader.GetInt64(i++);
        var tags = new AudioFileTags
        {
            Probed = true,
            Title = TagText(reader, ref i),
            Artist = TagText(reader, ref i),
            Album = TagText(reader, ref i),
            AlbumArtist = TagText(reader, ref i),
            Track = TagText(reader, ref i),
            Disc = TagText(reader, ref i),
            Year = TagText(reader, ref i),
            Genre = TagText(reader, ref i),
            Comment = TagText(reader, ref i),
            Composer = TagText(reader, ref i),
            DurationSeconds = reader.GetDouble(i++),
            SampleRate = reader.GetInt32(i++),
            BitsPerSample = reader.GetInt32(i++),
            Channels = reader.GetInt32(i++),
            BitRateKbps = reader.GetInt32(i++),
            VideoWidth = reader.GetInt32(i++),
            VideoHeight = reader.GetInt32(i++),
            FrameRate = reader.GetDouble(i++),
            VideoCodec = reader.GetString(i++),
            AudioCodec = reader.GetString(i++),
            HasArtwork = reader.GetInt64(i) != 0,
        };
        return LibraryPlaylistSearchTexts.Build(reader.GetString(0), size, mtime, tags);
    }

    private static void AppendCachedLoudness(Dictionary<string, string[]> texts)
    {
        if (texts.Count == 0)
        {
            return;
        }

        foreach (var path in texts.Keys.ToArray())
        {
            if (!AudioCodec.IsUncompressedPcm(path)
                || !LibraryMediaCache.TryGetLoudness(path, out var lufs))
            {
                continue;
            }

            var loudness = UiStrings.FormatIntegratedLufs(lufs);
            if (loudness.Length == 0)
            {
                continue;
            }

            var row = texts[path];
            var next = new string[row.Length + 1];
            row.CopyTo(next, 0);
            next[^1] = loudness;
            texts[path] = next;
        }
    }

    private static long FileLength(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string TagText(SqliteDataReader reader, ref int ordinal)
    {
        var text = reader.GetString(ordinal);
        ordinal++;
        return AudioTagText.CollapseIdentical(text);
    }

    private static AudioFileTags ReadTags(SqliteDataReader reader, int startOrdinal)
    {
        var i = startOrdinal;
        return new AudioFileTags
        {
            Probed = true,
            Title = TagText(reader, ref i),
            Artist = TagText(reader, ref i),
            Album = TagText(reader, ref i),
            AlbumArtist = TagText(reader, ref i),
            Track = TagText(reader, ref i),
            TrackNumber = reader.GetInt32(i++),
            Disc = TagText(reader, ref i),
            DiscNumber = reader.GetInt32(i++),
            Year = TagText(reader, ref i),
            YearNumber = reader.GetInt32(i++),
            Genre = TagText(reader, ref i),
            Comment = TagText(reader, ref i),
            Composer = TagText(reader, ref i),
            DurationSeconds = reader.GetDouble(i++),
            SampleRate = reader.GetInt32(i++),
            BitsPerSample = reader.GetInt32(i++),
            Channels = reader.GetInt32(i++),
            BitRateKbps = reader.GetInt32(i++),
            VideoWidth = reader.GetInt32(i++),
            VideoHeight = reader.GetInt32(i++),
            FrameRate = reader.GetDouble(i++),
            VideoCodec = reader.GetString(i++),
            AudioCodec = reader.GetString(i++),
            EncoderDelayFrames = reader.GetInt32(i++),
            EncoderPaddingFrames = reader.GetInt32(i++),
            EncoderOriginalFrames = reader.GetInt64(i++),
            HasArtwork = reader.GetInt64(i) != 0,
        };
    }

    private static bool IsRootCovered_NoLock(string root)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM roots WHERE path = $path LIMIT 1;";
        cmd.Parameters.AddWithValue("$path", root);
        var value = cmd.ExecuteScalar();
        return value is not null and not DBNull;
    }

    private static bool TryEnsureOpen_NoLock()
    {
        try
        {
            EnsureOpen_NoLock();
            return _connection is not null;
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            Close_NoLock();
            return false;
        }
    }

    private static void EnsureOpen_NoLock()
    {
        if (_connection is not null)
        {
            return;
        }

        Directory.CreateDirectory(DirectoryPath);
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = DatabasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared,
            }.ToString());
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }

        using (var create = connection.CreateCommand())
        {
            create.CommandText =
                """
                CREATE TABLE IF NOT EXISTS meta(
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS roots(
                    path TEXT PRIMARY KEY COLLATE NOCASE NOT NULL,
                    reconciled_utc INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS files(
                    path TEXT PRIMARY KEY COLLATE NOCASE NOT NULL,
                    size INTEGER NOT NULL,
                    mtime_utc INTEGER NOT NULL,
                    file_name TEXT NOT NULL,
                    parent_dir TEXT NOT NULL,
                    probed INTEGER NOT NULL DEFAULT 0,
                    title TEXT NOT NULL DEFAULT '',
                    artist TEXT NOT NULL DEFAULT '',
                    album TEXT NOT NULL DEFAULT '',
                    album_artist TEXT NOT NULL DEFAULT '',
                    track TEXT NOT NULL DEFAULT '',
                    track_number INTEGER NOT NULL DEFAULT 0,
                    disc TEXT NOT NULL DEFAULT '',
                    disc_number INTEGER NOT NULL DEFAULT 0,
                    year TEXT NOT NULL DEFAULT '',
                    year_number INTEGER NOT NULL DEFAULT 0,
                    genre TEXT NOT NULL DEFAULT '',
                    comment TEXT NOT NULL DEFAULT '',
                    composer TEXT NOT NULL DEFAULT '',
                    duration_seconds REAL NOT NULL DEFAULT 0,
                    sample_rate INTEGER NOT NULL DEFAULT 0,
                    bits_per_sample INTEGER NOT NULL DEFAULT 0,
                    channels INTEGER NOT NULL DEFAULT 0,
                    bit_rate_kbps INTEGER NOT NULL DEFAULT 0,
                    video_width INTEGER NOT NULL DEFAULT 0,
                    video_height INTEGER NOT NULL DEFAULT 0,
                    frame_rate REAL NOT NULL DEFAULT 0,
                    video_codec TEXT NOT NULL DEFAULT '',
                    audio_codec TEXT NOT NULL DEFAULT '',
                    encoder_delay INTEGER NOT NULL DEFAULT 0,
                    encoder_padding INTEGER NOT NULL DEFAULT 0,
                    encoder_original INTEGER NOT NULL DEFAULT 0,
                    has_artwork INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX IF NOT EXISTS ix_files_parent ON files(parent_dir);
                CREATE INDEX IF NOT EXISTS ix_files_name ON files(file_name);
                INSERT OR IGNORE INTO meta(key, value) VALUES ('schema', '1');
                """;
            create.ExecuteNonQuery();
        }

        _connection = connection;
    }

    private static void Close_NoLock()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private static List<string> NormalizeRoots(IReadOnlyList<string> roots)
    {
        var result = new List<string>(roots.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            if (!TryNormalizePath(root, out var full))
            {
                continue;
            }

            full = TrimTrailingSeparators(full);
            if (seen.Add(full))
            {
                result.Add(full);
            }
        }

        return result;
    }

    private static bool TryNormalizePath(string path, out string full) =>
        PathUtil.TryGetFullPathTrimmed(path, out full);

    private static bool TryStat(string path, out long size, out long mtimeTicks)
    {
        size = 0;
        mtimeTicks = 0;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            size = info.Length;
            mtimeTicks = info.LastWriteTimeUtc.Ticks;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string TrimTrailingSeparators(string path)
    {
        if (path.Length <= 3)
        {
            // "C:\" はそのまま。
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsUnderRoot(string filePath, string root)
    {
        if (filePath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return filePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>LIKE 用。\ と % _ をエスケープし、配下一致の prefix にする。</summary>
    private static string LikePrefix(string root)
    {
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        return EscapeLike(prefix) + "%";
    }

    private static string EscapeLike(string value)
    {
        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
    }
}
