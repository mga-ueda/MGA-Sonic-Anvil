using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイヤーのツリー／お気に入り／プレイリストから、実ファイルをコピーして渡す。
/// クリップボードへ置くときはコピー（移動ではない）を明示する。
/// </summary>
internal static class LibraryShellFiles
{
    internal const string PreferredDropEffectFormat = "Preferred DropEffect";

    /// <summary>DROPEFFECT_COPY = 1。2 は DROPEFFECT_MOVE（FO_COPY と取り違えない）。</summary>
    internal const int DropEffectCopy = 1;

    /// <summary>DROPEFFECT_MOVE。Preferred DropEffect にこれを書くと Explorer は移動する。</summary>
    internal const int DropEffectMove = 2;

    public static string[] ExistingPaths(IEnumerable<string?> paths)
    {
        if (paths is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in paths)
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

    public static DataObject? CreateCopyData(IEnumerable<string?> paths)
    {
        var existing = ExistingPaths(paths);
        if (existing.Length == 0)
        {
            return null;
        }

        var files = new StringCollection();
        files.AddRange(existing);
        var data = new DataObject();
        data.SetFileDropList(files);
        data.SetData(PreferredDropEffectFormat, CreateCopyEffectStream(), autoConvert: false);
        return data;
    }

    /// <summary>ドラッグ／クリップボード用。OLE が Preferred DropEffect を読める。</summary>
    public static object? CreateOleCopyData(IEnumerable<string?> paths) =>
        LibraryCopyDropDataObject.FromPaths(paths);

    internal static MemoryStream CreateCopyEffectStream()
    {
        var stream = new MemoryStream(4);
        stream.WriteByte(DropEffectCopy);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// explorer.exe の引数。フォルダはその場を開く。ファイルは親フォルダで選択。
    /// 同じ場所は一度だけ。
    /// </summary>
    public static string[] ExplorerArguments(IEnumerable<string?> paths)
    {
        var existing = ExistingPaths(paths);
        if (existing.Length == 0)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var path in existing)
        {
            if (Directory.Exists(path))
            {
                if (seen.Add(Normalize(path)))
                {
                    result.Add(Quote(path));
                }

                continue;
            }

            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent) || !seen.Add(Normalize(parent)))
            {
                continue;
            }

            result.Add("/select," + Quote(path));
        }

        return [.. result];
    }

    public static bool TryOpenInExplorer(IEnumerable<string?> paths) =>
        TryOpenInExplorer(paths, StartExplorer);

    internal static bool TryOpenInExplorer(IEnumerable<string?> paths, Func<string, bool> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        var started = false;
        foreach (var arguments in ExplorerArguments(paths))
        {
            if (start(arguments))
            {
                started = true;
            }
        }

        return started;
    }

    internal static string Quote(string path) => "\"" + path + "\"";

    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool StartExplorer(string arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }
}
