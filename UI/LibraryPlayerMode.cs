using System.Windows.Input;

namespace MgaSonicAnvil.UI;

/// <summary>F10 プレイヤーモードで拒否する編集系コマンドと、リストから通すキー。</summary>
internal static class LibraryPlayerMode
{
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
            or WaveMenuCommand.CenterPlayhead
            or WaveMenuCommand.CenterLock
            or WaveMenuCommand.MaximizeWaveform
            or WaveMenuCommand.MaximizeAnalyzers
            or WaveMenuCommand.MaximizeLibrary
            or WaveMenuCommand.ViewWaveform
            or WaveMenuCommand.SilentSkip
            or WaveMenuCommand.AlwaysOnTop
            or WaveMenuCommand.Open
            or WaveMenuCommand.Save
            or WaveMenuCommand.SaveAs
            or WaveMenuCommand.SaveMp3
            or WaveMenuCommand.Settings
            or WaveMenuCommand.Quit
            or WaveMenuCommand.CloseTab
            or WaveMenuCommand.CloseOthers
            or WaveMenuCommand.CloseTabsRight
            or WaveMenuCommand.CloseTabsLeft
            or WaveMenuCommand.CloseAll
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
            or TransportCommand.CenterPlayhead
            or TransportCommand.Open
            or TransportCommand.Save
            or TransportCommand.SaveAs
            or TransportCommand.SaveMp3
            or TransportCommand.ToggleUiTheme
            or TransportCommand.OpenColorPanel
            or TransportCommand.ToggleTips
            or TransportCommand.OpenSettings
            or TransportCommand.OpenManual
            => false,
        _ => true,
    };

    /// <summary>
    /// プレイヤーで通すキー。↑↓・Home／End・PageUp／PageDown はファイル選択として別処理するので含めない。
    /// Delete はリスト除外として別処理するので含めない（波形削除へ落とさない）。
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
            (Key.Z or Key.OemPeriod or Key.Decimal, ModifierKeys.None) => true,
            (Key.L, ModifierKeys.None) => true,
            (Key.A, ModifierKeys.Control) => true,
            (Key.W, ModifierKeys.Control) => true,
            (Key.W, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.S, ModifierKeys.Alt or ModifierKeys.Control) => true,
            (Key.S, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.O, ModifierKeys.Control) => true,
            (Key.O, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.M, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Q, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Tab, ModifierKeys.Control) => true,
            (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };
    }

    /// <summary>
    /// フォルダツリーで拒否するキー（コピー／削除／リネーム等）。ナビと Enter 以外はここで止める。
    /// </summary>
    public static bool BlocksExplorerKey(Key key, ModifierKeys modifiers)
    {
        if (IsModifierOnly(key))
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

        if (key == Key.Enter && modifiers == ModifierKeys.None)
        {
            return false;
        }

        // アプリ共通のモード切替・終了などはツリーからでも通す。
        if (key is Key.Escape or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
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

        // 削除・リネーム・コピー／切り取り／貼り付け・複製などファイル操作系はすべて止める。
        if (key is Key.Delete or Key.Back or Key.F2)
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

    /// <summary>プレイヤーに入ったときに選んで再生する先頭のファイル。</summary>
    public static DocumentSession? FirstSession(IReadOnlyList<DocumentSession> sessions) =>
        sessions.Count == 0 ? null : sessions[0];

    public static int EdgeIndex(int count, int edge) =>
        count <= 0 ? -1 : edge < 0 ? 0 : count - 1;

    public static int PageStep(int visibleRows) =>
        Math.Max(1, visibleRows - 1);

    private static bool IsModifierOnly(Key key) =>
        key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.CapsLock or Key.NumLock or Key.Scroll;

    private static bool IsDigitKey(Key key) =>
        key is >= Key.D0 and <= Key.D9 or >= Key.NumPad0 and <= Key.NumPad9;
}
