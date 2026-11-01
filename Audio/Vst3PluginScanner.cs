using System.IO;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>見つかった VST3（パスと名前だけ。読み込みはしない）。</summary>
internal sealed record Vst3PluginInfo(string Name, string Path, DateTimeOffset LastWriteUtc);

/// <summary>
/// VST3 の列挙。.vst3 はフォルダ（バンドル）でも単体ファイルでもよい。
/// プラグインはロードしない。設定のスキャンボタンからだけ呼ぶ（自動では走らせない）。
/// </summary>
internal static class Vst3PluginScanner
{
    public const string Extension = ".vst3";

    /// <summary>OS 既定の VST3 フォルダ。存在するものだけ。</summary>
    public static string[] DefaultSearchRoots()
    {
        var candidates = new[]
        {
            Combine(Environment.SpecialFolder.CommonProgramFiles, "VST3"),
            Combine(Environment.SpecialFolder.CommonProgramFilesX86, "VST3"),
            Combine(Environment.SpecialFolder.LocalApplicationData, "Programs", "Common", "VST3"),
            Combine(Environment.SpecialFolder.UserProfile, "Documents", "VST3"),
        };

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in candidates)
        {
            if (!PathUtil.TryGetFullPathTrimmedSeparators(path, out var full)
                || !seen.Add(full)
                || !SafeDirectoryExists(full))
            {
                continue;
            }

            result.Add(full);
        }

        return [.. result];
    }

    /// <summary>
    /// roots 配下を再帰で探す。roots が空なら <see cref="DefaultSearchRoots"/>。
    /// 読めないフォルダは飛ばして続行。結果は名前順、フルパスで重複なし。
    /// </summary>
    public static IReadOnlyList<Vst3PluginInfo> Scan(
        IEnumerable<string>? roots,
        CancellationToken cancellationToken = default) =>
        Scan(roots, rootProgress: null, cancellationToken);

    /// <summary>rootProgress には探し終えたルートの割合（0〜1）を渡す。</summary>
    private static IReadOnlyList<Vst3PluginInfo> Scan(
        IEnumerable<string>? roots,
        Action<double>? rootProgress,
        CancellationToken cancellationToken)
    {
        var searchRoots = roots?.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray() ?? [];
        if (searchRoots.Length == 0)
        {
            searchRoots = DefaultSearchRoots();
        }

        var found = new List<Vst3PluginInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < searchRoots.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanRoot(searchRoots[i], found, seen, cancellationToken);
            rootProgress?.Invoke((i + 1) / (double)searchRoots.Length);
        }

        found.Sort(static (a, b) =>
        {
            var byName = StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
            return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
        });
        return found;
    }

    private static void ScanRoot(
        string raw,
        List<Vst3PluginInfo> found,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        if (!PathUtil.TryGetFullPathTrimmedSeparators(raw, out var root))
        {
            return;
        }

        if (IsVst3Name(root))
        {
            // ルート自体が .vst3 を指している。
            TryAdd(root, found, seen);
            return;
        }

        if (SafeDirectoryExists(root))
        {
            Walk(root, found, seen, cancellationToken);
        }
    }

    /// <summary>進捗のうち列挙に充てる割合。残りは付帯情報の読み取り（1 件ごと）。</summary>
    public const double EnumerateProgressShare = 0.15;

    /// <summary><see cref="Scan"/> のあと、各プラグインの付帯情報を読む（ロードはしない）。</summary>
    public static IReadOnlyList<Vst3ScannedPlugin> ScanWithMetadata(
        IEnumerable<string>? roots,
        IProgress<Vst3ScanProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool useSteinbergHostCache = false)
    {
        progress?.Report(new Vst3ScanProgress(0, 0, 0));
        var found = Scan(
            roots,
            progress is null ? null : f => progress.Report(new Vst3ScanProgress(f * EnumerateProgressShare, 0, 0)),
            cancellationToken);
        var enrichProgress = progress is null
            ? null
            : new MappedProgress(progress, p => p with
            {
                Fraction = EnumerateProgressShare + (1 - EnumerateProgressShare) * p.Fraction,
            });
        return Vst3PluginMetadata.Enrich(
            found,
            enrichProgress,
            cancellationToken,
            useSteinbergHostCache);
    }

    /// <summary>Progress&lt;T&gt; と違い同期で渡す（UI スレッドへの転送は受け手側の Progress に任せる）。</summary>
    private sealed class MappedProgress(IProgress<Vst3ScanProgress> inner, Func<Vst3ScanProgress, Vst3ScanProgress> map)
        : IProgress<Vst3ScanProgress>
    {
        public void Report(Vst3ScanProgress value) => inner.Report(map(value));
    }

    public static bool IsVst3Name(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    private static void Walk(
        string root,
        List<Vst3PluginInfo> found,
        HashSet<string> seen,
        CancellationToken cancellationToken)
    {
        // 再帰はスタックで。シンボリックリンク／ジャンクションは辿らない（循環防止）。
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var dir = pending.Pop();
            IEnumerable<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos().ToArray();
            }
            catch (Exception ex) when (IsSkippable(ex))
            {
                continue;
            }

            foreach (var entry in entries)
            {
                try
                {
                    var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                    if (IsVst3Name(entry.Name))
                    {
                        // バンドル内部（Contents\x86_64-win\*.vst3）は辿らない。
                        TryAdd(entry, found, seen);
                        continue;
                    }

                    if (isDirectory && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    {
                        pending.Push(entry.FullName);
                    }
                }
                catch (Exception ex) when (IsSkippable(ex))
                {
                }
            }
        }
    }

    private static void TryAdd(string path, List<Vst3PluginInfo> found, HashSet<string> seen)
    {
        try
        {
            FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
            if (info.Exists)
            {
                TryAdd(info, found, seen);
            }
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
        }
    }

    private static void TryAdd(FileSystemInfo entry, List<Vst3PluginInfo> found, HashSet<string> seen)
    {
        var full = PathUtil.TrimDirectorySeparators(entry.FullName);
        if (!seen.Add(full))
        {
            return;
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(full);
        DateTimeOffset lastWrite;
        try
        {
            lastWrite = new DateTimeOffset(entry.LastWriteTimeUtc, TimeSpan.Zero);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            lastWrite = DateTimeOffset.MinValue;
        }

        found.Add(new Vst3PluginInfo(string.IsNullOrEmpty(name) ? full : name, full, lastWrite));
    }

    private static string Combine(Environment.SpecialFolder folder, params string[] parts)
    {
        var basePath = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(basePath)
            ? string.Empty
            : System.IO.Path.Combine([basePath, .. parts]);
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (IsSkippable(ex))
        {
            return false;
        }
    }

    private static bool IsSkippable(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException
            or ArgumentException or NotSupportedException;
}
