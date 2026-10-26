using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
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
    private int _selectionLoudnessGeneration;
    private CancellationTokenSource? _selectionLoudnessCts;
    private DocumentSession? _libraryLoadSession;
    private bool _libraryPlayFirstPending;
    private int _libraryEnterPlayGeneration;
    /// <summary>本再生リクエストの世代。プロキシ待ち中に別再生へ差し替わったら古い方を捨てる。</summary>
    private int _libraryPlayTicket;

    /// <summary>再帰追加中に再生する最初の登録曲。グループ再配置で他曲へ飛ばないようにする。</summary>
    private DocumentSession? _libraryFolderPlaySession;
    /// <summary>プレイリスト置換中は空セッションで既定ウォッシュへ落とさない。</summary>
    private bool _libraryHoldJacketWash;
    /// <summary>追加バッチ中に要求された背景色替え。完了時に 1 回だけ Apply する。</summary>
    private DocumentSession? _pendingLibraryWashSession;
    private Task<LibraryBrowserView.PreparedArtworkPresentation>? _pendingLibraryWashPrepare;
    private bool? _pendingLibraryWashHadArt;
    private bool _libraryPlayOnArrowRelease;
    private bool _libraryPlayOnTrackKeyRelease;
    /// <summary>Space 再生。true のあいだは曲末で次へ進まず停止する。</summary>
    private bool _libraryStopAfterTrack;
    /// <summary>F9（および F8 動画ミニ）。サイド／トランスポート／ステータスを隠す。F8 はアナライザを残す。再生は止めない。</summary>
    private bool _libraryMinimalChrome;
    private bool _libraryPlayerHostActive;

    /// <summary>プレイヤーを抜ける直前の全曲。エディタでは選択分だけタブに残し、戻りでは消さない。</summary>
    private DocumentSession[]? _playlistParked;
    /// <summary>F10 突入前のタイル配置。退出後に付け直す。</summary>
    private WaveformTileArrange _librarySuspendedTileArrange;
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
    private int _libraryArtworkShowGeneration;
    /// <summary>
    /// フォルダ追加の実行中。
    /// Shift 付き追加／追加ドロップは重ねない。クリアして追加は Cancel して差し替える。
    /// </summary>
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
    /// <summary>映像／PDF 本再生のつなぎでクロームを出さない。</summary>
    private bool _playlistVisualChromeBridge;

    /// <summary>本再生からの一時停止（テンキー 0）中はクロームを出さない。</summary>
    private bool _playlistVideoImmersivePause;
    /// <summary>
    /// リスト右クリック表示中。
    /// </summary>
    private bool _listContextMenuOpen;
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
        ApplyDocumentTabScaleColumn();
        ApplyTransportBrandLeftInset();
    }

    /// <summary>
    /// タブ左を波形描画開始に揃える。送り ＜ 表示中はボタン幅を確保し、
    /// 全画面では目盛り＋＜ 分だけ右へずらして潰れを防ぐ。
    /// </summary>
    private void ApplyDocumentTabScaleColumn(bool? scrollArrows = null)
    {
        var arrows = scrollArrows
            ?? (TabScrollLeft.Visibility == Visibility.Visible);
        var left = ShowWaveformScaleLane ? DesignMetrics.DbScaleWidth : 0;
        if (arrows)
        {
            left = IsWaveformMaximized
                ? left + DesignMetrics.DocumentTabScrollButtonWidth
                : Math.Max(left, DesignMetrics.DocumentTabScrollButtonWidth);
        }

        var width = left > 0 ? new GridLength(left) : new GridLength(0);
        if (DocumentTabScaleColumn.Width != width)
        {
            DocumentTabScaleColumn.Width = width;
        }
    }

    internal bool IsLibraryGroupComboFocused =>
        IsLibraryMaximized && LibraryBrowser.IsGroupComboFocused;

    internal bool IsLibraryExplorerFocused =>
        IsLibraryMaximized && LibraryBrowser.IsExplorerFocused;

    internal bool IsLibraryFavoritesFocused =>
        IsLibraryMaximized && LibraryBrowser.IsFavoritesFocused;

    /// <summary>
    /// 波形ホストまたはアクティブ波形にキーボードフォーカスがある。
    /// ライブラリ最大化中のツリー／お気に入り／プレイリストは波形ホスト配下にあり
    /// <see cref="WaveformHostBorder"/>.IsKeyboardFocusWithin が真になるため、
    /// それらにフォーカスがあるときは波形エリア扱いにしない（Ctrl+Space プリロールの誤発火防止）。
    /// </summary>
    internal bool IsWaveformAreaFocused =>
        !IsLibraryExplorerFocused
        && !IsLibraryFavoritesFocused
        && !(IsLibraryMaximized && LibraryBrowser.IsListKeyboardFocused)
        && (WaveformHostBorder.IsKeyboardFocusWithin || Waveform.IsKeyboardFocusWithin);

    private void ToggleLibraryMaximize()
    {
        // メニュー表示中、および外側クリック dismiss がプレイヤー切替ボタンへ貫通したときの解除は捨てる。
        // （右クリックメニューを閉じるクリックが下のトランスポートボタンに当たると、
        //   ここが走って選択以外のトラックが消えてしまう。ガードは左ボタン解放まで残る。）
        if (IsLibraryMaximized
            && (_listContextMenuOpen
                || LibraryBrowser.HasOpenContextMenu
                || LibraryBrowser.IsContextMenuDismissGuardActive))
        {
            return;
        }

        ApplyLayoutPreset(WindowLayoutSlot.F10, fromUserShortcut: true);
    }

    private void LibraryBrowser_ListContextMenuOpened(object? sender, EventArgs e)
    {
        _listContextMenuOpen = true;
    }

    private void LibraryBrowser_ListContextMenuClosed(object? sender, EventArgs e)
    {
        _listContextMenuOpen = false;
    }

    /// <summary>
    /// F8 動画専用ミニプレイヤー。前面 UI 無し・映像原寸ウィンドウ枠。
    /// 突入は背面に動画があるとき（本再生または暗いバックグラウンドプレビュー）かつ
    /// プレイリストに再生対象があるとき（拡張子連動は自動）。暗いプレビューからは本再生を再開して入る。
    /// もう一度 F8 で通常プレイヤーへ（再生は続ける）。F9／F10 でプレイリストへ戻る。
    /// </summary>
    private void ToggleVideoMiniPlayer()
    {
        if (RejectOutsideFadeToolAction())
        {
            return;
        }

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

    /// <summary>PDF の明るい表示中（暗いプレビューは含まない）。</summary>
    private bool IsPdfBrightDisplay() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsPdf
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

    /// <summary>
    /// Shift＋テンキー 1／3／7／9・J／K で選択を伸ばせるか。
    /// エディタ全般、およびプレイヤーでシーク＆選択できる波形（捨てキューと同じコンテキスト。暗いプレビューは不可）。
    /// </summary>
    private bool AllowsWaveformSelectionTransport() =>
        _document is not null
        && (!IsLibraryMaximized || IsPlayerScratchContext());

    private void SyncPlayerScratchEnabled()
    {
        var enabled = IsPlayerScratchContext();
        PrimaryWaveform.PlayerScratchEnabled = enabled;
        ForEachWaveform(view => view.PlayerScratchEnabled = enabled);
    }

    /// <summary>
    /// プレイヤー捨てキュー（I／O／M／Ins）。呼び出し前に <see cref="LibraryPlayerMode.AllowsPlayerScratchCueKey"/> でゲートすること。
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

    /// <summary>F9 レイアウトプリセットを適用する。</summary>
    private void ToggleLibraryMinimalChrome() =>
        ApplyLayoutPreset(WindowLayoutSlot.F9, fromUserShortcut: true);

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
        ApplyRememberedVideoChrome();
        if (!_playlistVideoFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
        }
    }

    /// <summary>動画本再生と動画ミニで共通の A／T。未変更なら両方オン。</summary>
    private void ApplyRememberedVideoChrome()
    {
        var settings = AppStorage.Settings;
        LibraryPlayerMode.ResolveVideoLaunchChrome(
            settings.VideoChromeChosen,
            settings.VideoLaunchHud,
            settings.VideoLaunchTimecode,
            out _playlistVideoHud,
            out _playlistVideoTimecode);
    }

    /// <summary>動画本再生／ミニで変えた A／T を以後ずっと使う。</summary>
    private void RememberVideoChrome()
    {
        var settings = AppStorage.Settings;
        if (settings.VideoChromeChosen
            && settings.VideoLaunchHud == _playlistVideoHud
            && settings.VideoLaunchTimecode == _playlistVideoTimecode)
        {
            return;
        }

        settings.VideoChromeChosen = true;
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

        PrimaryWaveform.OverlayChromeScale = 1;
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
        if (show)
        {
            RestoreParkedPlaylist();
        }

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
                // 離脱中もブラウザの行は残っているので rebuild しない（同一集合なら patch）。
                LibraryBrowser.SetSessions(_sessions, _activeSession, rebuild: false);
                ShowLibraryArtworkOrClear(_activeSession);
            }

            // 行高が 133px に落ちたあとの描画でないと、エディタサイズのビットマップが画面外へずれたまま残る。
            ScheduleLibraryWaveformPaint();
            RefreshDisplayedPlaylistPosition();
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
            _libraryPlayOnTrackKeyRelease = false;
            _libraryStopAfterTrack = false;
            _libraryHoldJacketWash = false;
            _ = LeaveLibraryMaximizeAsync();
            if (_videoLaunchPlacement)
            {
                EndVideoLaunchSession();
            }
        }

        SyncPlayerMeterFade();
    }

    /// <summary>
    /// レベルメーター／スペアナ／ラウドネス／ゴニオ／WAAPI 表記と、エディタの波形スクロール。
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
            // 再生中は StartMeterRendering が何度もここに来る。既に不透明なら触らない
            // （FadeElementOpacity の BeginAnimation 解除がジャケット色のカクつきになる）。
            if (LibraryPlayerMode.InstantPlayerMeterFade(show, instantReveal)
                && PlayerMeterTargetsNeedInstantSnap())
            {
                ApplyPlayerMeterFade(visible: true, instant: true);
            }

            return;
        }

        // インは再生中即表示／停止中 1 秒。アウトは常に 1 秒（突入スナップは上で処理済み）。
        ApplyPlayerMeterFade(show, LibraryPlayerMode.InstantPlayerMeterFade(show, instantReveal));
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

        // PDF 表示中は A／T を出さないだけ（フラグは触らない。本再生もミニも保存した A／T を使う）。
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
            // 状態フラグは見えるのに Opacity が固まったまま、を起こさない。
            // フェードイン中の再 Sync（DimPreview の StateChanged ＋直後の Sync など）では触らない。
            EnsurePlaylistVideoChromeOpaque();
        }

        ApplyPlaylistVideoHudFade(hideAtOverlays, instant);
        if (IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo)
        {
            EnsureLibraryVisualPlayheadTicker();
        }

        ApplyPlaylistVideoTimecodeChrome(instant);
        SyncPlaylistTrackIndexToast(
            LibraryPlayerMode.ShowsPlaylistTrackIndex(
                hide,
                LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf,
                _sessions.Count),
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
        SyncPlaylistVisualCursorHide();
    }

    private void EnsurePlaylistVideoWindowRestored()
    {
        // F 全画面中はウィンドウ寸法も触らない（停止やクローム復帰で勝手に戻さない）。
        if (_playlistVideoFullscreen)
        {
            return;
        }

        // レイアウト全画面（F11／F12 等）のままプレイヤーへ移るときはタイトルバーを出さない。
        if (IsWaveformMaximized)
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
            // フェードイン中は NeedsForcedOpaque が false（本再生→暗いプレビュー復帰の 0.2s を守る）。
            if (LibraryPlayerMode.NeedsForcedOpaque(target))
            {
                LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
            }
        }

        var wantStatusBar = _activeLayoutSlot is { } statusSlot
            ? AppStorage.Settings.ResolvedLayoutPreset(statusSlot).ResolvedShowStatusBar(statusSlot)
            : true;
        if (wantStatusBar
            && StatusBarHost.Visibility != Visibility.Visible
            && !_libraryMinimalChrome)
        {
            StatusBarHost.Visibility = Visibility.Visible;
        }
        else if (!wantStatusBar)
        {
            StatusBarHost.Visibility = Visibility.Collapsed;
        }

        ApplyMainMenuBarVisibility();
    }

    private void EnsurePlaylistVideoHudOpaque()
    {
        foreach (var target in PlaylistVideoHudTargets())
        {
            // 波形はメーター非表示条件でも HUD オンなら出す。WAAPI／アナライザは空リストでは出さない。
            if (ReferenceEquals(target, WaveformHostBorder)
                || LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count))
            {
                if (LibraryPlayerMode.NeedsForcedOpaque(target))
                {
                    LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
                }
            }
        }
    }

    private void ApplyPlaylistVideoHudFade(bool hideHud, bool instant)
    {
        var showMeters = LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count);
        foreach (var target in PlaylistVideoHudTargets())
        {
            // 空リストの WAAPI／アナライザは SyncPlayerMeterFade（アウト 1 秒）に任せる。
            // ここで 0.2 秒フェードすると、先に始まった 1 秒アウトを潰す。
            if (IsPlayerMeterHudTarget(target) && !showMeters)
            {
                continue;
            }

            LibraryPlayerMode.FadeElementOpacity(
                target,
                hideHud ? 0 : 1,
                instant,
                hitTestVisible: !hideHud,
                seconds: LibraryPlayerMode.ChromeFadeSeconds);
        }
    }

    private bool IsPlayerMeterHudTarget(UIElement target) =>
        ReferenceEquals(target, MeterTopSlot)
        || ReferenceEquals(target, LevelMeter)
        || ReferenceEquals(target, VectorScope)
        || ReferenceEquals(target, Spectrum)
        || ReferenceEquals(target, LoudnessMeter)
        || ReferenceEquals(target, MonitorToggles);

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

    /// <summary>
    /// 動画／PDF の本再生（明るい表示）／ミニ中のダブルクリックは
    /// フル（タイトルバー無しの F 全画面）⇔ミニ（F8）切替。
    /// 暗いプレビュー上は F キーと同じ全画面オン／オフ。操作系 UI 上は無視。
    /// </summary>
    private void MainWindow_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.Handled)
        {
            return;
        }

        if (LibraryPlayerMode.BlocksVideoFullscreenDoubleClick(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (LibraryPlayerMode.PrefersVisualFullMiniToggleOnDoubleClick(
                IsVideoRealPlayback() || IsPdfBrightDisplay(),
                IsVideoMiniPlayerActive()))
        {
            if (TryTogglePlaylistVisualFullAndMini())
            {
                e.Handled = true;
            }

            return;
        }

        if (TryTogglePlaylistVideoFullscreen())
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// フル（タイトルバー無し）⇔ミニ。全画面中はミニへ、それ以外はフルへ。
    /// </summary>
    private bool TryTogglePlaylistVisualFullAndMini()
    {
        if (LibraryPlayerMode.PrefersEnterMiniOnVisualFullMiniDoubleClick(_playlistVideoFullscreen))
        {
            if (IsVideoMiniPlayerActive())
            {
                // ミニ＋全画面 → 全画面だけ外してミニ窓へ。
                SetPlaylistVideoFullscreen(false);
                return true;
            }

            if (!LibraryPlayerMode.CanToggleVideoMiniPlayer(
                    alreadyActive: false,
                    IsVideoMiniEnterEligible(),
                    hasPlayableSession: _sessions.Count > 0))
            {
                return false;
            }

            // 全画面の本再生 → ミニ（Enter 側で全画面を先に抜ける）。
            ToggleVideoMiniPlayer();
            return true;
        }

        // ミニまたは窓付き本再生 → フル（タイトルバー無し）。
        if (!IsLibraryMaximized
            || !(LibraryBrowser.PlaylistVisualShown
                && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf)))
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
        ApplyPlaylistVideoTimecodeChrome(instant: false);
        SyncPlaylistVideoUiShadows(
            _playlistVideoChromeVisible,
            IsPlaylistVisualHudContext() && !_playlistVideoHud);
        RememberVideoChrome();
        return true;
    }

    private bool TryTogglePlaylistVideoTimecode()
    {
        if (!IsPlaylistVisualHudContext())
        {
            return false;
        }

        _playlistVideoTimecode = !_playlistVideoTimecode;
        ApplyPlaylistVideoTimecodeChrome(instant: false);
        RememberVideoChrome();
        return true;
    }

    /// <summary>T でまとめて出すもの。タイムコードとファイル名。簡易シークバーは A オフで別判定。</summary>
    private void ApplyPlaylistVideoTimecodeChrome(bool instant)
    {
        var chromeHidden = !_playlistVideoChromeVisible;
        var video = LibraryBrowser.PlaylistVisualIsVideo;
        ApplyPlaylistVideoTimecodeFade(
            LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                chromeHidden,
                _playlistVideoTimecode,
                video),
            instant);
        ApplyPlaylistVideoFileNameFade(
            LibraryPlayerMode.ShowsPlaylistVideoFileName(
                chromeHidden,
                _playlistVideoTimecode,
                video),
            instant);
        ApplyPlaylistVideoSimpleSeekFade(
            LibraryPlayerMode.ShowsPlaylistVideoSimpleSeek(
                chromeHidden,
                _playlistVideoHud,
                video),
            instant);
    }

    private bool IsPlaylistVideoSimpleSeekArmed() =>
        LibraryPlayerMode.ShowsPlaylistVideoSimpleSeek(
            !_playlistVideoChromeVisible,
            _playlistVideoHud,
            LibraryBrowser.PlaylistVisualIsVideo);

    private bool _playlistVideoSimpleSeekWantVisible;
    private bool _playlistVideoSimpleSeekWasArmed;
    private long _playlistVideoSimpleSeekKeyboardUntil;
    private DispatcherTimer? _playlistVideoSimpleSeekKeyboardTimer;
    private DispatcherTimer? _playlistVideoSimpleSeekHitTestOffTimer;

    private void ApplyPlaylistVideoSimpleSeekFade(bool show, bool instant)
    {
        if (!show)
        {
            _playlistVideoSimpleSeekWasArmed = false;
            _playlistVideoSimpleSeekKeyboardUntil = 0;
            _playlistVideoSimpleSeekKeyboardTimer?.Stop();
            SetPlaylistVideoSimpleSeekVisible(false, instant);
            return;
        }

        PlaylistVideoSimpleSeek.Margin = DesignMetrics.PlaylistVideoSimpleSeekMargin(VideoMiniOverlayLayoutScale);
        SyncPlaylistVideoSimpleSeekRatio();
        var revealNow = !_playlistVideoSimpleSeekWasArmed;
        _playlistVideoSimpleSeekWasArmed = true;
        if (revealNow)
        {
            // A でアナライザを隠した瞬間は、マウスを待たずすぐ出す。
            NotePlaylistVideoSimpleSeekKeyboard(instant: true);
            return;
        }

        RefreshPlaylistVideoSimpleSeekPresence(instant);
    }

    /// <summary>マウス移動かキーボードの時間移動から 3 秒以内、またはドラッグ中なら出す。</summary>
    private void RefreshPlaylistVideoSimpleSeekPresence(bool instant = false)
    {
        if (!IsPlaylistVideoSimpleSeekArmed())
        {
            _playlistVideoSimpleSeekWasArmed = false;
            _playlistVideoSimpleSeekKeyboardUntil = 0;
            _playlistVideoSimpleSeekKeyboardTimer?.Stop();
            SetPlaylistVideoSimpleSeekVisible(false, instant);
            return;
        }

        var active = Environment.TickCount64 < _playlistVideoSimpleSeekKeyboardUntil;
        SetPlaylistVideoSimpleSeekVisible(
            active || PlaylistVideoSimpleSeek.IsDragging,
            instant);
    }

    private void SetPlaylistVideoSimpleSeekVisible(bool visible, bool instant)
    {
        if (_playlistVideoSimpleSeekWantVisible == visible && !instant)
        {
            // 表示意図が同じでも、フェードアウト開始時に落としたヒットが戻らないことがある。
            if (PlaylistVideoSimpleSeek.IsHitTestVisible != visible)
            {
                PlaylistVideoSimpleSeek.IsHitTestVisible = visible;
            }

            return;
        }

        _playlistVideoSimpleSeekWantVisible = visible;
        if (visible)
        {
            _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
            SyncPlaylistVideoSimpleSeekRatio();
        }

        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoSimpleSeek,
            visible ? 1 : 0,
            instant,
            // フェードアウト中もドラッグできるように、消えるまでヒットを残す。
            hitTestVisible: visible || instant ? visible : null,
            seconds: visible
                ? LibraryPlayerMode.ChromeFadeSeconds
                : LibraryPlayerMode.PlaylistVideoSimpleSeekFadeOutSeconds);
        if (!visible && !instant)
        {
            SchedulePlaylistVideoSimpleSeekHitTestOff();
        }
        else if (!visible)
        {
            _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
        }
    }

    private void SchedulePlaylistVideoSimpleSeekHitTestOff()
    {
        if (_playlistVideoSimpleSeekWantVisible)
        {
            return;
        }

        _playlistVideoSimpleSeekHitTestOffTimer ??= new DispatcherTimer();
        _playlistVideoSimpleSeekHitTestOffTimer.Stop();
        _playlistVideoSimpleSeekHitTestOffTimer.Interval = TimeSpan.FromSeconds(
            LibraryPlayerMode.PlaylistVideoSimpleSeekFadeOutSeconds);
        _playlistVideoSimpleSeekHitTestOffTimer.Tick -= OnPlaylistVideoSimpleSeekHitTestOffTick;
        _playlistVideoSimpleSeekHitTestOffTimer.Tick += OnPlaylistVideoSimpleSeekHitTestOffTick;
        _playlistVideoSimpleSeekHitTestOffTimer.Start();
    }

    private void OnPlaylistVideoSimpleSeekHitTestOffTick(object? sender, EventArgs e)
    {
        _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
        if (_playlistVideoSimpleSeekWantVisible || PlaylistVideoSimpleSeek.IsDragging)
        {
            return;
        }

        PlaylistVideoSimpleSeek.IsHitTestVisible = false;
    }

    private void NotePlaylistVideoSimpleSeekKeyboard(bool instant = false)
    {
        if (!IsPlaylistVideoSimpleSeekArmed())
        {
            return;
        }

        _playlistVideoSimpleSeekKeyboardUntil = Environment.TickCount64
            + (long)(LibraryPlayerMode.PlaylistVideoSimpleSeekKeyboardHoldSeconds * 1000);
        _playlistVideoSimpleSeekKeyboardTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistVideoSimpleSeekKeyboardHoldSeconds),
        };
        _playlistVideoSimpleSeekKeyboardTimer.Stop();
        _playlistVideoSimpleSeekKeyboardTimer.Tick -= OnPlaylistVideoSimpleSeekKeyboardTick;
        _playlistVideoSimpleSeekKeyboardTimer.Tick += OnPlaylistVideoSimpleSeekKeyboardTick;
        _playlistVideoSimpleSeekKeyboardTimer.Start();
        RefreshPlaylistVideoSimpleSeekPresence(instant);
    }

    private void OnPlaylistVideoSimpleSeekKeyboardTick(object? sender, EventArgs e)
    {
        _playlistVideoSimpleSeekKeyboardTimer?.Stop();
        var remain = _playlistVideoSimpleSeekKeyboardUntil - Environment.TickCount64;
        if (remain > 0 && _playlistVideoSimpleSeekKeyboardTimer is not null)
        {
            _playlistVideoSimpleSeekKeyboardTimer.Interval = TimeSpan.FromMilliseconds(remain);
            _playlistVideoSimpleSeekKeyboardTimer.Start();
            return;
        }

        RefreshPlaylistVideoSimpleSeekPresence();
    }

    private void OnPlaylistVideoSimpleSeekPointerMove(object sender, MouseEventArgs e)
    {
        NotePlaylistVideoSimpleSeekKeyboard();
        NotePlaylistVisualCursorActivity();
    }

    private bool _playlistVisualCursorHideArmed;
    private bool _playlistVisualCursorHidden;
    private long _playlistVisualCursorIdleUntil;
    private DispatcherTimer? _playlistVisualCursorHideTimer;

    private bool IsPlaylistVisualCursorHideArmed() =>
        LibraryPlayerMode.HidesMouseCursorForPlaylistVisual(
            !_playlistVideoChromeVisible,
            LibraryBrowser.PlaylistVisualIsVideo,
            LibraryBrowser.PlaylistVisualIsPdf);

    /// <summary>
    /// 動画／PDF 本再生中はカーソルを消し、マウス移動で一時表示してからまた消す。
    /// </summary>
    private void SyncPlaylistVisualCursorHide()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            _playlistVisualCursorHideArmed = false;
            _playlistVisualCursorIdleUntil = 0;
            _playlistVisualCursorHideTimer?.Stop();
            SetPlaylistVisualCursorHidden(false);
            return;
        }

        var justArmed = !_playlistVisualCursorHideArmed;
        _playlistVisualCursorHideArmed = true;
        if (justArmed)
        {
            // 本再生突入時はすぐ消す（マウスを待たない）。
            SetPlaylistVisualCursorHidden(true);
            return;
        }

        RefreshPlaylistVisualCursorHide();
    }

    private void NotePlaylistVisualCursorActivity()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            return;
        }

        _playlistVisualCursorIdleUntil = Environment.TickCount64
            + (long)(LibraryPlayerMode.PlaylistVisualCursorHideIdleSeconds * 1000);
        _playlistVisualCursorHideTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistVisualCursorHideIdleSeconds),
        };
        _playlistVisualCursorHideTimer.Stop();
        _playlistVisualCursorHideTimer.Tick -= OnPlaylistVisualCursorHideTick;
        _playlistVisualCursorHideTimer.Tick += OnPlaylistVisualCursorHideTick;
        _playlistVisualCursorHideTimer.Start();
        SetPlaylistVisualCursorHidden(false);
    }

    private void OnPlaylistVisualCursorHideTick(object? sender, EventArgs e)
    {
        _playlistVisualCursorHideTimer?.Stop();
        var remain = _playlistVisualCursorIdleUntil - Environment.TickCount64;
        if (remain > 0 && _playlistVisualCursorHideTimer is not null)
        {
            _playlistVisualCursorHideTimer.Interval = TimeSpan.FromMilliseconds(remain);
            _playlistVisualCursorHideTimer.Start();
            return;
        }

        RefreshPlaylistVisualCursorHide();
    }

    private void RefreshPlaylistVisualCursorHide()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            SyncPlaylistVisualCursorHide();
            return;
        }

        var keepVisible = Environment.TickCount64 < _playlistVisualCursorIdleUntil
            || PlaylistVideoSimpleSeek.IsDragging;
        SetPlaylistVisualCursorHidden(!keepVisible);
    }

    private void SetPlaylistVisualCursorHidden(bool hidden)
    {
        if (_playlistVisualCursorHidden == hidden)
        {
            if (hidden && Mouse.OverrideCursor != Cursors.None)
            {
                Mouse.OverrideCursor = Cursors.None;
            }
            else if (!hidden && ReferenceEquals(Mouse.OverrideCursor, Cursors.None))
            {
                Mouse.OverrideCursor = null;
            }

            return;
        }

        _playlistVisualCursorHidden = hidden;
        Mouse.OverrideCursor = hidden ? Cursors.None : null;
    }

    private void SyncPlaylistVideoSimpleSeekRatio()
    {
        if (PlaylistVideoSimpleSeek.IsDragging || _document is null)
        {
            return;
        }

        PlaylistVideoSimpleSeek.Ratio = LibraryPlayerMode.SeekRatioAtFrame(
            Waveform.PlayheadFrame,
            _document.FrameCount);
    }

    private void PlaylistVideoSimpleSeek_RatioSeeked(object sender, double ratio)
    {
        if (_document is null)
        {
            return;
        }

        var frame = LibraryPlayerMode.FrameAtSeekRatio(ratio, _document.FrameCount);
        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
        {
            // 再生中はセッションで止め、停止中も通常 Seek の Play→Pause 連打を避ける。
            if (_player.IsPlaying || LibraryBrowser.PlaylistVisualPlaying || _simpleSeekScrubActive)
            {
                BeginSimpleSeekScrubSession(frame);
                if (!_player.IsScrubbing)
                {
                    SeekFrame(frame);
                    return;
                }

                _document.CursorFrame = frame;
                Waveform.PlayheadFrame = frame;
                _player.CaptureScrub(_document, frame);
                RequestPlaylistVideoScrub(frame);
                SyncOverviewPlayhead();
                SyncTransportPosition(frame);
                RefreshStatus();
                return;
            }

            _document.CursorFrame = frame;
            Waveform.PlayheadFrame = frame;
            _player.Seek(frame);
            RequestPlaylistVideoScrub(frame);
            SyncOverviewPlayhead();
            SyncTransportPosition(frame);
            RefreshStatus();
            return;
        }

        SeekFrame(frame);
    }

    private void BeginSimpleSeekScrubSession(long frame)
    {
        if (_simpleSeekScrubActive || _document is null)
        {
            return;
        }

        _simpleSeekScrubActive = true;
        _resumeAfterSimpleSeek = _player.IsPlaying || LibraryBrowser.PlaylistVisualPlaying;
        StopPlaybackShuttle();
        StopSeekNudge();
        StopVideoFrameStep();
        _playTimer.Stop();
        Waveform.SetTrailRecording(false);
        try
        {
            // グレインスクラブへ切り替え（再生デコーダの Seek 連打を避ける）。
            _player.BeginScrub(_document, frame);
            _playbackGeneration = _player.Generation;
            RequestPlaylistVideoScrub(frame, force: true);
            StartMeterRendering();
            Transport.SetPlaying(true);
        }
        catch
        {
            _resumeAfterSimpleSeek = false;
            _simpleSeekScrubActive = false;
            CancelPlaylistVideoScrub();
        }
    }

    private void OnPlaylistVideoSimpleSeekInteractionEnded()
    {
        if (_simpleSeekScrubActive && _document is not null)
        {
            var frame = Waveform.PlayheadFrame;
            var resume = _resumeAfterSimpleSeek;
            _resumeAfterSimpleSeek = false;
            _simpleSeekScrubActive = false;
            FlushPlaylistVideoScrub();
            if (_player.IsScrubbing)
            {
                _player.EndScrub();
            }

            if (resume)
            {
                StartPlayback(frame, prerollSeconds: 0);
            }
            else
            {
                PausePlaybackSoft();
                SeekFrame(frame);
            }
        }
        else
        {
            FlushPlaylistVideoScrub();
        }

        RefreshPlaylistVideoSimpleSeekPresence();
        // ドラッグ中はカーソルを残していたので、離したらアイドル判定へ戻す。
        NotePlaylistVisualCursorActivity();
    }

    /// <summary>
    /// 動画スクラブの Position 更新を間引く。UI ヘッドは呼び出し側が即時更新する。
    /// </summary>
    private void RequestPlaylistVideoScrub(long frame, bool force = false)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return;
        }

        _playlistVideoScrubFrame = frame;
        var now = Environment.TickCount64;
        if (force
            || now - _playlistVideoScrubLastMs >= LibraryPlayerMode.VideoScrubSeekMinIntervalMs)
        {
            FlushPlaylistVideoScrub();
            return;
        }

        _playlistVideoScrubTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.VideoScrubSeekMinIntervalMs),
        };
        _playlistVideoScrubTimer.Tick -= OnPlaylistVideoScrubTimerTick;
        _playlistVideoScrubTimer.Tick += OnPlaylistVideoScrubTimerTick;
        if (!_playlistVideoScrubTimer.IsEnabled)
        {
            var remain = LibraryPlayerMode.VideoScrubSeekMinIntervalMs - (now - _playlistVideoScrubLastMs);
            _playlistVideoScrubTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, remain));
            _playlistVideoScrubTimer.Start();
        }
    }

    private void OnPlaylistVideoScrubTimerTick(object? sender, EventArgs e)
    {
        _playlistVideoScrubTimer?.Stop();
        FlushPlaylistVideoScrub();
    }

    private void FlushPlaylistVideoScrub()
    {
        _playlistVideoScrubTimer?.Stop();
        if (_document is null || _playlistVideoScrubFrame < 0)
        {
            return;
        }

        var frame = _playlistVideoScrubFrame;
        _playlistVideoScrubFrame = -1;
        _playlistVideoScrubLastMs = Environment.TickCount64;
        LibraryBrowser.ScrubPlaylistVisual(
            TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)));
    }

    private void CancelPlaylistVideoScrub()
    {
        _playlistVideoScrubTimer?.Stop();
        _playlistVideoScrubFrame = -1;
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

    /// <summary>
    /// 複数再生の位置トースト。許可時は曲／件数が変わったときだけ出し、少ししたらフェードアウト。
    /// </summary>
    private void SyncPlaylistTrackIndexToast(bool allow, bool instant)
    {
        if (!allow)
        {
            _playlistTrackIndexToastKey = null;
            HidePlaylistTrackIndex(instant);
            return;
        }

        var key = BuildPlaylistTrackIndexToastKey();
        if (string.Equals(_playlistTrackIndexToastKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _playlistTrackIndexToastKey = key;
        FlashPlaylistTrackIndex();
    }

    private string BuildPlaylistTrackIndexToastKey()
    {
        var index = DisplayedPlaylistIndex(_activeSession);
        var count = DisplayedPlaylistCount();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{index}/{count}");
    }

    /// <summary>曲切替・本再生／ミニ突入時。番号を出してから一定時間でフェードアウト。</summary>
    private void FlashPlaylistTrackIndex()
    {
        ApplyPlaylistTrackIndexOverlayScale(VideoMiniOverlayLayoutScale);
        RefreshPlaylistTrackIndex();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistTrackIndex,
            to: 1,
            instant: true,
            hitTestVisible: false);
        _playlistTrackIndexToastTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistTrackIndexToastHoldSeconds),
        };
        _playlistTrackIndexToastTimer.Tick -= OnPlaylistTrackIndexToastTick;
        _playlistTrackIndexToastTimer.Tick += OnPlaylistTrackIndexToastTick;
        _playlistTrackIndexToastTimer.Stop();
        _playlistTrackIndexToastTimer.Start();
    }

    private void OnPlaylistTrackIndexToastTick(object? sender, EventArgs e)
    {
        _playlistTrackIndexToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistTrackIndex,
            to: 0,
            instant: false,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void HidePlaylistTrackIndex(bool instant)
    {
        _playlistTrackIndexToastTimer?.Stop();
        LibraryPlayerMode.FadeElementOpacity(
            PlaylistTrackIndex,
            to: 0,
            instant,
            hitTestVisible: false,
            seconds: instant ? null : LibraryPlayerMode.ChromeFadeSeconds);
    }

    private void RefreshPlaylistTrackIndex()
    {
        var index = DisplayedPlaylistIndex(_activeSession);
        var count = DisplayedPlaylistCount();
        PlaylistTrackIndexText.Text = LibraryPlayerMode.FormatPlaylistTrackLabel(index, count);
    }

    private void ApplyPlaylistTrackIndexOverlayScale(double scale)
    {
        var designHeight = DesignMetrics.PlaylistTrackIndexHostHeight;
        var designMargin = DesignMetrics.PlaylistTrackIndexMargin;
        if (!IsVideoMiniPlayerActive() || _playlistVideoFullscreen)
        {
            PlaylistTrackIndex.LayoutTransform = Transform.Identity;
            PlaylistTrackIndex.Height = designHeight;
            PlaylistTrackIndex.Margin = designMargin;
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

        PlaylistTrackIndex.LayoutTransform = Transform.Identity;
        PlaylistTrackIndex.Height = designHeight * s;
        PlaylistTrackIndex.Margin = new Thickness(
            designMargin.Left * s,
            designMargin.Top * s,
            designMargin.Right * s,
            designMargin.Bottom * s);
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
        var rawName = text;
        text = LibraryPlayerMode.FormatPlaylistVideoFileName(text, usingProxy);
        PlaylistVideoFileNameText.Text = text;
        PlaylistVideoFileNameShadow.Text = text;
        FileNameColorMarkerVisual.Apply(PlaylistVideoFileNameMarker, rawName, source);
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
        // 簡易シーク（A オフ）のあいだはラウドネスへ避けず、画面中央に置く。
        if (!LibraryPlayerMode.ShowsPlaylistVideoFileName(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                LibraryBrowser.PlaylistVisualIsVideo)
            || IsPlaylistVideoSimpleSeekArmed()
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

        QueueVideoHudFrostLayout();
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
    /// Escape／Q で本再生／イマーシブ一時停止／PDF 明るい表示から暗いプレビューへ戻す（F10）。
    /// 動画ミニの Escape／Q は Shortcut 側でプレイリストへ戻す。
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
            && !LibraryBrowser.PlaylistVisualFramePaused)
        {
            return false;
        }

        StopVideoFrameStep();
        DropPlaylistVideoToDimPreview();
        _libraryStopAfterTrack = false;
        return true;
    }

    private bool CanOfferExitRealPlayback()
    {
        if (IsVideoMiniPlayerActive())
        {
            return true;
        }

        if (LibraryBrowser.PlaylistVisualIsPdf && LibraryBrowser.PlaylistVisualPlaying)
        {
            return true;
        }

        if (!LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(
                IsPlaybackActive(),
                _playlistVideoImmersivePause))
        {
            return false;
        }

        return IsPlaylistVideoPlaying()
            || _playlistVideoImmersivePause
            || LibraryBrowser.PlaylistVisualFramePaused;
    }

    private bool CanOfferForceVideoProxy()
    {
        if (!IsLibraryMaximized || IsUiBusy)
        {
            return false;
        }

        var session = ResolveForceVideoProxySession();
        return session is not null && LibraryPlaylistDocuments.IsVideo(session.Document);
    }

    private bool IsPlaylistVideoPlaying() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsVideo
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>
    /// 停止中の Enter を本再生／表示にする対象（動画／PDF）。Enter は先頭から、Space は再開。
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
        LibraryPlayerMode.IsPlaylistVideoHudContext(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualIsVideo,
            LibraryBrowser.PlaylistVisualPlaying,
            _playlistVideoImmersivePause,
            LibraryBrowser.PlaylistVisualFramePaused);

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
        SyncShellFullscreen();
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
            LibraryBrowser,
            LibrarySplitter,
            MeterColumnSplitter,
            TransportBarHost,
            DocumentTabHost,
            HistoryStrip,
        ];

    /// <summary>A トグルで出す／消す HUD。波形・アナライザに加え WAAPI 表記（MeterTopSlot）も含む。</summary>
    private UIElement[] PlaylistVideoHudTargets() =>
        [WaveformHostBorder, MeterTopSlot, LevelMeter, VectorScope, Spectrum, LoudnessMeter, MonitorToggles];

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

        // ファイル名は XAML の文字シルエット影。ラベル全体への Effect は使わない。T と一緒に出す。
        PlaylistVideoFileName.Effect = null;
        LibraryPlayerMode.SetVideoTextShadow(
            PlaylistVideoFileNameShadow,
            video
            && LibraryPlayerMode.ShowsPlaylistVideoFileName(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                isVideo: true));
        // タイムコードは T の表示中だけ影（A の HUD とは独立）。
        LibraryPlayerMode.SetVideoUiShadow(
            PlaylistVideoTimecode,
            video
            && LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                isVideo: true));
        SyncPlaylistVideoHudLabelColors();
    }

    /// <summary>動画本再生／ミニ限定。メーター文字をファイル名と同じ #F5F5F5 に。</summary>
    private void SyncPlaylistVideoHudLabelColors()
    {
        var on = LibraryPlayerMode.UsesVideoOverlayLabelFore(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualIsVideo,
            !_playlistVideoChromeVisible,
            IsVideoMiniPlayerActive());
        LevelMeter.VideoOverlayLabels = on;
        VectorScope.VideoOverlayLabels = on;
        Spectrum.VideoOverlayLabels = on;
        LoudnessMeter.VideoOverlayLabels = on;
        MonitorToggles.VideoOverlayLabels = on;
        ForEachWaveform(view => view.VideoOverlayLabels = on);
        SyncVideoHudFrost(on);
    }

    private DispatcherTimer? _videoHudFrostTimer;

    /// <summary>
    /// 動画本再生／ミニの波形・ピーク・ゴニオ・スペアナ・ラウドネス・ファイル名の背後だけ、
    /// 少し暗くする。ラウドネスとスペアナのあいだ（モニター列）は一枚で埋める。縁は映像へ溶かす。
    /// </summary>
    private void SyncVideoHudFrost(bool on)
    {
        if (!on)
        {
            _videoHudFrostTimer?.Stop();
            _videoHudFrostLayoutQueued = false;
            LibraryBrowser.SetVideoHudFrost(false, null);
            return;
        }

        _videoHudFrostTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoHudFrostTimer.Tick -= OnVideoHudFrostTick;
        _videoHudFrostTimer.Tick += OnVideoHudFrostTick;
        if (!_videoHudFrostTimer.IsEnabled)
        {
            _videoHudFrostTimer.Start();
        }

        OnVideoHudFrostTick(null, EventArgs.Empty);
    }

    private bool _videoHudFrostLayoutQueued;

    /// <summary>レイアウト直後のすりガラス位置合わせ。連打は1フレームにまとめる。</summary>
    private void QueueVideoHudFrostLayout()
    {
        if (_videoHudFrostTimer is not { IsEnabled: true } || _videoHudFrostLayoutQueued)
        {
            return;
        }

        _videoHudFrostLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _videoHudFrostLayoutQueued = false;
            if (_videoHudFrostTimer is { IsEnabled: true })
            {
                OnVideoHudFrostTick(null, EventArgs.Empty);
            }
        });
    }

    private void OnVideoHudFrostTick(object? sender, EventArgs e) =>
        LibraryBrowser.SetVideoHudFrost(true, BuildVideoHudFrostClip());

    private Geometry? BuildVideoHudFrostClip()
    {
        var stage = LibraryBrowser.VideoHudFrostTarget;
        if (stage is null || stage.ActualWidth < 1 || stage.ActualHeight < 1)
        {
            return null;
        }

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var element in VideoHudFrostElements)
        {
            if (!IsVideoHudFrostElementShown(element))
            {
                continue;
            }

            try
            {
                var transform = element.TransformToVisual(stage);
                var rect = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (rect.Width < 1 || rect.Height < 1)
                {
                    continue;
                }

                group.Children.Add(new RectangleGeometry(rect));
            }
            catch (InvalidOperationException)
            {
            }
        }

        // ラウドネス〜モニター列〜スペアナは外接一枚。列マージンやボタン周りの隙間を残さない。
        TryAddLoudnessMonitorSpectrumFrost(group, stage);

        return group.Children.Count == 0 ? null : group;
    }

    /// <summary>
    /// ラウドネス・モニター列・スペアナをまとめた外接矩形を一枚だけ追加する。
    /// 個別 Rect だと列マージンと MonitorToggles 上下の空きが抜ける。
    /// </summary>
    private void TryAddLoudnessMonitorSpectrumFrost(GeometryGroup group, Visual stage)
    {
        FrameworkElement[] band = [LoudnessMeter, MonitorToggles, Spectrum];
        var has = false;
        var left = 0d;
        var top = 0d;
        var right = 0d;
        var bottom = 0d;
        foreach (var element in band)
        {
            if (!IsVideoHudFrostElementShown(element))
            {
                continue;
            }

            try
            {
                var rect = element.TransformToVisual(stage)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (rect.Width < 1 || rect.Height < 1)
                {
                    continue;
                }

                if (!has)
                {
                    left = rect.Left;
                    top = rect.Top;
                    right = rect.Right;
                    bottom = rect.Bottom;
                    has = true;
                }
                else
                {
                    left = Math.Min(left, rect.Left);
                    top = Math.Min(top, rect.Top);
                    right = Math.Max(right, rect.Right);
                    bottom = Math.Max(bottom, rect.Bottom);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (!has)
        {
            return;
        }

        var width = right - left;
        var height = bottom - top;
        if (width < 1 || height < 1)
        {
            return;
        }

        group.Children.Add(new RectangleGeometry(new Rect(left, top, width, height)));
    }

    private static bool IsVideoHudFrostElementShown(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth < 2 || element.ActualHeight < 2)
        {
            return false;
        }

        var opacity = 1d;
        DependencyObject? current = element;
        while (current is UIElement visual)
        {
            opacity *= visual.Opacity;
            if (opacity <= 0.05)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private FrameworkElement[]? _videoHudFrostElements;

    /// <summary>
    /// ラウドネス／モニター／スペアナは <see cref="TryAddLoudnessMonitorSpectrumFrost"/> で一枚。
    /// 個別に入れると列マージンが抜けて隙間になる。
    /// </summary>
    private FrameworkElement[] VideoHudFrostElements =>
        _videoHudFrostElements ??=
            [WaveformTileHost, LevelMeter, VectorScope, PlaylistVideoFileName];

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

    private bool PlayerMeterTargetsNeedInstantSnap()
    {
        foreach (var target in PlayerMeterFadeTargets())
        {
            if (LibraryPlayerMode.NeedsInstantOpaqueSnap(target))
            {
                return true;
            }
        }

        return false;
    }

    private UIElement[] PlayerMeterFadeTargets() =>
        _playlistVideoChromeVisible
            ? [MeterTopSlot, LevelMeter, VectorScope, Spectrum, LoudnessMeter, MonitorToggles, TimeScrollStrip]
            : [MeterTopSlot, LevelMeter, VectorScope, Spectrum, LoudnessMeter, MonitorToggles];

    /// <summary>
    /// プレイヤー中はクロムの塗りを外し、ウィンドウ全体のジャケットウォッシュを透かす。
    /// 入るときはグローを出してから塗りを外し、出るときは先に塗りを戻す。
    /// </summary>
    private void ApplyLibraryWashChrome(bool show)
    {
        if (show)
        {
            StatusBarHost.Background = Brushes.Transparent;
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

        MonitorToggles.WashThrough = show;

        LevelMeter.InvalidateVisual();
        VectorScope.InvalidateVisual();
        Spectrum.InvalidateVisual();
        SyncPlaylistVideoHudLabelColors();
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
        // タイル構築やウィンドウ寸法の復元よりピーク完了が先だと、空の Primary へ描いて終わることがある。
        // アクティブを載せ直してから、レイアウト確定後にもう一度描く。
        RebindActiveWaveformAfterLibraryLeave();
        ForEachWaveform(view => view.Refresh());
        Overview.Refresh();
        ScheduleEditorWaveformPaint();
    }

    /// <summary>
    /// プレイヤー退出直後。タイル未作成時に Primary へ描いてしまわないよう、表示中ビューへ載せ直す。
    /// </summary>
    private void RebindActiveWaveformAfterLibraryLeave()
    {
        if (IsLibraryMaximized || _activeSession is null)
        {
            return;
        }

        if (_tileMode)
        {
            if (FindTilePane(_activeSession) is { } pane)
            {
                if (!ReferenceEquals(pane.View.Document, _activeSession.Document))
                {
                    ApplySessionToView(pane.View, _activeSession, applyAnalysis: false);
                }

                _tileActiveView = pane.View;
                Overview.SeekTrailSource = pane.View;
                Overview.Document = _activeSession.Document;
                return;
            }

            // タイルがまだ無いときは Primary を触らない（ClearSingle 済みの空描画を避ける）。
            return;
        }

        if (!ReferenceEquals(Waveform.Document, _activeSession.Document))
        {
            BindSingleWorkspace(_activeSession);
        }
    }

    /// <summary>
    /// エディタへ戻したあと、ウィンドウ／タイルのレイアウトが決まってから波形を描き直す。
    /// プレイヤー帯のまま描くと空に見え、タブを行き来するまで直らないことがある。
    /// </summary>
    private void ScheduleEditorWaveformPaint()
    {
        var ticket = ++_libraryWavePaintTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PaintEditorWaveform(ticket));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PaintEditorWaveform(ticket));
    }

    private void PaintEditorWaveform(int ticket)
    {
        if (ticket != _libraryWavePaintTicket || IsLibraryMaximized)
        {
            return;
        }

        if (_tileMode)
        {
            foreach (var pane in _tilePanes)
            {
                if (!_sessions.Contains(pane.Session))
                {
                    continue;
                }

                if (!ReferenceEquals(pane.View.Document, pane.Session.Document))
                {
                    ApplySessionToView(pane.View, pane.Session, applyAnalysis: false);
                }

                pane.View.UpdateLayout();
                if (pane.View.ActualWidth >= 8 && pane.View.ActualHeight >= 8)
                {
                    pane.View.Refresh();
                }
            }

            Overview.Refresh();
            return;
        }

        if (Waveform.Document is null && _activeSession is not null)
        {
            BindSingleWorkspace(_activeSession);
        }

        Waveform.UpdateLayout();
        if (Waveform.ActualWidth >= 8 && Waveform.ActualHeight >= 8)
        {
            Waveform.Refresh();
        }

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

    /// <summary>抜ける前のプレイリストを戻す。エディタで開いているファイルも残す。</summary>
    private void RestoreParkedPlaylist()
    {
        var parked = _playlistParked;
        _playlistParked = null;
        if (parked is null || parked.Length == 0)
        {
            return;
        }

        // 退避中に破棄閉じされた参照が dirty のまま残っていると、復帰後にまた未保存確認が出る。
        parked = SanitizeParkedPlaylistSessions(parked);
        if (parked.Length == 0 && _sessions.Count == 0)
        {
            return;
        }

        var merged = LibraryPlayerMode.MergeParkedPlaylist(parked, _sessions);
        if (merged.Count == _sessions.Count)
        {
            var same = true;
            for (var i = 0; i < merged.Count; i++)
            {
                if (!ReferenceEquals(merged[i], _sessions[i]))
                {
                    same = false;
                    break;
                }
            }

            if (same)
            {
                return;
            }
        }

        var active = _activeSession;
        _sessions.Clear();
        _sessions.AddRange(merged);
        if (active is not null && !_sessions.Contains(active))
        {
            active = _sessions.Count > 0 ? _sessions[0] : null;
        }

        if (!ReferenceEquals(_activeSession, active))
        {
            BindWorkspace(active);
        }
        else
        {
            RebuildTabBar();
        }
    }

    /// <summary>
    /// エディタで開いていない dirty 退避をディスク版へ戻す。戻せない無題は落とす。
    /// </summary>
    private DocumentSession[] SanitizeParkedPlaylistSessions(DocumentSession[] parked)
    {
        List<DocumentSession>? kept = null;
        for (var i = 0; i < parked.Length; i++)
        {
            var session = parked[i];
            var drop = false;
            if (session.Document.IsDirty && !_sessions.Contains(session)
                && !TryReloadSessionDocument(session))
            {
                // 無題・欠落パスは会員から外す。再読込失敗（権限など）は dirty のまま残し、後で再確認。
                var path = session.Document.SourcePath;
                drop = string.IsNullOrWhiteSpace(path) || !File.Exists(path);
            }

            if (drop)
            {
                kept ??= CopyParkedPrefix(parked, i);
                continue;
            }

            kept?.Add(session);
        }

        return kept?.ToArray() ?? parked;
    }

    private static List<DocumentSession> CopyParkedPrefix(DocumentSession[] parked, int count)
    {
        var list = new List<DocumentSession>(parked.Length);
        for (var i = 0; i < count; i++)
        {
            list.Add(parked[i]);
        }

        return list;
    }

    private int DisplayedPlaylistIndex(DocumentSession? session)
    {
        if (session is not null
            && LibraryBrowser.TryGetDisplayedTrackPosition(session, out var index, out _))
        {
            return index;
        }

        return session is null ? -1 : _sessions.IndexOf(session);
    }

    private int DisplayedPlaylistCount()
    {
        if (_activeSession is not null
            && LibraryBrowser.TryGetDisplayedTrackPosition(_activeSession, out _, out var count))
        {
            return count;
        }

        return _sessions.Count;
    }

    /// <summary>
    /// プレイヤーを抜けるとき、リストで選んだファイルだけ残す。保存確認でキャンセルしたら false。
    /// dirty の確認を先に行い、キャンセル時は未編集セッションを消さない（途中破壊を防ぐ）。
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

        // 保存確認を先に。キャンセルなら clean も触らない。
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

        // 抜けが確定してから未編集をまとめて外す。
        if (clean.Count > 0)
        {
            DropLibrarySessionsFast(clean);
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

    private void SyncPlaylistSelectionLoudness() =>
        SyncSelectionLoudness(measureMissing: true);

    /// <summary>
    /// プレイヤーはプレイリスト、エディタはタブの複数選択。停止中は5項目の平均をメーターへ出す。
    /// </summary>
    private void SyncSelectionLoudness(bool measureMissing)
    {
        var selected = SelectionForLoudnessAverage();
        if (selected.Length < 2)
        {
            CancelSelectionLoudness();
            LoudnessMeter.SetSelectionLoudness(null);
            return;
        }

        var readings = new List<FileLoudness>(selected.Length);
        var missing = measureMissing ? new List<DocumentSession>() : null;
        foreach (var session in selected)
        {
            if (StoredFileLoudness(session) is FileLoudness loudness)
            {
                readings.Add(loudness);
                continue;
            }

            if (measureMissing)
            {
                missing!.Add(session);
            }
        }

        LoudnessMeter.SetSelectionLoudness(
            readings.Count > 0
                ? FileLoudness.Average(readings)
                : new FileLoudness(
                    float.NegativeInfinity,
                    float.NegativeInfinity,
                    float.NegativeInfinity,
                    float.NegativeInfinity,
                    float.NegativeInfinity));
        if (missing is { Count: > 0 })
        {
            ScheduleSelectionLoudness(missing);
        }
    }

    private DocumentSession[] SelectionForLoudnessAverage()
    {
        if (IsLibraryMaximized)
        {
            return LibraryBrowser.SelectedSessions;
        }

        return HasMultipleTabSelection ? SelectedTabsInOrder() : [];
    }

    private static FileLoudness? StoredFileLoudness(DocumentSession session)
    {
        var document = session.Document;
        var path = document.SourcePath;
        if (!string.IsNullOrWhiteSpace(path)
            && LibraryMediaCache.TryGetFileLoudness(path, out var cached))
        {
            if (!document.Tags.HasFileLoudness)
            {
                document.ApplyTags(document.Tags.WithFileLoudness(cached));
            }

            return cached;
        }

        if (document.Tags.HasFileLoudness && !string.IsNullOrWhiteSpace(path))
        {
            document.ApplyTags(document.Tags.WithoutFileLoudness());
            return null;
        }

        return document.Tags.HasFileLoudness ? document.Tags.ToFileLoudness() : null;
    }

    private void CancelSelectionLoudness()
    {
        _selectionLoudnessGeneration++;
        if (_selectionLoudnessCts is null)
        {
            return;
        }

        try
        {
            _selectionLoudnessCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _selectionLoudnessCts.Dispose();
        _selectionLoudnessCts = null;
    }

    private void ScheduleSelectionLoudness(List<DocumentSession> missing)
    {
        CancelSelectionLoudness();
        var cts = new CancellationTokenSource();
        _selectionLoudnessCts = cts;
        var generation = _selectionLoudnessGeneration;
        _ = MeasureSelectionLoudnessAsync(missing, generation, cts.Token);
    }

    private async Task MeasureSelectionLoudnessAsync(
        List<DocumentSession> missing,
        int generation,
        CancellationToken cancellationToken)
    {
        foreach (var session in missing)
        {
            if (generation != _selectionLoudnessGeneration || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var document = session.Document;
            if (document.Tags.HasFileLoudness)
            {
                continue;
            }

            FileLoudness? measured;
            try
            {
                measured = await Task.Run(
                    () =>
                    {
                        if (!LoudnessMeterEngine.TryMeasureFileLoudness(
                                document,
                                out var value,
                                cancellationToken))
                        {
                            return (FileLoudness?)null;
                        }

                        if (document.SourcePath is { Length: > 0 } path)
                        {
                            LibraryMediaCache.PutFileLoudness(path, value);
                        }

                        return value;
                    },
                    cancellationToken).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                continue;
            }

            if (generation != _selectionLoudnessGeneration || measured is null)
            {
                continue;
            }

            document.ApplyTags(document.Tags.WithFileLoudness(measured.Value));
        }

        if (generation == _selectionLoudnessGeneration && !cancellationToken.IsCancellationRequested)
        {
            SyncSelectionLoudness(measureMissing: false);
        }
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
        // フォルダ再帰追加と同じく、追加完了まで背景色替えを遅延する。
        var batchGeneration = _libraryFolderShowGeneration;
        var deferWash = IsLibraryMaximized;
        if (deferWash)
        {
            _libraryFolderShowInProgress = true;
        }

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

                DocumentSession session;
                try
                {
                    session = await Task.Run(() => CreateLibrarySession(path)).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    continue;
                }

                _sessions.Add(session);
                opened ??= session;
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
                    if (keep is not null)
                    {
                        QueuePendingLibraryWash(keep);
                        PreviewIdleLibrarySessionWithoutArtwork(keep);
                    }
                }

                SyncPlayerMeterFade();
                return;
            }

            if (playFirst)
            {
                SelectAndPlayFirstLibraryTrack(deferAmbientWash: deferWash);
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
                if (deferWash)
                {
                    QueuePendingLibraryWash(active);
                    _ = PlayLibrarySessionAsync(active, keepAmbientGlow: true);
                }
                else
                {
                    _ = PlayLibrarySessionAsync(active);
                }
            }

            SyncPlayerMeterFade();
        }
        finally
        {
            if (showProgress)
            {
                ClearOpenStatus();
            }

            if (deferWash
                && LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                    batchGeneration,
                    _libraryFolderShowGeneration))
            {
                _libraryFolderShowInProgress = false;
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
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
        var willRemove = false;
        foreach (var session in selected)
        {
            if (_sessions.Contains(session))
            {
                willRemove = true;
                break;
            }
        }

        if (!willRemove)
        {
            return;
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
        // 同一集合なら DataGrid を張り直さず選択だけ先頭へ（起動・明示的な playFirst 用）。
        LibraryBrowser.SetSessions(_sessions, first, selected, rebuild: false);
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

    private void SelectAndPlayFirstLibraryTrack(bool deferAmbientWash = false)
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
        if (deferAmbientWash)
        {
            if (first is not null)
            {
                QueuePendingLibraryWash(first);
                LibraryBrowser.RequestListFocus();
                // 動画ミニ（拡張子／引数）は直後に本再生するので、暗いプレビュー＋格子を挟まない。
                if (!_videoLaunchPlacement)
                {
                    PreviewLibrarySession(first);
                }

                _ = PlayLibrarySessionAsync(first, keepAmbientGlow: true);
            }

            return;
        }

        LibraryBrowser.SetArtwork(first?.Document, keepCurrentIfEmpty: first is null && _libraryHoldJacketWash);
        if (first is not null)
        {
            _libraryHoldJacketWash = false;
            LibraryBrowser.RequestListFocus();
            if (!_videoLaunchPlacement)
            {
                PreviewLibrarySession(first);
            }
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

    private void TryPlayLibraryAfterTrackKeyRelease()
    {
        if (!_libraryPlayOnTrackKeyRelease || !IsLibraryMaximized)
        {
            _libraryPlayOnTrackKeyRelease = false;
            return;
        }

        if (Keyboard.IsKeyDown(Key.NumPad4) || Keyboard.IsKeyDown(Key.NumPad6))
        {
            return;
        }

        _libraryPlayOnTrackKeyRelease = false;
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is not null)
        {
            _ = PlayLibrarySessionAsync(session);
        }
    }

    private void StepLibraryTrackSelection(int direction, bool wrap)
    {
        // 単発は再生中の曲から（端で回る）。長押しの続きは動いた選択から（端で止まる）。
        var cursor = wrap && IsPlaybackActive()
            ? (_activeSession ?? LibraryBrowser.SelectedSession)
            : (LibraryBrowser.SelectedSession ?? _activeSession);
        LibraryBrowser.SelectPlaylistStep(cursor, direction, wrap);
        _libraryPlayOnTrackKeyRelease = true;
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
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled
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

        // 非圧縮はメモリ再生。ギャップレス先読みストリームには載せない。
        if (AudioCodec.IsUncompressedPcm(document.SourceKind)
            || (document.SourcePath is { Length: > 0 } nextPath && AudioCodec.IsUncompressedPcm(nextPath)))
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
        if (!IsLibraryMaximized
            || _libraryStopAfterTrack
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled
            || !_player.HasGaplessAdvancePending)
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
        if (_libraryStopAfterTrack
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled)
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
            _ = ResumeLibraryDocumentPlaybackAsync();
            return;
        }

        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    /// <summary>
    /// 既に載っているドキュメントをシーク位置から再開する。
    /// 動画でプロキシ未作成なら、自動生成設定に従って先に Ensure する。
    /// </summary>
    private async Task ResumeLibraryDocumentPlaybackAsync()
    {
        if (_document is null)
        {
            return;
        }

        var startFrame = _document.CursorFrame;
        if (_activeSession is { } session
            && !await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        StartPlayback(startFrame, prerollSeconds: 0);
        ScheduleLibraryGaplessPrefetch();
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
            // プロキシ未作成のまま頭出しした場合も、ここで自動生成する。
            _ = RestartLibraryVideoFromStartAsync(session);
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

    private async Task RestartLibraryVideoFromStartAsync(DocumentSession session)
    {
        session.Document.CursorFrame = 0;
        Waveform.PlayheadFrame = 0;
        if (!await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
        {
            return;
        }

        StartPlayback(0, prerollSeconds: 0);
    }

    private void SeekLibraryBySeconds(double seconds)
    {
        if (_document is null)
        {
            return;
        }

        NotePlaylistVideoSimpleSeekKeyboard();

        var frames = (long)Math.Round(_document.SampleRate * seconds);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        SeekFrame(
            current + frames,
            IsPlaybackActive() ? LibraryPlayerMode.SeekNudgeFadeMilliseconds : 0);
        if (_transportSelectionExtend)
        {
            Waveform.ExtendSelectionTo(Waveform.PlayheadFrame);
        }
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

    private bool IsSeekNudgeHeld(int direction)
    {
        if (_transportSelectionExtend)
        {
            var expected = direction < 0 ? 7 : 9;
            var keyDown = _transportSelectionDigit == expected
                || (direction < 0
                    ? Keyboard.IsKeyDown(Key.NumPad7) || Keyboard.IsKeyDown(Key.J)
                    : Keyboard.IsKeyDown(Key.NumPad9) || Keyboard.IsKeyDown(Key.K));
            return keyDown
                && (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                && (Keyboard.Modifiers & ~(ModifierKeys.Shift)) == 0;
        }

        var plainKeyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.NumPad7) || Keyboard.IsKeyDown(Key.J)
            : Keyboard.IsKeyDown(Key.NumPad9) || Keyboard.IsKeyDown(Key.K);
        return plainKeyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopSeekNudge()
    {
        var wasExtend = _transportSelectionExtend
            && _transportSelectionDigit is 7 or 9;
        _seekNudgeDirection = 0;
        _seekNudgeRepeatStarted = false;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        if (wasExtend)
        {
            _transportSelectionDigit = -1;
            _transportSelectionExtend = false;
            SyncPlayWindowFromDocument();
        }
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
        NotePlaylistVideoSimpleSeekKeyboard();
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
        bool keepAmbientGlow = false,
        bool fromStart = false)
    {
        StopVideoFrameStep();
        CancelLibraryGapless();
        _libraryPlayOnArrowRelease = false;
        _libraryPlayOnTrackKeyRelease = false;
        _libraryStopAfterTrack = stopAfterTrack;
        var playTicket = ++_libraryPlayTicket;
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
            if (playTicket != _libraryPlayTicket)
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

            if (playTicket != _libraryPlayTicket)
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
                    && !await TryPrepareVideoProxyAsync(session, playTicket).ConfigureAwait(true))
                {
                    return;
                }

                if (playTicket != _libraryPlayTicket)
                {
                    return;
                }

                // プロキシ待ちのあいだに別ロードが _libraryLoadSession を動かしても、
                // この再生チケットが生きていれば本再生へ進む。
                _libraryLoadSession = session;
                StartLibraryVisualPlayback(session, stopAfterTrack, fromStart);
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
    /// 再生中は HaltPlaybackToStart（動画本再生中だけその場一時停止）。
    /// 停止中はシーク位置（CursorFrame）から単独再生／再開（曲末で次へ進まない）。
    /// 別曲へ切り替えるときだけ PlayLibrarySessionAsync（その曲は先頭から）。
    /// </summary>
    /// <param name="fromStart">
    /// true なら常に先頭から（プレイヤー Enter）。false なら同じ曲はシーク位置から再開（Space）。
    /// </param>
    private async void PlayLibrarySelectionOnceOrToggle(bool fromStart = false)
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // 再生中：動画本再生は一時停止／再開用にその場停止。それ以外は開始位置へ戻す。
        if (IsPlaybackActive())
        {
            StopVideoFrameStep();
            if (LibraryPlayerMode.PausesVideoRealPlaybackInPlaceOnSpace(IsPlaylistVideoPlaying()))
            {
                PausePlaybackHere(immersiveVideoHold: true);
            }
            else
            {
                HaltPlaybackToStart(keepVideoChromeHidden: false);
            }

            _libraryStopAfterTrack = false;
            return;
        }

        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null)
        {
            return;
        }

        // 同じ曲が既に載っているとき：Space はシークバー位置から、Enter は先頭から。
        if (ReferenceEquals(session, _activeSession) && _document is not null)
        {
            _libraryStopAfterTrack = true;
            if (LibraryPlaylistDocuments.IsPdf(session.Document))
            {
                await PlayLibrarySessionAsync(session, stopAfterTrack: true, fromStart: fromStart)
                    .ConfigureAwait(true);
                return;
            }

            // 選択／ホバープレビューはプロキシを待たない。Space 本再生ではここで作る。
            // Enter（fromStart）は先頭固定。Space は暗いプレビュー中の位置を引き継ぐ。
            var startFrame = fromStart ? 0L : _document.CursorFrame;
            if (!await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
            {
                return;
            }

            if (_document is null)
            {
                return;
            }

            if (fromStart)
            {
                session.Document.CursorFrame = 0;
                Waveform.PlayheadFrame = 0;
            }
            else if (LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && !LibraryBrowser.PlaylistVisualPlaying
                && LibraryBrowser.IsPlaylistVisualSameSource(session.Document.SourcePath))
            {
                // プロキシ待ちのあいだに進んだプレビュー位置を使う。待ち始めの値へ戻さない。
                startFrame = LibraryPlayerMode.HoverResumeFrame(
                    hoverVideoShown: true,
                    visualPlaying: false,
                    sameClip: true,
                    LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                    session.Document.SampleRate,
                    session.Document.FrameCount);
                session.Document.CursorFrame = startFrame;
                Waveform.PlayheadFrame = startFrame;
            }

            StartPlayback(startFrame, prerollSeconds: 0);
            return;
        }

        await PlayLibrarySessionAsync(session, stopAfterTrack: true, fromStart: fromStart)
            .ConfigureAwait(true);
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

        // プレイヤー中の圧縮／動画はフル Load しない。WAVE / AIFF はメモリ早戻し用に PCM 化する。
        if (IsLibraryMaximized
            && session.Document.SourcePath is { Length: > 0 }
            && AudioCodec.PrefersPlayerStream(session.Document.SourcePath))
        {
            return ActivateStreamLibrarySessionAsync(session, gen);
        }

        // エディタ復帰、またはプレイヤーの非圧縮ストリーム残りはフル PCM に昇格する。
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

    /// <summary>
    /// 破壊編集用に Wave / AIFF の PCM をメモリへ載せる。遅延／ストリームならフル Load。
    /// </summary>
    private Task<bool> EnsureSessionPcmForEditAsync(DocumentSession session)
    {
        if (!AudioCodec.IsUncompressedPcm(session.Document.SourceKind))
        {
            return Task.FromResult(false);
        }

        return EnsureSessionLoadedForExportAsync(session);
    }

    /// <summary>
    /// MP3 書き出し用に PCM をメモリへ載せる。遅延／ストリームならフル Load（圧縮音源含む）。
    /// </summary>
    private async Task<bool> EnsureSessionLoadedForExportAsync(DocumentSession session)
    {
        var document = session.Document;
        if (LibraryPlaylistDocuments.IsPdf(document)
            || LibraryPlaylistDocuments.IsVideo(document))
        {
            return false;
        }

        if (!document.IsDeferredLoad
            && !document.IsStreamPlayback
            && document.Interleaved.Length > 0
            && document.FrameCount > 0)
        {
            return true;
        }

        if (document.SourcePath is not { Length: > 0 } path || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var playhead = session.PlayheadFrame;
            var tags = document.Tags;
            var previousArt = document.Artwork;
            var loaded = await Task.Run(() =>
            {
                var next = AudioCodec.Load(path, buildPeaks: false);
                next.ReplacePeaks(
                    PeakPyramid.BuildDisplay(next.Interleaved, next.Channels, next.SampleCount));
                return next;
            }).ConfigureAwait(true);

            if (!session.Document.IsDeferredLoad
                && !session.Document.IsStreamPlayback
                && session.Document.Interleaved.Length > 0)
            {
                return session.Document.FrameCount > 0;
            }

            loaded.CursorFrame = Math.Clamp(playhead, 0, loaded.FrameCount);
            session.ReplaceDocument(loaded);
            loaded.ApplyTags(tags);
            if (!loaded.HasArtwork && previousArt is { Length: > 0 })
            {
                loaded.SetArtwork(previousArt);
            }

            session.PlayheadFrame = Math.Clamp(playhead, 0, loaded.FrameCount);
            ApplyLoadedLibrarySession(session, bind: ReferenceEquals(session, _activeSession));
            return loaded.FrameCount > 0;
        }
        catch
        {
            return false;
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
        var tilePane = FindTilePane(session);
        if (tilePane is not null)
        {
            view = tilePane.View;
        }
        else if (ReferenceEquals(_activeSession, session)
            && LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(_tileMode, sessionHasTilePane: false))
        {
            // タイル構築中は Primary が Clear 済み。ここに描くと空ビットマップが残り、
            // タブ切替まで波形が出ないことがある。
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

    private void ShowLibraryArtwork(DocumentSession session) =>
        _ = ShowLibraryArtworkAsync(session);

    /// <summary>
    /// ジャケット走査・デコード・ウォッシュ生成は裏スレッド。UI は差し替えだけ。
    /// プレイリスト追加バッチ中は拒否し、完了時の 1 回 Apply に回す。
    /// </summary>
    private async Task ShowLibraryArtworkAsync(DocumentSession session)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        if (LibraryPlayerMode.DefersAmbientWashApply(_libraryFolderShowInProgress))
        {
            QueuePendingLibraryWash(session);
            return;
        }

        var hadArt = session.Document.HasArtwork;
        Task<LibraryBrowserView.PreparedArtworkPresentation> prepare;
        try
        {
            prepare = Task.Run(() =>
            {
                ScanLibraryArtwork(session);
                return LibraryBrowserView.PrepareArtworkPresentation(session.Document.Artwork);
            });
        }
        catch (Exception)
        {
            prepare = Task.FromResult(LibraryBrowserView.PrepareArtworkPresentation(null));
        }

        await ApplyPreparedLibraryArtworkAsync(session, prepare, hadArt).ConfigureAwait(true);
    }

    private async Task ApplyPreparedLibraryArtworkAsync(
        DocumentSession session,
        Task<LibraryBrowserView.PreparedArtworkPresentation>? prepare,
        bool? hadArtBefore = null)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        if (LibraryPlayerMode.DefersAmbientWashApply(_libraryFolderShowInProgress))
        {
            QueuePendingLibraryWash(session, prepare, hadArtBefore);
            return;
        }

        var generation = ++_libraryArtworkShowGeneration;
        var hadArt = hadArtBefore ?? session.Document.HasArtwork;
        var allowReplace = LibraryBrowserView.AllowsJacketReplace(session.Document.SourceKind);
        LibraryBrowserView.PreparedArtworkPresentation prepared;
        try
        {
            prepared = prepare is null
                ? await Task.Run(() =>
                    {
                        ScanLibraryArtwork(session);
                        return LibraryBrowserView.PrepareArtworkPresentation(session.Document.Artwork);
                    }).ConfigureAwait(true)
                : await prepare.ConfigureAwait(true);
        }
        catch (Exception)
        {
            if (generation != _libraryArtworkShowGeneration || !IsLibraryMaximized)
            {
                return;
            }

            ScanLibraryArtwork(session);
            prepared = LibraryBrowserView.PrepareArtworkPresentation(session.Document.Artwork);
        }

        if (generation != _libraryArtworkShowGeneration || !IsLibraryMaximized)
        {
            return;
        }

        _libraryHoldJacketWash = false;
        LibraryBrowser.ApplyPreparedArtwork(prepared, allowReplace, keepCurrentIfEmpty: false);
        if (!hadArt && session.Document.HasArtwork)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private void QueuePendingLibraryWash(
        DocumentSession session,
        Task<LibraryBrowserView.PreparedArtworkPresentation>? prepare = null,
        bool? hadArtBefore = null)
    {
        _pendingLibraryWashSession = LibraryPlayerMode.MergePendingWashSession(
            _pendingLibraryWashSession,
            session);
        _pendingLibraryWashPrepare = prepare;
        _pendingLibraryWashHadArt = hadArtBefore;
    }

    private void ClearPendingLibraryWash()
    {
        _pendingLibraryWashSession = null;
        _pendingLibraryWashPrepare = null;
        _pendingLibraryWashHadArt = null;
    }

    /// <summary>
    /// バッチ中断時も、載っている曲があれば完了時 Apply に乗せる。
    /// </summary>
    private void EnsurePendingLibraryWashFromPlaylist()
    {
        if (_pendingLibraryWashSession is not null)
        {
            return;
        }

        var session = _libraryFolderPlaySession
            ?? LibraryBrowser.SelectedSession
            ?? LibraryPlayerMode.FirstSession(_sessions);
        if (session is not null)
        {
            QueuePendingLibraryWash(session);
        }
    }

    /// <summary>
    /// 追加バッチ終了後に保留していた背景色替えを 1 回だけ載せる。
    /// リスト仕上げ（FinishIncremental）より後の Idle で走らせ、行追加とクロスフェードを重ねない。
    /// </summary>
    private async Task FlushPendingLibraryWashAsync()
    {
        var session = _pendingLibraryWashSession;
        var prepare = _pendingLibraryWashPrepare;
        var hadArt = _pendingLibraryWashHadArt;
        ClearPendingLibraryWash();

        if (!IsLibraryMaximized)
        {
            return;
        }

        if (session is null)
        {
            ReleaseHeldLibraryJacket();
            return;
        }

        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
        if (!IsLibraryMaximized
            || LibraryPlayerMode.DefersAmbientWashApply(_libraryFolderShowInProgress))
        {
            // 別バッチが始まっていたら、そちらの完了フラッシュに任せる。
            if (LibraryPlayerMode.DefersAmbientWashApply(_libraryFolderShowInProgress))
            {
                QueuePendingLibraryWash(session, prepare, hadArt);
            }

            return;
        }

        await ApplyPreparedLibraryArtworkAsync(session, prepare, hadArt).ConfigureAwait(true);
    }

    /// <summary>動的背景は呼び出し側で済ませたあとの、停止中プレビュー（波形など）。</summary>
    private void PreviewIdleLibrarySessionWithoutArtwork(DocumentSession session)
    {
        if (ContinuesLibraryPlaylistPlayback() || !IsLibraryMaximized)
        {
            return;
        }

        if (LibraryPlaylistDocuments.IsVisual(session.Document))
        {
            if (!IsPlaybackActive())
            {
                ActivateVisualLibraryMeta(session);
                LibraryBrowser.ShowPlaylistVisual(session, play: false);
                ApplyLoadedLibrarySession(session);
                EnsureLibraryVisualPlayheadTicker();
                _ = FillLibraryPeaksAsync(session);
            }

            return;
        }

        LibraryBrowser.HidePlaylistVisual();
        if (!IsPlaybackActive())
        {
            _ = ShowLibrarySessionWaveformAsync(session);
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
        RefreshDisplayedPlaylistPosition();
    }

    private void LibraryBrowser_SortChanged(object? sender, LibraryPlaylistSort sort)
    {
        AppStorage.Settings.ApplyLibraryListSort(sort);
        AppStorage.Save();
        RefreshDisplayedPlaylistPosition();
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
        AppStorage.Settings.LibraryShuffle = LibraryBrowser.ShuffleEnabled;
        AppStorage.Save();
        if (IsLibraryMaximized && IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void LibraryBrowser_AutoPlayChanged(object? sender, EventArgs e)
    {
        AppStorage.Settings.LibraryAutoPlay = LibraryBrowser.AutoPlayEnabled;
        AppStorage.Save();
    }

    private void LibraryBrowser_AutoPlayNextChanged(object? sender, EventArgs e)
    {
        AppStorage.Settings.LibraryAutoPlayNext = LibraryBrowser.AutoPlayNextEnabled;
        AppStorage.Save();
        if (!IsLibraryMaximized || !IsPlaybackActive())
        {
            return;
        }

        if (LibraryBrowser.AutoPlayNextEnabled && !LibraryBrowser.AutoLoopEnabled)
        {
            ScheduleLibraryGaplessPrefetch();
        }
        else
        {
            CancelLibraryGapless();
        }
    }

    private void LibraryBrowser_AutoLoopChanged(object? sender, EventArgs e)
    {
        AppStorage.Settings.LibraryAutoLoop = LibraryBrowser.AutoLoopEnabled;
        AppStorage.Save();
        if (AutoLoopCheck.IsChecked != LibraryBrowser.AutoLoopEnabled)
        {
            AutoLoopCheck.IsChecked = LibraryBrowser.AutoLoopEnabled;
        }

        _crossfadeWindow?.SetSingleLoopChecked(LibraryBrowser.AutoLoopEnabled);

        // エディタでもサンプルループ再生ウィンドウを Single Loop に追従させる。
        SyncPlayWindowFromDocument();
        if (!IsLibraryMaximized || !IsPlaybackActive())
        {
            return;
        }

        if (LibraryBrowser.AutoLoopEnabled)
        {
            CancelLibraryGapless();
        }
        else if (LibraryBrowser.AutoPlayNextEnabled)
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
        ScheduleLibraryCatalogPrune();
    }

    /// <summary>今のルートから外れた索引と、消えたファイルの行を裏で捨てる。</summary>
    private void ScheduleLibraryCatalogPrune()
    {
        var roots = LibraryBrowser.ExplorerRootPaths;
        _ = Task.Run(() =>
        {
            try
            {
                LibraryCatalog.PruneUnreferenced(roots);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        });
    }

    private void LibraryBrowser_ClearPlaylistRequested(object? sender, EventArgs e) =>
        RemoveLibrarySelectedFromList();

    private void LibraryBrowser_FavoritesActivated(object? sender, LibraryFavoritesActivateEventArgs e)
    {
        if (e.Paths.Count == 0)
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
            // Enter。読み込み中でも中断して差し替える（TryClear → CancelLibraryFolderShow）。
            _libraryHoldJacketWash = true;
            if (!TryClearLibrarySessions())
            {
                _libraryHoldJacketWash = false;
                return;
            }

            _ = OpenLibraryFoldersRecursiveAsync(
                paths,
                LibraryExplorerPlaylistWalk.Inactive,
                playFirst: LibraryBrowser.AutoPlayEnabled,
                recursive: e.Recursive);
            return;
        }

        // Shift+Enter の追加は進行中の再帰追加に重ねない。
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        _ = OpenLibraryFoldersRecursiveAsync(
            paths,
            LibraryExplorerPlaylistWalk.Inactive,
            playFirst: false,
            recursive: e.Recursive);
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

        _ = OpenLibraryFoldersRecursiveAsync(
            existing,
            playFirst: false,
            recursive: LibraryBrowser.ExplorerOpenRecursive);
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

        await OpenLibraryFoldersRecursiveAsync(
            existing,
            playFirst: false,
            recursive: LibraryBrowser.ExplorerOpenRecursive).ConfigureAwait(true);
    }

    /// <summary>
    /// ツリーの Enter／Ctrl+Enter（クリア系）。
    /// プレイリストを空にしてから載せる。再帰は <see cref="LibraryBrowserView.ExplorerOpenRecursive"/>。
    /// 追加の最中でも、別フォルダへのクリア追加で進行中を中断して差し替える。
    /// </summary>
    private void ReplaceLibraryFromExplorerFolder()
    {
        // 欠損（赤字）ルートの Enter はスルー。クリアだけ走ると空リストなのにメーターが出る。
        var folders = LibraryShellFiles.ExistingPaths(LibraryBrowser.SelectedExplorerFolders);
        if (folders.Length == 0)
        {
            return;
        }

        // TryClear → CancelLibraryFolderShow で世代を進め、進行中の追加を破棄する。
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
            playFirst: LibraryBrowser.AutoPlayEnabled,
            recursive: LibraryBrowser.ExplorerOpenRecursive,
            focusPlaylist: true);
    }

    /// <summary>
    /// フォルダ配下（と単体ファイル）を収集し、1 曲ずつ載せる。
    /// 再生するのはクリアして追加かつ Auto Play オンのときだけ。
    /// Shift 付き追加は再生も停止もしない。
    /// ツリーの Enter（クリア追加）は Auto Play のオン／オフに関わらず、載せ終わったあとプレイリストへフォーカスする。
    /// クリア追加の再生は先頭が音声なら見つけ次第、動画／PDF は全件追加後。
    /// 検索中のツリーは見えているフォルダだけ。ファイル名ヒットはそのファイル、フォルダ名ヒットは配下すべて。
    /// お気に入りは検索フィルタを掛けない。
    /// </summary>
    private async Task OpenLibraryFoldersRecursiveAsync(
        IReadOnlyList<string> folders,
        LibraryExplorerPlaylistWalk? walk = null,
        bool playFirst = false,
        bool recursive = true,
        bool placeByCurrentSort = false,
        bool focusPlaylist = false)
    {
        LibraryPlaylistDocuments.ApplyFromSettings(AppStorage.Settings);
        var generation = ++_libraryFolderShowGeneration;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = true;
        ClearPendingLibraryWash();
        // 前の追加が残した進捗表示を消す（世代不一致の finally では消さないため）。
        ClearOpenStatus();
        var filter = walk ?? LibraryBrowser.SnapshotExplorerPlaylistWalk();
        var roots = new List<(string Path, bool AncestorHit)>(folders.Count);
        for (var i = 0; i < folders.Count; i++)
        {
            if (PathUtil.TryGetFullPath(folders[i], out var full))
            {
                roots.Add((full, false));
            }
        }

        if (roots.Count == 0)
        {
            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                _libraryFolderShowInProgress = false;
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }

            return;
        }

        _libraryPlayFirstPending = false;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is { Length: > 0 } path)
            {
                known.Add(path);
            }
        }

        var channel = Channel.CreateUnbounded<LibraryFolderProduceItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
        var produce = Task.Run(() =>
        {
            try
            {
                ProduceLibraryFolderSessions(
                    roots,
                    filter,
                    known,
                    channel.Writer,
                    () => generation != _libraryFolderShowGeneration,
                    recursive);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        var firstHandled = false;
        var folderPlayStarted = false;
        var playlistFocusTicket = 0;
        var playlistWasEmpty = _sessions.Count == 0;
        var loaded = 0;
        var planned = 0;
        var showProgress = false;
        var pending = new List<DocumentSession>();
        var flushClock = Stopwatch.StartNew();

        bool Alive() => generation == _libraryFolderShowGeneration && IsLibraryMaximized;

        void NotePlanned(int count)
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

        bool FlushPending()
        {
            if (pending.Count == 0)
            {
                return true;
            }

            if (!Alive())
            {
                return false;
            }

            var first = !firstHandled;
            var selectFirst = first
                && ShouldSelectAppendedLibrarySession(
                    playFirst,
                    firstInBatch: true,
                    playlistWasEmpty);
            var head = pending[0];
            if (placeByCurrentSort)
            {
                foreach (var session in pending)
                {
                    PlacePlaylistSession(session, selectFirst && ReferenceEquals(session, head));
                }
            }
            else
            {
                foreach (var session in pending)
                {
                    _sessions.Add(session);
                }
            }

            if (selectFirst)
            {
                ScanLibraryArtwork(head);
            }

            if (!placeByCurrentSort)
            {
                LibraryBrowser.AppendSessions(pending, selectFirst ? head : null);
            }
            SyncPlayerMeterFade();
            loaded += pending.Count;
            if (showProgress)
            {
                var name = Path.GetFileName(pending[^1].Document.SourcePath ?? string.Empty);
                SetOpenStatus(loaded, Math.Max(planned, loaded), name);
            }

            if (first && playFirst)
            {
                _libraryFolderPlaySession = head;
                if (ShouldPlayFolderFirstImmediately(head.Document.SourcePath))
                {
                    folderPlayStarted = true;
                    // 動的背景は全件載せ終わってから差し替える。
                    _ = PlayLibrarySessionAsync(head, keepAmbientGlow: true);
                    if (Alive())
                    {
                        playlistFocusTicket = LibraryBrowser.RequestListFocus();
                    }
                }
            }
            else if (playFirst && folderPlayStarted && IsPlaybackActive())
            {
                ScheduleLibraryGaplessPrefetch();
            }

            firstHandled = true;
            pending.Clear();
            flushClock.Restart();
            return Alive();
        }

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync().ConfigureAwait(true))
            {
                if (!Alive())
                {
                    if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                            generation,
                            _libraryFolderShowGeneration))
                    {
                        LibraryBrowser.FinishIncrementalSessionLoad();
                        EnsurePendingLibraryWashFromPlaylist();
                    }

                    return;
                }

                if (item.PlanDelta > 0)
                {
                    NotePlanned(item.PlanDelta);
                    continue;
                }

                if (item.Session is null)
                {
                    continue;
                }

                pending.Add(item.Session);
                var flush = item.FlushNow
                    || (!firstHandled && playFirst)
                    || pending.Count >= LibraryPlayerMode.PlaylistLoadUiFlushCount
                    || flushClock.ElapsedMilliseconds >= LibraryPlayerMode.PlaylistLoadUiFlushMs;
                if (!flush)
                {
                    continue;
                }

                if (!FlushPending())
                {
                    if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                            generation,
                            _libraryFolderShowGeneration))
                    {
                        LibraryBrowser.FinishIncrementalSessionLoad();
                        EnsurePendingLibraryWashFromPlaylist();
                    }

                    return;
                }
            }

            if (!FlushPending())
            {
                if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                        generation,
                        _libraryFolderShowGeneration))
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    EnsurePendingLibraryWashFromPlaylist();
                }

                return;
            }

            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration)
                && IsLibraryMaximized)
            {
                // 列・波形の仕上げと並行してウォッシュを裏で作り、バッチ終了後に 1 回だけ Apply。
                Task<LibraryBrowserView.PreparedArtworkPresentation>? artPrepare = null;
                DocumentSession? artSession = null;
                var artHadArt = false;
                if (firstHandled && playFirst && _libraryFolderPlaySession is { } playHead)
                {
                    artSession = playHead;
                    artHadArt = playHead.Document.HasArtwork;
                    artPrepare = Task.Run(() =>
                    {
                        ScanLibraryArtwork(playHead);
                        return LibraryBrowserView.PrepareArtworkPresentation(playHead.Document.Artwork);
                    });
                }
                else if (firstHandled && !playFirst && LibraryBrowser.SelectedSession is { } selectedIdle)
                {
                    artSession = selectedIdle;
                    artHadArt = selectedIdle.Document.HasArtwork;
                    artPrepare = Task.Run(() =>
                    {
                        ScanLibraryArtwork(selectedIdle);
                        return LibraryBrowserView.PrepareArtworkPresentation(selectedIdle.Document.Artwork);
                    });
                }

                LibraryBrowser.FinishIncrementalSessionLoad();
                if (firstHandled)
                {
                    if (playFirst && _libraryFolderPlaySession is { } first)
                    {
                        var selected = LibraryBrowser.SelectedSession;
                        if (ShouldRestoreFolderPlaySelection(selected, first))
                        {
                            LibraryBrowser.SelectSessionQuiet(first);
                        }

                        QueuePendingLibraryWash(first, artPrepare, artHadArt);
                        if (!folderPlayStarted)
                        {
                            // 再生開始はウォッシュ準備と並行。色替えはバッチ終了 Flush で Apply。
                            var playTask = PlayLibrarySessionAsync(first, keepAmbientGlow: true);
                            await playTask.ConfigureAwait(true);
                            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                    generation,
                                    _libraryFolderShowGeneration)
                                && IsLibraryMaximized)
                            {
                                LibraryBrowser.RequestListFocus();
                            }
                        }
                        else
                        {
                            if (IsPlaybackActive())
                            {
                                ScheduleLibraryGaplessPrefetch();
                            }

                            // 再生開始直後に移しても、続きの行追加でツリーへ戻る。
                            // 仕上げのあと、そのあいだにツリーへ残す操作が無ければもう一度移す。
                            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                    generation,
                                    _libraryFolderShowGeneration)
                                && IsLibraryMaximized)
                            {
                                LibraryBrowser.RequestListFocusIfCurrent(playlistFocusTicket);
                            }
                        }
                    }
                    else if (!playFirst)
                    {
                        if (artSession is not null)
                        {
                            QueuePendingLibraryWash(artSession, artPrepare, artHadArt);
                            PreviewIdleLibrarySessionWithoutArtwork(artSession);
                        }
                        else
                        {
                            PreviewIdleLibrarySession(LibraryBrowser.SelectedSession);
                        }

                        // 再生は Auto Play オンのときだけ。フォーカスはツリーの Enter なら常にプレイリストへ。
                        if (focusPlaylist
                            && LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                generation,
                                _libraryFolderShowGeneration)
                            && IsLibraryMaximized)
                        {
                            LibraryBrowser.RequestListFocus();
                        }
                    }
                }
                else
                {
                    EnsurePendingLibraryWashFromPlaylist();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                EnsurePendingLibraryWashFromPlaylist();
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            try
            {
                await produce.ConfigureAwait(true);
            }
            catch (Exception)
            {
            }

            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                _libraryFolderPlaySession = null;
                _libraryFolderShowInProgress = false;
                if (showProgress)
                {
                    ClearOpenStatus();
                }

                // キャンセルで途中終了しても、載っている曲があればここで 1 回 Apply に乗る。
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// フォルダ走査とタグ読みはすべて裏スレッド。UI へは完成したセッションだけ流す。
    /// </summary>
    private static void ProduceLibraryFolderSessions(
        IReadOnlyList<(string Path, bool AncestorHit)> roots,
        LibraryExplorerPlaylistWalk filter,
        HashSet<string> knownPaths,
        ChannelWriter<LibraryFolderProduceItem> writer,
        Func<bool> cancelled,
        bool recursive = true)
    {
        var remaining = new Stack<(string Path, bool AncestorHit)>(roots.Count);
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            remaining.Push(roots[i]);
        }

        while (remaining.Count > 0)
        {
            if (cancelled())
            {
                return;
            }

            var current = remaining.Pop();
            if (!Directory.Exists(current.Path))
            {
                if (File.Exists(current.Path)
                    && AudioCodec.IsPlayerOpenable(current.Path)
                    && filter.IncludeFile(current.Path, current.AncestorHit)
                    && knownPaths.Add(current.Path))
                {
                    writer.TryWrite(LibraryFolderProduceItem.Plan(1));
                    try
                    {
                        var session = CreateLibrarySession(current.Path);
                        writer.TryWrite(LibraryFolderProduceItem.FromSession(session, flushNow: true));
                    }
                    catch (Exception)
                    {
                    }
                }

                continue;
            }

            string[] files;
            (string Path, bool AncestorHit)[] children;
            try
            {
                var folderHit = filter.FolderNameHit(current.Path, current.AncestorHit);
                AudioCodec.CollectPlayerOpenableDirectoryLayer(current.Path, out var layerFiles, out var layerChildren);
                var included = new List<string>();
                foreach (var file in layerFiles)
                {
                    if (filter.IncludeFile(file, folderHit) && knownPaths.Add(file))
                    {
                        included.Add(file);
                    }
                }

                var next = new List<(string Path, bool AncestorHit)>();
                foreach (var child in layerChildren)
                {
                    if (filter.IncludeChild(child, folderHit))
                    {
                        next.Add((child, folderHit));
                    }
                }

                files = included.ToArray();
                children = next.ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (files.Length > 0)
            {
                writer.TryWrite(LibraryFolderProduceItem.Plan(files.Length));
            }

            // キャッシュが揃っていれば層ごと作って一気に流す。欠けがあるときは1曲ずつ。
            if (files.Length > 1 && LibraryPlaylistCache.AreAllWarm(files))
            {
                for (var i = 0; i < files.Length; i++)
                {
                    if (cancelled())
                    {
                        return;
                    }

                    try
                    {
                        var session = CreateLibrarySession(files[i]);
                        writer.TryWrite(
                            LibraryFolderProduceItem.FromSession(session, flushNow: i + 1 == files.Length));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
            {
                for (var i = 0; i < files.Length; i++)
                {
                    if (cancelled())
                    {
                        return;
                    }

                    try
                    {
                        var session = CreateLibrarySession(files[i]);
                        writer.TryWrite(LibraryFolderProduceItem.FromSession(session, flushNow: false));
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (!recursive)
            {
                continue;
            }

            for (var i = children.Length - 1; i >= 0; i--)
            {
                remaining.Push(children[i]);
            }
        }
    }

    private readonly struct LibraryFolderProduceItem
    {
        private LibraryFolderProduceItem(DocumentSession? session, int planDelta, bool flushNow)
        {
            Session = session;
            PlanDelta = planDelta;
            FlushNow = flushNow;
        }

        public DocumentSession? Session { get; }

        public int PlanDelta { get; }

        public bool FlushNow { get; }

        public static LibraryFolderProduceItem Plan(int count) => new(null, count, false);

        public static LibraryFolderProduceItem FromSession(DocumentSession session, bool flushNow) =>
            new(session, 0, flushNow);
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
    /// Enter の先頭再生を全件追加前に始めてよいか。
    /// 動画／PDF は Open と後続追加が競合するので待つ。
    /// </summary>
    internal static bool ShouldPlayFolderFirstImmediately(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !LibraryPlaylistDocuments.IsDocument(path);

    /// <summary>
    /// 追加した曲をリストで選ぶか。Enter 再生は先頭のみ。Shift+Enter は空リストへ足したときだけ 1 行目を選ぶ。
    /// </summary>
    internal static bool ShouldSelectAppendedLibrarySession(
        bool playFirst,
        bool firstInBatch,
        bool playlistWasEmpty) =>
        firstInBatch && (playFirst || playlistWasEmpty);

    /// <summary>F5。フォルダツリーとプレイリスト行を読み直す。索引の再構築はしない。</summary>
    private void RefreshLibraryTree()
    {
        var playlistFocused = LibraryBrowser.IsListKeyboardFocused;
        LibraryBrowser.RefreshExplorer();
        RefreshPlaylistList();
        if (playlistFocused)
        {
            LibraryBrowser.FocusList();
            return;
        }

        LibraryBrowser.FocusExplorer();
    }

    /// <summary>開いているセッションからプレイリスト行と波形棒を作り直す。</summary>
    private void RefreshPlaylistList()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        ProbeLibraryTags();
        LibraryBrowser.SetSessions(
            _sessions,
            _activeSession,
            LibraryBrowser.SelectedSessions,
            rebuild: true);
        RefreshDisplayedPlaylistPosition();
    }

    private void RefreshDisplayedPlaylistPosition()
    {
        RefreshPlaylistTrackIndex();
        RefreshStatus();
    }

    /// <summary>Ctrl+F5。ツリーを読み直し、索引を再構築する。</summary>
    private void RebuildLibraryCatalog()
    {
        RefreshLibraryTree();
        _ = WarmupLibraryCatalogAsync();
    }

    /// <summary>
    /// Ctrl+F5。Explorer ルート配下をタグ／波形／ジャケットまで温める。すりガラスで操作を止める。
    /// </summary>
    private async Task WarmupLibraryCatalogAsync()
    {
        if (IsUiBusy && !_libraryCatalogWarmupBusy)
        {
            return;
        }

        var roots = LibraryBrowser.ExplorerRootPaths;
        if (roots.Length == 0)
        {
            return;
        }

        _libraryCatalogWarmupCts?.Cancel();
        try
        {
            _libraryCatalogWarmupCts?.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }

        var generation = ++_libraryCatalogWarmupGeneration;
        var cts = new CancellationTokenSource();
        _libraryCatalogWarmupCts = cts;
        var token = cts.Token;
        _libraryCatalogWarmupBusy = true;
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        ShowBusyGlass(UiStrings.OverlayLibraryCatalog);
        var progress = new Progress<double>(p =>
        {
            if (generation == _libraryCatalogWarmupGeneration)
            {
                _busyGlass.SetProgress(p);
            }
        });
        try
        {
            await Task.Run(() => LibraryCatalogWarmup.Run(roots, progress, token), token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            cts.Dispose();
            if (generation == _libraryCatalogWarmupGeneration)
            {
                _libraryCatalogWarmupBusy = false;
                _libraryCatalogWarmupCts = null;
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }
            }
        }
    }

    /// <summary>
    /// タグ・表示済みジャケット／波形キャッシュを載せてセッションを作る。
    /// </summary>
    private static DocumentSession CreateLibrarySession(string file)
    {
        var document = AudioDocument.CreateDeferred(file);
        AudioTagProbe.Ensure(document);
        if (!document.HasArtwork && LibraryMediaCache.TryGetArtwork(file, out var art))
        {
            document.SetArtwork(art);
        }

        // 一度出した波形列があればメモリへ戻し、追加直後の再デコードを避ける。
        _ = LibraryPlaylistWaveform.HasEntry(file, LibraryPlaylistWaveform.BarCount);
        return new DocumentSession(document);
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
        _libraryArtworkShowGeneration++;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = false;
        // 差し替え側のバッチが新しい保留を積む。旧バッチの Flush は世代不一致で走らない。
        ClearPendingLibraryWash();
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

    private void StartLibraryVisualPlayback(
        DocumentSession session,
        bool stopAfterTrack,
        bool fromStart = false)
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
        var startFrame = fromStart
            ? 0L
            : LibraryPlayerMode.HoverResumeFrame(
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

    /// <summary>
    /// 本再生直前。プロキシ不要／作成不可なら true のまま通し、必要なら Ensure する。
    /// </summary>
    private async Task<bool> EnsureVideoProxyBeforePlaybackAsync(DocumentSession session)
    {
        if (!LibraryPlaylistDocuments.IsVideo(session.Document)
            || CanShowPlaylistVideo(session.Document))
        {
            return true;
        }

        return await TryPrepareVideoProxyAsync(session, playTicket: -1).ConfigureAwait(true);
    }

    private async Task<bool> TryPrepareVideoProxyAsync(
        DocumentSession session,
        int playTicket = -1,
        bool forceEncode = false)
    {
        if (!LibraryPlaylistDocuments.IsVideo(session.Document)
            || session.Document.SourcePath is not { Length: > 0 } path)
        {
            return false;
        }

        // 強制は既存プロキシがあっても作り直す（同名同サイズの誤共有の直し用）。
        var needsEncode = forceEncode
            ? !VideoCodecProbe.CanPlayWithoutProxy(path) && VideoProxy.CanForceEncodeProxy()
            : !CanShowPlaylistVideo(session.Document) && VideoProxy.CanEncodeProxy();
        // 自分のプロキシ生成中は差し替え可。別種の Busy だけ拒否する。
        if (needsEncode && IsUiBusy && !_videoProxyBusy)
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
            // プロキシ完了後は再生チケットだけ見る。待ちのあいだに別ロードが
            // _libraryLoadSession を動かしても、この Enter の本再生は続ける。
            if (!IsLoaded
                || token.IsCancellationRequested
                || (playTicket >= 0 && playTicket != _libraryPlayTicket))
            {
                return false;
            }

            _libraryLoadSession = session;
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
            // 自分が始めた生成のときだけ Busy を下ろす（差し替え後の新しい生成は残す）。
            if (needsEncode && (_videoProxyCts is null || token == _videoProxyCts.Token))
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
    /// プレイリスト選択・本再生・ミニプレイヤーの動画が対象。すでにあっても作り直す。
    /// プロキシ不要（MJPEG 等）／ffmpeg 無しは何もしない。
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
            || VideoCodecProbe.CanPlayWithoutProxy(path)
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

        if (!await TryPrepareVideoProxyAsync(session, playTicket: -1, forceEncode: true)
                .ConfigureAwait(true))
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
        RefreshPlaylistTrackIndex();
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
            || !IsLibraryMaximized
            || !LibraryBrowser.PlaylistVisualPlaying
            || !LibraryBrowser.PlaylistVisualIsVideo)
        {
            return false;
        }

        long startFrame;
        if (!_document.Selection.IsEmpty)
        {
            startFrame = _document.Selection.StartFrame;
        }
        else if (LibraryBrowser.AutoLoopEnabled)
        {
            startFrame = 0;
        }
        else
        {
            return false;
        }

        LibraryBrowser.SeekPlaylistVisual(
            TimeSpan.FromSeconds(startFrame / (double)Math.Max(1, _document.SampleRate)));
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
