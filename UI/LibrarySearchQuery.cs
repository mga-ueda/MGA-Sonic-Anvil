using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーのライブラリ／プレイリスト検索。空白は AND、<c>|</c> は OR。
/// <c>"..."</c> は中の空白を分けず、その文字列の連続一致。
/// <c>*</c> は任意の文字列、<c>?</c> は任意の一文字（語に含むとき全体マッチ）。
/// 対象はファイル名、フォルダ名、コメント／タイトル／アルバム／アーティスト／
/// 作曲／ジャンル／アルバムアーティスト。タグが未構築のファイルはタグではヒットしない。
/// </summary>
internal static class LibrarySearchQuery
{
    public static List<string[]> Parse(string text) => TileSearchQuery.Parse(text);

    public static bool MatchesFileName(string pathOrName, IReadOnlyList<string[]> groups)
    {
        var name = Path.GetFileName(pathOrName);
        return string.IsNullOrEmpty(name)
            ? TileSearchQuery.Matches(pathOrName, groups)
            : TileSearchQuery.Matches(name, groups);
    }

    public static bool MatchesFolderName(string pathOrName, IReadOnlyList<string[]> groups) =>
        MatchesFileName(pathOrName, groups);

    public static bool MatchesRow(LibraryFileRow row, IReadOnlyList<string[]> groups) =>
        TileSearchQuery.MatchesAny(RowTexts(row), groups);

    private static IEnumerable<string> RowTexts(LibraryFileRow row)
    {
        yield return row.Name;
        yield return row.ParentFolder;
        yield return row.Title;
        yield return row.Artist;
        yield return row.AlbumArtist;
        yield return row.Album;
        yield return row.Genre;
        yield return row.Composer;
        yield return row.Comment;
    }

    /// <summary>
    /// ヒットしたファイルの親、名前がヒットしたフォルダ、ルートまでの祖先だけ。ヒットの無い枝は含めない。
    /// ファイルはファイル名、祖先フォルダ名、索引済みの対象タグを横断して AND／OR する。
    /// </summary>
    public static HashSet<string> VisibleFolders(
        IReadOnlyList<string> roots,
        IEnumerable<string> filePaths,
        IReadOnlyList<string[]> groups) =>
        VisibleFolders(roots, filePaths, folderPaths: null, groups);

    public static HashSet<string> VisibleFolders(
        IReadOnlyList<string> roots,
        IEnumerable<string> filePaths,
        IEnumerable<string>? folderPaths,
        IReadOnlyList<string[]> groups,
        IReadOnlyDictionary<string, string[]>? tagTexts = null)
    {
        var visible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (groups.Count == 0)
        {
            return visible;
        }

        var normalizedRoots = new List<string>(roots.Count);
        foreach (var root in roots)
        {
            var full = NormalizeFolder(root);
            if (full.Length > 0)
            {
                normalizedRoots.Add(full);
            }
        }

        if (normalizedRoots.Count == 0)
        {
            return visible;
        }

        foreach (var path in filePaths)
        {
            if (string.IsNullOrEmpty(path)
                || !MatchesLibraryFile(path, groups, normalizedRoots, tagTexts))
            {
                continue;
            }

            try
            {
                AddAncestors(Path.GetDirectoryName(path), normalizedRoots, visible);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
            }
        }

        if (folderPaths is not null)
        {
            foreach (var folder in folderPaths)
            {
                if (string.IsNullOrEmpty(folder) || !MatchesFolderName(folder, groups))
                {
                    continue;
                }

                AddAncestors(folder, normalizedRoots, visible);
            }
        }

        return visible;
    }

