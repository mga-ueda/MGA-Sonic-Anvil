using System.Windows.Input;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>F10 プレイヤーモードで拒否する編集系コマンドと、リストから通すキー。</summary>
internal static class LibraryPlayerMode
{
    /// <summary>プレイヤーでは波形の右クリック／メニューキーを出さない。</summary>
    public static bool HidesWaveformContextMenu(bool playerMode) => playerMode;

    public static bool BlocksWaveMenu(WaveMenuCommand command) => command switch
    {
        WaveMenuCommand.PlayFromHere
            or WaveMenuCommand.SeekHere
            or WaveMenuCommand.SelectSpanHere
            or WaveMenuCommand.SelectAll
            or WaveMenuCommand.ClearSelection
            or WaveMenuCommand.SelectToStart
            or WaveMenuCommand.SelectToEnd
            or WaveMenuCommand.TogglePlayback
            or WaveMenuCommand.PauseHere
            or WaveMenuCommand.Preroll
            or WaveMenuCommand.Restart
            or WaveMenuCommand.LoopPlay
            or WaveMenuCommand.GoStart
            or WaveMenuCommand.GoEnd
            or WaveMenuCommand.ViewLeft
            or WaveMenuCommand.ViewRight
            or WaveMenuCommand.MaximizeWaveform
            or WaveMenuCommand.MaximizeAnalyzers
            or WaveMenuCommand.MaximizeLibrary
            or WaveMenuCommand.ViewWaveform
            or WaveMenuCommand.SilentSkip
            or WaveMenuCommand.AlwaysOnTop
            or WaveMenuCommand.Open
            or WaveMenuCommand.Settings
            or WaveMenuCommand.Quit
            or WaveMenuCommand.ReopenTab
            or WaveMenuCommand.NextTab
            or WaveMenuCommand.PrevTab
            or WaveMenuCommand.CopyAllTabTimes
            or WaveMenuCommand.Tips
            or WaveMenuCommand.Manual
            or WaveMenuCommand.GitHub
            => false,
        _ => true,
    };

    public static bool BlocksTransport(TransportCommand command) => command switch
    {
        TransportCommand.TogglePlayback
            or TransportCommand.Stop
            or TransportCommand.JumpToTime
            or TransportCommand.GoToStart
            or TransportCommand.PreviousPage
            or TransportCommand.PreviousMarker
            or TransportCommand.NextMarker
            or TransportCommand.NextPage
            or TransportCommand.GoToEnd
            or TransportCommand.Open
            or TransportCommand.ToggleUiTheme
            or TransportCommand.OpenColorPanel
            or TransportCommand.ToggleTips
            or TransportCommand.OpenSettings
            or TransportCommand.OpenManual
            or TransportCommand.ToggleLibraryMaximize
            or TransportCommand.ToggleAnalyzerMaximize
            => false,
        _ => true,
    };

    /// <summary>
    /// プレイヤーで通すキー。↑↓・Home／End・PageUp／PageDown はファイル選択として別処理するので含めない。
    /// Delete はリスト除外として別処理。Ctrl+W は閉じない（リスト除外も Delete のみ）。
    /// </summary>
    public static bool AllowsKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
        {
            return true;
        }

        if (key is Key.Escape or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3)
        {
            return true;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return true;
        }

        if (key == Key.F4 && (modifiers & ModifierKeys.Alt) != 0)
        {
            return true;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Alt)
        {
            return true;
        }

        if ((key is Key.Left or Key.Right) && (modifiers & ModifierKeys.Alt) == 0)
        {
            return true;
        }

        if ((key is Key.Home or Key.End or Key.PageUp or Key.PageDown)
            && (modifiers & ModifierKeys.Control) != 0
            && (modifiers & ModifierKeys.Alt) == 0)
        {
            return true;
        }

        if (IsDigitKey(key) && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return true;
        }

        return (key, modifiers) switch
        {
            (Key.Space, ModifierKeys.None or ModifierKeys.Control) => true,
            (Key.Enter, ModifierKeys.None or ModifierKeys.Alt) => true,
            (Key.L, ModifierKeys.None) => true,
            (Key.A, ModifierKeys.Control) => true,
            (Key.S, ModifierKeys.Alt) => true,
            (Key.O, ModifierKeys.Control) => true,
            (Key.O, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Q, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Tab, ModifierKeys.Control) => true,
            (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };
    }

