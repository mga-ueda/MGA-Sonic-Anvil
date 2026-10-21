using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>F10 プレイヤーモードで拒否する編集系コマンドと、リストから通すキー。</summary>
internal static class LibraryPlayerMode
{
    /// <summary>プレイヤーでも波形メニューを出す（編集系は <see cref="BlocksWaveMenu"/> で拒否）。</summary>
#pragma warning disable IDE0060 // 呼び出し側の名前付き引数 playerMode を残す
    public static bool HidesWaveformContextMenu(bool playerMode) => false;
#pragma warning restore IDE0060

    /// <summary>プレイヤーではドキュメントのマーカー／リージョン／ループを波形に出さない。</summary>
    public static bool ShowsCueOverlays(bool playerMode) => !playerMode;

    /// <summary>
    /// 捨て A-B／マーカーを波形に出すか。
    /// 音声のプレイリスト波形、動画本再生／F8 ミニ／イマーシブ一時停止。
    /// 動画／PDF の暗いプレビューでは出さない（キューは保持してよい）。
    /// </summary>
    public static bool ShowsPlayerScratchOverlays(
        bool playerMode,
        bool videoMiniPlayer,
        bool videoRealPlayback,
        bool videoImmersivePause = false,
        bool audioWaveform = false) =>
        playerMode
        && (audioWaveform
            || videoMiniPlayer
            || videoRealPlayback
            || videoImmersivePause);

    /// <summary>
    /// 波形の再生ヘッド（シークバー）とマウス直下ガイド。
    /// プレイヤーの PDF はタイムラインが無いので出さない。
    /// </summary>
    public static bool ShowsSeekCursors(bool playerMode, bool isPdf) => !playerMode || !isPdf;

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
            or WaveMenuCommand.Shuffle
            or WaveMenuCommand.AutoPlayNext
            or WaveMenuCommand.AutoPlay
            or WaveMenuCommand.PreviousTrack
            or WaveMenuCommand.NextTrack
            or WaveMenuCommand.RestartTrack
            or WaveMenuCommand.Undo
            or WaveMenuCommand.Redo
            or WaveMenuCommand.GoStart
            or WaveMenuCommand.GoEnd
            or WaveMenuCommand.ViewLeft
            or WaveMenuCommand.ViewRight
            or WaveMenuCommand.MaximizeWaveform
            or WaveMenuCommand.MaximizeAnalyzers
            or WaveMenuCommand.MaximizeLibrary
            or WaveMenuCommand.VideoMini
            or WaveMenuCommand.MinimalChrome
            or WaveMenuCommand.VideoFullscreen
            or WaveMenuCommand.VideoMiniZoomIn
            or WaveMenuCommand.VideoMiniZoomOut
            or WaveMenuCommand.VideoHud
            or WaveMenuCommand.VideoTimecode
            or WaveMenuCommand.PdfBackgroundPin
            or WaveMenuCommand.ExitRealPlayback
            or WaveMenuCommand.ForceVideoProxy
            or WaveMenuCommand.ViewWaveform
            or WaveMenuCommand.SilentSkip
            or WaveMenuCommand.AlwaysOnTop
            or WaveMenuCommand.Open
            or WaveMenuCommand.Settings
            or WaveMenuCommand.ColorPanel
            or WaveMenuCommand.Quit
            // プレイヤーではタブ UI 無し。Reopen／次／前タブは無効（プレイリスト操作を使う）。
            or WaveMenuCommand.CopyAllTabTimes
            or WaveMenuCommand.CopySelectionTime
            or WaveMenuCommand.CopySelectionTimePath
            or WaveMenuCommand.CopyFileName
            or WaveMenuCommand.CopyFilePath
            or WaveMenuCommand.Manual
            or WaveMenuCommand.GitHub
            or WaveMenuCommand.AddMarker
            or WaveMenuCommand.AddMarkerHere
            or WaveMenuCommand.ClearMarkers
            or WaveMenuCommand.DeleteMarkers
            or WaveMenuCommand.DeleteAllMarkers
            or WaveMenuCommand.DeleteAllRegions
            or WaveMenuCommand.ClearLoop
            or WaveMenuCommand.ClearLoopItem
            or WaveMenuCommand.PrevMarker
            or WaveMenuCommand.NextMarker
            // プレイリスト選択への破壊編集・書き出し（キーは不可。右クリックメニューのみ）。
            or WaveMenuCommand.Volume
            or WaveMenuCommand.Normalize
            or WaveMenuCommand.NormalizePerRegion
            or WaveMenuCommand.Pitch
            or WaveMenuCommand.TimeStretch
            or WaveMenuCommand.Reverse
            or WaveMenuCommand.SampleRate
            or WaveMenuCommand.BitDepth
            or WaveMenuCommand.Channels
            or WaveMenuCommand.FadeIn
            or WaveMenuCommand.FadeOut
            or WaveMenuCommand.InsertSilence
            or WaveMenuCommand.DetectSilence
            or WaveMenuCommand.MergeTabs
            or WaveMenuCommand.ExportByChannels
            or WaveMenuCommand.ExportMp3
            or WaveMenuCommand.ExportWave
            or WaveMenuCommand.ExportByMarkers
            or WaveMenuCommand.ExportByRegions
            or WaveMenuCommand.PlaybackPitchUp
            or WaveMenuCommand.PlaybackPitchDown
            or WaveMenuCommand.PlaybackPitchReset
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
            or TransportCommand.OpenColorPanel
            or TransportCommand.OpenSettings
            or TransportCommand.OpenManual
            or TransportCommand.ToggleLibraryMaximize
            or TransportCommand.ToggleAnalyzerMaximize
            or TransportCommand.SaveMp3
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

        if (key is Key.Escape or Key.F8 or Key.F9 or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3 or Key.F5)
        {
            return true;
        }

