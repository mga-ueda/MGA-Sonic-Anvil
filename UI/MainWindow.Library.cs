using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private int _libraryLoadGeneration;
    private DocumentSession? _libraryLoadSession;
    private bool _libraryPlayFirstPending;
    private int _libraryEnterPlayGeneration;

    /// <summary>再帰追加中に再生する最初の登録曲。グループ再配置で他曲へ飛ばないようにする。</summary>
    private DocumentSession? _libraryFolderPlaySession;
    /// <summary>プレイリスト置換中は空セッションで既定ウォッシュへ落とさない。</summary>
    private bool _libraryHoldJacketWash;
    private bool _libraryPlayOnArrowRelease;
    /// <summary>Space 再生。true のあいだは曲末で次へ進まず停止する。</summary>
    private bool _libraryStopAfterTrack;
    /// <summary>F9（および F8 動画ミニ）。サイド／トランスポート／ステータスを隠す。F8 はアナライザを残す。再生は止めない。</summary>
    private bool _libraryMinimalChrome;
    private bool _libraryPlayerHostActive;
    /// <summary>F10 突入前のタイル配置。退出後に付け直す。</summary>
    private WaveformTileArrange _librarySuspendedTileArrange;
    private readonly LibraryPlaylistRemoveUndo _libraryPlaylistUndo = new();
    private bool _libraryWaapiSuspended;
    private bool _restoreWaapiAfterLibrary;
    private CancellationTokenSource? _videoProxyCts;
    private readonly HashSet<AudioDocument> _libraryPeakJobs = [];
    private readonly Dictionary<AudioDocument, Task> _libraryPeakTasks = [];
    private readonly Dictionary<AudioDocument, CancellationTokenSource> _libraryPeakJobCts = [];
    private int _libraryPeakGeneration;
    private int _libraryWavePaintTicket;
    private CancellationTokenSource _libraryPeakCts = new();
    private int _libraryFolderShowGeneration;
    /// <summary>フォルダ再帰追加の実行中。Enter 連打で置換が重なり 1 曲で打ち切られるのを防ぐ。</summary>
    private bool _libraryFolderShowInProgress;
    private int _gaplessToken;
    private DocumentSession? _gaplessTarget;
    private bool _gaplessInFlight;
    private bool _gaplessFailed;
    private bool _playerMeterChrome;
    private bool? _playerMetersShown;
    private bool _playlistVideoChromeVisible = true;
    private bool _playlistVideoHud = true;
    private bool _playlistVideoTimecode = true;
    /// <summary>プレイリスト動画本再生中だったか（開始／曲切替で A／T をオンに戻す判定用）。</summary>
    private bool _playlistVideoChromePlayActive;
    private string? _playlistVideoChromePlayPath;
    /// <summary>映像／PDF 本再生のつなぎでクロームを出さない。</summary>
    private bool _playlistVisualChromeBridge;

    /// <summary>本再生からの一時停止（テンキー 0）中はクロームを出さない。</summary>
    private bool _playlistVideoImmersivePause;
    private bool _playlistVideoFullscreen;
    /// <summary>F 全画面中に動画寸法の適用を先送りしたか。解除時に Apply する。</summary>
    private bool _videoLaunchPlacementDeferred;
    private WindowState _windowStateBeforeVideoFullscreen;
    private WindowStyle _windowStyleBeforeVideoFullscreen;
    private ResizeMode _resizeModeBeforeVideoFullscreen;
    private Rect _boundsBeforeVideoFullscreen;

    internal bool IsLibraryMaximized => _waveformMaximizeMode == WaveformMaximizeMode.Library;

    /// <summary>
    /// F11・プレイヤーでは dB 目盛り列を畳む。
    /// F12 は単一表示では残し、タイル中は仕切りに代えるため畳む。編集時だけ常に残す。
    /// </summary>
    private bool ShowWaveformScaleLane =>
        !IsLibraryMaximized
        && _waveformMaximizeMode switch
        {
            WaveformMaximizeMode.Waveform => false,
            WaveformMaximizeMode.Analyzers => !_tileMode,
            _ => true,
        };

    private void ApplyWaveformScaleLanes()
    {
        PrimaryWaveform.ShowScaleLane = ShowWaveformScaleLane;
        ForEachWaveform(view => view.ShowScaleLane = ShowWaveformScaleLane);
    }

    internal bool IsLibraryGroupComboFocused =>
        IsLibraryMaximized && LibraryBrowser.IsGroupComboFocused;

    internal bool IsLibraryExplorerFocused =>
        IsLibraryMaximized && LibraryBrowser.IsExplorerFocused;

    internal bool IsLibraryFavoritesFocused =>
        IsLibraryMaximized && LibraryBrowser.IsFavoritesFocused;

    private void ToggleLibraryMaximize()
    {
        // 動画ミニ（F8）中の F10 はプレイリストが見える通常プレイヤーへ戻す。
        if (IsVideoMiniPlayerActive())
        {
            LeaveVideoMiniToPlaylist(minimalChrome: false);
            return;
        }

        // F9 ミニマム中の F10 はエディタへ落とさず、通常の F10 プレイヤーへ戻す。
        if (IsLibraryMaximized && _libraryMinimalChrome)
        {
            LeaveLibraryMinimalChrome();
            return;
        }

        // 動画／PDF の本再生中、または ←→／テンキー 0 のフレーム一時停止中は前面 UI が消えている。
        // エディタへ落とさず、暗いバックグラウンド再生へ落としてクローム（プレイリスト）を出す。
        if (LibraryPlayerMode.ReturnsToPlaylistChromeOnF10(
                IsLibraryMaximized,
                videoMini: false,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualPlaying,
                LibraryBrowser.PlaylistVisualIsPdf,
                LibraryBrowser.PlaylistVisualIsVideo,
                _playlistVisualChromeBridge,
                _playlistVideoImmersivePause))
        {
            StopVideoFrameStep();
            DropPlaylistVideoToDimPreview();
            return;
        }

        SetWaveformMaximizeMode(
            _waveformMaximizeMode == WaveformMaximizeMode.Library
                ? WaveformMaximizeMode.Off
                : WaveformMaximizeMode.Library);
    }

    /// <summary>
    /// F8 動画専用ミニプレイヤー。前面 UI 無し・映像原寸ウィンドウ枠。
    /// 突入は背面に動画があるとき（本再生または暗いバックグラウンドプレビュー）かつ
    /// プレイリストに再生対象があるとき（拡張子連動は自動）。暗いプレビューからは本再生を再開して入る。
    /// もう一度 F8 で通常プレイヤーへ（再生は続ける）。F9／F10 でプレイリストへ戻る。
    /// </summary>
    private void ToggleVideoMiniPlayer()
    {
        var active = IsVideoMiniPlayerActive();
        if (!LibraryPlayerMode.CanToggleVideoMiniPlayer(
                active,
                IsVideoMiniEnterEligible(),
                hasPlayableSession: _sessions.Count > 0))
        {
            return;
        }

        if (active)
        {
            // F8 解除は再生を続けたまま通常プレイヤーへ。
            LeaveVideoMiniKeepingPlayback();
            return;
        }

        // F8 は設定にかかわらずプロキシを作らない（本再生再開より先に抑止する）。
        VideoProxy.SuppressAutoEncode = true;

        // 暗いバックグラウンド／フレーム一時停止からは、いまの位置で本再生してからミニへ。
        if (LibraryBrowser.PlaylistVisualIsVideo && !IsVideoRealPlayback())
        {
            ResumeVideoPlaybackForMini();
        }
        else if (LibraryBrowser.PlaylistVisualIsPdf && !LibraryBrowser.PlaylistVisualPlaying)
        {
            // PDF の暗いプレビューからは明るい表示にしてからミニへ。
            LibraryBrowser.SetPlaylistVisualPlaying(true);
        }

        EnterVideoMiniPlayer();
    }

    private bool IsVideoMiniPlayerActive() =>
        IsLibraryMaximized && _videoLaunchPlacement;

    /// <summary>動画の本再生中（暗いプレビューは含まない）。</summary>
    private bool IsVideoRealPlayback() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsVideo
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>
    /// 捨てマーカー／InOut／番号ジャンプを使うか。
    /// 音声のプレイリスト波形、動画本再生・F8・イマーシブ一時停止（暗いプレビューは不可）。
    /// </summary>
    private bool IsPlayerScratchContext() =>
        LibraryPlayerMode.UsesPlayerMarkerShortcuts(
            videoMiniPlayer: IsVideoMiniPlayerActive(),
            videoRealPlayback: IsVideoRealPlayback(),
            videoImmersivePause: IsLibraryMaximized
                && LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && _playlistVideoImmersivePause,
            audioWaveform: IsLibraryMaximized
                && _document is not null
                && !LibraryPlaylistDocuments.IsVisual(_document));

    private void SyncPlayerScratchEnabled()
    {
        var enabled = IsPlayerScratchContext();
        PrimaryWaveform.PlayerScratchEnabled = enabled;
        ForEachWaveform(view => view.PlayerScratchEnabled = enabled);
    }

    /// <summary>
    /// プレイヤー捨てキュー（I／O／M／Ins）。ツリータイプアヘッドより先に呼ぶこと。
    /// </summary>
    private bool TryHandlePlayerScratchCueKey(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsPlayerScratchCueKey(key, modifiers)
            || !IsPlayerScratchContext())
        {
            return false;
        }

        if (key is Key.I or Key.O)
        {
            TrySetPlayerAbPoint(key == Key.I);
            return true;
        }

        TryAddPlayerScratchMarkerAtPlayhead();
        return true;
    }

    /// <summary>F8 突入可。背面に動画または PDF が出ていれば本再生でなくても可。</summary>
    private bool IsVideoMiniEnterEligible() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf);

    /// <summary>暗いプレビュー等から F8 用に本再生を再開する。</summary>
    private void ResumeVideoPlaybackForMini()
    {
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null || !LibraryPlaylistDocuments.IsVideo(session.Document))
        {
            return;
        }

        ClearPlaylistVideoImmersivePause(sync: false);
        StartLibraryVisualPlayback(session, stopAfterTrack: false);
    }

    /// <summary>
    /// F9 ミニマムプレイヤー。どのモードからでも入れる。
    /// ミニマム中の F9 は通常のエディタへ戻す。F10 は通常の F10 プレイヤーへ戻す（Esc や F1 では戻さない）。
    /// 切替時にウィンドウ位置・サイズをスロットへ記憶／復元する。F10 へ戻るときは再生を続ける。
    /// </summary>
    private void ToggleLibraryMinimalChrome()
    {
        if (IsVideoMiniPlayerActive())
        {
            LeaveVideoMiniToPlaylist(minimalChrome: true);
            return;
        }

        if (IsLibraryMaximized && _libraryMinimalChrome)
        {
            LeaveMinimalToEditor();
            return;
        }

        EnterLibraryMinimalChrome();
    }

    /// <summary>ミニマム中の F9。通常のエディタへ戻す。F9 のウィンドウ位置は残す。</summary>
    private void LeaveMinimalToEditor()
    {
        if (!IsLibraryMaximized || !_libraryMinimalChrome)
        {
            return;
        }

        SetWaveformMaximizeMode(WaveformMaximizeMode.Off);
        if (IsLibraryMaximized)
        {
            return;
        }

        _libraryMinimalChrome = false;
    }

    /// <summary>ミニマム解除 → 通常の F10。F10 から。</summary>
    private void LeaveLibraryMinimalChrome()
    {
        if (!IsLibraryMaximized || !_libraryMinimalChrome)
        {
            return;
        }

        PersistCurrentWindowPlacement();
        _libraryMinimalChrome = false;
        ApplyWaveformMaximizeChrome();
        TryApplyCurrentModePlacement();
        AppStorage.Save();
        FocusLibraryPaneForPlaylist();
    }

    private void EnterLibraryMinimalChrome()
    {
        if (IsLibraryMaximized)
        {
            if (_libraryMinimalChrome)
            {
                return;
            }

            PersistCurrentWindowPlacement();
            _libraryMinimalChrome = true;
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
            AppStorage.Save();
            LibraryBrowser.FocusList();
            return;
        }

        var preservePlayback = IsPlaybackActive();
        _libraryMinimalChrome = true;
        SetWaveformMaximizeMode(
            WaveformMaximizeMode.Library,
            playFirstOnLibrary: !preservePlayback,
            retainMinimalChrome: true);
        LibraryBrowser.FocusList();
    }

    /// <summary>動画ミニへ入る（F8／拡張子連動／引数）。プロキシは作らない。</summary>
    private void EnterVideoMiniPlayer()
    {
        // F 全画面中の F8 は全画面を抜けてから原寸ミニへ（遅延配置のままだと変化が分からない）。
        if (LibraryPlayerMode.ExitsVideoFullscreenBeforeEnterMini(
                _playlistVideoFullscreen,
                alreadyVideoMini: _videoLaunchPlacement))
        {
            SetPlaylistVideoFullscreen(false);
        }

        if (IsLibraryMaximized && !_videoLaunchPlacement)
        {
            PersistCurrentWindowPlacement();
        }

        BeginVideoMiniPlayerSession();
        ApplyVideoMiniPlayerUi();
        if (!IsLibraryMaximized)
        {
            SetWaveformMaximizeMode(
                WaveformMaximizeMode.Library,
                playFirstOnLibrary: !IsPlaybackActive(),
                retainMinimalChrome: true);
        }
        else
        {
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
        }

        SyncPlaylistVideoChromeFade();
        ScheduleLibraryWaveformPaint();
        AppStorage.Save();
    }

    /// <summary>動画ミニセッション開始。F9／F10 本再生以外ではプロキシを作らない。</summary>
    private void BeginVideoMiniPlayerSession()
    {
        if (!_videoLaunchPlacement)
        {
            _videoLaunchScale = LibraryVideoWindowScale.Native;
            _videoLaunchScaleLatched = false;
        }

        _videoLaunchPlacement = true;
        VideoProxy.SuppressAutoEncode = true;
        if (!_playlistVideoFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void ApplyVideoMiniPlayerUi()
    {
        _libraryMinimalChrome = true;
        // F8／引数起動共通。前回の A／T を復元（既定はどちらもオフ）。
        LibraryPlayerMode.ResolveVideoLaunchChrome(
            AppStorage.Settings.VideoLaunchHud,
            AppStorage.Settings.VideoLaunchTimecode,
            out _playlistVideoHud,
            out _playlistVideoTimecode);
        if (!_playlistVideoFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
        }
    }

    /// <summary>動画ミニ（F8／引数起動）中の A／T を次回用に覚える（両者共通）。</summary>
    private void RememberVideoLaunchChromeIfNeeded()
    {
        if (!_videoLaunchPlacement)
        {
            return;
        }

        var settings = AppStorage.Settings;
        if (settings.VideoLaunchHud == _playlistVideoHud
            && settings.VideoLaunchTimecode == _playlistVideoTimecode)
        {
            return;
        }

        settings.VideoLaunchHud = _playlistVideoHud;
        settings.VideoLaunchTimecode = _playlistVideoTimecode;
        AppStorage.Save();
    }

    /// <summary>F8 解除。再生は続けたまま通常の F10 プレイヤーへ。</summary>
    private void LeaveVideoMiniKeepingPlayback()
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        PersistCurrentWindowPlacement();
        EndVideoLaunchSession();
        _libraryMinimalChrome = false;
        _playlistVideoHud = true;
        _playlistVideoTimecode = true;
        ApplyWaveformMaximizeChrome();
        TryApplyCurrentModePlacement();
        SyncPlaylistVideoChromeFade();
        AppStorage.Save();
        FocusLibraryPaneForPlaylist();
    }

    /// <summary>
    /// F9／F10。動画ミニを解き、プレイリストが見える通常／F9 プレイヤーへ戻す。
    /// 本再生中は暗いプレビューへ落として前面 UI（リスト）を出す。
    /// </summary>
    private void LeaveVideoMiniToPlaylist(bool minimalChrome)
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        PersistCurrentWindowPlacement();
        EndVideoLaunchSession();
        _playlistVideoImmersivePause = false;
        if (IsPlaybackActive() || LibraryBrowser.PlaylistVisualPlaying)
        {
            DropPlaylistVideoToDimPreview();
        }

        _libraryMinimalChrome = minimalChrome;
        _playlistVideoHud = true;
        _playlistVideoTimecode = true;
        ApplyWaveformMaximizeChrome();
        TryApplyCurrentModePlacement();
        SyncPlaylistVideoChromeFade();
        AppStorage.Save();
        if (minimalChrome)
        {
            LibraryBrowser.FocusList();
        }
        else
        {
            FocusLibraryPaneForPlaylist();
        }
    }

    /// <summary>
    /// F11／F12 へ移る前に動画ミニだけ解く（プレイリストは触らない。動画の持ち込みは KeepOnly で除外）。
    /// </summary>
    private void LeaveVideoMiniBeforeWaveformFullscreen()
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        PersistCurrentWindowPlacement();
        EndVideoLaunchSession();
        _playlistVideoImmersivePause = false;
        _libraryMinimalChrome = false;
        LibraryBrowser.HidePlaylistVisual();
        // F11／F12 が覚える枠が映像サイズにならないよう、先に F10 寸法へ戻す。
        WindowPlacement.TryApply(this, AppStorage.Settings, MainWindowPlacementKind.Player);
        SyncPlaylistVideoChromeFade();
        AppStorage.Save();
    }

    private void ApplyLibraryChrome()
    {
        var show = IsLibraryMaximized;
        ApplyStatusFieldChrome();
        LibraryBrowser.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LibraryBrowser.IsEnabled = show;
        if (show)
        {
            LibraryBrowser.SetGlowExtendsWaveform(true);
            ApplyLibraryWashChrome(true);
        }
        else
        {
            LibraryBrowser.ClearPdfBackgroundPin();
            LibraryBrowser.ResetShuffle();
            LibraryBrowser.UseWindowFallbackWash();
            ApplyLibraryWashChrome(true);
        }
        LibrarySplitter.Visibility = Visibility.Collapsed;
        DocumentTabHost.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        LibraryRowDef.MinHeight = show ? DesignMetrics.LibraryPaneMinHeight : 0;
        LibraryRowDef.Height = show
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (show)
        {
            LibraryWaveformRowDef.MinHeight = DesignMetrics.LibraryWaveformHeight;
            LibraryWaveformRowDef.MaxHeight = DesignMetrics.LibraryWaveformHeight;
            LibraryWaveformRowDef.Height = new GridLength(DesignMetrics.LibraryWaveformHeight);
            WaveformHostBorder.MinHeight = DesignMetrics.LibraryPaneMinHeight + DesignMetrics.LibraryWaveformHeight;
        }
        else
        {
            LibraryWaveformRowDef.MinHeight = 0;
            LibraryWaveformRowDef.MaxHeight = double.PositiveInfinity;
            LibraryWaveformRowDef.Height = new GridLength(1, GridUnitType.Star);
            WaveformHostBorder.MinHeight = DesignMetrics.WaveformHostMinHeight;
        }

        PrimaryWaveform.SeekAndSelectOnly = show;
        ForEachWaveform(view => view.SeekAndSelectOnly = show);
        SyncPlayerScratchEnabled();
        SyncPlaylistVideoChromeFade();
        TimeScrollStrip.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        HistoryStrip.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            CloseEditHistory(commit: true);
        }

        RefreshTransportCommandsEnabled();
        ApplyLibraryWaapi(show);
        ApplyLibraryPlayerHost(show);
        if (show)
        {
            ProbeLibraryTags();
            if (_libraryPlayFirstPending)
            {
                // 再生はクロムの描画が終わってから。ここでは選択だけ。
                SelectLibraryFirstTrackForEnter();
            }
            else
            {
                LibraryBrowser.SetSessions(_sessions, _activeSession);
                ShowLibraryArtworkOrClear(_activeSession);
            }

            // 行高が 133px に落ちたあとの描画でないと、エディタサイズのビットマップが画面外へずれたまま残る。
            ScheduleLibraryWaveformPaint();
        }
        else
        {
            // 抜けるときの Persist（SetWaveformMaximizeMode）より先にフラグを消すと
            // 映像サイズが F9 ミニマム枠へ書かれるので、位置だけ残してから消す。
            if (_videoLaunchPlacement)
            {
                WindowPlacement.CaptureVideoLaunchPosition(this, AppStorage.Settings);
            }

            _libraryWavePaintTicket++;
            _libraryEnterPlayGeneration++;
            _libraryPlayFirstPending = false;
            _libraryPlayOnArrowRelease = false;
            _libraryStopAfterTrack = false;
            _libraryHoldJacketWash = false;
            _libraryPlaylistUndo.Clear();
            _ = LeaveLibraryMaximizeAsync();
            if (_videoLaunchPlacement)
            {
                EndVideoLaunchSession();
            }
        }

        SyncPlayerMeterFade();
    }

    /// <summary>
    /// レベルメーター／スペアナ／ラウドネス／ゴニオと、エディタの波形スクロール。
    /// 波形が無いときは出さない。読み込みは 1 秒フェードイン、閉じたら 1 秒フェードアウト。
    /// プレイヤーは音声で動き始めたら即表示。突入は消えた状態から。
    /// </summary>
    private void SyncPlayerMeterFade()
    {
        var player = IsLibraryMaximized;
        var show = LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count);
        var entering = player && !_playerMeterChrome;
        var leaving = !player && _playerMeterChrome;
        _playerMeterChrome = player;
        var instantReveal = LibraryPlayerMode.InstantPlayerMeterReveal(
            show,
            player && IsPlaybackActive());

        if (leaving)
        {
            ApplyPlayerMeterFade(visible: show, instant: true);
            _playerMetersShown = show;
            return;
        }

        if (LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(entering, instantReveal))
        {
            ApplyPlayerMeterFade(visible: false, instant: true);
            _playerMetersShown = false;
        }

        if (_playerMetersShown == show)
        {
            if (show && instantReveal)
            {
                ApplyPlayerMeterFade(visible: true, instant: true);
            }

            return;
        }

        ApplyPlayerMeterFade(show, instantReveal);
        _playerMetersShown = show;
    }

    private void SyncPlaylistVideoChromeFade()
    {
        SyncPlayerScratchEnabled();

        var hide = LibraryPlayerMode.HidesChromeForPlaylistVisual(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryBrowser.PlaylistVisualIsPdf,
            LibraryBrowser.PlaylistVisualIsVideo,
            _playlistVisualChromeBridge,
            _playlistVideoImmersivePause);
        if (LibraryPlayerMode.HidesChromeForVideoFileLaunch(_videoLaunchPlacement, IsLibraryMaximized))
        {
            hide = true;
        }

        ApplySilentSkipFromSettings();

        if (!hide)
        {
            // F 全画面はここでは解除しない（ユーザーの F でのみ戻す）。
            EnsurePlaylistVideoWindowRestored();
        }

        // プレイリスト動画の本再生開始／曲切替では A／T をオン（再生中はトグル可。ミニは記憶値）。
        var playlistVideoPlaying = !_videoLaunchPlacement
            && LibraryBrowser.PlaylistVisualIsVideo
            && LibraryBrowser.PlaylistVisualPlaying;
        var playlistVideoPath = playlistVideoPlaying
            ? _activeSession?.Document.SourcePath
            : null;
        if (LibraryPlayerMode.ResetsPlaylistVideoChromeOnPlay(
                _videoLaunchPlacement,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _playlistVideoChromePlayActive,
                playlistVideoPath,
                _playlistVideoChromePlayPath))
        {
            _playlistVideoHud = true;
            _playlistVideoTimecode = true;
        }

        _playlistVideoChromePlayActive = playlistVideoPlaying;
        _playlistVideoChromePlayPath = playlistVideoPath;

        // PDF 表示中は A／T を出さないだけ（フラグは触らない）。
        var hideAtOverlays = LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
                LibraryBrowser.PlaylistVisualIsPdf,
                hide)
            || (hide && !_playlistVideoHud);

        var visible = !hide;
        var instant = !IsLibraryMaximized
            || LibraryPlayerMode.HidesChromeForVideoFileLaunch(_videoLaunchPlacement, IsLibraryMaximized);
        if (_playlistVideoChromeVisible != visible || instant)
        {
            _playlistVideoChromeVisible = visible;
            LibraryPlayerMode.FadeElementOpacity(RootDock, 1, instant: true, hitTestVisible: true);
            foreach (var target in PlaylistVideoChromeTargets())
            {
                LibraryPlayerMode.FadeElementOpacity(
                    target,
                    visible ? 1 : 0,
                    instant,
                    hitTestVisible: visible,
                    seconds: LibraryPlayerMode.ChromeFadeSeconds);
            }
        }
        else if (visible)
        {
            // 状態フラグは見えるのに Opacity アニメで消えたまま、を起こさない。
            EnsurePlaylistVideoChromeOpaque();
        }

        ApplyPlaylistVideoHudFade(hideAtOverlays, instant);
        if (IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo)
        {
            EnsureLibraryVisualPlayheadTicker();
        }

        ApplyPlaylistVideoTimecodeFade(
            LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                hide,
                _playlistVideoTimecode,
                LibraryBrowser.PlaylistVisualIsVideo),
            instant);
        ApplyPlaylistVideoFileNameFade(
            LibraryPlayerMode.ShowsPlaylistVideoFileName(
                hide,
                _playlistVideoHud,
                LibraryBrowser.PlaylistVisualIsVideo),
            instant);
        // PDF ページ番号は常時出さない。本表示を抜けたら隠し、切替時だけトーストする。
        if (!LibraryPlayerMode.ShowsPlaylistPdfPageNumber(hide, LibraryBrowser.PlaylistVisualIsPdf))
        {
            HidePlaylistPdfPageNumber(instant);
        }
        if (!hide)
        {
            EnsurePlaylistVideoHudOpaque();
        }
        else if (_playlistVideoHud && LibraryBrowser.PlaylistVisualIsVideo)
        {
            // メーターだけ戻して波形が Opacity 0 のまま残るのを防ぐ（PDF では出さない）。
            EnsurePlaylistVideoHudOpaque();
        }

        SyncPlaylistVideoUiShadows(
            !hide,
            hideAtOverlays && LibraryBrowser.PlaylistVisualIsVideo);
    }

    private void EnsurePlaylistVideoWindowRestored()
    {
        // F 全画面中はウィンドウ寸法も触らない（停止やクローム復帰で勝手に戻さない）。
        if (_playlistVideoFullscreen)
        {
            return;
        }

        if (!IsLibraryMaximized)
        {
            return;
        }

        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        }

        // 全画面のモニター寸法が settings に残っていると、ステータスバーがタスクバー下に隠れる。
        if (IsLikelyExclusiveFullscreenSize())
        {
            RestorePlaylistVideoWindowBounds();
        }
    }

    private void RestorePlaylistVideoWindowBounds()
    {
        WindowState = WindowState.Normal;
        var bounds = _boundsBeforeVideoFullscreen;
        if (bounds.Width >= MinWidth
            && bounds.Height >= MinHeight
            && !IsMonitorCoveringBounds(bounds))
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(MinWidth, bounds.Width);
            Height = Math.Max(MinHeight, bounds.Height);
            if (_windowStateBeforeVideoFullscreen == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }

            PersistCurrentWindowPlacement();
            return;
        }

        if (TryApplyCurrentModePlacement() && !IsLikelyExclusiveFullscreenSize())
        {
            PersistCurrentWindowPlacement();
            return;
        }

        FitPlayerToWorkArea();
        PersistCurrentWindowPlacement();
    }

    private bool IsLikelyExclusiveFullscreenSize()
    {
        if (WindowState == WindowState.Maximized)
        {
            return false;
        }

        if (!WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            return false;
        }

        return Width >= monitor.Width - 4 && Height >= monitor.Height - 4;
    }

    private bool IsMonitorCoveringBounds(Rect bounds)
    {
        if (WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            return bounds.Width >= monitor.Width - 4 && bounds.Height >= monitor.Height - 4;
        }

        return bounds.Width >= SystemParameters.PrimaryScreenWidth - 4
            && bounds.Height >= SystemParameters.PrimaryScreenHeight - 4;
    }

    private void FitPlayerToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        if (WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            // モニター全面ではなく作業領域相当（タスクバー分を引いた高さ）に収める。
            work = new Rect(
                monitor.X,
                monitor.Y,
                monitor.Width,
                Math.Max(MinHeight, monitor.Height - 48));
        }

        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, work.Width * 0.9);
        Height = Math.Max(MinHeight, work.Height * 0.9);
        Left = work.X + Math.Max(0, (work.Width - Width) * 0.5);
        Top = work.Y + Math.Max(0, (work.Height - Height) * 0.5);
    }

    private void EnsurePlaylistVideoChromeOpaque()
    {
        LibraryPlayerMode.FadeElementOpacity(RootDock, 1, instant: true, hitTestVisible: true);
        foreach (var target in PlaylistVideoChromeTargets())
        {
            if (target.Opacity < 0.999 || !target.IsHitTestVisible)
            {
                LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
            }
        }

        if (StatusBarHost.Visibility != Visibility.Visible && !_libraryMinimalChrome)
        {
            StatusBarHost.Visibility = Visibility.Visible;
        }
    }

    private void EnsurePlaylistVideoHudOpaque()
    {
        foreach (var target in PlaylistVideoHudTargets())
        {
            // 波形はメーター非表示条件でも HUD オンなら出す。
            if (ReferenceEquals(target, WaveformHostBorder)
                || LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count))
            {
                if (target.Opacity < 0.999 || !target.IsHitTestVisible)
                {
                    LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
                }
            }
        }
    }

    private void ApplyPlaylistVideoHudFade(bool hideHud, bool instant)
    {
        foreach (var target in PlaylistVideoHudTargets())
        {
            LibraryPlayerMode.FadeElementOpacity(
                target,
                hideHud ? 0 : 1,
                instant,
                hitTestVisible: !hideHud,
                seconds: LibraryPlayerMode.ChromeFadeSeconds);
        }
    }

    private bool TryTogglePlaylistVideoFullscreen()
    {
        if (!IsLibraryMaximized)
        {
            return false;
        }

        // 解除は映像の有無にかかわらず受け付ける（再生終了後に全画面が残ったとき用）。
        if (_playlistVideoFullscreen)
        {
            SetPlaylistVideoFullscreen(false);
            return true;
        }

        if (!LibraryBrowser.PlaylistVisualShown
            || !(LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf))
        {
            return false;
        }

        SetPlaylistVideoFullscreen(true);
        return true;
    }

    private bool TryTogglePlaylistVideoHud()
    {
        if (!IsPlaylistVisualHudContext())
        {
            return false;
        }

        _playlistVideoHud = !_playlistVideoHud;
        ApplyPlaylistVideoHudFade(!_playlistVideoHud, instant: false);
        ApplyPlaylistVideoFileNameFade(
            LibraryPlayerMode.ShowsPlaylistVideoFileName(
                !_playlistVideoChromeVisible,
                _playlistVideoHud,
                LibraryBrowser.PlaylistVisualIsVideo),
            instant: false);
        SyncPlaylistVideoUiShadows(
            _playlistVideoChromeVisible,
            LibraryBrowser.PlaylistVisualIsVideo
                && LibraryBrowser.PlaylistVisualPlaying
                && !_playlistVideoHud);
        RememberVideoLaunchChromeIfNeeded();
        return true;
    }

    private bool TryTogglePlaylistVideoTimecode()
    {
        if (!IsLibraryMaximized
            || !LibraryBrowser.PlaylistVisualShown
            || !LibraryBrowser.PlaylistVisualIsVideo
            || !LibraryBrowser.PlaylistVisualPlaying)
        {
            return false;
        }

        _playlistVideoTimecode = !_playlistVideoTimecode;
        ApplyPlaylistVideoTimecodeFade(
            LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                isVideo: true),
            instant: false);
        RememberVideoLaunchChromeIfNeeded();
        return true;
    }

    private void ApplyPlaylistVideoTimecodeFade(bool show, bool instant)
    {
        if (show)
        {
            // T で出した瞬間にも窓比例を当てる（リサイズ前に Identity のまま残るのを防ぐ）。
            ApplyPlaylistVideoTimecodeOverlayScale(VideoMiniOverlayLayoutScale);
        }

        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoTimecode,
            show ? 1 : 0,
            instant,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void ApplyPlaylistVideoFileNameFade(bool show, bool instant)
    {
        if (show)
        {
            RefreshPlaylistVideoFileName();
        }
        else
        {
            PlaylistVideoFileName.RenderTransform = Transform.Identity;
        }

        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoFileName,
            show ? 1 : 0,
            instant,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
        if (show)
        {
            SchedulePlaylistVideoFileNamePlacement();
        }
    }

    /// <summary>ページ切替成功時。番号を出してから一定時間でフェードアウト。</summary>
    private void FlashPlaylistPdfPageNumber()
    {
        if (!LibraryPlayerMode.ShowsPlaylistPdfPageNumber(
                !_playlistVideoChromeVisible,
                LibraryBrowser.PlaylistVisualIsPdf))
        {
            return;
        }

        ApplyPlaylistPdfPageNumberOverlayScale(VideoMiniOverlayLayoutScale);
        RefreshPlaylistPdfPageNumber();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistPdfPageNumber,
            to: 1,
            instant: true,
            hitTestVisible: false);
        _playlistPdfPageNumberToastTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PdfPageNumberToastHoldSeconds),
        };
        _playlistPdfPageNumberToastTimer.Tick -= OnPlaylistPdfPageNumberToastTick;
        _playlistPdfPageNumberToastTimer.Tick += OnPlaylistPdfPageNumberToastTick;
        _playlistPdfPageNumberToastTimer.Stop();
        _playlistPdfPageNumberToastTimer.Start();
    }

    private void OnPlaylistPdfPageNumberToastTick(object? sender, EventArgs e)
    {
        _playlistPdfPageNumberToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistPdfPageNumber,
            to: 0,
            instant: false,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void HidePlaylistPdfPageNumber(bool instant)
    {
        _playlistPdfPageNumberToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistPdfPageNumber,
            to: 0,
            instant,
            hitTestVisible: false,
            seconds: instant ? null : LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void RefreshPlaylistPdfPageNumber()
    {
        PlaylistPdfPageNumberText.Text = LibraryPlayerMode.FormatPdfPageLabel(
            LibraryBrowser.PlaylistPdfPageIndex,
            LibraryBrowser.PlaylistPdfPageCount);
    }

    private void ApplyPlaylistPdfPageNumberOverlayScale(double scale)
    {
        var designHeight = DesignMetrics.PlaylistPdfPageNumberHostHeight;
        var designMargin = DesignMetrics.PlaylistPdfPageNumberMargin;
        if (!IsVideoMiniPlayerActive() || _playlistVideoFullscreen)
        {
            PlaylistPdfPageNumber.LayoutTransform = Transform.Identity;
            PlaylistPdfPageNumber.Height = designHeight;
            PlaylistPdfPageNumber.Margin = designMargin;
            return;
        }

        var s = Math.Clamp(
            scale,
            DesignMetrics.OverlayScaleMin,
            DesignMetrics.OverlayScaleMax);
        if (double.IsNaN(s) || double.IsInfinity(s) || s <= 0)
        {
            s = 1;
        }

        PlaylistPdfPageNumber.LayoutTransform = Transform.Identity;
        PlaylistPdfPageNumber.Height = designHeight * s;
        PlaylistPdfPageNumber.Margin = new Thickness(
            designMargin.Left * s,
            designMargin.Top * s,
            designMargin.Right * s,
            designMargin.Bottom * s);
    }

    private void RefreshPlaylistVideoFileName()
    {
        var text = _activeSession?.DisplayName
            ?? (_document?.SourcePath is { Length: > 0 } path
                ? System.IO.Path.GetFileName(path)
                : string.Empty);
        var source = _activeSession?.Document.SourcePath ?? _document?.SourcePath;
        var usingProxy = source is { Length: > 0 }
            && LibraryPlaylistDocuments.IsVideo(source)
            && VideoProxy.TryGetCached(source, out _);
        text = LibraryPlayerMode.FormatPlaylistVideoFileName(text, usingProxy);
        PlaylistVideoFileNameText.Text = text;
        PlaylistVideoFileNameShadow.Text = text;
    }

    private void SchedulePlaylistVideoFileNamePlacement()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, SyncPlaylistVideoFileNamePlacement);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, SyncPlaylistVideoFileNamePlacement);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, SyncPlaylistVideoFileNamePlacement);
        // A で HUD／ファイル名をフェードインした直後は Idle 時点でレイアウトが未確定なことがある。
        _playlistVideoFileNamePlaceTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.ChromeFadeSeconds + 0.05),
        };
        _playlistVideoFileNamePlaceTimer.Tick -= OnPlaylistVideoFileNamePlaceTimerTick;
        _playlistVideoFileNamePlaceTimer.Tick += OnPlaylistVideoFileNamePlaceTimerTick;
        _playlistVideoFileNamePlaceTimer.Stop();
        _playlistVideoFileNamePlaceTimer.Start();
    }

    private void OnPlaylistVideoFileNamePlaceTimerTick(object? sender, EventArgs e)
    {
        _playlistVideoFileNamePlaceTimer?.Stop();
        SyncPlaylistVideoFileNamePlacement();
    }

    /// <summary>中央配置のファイル名がラウドネスに被る分だけ左へずらす。</summary>
    private void SyncPlaylistVideoFileNamePlacement()
    {
        var margin = DesignMetrics.PlaylistVideoFileNameMargin;
        PlaylistVideoFileName.Margin = margin;
        // 前回のずらしを外してから測らないと二重に避ける。
        PlaylistVideoFileName.RenderTransform = Transform.Identity;
        // Opacity は見ない。A でフェードイン中は両方とも 0 近傍のまま測れず、初回だけ被る。
        if (!LibraryPlayerMode.ShowsPlaylistVideoFileName(
                !_playlistVideoChromeVisible,
                _playlistVideoHud,
                LibraryBrowser.PlaylistVisualIsVideo)
            || LoudnessMeter.Visibility != Visibility.Visible
            || RootChrome.ActualWidth < 8)
        {
            return;
        }

        PlaylistVideoFileName.UpdateLayout();
        LoudnessMeter.UpdateLayout();
        if (PlaylistVideoFileName.RenderSize.Width < 1
            || LoudnessMeter.RenderSize.Width < 1)
        {
            return;
        }

        Rect nameBox;
        Rect loudBox;
        try
        {
            nameBox = PlaylistVideoFileName.TransformToVisual(RootChrome)
                .TransformBounds(new Rect(PlaylistVideoFileName.RenderSize));
            loudBox = LoudnessMeter.TransformToVisual(RootChrome)
                .TransformBounds(new Rect(LoudnessMeter.RenderSize));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var dodge = LibraryPlayerMode.PlaylistVideoFileNameLoudnessDodge(
            nameBox,
            loudBox,
            minNameLeft: margin.Left,
            gap: DesignMetrics.PlaylistVideoFileNameLoudnessGap);
        if (dodge > 0.5)
        {
            PlaylistVideoFileName.RenderTransform = new TranslateTransform(-dodge, 0);
        }
    }

    private bool TryTogglePdfBackgroundPin()
    {
        if (!IsLibraryMaximized)
        {
            return false;
        }

        // PDF 表示中は常に今のページで固定し直し、即抜ける（既にオンでも 1 回で反映）。
        if (LibraryBrowser.PlaylistVisualIsPdf)
        {
            if (!LibraryBrowser.TryPinPdfBackground())
            {
                return false;
            }

            ExitPlaylistPdfView(restoreDimPreview: false);
            return true;
        }

        // 表示していないときの B は解除。
        if (LibraryBrowser.PdfBackgroundPinned)
        {
            LibraryBrowser.ClearPdfBackgroundPin();
            return true;
        }

        return false;
    }

    /// <summary>
    /// PDF の明るい表示を閉じる（Enter／Space／Escape）。暗いプレビューへ戻す。
    /// W 固定直後は <paramref name="restoreDimPreview"/> を false にし、静止背景だけ残す。
    /// </summary>
    private void ExitPlaylistPdfView(bool restoreDimPreview = true)
    {
        if (!LibraryBrowser.PlaylistVisualIsPdf)
        {
            return;
        }

        SyncPlaylistPdfCursorFromVisual();
        _libraryStopAfterTrack = false;
        SetPlaylistVideoFullscreen(false);
        if (restoreDimPreview
            && LibraryPlayerMode.RestoresPdfDimPreviewOnExit(LibraryBrowser.PdfBackgroundPinned))
        {
            // Hide すると背面の暗いプレビューまで消える。本再生と同じく dim へ戻す。
            LibraryBrowser.SetPlaylistVisualPlaying(false);
        }
        else
        {
            LibraryBrowser.HidePlaylistVisual();
        }

        Transport.SetPlaying(false);
        EnsurePlaylistVideoWindowRestored();
    }

    /// <summary>
    /// Escape／Q で本再生／イマーシブ一時停止／PDF 明るい表示から抜ける。
    /// F10 プレイリストは暗い 1/3 速へ。F8 ミニは静止のまま。
    /// </summary>
    private bool TryExitPlaylistRealPlayback()
    {
        if (LibraryBrowser.PlaylistVisualIsPdf && LibraryBrowser.PlaylistVisualPlaying)
        {
            ExitPlaylistPdfView();
            return true;
        }

        if (!LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(
                IsPlaybackActive(),
                _playlistVideoImmersivePause))
        {
            return false;
        }

        // 動画本再生・フレーム静止・イマーシブ一時停止以外（音声のみの再生）は Escape では止めない。
        if (!IsPlaylistVideoPlaying()
            && !_playlistVideoImmersivePause
            && !LibraryBrowser.PlaylistVisualFramePaused
            && !(IsVideoMiniPlayerActive() && IsPlaybackActive()))
        {
            return false;
        }

        StopVideoFrameStep();
        if (IsVideoMiniPlayerActive())
        {
            // F8 はプレイリストが無いので暗いプレビューへは落とさず、止めた位置で静止。
            PausePlaybackHere(immersiveVideoHold: true);
        }
        else
        {
            DropPlaylistVideoToDimPreview();
        }

        _libraryStopAfterTrack = false;
        return true;
    }

    private bool IsPlaylistVideoPlaying() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsVideo
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>
    /// 停止中の Enter を Space と同じ本再生／表示にする対象（動画／PDF）。
    /// 本再生中の Enter はその場停止（immersive）で、ここは使わない。
    /// </summary>
    private bool IsLibraryVisualTransportTarget()
    {
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        return session is not null && LibraryPlaylistDocuments.IsVisual(session.Document);
    }

    /// <summary>
    /// 本再生中、または ←→／テンキー 0 のフレーム一時停止中。
    /// 停止扱いにすると次曲選択が暗いプレビューになってしまうので、プレイリスト送りでは再生継続とみなす。
    /// </summary>
    private bool ContinuesLibraryPlaylistPlayback() =>
        IsPlaybackActive() || _playlistVideoImmersivePause;

    private bool IsPlaylistVisualHudContext() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualPlaying
        && LibraryBrowser.PlaylistVisualIsVideo;

    private void SetPlaylistVideoFullscreen(bool on)
    {
        if (_playlistVideoFullscreen == on)
        {
            return;
        }

        if (on)
        {
            PersistCurrentWindowPlacement();
            _windowStyleBeforeVideoFullscreen = WindowStyle == WindowStyle.None
                ? WindowStyle.SingleBorderWindow
                : WindowStyle;
            _resizeModeBeforeVideoFullscreen = ResizeMode == ResizeMode.NoResize
                ? ResizeMode.CanResize
                : ResizeMode;
            _windowStateBeforeVideoFullscreen = WindowState;
            _boundsBeforeVideoFullscreen = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            _playlistVideoFullscreen = true;
            ApplyWaveformFullscreenFrame();
            // 全画面寸法＋倍率 1 に列幅／行高を合わせ直す（縮小レイアウトのままだと見切れる）。
            ApplyLibraryMinimalChrome();
            SyncWindowProportionalOverlayScale();
            return;
        }

        // settings ではなく入る直前の寸法へ戻す（全画面中にモニター寸法が保存されるとステータスバーが隠れる）。
        // 全画面中に次の動画へ移っていたら、抜けたタイミングで原寸配置を反映する。
        _playlistVideoFullscreen = false;
        WindowStyle = _windowStyleBeforeVideoFullscreen == WindowStyle.None
            ? WindowStyle.SingleBorderWindow
            : _windowStyleBeforeVideoFullscreen;
        ResizeMode = _resizeModeBeforeVideoFullscreen == ResizeMode.NoResize
            ? ResizeMode.CanResize
            : _resizeModeBeforeVideoFullscreen;
        if (_videoLaunchPlacement)
        {
            ResizeMode = ResizeMode.CanResize;
        }

        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        if (_videoLaunchPlacementDeferred)
        {
            _videoLaunchPlacementDeferred = false;
            if (TryApplyVideoLaunchPlacement())
            {
                SyncWindowProportionalOverlayScale();
                return;
            }
        }

        RestorePlaylistVideoWindowBounds();
        SyncWindowProportionalOverlayScale();
    }

    /// <summary>動画再生中に隠す前面 UI。波形・レベル／スペアナ／ゴニオ／サラウンド／ラウドネスは残す。</summary>
    private UIElement[] PlaylistVideoChromeTargets() =>
        [
            StatusBarHost,
            WaapiBar,
            TipsPanel,
            LibraryBrowser,
            LibrarySplitter,
            MeterColumnSplitter,
            TransportBarHost,
            DocumentTabHost,
            HistoryStrip,
        ];

    private UIElement[] PlaylistVideoHudTargets() =>
        [WaveformHostBorder, LevelMeter, VectorScope, Spectrum, LoudnessMeter];

    private void SyncPlaylistVideoUiShadows(bool chromeVisible, bool hideHud)
    {
        var video = IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo;
        foreach (var target in PlaylistVideoChromeTargets())
        {
            LibraryPlayerMode.SetVideoUiShadow(target, video && chromeVisible);
        }

        foreach (var target in PlaylistVideoHudTargets())
        {
            LibraryPlayerMode.SetVideoUiShadow(target, video && !hideHud);
        }

        // ファイル名は XAML の文字シルエット影。ラベル全体への Effect は使わない。
        PlaylistVideoFileName.Effect = null;
        LibraryPlayerMode.SetVideoTextShadow(PlaylistVideoFileNameShadow, video && !hideHud);
        // タイムコードは T の表示中だけ影（A の HUD とは独立）。
        LibraryPlayerMode.SetVideoUiShadow(
            PlaylistVideoTimecode,
            video
            && LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                isVideo: true));
    }

    private void ApplyPlayerMeterFade(bool visible, bool instant)
    {
        var chromeHiddenVisual =
            LibraryPlayerMode.HidesChromeForVideoFileLaunch(_videoLaunchPlacement, IsLibraryMaximized)
            || LibraryPlayerMode.HidesChromeForPlaylistVisual(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualPlaying,
                LibraryBrowser.PlaylistVisualIsPdf,
                LibraryBrowser.PlaylistVisualIsVideo,
                _playlistVisualChromeBridge,
                _playlistVideoImmersivePause);
        if (visible
            && chromeHiddenVisual
            && (!_playlistVideoHud
                || LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
                    LibraryBrowser.PlaylistVisualIsPdf,
                    chromeHidden: true)))
        {
            visible = false;
        }

        var to = visible ? 1d : 0d;
        foreach (var target in PlayerMeterFadeTargets())
        {
            LibraryPlayerMode.FadeElementOpacity(target, to, instant, hitTestVisible: visible);
        }
    }

    private UIElement[] PlayerMeterFadeTargets() =>
        _playlistVideoChromeVisible
            ? [LevelMeter, VectorScope, Spectrum, LoudnessMeter, TimeScrollStrip]
            : [LevelMeter, VectorScope, Spectrum, LoudnessMeter];

    /// <summary>
    /// プレイヤー中はクロムの塗りを外し、ウィンドウ全体のジャケットウォッシュを透かす。
    /// 入るときはグローを出してから塗りを外し、出るときは先に塗りを戻す。
    /// </summary>
    private void ApplyLibraryWashChrome(bool show)
    {
        if (show)
        {
            StatusBarHost.Background = Brushes.Transparent;
            TipsPanel.Background = Brushes.Transparent;
            WaveformHostBorder.Background = Brushes.Transparent;
            WaveformTileHost.Background = null;
            MeterColumn.Background = Brushes.Transparent;
            MeterTopSlot.Background = Brushes.Transparent;
            TransportChromeHost.Background = Brushes.Transparent;
            DocumentTabHost.Background = Brushes.Transparent;
            DocumentTabScroll.Background = Brushes.Transparent;
            DocumentTabs.Background = Brushes.Transparent;
            TransportBarHost.Background = Brushes.Transparent;
            LoudnessMeter.Background = Brushes.Transparent;
            WaapiBar.Background = Brushes.Transparent;
        }
        else
        {
            StatusBarHost.SetResourceReference(Border.BackgroundProperty, "StatusBarBackBrush");
            TipsPanel.SetResourceReference(Border.BackgroundProperty, "WaapiBarBackBrush");
            WaveformHostBorder.SetResourceReference(Border.BackgroundProperty, "WaveformBackBrush");
            WaveformTileHost.SetResourceReference(Panel.BackgroundProperty, "WaveformBackBrush");
            MeterColumn.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            MeterTopSlot.SetResourceReference(Border.BackgroundProperty, "TransportBackBrush");
            TransportChromeHost.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            DocumentTabHost.SetResourceReference(Border.BackgroundProperty, "TransportBackBrush");
            TransportBarHost.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            LoudnessMeter.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            WaapiBar.SetResourceReference(Control.BackgroundProperty, "WaapiBarBackBrush");
        }

        LevelMeter.WashThrough = show;
        VectorScope.WashThrough = show;
        Spectrum.WashThrough = show;
        Transport.SetWashThrough(show);
        if (_waapiToggle is not null)
        {
            _waapiToggle.WashThrough = show;
            _waapiToggle.InvalidateVisual();
        }

        LevelMeter.InvalidateVisual();
        VectorScope.InvalidateVisual();
        Spectrum.InvalidateVisual();
    }

    /// <summary>
    /// プレイヤー帯のレイアウトが決まってから波形を描き直す。
    /// エディタの拡大表示のまま描くと、133px の帯では画面外に残って空に見える。
    /// ウィンドウサイズの復元はクロム適用のあとなので、Loaded だけでは間に合わない。
    /// </summary>
    private void ScheduleLibraryWaveformPaint()
    {
        var ticket = ++_libraryWavePaintTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PaintLibraryWaveform(ticket));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PaintLibraryWaveform(ticket));
    }

    private void PaintLibraryWaveform(int ticket)
    {
        if (ticket != _libraryWavePaintTicket || !IsLibraryMaximized || Waveform.Document is null)
        {
            return;
        }

        Waveform.UpdateLayout();
        if (Waveform.ActualWidth < 8 || Waveform.ActualHeight < 8)
        {
            return;
        }

        Waveform.Refresh();
    }

    /// <summary>
    /// プレイヤー退出。再生は先に止めてからクロームを戻し、そのあとフル PCM へ昇格する。
    /// 133px 帯のままだとタイルが収まらず 1 本に落ちるので、レイアウト後に並べ直す。
    /// </summary>
    private async Task LeaveLibraryMaximizeAsync()
    {
        var active = _activeSession;
        CancelLibraryPeakJobs();
        CancelLibraryGapless();
        if (active is not null)
        {
            active.PlayheadFrame = Waveform.PlayheadFrame;
            active.Document.CursorFrame = Waveform.PlayheadFrame;
        }

        // レイアウトと波形の初回描画を、フルデコードより先に通す。
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (IsLibraryMaximized)
        {
            return;
        }

        if (active is not null
            && ReferenceEquals(active, _activeSession)
            && LibraryPlayerMode.NeedsEditorPcmUpgrade(active.Document))
        {
            await EnsureLibrarySessionLoadedAsync(active).ConfigureAwait(true);
            if (IsLibraryMaximized)
            {
                return;
            }
        }

        RestoreLibrarySuspendedTileArrange();
        await UpgradeKeptLibrarySessionsForEditorAsync(active).ConfigureAwait(true);
        if (IsLibraryMaximized)
        {
            return;
        }

        UpgradeLibraryPeaksForEditor();
        ForEachWaveform(view => view.Refresh());
        Overview.Refresh();
    }

    /// <summary>選択して残したファイルを、表示中以外もフル PCM へ。タイルが空／1 レーンのまま残らない。</summary>
    private async Task UpgradeKeptLibrarySessionsForEditorAsync(DocumentSession? active)
    {
        var sessions = _sessions.ToArray();
        foreach (var session in sessions)
        {
            if (IsLibraryMaximized || !_sessions.Contains(session))
            {
                return;
            }

            if (ReferenceEquals(session, active)
                || !LibraryPlayerMode.NeedsEditorPcmUpgrade(session.Document))
            {
                continue;
            }

            await EnsureLibrarySessionLoadedAsync(session, bind: false).ConfigureAwait(true);
        }
    }

    private void CancelLibraryPeakJobs()
    {
        _libraryPeakGeneration++;
        _libraryPeakJobs.Clear();
        _libraryPeakTasks.Clear();
        foreach (var job in _libraryPeakJobCts.Values)
        {
            try
            {
                job.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _libraryPeakJobCts.Clear();
        var previous = _libraryPeakCts;
        _libraryPeakCts = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    /// <summary>
    /// 表示中と次曲以外の走査だけ止める。世代は進めない（今の波形を途中で捨てない）。
    /// </summary>
    private void TrimUnwantedLibraryPeakJobs()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        var playing = _activeSession?.Document;
        var next = _gaplessTarget?.Document;
        foreach (var pair in _libraryPeakJobCts)
        {
            if (LibraryPlayerMode.KeepPeakJob(pair.Key, playing, next))
            {
                continue;
            }

            try
            {
                pair.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void UpgradeLibraryPeaksForEditor()
    {
        foreach (var session in _sessions)
        {
            if (!LibraryPlayerMode.NeedsEditorPeakUpgrade(session.Document))
            {
                continue;
            }

            _ = FillLibraryPeaksAsync(session);
        }
    }

    private void RefreshTransportCommandsEnabled() =>
        Transport.SetCommandsEnabled(_document is not null, allowEdit: !IsLibraryMaximized);

    private void ApplyLibraryWaapi(bool show)
    {
        if (show)
        {
            if (!_libraryWaapiSuspended)
            {
                _restoreWaapiAfterLibrary = _waapiPanelVisible;
                _libraryWaapiSuspended = true;
                if (_waapiPanelVisible)
                {
                    _waapiPanelVisible = false;
                    ApplyWaapiPanelVisible();
                }
            }

            SetWaapiToggleEnabled(false);
            return;
        }

        if (!_libraryWaapiSuspended)
        {
            SetWaapiToggleEnabled(true);
            return;
        }

        var restore = _restoreWaapiAfterLibrary;
        _libraryWaapiSuspended = false;
        _restoreWaapiAfterLibrary = false;
        SetWaapiToggleEnabled(true);
        if (restore)
        {
            ShowWaapiPanel();
        }
    }

    private void SetWaapiToggleEnabled(bool enabled)
    {
        if (_waapiToggle is null)
        {
            return;
        }

        _waapiToggle.IsEnabled = enabled;
        _waapiToggle.InvalidateVisual();
    }

    private void ApplyLibraryPlayerHost(bool show)
    {
        if (show)
        {
            // Collapsed だけでは足りない。133px 帯への SizeChanged で EnsureUsableTileArrange が
            // 縦→格子へ張り直し、プレイヤー下部にタイルが復活する。
            _librarySuspendedTileArrange = _tileArrange;
            if (_tileMode || _tilePanes.Count > 0 || _tileGrid is not null)
            {
                ExitWaveformTileMode(bindPrimary: false);
            }
            else
            {
                SingleWaveformHost.Visibility = Visibility.Visible;
                PrimaryWaveform.Visibility = Visibility.Visible;
                _tileActiveView = null;
            }

            // F8／F9↔F10 のクローム切替で毎回 Bind すると ResetWorkspaceInteraction が再生を止める。
            if (_activeSession is not null)
            {
                var sameDocument = ReferenceEquals(Waveform.Document, _activeSession.Document);
                if (_libraryPlayerHostActive && sameDocument)
                {
                    if (IsPlaybackActive())
                    {
                        AttachPlaybackToActiveWaveform();
                    }

                    ScheduleLibraryWaveformPaint();
                }
                else if (sameDocument && IsPlaybackActive())
                {
                    Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
                    Waveform.ResetTimeZoom();
                    Waveform.ResetAmpZoom();
                    Waveform.LoopEnabled = _activeSession.LoopEnabled;
                    AttachPlaybackToActiveWaveform();
                    ScheduleLibraryWaveformPaint();
                }
                else
                {
                    BindSingleWorkspace(_activeSession);
                }
            }

            _libraryPlayerHostActive = true;
            return;
        }

        _libraryPlayerHostActive = false;
        ClearPlaylistVideoImmersivePause();
        LibraryBrowser.HidePlaylistVisual();
        // タイルは LeaveLibraryMaximizeAsync → RestoreLibrarySuspendedTileArrange で付け直す。
    }

    /// <summary>
    /// F10 で外したタイルを、エディタのレイアウトが戻ってから付け直す。
    /// 退避が無ければ設定の複数ファイル配置へ。
    /// </summary>
    private void RestoreLibrarySuspendedTileArrange()
    {
        var suspended = _librarySuspendedTileArrange;
        _librarySuspendedTileArrange = WaveformTileArrange.Off;
        if (suspended != WaveformTileArrange.Off)
        {
            RestoreWaveformTileArrange(WaveformTileLayout.Format(suspended));
            if (_tileMode)
            {
                return;
            }
        }

        ApplyPreferredMultiFileArrange(_sessions.Count);
    }

    private void RefreshLibraryBrowser()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        LibraryBrowser.SetSessions(_sessions, _activeSession);
        ShowLibraryArtworkOrClear(_activeSession);
    }

    /// <summary>
    /// プレイヤーを抜けるとき、リストで選んだファイルだけ残す。保存確認でキャンセルしたら false。
    /// </summary>
    private bool KeepOnlyLibrarySelectedSessions()
    {
        var selected = LibraryBrowser.SelectedSessions;
        var current = LibraryBrowser.SelectedSession ?? _activeSession;
        var keep = LibraryPlayerMode.ExcludeEditorBlocked(
            LibraryPlayerMode.SessionsToKeep(selected, current));
        if (current is not null && LibraryPlaylistDocuments.BlocksEditor(current.Document))
        {
            current = keep.Length > 0 ? keep[0] : null;
        }
        var drop = LibraryPlayerMode.SessionsToDrop(_sessions, keep);
        if (drop.Length == 0)
        {
            ApplyLibraryHandoffSelection(keep, current);
            return true;
        }

        var dirty = new List<DocumentSession>();
        var clean = new List<DocumentSession>();
        foreach (var session in drop)
        {
            if (session.Document.IsDirty)
            {
                dirty.Add(session);
            }
            else
            {
                clean.Add(session);
            }
        }

        // 未編集は 1 件ずつ CloseSession しない。タブ再構築の繰り返しが再生中に重い。
        if (clean.Count > 0)
        {
            DropLibrarySessionsFast(clean);
        }

        if (dirty.Count > 0)
        {
            var cancelled = false;
            RunCloseBatch(dirty, () =>
            {
                foreach (var session in dirty)
                {
                    if (ReferenceEquals(session, _activeSession))
                    {
                        continue;
                    }

                    if (!CloseSession(session, rememberClosed: !session.Document.IsDeferredLoad))
                    {
                        cancelled = true;
                        return;
                    }
                }

                if (_activeSession is { } active
                    && dirty.Contains(active)
                    && !CloseSession(active, rememberClosed: !active.Document.IsDeferredLoad))
                {
                    cancelled = true;
                }
            });

            if (cancelled)
            {
                return false;
            }
        }

        ApplyLibraryHandoffSelection(keep, current);
        if (_tileMode)
        {
            DropStaleTilePanes();
        }

        return true;
    }

    private void DropLibrarySessionsFast(IReadOnlyList<DocumentSession> drop)
    {
        var dropSet = drop as HashSet<DocumentSession> ?? [.. drop];
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            var session = _sessions[i];
            if (!dropSet.Contains(session))
            {
                continue;
            }

            _selectedTabs.Remove(session);
            if (ReferenceEquals(_tabSelectionAnchor, session))
            {
                _tabSelectionAnchor = null;
            }

            if (!session.Document.IsDeferredLoad)
            {
                RememberClosedTab(session, i);
            }

            _sessions.RemoveAt(i);
        }
    }

    private void ApplyLibraryHandoffSelection(
        IReadOnlyList<DocumentSession> keep,
        DocumentSession? current)
    {
        var remaining = new List<DocumentSession>();
        foreach (var session in _sessions)
        {
            foreach (var item in keep)
            {
                if (ReferenceEquals(item, session))
                {
                    remaining.Add(session);
                    break;
                }
            }
        }

        // 動画／PDF を除外した結果が空なら、旧 _activeSession（映像）へフォールバックしない。
        current = LibraryPlayerMode.ResolveLibraryHandoffCurrent(remaining, current);

        if (current is null)
        {
            if (_activeSession is not null)
            {
                BindWorkspace(null);
            }
        }
        else if (!ReferenceEquals(_activeSession, current))
        {
            BindWorkspace(current);
        }

        // プレイヤーでの複数選択はエディタへ持ち越さない。
        _selectedTabs.Clear();
        _tabSelectionAnchor = current;
        RebuildTabBar();
        RefreshTileChrome();
    }

    private void LibraryBrowser_SessionActivated(object? sender, DocumentSession session)
    {
        // フォルダ再帰追加中は Preview／Play しない（先頭行の自動選択で打ち切られるのを防ぐ）。
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        // フォルダ再生マーカーがあるときは、最初に登録した曲以外へ再生を付け替えない。
        if (_libraryFolderPlaySession is not null)
        {
            if (ReferenceEquals(session, _libraryFolderPlaySession))
            {
                if (!IsPlaybackActive())
                {
                    PreviewLibrarySession(session);
                }
                else
                {
                    ShowLibraryArtwork(session);
                }
            }

            return;
        }

        // 停止中のクリックや選択は再生しない。選択曲の波形だけ出す。
        // 本再生中／フレーム一時停止中だけその曲へ切り替える（一時停止を暗いプレビューに落とさない）。
        if (!ContinuesLibraryPlaylistPlayback())
        {
            PreviewLibrarySession(session);
            return;
        }

        _ = PlayLibrarySessionAsync(session);
    }

    private void LibraryBrowser_SessionPlayRequested(object? sender, DocumentSession session)
    {
        _ = PlayLibrarySessionAsync(session);
    }

    private Task RegisterLibraryPathsAsync(IReadOnlyList<string> paths) =>
        RegisterLibraryPathsAsync(paths, play: true);

    private async Task RegisterLibraryPathsAsync(IReadOnlyList<string> paths, bool play)
    {
        // 進行中のフォルダ追加を止めて、起動・ドロップを優先する。
        CancelLibraryFolderShow();
        var playFirst = play && (_libraryPlayFirstPending || _sessions.Count == 0);
        DocumentSession? opened = null;
        DocumentSession? existingFirst = null;
        var showProgress = paths.Count > 1;
        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (showProgress)
                {
                    SetOpenStatus(i + 1, paths.Count, Path.GetFileName(path) ?? path);
                }

                var existing = FindSessionByPath(path);
                if (existing is not null)
                {
                    existingFirst ??= existing;
                    continue;
                }

                var session = await Task.Run(() =>
                {
                    var document = AudioDocument.CreateDeferred(path);
                    AudioTagProbe.Ensure(document);
                    return new DocumentSession(document);
                }).ConfigureAwait(true);

                _sessions.Add(session);
                opened ??= session;

                // 1 件ごとに UI へ制御を返す（キー連打・描画を止めない）。
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }

            RebuildTabBar();
            if (!play)
            {
                // 追加だけ。既存の選択は動かさない。空リストへ足したときだけ 1 行目を選び、停止中プレビューを出す。
                if (IsLibraryMaximized)
                {
                    var keep = LibraryBrowser.SelectedSession;
                    var keepMany = LibraryBrowser.SelectedSessions;
                    if (keep is null)
                    {
                        keep = LibraryPlayerMode.FirstSession(_sessions);
                        if (keep is not null)
                        {
                            keepMany = [keep];
                        }
                    }

                    LibraryBrowser.SetSessions(_sessions, keep, keepMany);
                    PreviewIdleLibrarySession(keep);
                }

                SyncPlayerMeterFade();
                return;
            }

            if (playFirst)
            {
                SelectAndPlayFirstLibraryTrack();
                _libraryPlayFirstPending = false;
                SyncPlayerMeterFade();
                return;
            }

            var active = LaunchFiles.PreferOpened(opened, existingFirst);
            if (IsLibraryMaximized && active is not null)
            {
                LibraryBrowser.SetSessions(_sessions, active, [active]);
            }

            if (active is not null)
            {
                _ = PlayLibrarySessionAsync(active);
            }

            SyncPlayerMeterFade();
        }
        finally
        {
            if (showProgress)
            {
                ClearOpenStatus();
            }
        }
    }

    /// <summary>
    /// リスト選択をプレイリストから外す。ファイルの削除はしない。
    /// 未編集はまとめて外す。1 件ずつ閉じるとタブとリストの再構築が曲数分走る。
    /// </summary>
    private void RemoveLibrarySelectedFromList()
    {
        // 前面 UI 隠れ中は Delete／メニューの「プレイリストからクリア」も効かせない。
        if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(_playlistVideoChromeVisible))
        {
            return;
        }

        var selected = LibraryBrowser.SelectedSessions;
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var session in selected)
        {
            if (session.Document.IsDirty)
            {
                RemoveLibrarySelectedFromListAsking(selected);
                return;
            }
        }

        var dropSet = new HashSet<DocumentSession>(selected);
        var removedSessions = new DocumentSession[selected.Length];
        var removedIndices = new int[selected.Length];
        var removedCount = 0;
        foreach (var session in selected)
        {
            var index = _sessions.IndexOf(session);
            if (index < 0)
            {
                continue;
            }

            removedSessions[removedCount] = session;
            removedIndices[removedCount] = index;
            removedCount++;
        }

        if (removedCount == 0)
        {
            return;
        }

        if (removedCount != removedSessions.Length)
        {
            Array.Resize(ref removedSessions, removedCount);
            Array.Resize(ref removedIndices, removedCount);
        }

        var next = NextLibrarySessionAfter(dropSet);
        var resumePlayback = IsPlaybackActive()
            && _activeSession is { } playing
            && dropSet.Contains(playing);
        if (resumePlayback)
        {
            StopPlayback();
        }

        ReleaseLibraryStreamsForRemoved(dropSet);
        CancelLibraryPeakJobsFor(dropSet);

        foreach (var session in selected)
        {
            _selectedTabs.Remove(session);
            if (ReferenceEquals(_tabSelectionAnchor, session))
            {
                _tabSelectionAnchor = null;
            }
        }

        _libraryPlaylistUndo.Push(removedSessions, removedIndices);
        _sessions.RemoveAll(dropSet.Contains);
        DetachTabItems(dropSet);

        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            EndLibraryBackgroundPlayback();
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);
            RefreshStatus();
            SyncPlayerMeterFade();
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        next ??= _sessions[0];
        LibraryBrowser.SetSessions(_sessions, next, [next]);
        RefreshStatus();
        SyncPlayerMeterFade();
        if (resumePlayback)
        {
            _ = PlayLibrarySessionAsync(next);
            return;
        }

        if (IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }

        PreviewLibrarySession(next);
    }

    /// <summary>
    /// 外した曲の再生／先読みストリームを閉じる。Pause だけではハンドルが残る。
    /// </summary>
    private void ReleaseLibraryStreamsForRemoved(HashSet<DocumentSession> dropSet)
    {
        var dropGapless = _gaplessTarget is not null && dropSet.Contains(_gaplessTarget);
        var dropActive = _activeSession is { } active && dropSet.Contains(active);
        if (dropGapless || dropActive)
        {
            CancelLibraryGapless();
        }

        if (WillClearAllLibrarySessions(dropSet) || PlayerHoldsStreamFor(dropSet))
        {
            CancelLibraryGapless();
            _player.ReleaseStreamSource();
        }
    }

    private bool WillClearAllLibrarySessions(HashSet<DocumentSession> dropSet)
    {
        if (_sessions.Count == 0)
        {
            return true;
        }

        foreach (var session in _sessions)
        {
            if (!dropSet.Contains(session))
            {
                return false;
            }
        }

        return true;
    }

    private bool PlayerHoldsStreamFor(IEnumerable<DocumentSession> sessions)
    {
        if (!_player.IsStreamBound)
        {
            return false;
        }

        foreach (var session in sessions)
        {
            if (_player.IsBoundTo(session.Document))
            {
                return true;
            }
        }

        return false;
    }

    private void CancelLibraryPeakJobsFor(IEnumerable<DocumentSession> sessions)
    {
        foreach (var session in sessions)
        {
            var document = session.Document;
            if (!_libraryPeakJobCts.TryGetValue(document, out var job))
            {
                continue;
            }

            try
            {
                job.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    /// <summary>プレイヤー中の Ctrl+Z。Delete で外した曲を戻す。</summary>
    private bool TryUndoLibraryPlaylistRemove()
    {
        if (!IsLibraryMaximized || !_libraryPlaylistUndo.TryPop(out var sessions, out var indices))
        {
            return false;
        }

        LibraryPlaylistRemoveUndo.RestoreInto(_sessions, sessions, indices);
        LibraryBrowser.SetSessions(_sessions, sessions[0], sessions);
        RefreshStatus();
        SyncPlayerMeterFade();
        if (IsPlaybackActive())
        {
            LibraryBrowser.RequestListFocus();
            return true;
        }

        PreviewLibrarySession(sessions[0]);
        LibraryBrowser.RequestListFocus();
        return true;
    }

    private DocumentSession? NextLibrarySessionAfter(HashSet<DocumentSession> dropSet)
    {
        var anchor = LibraryBrowser.SelectedSession ?? _activeSession;
        if (anchor is null)
        {
            return null;
        }

        var index = _sessions.IndexOf(anchor);
        for (var i = index + 1; i < _sessions.Count; i++)
        {
            if (!dropSet.Contains(_sessions[i]))
            {
                return _sessions[i];
            }
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (!dropSet.Contains(_sessions[i]))
            {
                return _sessions[i];
            }
        }

        return null;
    }

    private void DetachTabItems(HashSet<DocumentSession> drop)
    {
        for (var i = DocumentTabs.Children.Count - 1; i >= 0; i--)
        {
            if (DocumentTabs.Children[i] is FrameworkElement { Tag: DocumentSession session }
                && drop.Contains(session))
            {
                DocumentTabs.Children.RemoveAt(i);
            }
        }
    }

    private void RemoveLibrarySelectedFromListAsking(DocumentSession[] drop)
    {
        var dropSet = new HashSet<DocumentSession>(drop);
        DocumentSession? next = NextLibrarySessionAfter(dropSet);
        var resumePlayback = IsPlaybackActive()
            && _activeSession is { } playing
            && dropSet.Contains(playing);
        if (resumePlayback)
        {
            StopPlayback();
        }

        ReleaseLibraryStreamsForRemoved(dropSet);
        CancelLibraryPeakJobsFor(dropSet);

        var cancelled = false;
        RunCloseBatch(drop, () =>
        {
            foreach (var session in drop)
            {
                if (ReferenceEquals(session, _activeSession))
                {
                    continue;
                }

                if (!CloseSession(session, rememberClosed: false))
                {
                    cancelled = true;
                    return;
                }
            }

            if (_activeSession is { } active
                && dropSet.Contains(active)
                && !CloseSession(active, rememberClosed: false))
            {
                cancelled = true;
            }
        });

        if (cancelled)
        {
            SyncPlayerMeterFade();
            return;
        }

        if (_sessions.Count == 0)
        {
            EndLibraryBackgroundPlayback();
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);
            SyncPlayerMeterFade();
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        next ??= _sessions[0];
        LibraryBrowser.SetSessions(_sessions, next, [next]);
        SyncPlayerMeterFade();
        if (resumePlayback)
        {
            _ = PlayLibrarySessionAsync(next);
            return;
        }

        if (IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }

        PreviewLibrarySession(next);
    }

    /// <summary>プレイヤー突入の一覧。再生と背景の差し替えはしない。</summary>
    private void SelectLibraryFirstTrackForEnter()
    {
        var first = LibraryPlayerMode.FirstSession(_sessions);
        IReadOnlyList<DocumentSession> selected = first is null ? [] : [first];
        LibraryBrowser.SetSessions(_sessions, first, selected);
        if (first is not null)
        {
            _libraryHoldJacketWash = false;
            LibraryBrowser.RequestListFocus();
        }
    }

    /// <summary>
    /// 一覧と波形を描いてから再生する。描画完了前に音が始まらないようにする。
    /// 動いている背景は描き直さず、そのまま続ける。
    /// </summary>
    private void ScheduleLibraryEnterPlayback()
    {
        if (!_libraryPlayFirstPending)
        {
            return;
        }

        var generation = ++_libraryEnterPlayGeneration;
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            () => _ = PlayLibraryEnterAfterPaintAsync(generation));
    }

    private async Task PlayLibraryEnterAfterPaintAsync(int generation)
    {
        if (!LibraryEnterPlaybackStillPending(generation))
        {
            return;
        }

        var first = LibraryPlayerMode.FirstSession(_sessions);
        var keepGlow = LibraryBrowser.AmbientGlowAnimating;
        if (first is not null)
        {
            await EnsureLibrarySessionLoadedAsync(first).ConfigureAwait(true);
            if (!LibraryEnterPlaybackStillPending(generation))
            {
                return;
            }

            if (!first.Document.IsDeferredLoad && ReferenceEquals(_libraryLoadSession, first))
            {
                LibraryBrowser.SelectSessionQuiet(first);
                ApplyLoadedLibrarySession(first);
            }
        }

        await WaitForPresentedFrameAsync().ConfigureAwait(true);
        if (!LibraryEnterPlaybackStillPending(generation))
        {
            return;
        }

        if (_sessions.Count > 0)
        {
            _libraryPlayFirstPending = false;
        }

        await PlayLibrarySessionAsync(first, keepAmbientGlow: keepGlow).ConfigureAwait(true);
    }

    private bool LibraryEnterPlaybackStillPending(int generation) =>
        generation == _libraryEnterPlayGeneration && IsLibraryMaximized && _libraryPlayFirstPending;

    /// <summary>次のフレームの描画パスが走ったあとで続ける。</summary>
    private static Task WaitForPresentedFrameAsync()
    {
        var done = new TaskCompletionSource();
        EventHandler rendered = null!;
        rendered = (_, _) =>
        {
            CompositionTarget.Rendering -= rendered;
            Dispatcher.CurrentDispatcher.BeginInvoke(
                () => done.TrySetResult(),
                DispatcherPriority.Normal);
        };
        CompositionTarget.Rendering += rendered;
        return done.Task;
    }

    private void SelectAndPlayFirstLibraryTrack()
    {
        var first = LibraryPlayerMode.FirstSession(_sessions);
        var selected = first is null
            ? (IReadOnlyList<DocumentSession>)[]
            : [first];
        if (first is not null)
        {
            ScanLibraryArtwork(first);
        }

        LibraryBrowser.SetSessions(_sessions, first, selected);
        LibraryBrowser.SetArtwork(first?.Document, keepCurrentIfEmpty: first is null && _libraryHoldJacketWash);
        if (first is not null)
        {
            _libraryHoldJacketWash = false;
            LibraryBrowser.RequestListFocus();
            PreviewLibrarySession(first);
        }

        _ = PlayLibrarySessionAsync(first);
    }

    private void ShowLibraryArtworkOrClear(DocumentSession? session)
    {
        if (session is null)
        {
            LibraryBrowser.SetArtwork(null, keepCurrentIfEmpty: _libraryHoldJacketWash);
            return;
        }

        _libraryHoldJacketWash = false;
        PreviewLibrarySession(session);
    }

    /// <summary>
    /// 停止中だけ選択曲の暗いプレビュー／波形を出す。再生中／フレーム一時停止中は触らない。
    /// </summary>
    private void PreviewIdleLibrarySession(DocumentSession? session)
    {
        if (session is null || ContinuesLibraryPlaylistPlayback())
        {
            return;
        }

        PreviewLibrarySession(session);
    }

    /// <summary>
    /// 停止中に選択曲のジャケットと波形を出す。再生は始めない。
    /// </summary>
    private void PreviewLibrarySession(DocumentSession session)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        if (LibraryPlaylistDocuments.IsVisual(session.Document))
        {
            if (!IsPlaybackActive())
            {
                // 映像の Open／表示を最優先。波形・タグ・ジャケットはその後。
                // ホバー／選択プレビューはプロキシを待たず原ファイルでも開く（本再生で必要なら作る）。
                // Shift+Enter 追加は SessionActivated を飛ばすので、呼び出し側がここで開く。
                ActivateVisualLibraryMeta(session);
                LibraryBrowser.ShowPlaylistVisual(session, play: false);
                ApplyLoadedLibrarySession(session);
                EnsureLibraryVisualPlayheadTicker();
                _ = FillLibraryPeaksAsync(session);
                ShowLibraryArtwork(session);
            }

            return;
        }

        ShowLibraryArtwork(session);
        LibraryBrowser.HidePlaylistVisual();
        if (IsPlaybackActive())
        {
            return;
        }

        _ = ShowLibrarySessionWaveformAsync(session);
    }

    private async Task ShowLibrarySessionWaveformAsync(DocumentSession session)
    {
        await EnsureLibrarySessionLoadedAsync(session, bind: true).ConfigureAwait(true);
        if (!ReferenceEquals(_libraryLoadSession, session)
            || !IsLibraryMaximized
            || IsPlaybackActive())
        {
            return;
        }

        if (!ReferenceEquals(Waveform.Document, session.Document)
            || !ReferenceEquals(_activeSession, session))
        {
            ApplyLoadedLibrarySession(session);
        }
        else
        {
            ScheduleLibraryWaveformPaint();
        }

        _ = FillLibraryPeaksAsync(session);
    }

    private void ProbeLibraryTags()
    {
        foreach (var session in _sessions)
        {
            AudioTagProbe.Ensure(session.Document);
        }
    }

    private void TryPlayLibraryAfterArrowRelease()
    {
        if (!_libraryPlayOnArrowRelease || !IsLibraryMaximized)
        {
            _libraryPlayOnArrowRelease = false;
            return;
        }

        if (Keyboard.IsKeyDown(Key.Up)
            || Keyboard.IsKeyDown(Key.Down)
            || Keyboard.IsKeyDown(Key.Home)
            || Keyboard.IsKeyDown(Key.End)
            || Keyboard.IsKeyDown(Key.PageUp)
            || Keyboard.IsKeyDown(Key.PageDown))
        {
            return;
        }

        _libraryPlayOnArrowRelease = false;
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (!ContinuesLibraryPlaylistPlayback())
        {
            if (session is not null)
            {
                PreviewLibrarySession(session);
            }

            return;
        }

        _ = PlayLibrarySessionAsync(session);
    }

    private void CancelLibraryGapless()
    {
        _gaplessToken++;
        _gaplessTarget = null;
        _gaplessInFlight = false;
        _gaplessFailed = false;
        _player.ClearGaplessNext();
    }

    private void ScheduleLibraryGaplessPrefetch()
    {
        if (!IsLibraryMaximized
            || !IsPlaybackActive()
            || _activeSession is null
            || _libraryStopAfterTrack
            || LibraryPlaylistDocuments.IsVisual(_activeSession.Document))
        {
            return;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null || LibraryPlaylistDocuments.IsVisual(next.Document))
        {
            return;
        }

        if (ReferenceEquals(next, _gaplessTarget)
            && (_gaplessInFlight || _gaplessFailed || _player.HasGaplessArmed))
        {
            return;
        }

        _gaplessToken++;
        _player.ClearGaplessNext();
        _gaplessTarget = next;
        _gaplessInFlight = true;
        _gaplessFailed = false;
        var token = _gaplessToken;
        _ = ArmLibraryGaplessAfterPeaksAsync(next, token);
    }

    /// <summary>
    /// 次曲のピーク走査が終わってから先読みストリームを開く。
    /// 同じ MP3 を Media Foundation で二重に開くと、プレイリスト波形が壊れる。
    /// </summary>
    private async Task ArmLibraryGaplessAfterPeaksAsync(DocumentSession next, int token)
    {
        var startedPrefetch = false;
        try
        {
            if (_activeSession is { } playing)
            {
                await FillLibraryPeaksAsync(playing).ConfigureAwait(true);
            }

            if (token != _gaplessToken)
            {
                return;
            }

            await FillLibraryPeaksAsync(next).ConfigureAwait(true);
            if (token != _gaplessToken)
            {
                return;
            }

            startedPrefetch = true;
            await PrefetchLibraryGaplessAsync(next, token).ConfigureAwait(true);
        }
        catch
        {
            if (token == _gaplessToken)
            {
                _gaplessFailed = true;
            }
        }
        finally
        {
            if (!startedPrefetch && token == _gaplessToken)
            {
                _gaplessInFlight = false;
            }
        }
    }

    private async Task PrefetchLibraryGaplessAsync(DocumentSession next, int token)
    {
        AudioStreamSource? source = null;
        try
        {
            source = await Task.Run(() => OpenLibraryGaplessSource(next)).ConfigureAwait(true);
            if (token != _gaplessToken || source is null)
            {
                source?.Dispose();
                if (token == _gaplessToken && source is null)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            if (!IsLibraryMaximized
                || !IsPlaybackActive()
                || !ReferenceEquals(next, LibraryBrowser.NextPlaylistSession(_activeSession)))
            {
                source.Dispose();
                if (token == _gaplessToken)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            ScanLibraryArtwork(next);
            if (!_player.TryArmGapless(source, next.Document))
            {
                source.Dispose();
                _gaplessFailed = true;
            }
        }
        catch
        {
            source?.Dispose();
            if (token == _gaplessToken)
            {
                _gaplessFailed = true;
            }
        }
        finally
        {
            if (token == _gaplessToken)
            {
                _gaplessInFlight = false;
            }
        }
    }

    private static AudioStreamSource? OpenLibraryGaplessSource(DocumentSession next)
    {
        var document = next.Document;
        var path = document.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !AudioCodec.CanStreamPlay(path))
        {
            return null;
        }

        // ActivateStreamPlayback は PCM とピークを捨てる。
        // 1曲のときは次曲も同じ曲なので、エディタで見えていた波形がプレイヤーへ戻すと消える。
        if (!document.IsStreamPlayback
            && document.Interleaved.Length == 0
            && document.Peaks.IsEmpty
            && !AudioCodec.TryActivateStreamPlayback(document)
            && !LibraryPlaylistDocuments.IsVideo(document))
        {
            return null;
        }

        return AudioPlayer.OpenLibraryPlaybackSource(document, prebufferTimeoutMs: 200);
    }

    private bool AdoptLibraryGaplessAdvance()
    {
        if (!IsLibraryMaximized || _libraryStopAfterTrack || !_player.HasGaplessAdvancePending)
        {
            return false;
        }

        if (!_player.TryTakeGaplessAdvance(out var document) || document is null)
        {
            return false;
        }

        DocumentSession? session = null;
        foreach (var item in _sessions)
        {
            if (ReferenceEquals(item.Document, document))
            {
                session = item;
                break;
            }
        }

        if (session is null)
        {
            return false;
        }

        _gaplessTarget = null;
        _gaplessFailed = false;
        _activeSession = session;
        Waveform.Document = document;
        Overview.Document = document;
        Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
        Waveform.ResetTimeZoom();
        Waveform.ResetAmpZoom();
        Waveform.PlayheadFrame = _player.CursorFrame;
        // Document の差し替えが軌跡の記録を止める。再生は続いているので付け直す。
        Waveform.SetTrailRecording(true);
        LibraryBrowser.SelectSessionQuiet(session);
        LibraryBrowser.UpdateSessionRow(session);
        ShowLibraryArtwork(session);
        _ = FillLibraryPeaksAsync(session);
        Transport.SetPlaying(true);
        RefreshTitle();
        RefreshStatus();
        SyncMonitorLayout();
        ScheduleLibraryGaplessPrefetch();
        return true;
    }

    private bool TryAdvanceLibraryPlaylist()
    {
        if (_libraryStopAfterTrack)
        {
            return false;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null)
        {
            return false;
        }

        _ = PlayLibrarySessionAsync(next);
        return true;
    }

    private bool TryStepLibraryPlaylist(int direction)
    {
        // 再生中は今の曲、停止中は選択行を基準。先頭の前→末尾、末尾の次→先頭。
        var cursor = IsPlaybackActive()
            ? (_activeSession ?? LibraryBrowser.SelectedSession)
            : (LibraryBrowser.SelectedSession ?? _activeSession);
        var session = direction < 0
            ? LibraryBrowser.PreviousPlaylistSession(cursor)
            : LibraryBrowser.NextPlaylistSession(cursor);
        if (session is null)
        {
            return false;
        }

        _ = PlayLibrarySessionAsync(session);
        return true;
    }

    private void ToggleLibraryPlayPause()
    {
        if (IsPlaybackActive())
        {
            // 本再生中の一時停止では暗いプレビューへ移ってもクロームを出さない。
            PausePlaybackHere(immersiveVideoHold: IsPlaylistVideoPlaying());
            return;
        }

        _libraryStopAfterTrack = false;
        if (_document is not null)
        {
            StartPlayback(_document.CursorFrame, prerollSeconds: 0);
            ScheduleLibraryGaplessPrefetch();
            return;
        }

        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    private void ClearPlaylistVideoImmersivePause(bool sync = true)
    {
        if (!_playlistVideoImmersivePause)
        {
            return;
        }

        _playlistVideoImmersivePause = false;
        if (sync)
        {
            SyncPlaylistVideoChromeFade();
        }
    }

    private void RestartLibraryTrack()
    {
        _libraryStopAfterTrack = false;
        var session = _activeSession ?? LibraryBrowser.SelectedSession;
        if (session is not null
            && IsLibraryMaximized
            && LibraryPlaylistDocuments.IsVideo(session.Document)
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo)
        {
            // 止め直すとクローム（プレイリスト）が一瞬見える。本再生のまま 0 へシークする。
            session.Document.CursorFrame = 0;
            Waveform.PlayheadFrame = 0;
            StartPlayback(0, prerollSeconds: 0);
            return;
        }

        if (session is not null)
        {
            _ = PlayLibrarySessionAsync(session);
            return;
        }

        if (_document is not null)
        {
            StartPlayback(0, prerollSeconds: 0);
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void SeekLibraryBySeconds(double seconds)
    {
        if (_document is null)
        {
            return;
        }

        var frames = (long)Math.Round(_document.SampleRate * seconds);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        SeekFrame(
            current + frames,
            IsPlaybackActive() ? LibraryPlayerMode.SeekNudgeFadeMilliseconds : 0);
    }

    private bool BeginOrContinueSeekNudge(int direction)
    {
        if (direction == 0 || _document is null)
        {
            return false;
        }

        if (_seekNudgeDirection == direction && _seekNudgeTimer.IsEnabled)
        {
            return true;
        }

        SeekLibraryBySeconds(
            direction < 0 ? -LibraryPlayerMode.SeekNudgeSeconds : LibraryPlayerMode.SeekNudgeSeconds);
        _seekNudgeDirection = direction;
        _seekNudgeRepeatStarted = false;
        _seekNudgeLastAt = Environment.TickCount64;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        _seekNudgeTimer.Start();
        return true;
    }

    private void OnSeekNudgeTick()
    {
        if (_seekNudgeDirection == 0 || !IsSeekNudgeHeld(_seekNudgeDirection))
        {
            StopSeekNudge();
            return;
        }

        var now = Environment.TickCount64;
        var seconds = _seekNudgeDirection < 0
            ? -LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted)
            : LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted);
        SeekLibraryBySeconds(seconds);

        _seekNudgeLastAt = now;
        if (_seekNudgeRepeatStarted)
        {
            return;
        }

        _seekNudgeRepeatStarted = true;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: true));
        _seekNudgeTimer.Start();
    }

    private static bool IsSeekNudgeHeld(int direction)
    {
        var keyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.NumPad7) || Keyboard.IsKeyDown(Key.J)
            : Keyboard.IsKeyDown(Key.NumPad9) || Keyboard.IsKeyDown(Key.K);
        return keyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopSeekNudge()
    {
        _seekNudgeDirection = 0;
        _seekNudgeRepeatStarted = false;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
    }

    private bool TryHandlePlaylistVideoFrameStep(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!LibraryPlayerMode.IsVideoFrameStepKey(key, modifiers)
            || !LibraryPlayerMode.CanStepPlaylistVideoFrame(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _playlistVideoImmersivePause))
        {
            return false;
        }

        // OS のキーリピートはタイマー側で扱う（最初だけウェイト）。
        if (isRepeat)
        {
            return true;
        }

        var direction = key == Key.Left ? -1 : 1;
        return BeginOrContinueVideoFrameStep(direction);
    }

    private bool BeginOrContinueVideoFrameStep(int direction)
    {
        if (direction == 0 || _document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return false;
        }

        if (_videoFrameStepDirection == direction && _videoFrameStepTimer.IsEnabled)
        {
            return true;
        }

        StopPlaybackShuttle();
        StopSeekNudge();
        StepPlaylistVideoFrame(direction);
        _videoFrameStepDirection = direction;
        _videoFrameStepRepeatStarted = false;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: false));
        _videoFrameStepTimer.Start();
        return true;
    }

    private void OnVideoFrameStepTick()
    {
        if (_videoFrameStepDirection == 0 || !IsVideoFrameStepHeld(_videoFrameStepDirection))
        {
            StopVideoFrameStep();
            return;
        }

        if (!LibraryPlayerMode.CanStepPlaylistVideoFrame(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _playlistVideoImmersivePause))
        {
            StopVideoFrameStep();
            return;
        }

        StepPlaylistVideoFrame(_videoFrameStepDirection);
        if (_videoFrameStepRepeatStarted)
        {
            return;
        }

        _videoFrameStepRepeatStarted = true;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: true));
        _videoFrameStepTimer.Start();
    }

    private void StepPlaylistVideoFrame(int direction)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return;
        }

        if (IsPlaybackActive())
        {
            PausePlaybackHere(immersiveVideoHold: true);
        }
        else if (LibraryBrowser.PlaylistVisualPlaying || !_playlistVideoImmersivePause)
        {
            _playlistVideoImmersivePause = true;
            LibraryBrowser.PausePlaylistVisualFrame();
            SyncPlaylistVideoChromeFade();
        }

        var step = LibraryPlayerMode.VideoFrameStepSamples(
            _document.SampleRate,
            LibraryPlayerMode.VideoTimecodeFramesPerSecond);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        // 一時停止直後は音声ヘッドが止まるので、映像位置を優先する。
        if (!IsPlaybackActive() && LibraryBrowser.PlaylistVisualShown)
        {
            var rate = Math.Max(1, _document.SampleRate);
            current = LibraryPlayerMode.FrameFromSeconds(
                LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                rate,
                _document.FrameCount);
        }

        SeekFrame(current + direction * step);
    }

    private static bool IsVideoFrameStepHeld(int direction)
    {
        var keyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.Left)
            : Keyboard.IsKeyDown(Key.Right);
        return keyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopVideoFrameStep()
    {
        _videoFrameStepDirection = 0;
        _videoFrameStepRepeatStarted = false;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: false));
    }

    private async Task PlayLibrarySessionAsync(
        DocumentSession? session,
        bool stopAfterTrack = false,
        bool keepAmbientGlow = false)
    {
        StopVideoFrameStep();
        CancelLibraryGapless();
        _libraryPlayOnArrowRelease = false;
        _libraryStopAfterTrack = stopAfterTrack;
        if (session is null)
        {
            ClearPlaylistVisualChromeBridge();
            if (!keepAmbientGlow)
            {
                LibraryBrowser.SetArtwork(null);
            }

            return;
        }

        // フレーム一時停止からの次曲は本再生へつなぐ（暗いプレビューに落とさない）。
        var immersiveHold = _playlistVideoImmersivePause;
        ClearPlaylistVideoImmersivePause(sync: false);
        // 速度ランプ中にプロキシ準備などを await すると低速のまま待たされるので、
        // 同じ動画の暗いプレビューなら先に 1 倍・明るい本再生映像へ戻す。
        var promoteDimPreview = LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryBrowser.IsPlaylistVisualSameSource(session.Document.SourcePath),
            LibraryPlaylistDocuments.IsVideo(session.Document));
        if (promoteDimPreview)
        {
            LibraryBrowser.PromotePlaylistVisualToMainPlay();
        }

        var bridgeChrome = LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryPlaylistDocuments.IsVisual(session.Document),
            immersiveHold || promoteDimPreview);
        if (bridgeChrome)
        {
            BeginPlaylistVisualChromeBridge();
        }

        try
        {
            if (!keepAmbientGlow)
            {
                ShowLibraryArtwork(session);
            }

            await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
            if (!ReferenceEquals(_libraryLoadSession, session))
            {
                return;
            }

            if (session.Document.IsDeferredLoad
                && !LibraryPlaylistDocuments.IsVisual(session.Document))
            {
                return;
            }

            if (LibraryPlaylistDocuments.IsVideo(session.Document))
            {
                ActivateVisualLibraryMeta(session);
            }

            if (!ReferenceEquals(_libraryLoadSession, session))
            {
                return;
            }

            if (IsLibraryMaximized)
            {
                LibraryBrowser.SelectSessionQuiet(session);
            }

            if (IsPlaybackActive())
            {
                // 次の本再生へつなぐときは暗いプレビューへ落とさない
                //（プレイリストが一瞬見える／dim・格子が本再生に残る）。
                if (bridgeChrome)
                {
                    PausePlaybackSoft(keepPlaylistVisual: true);
                }
                else
                {
                    StopPlayback();
                }
            }

            ApplyLoadedLibrarySession(session);
            if (LibraryPlaylistDocuments.IsVisual(session.Document))
            {
                if (LibraryPlaylistDocuments.IsVideo(session.Document)
                    && !await TryPrepareVideoProxyAsync(session).ConfigureAwait(true))
                {
                    return;
                }

                if (!ReferenceEquals(_libraryLoadSession, session))
                {
                    return;
                }

                StartLibraryVisualPlayback(session, stopAfterTrack);
                if (!keepAmbientGlow)
                {
                    ShowLibraryArtwork(session);
                }

                return;
            }

            SetPlaylistVideoFullscreen(false);
            LibraryBrowser.HidePlaylistVisual();
            StartPlayback(0, prerollSeconds: 0);
            if (!keepAmbientGlow)
            {
                ShowLibraryArtwork(session);
            }

            // 再生とは別ハンドルでピーク走査する（停止待ちしない）。
            // 次曲の先読みは、今の曲と次曲の走査が終わってから開く。
            _ = FillLibraryPeaksAsync(session);
            if (!stopAfterTrack)
            {
                ScheduleLibraryGaplessPrefetch();
            }
        }
        finally
        {
            ClearPlaylistVisualChromeBridge();
        }
    }

    private void BeginPlaylistVisualChromeBridge()
    {
        if (_playlistVisualChromeBridge)
        {
            return;
        }

        _playlistVisualChromeBridge = true;
        SyncPlaylistVideoChromeFade();
    }

    private void ClearPlaylistVisualChromeBridge()
    {
        if (!_playlistVisualChromeBridge)
        {
            return;
        }

        _playlistVisualChromeBridge = false;
        SyncPlaylistVideoChromeFade();
    }

    /// <summary>
    /// Space／トランスポート再生。エディタの TogglePlayback 相当。
    /// 再生中は HaltPlaybackToStart。停止中はシーク位置（CursorFrame）から単独再生／再開（曲末で次へ進まない）。
    /// 別曲へ切り替えるときだけ PlayLibrarySessionAsync（その曲は先頭から）。
    /// </summary>
    private async void PlayLibrarySelectionOnceOrToggle()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // エディタと同じ：本再生中だけ止めて開始位置へ戻す（即再開しない）。
        // 動画本再生中はプレイリストを出さず、開始位置で静止する。
        if (IsPlaybackActive())
        {
            StopVideoFrameStep();
            HaltPlaybackToStart(keepVideoChromeHidden: IsPlaylistVideoPlaying());
            _libraryStopAfterTrack = false;
            return;
        }

        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null)
        {
            return;
        }

        // 同じ曲が既に載っているときは先頭へ戻さず、シークバー位置から始める／再開する。
        if (ReferenceEquals(session, _activeSession) && _document is not null)
        {
            _libraryStopAfterTrack = true;
            if (LibraryPlaylistDocuments.IsPdf(session.Document))
            {
                await PlayLibrarySessionAsync(session, stopAfterTrack: true).ConfigureAwait(true);
                return;
            }

            StartPlayback(_document.CursorFrame, prerollSeconds: 0);
            return;
        }

        await PlayLibrarySessionAsync(session, stopAfterTrack: true).ConfigureAwait(true);
    }

    private Task EnsureLibrarySessionLoadedAsync(DocumentSession session, bool bind = true)
    {
        if (bind && IsPlaybackActive() && !ReferenceEquals(session, _activeSession))
        {
            StopPlayback();
        }

        if (LibraryPlaylistDocuments.IsVisual(session.Document))
        {
            _libraryLoadGeneration++;
            _libraryLoadSession = session;
            ActivateVisualLibraryMeta(session);
            return Task.CompletedTask;
        }

        _libraryLoadGeneration++;
        var gen = _libraryLoadGeneration;
        _libraryLoadSession = session;

        if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
        {
            return Task.CompletedTask;
        }

        // プレイヤー中のストリーム再生はフル Load しない。
        if (IsLibraryMaximized
            && session.Document.SourcePath is { Length: > 0 }
            && AudioCodec.CanStreamPlay(session.Document.SourcePath))
        {
            return ActivateStreamLibrarySessionAsync(session, gen);
        }

        // エディタ復帰時はストリームをフル PCM に昇格する。
        if (session.Document.IsStreamPlayback)
        {
            return LoadDeferredLibrarySessionAsync(session, gen, bind);
        }

        if (!session.Document.IsDeferredLoad)
        {
            return Task.CompletedTask;
        }

        return LoadDeferredLibrarySessionAsync(session, gen, bind);
    }

    private async Task ActivateStreamLibrarySessionAsync(DocumentSession session, int generation)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(() =>
            {
                if (session.Document.IsStreamPlayback
                    && session.Document.FrameCount > 0
                    && session.Document.SampleRate > 0)
                {
                    return true;
                }

                return AudioCodec.TryActivateStreamPlayback(session.Document);
            }).ConfigureAwait(true);
            if (!ok)
            {
                // ストリーム不可なら従来どおりフル展開。
                await LoadDeferredLibrarySessionAsync(session, generation, bind: true).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            return;
        }

        ApplyLoadedLibrarySession(session);
        // ピークは再生と同時に走らせない（Activate 直後の再生と MF が競合する）。
        if (!IsPlaybackActive())
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    private async Task LoadDeferredLibrarySessionAsync(DocumentSession session, int generation, bool bind = true)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var loaded = await Task.Run(() =>
            {
                var document = AudioCodec.Load(path, buildPeaks: false);
                document.ReplacePeaks(
                    PeakPyramid.BuildDisplay(document.Interleaved, document.Channels, document.SampleCount));
                return document;
            }).ConfigureAwait(true);
            if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
            {
                return;
            }

            var tags = session.Document.Tags;
            var previousArt = session.Document.Artwork;
            loaded.CursorFrame = Math.Clamp(session.PlayheadFrame, 0, loaded.FrameCount);
            session.ReplaceDocument(loaded);
            loaded.ApplyTags(tags);
            if (!loaded.HasArtwork && previousArt is { Length: > 0 })
            {
                loaded.SetArtwork(previousArt);
            }

            if (loaded.SourcePath is { } openedPath)
            {
                RememberOpenedPath(openedPath);
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            if (bind || !_sessions.Contains(session))
            {
                return;
            }
        }

        ApplyLoadedLibrarySession(session, bind);
        _ = FillLibraryPeaksAsync(session);
    }

    private Task FillLibraryPeaksAsync(DocumentSession session)
    {
        var document = session.Document;
        if (document.IsDeferredLoad || LibraryPlaylistDocuments.IsPdf(document))
        {
            return Task.CompletedTask;
        }

        var display = IsLibraryMaximized;
        TrimUnwantedLibraryPeakJobs();
        // 途中スナップショット（未走査は 0）が残っている間は、完成までやり直す。
        // プレイヤー包絡（1ch）をエディタで使い続けるとレーンが 1 本のまま。
        if (LibraryPlayerMode.CanReusePeaks(display, document))
        {
            // エディタで作り終えたピークは作り直さない。ただし表示中の波形はプレイヤー帯で描き直す。
            if (display && ReferenceEquals(_activeSession, session))
            {
                Waveform.Refresh();
            }

            return Task.CompletedTask;
        }

        if (!display && document.IsStreamPlayback)
        {
            return Task.CompletedTask;
        }

        if (_libraryPeakTasks.TryGetValue(document, out var running)
            && !running.IsCompleted)
        {
            return running;
        }

        if (!_libraryPeakJobs.Add(document))
        {
            return Task.CompletedTask;
        }

        Task task = null!;
        task = FillLibraryPeaksCoreAsync(session, document, display);
        _libraryPeakTasks[document] = task;
        return task;
    }

    private async Task FillLibraryPeaksCoreAsync(
        DocumentSession session,
        AudioDocument document,
        bool display)
    {
        var peakGeneration = _libraryPeakGeneration;
        var pendingPartial = new PeakPyramid?[1];
        var invokePending = 0;
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_libraryPeakCts.Token);
        _libraryPeakJobCts[document] = jobCts;
        var token = jobCts.Token;
        var retry = false;
        try
        {
            PeakPyramid peaks;
            if (document.IsStreamPlayback
                && document.SourcePath is { Length: > 0 } path
                && File.Exists(path))
            {
                var dispatcher = Dispatcher;
                var peakPath = path;
                if (LibraryPlaylistDocuments.IsVideo(path)
                    && VideoProxy.TryGetCached(path, out var proxy))
                {
                    peakPath = proxy;
                }

                peaks = await Task.Run(() => PeakPyramid.BuildPlayerDisplayFromPath(
                        peakPath,
                        onProgress: partial =>
                        {
                            if (peakGeneration != _libraryPeakGeneration)
                            {
                                return;
                            }

                            Volatile.Write(ref pendingPartial[0], partial);
                            if (Interlocked.Exchange(ref invokePending, 1) != 0)
                            {
                                return;
                            }

                            // Normal は Input より先。長いピーク走査中にキーとマウスが飢える。
                            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                            {
                                Interlocked.Exchange(ref invokePending, 0);
                                var snapshot = Volatile.Read(ref pendingPartial[0]);
                                if (snapshot is null || peakGeneration != _libraryPeakGeneration)
                                {
                                    return;
                                }

                                TryApplyLibraryPeaks(session, document, peakGeneration, snapshot, throttlePaint: true);
                            });
                        },
                        token),
                    token).ConfigureAwait(true);
            }
            else
            {
                var interleaved = document.Interleaved;
                var channels = document.Channels;
                var sampleCount = document.SampleCount;
                peaks = await Task.Run(() => display
                        ? PeakPyramid.BuildPlayerDisplay(interleaved, channels, sampleCount)
                        : PeakPyramid.Build(interleaved, channels, sampleCount),
                    token).ConfigureAwait(true);
            }

            var superseded = peakGeneration != _libraryPeakGeneration
                || !ReferenceEquals(session.Document, document);
            if (!superseded && IsLibraryMaximized != display && !document.IsStreamPlayback)
            {
                retry = true;
            }
            else if (!superseded)
            {
                TryApplyLibraryPeaks(session, document, peakGeneration, peaks);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or OperationCanceledException
                                       or System.Runtime.InteropServices.COMException)
        {
            // ピークだけ失敗しても再生は続ける。
            // 今の曲の走査が次曲の先読みで止まったときは、世代を進めずにやり直す。
            if (ex is (OperationCanceledException or System.Runtime.InteropServices.COMException)
                && IsLibraryMaximized
                && _sessions.Contains(session)
                && ReferenceEquals(_activeSession, session)
                && ReferenceEquals(session.Document, document)
                && document.Peaks.IsBuilding)
            {
                retry = true;
            }
        }
        finally
        {
            if (_libraryPeakJobCts.TryGetValue(document, out var tracked)
                && ReferenceEquals(tracked, jobCts))
            {
                _libraryPeakJobCts.Remove(document);
            }

            jobCts.Dispose();
            if (peakGeneration == _libraryPeakGeneration)
            {
                if (_libraryPeakTasks.TryGetValue(document, out var trackedTask)
                    && trackedTask.IsCompleted)
                {
                    _libraryPeakTasks.Remove(document);
                }

                _libraryPeakJobs.Remove(document);
            }
        }

        if (retry)
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    /// <summary>
    /// 同じ曲の、より手前で止まったスナップショットでは置き換えない。
    /// </summary>
    private bool TryApplyLibraryPeaks(
        DocumentSession session,
        AudioDocument document,
        int peakGeneration,
        PeakPyramid peaks,
        bool throttlePaint = false)
    {
        if (peakGeneration != _libraryPeakGeneration
            || !ReferenceEquals(session.Document, document)
            || !LibraryPlayerMode.IsNewerLibraryPeaks(document.Peaks, peaks))
        {
            return false;
        }

        document.ReplacePeaks(peaks);
        if (document.IsStreamPlayback
            && !peaks.IsEmpty
            && !peaks.IsBuilding
            && peaks.FrameCount > 0
            && peaks.FrameCount != document.FrameCount)
        {
            document.SyncStreamPlaybackMeta(
                document.SampleRate,
                document.Channels,
                document.BitsPerSample,
                peaks.FrameCount);
        }

        if (!peaks.IsBuilding && document.SourcePath is { Length: > 0 } path)
        {
            LibraryBrowser.ApplyCompletedPlaylistWaveform(path, peaks);
        }

        RefreshLibrarySessionWaveform(session, throttlePaint);
        return true;
    }

    private void RefreshLibrarySessionWaveform(DocumentSession session, bool throttlePaint)
    {
        WaveformView? view = null;
        if (FindTilePane(session) is { } pane)
        {
            view = pane.View;
        }
        else if (ReferenceEquals(_activeSession, session))
        {
            view = Waveform;
        }

        if (view is null)
        {
            return;
        }

        if (throttlePaint)
        {
            view.RefreshThrottled();
        }
        else
        {
            view.Refresh();
        }

        if (!IsLibraryMaximized && ReferenceEquals(_activeSession, session))
        {
            Overview.Refresh();
        }
    }

    private void ApplyLoadedLibrarySession(DocumentSession session, bool bind = true)
    {
        if (!bind)
        {
            ApplyBackgroundLibrarySession(session);
            return;
        }

        var needsBind = !ReferenceEquals(session, _activeSession)
            || _tileActiveView is not null
            || !ReferenceEquals(Waveform.Document, session.Document);
        if (IsLibraryMaximized)
        {
            if (needsBind)
            {
                BindSingleWorkspace(session);
            }

            LibraryBrowser.UpdateSessionRow(session);
            ScheduleLibraryWaveformPaint();
            return;
        }

        if (needsBind)
        {
            BindWorkspace(session);
        }
    }

    /// <summary>非表示タブ／タイルをフル PCM に差し替える。アクティブへ切り替えない。</summary>
    private void ApplyBackgroundLibrarySession(DocumentSession session)
    {
        if (FindTilePane(session) is { } pane)
        {
            ApplySessionToView(pane.View, session, applyAnalysis: false);
            pane.View.Refresh();
            RefreshTileChrome();
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private void ShowLibraryArtwork(DocumentSession session)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        var hadArt = session.Document.HasArtwork;
        ScanLibraryArtwork(session);
        _libraryHoldJacketWash = false;
        LibraryBrowser.SetArtwork(session.Document);
        if (!hadArt && session.Document.HasArtwork)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private static void ScanLibraryArtwork(DocumentSession session)
    {
        var document = session.Document;
        if (document.HasArtwork
            || document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path))
        {
            return;
        }

        if (document.SourceKind == AudioFileKind.Mp3)
        {
            if (Id3Artwork.TryRead(path, out var artwork))
            {
                document.SetArtwork(artwork);
            }

            return;
        }

        if (document.SourceKind == AudioFileKind.M4a
            && M4aArtwork.TryRead(path, out var cover))
        {
            document.SetArtwork(cover);
            return;
        }

        if (LibraryPlaylistDocuments.IsVideo(document)
            && VideoArtwork.TryRead(path, out var frame))
        {
            document.SetArtwork(frame);
        }
    }

    private void LibraryBrowser_VisibleColumnPresetsChanged(object? sender, LibraryColumnPresets presets)
    {
        if (presets.PersistMixed)
        {
            AppStorage.Settings.ApplyLibraryListColumnPresets(
                presets.Wave,
                presets.Mp3,
                presets.Video,
                presets.Mixed);
        }
        else
        {
            AppStorage.Settings.ApplyLibraryListColumnPresets(
                presets.Wave,
                presets.Mp3,
                presets.Video);
        }

        AppStorage.Save();
    }

    private void LibraryBrowser_GroupChanged(object? sender, LibraryFileGroup group)
    {
        AppStorage.Settings.ApplyLibraryListGroup(group);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerFolderChanged(object? sender, string path)
    {
        AppStorage.Settings.ApplyLibraryExplorerPath(path);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerExpandedChanged(object? sender, IReadOnlyList<string> paths)
    {
        AppStorage.Settings.ApplyLibraryExplorerExpanded(paths);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerWidthChanged(object? sender, double width)
    {
        AppStorage.Settings.LibraryExplorerWidth = DesignMetrics.ClampLibraryExplorerWidth(width);
        AppStorage.Save();
    }

    private void LibraryBrowser_FavoritesSplitChanged(object? sender, double ratio)
    {
        AppStorage.Settings.LibraryFavoritesSplit = DesignMetrics.ClampLibraryFavoritesSplit(ratio);
        AppStorage.Save();
    }

    private void LibraryBrowser_ShuffleChanged(object? sender, EventArgs e)
    {
        if (IsLibraryMaximized && IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void LibraryBrowser_FavoritesChanged(object? sender, IReadOnlyList<string> paths)
    {
        AppStorage.Settings.ApplyLibraryFavoritePaths(paths);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerRootsChanged(object? sender, IReadOnlyList<string> paths)
    {
        AppStorage.Settings.ApplyLibraryExplorerRoots(paths);
        AppStorage.Save();
    }

    private void LibraryBrowser_ClearPlaylistRequested(object? sender, EventArgs e) =>
        RemoveLibrarySelectedFromList();

    private void LibraryBrowser_FavoritesActivated(object? sender, LibraryFavoritesActivateEventArgs e)
    {
        if (e.Paths.Count == 0 || _libraryFolderShowInProgress)
        {
            return;
        }

        // 欠損（赤字）だけの Enter はスルー。空クリアでメーターだけ出さない。
        var paths = LibraryShellFiles.ExistingPaths(e.Paths);
        if (paths.Length == 0)
        {
            return;
        }

        if (e.ClearPlaylist)
        {
            _libraryHoldJacketWash = true;
            if (!TryClearLibrarySessions())
            {
                _libraryHoldJacketWash = false;
                return;
            }

            _ = OpenLibraryFoldersRecursiveAsync(
                paths,
                LibraryExplorerPlaylistWalk.Inactive,
                playFirst: true);
            return;
        }

        _ = OpenLibraryFoldersRecursiveAsync(
            paths,
            LibraryExplorerPlaylistWalk.Inactive,
            playFirst: false);
    }

    private void LibraryBrowser_ExplorerFoldersOpened(object? sender, IReadOnlyList<string> folders)
    {
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        var existing = LibraryShellFiles.ExistingPaths(folders);
        if (existing.Length == 0)
        {
            return;
        }

        _ = OpenLibraryFoldersRecursiveAsync(existing, playFirst: false);
    }

    private async void LibraryBrowser_ExplorerFolderOpened(object? sender, string path)
    {
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        var existing = LibraryShellFiles.ExistingPaths([path]);
        if (existing.Length == 0)
        {
            return;
        }

        await OpenLibraryFoldersRecursiveAsync(existing, playFirst: false).ConfigureAwait(true);
    }

    /// <summary>ツリーの Enter。プレイリストを空にしてから、選んだフォルダ配下を載せる。</summary>
    private void ReplaceLibraryFromExplorerFolder()
    {
        // 欠損（赤字）ルートの Enter はスルー。クリアだけ走ると空リストなのにメーターが出る。
        var folders = LibraryShellFiles.ExistingPaths(LibraryBrowser.SelectedExplorerFolders);
        if (folders.Length == 0)
        {
            return;
        }

        // 読み込み中の Enter 連打は無視（都度クリア＋世代更新で 1 曲しか残らない）。
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        _libraryHoldJacketWash = true;
        if (!TryClearLibrarySessions())
        {
            _libraryHoldJacketWash = false;
            return;
        }

        // イベント経由だと共有フラグ／再入で打ち切られるので、再生意図を引数で直接渡す。
        _ = OpenLibraryFoldersRecursiveAsync(
            folders,
            LibraryBrowser.SnapshotExplorerPlaylistWalk(),
            playFirst: true);
    }

    /// <summary>
    /// フォルダ配下（と単体ファイル）を再帰収集し、1 曲ずつ載せる。
    /// 再生するのは Enter（クリア後）だけ。Shift+Enter・ダブルクリック・追加メニューは再生も停止もしない。
    /// Enter 再生は全件追加が終わってから先頭を再生し、プレイリストへフォーカスする。
    /// 検索中のツリーは見えているフォルダだけ。ファイル名ヒットはそのファイル、フォルダ名ヒットは配下すべて。
    /// お気に入りは検索フィルタを掛けない。
    /// </summary>
    private async Task OpenLibraryFoldersRecursiveAsync(
        IReadOnlyList<string> folders,
        LibraryExplorerPlaylistWalk? walk = null,
        bool playFirst = false)
    {
        LibraryPlaylistDocuments.ApplyFromSettings(AppStorage.Settings);
        var generation = ++_libraryFolderShowGeneration;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = true;
        // 前の追加が残した進捗表示を消す（世代不一致の finally では消さないため）。
        ClearOpenStatus();
        var filter = walk ?? LibraryBrowser.SnapshotExplorerPlaylistWalk();
        var remaining = new Stack<(string Path, bool AncestorHit)>();
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            try
            {
                remaining.Push((Path.GetFullPath(folders[i]), false));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        if (remaining.Count == 0)
        {
            if (generation == _libraryFolderShowGeneration)
            {
                _libraryFolderShowInProgress = false;
            }

            ReleaseHeldLibraryJacket();
            return;
        }

        _libraryPlayFirstPending = false;
        var firstHandled = false;
        var loaded = 0;
        var planned = 0;
        var showProgress = false;

        void PlanFiles(int count)
        {
            if (count <= 0)
            {
                return;
            }

            planned += count;
            if (planned > 1)
            {
                showProgress = true;
            }
        }

        async Task<bool> AppendFoundAsync(string path)
        {
            if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
            {
                return false;
            }

            var first = !firstHandled;
            // Enter 再生バッチ中は選択／プレビューしない（先頭動画の Open が後続追加と競合する）。
            // Shift+Enter は空リストへの先頭だけ選択する。
            var select = !playFirst
                && ShouldSelectAppendedLibrarySession(
                    playFirst: false,
                    first,
                    playlistWasEmpty: _sessions.Count == 0);
            loaded++;
            if (showProgress)
            {
                SetOpenStatus(loaded, planned, Path.GetFileName(path) ?? path);
            }

            if (!await TryAppendLibrarySessionAsync(path, generation, select, play: false)
                    .ConfigureAwait(true))
            {
                return false;
            }

            if (first && playFirst)
            {
                _libraryFolderPlaySession = FindSessionByPath(path);
            }

            firstHandled = true;
            return true;
        }

        try
        {
            while (remaining.Count > 0)
            {
                if (generation != _libraryFolderShowGeneration)
                {
                    // 新しい Enter / 追加が進行中。波形仕上げはそちらに任せる。
                    return;
                }

                if (!IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    _libraryHoldJacketWash = false;
                    return;
                }

                var current = remaining.Pop();
                if (!Directory.Exists(current.Path))
                {
                    if (File.Exists(current.Path)
                        && AudioCodec.IsPlayerOpenable(current.Path)
                        && filter.IncludeFile(current.Path, current.AncestorHit)
                        && FindSessionByPath(current.Path) is null)
                    {
                        PlanFiles(1);
                        if (!await AppendFoundAsync(current.Path).ConfigureAwait(true))
                        {
                            if (generation != _libraryFolderShowGeneration)
                            {
                                return;
                            }

                            LibraryBrowser.FinishIncrementalSessionLoad();
                            return;
                        }
                    }

                    continue;
                }

                var layer = await Task.Run(() =>
                {
                    var folderHit = filter.FolderNameHit(current.Path, current.AncestorHit);
                    AudioCodec.CollectPlayerOpenableDirectoryLayer(current.Path, out var files, out var children);
                    var included = new List<string>();
                    foreach (var file in files)
                    {
                        if (filter.IncludeFile(file, folderHit))
                        {
                            included.Add(file);
                        }
                    }

                    var next = new List<(string Path, bool AncestorHit)>();
                    foreach (var child in children)
                    {
                        if (filter.IncludeChild(child, folderHit))
                        {
                            next.Add((child, folderHit));
                        }
                    }

                    return (files: included.ToArray(), children: next.ToArray());
                }).ConfigureAwait(true);

                if (generation != _libraryFolderShowGeneration)
                {
                    return;
                }

                if (!IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    _libraryHoldJacketWash = false;
                    return;
                }

                var fresh = 0;
                foreach (var file in layer.files)
                {
                    if (FindSessionByPath(file) is null)
                    {
                        fresh++;
                    }
                }

                PlanFiles(fresh);
                foreach (var file in layer.files)
                {
                    if (FindSessionByPath(file) is not null)
                    {
                        continue;
                    }

                    if (!await AppendFoundAsync(file).ConfigureAwait(true))
                    {
                        if (generation != _libraryFolderShowGeneration)
                        {
                            return;
                        }

                        LibraryBrowser.FinishIncrementalSessionLoad();
                        return;
                    }
                }

                for (var i = layer.children.Length - 1; i >= 0; i--)
                {
                    remaining.Push(layer.children[i]);
                }
            }

            if (generation == _libraryFolderShowGeneration && IsLibraryMaximized)
            {
                LibraryBrowser.FinishIncrementalSessionLoad();
                if (firstHandled)
                {
                    _libraryHoldJacketWash = false;
                    if (playFirst && _libraryFolderPlaySession is { } first)
                    {
                        // 読み込み中に↓等で別行へ移した選択は残す。
                        // ここで常に先頭へ Quiet 選択すると、FinishIncrementalSessionLoad
                        // （行波形の開始）と同じタイミングでシアンだけ1行目へ飛び、
                        // 再生中の曲（_activeSession）と食い違う。
                        var selected = LibraryBrowser.SelectedSession;
                        if (ShouldRestoreFolderPlaySelection(selected, first))
                        {
                            LibraryBrowser.SelectSessionQuiet(first);
                        }

                        await PlayLibrarySessionAsync(first).ConfigureAwait(true);
                        if (generation == _libraryFolderShowGeneration && IsLibraryMaximized)
                        {
                            LibraryBrowser.RequestListFocus();
                        }
                    }
                    else if (!playFirst)
                    {
                        PreviewIdleLibrarySession(LibraryBrowser.SelectedSession);
                    }
                }
                else
                {
                    ReleaseHeldLibraryJacket();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (generation == _libraryFolderShowGeneration)
            {
                ReleaseHeldLibraryJacket();
            }
        }
        finally
        {
            if (generation == _libraryFolderShowGeneration)
            {
                _libraryFolderPlaySession = null;
                _libraryFolderShowInProgress = false;
                if (showProgress)
                {
                    ClearOpenStatus();
                }
            }
        }
    }

    /// <summary>
    /// フォルダ登録完了時に先頭曲へ選択を戻してよいか。
    /// 読み込み中にユーザーが別行へ移していれば false（シアンを奪わない）。
    /// </summary>
    internal static bool ShouldRestoreFolderPlaySelection(
        DocumentSession? selected,
        DocumentSession first) =>
        selected is null || ReferenceEquals(selected, first);

    /// <summary>
    /// 追加した曲をリストで選ぶか。Enter 再生は先頭のみ。Shift+Enter は空リストへ足したときだけ 1 行目を選ぶ。
    /// </summary>
    internal static bool ShouldSelectAppendedLibrarySession(
        bool playFirst,
        bool firstInBatch,
        bool playlistWasEmpty) =>
        firstInBatch && (playFirst || playlistWasEmpty);

    /// <summary>
    /// 1 曲を裏でタグ読みしてリストへ追加。generation が変わったら false（途中キャンセル）。
    /// </summary>
    private async Task<bool> TryAppendLibrarySessionAsync(
        string file,
        int generation,
        bool select,
        bool play)
    {
        if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
        {
            return false;
        }

        DocumentSession session;
        try
        {
            session = await Task.Run(() =>
            {
                var document = AudioDocument.CreateDeferred(file);
                AudioTagProbe.Ensure(document);
                return new DocumentSession(document);
            }).ConfigureAwait(true);
        }
        catch (Exception)
        {
            return true;
        }

        if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
        {
            return false;
        }

        _sessions.Add(session);
        if (play)
        {
            _libraryFolderPlaySession = session;
        }

        if (select)
        {
            ScanLibraryArtwork(session);
        }

        LibraryBrowser.AppendSession(session, select);
        SyncPlayerMeterFade();
        if (select)
        {
            _libraryHoldJacketWash = false;
            if (play)
            {
                LibraryBrowser.SetArtwork(session.Document);
                _ = PlayLibrarySessionAsync(session);
            }
            else
            {
                PreviewLibrarySession(session);
            }
        }

        // 1 件ごとに UI へ制御を返す（キー連打・描画を止めない）。
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
        return true;
    }

    private void ReleaseHeldLibraryJacket()
    {
        if (!_libraryHoldJacketWash)
        {
            return;
        }

        _libraryHoldJacketWash = false;
        if (_sessions.Count == 0 && IsLibraryMaximized)
        {
            LibraryBrowser.SetArtwork(null);
            ActivateLibraryExplorerIfPlaylistEmpty();
        }
    }

    /// <summary>
    /// プレイリストが空のままならライブラリ（フォルダツリー）をアクティブにする。
    /// クリア直後に再追加する途中（ジャケット保持中）は呼ばない。
    /// </summary>
    private void ActivateLibraryExplorerIfPlaylistEmpty()
    {
        if (!IsLibraryMaximized || _sessions.Count != 0 || _libraryHoldJacketWash)
        {
            return;
        }

        LibraryBrowser.RequestExplorerFocus();
    }

    /// <summary>
    /// プレイリストが空になったとき、背面の動画プレビュー／PDF 静止背景も終わらせる。
    /// StopPlayback は暗いプレビューへ残すので、空リストではここで隠す。
    /// </summary>
    private void EndLibraryBackgroundPlayback()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        _libraryStopAfterTrack = false;
        SetPlaylistVideoFullscreen(false);
        LibraryBrowser.ClearPdfBackgroundPin();
        ClearPlaylistVideoImmersivePause();
        LibraryBrowser.HidePlaylistVisual();
        EnsurePlaylistVideoWindowRestored();
        SyncPlaylistVideoChromeFade();
    }

    /// <summary>F10 突入／ミニマム解除。空ならツリー、曲があればプレイリスト。</summary>
    private void FocusLibraryPaneForPlaylist()
    {
        if (!IsLibraryMaximized || _libraryMinimalChrome)
        {
            return;
        }

        if (_sessions.Count == 0)
        {
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        LibraryBrowser.RequestListFocus();
    }

    /// <summary>進行中のフォルダ再帰追加を破棄する（Enter 置換・ドロップなど）。</summary>
    private void CancelLibraryFolderShow()
    {
        _libraryFolderShowGeneration++;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = false;
        ClearOpenStatus();
    }

    private bool TryClearLibrarySessions()
    {
        // Enter 置換などはクリア直後〜新走査開始前に、前回の非同期追加が戻ってこないよう世代を進める。
        CancelLibraryFolderShow();

        if (_sessions.Count == 0)
        {
            EndLibraryBackgroundPlayback();
            if (IsLibraryMaximized)
            {
                LibraryBrowser.SetSessions(_sessions, null);
            }

            return true;
        }

        CancelLibraryGapless();
        CancelLibraryPeakJobs();
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        // 未編集だけなら1件ずつ閉じない（タブ再構築の繰り返しが重い）。まとめて消す。
        var allClean = true;
        foreach (var session in _sessions)
        {
            if (session.Document.IsDirty)
            {
                allClean = false;
                break;
            }
        }

        if (allClean)
        {
            _selectedTabs.Clear();
            _tabSelectionAnchor = null;
            _sessions.Clear();
            BindWorkspace(null);
            RebuildTabBar();
            NotifyWaveformSessionsChanged();
            EndLibraryBackgroundPlayback();
            if (IsLibraryMaximized)
            {
                LibraryBrowser.SetSessions(_sessions, null);
            }

            SyncPlayerMeterFade();
            return true;
        }

        var drop = _sessions.ToArray();
        var cancelled = false;
        RunCloseBatch(drop, () =>
        {
            foreach (var session in drop)
            {
                if (ReferenceEquals(session, _activeSession))
                {
                    continue;
                }

                if (!CloseSession(session, rememberClosed: !session.Document.IsDeferredLoad))
                {
                    cancelled = true;
                    return;
                }
            }

            if (_activeSession is { } active
                && Array.IndexOf(drop, active) >= 0
                && !CloseSession(active, rememberClosed: !active.Document.IsDeferredLoad))
            {
                cancelled = true;
            }
        });

        if (!cancelled && _sessions.Count == 0)
        {
            EndLibraryBackgroundPlayback();
        }

        if (!cancelled && IsLibraryMaximized)
        {
            LibraryBrowser.SetSessions(_sessions, _activeSession);
        }

        SyncPlayerMeterFade();
        return !cancelled;
    }

    private void ActivateVisualLibraryMeta(DocumentSession session)
    {
        var document = session.Document;
        if (!LibraryPlaylistDocuments.IsVisual(document)
            || document.SourcePath is not { Length: > 0 } path)
        {
            return;
        }

        // 動画も妥当なストリームメタがあれば再プローブしない。
        // ホバーのたびに MediaFoundation が MOV で壊れたレートを返すと列表示が跳ねる。
        if (document.IsStreamPlayback
            && document.FrameCount > 0
            && AudioCodec.IsPlausibleAudioSampleRate(document.SampleRate)
            && document.Channels > 0)
        {
            return;
        }

        // 未プローブ、または過去の壊れた MF メタ（0.001kHz 等）がタグに残っているときボックスから取り直す。
        if (LibraryPlaylistDocuments.IsVideo(document)
            && !AudioCodec.IsPlausibleAudioSampleRate(document.Tags.SampleRate)
            && AudioTagProbe.TryRead(path, out var fresh)
            && AudioCodec.IsPlausibleAudioSampleRate(fresh.SampleRate))
        {
            document.ApplyTags(fresh);
        }

        if (LibraryPlaylistDocuments.IsVideo(document) && AudioCodec.TryActivateStreamPlayback(document))
        {
            return;
        }

        AudioTagProbe.Ensure(document);
        var taggedSeconds = document.Tags.DurationSeconds;
        var visualSeconds = LibraryBrowser.PlaylistVisualDuration.TotalSeconds;
        var seconds = LibraryPlaylistDocuments.IsPdf(document)
            ? LibraryPlaylistDocuments.PdfSecondsPerPage
            : taggedSeconds > 0
                ? taggedSeconds
                : Math.Max(1, visualSeconds);
        var frames = Math.Max(1, (long)Math.Round(seconds * 48000));

        // 音声が無くても MediaOpened／タグの映像尺は残す。ダミーのレートは列に出さない。
        if (LibraryPlaylistDocuments.IsVideo(document)
            && document.IsStreamPlayback
            && document.FrameCount > 0
            && document.SampleRate > 0)
        {
            if (frames > document.FrameCount)
            {
                document.SyncStreamPlaybackMeta(
                    document.SampleRate,
                    document.Channels,
                    document.BitsPerSample,
                    frames);
            }

            return;
        }

        document.ActivateStreamPlayback(48000, 2, 16, frames);
    }

    private void StartLibraryVisualPlayback(DocumentSession session, bool stopAfterTrack)
    {
        ClearPlaylistVideoImmersivePause(sync: false);
        if (LibraryPlaylistDocuments.IsPdf(session.Document))
        {
            // タイムラインが無いので表示モードで止める（連続再生でも次へは進まない）。
            // play:true で明るくクロームを隠し、Transport は停止のまま。
            _libraryStopAfterTrack = true;
            LibraryBrowser.ShowPlaylistVisual(session, play: true);
            _lastPlaybackStart = 0;
            session.Document.CursorFrame = 0;
            Waveform.PlayheadFrame = 0;
            Transport.SetPlaying(false);
            return;
        }

        _libraryStopAfterTrack = stopAfterTrack;
        var startFrame = LibraryPlayerMode.HoverResumeFrame(
            LibraryBrowser.PlaylistVisualShown && LibraryBrowser.PlaylistVisualIsVideo,
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryBrowser.IsPlaylistVisualSameSource(session.Document.SourcePath),
            LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
            session.Document.SampleRate,
            session.Document.FrameCount);
        session.Document.CursorFrame = startFrame;
        Waveform.PlayheadFrame = startFrame;
        LibraryBrowser.ShowPlaylistVisual(session, play: true);
        _lastPlaybackStart = startFrame;
        StartPlayback(startFrame, prerollSeconds: 0);
        _ = FillLibraryPeaksAsync(session);
    }

    private static bool CanShowPlaylistVideo(AudioDocument document)
    {
        if (!LibraryPlaylistDocuments.IsVideo(document)
            || document.SourcePath is not { Length: > 0 } path)
        {
            return LibraryPlaylistDocuments.IsPdf(document);
        }

        return VideoCodecProbe.CanPlayWithoutProxy(path) || VideoProxy.TryGetCached(path, out _);
    }

    private async Task<bool> TryPrepareVideoProxyAsync(DocumentSession session, bool forceEncode = false)
    {
        if (!LibraryPlaylistDocuments.IsVideo(session.Document)
            || session.Document.SourcePath is not { Length: > 0 } path)
        {
            return false;
        }

        var needsEncode = !CanShowPlaylistVideo(session.Document)
            && (forceEncode ? VideoProxy.CanForceEncodeProxy() : VideoProxy.CanEncodeProxy());
        if (needsEncode && IsUiBusy)
        {
            return false;
        }

        var token = CancellationToken.None;
        if (needsEncode)
        {
            _videoProxyCts?.Cancel();
            _videoProxyCts?.Dispose();
            _videoProxyCts = new CancellationTokenSource();
            token = _videoProxyCts.Token;
            _videoProxyBusy = true;
            ShowBusyGlass(UiStrings.OverlayVideoProxy);
        }

        try
        {
            IProgress<double>? progress = needsEncode
                ? new Progress<double>(p => _busyGlass.SetProgress(p))
                : null;
            await VideoProxy.EnsurePlayableAsync(path, progress, token, forceEncode).ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_libraryLoadSession, session) || token.IsCancellationRequested)
            {
                return false;
            }

            ActivateVisualLibraryMeta(session);
            if (!session.Document.HasArtwork
                && session.Document.SourcePath is { Length: > 0 } source
                && VideoArtwork.TryRead(source, out var frame))
            {
                session.Document.SetArtwork(frame);
                LibraryBrowser.UpdateSessionRow(session);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (IsLoaded)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    ex.Message,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
        finally
        {
            if (needsEncode)
            {
                _videoProxyBusy = false;
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }
            }
        }
    }

    /// <summary>
    /// Ctrl+Shift+P。設定の「自動生成しない」や F8／引数起動の抑止を無視してプロキシを作る。
    /// プレイリスト選択・本再生・ミニプレイヤーの動画が対象。すでにある／不要／ffmpeg 無しは何もしない。
    /// </summary>
    private void RequestForceVideoProxyEncode() => _ = ForceVideoProxyEncodeAsync();

    private async Task ForceVideoProxyEncodeAsync()
    {
        if (!IsLibraryMaximized || IsUiBusy)
        {
            return;
        }

        var session = ResolveForceVideoProxySession();
        if (session is null
            || !LibraryPlaylistDocuments.IsVideo(session.Document)
            || session.Document.SourcePath is not { Length: > 0 } path
            || CanShowPlaylistVideo(session.Document)
            || !VideoProxy.CanForceEncodeProxy())
        {
            return;
        }

        var sameSource = LibraryBrowser.IsPlaylistVisualSameSource(path);
        var wasShown = sameSource && LibraryBrowser.PlaylistVisualShown;
        var wasPlaying = sameSource && LibraryBrowser.PlaylistVisualPlaying;
        if (wasShown && session.Document.SampleRate > 0)
        {
            var seconds = LibraryBrowser.PlaylistVisualPosition.TotalSeconds;
            if (seconds > 0 || wasPlaying || IsPlaybackActive() || _playlistVideoImmersivePause)
            {
                session.Document.CursorFrame = (long)Math.Round(seconds * session.Document.SampleRate);
                Waveform.PlayheadFrame = session.Document.CursorFrame;
            }
        }

        // 本再生中の差し替えは効かないので、生成前に止めて暗いプレビューへ。
        // 次の Space／Enter でプロキシが使われる。
        if (wasShown && (wasPlaying || IsPlaybackActive() || _playlistVideoImmersivePause))
        {
            DropPlaylistVideoToDimPreview();
        }
        else if (IsPlaybackActive())
        {
            StopPlayback();
        }

        if (!await TryPrepareVideoProxyAsync(session, forceEncode: true).ConfigureAwait(true))
        {
            return;
        }

        if (!IsLoaded || !LibraryPlaylistDocuments.IsVideo(session.Document))
        {
            return;
        }

        // ActivateVisualLibraryMeta → ActivateStreamPlayback がピークを捨てる。
        // プロキシができたら暗いプレビューへ載せ替え（再開はしない）。
        if (wasShown && LibraryBrowser.IsPlaylistVisualSameSource(path))
        {
            LibraryBrowser.ShowPlaylistVisual(session, play: false);
        }

        await RestartLibraryPeaksForSessionAsync(session).ConfigureAwait(true);
        RefreshPlaylistVideoFileName();
        SchedulePlaylistVideoFileNamePlacement();
        LibraryBrowser.UpdateSessionRow(session);
    }

    /// <summary>
    /// 走査中なら止めてからやり直す。プロキシ差し替え後に原ファイル走査が残ると波形が空のままになる。
    /// </summary>
    private async Task RestartLibraryPeaksForSessionAsync(DocumentSession session)
    {
        var document = session.Document;
        if (_libraryPeakJobCts.TryGetValue(document, out var job))
        {
            try
            {
                job.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_libraryPeakTasks.TryGetValue(document, out var running))
        {
            try
            {
                await running.ConfigureAwait(true);
            }
            catch
            {
                // ピーク失敗は再生を止めない（FillLibraryPeaksCoreAsync 内でも握る）。
            }
        }

        await FillLibraryPeaksAsync(session).ConfigureAwait(true);
    }

    /// <summary>
    /// 本再生／ミニプレイヤー中は再生中の動画、それ以外は選択（無ければアクティブ）。
    /// </summary>
    private DocumentSession? ResolveForceVideoProxySession()
    {
        if (IsVideoMiniPlayerActive()
            || (LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && (LibraryBrowser.PlaylistVisualPlaying || _playlistVideoImmersivePause)))
        {
            return _activeSession ?? LibraryBrowser.SelectedSession;
        }

        return LibraryBrowser.SelectedSession ?? _activeSession;
    }

    private void DropPlaylistDocumentSessionsIfDisabled()
    {
        var drop = new List<DocumentSession>();
        foreach (var session in _sessions)
        {
            if (LibraryPlaylistDocuments.IsVisual(session.Document)
                && !LibraryPlaylistDocuments.ShouldList(session.Document.SourcePath))
            {
                drop.Add(session);
            }
        }

        if (drop.Count == 0)
        {
            return;
        }

        LibraryBrowser.HidePlaylistVisual();
        DropLibrarySessionsFast(drop);
        if (IsLibraryMaximized)
        {
            LibraryBrowser.SetSessions(_sessions, _activeSession, LibraryBrowser.SelectedSessions);
        }

        SyncPlayerMeterFade();
    }

    private void LibraryBrowser_PlaylistVisualEnded(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (TryRestartPlaylistVisualLoop())
            {
                return;
            }

            if (_player.IsPlaying)
            {
                return;
            }

            OnPlaybackEnded(_playbackGeneration);
        });

    private bool TryRestartPlaylistVisualLoop()
    {
        if (_document is null
            || _document.Selection.IsEmpty
            || !IsLibraryMaximized
            || !LibraryBrowser.PlaylistVisualPlaying
            || !LibraryBrowser.PlaylistVisualIsVideo)
        {
            return false;
        }

        LibraryBrowser.SeekPlaylistVisual(
            TimeSpan.FromSeconds(
                _document.Selection.StartFrame / (double)Math.Max(1, _document.SampleRate)));
        EnsureLibraryVisualPlaybackClock();
        return true;
    }

    private void LibraryBrowser_PlaylistVisualOpened(object? sender, TimeSpan duration)
    {
        if (_videoLaunchPlacement
            && IsLibraryMaximized
            && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf))
        {
            TryApplyVideoLaunchPlacement();
            SignalVideoLaunchShowReady();
            ScheduleLibraryWaveformPaint();
        }

        if (LibraryBrowser.PlaylistVisualIsPdf && LibraryBrowser.PlaylistVisualPlaying)
        {
            RefreshPlaylistPdfPageNumber();
        }

        if (_document is null
            || !LibraryPlaylistDocuments.IsVisual(_document)
            || duration <= TimeSpan.Zero
            || _document.SampleRate < 1)
        {
            return;
        }

        var frames = Math.Max(1, (long)Math.Round(duration.TotalSeconds * _document.SampleRate));
        _document.SyncStreamPlaybackMeta(
            _document.SampleRate,
            _document.Channels,
            _document.BitsPerSample,
            frames);
        // PDF はページ秒を時間列に載せない。動画だけタグ尺を覚える。
        if (!LibraryPlaylistDocuments.IsPdf(_document)
            && (_document.Tags.DurationSeconds <= 0
                || Math.Abs(_document.Tags.DurationSeconds - duration.TotalSeconds) > 0.05))
        {
            _document.ApplyTags(_document.Tags.WithDurationSeconds(duration.TotalSeconds));
        }
        if (IsPlaybackActive())
        {
            _player.SyncSilenceLength(frames);
        }
        if (_activeSession is not null)
        {
            LibraryBrowser.UpdateSessionRow(_activeSession);
        }

        Waveform.ResetTimeZoom();
        ScheduleLibraryWaveformPaint();
        RefreshStatus();
    }
}