    /// <summary>ツリーが左右を使う。再生中の早送り／巻き戻しにはしない。</summary>
    public static bool ExplorerOwnsHorizontal(Key key, ModifierKeys modifiers) =>
        key is Key.Left or Key.Right && modifiers == ModifierKeys.None;

    /// <summary>エクスプローラーの *。テンキーと Shift+8。</summary>
    public static bool IsExplorerExpandAll(Key key, ModifierKeys modifiers) =>
        (key == Key.Multiply && modifiers == ModifierKeys.None)
        || (key == Key.D8 && modifiers == ModifierKeys.Shift);

    /// <summary>エクスプローラーの /。テンキーと Oem2（日本語キーボードの /）。</summary>
    public static bool IsExplorerCollapseSubtree(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Divide or Key.Oem2;

    /// <summary>
    /// フォルダツリーで拒否するキー（コピー／削除／リネーム等）。ナビと Enter 以外はここで止める。
    /// </summary>
    public static bool BlocksExplorerKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
        {
            return false;
        }

        // 設定 (Ctrl+Shift+O) など AllowsKey のアプリ共通ショートカットはツリーからでも通す。
        if (AllowsKey(key, modifiers)
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0)
        {
            return false;
        }

        if (modifiers is ModifierKeys.None or ModifierKeys.Shift
            && key is Key.Up or Key.Down or Key.Left or Key.Right
                or Key.Home or Key.End or Key.PageUp or Key.PageDown
                or Key.Add or Key.Subtract or Key.OemPlus or Key.OemMinus)
        {
            return false;
        }

        if (IsExplorerExpandAll(key, modifiers) || IsExplorerCollapseSubtree(key, modifiers))
        {
            return false;
        }

        if (key == Key.Enter && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return false;
        }

        // アプリ共通のモード切替・終了、プレイヤーのペイン切替はツリーからでも通す。
        if (key is Key.Escape or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return false;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3)
        {
            return false;
        }

        if (key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return false;
        }

        if (key == Key.F4 && (modifiers & ModifierKeys.Alt) != 0)
        {
            return false;
        }

        if (key == Key.Space && modifiers == ModifierKeys.Alt)
        {
            return false;
        }

        if ((key == Key.Q && modifiers == ModifierKeys.Control)
            || (key == Key.S && (modifiers & ModifierKeys.Alt) != 0))
        {
            return false;
        }

        // 削除・バックスペース・コピー／切り取り／貼り付け・複製などファイル操作系はすべて止める。
        // F2 はプレイヤーでお気に入りフォーカスに使うので、ここでは止めない。
        if (key is Key.Delete or Key.Back)
        {
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0
            && key is Key.C or Key.X or Key.V or Key.D or Key.A or Key.N or Key.R)
        {
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0
            && (modifiers & ModifierKeys.Shift) != 0
            && key is Key.D or Key.N or Key.C or Key.V)
        {
            return true;
        }

        // 文字キーのタイプアヘッドは許可（フォルダ名ジャンプ）。上記以外の修飾付きは止める。
        if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// プレイヤーを抜けるときに残すセッション。リスト選択があればそれ、なければ今のファイル。
    /// </summary>
    public static DocumentSession[] SessionsToKeep(
        IReadOnlyCollection<DocumentSession> selected,
        DocumentSession? active)
    {
        if (selected.Count > 0)
        {
            var copy = new DocumentSession[selected.Count];
            var i = 0;
            foreach (var session in selected)
            {
                copy[i++] = session;
            }

            return copy;
        }

        return active is null ? [] : [active];
    }

    public static DocumentSession[] SessionsToDrop(
        IReadOnlyList<DocumentSession> all,
        IReadOnlyCollection<DocumentSession> keep)
    {
        if (keep.Count == 0)
        {
            return all.Count == 0 ? [] : [.. all];
        }

        var kept = new HashSet<DocumentSession>(keep);
        var drop = new List<DocumentSession>();
        foreach (var session in all)
        {
            if (!kept.Contains(session))
            {
                drop.Add(session);
            }
        }

        return [.. drop];
    }

    /// <summary>エディタ復帰でフル PCM が必要なストリーム／遅延読み込み。</summary>
    public static bool NeedsEditorPcmUpgrade(AudioDocument document) =>
        document.IsDeferredLoad || document.IsStreamPlayback;

    /// <summary>
    /// 既に PCM があるファイルだけピークを作り直す。ストリームはフル Load が兼ねる。
    /// </summary>
    public static bool NeedsEditorPeakUpgrade(AudioDocument document) =>
        !NeedsEditorPcmUpgrade(document);

    /// <summary>プレイヤーに入ったときに選んで再生する先頭のファイル。</summary>
    public static DocumentSession? FirstSession(IReadOnlyList<DocumentSession> sessions) =>
        sessions.Count == 0 ? null : sessions[0];

    public static int EdgeIndex(int count, int edge) =>
        count <= 0 ? -1 : edge < 0 ? 0 : count - 1;

    /// <summary>曲が終わった次。末尾の次は先頭。今の曲が見つからなければ先頭。空なら -1。</summary>
    public static int NextLoopIndex(int count, int current)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current < 0 || current >= count - 1)
        {
            return 0;
        }