        if (IsLibraryCatalogRebuild(key, modifiers))
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
            && modifiers is ModifierKeys.None
                or ModifierKeys.Control
                or (ModifierKeys.Control | ModifierKeys.Shift))
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

        if ((key is Key.Left or Key.Right)
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & ~(ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Control)) == 0)
        {
            return true;
        }

        return (key, modifiers) switch
        {
            (Key.Space, ModifierKeys.None or ModifierKeys.Control or ModifierKeys.Shift) => true,
            // 振幅ズーム（エディタ／プレイヤー波形）。旧キャレット移動用ではない。
            (Key.Up or Key.Down, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Enter, ModifierKeys.None or ModifierKeys.Alt or ModifierKeys.Shift
                or ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift)) => true,
            (Key.L, ModifierKeys.None) => true,
            (Key.L, ModifierKeys.Alt) => true,
            // N／P／R 素押しは曲送り・頭出し専用（AllowsKey より前で処理）。破壊編集ショートカットには通さない。
            (Key.N, ModifierKeys.Alt) => true,
            (Key.P, ModifierKeys.Alt) => true,
            (Key.P, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.R, ModifierKeys.Alt) => true,
            (Key.J, ModifierKeys.None) => true,
            (Key.K, ModifierKeys.None) => true,
            (Key.OemComma, ModifierKeys.None) => true,
            (Key.OemPeriod, ModifierKeys.None) => true,
            // I／O／M／Ins 素押しは捨てキュー専用（AllowsKey より前）。フェード／無音挿入へは通さない。
            (Key.M, ModifierKeys.None) => true,
            (Key.Insert, ModifierKeys.None) => true,
            (Key.Delete, ModifierKeys.Control) => true,
            (Key.R, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.A, ModifierKeys.Control) => true,
            (Key.F, ModifierKeys.Control) => true,
            (Key.S, ModifierKeys.Alt) => true,
            (Key.A, ModifierKeys.Alt) => true,
            (Key.O, ModifierKeys.Control) => true,
            (Key.O, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Q, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control) => true,
            (Key.C, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Z, ModifierKeys.Control) => true,
            (Key.Z, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Y, ModifierKeys.Control) => true,
            (Key.T, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.M, ModifierKeys.Control | ModifierKeys.Shift) => true,
            (Key.Tab, ModifierKeys.Control) => true,
            (Key.Tab, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };
    }

    /// <summary>
    /// プレイヤーでキーから起こすとまずい破壊編集ショートカット。
    /// 右クリックメニューからは <see cref="BlocksWaveMenu"/> 経由で実行する。
    /// </summary>
    public static bool IsDestructiveEditShortcut(Key key, ModifierKeys modifiers) =>
        (key, modifiers) switch
        {
            (Key.N or Key.P or Key.R or Key.T or Key.S or Key.B or Key.C or Key.V
                or Key.I or Key.O, ModifierKeys.None) => true,
            (Key.D, ModifierKeys.Control) => true,
            (Key.Insert, ModifierKeys.Control) => true,
            (Key.B, ModifierKeys.Control | ModifierKeys.Shift) => true,
            _ => false,
        };

    /// <summary>プレイヤーで選択中の捨てマーカーを Delete で消すとき。リスト除外より優先。</summary>
    public static bool PrefersScratchMarkerDelete(bool playerMode, bool hasSelectedScratchMarkers) =>
        playerMode && hasSelectedScratchMarkers;

    /// <summary>
    /// Space 単独再生はプレイリストがアクティブなときだけ。
    /// ライブラリ（フォルダツリー）／お気に入りでは始めない。検索欄は呼び出し前に弾く。
    /// </summary>
    public static bool PlaysLibrarySelectionOnSpace(bool explorerFocused, bool favoritesFocused) =>
        !explorerFocused && !favoritesFocused;

    /// <summary>
    /// Escape で本再生／フレーム一時停止から暗いバックグラウンドへ戻す状態。
    /// Space 自体は開始位置へ静止（暗いプレビューへは落とさない）。
    /// </summary>
    public static bool HaltsLibraryPlaybackOnSpace(bool playbackActive, bool immersiveVideoPause) =>
        playbackActive || immersiveVideoPause;

    /// <summary>
    /// 動画本再生／PDF 明るい表示／イマーシブ一時停止など前面 UI が隠れているとき、
    /// プレイリストの全選択・行除外（Ctrl+A／Delete）を受け付けない。
    /// </summary>
    public static bool BlocksPlaylistMutationWhileChromeHidden(bool chromeVisible) =>
        !chromeVisible;

    /// <summary>
    /// 前面クローム隠れ中（動画本再生・ミニ・PDF 本表示など）は、
    /// Home／End／PageUp／PageDown をプレイリスト移動ではなくエディタと同じ波形タイムライン操作にする。
    /// </summary>
    public static bool UsesEditorTimelineNavKeys(bool chromeVisible) =>
        !chromeVisible;

    /// <summary>
    /// プレイリスト非表示の映像本再生／ミニ／PDF 本表示では、曲末で次へ進まず先頭で停止する。
    /// プレイリストが見えていて次の曲を自動再生がオンなら連続再生してよい。次へは明示の N／P・テンキー 4／6。
    /// </summary>
    public static bool HaltsAtEndWithoutPlaylistAdvance(
        bool chromeVisible,
        bool visualShown,
        bool isVideo,
        bool isPdf) =>
        !chromeVisible && visualShown && (isVideo || isPdf);

    /// <summary>
    /// F10 でエディタへ落とさず、暗いプレビューへ戻してプレイリスト（前面 UI）を出す。
    /// 動画／PDF の本再生中（クローム隠れ）とフレーム一時停止中。動画ミニ（F8）は別経路。
    /// </summary>
    public static bool ReturnsToPlaylistChromeOnF10(
        bool player,
        bool videoMini,
        bool visualShown,
        bool visualPlaying,
        bool isPdf,
        bool isVideo,
        bool bridgeHold = false,
        bool immersivePauseHold = false) =>
        player
        && !videoMini
        && HidesChromeForPlaylistVisual(
            player,
            visualShown,
            visualPlaying,
            isPdf,
            isVideo,
            bridgeHold,
            immersivePauseHold);

    /// <summary>
    /// 同じ動画の暗いプレビュー（速度ランプ中含む）から本再生へ戻すとき、
    /// プロキシ準備などの await より先に映像を 1 倍へ戻す。
    /// </summary>
    public static bool PromotesDimPreviewBeforePlayLoad(
        bool visualShown,
        bool visualPlaying,
        bool sameSource,
        bool isVideo) =>
        visualShown && !visualPlaying && sameSource && isVideo;

    /// <summary>
    /// プレイヤーの暗いバックグラウンド動画プレビュー中は早送り／巻き戻し不可。
    /// （本再生・音声再生中・フレーム／イマーシブ一時停止は対象外。）
    /// </summary>
    public static bool BlocksPlaybackShuttleInDimPreview(
        bool playerMode,
        bool visualShown,
        bool isVideo,
        bool visualPlaying,
        bool audioPlaying,
        bool frameOrImmersivePaused = false) =>
        playerMode
        && visualShown
        && isVideo
        && !visualPlaying
        && !audioPlaying
        && !frameOrImmersivePaused;

    /// <summary>
    /// ランダム再生の切替。プレイリストが空でも、ツリー／お気に入り／検索フォーカスでも効く。
    /// 素の R は検索入力または再生し直しのまま。
    /// </summary>
    public static bool IsShuffleToggle(Key key, ModifierKeys modifiers) =>
        key == Key.R && modifiers == ModifierKeys.Alt;

    /// <summary>
    /// 次の曲を自動再生の切替。プレイリストが空でも、ツリー／お気に入り／検索フォーカスでも効く。
    /// 素の N は検索入力または次の曲のまま。
    /// </summary>
    public static bool IsAutoPlayNextToggle(Key key, ModifierKeys modifiers) =>
        key == Key.N && modifiers == ModifierKeys.Alt;

    /// <summary>
    /// 曲末で先頭へ戻す Auto Loop の切替。プレイリストが空でも、ツリー／お気に入り／検索フォーカスでも効く。
    /// 素の L はループ再生のまま。
    /// </summary>
    public static bool IsAutoLoopToggle(Key key, ModifierKeys modifiers) =>
        key == Key.L && modifiers == ModifierKeys.Alt;

    /// <summary>
    /// Enter 追加時の自動再生の切替。プレイリストが空でも、ツリー／お気に入り／検索フォーカスでも効く。
    /// 素の P は検索入力または前の曲のまま。
    /// </summary>
    public static bool IsAutoPlayToggle(Key key, ModifierKeys modifiers) =>
        key == Key.P && modifiers == ModifierKeys.Alt;

    /// <summary>
    /// 検索欄フォーカス中でもショートカット側で先に拾うキー。
    /// 文字入力（素の N／P／R など）は検索へ残し、Alt＋トグルと Ctrl+Shift+P だけ通す。
    /// </summary>
    public static bool BypassesSearchFocus(Key key, ModifierKeys modifiers) =>
        IsShuffleToggle(key, modifiers)
        || IsAutoPlayNextToggle(key, modifiers)
        || IsAutoLoopToggle(key, modifiers)
        || IsAutoPlayToggle(key, modifiers)
        || IsForceVideoProxyEncode(key, modifiers);

    /// <summary>
    /// ツリー／お気に入りの Enter 追加コード。
    /// Enter／Ctrl+Enter／Shift+Enter／Ctrl+Shift+Enter（Alt なし）。
    /// </summary>
    public static bool IsPlaylistFolderAddKey(Key key, ModifierKeys modifiers)
    {
        if (key != Key.Enter)
        {
            return false;
        }

        var chord = modifiers & (ModifierKeys.Shift | ModifierKeys.Control | ModifierKeys.Alt);
        return chord is ModifierKeys.None
            or ModifierKeys.Control
            or ModifierKeys.Shift
            or (ModifierKeys.Control | ModifierKeys.Shift);
    }

    /// <summary>Shift なし＝クリアして追加。Shift＝既存へ足す。</summary>
    public static bool ClearsPlaylistOnFolderAdd(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Shift) == 0;

    /// <summary>Ctrl＝再帰。なし＝直下のみ。</summary>
    public static bool RecursesPlaylistFolderAdd(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Control) != 0;

    /// <summary>ドロップのクリア／再帰。KeyStates が欠けるときは <paramref name="modifiers"/> を併用。</summary>
    public static void ResolvePlaylistFolderDrop(
        DragDropKeyStates keyStates,
        ModifierKeys modifiers,
        out bool clearPlaylist,
        out bool recursive)
    {
        var shift = (keyStates & DragDropKeyStates.ShiftKey) != 0
            || (modifiers & ModifierKeys.Shift) != 0;
        var control = (keyStates & DragDropKeyStates.ControlKey) != 0
            || (modifiers & ModifierKeys.Control) != 0;
        clearPlaylist = !shift;
        recursive = control;
    }

    /// <summary>前の曲／次の曲（テンキー 4／6 と同じ。ノートPC向け）。</summary>
    public static bool IsPlayerTrackStepKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.P or Key.N;

    /// <summary>
    /// 動画プロキシの強制生成。設定の「自動生成しない」や F8 抑止を無視する。
    /// プレイリスト／本再生／ミニプレイヤーで使う。
    /// </summary>
    public static bool IsForceVideoProxyEncode(Key key, ModifierKeys modifiers) =>
        key == Key.P && modifiers == (ModifierKeys.Control | ModifierKeys.Shift);

    /// <summary>再生し直し（テンキー 5 と同じ。ノートPC向け）。</summary>
    public static bool IsPlayerRestartKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key == Key.R;

    /// <summary>
    /// フォルダツリーの再読込。新しいフォルダとリネームだけを反映する。
    /// プレイリスト／お気に入り／波形フォーカスでも効く。
    /// </summary>
    public static bool IsExplorerRefresh(Key key, ModifierKeys modifiers) =>
        key == Key.F5 && modifiers == ModifierKeys.None;

    /// <summary>
    /// ライブラリ索引の再構築（タグ／波形／ジャケット）。すりガラスで操作を止める。
    /// ツリーも読み直す。
    /// </summary>
    public static bool IsLibraryCatalogRebuild(Key key, ModifierKeys modifiers) =>
        key == Key.F5 && modifiers == ModifierKeys.Control;

    /// <summary>ツリーが左右を使う。再生中の早送り／巻き戻しにはしない。</summary>
    public static bool ExplorerOwnsHorizontal(Key key, ModifierKeys modifiers) =>
        key is Key.Left or Key.Right && modifiers == ModifierKeys.None;

    /// <summary>エクスプローラーの *。テンキーと Shift+8。</summary>
    public static bool IsExplorerExpandAll(Key key, ModifierKeys modifiers) =>
        (key == Key.Multiply && modifiers == ModifierKeys.None)
        || (key == Key.D8 && modifiers == ModifierKeys.Shift);

    /// <summary>
    /// プレイヤーの上段数字ジャンプ（割合／マーカー）。
    /// ツリーフォーカス中は使わず、Shift+8 の配下展開などを優先する。
    /// </summary>
    public static bool AllowsPlayerDigitJump(bool explorerFocused) => !explorerFocused;

    /// <summary>
    /// ツリー／検索フォーカス中は N／P／R／J／K をタイプアヘッド（または検索入力）に譲る。
    /// テンキーの前後曲・再生し直しは別経路のまま。
    /// </summary>
    public static bool AllowsPlayerLetterTransport(bool explorerFocused) => !explorerFocused;

    /// <summary>
    /// ツリーフォーカス中は I／O／M をタイプアヘッドに譲る。Ins は捨てマーカーのまま。
    /// </summary>
    public static bool AllowsPlayerScratchCueKey(Key key, ModifierKeys modifiers, bool explorerTreeFocused)
    {
        if (!IsPlayerScratchCueKey(key, modifiers))
        {
            return false;
        }

        if (explorerTreeFocused && key is Key.I or Key.O or Key.M)
        {
            return false;
        }

        return true;
    }

    /// <summary>エクスプローラーの /。テンキーと Oem2（日本語キーボードの /）。</summary>
    public static bool IsExplorerCollapseSubtree(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Divide or Key.Oem2;

    /// <summary>エクスプローラーの Shift+/。ルートの各フォルダを / と同じく配下ごと閉じる。</summary>
    public static bool IsExplorerCollapseAllRoots(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Shift && key is Key.Divide or Key.Oem2;

    /// <summary>ツリーのルート並び替え。設定の「上へ／下へ」と同じ。Alt+↑／↓。</summary>
    public static bool IsExplorerRootReorder(Key key, ModifierKeys modifiers) =>
        key is Key.Up or Key.Down && modifiers == ModifierKeys.Alt;

    /// <summary>プレイリストを1行スクロール。Alt+↑／↓。ツリーではルート並び替え。</summary>
    public static bool IsPlaylistScroll(Key key, ModifierKeys modifiers) =>
        key is Key.Up or Key.Down && modifiers == ModifierKeys.Alt;

    /// <summary>Shift+Space。プレイリストのチェック選択モードをトグル（ツリーは折りたたみのまま）。</summary>
    public static bool IsCheckSelectModeToggle(Key key, ModifierKeys modifiers) =>
        key == Key.Space && modifiers == ModifierKeys.Shift;

    /// <summary>Ctrl+Space のプリロールは波形フォーカス時だけ。</summary>
    public static bool PrefersPrerollOnCtrlSpace(bool waveformFocused) => waveformFocused;

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

        if (IsExplorerExpandAll(key, modifiers)
            || IsExplorerCollapseSubtree(key, modifiers)
            || IsExplorerCollapseAllRoots(key, modifiers))
        {
            return false;
        }

        if (IsExplorerRootReorder(key, modifiers))
        {
            return false;
        }

        if (IsPlaylistFolderAddKey(key, modifiers))
        {
            return false;
        }

        // アプリ共通のモード切替・終了、プレイヤーのペイン切替はツリーからでも通す。
        if (key is Key.Escape or Key.F10 or Key.F11 or Key.F12 or Key.Apps)
        {
            return false;
        }

        if (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3 or Key.F5)
        {
            return false;
        }

        if (IsLibraryCatalogRebuild(key, modifiers))
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

        // Space / Shift+Space はツリーで展開・閉じ。再生はプレイリストの Space だけ。
        if (key == Key.Space && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return true;
        }

        if ((key == Key.Q && modifiers == ModifierKeys.Control)
            || (key == Key.S && (modifiers & ModifierKeys.Alt) != 0))
        {
            return false;
        }

        // 削除・バックスペース・切り取り／貼り付け・複製などファイル操作系は止める。Ctrl+C はコピーとして通す。
        // ツリーとプレイリストの Ctrl+Delete、および F2 は、ウィンドウ側で実ファイルの削除／名前変更にする。
        // 波形フォーカスの Ctrl+Delete は捨てマーカーのままなので、ここでは握らない。
        if (key == Key.Delete && modifiers == ModifierKeys.Control)
        {
            return false;
        }

        if ((key is Key.Delete or Key.Back) && modifiers == ModifierKeys.None)
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

    /// <summary>
    /// エディタからプレイヤーへ戻すとき、抜ける前のプレイリストは消さない。
    /// エディタでタブを閉じてもプレイリスト会員は残す（閉じたのは編集タブだけ）。
    /// 同じパスを開き直していればそのセッションを使い、エディタで新しく開いたファイルは末尾へ足す。
    /// </summary>
    public static List<DocumentSession> MergeParkedPlaylist(
        IReadOnlyList<DocumentSession> parked,
        IReadOnlyList<DocumentSession> editorNow)
    {
        var liveByPath = new Dictionary<string, DocumentSession>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in editorNow)
        {
            var path = session.Document.SourcePath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                liveByPath[path] = session;
            }
        }

        var result = new List<DocumentSession>(parked.Count + editorNow.Count);
        var used = new HashSet<DocumentSession>();
        foreach (var session in parked)
        {
            var use = session;
            var path = session.Document.SourcePath;
            if (!string.IsNullOrWhiteSpace(path) && liveByPath.TryGetValue(path, out var live))
            {
                use = live;
            }

            if (used.Add(use))
            {
                result.Add(use);
            }
        }

        foreach (var session in editorNow)
        {
            if (used.Add(session))
            {
                result.Add(session);
            }
        }

        return result;
    }

    public static DocumentSession[] ExcludeEditorBlocked(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return [];
        }

        var keep = new List<DocumentSession>(sessions.Count);
        foreach (var session in sessions)
        {
            if (!LibraryPlaylistDocuments.BlocksEditor(session.Document))
            {
                keep.Add(session);
            }
        }

        return [.. keep];
    }

    /// <summary>
    /// プレイヤー→エディタ引き継ぎのカレント。残件が無ければ null（動画の音声だけ残さない）。
    /// </summary>
    public static DocumentSession? ResolveLibraryHandoffCurrent(
        IReadOnlyList<DocumentSession> remaining,
        DocumentSession? preferred)
    {
        if (preferred is not null)
        {
            foreach (var session in remaining)
            {
                if (ReferenceEquals(session, preferred))
                {
                    return preferred;
                }
            }
        }

        return remaining.Count > 0 ? remaining[0] : null;
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

    /// <summary>エディタ復帰でフル PCM が必要なストリーム／遅延読み込み。動画／PDF は昇格しない。</summary>
    public static bool NeedsEditorPcmUpgrade(AudioDocument document) =>
        !LibraryPlaylistDocuments.BlocksEditor(document)
        && (document.IsDeferredLoad || document.IsStreamPlayback);

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
    /// 同じ曲のピーク差し替え。完成を走査中スナップショットで戻さない。
    /// 推定尺と実デコードが数フレームずれると FrameCount だけ見て古い途中結果が勝ってしまう。
    /// </summary>
    public static bool IsNewerLibraryPeaks(PeakPyramid current, PeakPyramid incoming)
    {
        if (incoming.IsEmpty)
        {
            return false;
        }

        if (current.IsEmpty)
        {
            return true;
        }

        if (!current.IsBuilding && incoming.IsBuilding)
        {
            return false;
        }

        if (!incoming.IsBuilding && current.IsBuilding)
        {
            return true;
        }

        if (current.Channels != incoming.Channels)
        {
            return !incoming.IsBuilding || incoming.FilledFrames >= current.FilledFrames;
        }

        if (current.FrameCount != incoming.FrameCount)
        {
            return !incoming.IsBuilding || incoming.FilledFrames >= current.FilledFrames;
        }

        return incoming.FilledFrames >= current.FilledFrames;
    }

    /// <summary>
    /// 既に PCM があるファイルだけピークを作り直す。ストリームはフル Load が兼ねる。
    /// プレイヤー包絡（モノラル）のままだと、エディタでレーンが 1 本しか出ない。
    /// </summary>
    public static bool NeedsEditorPeakUpgrade(AudioDocument document) =>
        !NeedsEditorPcmUpgrade(document) && !CanReusePeaks(playerMode: false, document);

    /// <summary>
    /// ピーク更新を Primary 波形へ出してよいか。タイル構築中は Primary が Clear 済みなので不可。
    /// </summary>
    /// <summary>プレイヤーの MP3 書き出し対象。既存 MP3／PDF／動画は除く（混在選択では非 MP3 だけ残す）。</summary>
    public static bool IsMp3ExportCandidate(AudioDocument document) =>
        document.SourceKind != AudioFileKind.Mp3
        && !LibraryPlaylistDocuments.IsPdf(document)
        && !LibraryPlaylistDocuments.IsVideo(document)
        && (document.SourcePath is { Length: > 0 } || document.FrameCount > 0);

    public static DocumentSession[] FilterMp3ExportCandidates(IReadOnlyList<DocumentSession> sessions)
    {
        var list = new List<DocumentSession>(sessions.Count);
        foreach (var session in sessions)
        {
            if (IsMp3ExportCandidate(session.Document))
            {
                list.Add(session);
            }
        }

        return list.ToArray();
    }

    /// <summary>選択に MP3 以外の書き出し候補があるか。単体／全部が既存 MP3 なら false（メニューをグレーアウト）。</summary>
    public static bool HasMp3ExportCandidate(IReadOnlyList<DocumentSession> sessions) =>
        FilterMp3ExportCandidates(sessions).Length > 0;

    public static bool AllSessionsAreMp3(IReadOnlyList<DocumentSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].Document.SourceKind != AudioFileKind.Mp3)
            {
                return false;
            }
        }

        return true;
    }

    public static bool RefreshesPrimaryForLibraryPeaks(bool tileMode, bool sessionHasTilePane) =>
        sessionHasTilePane
            ? false
            : !tileMode;

    /// <summary>プレイヤーに入ったときに選んで再生する先頭のファイル。</summary>
    public static DocumentSession? FirstSession(IReadOnlyList<DocumentSession> sessions) =>
        sessions.Count == 0 ? null : sessions[0];

    /// <summary>
    /// 既定 Wave フォーマットと違うレート／ビット／Ch。0（未確定）は着色しない。
    /// 着色は該当セルだけ（行全体ではない）。色は <see cref="FormatMismatchForeBrushKey"/>。
    /// </summary>
    public static bool HighlightsFormatValue(int value, int defaultValue) =>
        value > 0 && value != defaultValue;

    /// <summary>既定 Wave フォーマットと違うプレイリスト文字。色設定のプレイヤー項目。</summary>
    public const string FormatMismatchForeBrushKey = "PlayerFormatMismatchForeBrush";

    /// <summary>
    /// 開いている波形が無いとき、レベルメーター／スペアナ／ラウドネス／ゴニオ／WAAPI 表記を出さない。
    /// プレイヤーもエディタも同じ。
    /// </summary>
    public static bool ShowsPlayerMeters(int playlistCount) => playlistCount > 0;

    /// <summary>
    /// 動画 HUD（A）でも、空プレイリストのメーター／WAAPI は出さない。
    /// 波形ホストは isMeterChrome=false のまま HUD に従う。
    /// 空リスト時の出し入れアニメは <see cref="MeterFadeSeconds"/>（アウト 1 秒）。
    /// </summary>
    public static bool HidesPlayerMeterHud(bool hideHud, bool showMeters, bool isMeterChrome) =>
        hideHud || (isMeterChrome && !showMeters);

    /// <summary>
    /// 音声信号で動くメーターはフェードせず即出す。停止中の追加だけ 1 秒フェード。
    /// 消すときは常にフェード（即消しは Snap／モード切替側）。
    /// </summary>
    public static bool InstantPlayerMeterReveal(bool show, bool signalMoving) =>
        show && signalMoving;

    /// <summary>
    /// メーター表示切替の instant。インだけ信号即出しを許し、アウトは常にアニメ。
    /// </summary>
    public static bool InstantPlayerMeterFade(bool show, bool instantReveal) =>
        show && instantReveal;

    /// <summary>
    /// プレイヤー突入は消えた状態から。空リストで一度出してフェードアウトしない。
    /// 再生中の即表示は先に消さない。
    /// </summary>
    public static bool SnapPlayerMetersHiddenOnEnter(bool enteringPlayer, bool instantReveal) =>
        enteringPlayer && !instantReveal;

    /// <summary>メーターのフェード。ジャケットのクロスフェードと同じ 1 秒。</summary>
    public const double MeterFadeSeconds = 1;

    /// <summary>ホバー↔本再生／PDF 表示の前面クローム。メーター用の 1 秒より短く。</summary>
    public const double ChromeFadeSeconds = 0.2;

    /// <summary>PDF ページ切替時、ページ番号を出してからフェードアウトするまでの表示時間。フォルダ名ジャンプと同じ。</summary>
    public const double PdfPageNumberToastHoldSeconds = 1;

    public const int MeterFadeFrameRate = 30;

    /// <summary>
    /// 動画専用ミニプレイヤー（F8／拡張子連動起動）中は、前面 UI をすべて隠す。
    /// </summary>
    public static bool HidesChromeForVideoFileLaunch(bool videoMiniPlayer, bool player) =>
        videoMiniPlayer && player;

    /// <summary>
    /// F8 で動画／PDF ミニへ入れるか。背面に動画または PDF が出ていてプレイリストに再生対象があるとき
    /// （本再生中または暗いバックグラウンドプレビュー）、またはすでにミニ中（解除用）。
    /// 拡張子連動起動の自動突入は別経路。
    /// </summary>
    public static bool CanToggleVideoMiniPlayer(
        bool alreadyActive,
        bool visualShown,
        bool hasPlayableSession) =>
        alreadyActive || (visualShown && hasPlayableSession);


    /// <summary>
    /// 設定を開くとき、動画／PDF 本再生中なら前面クロームを出したままにしない
    ///（停止はするがイマーシブ一時停止へ）。
    /// </summary>
    public static bool KeepsChromeHiddenForSettingsWhileVisualPlaying(
        bool player,
        bool visualPlaying,
        bool isVideo,
        bool isPdf) =>
        player && visualPlaying && (isVideo || isPdf);

    /// <summary>
    /// 動画再生中、または PDF 表示モード中は前面クロームを隠す。
    /// 選択プレビュー（暗表示）ではクロームを残す。
    /// 上下キーなどで次の本再生へつなぐあいだは <paramref name="bridgeHold"/> で隠したままにする。
    /// テンキー 0 などで本再生から一時停止したあとは <paramref name="immersivePauseHold"/> で隠したままにする。
    /// </summary>
    public static bool HidesChromeForPlaylistVisual(
        bool player,
        bool visualShown,
        bool visualPlaying,
        bool isPdf,
        bool isVideo,
        bool bridgeHold = false,
        bool immersivePauseHold = false)
    {
        if (!player || !visualShown)
        {
            return false;
        }

        if ((bridgeHold || immersivePauseHold) && (isPdf || isVideo || visualPlaying))
        {
            return true;
        }

        if (!visualPlaying)
        {
            return false;
        }

        return isPdf || isVideo;
    }

    /// <summary>
    /// 映像／PDF の本再生から次の映像／PDF 本再生へ移るとき、クロームを出さない。
    /// </summary>
    public static bool HoldsPlaylistVisualChromeBridge(
        bool currentVisualPlaying,
        bool nextIsVisual,
        bool immersivePauseHold = false) =>
        (currentVisualPlaying || immersivePauseHold) && nextIsVisual;

    /// <summary>
    /// 明るい PDF 表示中は A／T オーバーレイを出さない（フラグは触らず、次の音声／動画で覚える）。
    /// </summary>
    public static bool HidesPlaylistVideoChromeOverlaysForPdf(bool isPdf, bool chromeHidden) =>
        isPdf && chromeHidden;

    /// <summary>再生中の映像へ合わせ直す最小ずれ。これ未満のシークは MediaElement を落とす。</summary>
    public const double VideoClockSeekSeconds = 1;

    /// <summary>1／3 早送り・巻き戻し中は、ほぼ毎フレーム映像を音声へ合わせる。</summary>
    public const double VideoShuttleSeekSeconds = 1.0 / 60;

    /// <summary>
    /// 本再生中に早送り・巻き戻しで映像クロックを止めているときは、Position スクラブだけにする。
    /// （Play し直すと 1 倍に戻り、音声 3 倍と食い違う。1/3 速プレビューは _playing が false。）
    /// </summary>
    public static bool VideoSeekIsShuttleScrub(bool playing, bool visualClockRunning) =>
        playing && !visualClockRunning;

    /// <summary>
    /// テンキー 1／3 中は映像クロックが止まっていても、音声ヘッドで映像とシークバーを進める。
    /// </summary>
    public static bool DrivesPlayheadWhileVideoShuttles(
        bool playerMode,
        bool isVideoFile,
        bool visualShown,
        bool shuttling,
        bool audioPlaying) =>
        playerMode && isVideoFile && visualShown && shuttling && audioPlaying;

    /// <summary>
    /// 動画ファイルは映像クロックでシークバーを進める（本再生・1/3 速プレビュー）。
    /// wave / mp3 などは音声側（AudioPlayer）の再生ヘッドを使う。
    /// </summary>
    public static bool DrivesPlayheadWhileVideoPlays(
        bool playerMode,
        bool isVideoFile,
        bool videoClockRunning) =>
        playerMode && isVideoFile && videoClockRunning;

    /// <summary>
    /// プレイヤーで曲を切り替えても、ホバー映像のシークバー用タイマーを殺さない。
    /// Bind は ResetWorkspaceInteraction を呼ぶが、1本目は既にバインド済みでスキップされ、2本目だけ止まる。
    /// </summary>
    public static bool PreservesVisualPlayheadOnBind(bool playerMode) => playerMode;

    /// <summary>
    /// 同じ動画のホバー／暗いプレビュー中に本再生へ移るときは、いまの映像位置から続ける。
    /// 別ファイルへ移るとき（上下キーなど）は先頭から。
    /// </summary>
    public static long HoverResumeFrame(
        bool hoverVideoShown,
        bool visualPlaying,
        bool sameClip,
        double positionSeconds,
        int sampleRate,
        long frameCount)
    {
        if (!hoverVideoShown || visualPlaying || !sameClip)
        {
            return 0;
        }

        var rate = Math.Max(1, sampleRate);
        var frame = (long)Math.Round(Math.Max(0, positionSeconds) * rate);
        if (frameCount <= 0)
        {
            return frame;
        }

        return Math.Clamp(frame, 0, frameCount);
    }

    public static long FrameFromSeconds(double seconds, int sampleRate, long frameCount)
    {
        var rate = Math.Max(1, sampleRate);
        var frame = (long)Math.Round(Math.Max(0, seconds) * rate);
        return Math.Clamp(frame, 0, Math.Max(0, frameCount));
    }

    /// <summary>MediaElement の尺がまだ無いときは、タグ／mvhd の尺でシークバーを進める。</summary>
    public static TimeSpan VisualDurationOrFallback(TimeSpan natural, TimeSpan fallback)
    {
        if (natural > TimeSpan.Zero)
        {
            return natural;
        }

        return fallback > TimeSpan.Zero ? fallback : TimeSpan.Zero;
    }

    /// <summary>
    /// MediaElement.Position は無音だと更新されないことがあるので、経過時間で再生位置を出す。
    /// </summary>
    public static TimeSpan VisualPlayPosition(
        TimeSpan origin,
        double elapsedSeconds,
        double speed,
        TimeSpan duration)
    {
        var seconds = origin.TotalSeconds + (Math.Max(0, elapsedSeconds) * speed);
        if (seconds < 0)
        {
            return TimeSpan.Zero;
        }

        var pos = TimeSpan.FromSeconds(seconds);
        if (duration > TimeSpan.Zero && pos > duration)
        {
            return duration;
        }

        return pos;
    }

    /// <summary>
    /// 本再生→暗いプレビューの速度ランプ。progress 0→1 で from→to（SineEase EaseOut）。
    /// </summary>
    public static double PreviewSpeedAtRampProgress(
        double progress01,
        double from = 1,
        double to = LibraryVisualStage.PreviewLoopSpeed)
    {
        var t = progress01 <= 0 ? 0 : progress01 >= 1 ? 1 : progress01;
        // WPF SineEase EaseOut: 先に落として 1/3 へ収束する。
        var eased = Math.Sin(Math.PI * 0.5 * t);
        return from + ((to - from) * eased);
    }

    /// <summary>選択範囲ループ。範囲が無ければそのまま。</summary>
    public static long WrapLoopFrame(long frame, long start, long end)
    {
        var length = end - start;
        if (length <= 0)
        {
            return frame;
        }

        if (frame < start)
        {
            return start;
        }

        if (frame < end)
        {
            return frame;
        }

        return start + ((frame - start) % length);
    }

    public static bool NeedsLoopSeek(long frame, long start, long end) =>
        end > start && (frame < start || frame >= end);

    /// <summary>ブラウザの F11 と同じ、モニタ一面の全画面（動画／PDF）。解除もユーザー操作のみ。</summary>
    public static bool IsVideoFullscreenToggle(Key key, ModifierKeys modifiers) =>
        key == Key.F && modifiers == ModifierKeys.None;

    /// <summary>F 全画面中は動画ミニの原寸配置を当てない（解除後に反映）。</summary>
    public static bool DefersVideoLaunchPlacementWhileFullscreen(bool fullscreen) =>
        fullscreen;

    /// <summary>
    /// F 全画面中に F8 で動画ミニへ入るときは、先に全画面を抜ける。
    /// （全画面のまま原寸配置を遅延すると、ミニに入ったように見えない。）
    /// </summary>
    public static bool ExitsVideoFullscreenBeforeEnterMini(
        bool playlistVideoFullscreen,
        bool alreadyVideoMini) =>
        playlistVideoFullscreen && !alreadyVideoMini;

    /// <summary>
    /// 動画専用ミニプレイヤーだけで、アナライザ／タイムコードを窓に比例させる倍率。
    /// F10（通常／F 全画面）は常に 1。ミニでも F 全画面中は 1。
    /// 基準は最大化相当（既定 1920×1080、または呼び出し側の作業領域）。
    /// ピーク／ゴニオ／下段スペアナが見切れないよう高さ・幅の上限もかける。
    /// </summary>
    public static double VideoMiniOverlayScale(
        double windowWidth,
        double windowHeight,
        bool videoMini,
        bool fullscreen,
        double referenceWidth = DesignMetrics.OverlayScaleReferenceWidth,
        double referenceHeight = DesignMetrics.OverlayScaleReferenceHeight)
    {
        if (!videoMini || fullscreen || windowWidth < 1 || windowHeight < 1)
        {
            return 1;
        }

        var refW = referenceWidth > 1 ? referenceWidth : DesignMetrics.OverlayScaleReferenceWidth;
        var refH = referenceHeight > 1 ? referenceHeight : DesignMetrics.OverlayScaleReferenceHeight;
        // 最大化相当を 1 とし、F8 の原寸窓（例: 1280×720）はそれより小さくする。
        var byReference = Math.Min(windowWidth / refW, windowHeight / refH);
        // 波形帯＋下段ゴニオ／スペアナ＋右メーター列が同時に拡縮して収まる上限。
        var meterWidth = Math.Max(1, DesignMetrics.LevelMeterWidth);
        var sideWidth = Math.Max(
            1,
            DesignMetrics.LoudnessMeterWidth + DesignMetrics.SpectrumWidth + meterWidth);
        var stackHeight = Math.Max(
            1,
            DesignMetrics.LibraryWaveformHeight + DesignMetrics.VectorScopeHeight);
        var contentHeight = Math.Max(1, windowHeight - DesignMetrics.WindowFramePad);
        var fitWidth = Math.Min((windowWidth * 0.30) / meterWidth, windowWidth / sideWidth);
        var fitHeight = contentHeight / stackHeight;
        var scale = Math.Min(byReference, Math.Min(fitWidth, fitHeight));
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1;
        }

        return Math.Clamp(scale, DesignMetrics.OverlayScaleMin, DesignMetrics.OverlayScaleMax);
    }

    /// <summary>動画ミニの波形帯高さ（基準高 × 窓比例倍率）。</summary>
    public static double VideoMiniWaveformHeight(double scale) =>
        DesignMetrics.LibraryWaveformHeight * SanitizeOverlayScale(scale);

    /// <summary>動画ミニの下段行高（スペアナ／ゴニオ帯）。未スケールの TransportChromeHeight は使わない。</summary>
    public static double VideoMiniTransportRowHeight(double scale) =>
        DesignMetrics.SpectrumHeight * SanitizeOverlayScale(scale);

    /// <summary>メーター列幅（ピーク LayoutTransform と同じ倍率）。</summary>
    public static (double Min, double Max, double Width) VideoMiniMeterColumnWidths(
        double preferredWidth,
        double maxWidth,
        double scale)
    {
        var s = SanitizeOverlayScale(scale);
        return (
            DesignMetrics.LevelMeterWidth * s,
            maxWidth * s,
            preferredWidth * s);
    }

    /// <summary>
    /// 動画ミニ用：レベルメーターの LayoutTransform 前の高さ上限。
    /// メーター列の実高を基準に、同じ幅の 16:9 相当までで打ち切り、
    /// 上段（WAAPI）と下段ゴニオの見た目高を差し引く。低い窓は列高そのもので縮む。
    /// </summary>
    /// <param name="meterColumnHeight">MeterColumn の実高。</param>
    /// <param name="windowWidth">窓幅（16:9 上限用）。</param>
    /// <param name="windowHeight">窓高（16:9 上限用）。</param>
    /// <param name="layoutScale">ピーク／ゴニオに当てる LayoutTransform 倍率。</param>
    public static double LevelMeterMaxLayoutHeight(
        double meterColumnHeight,
        double windowWidth,
        double windowHeight,
        double layoutScale)
    {
        if (meterColumnHeight <= 0
            || windowWidth <= 0
            || windowHeight <= 0
            || layoutScale <= 0
            || double.IsNaN(meterColumnHeight)
            || double.IsNaN(windowWidth)
            || double.IsNaN(windowHeight)
            || double.IsNaN(layoutScale)
            || double.IsInfinity(meterColumnHeight)
            || double.IsInfinity(windowWidth)
            || double.IsInfinity(windowHeight)
            || double.IsInfinity(layoutScale))
        {
            return double.PositiveInfinity;
        }

        var s = SanitizeOverlayScale(layoutScale);
        // 現在の列高に、窓を 16:9 にしたときの差分を足す → 縦長では縮小、低い窓では列高のまま。
        var aspectColumnH = meterColumnHeight + windowWidth * 9d / 16d - windowHeight;
        var cappedColumn = Math.Min(meterColumnHeight, Math.Max(0, aspectColumnH));
        var reservedVisual =
            (DesignMetrics.ProjectBarHeight + DesignMetrics.VectorScopeHeight) * s;
        var availableVisual = cappedColumn - reservedVisual;
        return availableVisual > 0 ? availableVisual / s : 0;
    }

    private static double SanitizeOverlayScale(double scale)
    {
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
        {
            return 1;
        }

        return Math.Clamp(scale, DesignMetrics.OverlayScaleMin, DesignMetrics.OverlayScaleMax);
    }

    /// <summary>動画ミニで Q／Escape（無修飾）→ プレイリストへ戻る。</summary>
    public static bool LeavesVideoMiniToPlaylist(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Escape or Key.Q;

    /// <summary>
    /// 動画ミニの Escape は、In／Out があるとき解除だけしてプレイリストへ戻さない。
    /// Q は In／Out があってもプレイリストへ戻る。
    /// </summary>
    public static bool ClearsVideoMiniInOutInsteadOfLeaving(
        Key key,
        ModifierKeys modifiers,
        bool video,
        bool hasAbPoint) =>
        video
        && hasAbPoint
        && key == Key.Escape
        && modifiers == ModifierKeys.None;

    /// <summary>波形・アナライザ・WAAPI 表記の表示切替。オフのとき簡易シークバーを使える（タイムコード・ファイル名は含まない）。</summary>
    public static bool IsVideoHudToggle(Key key, ModifierKeys modifiers) =>
        key == Key.A && modifiers == ModifierKeys.None;

    /// <summary>動画オーバーレイの小数点以下フレーム換算に使う fps（ソース fps が無いときの既定）。</summary>
    public const double VideoTimecodeFramesPerSecond = 30;

    /// <summary>←／→ コマ送りの最初のリピートまでの待ち。</summary>
    public const int VideoFrameStepRepeatDelayMs = 400;

    /// <summary>←／→ コマ送りのリピート間隔（約 30 fps）。</summary>
    public const int VideoFrameStepRepeatIntervalMs = 33;

    public static int VideoFrameStepTimerIntervalMs(bool repeatStarted) =>
        repeatStarted ? VideoFrameStepRepeatIntervalMs : VideoFrameStepRepeatDelayMs;

    /// <summary>本再生中、またはテンキー 0／←→ でフレーム一時停止中ならコマ送りできる。</summary>
    public static bool CanStepPlaylistVideoFrame(
        bool playerMaximized,
        bool visualShown,
        bool isVideo,
        bool visualPlaying,
        bool immersivePause) =>
        playerMaximized && visualShown && isVideo && (visualPlaying || immersivePause);

    public static bool IsVideoFrameStepKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Left or Key.Right;

    /// <summary>1 映像フレームぶんのサンプル数（最低 1）。</summary>
    public static long VideoFrameStepSamples(int sampleRate, double framesPerSecond = VideoTimecodeFramesPerSecond)
    {
        var rate = Math.Max(1, sampleRate);
        var fps = framesPerSecond > 0 && !double.IsNaN(framesPerSecond) && !double.IsInfinity(framesPerSecond)
            ? framesPerSecond
            : VideoTimecodeFramesPerSecond;
        return Math.Max(1, (long)Math.Round(rate / fps));
    }

    /// <summary>
    /// 動画本再生中のタイムコード（MM:SS.ff。小数点以下は 30fps 換算のフレーム。選択範囲）と
    /// ファイル名の表示切替。簡易シークバーは含まない。
    /// </summary>
    public static bool IsVideoTimecodeToggle(Key key, ModifierKeys modifiers) =>
        key == Key.T && modifiers == ModifierKeys.None;

    /// <summary>
    /// 動画本再生と動画ミニで使う A／T。ユーザーが変えていなければ両方オン。
    /// 変えたあとは保存値を以後ずっと使う。
    /// </summary>
    public static void ResolveVideoLaunchChrome(
        bool userChosen,
        bool storedHud,
        bool storedTimecode,
        out bool hud,
        out bool timecode)
    {
        if (!userChosen)
        {
            hud = true;
            timecode = true;
            return;
        }

        hud = storedHud;
        timecode = storedTimecode;
    }

    /// <summary>
    /// 動画本再生の A／T を操作できるか。本再生・イマーシブ一時停止・フレーム一時停止。
    /// </summary>
    public static bool IsPlaylistVideoHudContext(
        bool playerMaximized,
        bool visualShown,
        bool isVideo,
        bool visualPlaying,
        bool immersivePause,
        bool framePaused) =>
        playerMaximized
        && visualShown
        && isVideo
        && (visualPlaying || immersivePause || framePaused);

    /// <summary>
    /// 動画本再生／ミニ限定。メーター等の文字をファイル名と同じ白系にするか。
    /// プレイヤー一般（音声＋ジャケット）では使わない。
    /// </summary>
    public static bool UsesVideoOverlayLabelFore(
        bool playerMaximized,
        bool visualShown,
        bool isVideo,
        bool chromeHidden,
        bool videoMini) =>
        playerMaximized && visualShown && isVideo && (chromeHidden || videoMini);

    /// <summary>ファイル名オーバーレイと同じ文字色（#F5F5F5）。</summary>
    public static Color VideoOverlayLabelColor { get; } = Color.FromRgb(0xF5, 0xF5, 0xF5);

    /// <summary>前面クロームが消えている本再生中だけ、タイムコードを出せる。</summary>
    public static bool ShowsPlaylistVideoTimecode(bool chromeHidden, bool timecodeOn, bool isVideo) =>
        chromeHidden && timecodeOn && isVideo;

    /// <summary>前面クロームが消えている動画本再生中、T がオンのとき下中央のファイル名を出す。</summary>
    public static bool ShowsPlaylistVideoFileName(bool chromeHidden, bool timecodeOn, bool isVideo) =>
        chromeHidden && timecodeOn && isVideo;

    /// <summary>マウスを動かしたあと、またはキーボードで時間を動かしたあと、簡易シークバーを出しておく秒数。</summary>
    public const double PlaylistVideoSimpleSeekKeyboardHoldSeconds = 3;

    /// <summary>簡易シークバーを消し始めるときのフェード秒数。表示中のフェードインとは別。</summary>
    public const double PlaylistVideoSimpleSeekFadeOutSeconds = 1;

    /// <summary>
    /// 前面クロームが消えている動画本再生で、A がアナライザを隠しているとき簡易シークバーを使える。
    /// T は無関係。A で隠した瞬間に出し、その後はマウスが動いたときとキーボードの時間移動のときに出す。
    /// 波形は描かない。A がオンのときは波形側がシークを受け持つ。
    /// </summary>
    public static bool ShowsPlaylistVideoSimpleSeek(
        bool chromeHidden,
        bool hudOn,
        bool isVideo) =>
        chromeHidden && !hudOn && isVideo;

    /// <summary>簡易シークバーの比率（0〜1）から再生フレームへ。尺が無ければ 0。</summary>
    public static long FrameAtSeekRatio(double ratio, long frameCount)
    {
        if (frameCount <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
        {
            return 0;
        }

        var t = Math.Clamp(ratio, 0, 1);
        return t >= 1 ? frameCount : (long)Math.Round(t * frameCount);
    }

    /// <summary>再生フレームから簡易シークバーの比率（0〜1）。尺が無ければ 0。</summary>
    public static double SeekRatioAtFrame(long frame, long frameCount)
    {
        if (frameCount <= 0)
        {
            return 0;
        }

        return Math.Clamp(frame / (double)frameCount, 0, 1);
    }

    /// <summary>再生用プロキシを参照しているとき、ファイル名の後に付ける。</summary>
    public const string PlaylistVideoProxySuffix = " (Proxy)";

    /// <summary>下中央のファイル名。プロキシ参照中は <see cref="PlaylistVideoProxySuffix"/> を付ける。</summary>
    public static string FormatPlaylistVideoFileName(string? displayName, bool usingProxy)
    {
        var name = displayName ?? string.Empty;
        if (!usingProxy || name.Length == 0)
        {
            return name;
        }

        return name + PlaylistVideoProxySuffix;
    }

    /// <summary>
    /// 中央のファイル名がラウドネスと重なる（または隙間未満）とき、左へずらす量。
    /// 左端は <paramref name="minNameLeft"/> より左へ出さない。
    /// </summary>
    public static double PlaylistVideoFileNameLoudnessDodge(
        Rect fileName,
        Rect loudness,
        double minNameLeft,
        double gap = 0)
    {
        if (fileName.Width <= 0 || loudness.Width <= 0)
        {
            return 0;
        }

        // 縦に重ならなければ横の回避は不要。
        if (fileName.Bottom <= loudness.Top || fileName.Top >= loudness.Bottom)
        {
            return 0;
        }

        var needed = fileName.Right + Math.Max(0, gap) - loudness.Left;
        if (needed <= 0)
        {
            return 0;
        }

        var maxDodge = Math.Max(0, fileName.Left - minNameLeft);
        return Math.Min(needed, maxDodge);
    }

    /// <summary>PDF ページを静止背景レイヤーへ固定／解除（固定時は表示を閉じる）。</summary>
    public static bool IsPdfBackgroundPinToggle(Key key, ModifierKeys modifiers) =>
        key == Key.W && modifiers == ModifierKeys.None;

    /// <summary>
    /// 明るい PDF 表示を Escape で抜けるときは暗いプレビューへ戻す。
    /// W で静止背景に固定したあとはプレビューを出さず、固定レイヤーだけ残す。
    /// </summary>
    public static bool RestoresPdfDimPreviewOnExit(bool backgroundPinned) => !backgroundPinned;

    /// <summary>PDF 表示中のページめくり（←／→のみ。Home／End／Page は波形タイムラインへ）。</summary>
    public static bool IsPdfPageKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None
        && key is Key.Left or Key.Right;

    /// <summary>PDF 本表示中、キーボード上段の数字でページ番号へ飛ぶ（テンキーは再生操作のまま）。</summary>
    public static bool IsPdfPageDigitKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && IsTopRowDigitKey(key);

    /// <summary>
    /// PDF 本表示中にページ番号トーストを出してよい状態（前面クローム隠れ）。
    /// 常時表示ではなく、ページ切替の瞬間だけ Flash する。
    /// </summary>
    public static bool ShowsPlaylistPdfPageNumber(bool chromeHidden, bool isPdf) =>
        chromeHidden && isPdf;

    /// <summary>
    /// 動画／PDF の本再生またはミニプレイヤーで前面クロームが隠れ、プレイリストが 2 件以上のとき、
    /// 左下に「現在 / 総数」トーストを出してよい（曲切替・突入時だけ Flash。しばらくしたらフェードアウト）。
    /// </summary>
    public static bool ShowsPlaylistTrackIndex(bool chromeHidden, bool isVisual, int trackCount) =>
        chromeHidden && isVisual && trackCount >= 2;

    /// <summary>プレイリスト位置トーストの表示ホールド（秒）。その後フェードアウト。</summary>
    public const double PlaylistTrackIndexToastHoldSeconds = PdfPageNumberToastHoldSeconds;

    /// <summary>表示用の「現在 / 総数」（1 始まり）。ページ番号・プレイリスト位置共用。</summary>
    public static string FormatCurrentOfTotalLabel(int index0Based, int count)
    {
        var total = Math.Max(1, count);
        var current = Math.Clamp(index0Based + 1, 1, total);
        return $"{current} / {total}";
    }

    /// <summary>表示用の「現在 / 総ページ」（1 始まり）。</summary>
    public static string FormatPdfPageLabel(int pageIndex0Based, int pageCount) =>
        FormatCurrentOfTotalLabel(pageIndex0Based, pageCount);

    /// <summary>表示用の「いまいくつめ / 総ファイル」（1 始まり）。</summary>
    public static string FormatPlaylistTrackLabel(int trackIndex0Based, int trackCount) =>
        FormatCurrentOfTotalLabel(trackIndex0Based, trackCount);

    /// <summary>
    /// PDF ページ番号の桁送り（キーボード上段のみ。1 始まり。二桁可）。
    /// 0 単体は 1 と同じ（1 ページへ）。pending があるときの 0 は 10／20… 用。
    /// テンキーは再生操作（4＝前／6＝次など）のため使わない。
    /// seekPage があればその番号へ。無効な桁は握りつぶし（割合ジャンプには落とさない）。
    /// </summary>
    public static bool TryResolvePdfPageDigitJump(
        int pending,
        int digit,
        int pageCount,
        out int nextPending,
        out int? seekPage1Based)
    {
        nextPending = 0;
        seekPage1Based = null;
        if (pageCount <= 0 || digit is < 0 or > 9)
        {
            return false;
        }

        // 0 単体は 1 ページと同じ扱い（続けて 0 なら 10 ページへ）。
        if (pending == 0 && digit == 0)
        {
            digit = 1;
        }

        var next = pending * 10 + digit;
        if (next > 0 && next <= pageCount)
        {
            nextPending = next;
            seekPage1Based = next;
            return true;
        }

        if (digit > 0 && digit <= pageCount)
        {
            nextPending = digit;
            seekPage1Based = digit;
            return true;
        }

        // 総ページを超える桁は消費だけ（再生操作／割合へ落とさない）。
        return true;
    }

    /// <summary>
    /// 再生ピッチを 1 半音動かすか。Ctrl＋テンキー ± と Ctrl＋キーボード ±。
    /// キーボードの + は Shift 付きでも受け付ける。
    /// </summary>
    public static bool TryPlaybackPitchStep(Key key, ModifierKeys modifiers, out int step)
    {
        step = 0;
        if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Windows)) != ModifierKeys.Control)
        {
            return false;
        }

        if (key is Key.Add or Key.OemPlus)
        {
            step = 1;
            return true;
        }

        if (key is Key.Subtract or Key.OemMinus)
        {
            step = -1;
            return true;
        }

        return false;
    }

    /// <summary>Ctrl＋上段 0 または Ctrl＋テンキー 0。再生ピッチを 0 に戻す。</summary>
    public static bool IsPlaybackPitchReset(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control && key is Key.D0 or Key.NumPad0;

    /// <summary>キーボード／テンキーの +（PDF 拡縮・動画ミニ窓サイズ共通）。</summary>
    public static bool IsZoomInKey(Key key, ModifierKeys modifiers) =>
        key == Key.Add && modifiers == ModifierKeys.None
        || key == Key.OemPlus && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    /// <summary>キーボード／テンキーの -（PDF 拡縮・動画ミニ窓サイズ共通）。</summary>
    public static bool IsZoomOutKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Subtract or Key.OemMinus;

    /// <summary>
    /// PDF 表示中はプレイリスト操作キーを奪う（除外・全選択など）。
    /// ↑↓ は前後ファイルへ移すので奪わない。Home／End／Page は波形タイムラインへ通す。
    /// 表示を抜けると通常どおり。
    /// </summary>
    public static bool BlocksPlaylistWhilePdf(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Delete && modifiers == ModifierKeys.None)
        {
            return true;
        }

        // Enter／Space は表示解除として別処理する。
        if (IsShuffleToggle(key, modifiers)
            || IsAutoPlayNextToggle(key, modifiers)
            || IsAutoLoopToggle(key, modifiers)
            || IsAutoPlayToggle(key, modifiers))
        {
            return true;
        }

        if (key == Key.A && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.Control)
        {
            return true;
        }

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            return true;
        }

        // テンキー 4／5／6（前後曲・再生し直し）は ↑↓ と同じくプレイリストへ通す。

        return IsPaneCycleKey(key, modifiers)
            || (modifiers == ModifierKeys.None && key is Key.F1 or Key.F2 or Key.F3)
            || IsPaneFocusArrow(key, modifiers);
    }

    /// <summary>プレイヤーで動画を出しているとき（本再生・ホバー）は Silent Skip をかけない。チェックボックスは触らない。</summary>
    public static bool IgnoresSilentSkipForVideo(bool player, bool videoPlaying) =>
        player && videoPlaying;

    public static DoubleAnimation CreateMeterFade(double from, double to) =>
        CreateOpacityFade(from, to, MeterFadeSeconds);

    public static DoubleAnimation CreateOpacityFade(double from, double to, double seconds)
    {
        var rising = to > from;
        var anim = new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = TimeSpan.FromSeconds(Math.Max(0, seconds)),
            FillBehavior = FillBehavior.HoldEnd,
            EasingFunction = new SineEase
            {
                EasingMode = rising ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };
        Timeline.SetDesiredFrameRate(anim, MeterFadeFrameRate);
        return anim;
    }

    public static void FadeElementOpacity(
        UIElement target,
        double to,
        bool instant,
        bool? hitTestVisible = null,
        double? seconds = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        // 表示中の値を残してからアニメを外す。先に外すと HoldEnd の 0 がローカル 1 に飛び、フェードインが省略される。
        var from = target.Opacity;
        var animating = target.HasAnimatedProperties;
        if (hitTestVisible is { } hit)
        {
            target.IsHitTestVisible = hit;
        }

        // 既に目標値で静止しているのに BeginAnimation(null) すると、ジャケットウォッシュと
        // 同じ合成ツリーを毎フレーム汚して色反映がカクつく。
        if (!animating && Math.Abs(from - to) < 0.001)
        {
            return;
        }

        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = from;

        if (instant || Math.Abs(from - to) < 0.001)
        {
            target.Opacity = to;
            return;
        }

        var anim = CreateOpacityFade(from, to, seconds ?? MeterFadeSeconds);
        anim.Completed += (_, _) =>
        {
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = to;
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    /// <summary>
    /// フラグは見えるのに Opacity が 0 のまま固まったときだけ強制表示する。
    /// フェードイン中（HasAnimatedProperties）は再 Sync で潰さない。
    /// </summary>
    public static bool NeedsForcedOpaque(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (target.HasAnimatedProperties)
        {
            return false;
        }

        return target.Opacity < 0.999 || !target.IsHitTestVisible;
    }

    /// <summary>
    /// 信号即出しでメーターを 1 にスナップし直す必要があるか。
    /// フェード途中／未到達のときだけ true（再生中の StartMeterRendering 連打で再 Apply しない）。
    /// </summary>
    public static bool NeedsInstantOpaqueSnap(UIElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.HasAnimatedProperties || target.Opacity < 0.999;
    }

    /// <summary>映像の上に乗っているときだけ、読みやすいドロップシャドウ。</summary>
    public static void SetVideoUiShadow(UIElement target, bool on)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!on)
        {
            target.Effect = null;
            return;
        }

        // ぼかしを広げると薄く見えるので、狭く不透明にして縁を濃くする。
        ApplyVideoDropShadow(target, blurRadius: 12, opacity: 1);
    }

    /// <summary>
    /// 文字シルエット用の柔らかい影。白い本体文字には付けず、下層の黒文字だけに使う。
    /// </summary>
    public static void SetVideoTextShadow(UIElement target, bool on)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (!on)
        {
            target.Effect = null;
            return;
        }

        ApplyVideoDropShadow(target, blurRadius: 5, opacity: 1);
    }

    private static void ApplyVideoDropShadow(UIElement target, double blurRadius, double opacity)
    {
        if (target.Effect is DropShadowEffect existing)
        {
            existing.BlurRadius = blurRadius;
            existing.ShadowDepth = 0;
            existing.Direction = 0;
            existing.Color = Colors.Black;
            existing.Opacity = opacity;
            existing.RenderingBias = RenderingBias.Performance;
            return;
        }

        target.Effect = new DropShadowEffect
        {
            BlurRadius = blurRadius,
            ShadowDepth = 0,
            Direction = 0,
            Color = Colors.Black,
            Opacity = opacity,
            RenderingBias = RenderingBias.Performance,
        };
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

    /// <summary>
    /// ↑↓／ページ送りの選択移動。wrap なら端で反対側へ回る。長押し連続では wrap=false で端に一旦止まる。
    /// 今の行が見つからなければ送りは先頭・戻しは末尾。空なら -1。
    /// </summary>
    public static int StepLoopIndex(int count, int current, int delta, bool wrap = true)
    {
        if (count <= 0 || delta == 0)
        {
            return count <= 0 ? -1 : Math.Clamp(current, 0, count - 1);
        }

        if (current < 0 || current >= count)
        {
            return delta > 0 ? 0 : count - 1;
        }

        if (!wrap)
        {
            return Math.Clamp(current + delta, 0, count - 1);
        }

        var next = (current + delta) % count;
        return next < 0 ? next + count : next;
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

    /// <summary>裏スレッドから届いた曲を UI に載せるまとまり（件数）。</summary>
    public const int PlaylistLoadUiFlushCount = 16;

    /// <summary>裏スレッドから届いた曲を UI に載せる間隔。メーター描画の間に乗せる。</summary>
    public const int PlaylistLoadUiFlushMs = 33;

    /// <summary>
    /// プレイリスト追加バッチ中は背景ウォッシュ（動的背景の色替え）を Apply しない。
    /// 完了後に保留分を 1 回だけ載せる。
    /// </summary>
    public static bool DefersAmbientWashApply(bool playlistBatchInProgress) =>
        playlistBatchInProgress;

    /// <summary>追加バッチの完了／フラッシュを、この世代がまだ所有者のときだけ行う。</summary>
    public static bool OwnsPlaylistBatchCompletion(int batchGeneration, int currentGeneration) =>
        batchGeneration == currentGeneration;

    /// <summary>遅延中の色替え要求は最新のセッション 1 件に畳む。</summary>
    public static T? MergePendingWashSession<T>(T? pending, T requested)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(requested);
        return requested;
    }

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

    /// <summary>
    /// テンキー 4／6 の曲移動は ↑↓ と同じく OS のキーリピートで送る。
    /// 単発は端で回り、長押し連続は端で一旦止まる。
    /// </summary>
    public static bool PlayerNumpadTrackStepRepeats(LibraryNumpadCommand command) =>
        command is LibraryNumpadCommand.PreviousTrack or LibraryNumpadCommand.NextTrack;

    /// <summary>
    /// マーカー番号・Ctrl+←→ など捨てキュー操作を使うか。
    /// 音声波形／動画本再生／F8／イマーシブ一時停止（暗いプレビューは不可）。
    /// </summary>
    public static bool UsesPlayerMarkerShortcuts(
        bool videoMiniPlayer,
        bool videoRealPlayback,
        bool videoImmersivePause = false,
        bool audioWaveform = false) =>
        ShowsPlayerScratchOverlays(
            playerMode: true,
            videoMiniPlayer,
            videoRealPlayback,
            videoImmersivePause,
            audioWaveform);

    /// <summary>
    /// プレイヤーではエディタのドキュメント・マーカー追加へ落とさない
    /// （ShowsCueOverlays=false のため波形に出ず、捨てキューと二重管理になる）。
    /// </summary>
    public static bool BlocksEditorMarkerPlacement(bool playerMode) => playerMode;

    /// <summary>捨て A-B／一時マーカーのキー（修飾なし）。ツリーフォーカス時の優先は <see cref="AllowsPlayerScratchCueKey"/>。</summary>
    public static bool IsPlayerScratchCueKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.I or Key.O or Key.M or Key.Insert;

    /// <summary>
    /// プレイヤーで一時マーカーがあるとき、Shift＋上段数字でマーカー番号ジャンプ（二桁可）。
    /// 無修飾の上段は常に割合ジャンプ。無修飾テンキーは再生操作。
    /// Shift／Ctrl＋テンキーは上段と同じ番号ジャンプ（表示モードに限らない）。
    /// </summary>
    public static bool CanPlayerMarkerDigitJump(bool playerMarkerContext, bool hasScratchMarkers) =>
        playerMarkerContext && hasScratchMarkers;

    /// <summary>テンキー再生操作を使う（プレイヤー／エディタ共通。上段は割合ジャンプのまま）。</summary>
    public static bool UsesTransportNumpad() => true;

    /// <summary>上段 0–9（割合／マーカージャンプ用。テンキーは含めない）。</summary>
    public static bool IsTopRowDigitKey(Key key) =>
        key is >= Key.D0 and <= Key.D9;

    /// <summary>無修飾＝割合、Shift＝マーカー、Ctrl＝リージョン（いずれも上段のみ）。テンキー再生は別。</summary>
    public static bool IsDigitJumpChord(Key key, ModifierKeys modifiers) =>
        IsPercentDigitJump(key, modifiers)
        || IsMarkerDigitJump(key, modifiers)
        || IsRegionDigitJump(key, modifiers);

    public static bool IsPercentDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && IsTopRowDigitKey(key);

    public static bool IsMarkerDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Shift && IsTopRowDigitKey(key);

    /// <summary>Ctrl＋上段数字でリージョン番号へ。Ctrl＋テンキーは <see cref="IsNumpadRegionDigitJump"/>。</summary>
    public static bool IsRegionDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control && IsTopRowDigitKey(key);

    /// <summary>テンキー 0–9。</summary>
    public static bool IsNumpadDigitKey(Key key) =>
        key is >= Key.NumPad0 and <= Key.NumPad9;

    /// <summary>Shift＋テンキー数字。一時マーカーまたはドキュメントのマーカーがあるとき番号へ。</summary>
    public static bool IsNumpadMarkerDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Shift && IsNumpadDigitKey(key);

    /// <summary>Ctrl＋テンキー数字。リージョンがあるとき番号の先頭へ。</summary>
    public static bool IsNumpadRegionDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control && IsNumpadDigitKey(key);

    /// <summary>Alt＋テンキー数字。上段の無修飾と同じ表示範囲の割合（0＝先頭、9＝90%）。</summary>
    public static bool IsNumpadPercentDigitJump(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Alt && IsNumpadDigitKey(key);

    /// <summary>Alt＋テンキーの仮想キー（VK_NUMPAD0＝0x60）。Shift／Ctrl 付きは対象外。</summary>
    public static bool IsAltNumpadPercentVirtualKey(int virtualKey, bool altDown, bool shiftDown, bool ctrlDown) =>
        altDown && !shiftDown && !ctrlDown && virtualKey is >= 0x60 and <= 0x69;

    /// <summary>
    /// Alt 解放後の猶予中、または物理的に Alt が上がっているときは修飾が残留している。
    /// 新しい Alt 押下で猶予を終えるときは altReleasedAt を負にする。
    /// </summary>
    public static bool IsStaleAltModifier(bool physicalAltDown, long altReleasedAt, long now, int graceMs) =>
        (graceMs > 0 && altReleasedAt >= 0 && now >= altReleasedAt && now - altReleasedAt < graceMs)
        || !physicalAltDown;

    /// <summary>
    /// Alt 解放後に残ったシステムキーのテンキー。割合ではなく無修飾の再生操作にし、
    /// DefWindowProc へ渡すとメニュー不一致のシステム音になる。
    /// </summary>
    public static bool IsStaleSysNumpad(int virtualKey, bool systemKey, bool altModifierActive, bool shiftDown, bool ctrlDown) =>
        systemKey
        && !altModifierActive
        && !shiftDown
        && !ctrlDown
        && virtualKey is >= 0x60 and <= 0x69;

    /// <summary>
    /// マーカー番号の桁送り（Shift＋上段）。
    /// seekId があればその番号へ。無効な桁は percentFallback（呼び出し側で無視可）。
    /// </summary>
    public static bool TryResolveMarkerDigitJump(
        int pending,
        int digit,
        int markerCount,
        out int nextPending,
        out int? seekId,
        out bool percentFallback)
    {
        nextPending = 0;
        seekId = null;
        percentFallback = false;
        if (markerCount <= 0 || digit is < 0 or > 9)
        {
            return false;
        }

        var next = pending * 10 + digit;
        if (next > 0 && next <= markerCount)
        {
            nextPending = next;
            seekId = next;
            return true;
        }

        if (digit > 0 && digit <= markerCount)
        {
            nextPending = digit;
            seekId = digit;
            return true;
        }

        percentFallback = true;
        return true;
    }

    /// <summary>
    /// リージョン番号の桁送り（Ctrl＋上段。1 始まり。マーカー番号とは別）。
    /// seekId があればその番号の先頭へ。どの番号にもならない桁は seek せず消費する。
    /// </summary>
    public static bool TryResolveRegionDigitJump(
        int pending,
        int digit,
        int regionCount,
        out int nextPending,
        out int? seekId)
    {
        nextPending = 0;
        seekId = null;
        if (regionCount <= 0 || digit is < 0 or > 9)
        {
            return false;
        }

        var next = pending * 10 + digit;
        if (next > 0 && next <= regionCount)
        {
            nextPending = next;
            seekId = next;
            return true;
        }

        if (digit > 0 && digit <= regionCount)
        {
            nextPending = digit;
            seekId = digit;
            return true;
        }

        return true;
    }

    /// <summary>テンキー数字（無修飾）。プレイヤーではすべて再生操作側で消費（2／8 は未割り当て）。</summary>
    public static bool IsPlayerNumpadKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is >= Key.NumPad0 and <= Key.NumPad9;

    /// <summary>英字／記号のシャトル（,．）。テンキー 1／3 は呼び出し側で別判定。</summary>
    public static bool IsPlayerShuttleKey(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.OemComma or Key.OemPeriod;

    /// <summary>
    /// テンキー再生操作（プレイヤー／エディタ共通）。
    /// 2／8 は割り当てなし（呼び出し側で握りつぶし。割合ジャンプにはしない）。
    /// </summary>
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

    /// <summary>英字／記号の再生操作（J／K／,／.。テンキーは <see cref="PlayerNumpadCommand"/>）。</summary>
    public static LibraryNumpadCommand PlayerTransportCommand(Key key, ModifierKeys modifiers)
    {
        if (modifiers != ModifierKeys.None)
        {
            return LibraryNumpadCommand.None;
        }

        return key switch
        {
            Key.OemComma => LibraryNumpadCommand.Rewind,
            Key.OemPeriod => LibraryNumpadCommand.FastForward,
            Key.J => LibraryNumpadCommand.SeekBack,
            Key.K => LibraryNumpadCommand.SeekForward,
            _ => LibraryNumpadCommand.None,
        };
    }

    public static int PageStep(int visibleRows) =>
        Math.Max(1, visibleRows - 1);

    public static bool IsPaneCycleKey(Key key, ModifierKeys modifiers) =>
        key == Key.Tab && modifiers is ModifierKeys.None or ModifierKeys.Shift;

    /// <summary>
    /// Ctrl+←／↑ ライブラリ、Ctrl+→ プレイリスト、Ctrl+↓ お気に入り。
    /// 動画ミニ（F8）では Ctrl+←／→ をマーカージャンプに使うため、呼び出し側で除外する。
    /// </summary>
    public static bool IsPaneFocusArrow(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.Control
        && key is Key.Left or Key.Right or Key.Up or Key.Down;

    /// <summary>
    /// プレイヤーで一時マーカーがあるとき Ctrl+←／→ をマーカージャンプに使う。
    /// マーカーが無い／暗いプレビューではペインフォーカス（ライブラリ⇔プレイリスト）のまま。
    /// </summary>
    public static bool PrefersMarkerArrowJump(bool playerMarkerContext, bool hasScratchMarkers) =>
        playerMarkerContext && hasScratchMarkers;

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
    /// <summary>
    /// NumLock オンの Shift＋テンキーは、届く時点で Home／End／矢印になっている。
    /// そのとき Shift 自体は下りとして報告されないことがある。
    /// 拡張ビットが無く、仮想キーが対応する移動キーであるスキャンだけ数字に戻す。
    /// 専用の Home／End は拡張ビットが立つ。NumLock オフのテンキーと、無修飾のテンキー数字は対象外。
    /// </summary>
    public static bool TryTranslatedNumpadDigit(int virtualKey, int scanCode, bool extended, bool numLockOn, out int digit)
    {
        digit = -1;
        if (!numLockOn || extended)
        {
            return false;
        }

        var (expectedDigit, navigationKey) = (scanCode & 0xFF) switch
        {
            0x52 => (0, 0x2D),
            0x4F => (1, 0x23),
            0x50 => (2, 0x28),
            0x51 => (3, 0x22),
            0x4B => (4, 0x25),
            0x4C => (5, 0x0C),
            0x4D => (6, 0x27),
            0x47 => (7, 0x24),
            0x48 => (8, 0x26),
            0x49 => (9, 0x21),
            _ => (-1, 0),
        };
        if (expectedDigit < 0 || virtualKey != navigationKey)
        {
            return false;
        }

        digit = expectedDigit;
        return true;
    }

    /// <summary>
    /// NumLock オンの Shift＋テンキーは、WM_KEYDOWN の時点で Home／End／矢印になる。
    /// 拡張ビットの無いテンキーのスキャンコードだけ数字へ戻す。専用キーは拡張ビットが立つ。
    /// Ctrl／Alt が付いているときは触らない。
    /// </summary>
    public static bool TryShiftedNumpadDigit(
        long lParam,
        bool shiftDown,
        bool ctrlDown,
        bool altDown,
        out int digit)
    {
        digit = -1;
        if (!shiftDown || ctrlDown || altDown)
        {
            return false;
        }

        if ((lParam & 0x01000000) != 0)
        {
            return false;
        }

        digit = ((int)(lParam >> 16) & 0xFF) switch
        {
            0x52 => 0,
            0x4F => 1,
            0x50 => 2,
            0x51 => 3,
            0x4B => 4,
            0x4C => 5,
            0x4D => 6,
            0x47 => 7,
            0x48 => 8,
            0x49 => 9,
            _ => -1,
        };
        return digit >= 0;
    }

    public static bool IsKeyRepeat(long lParam) => (lParam & 0x40000000) != 0;

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