    private static IEnumerable<string> LibraryFileTexts(
        string filePath,
        IReadOnlyList<string> roots,
        IReadOnlyDictionary<string, string[]>? tagTexts)
    {
        foreach (var name in FileAndFolderNames(filePath, roots))
        {
            yield return name;
        }

        if (tagTexts is null || tagTexts.Count == 0)
        {
            yield break;
        }

        if (!tagTexts.TryGetValue(filePath, out var texts))
        {
            var full = NormalizeFolder(filePath);
            if (full.Length == 0 || !tagTexts.TryGetValue(full, out texts))
            {
                yield break;
            }
        }

        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text))
            {
                yield return text;
            }
        }
    }

    private static IEnumerable<string> FileAndFolderNames(string filePath, IReadOnlyList<string> roots)
    {
        var name = Path.GetFileName(filePath);
        if (!string.IsNullOrEmpty(name))
        {
            yield return name;
        }

        string? folder;
        try
        {
            folder = Path.GetDirectoryName(filePath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
        {
            yield break;
        }

        while (!string.IsNullOrEmpty(folder))
        {
            var full = NormalizeFolder(folder);
            if (full.Length == 0 || !IsUnderRoot(full, roots))
            {
                yield break;
            }

            var folderName = Path.GetFileName(full);
            yield return string.IsNullOrEmpty(folderName) ? full : folderName;
            if (IsRoot(full, roots))
            {
                yield break;
            }

            try
            {
                folder = Path.GetDirectoryName(full);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                yield break;
            }
        }
    }

    private static void AddAncestors(
        string? folder,
        IReadOnlyList<string> roots,
        HashSet<string> visible)
    {
        while (!string.IsNullOrEmpty(folder))
        {
            var full = NormalizeFolder(folder);
            if (full.Length == 0 || !IsUnderRoot(full, roots))
            {
                return;
            }

            if (!visible.Add(full))
            {
                return;
            }

            if (IsRoot(full, roots))
            {
                return;
            }

            try
            {
                folder = Path.GetDirectoryName(full);
            }
            catch (Exception ex) when (ex is ArgumentException or PathTooLongException)
            {
                return;
            }
        }
    }

    public static string NormalizeFolder(string path)
    {
        if (PathUtil.TryGetFullPathTrimmedSeparators(path, out var full))
        {
            return full;
        }

        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : PathUtil.TrimDirectorySeparators(path);
    }

    private static bool IsRoot(string folder, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (folder.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 検索中のプレイリスト登録。見えているフォルダだけ降りる。
    /// ファイル名ヒットはそのファイルだけ。フォルダ名ヒットはそのフォルダ配下すべて。
    /// </summary>
    public static bool MatchesLibraryFile(
        string path,
        IReadOnlyList<string[]> groups,
        IReadOnlyList<string> roots,
        IReadOnlyDictionary<string, string[]>? tagTexts) =>
        TileSearchQuery.MatchesAny(LibraryFileTexts(path, roots, tagTexts), groups);

    public static string[] CollectPlaylistFiles(
        IReadOnlyList<string> folders,
        IReadOnlyList<string[]> groups,
        IEnumerable<string>? visibleFolders,
        IReadOnlyList<string>? roots = null,
        IReadOnlyDictionary<string, string[]>? tagTexts = null)
    {
        var visible = visibleFolders is null
            ? null
            : new HashSet<string>(
                visibleFolders.Select(NormalizeFolder).Where(path => path.Length > 0),
                StringComparer.OrdinalIgnoreCase);
        var walk = new LibraryExplorerPlaylistWalk(groups, visible, roots, tagTexts);
        var result = new List<string>();
        var remaining = new Stack<(string Path, bool AncestorHit)>();
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            if (PathUtil.TryGetFullPath(folders[i], out var full))
            {
                remaining.Push((full, false));
            }
        }

        while (remaining.Count > 0)
        {
            var (current, ancestorHit) = remaining.Pop();
            var folderHit = walk.FolderNameHit(current, ancestorHit);
            AudioCodec.CollectPlayerOpenableDirectoryLayer(current, out var files, out var children);
            foreach (var file in files)
            {
                if (walk.IncludeFile(file, folderHit))
                {
                    result.Add(file);
                }
            }

            for (var i = children.Length - 1; i >= 0; i--)
            {
                if (walk.IncludeChild(children[i], folderHit))
                {
                    remaining.Push((children[i], folderHit));
                }
            }
        }

        return [.. result];
    }

    private static bool IsUnderRoot(string folder, IReadOnlyList<string> roots)
    {
        foreach (var root in roots)
        {
            if (folder.Equals(root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (folder.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 検索中のツリーからプレイリストへ載せる範囲。空なら従来どおり配下すべて。
/// </summary>
internal sealed class LibraryExplorerPlaylistWalk
{
    public static LibraryExplorerPlaylistWalk Inactive { get; } = new([], visible: null);

    public LibraryExplorerPlaylistWalk(
        IReadOnlyList<string[]> groups,
        HashSet<string>? visible,
        IReadOnlyList<string>? roots = null,
        IReadOnlyDictionary<string, string[]>? tagTexts = null)
    {
        Groups = groups;
        Visible = visible;
        Roots = roots ?? [];
        TagTexts = tagTexts;
    }

    public IReadOnlyList<string[]> Groups { get; }

    public HashSet<string>? Visible { get; }

    public IReadOnlyList<string> Roots { get; }

    public IReadOnlyDictionary<string, string[]>? TagTexts { get; }

    public bool Active => Groups.Count > 0;

    public bool FolderNameHit(string directory, bool ancestorHit) =>
        ancestorHit || (Active && LibrarySearchQuery.MatchesFolderName(directory, Groups));

    public bool IncludeFile(string file, bool folderHit) =>
        !Active || folderHit || LibrarySearchQuery.MatchesLibraryFile(file, Groups, Roots, TagTexts);

    public bool IncludeChild(string child, bool folderHit)
    {
        if (!Active || folderHit)
        {
            return true;
        }

        return Visible is not null && Visible.Contains(LibrarySearchQuery.NormalizeFolder(child));
    }
}