        return current + 1;
    }

    /// <summary>曲が戻る前。先頭の前は末尾。今の曲が見つからなければ末尾。空なら -1。</summary>
    public static int PreviousLoopIndex(int count, int current)
    {
        if (count <= 0)
        {
            return -1;
        }

        if (current <= 0 || current >= count)
        {
            return count - 1;
        }

        return current - 1;
    }

    /// <summary>プレイヤーのスキップ秒。テンキー 7／9。</summary>
    public const double SeekNudgeSeconds = 5;

    /// <summary>スキップ時のクロスフェード。推移元のフェードアウトと同時に、推移先をフェードイン（0.75 秒）。</summary>
    public const int SeekNudgeFadeMilliseconds = 750;

    /// <summary>7／9 押しっぱなしの最初のリピートまでの待ち。短い押しの誤リピートを防ぐ。</summary>
    public const int SeekNudgeRepeatDelayMs = 250;

    /// <summary>待ちのあとの 7／9 リピート間隔。</summary>
    public const int SeekNudgeRepeatIntervalMs = 200;

    public static int SeekNudgeTimerIntervalMs(bool repeatStarted) =>
        repeatStarted ? SeekNudgeRepeatIntervalMs : SeekNudgeRepeatDelayMs;

    public static bool IsPlayerNumpadKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is >= Key.NumPad0 and <= Key.NumPad9;

    public static bool IsPlayerShuttleKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.NumPad1 or Key.NumPad3;

    public static bool IsPlayerSeekNudgeKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.NumPad7 or Key.NumPad9;

    public static LibraryNumpadCommand PlayerNumpadCommand(Key key, ModifierKeys modifiers)
    {
        if (!IsPlayerNumpadKey(key, modifiers))
        {
            return LibraryNumpadCommand.None;
        }

        return key switch
        {
            Key.NumPad0 => LibraryNumpadCommand.PlayPause,
            Key.NumPad1 => LibraryNumpadCommand.Rewind,
            Key.NumPad3 => LibraryNumpadCommand.FastForward,
            Key.NumPad4 => LibraryNumpadCommand.PreviousTrack,
            Key.NumPad5 => LibraryNumpadCommand.Restart,
            Key.NumPad6 => LibraryNumpadCommand.NextTrack,
            Key.NumPad7 => LibraryNumpadCommand.SeekBack,
            Key.NumPad9 => LibraryNumpadCommand.SeekForward,
            _ => LibraryNumpadCommand.None,
        };
    }

    public static int PageStep(int visibleRows) =>
        Math.Max(1, visibleRows - 1);

    public static bool IsPaneCycleKey(Key key, ModifierKeys modifiers) =>
        key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    /// <summary>Tab：ツリー → お気に入り → プレイリスト → ツリー。Shift+Tab は逆。</summary>
    public static LibraryPane NextPane(LibraryPane current, bool reverse) =>
        reverse
            ? current switch
            {
                LibraryPane.List => LibraryPane.Favorites,
                LibraryPane.Favorites => LibraryPane.Explorer,
                _ => LibraryPane.List,
            }
            : current switch
            {
                LibraryPane.Explorer => LibraryPane.Favorites,
                LibraryPane.Favorites => LibraryPane.List,
                _ => LibraryPane.Explorer,
            };

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.CapsLock or Key.NumLock or Key.Scroll;

    private static bool IsDigitKey(Key key) =>
        key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9;
}

internal enum LibraryPane
{
    Explorer,
    Favorites,
    List,
}

internal enum LibraryNumpadCommand
{
    None,
    PlayPause,
    Restart,
    PreviousTrack,
    NextTrack,
    SeekBack,
    SeekForward,
    Rewind,
    FastForward,
}
