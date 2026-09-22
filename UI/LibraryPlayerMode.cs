using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>F10 プレイヤーモードで拒否する編集系コマンドと、リストから通すキー。</summary>
internal static class LibraryPlayerMode
{
    /// <summary>プレイヤーでは波形の右クリック／メニューキーを出さない。</summary>
    public static bool HidesWaveformContextMenu(bool playerMode) => playerMode;

    /// <summary>プレイヤーではマーカー／リージョン／ループを波形に出さない。</summary>
    public static bool ShowsCueOverlays(bool playerMode) => !playerMode;

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

        if (key is Key.Escape or Key.F9 or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
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

        if ((key is Key.Left or Key.Right)
            && modifiers is ModifierKeys.None or ModifierKeys.Control)
        {
            return true;
        }

        if ((key is Key.Up or Key.Down) && modifiers == ModifierKeys.Control)
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
            (Key.R, ModifierKeys.None) => true,
            (Key.A, ModifierKeys.Control) => true,
            (Key.F, ModifierKeys.Control) => true,
            (Key.S, ModifierKeys.Alt) => true,
            (Key.O, ModifierKeys.Control) => true,
            (Key.O, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Q, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Z, ModifierKeys.Control) => true,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Tab, ModifierKeys.Control) => true,
            (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };
    }

    /// <summary>
    /// ランダム再生の切替。プレイリストが空でも、ツリー／お気に入りフォーカスでも効く。
    /// 検索欄では文字入力のまま。
    /// </summary>
    public static bool IsShuffleToggle(Key key, ModifierKeys modifiers) =>
        key == Key.R && modifiers == ModifierKeys.None;

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
    /// フォルダツリーで拒否するキー（切り取り／貼り付け／削除／リネーム等）。コピーとナビと Enter 以外はここで止める。
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

        // 削除・バックスペース・切り取り／貼り付け・複製などファイル操作系は止める。Ctrl+C はコピーとして通す。
        // F2 はプレイヤーでお気に入りフォーカスに使うので、ここでは止めない。
        if (key is Key.Delete or Key.Back)
        {
            return true;
        }

        if ((modifiers & ModifierKeys.Control) != 0
            && key is Key.X or Key.V or Key.D or Key.A or Key.N or Key.R)
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
    /// プレイヤーの完成ピークをそのまま出してよいか。エディタではチャンネル数と粒度が要る。
    /// </summary>
    public static bool CanReusePeaks(bool playerMode, AudioDocument document)
    {
        if (document.Peaks.IsEmpty || document.Peaks.IsBuilding)
        {
            return false;
        }

        return playerMode || !document.Peaks.NeedsEditorRebuild(document.Channels);
    }

    /// <summary>
    /// 既に PCM があるファイルだけピークを作り直す。ストリームはフル Load が兼ねる。
    /// プレイヤー包絡（モノラル）のままだと、エディタでレーンが 1 本しか出ない。
    /// </summary>
    public static bool NeedsEditorPeakUpgrade(AudioDocument document) =>
        !NeedsEditorPcmUpgrade(document) && !CanReusePeaks(playerMode: false, document);

    /// <summary>プレイヤーに入ったときに選んで再生する先頭のファイル。</summary>
    public static DocumentSession? FirstSession(IReadOnlyList<DocumentSession> sessions) =>
        sessions.Count == 0 ? null : sessions[0];

    /// <summary>
    /// プレイヤーではプレイリストが空のときレベルメーター／スペアナ／ラウドネス／ゴニオ／サラウンドを出さない。
    /// エディタでは常に出す。
    /// </summary>
    public static bool ShowsPlayerMeters(bool playerMode, int playlistCount) =>
        !playerMode || playlistCount > 0;

    /// <summary>
    /// 音声信号で動くメーターはフェードせず即出す。停止中の追加だけ 1 秒フェード。
    /// </summary>
    public static bool InstantPlayerMeterReveal(bool show, bool signalMoving) =>
        show && signalMoving;

    /// <summary>
    /// プレイヤー突入は消えた状態から。空リストで一度出してフェードアウトしない。
    /// 再生中の即表示は先に消さない。
    /// </summary>
    public static bool SnapPlayerMetersHiddenOnEnter(bool enteringPlayer, bool instantReveal) =>
        enteringPlayer && !instantReveal;

    /// <summary>メーターのフェード。ジャケットのクロスフェードと同じ 1 秒。</summary>
    public const double MeterFadeSeconds = 1;

    public const int MeterFadeFrameRate = 30;

    public static DoubleAnimation CreateMeterFade(double from, double to)
    {
        var rising = to > from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(MeterFadeSeconds),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = rising ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        Timeline.SetDesiredFrameRate(anim, MeterFadeFrameRate);
        return anim;
    }

    public static int EdgeIndex(int count, int edge) =>
        count <= 0 ? -1 : edge < 0 ? 0 : count - 1;

    /// <summary>
    /// プレイヤーのピーク走査を残す曲。表示中と次曲先読みだけ。
    /// 世代を進めてまとめて止めると、今の波形が途中のまま切れる。
    /// </summary>
    public static bool KeepPeakJob(AudioDocument document, AudioDocument? playing, AudioDocument? next)
    {
        if (playing is not null && ReferenceEquals(document, playing))
        {
            return true;
        }

        return next is not null && ReferenceEquals(document, next);
    }

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

    /// <summary>
    /// 押しっぱなしのリピートは Background タイマーだと、描画が重いと Tick が落ちる。
    /// Send なら CompositionTarget.Rendering より先に進む。
    /// </summary>
    public const DispatcherPriority SeekNudgeTimerPriority = DispatcherPriority.Send;

    /// <summary>遅延した Tick で一度に足す上限。止めすぎず、溜め込みすぎない。</summary>
    public const int SeekNudgeCatchUpMax = 3;

    public static int SeekNudgeCatchUpSteps(long elapsedMs, bool repeatStarted)
    {
        var interval = SeekNudgeTimerIntervalMs(repeatStarted);
        if (elapsedMs < interval || interval <= 0)
        {
            return 1;
        }

        return (int)Math.Clamp(elapsedMs / interval, 1, SeekNudgeCatchUpMax);
    }

    public static double SeekNudgeCatchUpSeconds(long elapsedMs, bool repeatStarted) =>
        SeekNudgeSeconds * SeekNudgeCatchUpSteps(elapsedMs, repeatStarted);

    /// <summary>
    /// 再生ヘッド同期の最短間隔。144Hz でも 60fps 相当に抑え、7／9 のタイマーを飢餓させない。
    /// </summary>
    public const int PlaybackVisualMinIntervalMs = 16;

    /// <summary>
    /// スペアナ／ゴニオ／ラウドネスは CompositionTarget.Rendering（Render）に載せない。
    /// Render は Input より先なので、重いとマウスとキーがワンテンポ遅れる。
    /// </summary>
    public const DispatcherPriority AnalyzerTickPriority = DispatcherPriority.Background;

    /// <summary>再生ヘッドが同じピクセルに居るときの再描画間隔。残光の減衰用。</summary>
    public const int PlayheadIdleInvalidateMs = 50;

    public const double PlayheadMoveEpsilonPx = 0.5;

    public static bool ShouldRefreshPlayheadPaint(double lastX, long lastAtMs, double nextX, long nowMs)
    {
        if (double.IsNaN(lastX) || Math.Abs(nextX - lastX) >= PlayheadMoveEpsilonPx)
        {
            return true;
        }

        return nowMs - lastAtMs >= PlayheadIdleInvalidateMs;
    }

    public static bool PlayerNumpadRepeatIsHold(LibraryNumpadCommand command) =>
        command is LibraryNumpadCommand.Rewind
            or LibraryNumpadCommand.FastForward
            or LibraryNumpadCommand.SeekBack
            or LibraryNumpadCommand.SeekForward;

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

    /// <summary>
    /// Ctrl+←／↑ ライブラリ、Ctrl+→ プレイリスト、Ctrl+↓ お気に入り。
    /// エディタのマーカー移動／ズームは引き継がない。
    /// </summary>
    public static bool IsPaneFocusArrow(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control
        && key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>
    /// Ctrl+F の行き先。プレイリストならリスト検索、ツリー／お気に入りはライブラリ検索。
    /// </summary>
    public static LibraryPane SearchPane(LibraryPane current) =>
        current == LibraryPane.List ? LibraryPane.List : LibraryPane.Explorer;

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

    /// <summary>
    /// IME が上段／テンキーの数字を ImeProcessed にした場合、割合ジャンプとテンキー操作へ戻す。
    /// かな入力の文字キーはそのまま（A などのショートカットにしない）。
    /// </summary>
    public static Key ResolveDigitKey(Key key, Key imeProcessedKey) =>
        key == Key.ImeProcessed && IsDigitKey(imeProcessedKey) ? imeProcessedKey : key;

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
