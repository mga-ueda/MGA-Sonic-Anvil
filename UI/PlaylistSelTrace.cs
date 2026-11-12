#if PLAYLIST_SEL_TRACE
using System.Diagnostics;
using System.IO;
using System.Text;
#endif
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイリスト選択が上へ戻る不具合の一時診断。
/// 有効化: プロジェクト／ファイルに <c>PLAYLIST_SEL_TRACE</c> を定義する。
/// 出力: %TEMP%\mga-sonic-anvil-playlist-sel.log
/// </summary>
internal static class PlaylistSelTrace
{
#if PLAYLIST_SEL_TRACE
    public const bool Enabled = true;
#else
    public const bool Enabled = false;
#endif

#if PLAYLIST_SEL_TRACE
    private static readonly object Gate = new();
    private static readonly string Path =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "mga-sonic-anvil-playlist-sel.log");

    private static int _seq;
    private static int _lastCaretIndex = -1;
    private static string? _lastCaretName;
    private static bool _started;

    public static string LogPath => Path;
#else
    public static string LogPath => string.Empty;
#endif

    public static void Reset()
    {
#if PLAYLIST_SEL_TRACE
        lock (Gate)
        {
            try
            {
                File.WriteAllText(
                    Path,
                    $"# playlist-sel trace start {DateTime.Now:O}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
            }

            _seq = 0;
            _lastCaretIndex = -1;
            _lastCaretName = null;
            _started = true;
        }

        Write("RESET", "log cleared");
#endif
    }

    public static void Write(string tag, string detail)
    {
#if PLAYLIST_SEL_TRACE
        EnsureStarted();
        var n = Interlocked.Increment(ref _seq);
        var line =
            $"{DateTime.Now:HH:mm:ss.fff}\t{n:D5}\t{tag}\t{detail}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                File.AppendAllText(Path, line, Encoding.UTF8);
            }
            catch
            {
            }
        }

        Debug.WriteLine($"[PlaylistSel] {tag} {detail}");
#else
        _ = tag;
        _ = detail;
#endif
    }

#if PLAYLIST_SEL_TRACE
    private static void EnsureStarted()
    {
        if (_started)
        {
            return;
        }

        lock (Gate)
        {
            if (_started)
            {
                return;
            }

            try
            {
                File.WriteAllText(
                    Path,
                    $"# playlist-sel trace start {DateTime.Now:O}{Environment.NewLine}",
                    Encoding.UTF8);
            }
            catch
            {
            }

            _started = true;
        }
    }
#endif

    public static string NameOf(DocumentSession? session)
    {
        if (session is null)
        {
            return "-";
        }

        var path = session.Document.SourcePath;
        return string.IsNullOrEmpty(path) ? "?" : System.IO.Path.GetFileName(path);
    }

    public static void NoteCaret(int index, DocumentSession? session, string via)
    {
#if PLAYLIST_SEL_TRACE
        var name = NameOf(session);
        if (_lastCaretIndex >= 0
            && index >= 0
            && index < _lastCaretIndex)
        {
            Write(
                "JUMP_UP",
                $"via={via} from={_lastCaretIndex}:{_lastCaretName} to={index}:{name}");
            Write("JUMP_STACK", Environment.StackTrace.Replace('\r', ' ').Replace('\n', '|'));
        }

        _lastCaretIndex = index;
        _lastCaretName = name;
        Write("CARET", $"via={via} idx={index} name={name}");
#else
        _ = index;
        _ = session;
        _ = via;
#endif
    }
}
