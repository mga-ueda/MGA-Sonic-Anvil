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

    /// <summary>
    /// 直近に受け付けた本再生リクエスト（同一セッション・同一パラメータ）。
    /// ダブルクリックは「選択→SessionActivated」と「ダブルクリック→SessionPlayRequested」の
    /// 2 連（まれに 3 連）で同じ曲へ PlayLibrarySessionAsync が走る。プレイヤーは次曲を先読み
    /// 済みで EnsureLibrarySessionLoadedAsync が同期完了するため _libraryPlayTicket では潰せず、
    /// 新しい曲の頭が 2〜3 回鳴る（「ジャジャジャーン」）。ダブルクリック間隔内の同一要求は畳む。
    /// </summary>
    private DocumentSession? _lastLibraryPlayRequestSession;
    private bool _lastLibraryPlayRequestStopAfterTrack;
    private bool _lastLibraryPlayRequestKeepAmbientGlow;
    private bool _lastLibraryPlayRequestFromStart;
    private long _lastLibraryPlayRequestTicks;

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
    /// <summary>
    /// テンキー 5／4／6 やカーソルの曲送りを合流待ち（<see cref="LibraryPlayerMode.InteractivePlayCoalesceMs"/>）
    /// してから 1 回だけ本再生するためのタイマーと予約内容。連打で曲頭が重なるのを防ぐ。
    /// </summary>
    private DispatcherTimer? _interactivePlayTimer;
    private DocumentSession? _interactivePlaySession;
    private bool _interactivePlayStopAfterTrack;
    private bool _interactivePlayKeepAmbientGlow;
    private bool _interactivePlayFromStart;
    private int _interactivePlayGeneration;
    private int _interactivePlayScheduledGeneration;
    /// <summary>Shift+↑↓ 連打中はメーター平均を毎打で再計算せず、離したときにまとめる。</summary>
    private bool _playlistSelectionLoudnessDeferred;
    private int _playlistSelectionLoudnessDeferTicket;
    /// <summary>
    /// Shift 範囲のキーリピートを Render へ溜める。
    /// Input で毎打処理すると Background のスペアナ／レベルが止まる。
    /// </summary>
    private int _playlistExtendPendingDelta;
    private int _playlistExtendPendingGroupSteps;
    private bool _playlistExtendFlushQueued;
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
    /// <summary>
    /// エディタ復帰中。波形／再生を先に通すため SyncVstInsertEngine を一時抑止する。
    /// </summary>
    private bool _deferVstInsertSyncForEditorEnter;

    /// <summary>
    /// Leave 開始時点のモード名トースト世代。これより進んだら「この復帰のトースト」。
    /// </summary>
    private int _editorEnterToastEpochBaseline;

    /// <summary>
    /// プレイヤー離脱後に出すモード名トースト。ApplyLayoutPreset が予約し、Leave の初回レイアウト後に出す。
    /// </summary>
    private WindowLayoutSlot? _pendingLayoutModeToastSlot;

    private int _editorEnterBackgroundTicket;
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

    /// <summary>プレイヤーで波形をアナライザ行（ラウドネス左）へドックしているか。</summary>
    private bool _playerWaveformDockedBesideLoudness;
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


    private void ApplyLibraryChrome()
    {
        var show = IsLibraryMaximized;
        ApplyStatusFieldChrome();
        SyncEditorOnlyStatusLatches();
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
        SyncDocumentTabHostVisibility();
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
        SyncHistoryStripVisibility();

        RefreshTransportCommandsEnabled();
        ApplyLibraryWaapi(show);
        if (show)
        {
            RestoreParkedPlaylist();
        }

        ApplyLibraryPlayerHost(show);
        if (show)
        {
            // プレイヤー入りはすぐアンロード。退出時の後追い復元はキャンセル。
            _editorEnterBackgroundTicket++;
            _layoutModeToastUntilUtc = null;
            SyncVstInsertEngine();
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
            // チェック選択はプレイヤー専用。設定には覚えず、退出でオフへ戻す（トーストは出さない）。
            LibraryBrowser.SetCheckSelectMode(false, raise: false);
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
                _playlistVideoImmersivePause,
                simpleSeekScrubHold: IsSimpleSeekChromeHoldActive());
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
            TransportToolbarHost.Background = Brushes.Transparent;
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
            TransportToolbarHost.SetResourceReference(Border.BackgroundProperty, "TransportBackBrush");
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
        SetOptionLatchWashThrough(show);

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
    /// <summary>
    /// プレイヤーでラウドネス（アナライザ帯）表示中は、波形をその左へドックする。
    /// アナライザ非表示やエディタは下段帯（または非表示）のまま。
    /// </summary>
    private bool ShouldDockPlayerWaveformBesideLoudness()
    {
        if (_activeLayoutSlot is { } slot)
        {
            var preset = AppStorage.Settings.ResolvedLayoutPreset(slot);
            if (!preset.ResolvedShowWaveform(slot) || !preset.ShowAnalyzerArea)
            {
                return false;
            }
        }

        return LibraryPlayerMode.DocksPlayerWaveformBesideLoudness(
            libraryMaximized: IsLibraryMaximized,
            waveformHostVisible: WaveformHostBorder.Visibility == Visibility.Visible,
            analyzerAreaVisible: TransportChromeHost.Visibility == Visibility.Visible);
    }

    private void ApplyPlayerWaveformDockBesideLoudness()
    {
        var dock = ShouldDockPlayerWaveformBesideLoudness();
        if (dock == _playerWaveformDockedBesideLoudness
            && (!dock || ReferenceEquals(WaveformTileHost.Parent, PlayerWaveformSlot)))
        {
            if (dock)
            {
                CollapseLibraryWaveformRowForDock();
            }

            return;
        }

        if (dock)
        {
            DockWaveformTileHostBesideLoudness();
        }
        else
        {
            UndockWaveformTileHostFromLoudness();
        }

        _playerWaveformDockedBesideLoudness = dock;
        QueueVideoHudFrostLayout();
        ScheduleLibraryWaveformPaint();
        SchedulePlaylistVideoFileNamePlacement();
    }

    private void DockWaveformTileHostBesideLoudness()
    {
        if (!ReferenceEquals(WaveformTileHost.Parent, PlayerWaveformSlot))
        {
            if (WaveformTileHost.Parent is Panel oldPanel)
            {
                oldPanel.Children.Remove(WaveformTileHost);
            }
            else if (WaveformTileHost.Parent is Decorator oldDecorator)
            {
                oldDecorator.Child = null;
            }

            PlayerWaveformSlot.Child = WaveformTileHost;
        }

        PlayerWaveformSlot.Visibility = Visibility.Visible;
        SyncEditorPositionTimecodeVisibility();
        WaveformTileHost.HorizontalAlignment = HorizontalAlignment.Stretch;
        WaveformTileHost.VerticalAlignment = VerticalAlignment.Stretch;
        CollapseLibraryWaveformRowForDock();
        // アナライザ帯の高さに波形を合わせ、プレイリスト／映像側の縦を広げる。
        if (IsLibraryMaximized)
        {
            WaveformHostBorder.MinHeight = DesignMetrics.LibraryPaneMinHeight;
        }
    }

    private void UndockWaveformTileHostFromLoudness()
    {
        if (ReferenceEquals(WaveformTileHost.Parent, PlayerWaveformSlot))
        {
            PlayerWaveformSlot.Child = null;
            if (!LibraryWaveformDock.Children.Contains(WaveformTileHost))
            {
                LibraryWaveformDock.Children.Add(WaveformTileHost);
            }
        }

        PlayerWaveformSlot.Visibility = Visibility.Collapsed;
        SyncEditorPositionTimecodeVisibility();
        WaveformTileHost.ClearValue(FrameworkElement.HorizontalAlignmentProperty);
        WaveformTileHost.ClearValue(FrameworkElement.VerticalAlignmentProperty);

        if (!IsLibraryMaximized)
        {
            return;
        }

        // ApplyLibraryChrome / ApplyLayoutWaveformVisibility の行高を復元。
        if (_activeLayoutSlot is { } slot
            && !AppStorage.Settings.ResolvedLayoutPreset(slot).ResolvedShowWaveform(slot))
        {
            LibraryWaveformRowDef.MinHeight = 0;
            LibraryWaveformRowDef.MaxHeight = 0;
            LibraryWaveformRowDef.Height = new GridLength(0);
            WaveformHostBorder.MinHeight = DesignMetrics.LibraryPaneMinHeight;
            return;
        }

        LibraryWaveformRowDef.MinHeight = DesignMetrics.LibraryWaveformHeight;
        LibraryWaveformRowDef.MaxHeight = DesignMetrics.LibraryWaveformHeight;
        LibraryWaveformRowDef.Height = new GridLength(DesignMetrics.LibraryWaveformHeight);
        WaveformHostBorder.MinHeight =
            DesignMetrics.LibraryPaneMinHeight + DesignMetrics.LibraryWaveformHeight;
    }

    private void CollapseLibraryWaveformRowForDock()
    {
        LibraryWaveformRowDef.MinHeight = 0;
        LibraryWaveformRowDef.MaxHeight = 0;
        LibraryWaveformRowDef.Height = new GridLength(0);
    }

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
    /// 優先: アクティブ PCM → 波形表示 →（再生可能）→ 他タブ昇格／プラグイン復元は後追い。
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

        _deferVstInsertSyncForEditorEnter = true;
        // ApplyLayoutPreset の ShowLayoutModeToast より前に記録する（後で世代が進んだか見る）。
        _editorEnterToastEpochBaseline = _layoutModeToastEpoch;
        try
        {
            // レイアウトと波形の初回描画を、フルデコードより先に通す。
            // この Loaded より前に ApplyLayoutPreset が窓位置まで終え、pending トーストを予約する。
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
            if (IsLibraryMaximized)
            {
                return;
            }

            // F11 非全画面など、窓復元後の寸法が効いてからモード名を出す。
            FlushPendingLayoutModeToast();

            // 音が出るために必要なアクティブだけ先にフル PCM へ。
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
            UpgradeLibraryPeaksForEditor();

            // タイル構築やウィンドウ寸法の復元よりピーク完了が先だと、空の Primary へ描いて終わることがある。
            // アクティブを載せ直してから、レイアウト確定後にもう一度描く。
            RebindActiveWaveformAfterLibraryLeave();
            ForEachWaveform(view => view.Refresh());
            Overview.Refresh();
            ScheduleEditorWaveformPaint();

            // 波形描画を通し、スペース等で再生できる状態を先に作る。
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            if (IsLibraryMaximized)
            {
                return;
            }

            // ラック表示用のメタだけ先に載せる（エンジン load はしない）。
            foreach (var session in _sessions)
            {
                ApplyDefaultVstInserts(session);
            }
        }
        finally
        {
            // プレイヤーへ戻った／中断したときだけここで解除。
            // エディタ復帰は後追い側で、モード名トースト後に解除する。
            if (IsLibraryMaximized)
            {
                _pendingLayoutModeToastSlot = null;
                _deferVstInsertSyncForEditorEnter = false;
            }
        }

        if (!IsLibraryMaximized)
        {
            // 他タブ PCM とプラグイン実体は再生を妨げないよう後ろへ。
            ScheduleEditorEnterBackgroundWork(active);
        }
        else
        {
            _deferVstInsertSyncForEditorEnter = false;
        }
    }

    /// <summary>
    /// エディタ復帰後の後追い仕事。入力／再生を優先し、プラグイン wet は遅れてもよい。
    /// </summary>
    private void ScheduleEditorEnterBackgroundWork(DocumentSession? activeWhenLeft)
    {
        var ticket = ++_editorEnterBackgroundTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _ = RunEditorEnterBackgroundWorkAsync(ticket, activeWhenLeft);
        });
    }

    private async Task RunEditorEnterBackgroundWorkAsync(int ticket, DocumentSession? activeWhenLeft)
    {
        try
        {
            if (ticket != _editorEnterBackgroundTicket || IsLibraryMaximized)
            {
                return;
            }

            // 再生開始のキー入力を先に捌く。
            await Dispatcher.Yield(DispatcherPriority.Input);
            if (ticket != _editorEnterBackgroundTicket || IsLibraryMaximized)
            {
                return;
            }

            await UpgradeKeptLibrarySessionsForEditorAsync(activeWhenLeft).ConfigureAwait(true);
            if (ticket != _editorEnterBackgroundTicket || IsLibraryMaximized)
            {
                return;
            }

            // モード名トーストが消えてからすりガラス／プラグイン読み込み。
            // ApplyLayoutPreset はトーストより先に LeaveLibraryMaximize を始めるので、
            // 「まだ出ていない」うちに待ちを終えないこと。
            await WaitForLayoutModeToastToSettleAsync().ConfigureAwait(true);
            if (ticket != _editorEnterBackgroundTicket || IsLibraryMaximized)
            {
                return;
            }

            var needsPluginLoad = EditorEnterNeedsVstInsertLoad();
            if (!needsPluginLoad)
            {
                // トースト後に初めて Sync を許可する。
                _deferVstInsertSyncForEditorEnter = false;
                await SyncVstInsertEngineAsync(openEditorSlot: null, preferPlayback: true)
                    .ConfigureAwait(true);
                return;
            }

            // defer 解除より先に busy を立て、隙間の fire-and-forget Sync を防ぐ。
            _vstEditorEnterBusy = true;
            _vstSyncQueuedDuringEditorBusy = false;
            _deferVstInsertSyncForEditorEnter = false;
            RefreshTransportCommandsEnabled();
            ShowBusyGlass(UiStrings.OverlayVstPluginStartupLoading);
            await Dispatcher.Yield(DispatcherPriority.Render);
            try
            {
                await SyncVstInsertEngineAsync(openEditorSlot: null, preferPlayback: false)
                    .ConfigureAwait(true);
            }
            finally
            {
                _vstEditorEnterBusy = false;
                RefreshTransportCommandsEnabled();
                _busyGlass.BeginFadeOut();
                // ネイティブプラグイン窓がフォーカスを奪ったままになり、Space 再生が効かなくなるのを防ぐ。
                RestoreEditorKeyboardFocusAfterBusy();
                FlushVstSyncQueuedDuringEditorBusy();
            }
        }
        finally
        {
            _deferVstInsertSyncForEditorEnter = false;
        }
    }

    /// <summary>
    /// F11／F12 のモード名トーストが出て消え終わるまで待つ。
    /// 世代が進んでから閉じるまで待つ（前回期限残りでの即抜けを防ぐ）。
    /// </summary>
    private async Task WaitForLayoutModeToastToSettleAsync()
    {
        var needEpoch = _editorEnterToastEpochBaseline + 1;
        var appearDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(2.5);

        // 1) pending 予約またはこの復帰用 ShowLayoutModeToast を待つ。来なければトースト無しで進む。
        while (DateTime.UtcNow < appearDeadline
            && (_pendingLayoutModeToastSlot is not null || _layoutModeToastEpoch < needEpoch))
        {
            if (IsLibraryMaximized)
            {
                return;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }

        if (_layoutModeToastEpoch < needEpoch)
        {
            _layoutModeToastUntilUtc = null;
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            return;
        }

        // 2) すでに消えていれば（PCM 昇格が長かった等）そのまま進む。
        if (!_centerStatusToast.IsOpen
            && _layoutModeToastUntilUtc is { } done
            && done <= DateTime.UtcNow)
        {
            _layoutModeToastUntilUtc = null;
            await Dispatcher.Yield(DispatcherPriority.Render);
            await Dispatcher.Yield(DispatcherPriority.ContextIdle);
            return;
        }

        _layoutModeToastUntilUtc ??= DateTime.UtcNow
            + LibraryExplorerTypeahead.ToastIdleClear
            + TimeSpan.FromSeconds(LibraryPlayerMode.ChromeFadeSeconds);

        var endAt = _layoutModeToastUntilUtc.Value;
        var hardDeadline = endAt + TimeSpan.FromSeconds(1.0);
        var sawOpen = _centerStatusToast.IsOpen;
        var openDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.0);

        // 3) 開いているあいだは進まない。再表示で期限が延びたら追従する。
        // 世代が進んでから IsOpen になるまで短く待ち、未表示のまま期限切れですりガラスへ進まない。
        while (DateTime.UtcNow < hardDeadline)
        {
            if (IsLibraryMaximized)
            {
                return;
            }

            if (_layoutModeToastUntilUtc is { } latest && latest > endAt)
            {
                endAt = latest;
                hardDeadline = endAt + TimeSpan.FromSeconds(1.0);
            }

            if (_centerStatusToast.IsOpen)
            {
                sawOpen = true;
            }

            if (sawOpen && !_centerStatusToast.IsOpen && DateTime.UtcNow >= endAt)
            {
                break;
            }

            if (!sawOpen && !_centerStatusToast.IsOpen)
            {
                if (DateTime.UtcNow < openDeadline)
                {
                    await Task.Delay(16).ConfigureAwait(true);
                    continue;
                }

                if (DateTime.UtcNow >= endAt)
                {
                    break;
                }
            }

            await Task.Delay(16).ConfigureAwait(true);
        }

        // 4) まだ開いていれば閉じるまで待つ。
        var closeDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(1.5);
        while (_centerStatusToast.IsOpen && DateTime.UtcNow < closeDeadline)
        {
            if (IsLibraryMaximized)
            {
                return;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }

        _layoutModeToastUntilUtc = null;
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
    }

    /// <summary>エディタ復帰時にプラグイン実体を載せる必要があるか。</summary>
    private bool EditorEnterNeedsVstInsertLoad()
    {
        if (!IsVstInsertUiEnabled())
        {
            return false;
        }

        foreach (var session in _sessions)
        {
            if (DocumentSessionStore.ChainHasAnyPlugin(session.VstInserts))
            {
                return true;
            }
        }

        return DocumentSessionStore.SnapshotHasAnyPlugin(AppStorage.Settings.DefaultVstInserts);
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

    private void RefreshTransportCommandsEnabled()
    {
        var doc = _document;
        var editor = !IsLibraryMaximized;
        var allowsRegions = editor && doc is { AllowsRegionsAndLoops: true };
        var hasSampleLoop = allowsRegions && doc is { SampleLoop.IsEmpty: false };
        Transport.SetCommandsEnabled(
            doc is not null,
            allowEdit: editor,
            new TransportBar.CommandAvailability(
                HasMarkers: TransportHasMarkers(),
                HasRegions: allowsRegions && doc is { Regions.Count: > 0 },
                HasSelectedRegions: allowsRegions && Waveform.HasSelectedRegions,
                HasSampleLoop: hasSampleLoop,
                HasSelection: doc is not null && !doc.Selection.IsEmpty,
                AllowsRegionsAndLoops: allowsRegions,
                CanBounce: CanBounceSelectedVstInserts(),
                CanMergeTabs: CanMergeSelectedTabs()));
        Transport.SetSampleLoopPresent(hasSampleLoop);
        Transport.SetRangeClick(_rangeClickEnabled);
        Transport.SetSilentSkip(SilentSkipLatch.IsLatched);
        Transport.SetAutoLoop(LibraryBrowser.AutoLoopEnabled);
        Transport.SetAlwaysOnTop(AlwaysOnTopLatch.IsLatched);
        Transport.SetCenterLock(!IsLibraryMaximized && Waveform.CenterLocked);
        Transport.ApplyMenuEnabled(
            WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(CaptureMenuBarHit())),
            playerMode: IsLibraryMaximized);
        if (IsVolumeCurveMode || IsPitchCurveMode)
        {
            // 許可操作＋同モードトグル＋抑制ダイアログへ届けるモード切替ボタンは有効のまま。
            Transport.RestrictCommands(cmd =>
                IsAllowedTransportDuringVolumeCurve(cmd)
                || IsSameModeToggleDuringVolumeCurve(cmd)
                || IsWarnableTransportDuringVolumeCurve(cmd));
        }

        // 押せるボタンが無いグループを畳んだあと、ツールバー領域自体の表示も合わせる。
        ApplyTransportToolbarVisibility();
        // ツールバーと同じタイミングで、タイムライン等のルート有効状態も直す。
        // ファイルを開いただけでは ApplyWaveformMaximizeChrome が走らず、灰色のまま残ることがある。
        RebuildMainMenuBar();
    }

    /// <summary>シーク先と同じソース（プレイヤーは捨てマーカー、編集はドキュメント）。</summary>
    private bool TransportHasMarkers() =>
        IsLibraryMaximized
            ? Waveform.HasPlayerScratchMarkers
            : _document is { Markers.Count: > 0 };

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
                ForgetVstInserts(session, clearChain: false);
            }
            else
            {
                ForgetVstInserts(session);
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

    private void SyncPlaylistSelectionLoudness()
    {
        SyncSelectionLoudness(measureMissing: true);
        // ミックスダウン等の選択条件が変わるのでツールバー／メニュー有効状態を合わせる。
        RefreshTransportCommandsEnabled();
    }

    private void OnLibraryPlaylistSelectionChanged()
    {
        // Shift+矢印の範囲伸ばし中は毎キーの平均計算を避ける（キーリピート滞留の一因）。
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0
            && (Keyboard.IsKeyDown(Key.Up)
                || Keyboard.IsKeyDown(Key.Down)
                || Keyboard.IsKeyDown(Key.PageUp)
                || Keyboard.IsKeyDown(Key.PageDown)
                || Keyboard.IsKeyDown(Key.Home)
                || Keyboard.IsKeyDown(Key.End)))
        {
            ScheduleDeferredPlaylistSelectionLoudness();
            return;
        }

        _playlistSelectionLoudnessDeferred = false;
        _playlistSelectionLoudnessDeferTicket++;
        SyncPlaylistSelectionLoudness();
    }

    private void ScheduleDeferredPlaylistSelectionLoudness()
    {
        _playlistSelectionLoudnessDeferred = true;
        var ticket = ++_playlistSelectionLoudnessDeferTicket;
        Dispatcher.BeginInvoke(
            () =>
            {
                if (ticket != _playlistSelectionLoudnessDeferTicket
                    || !_playlistSelectionLoudnessDeferred)
                {
                    return;
                }

                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0
                    && (Keyboard.IsKeyDown(Key.Up)
                        || Keyboard.IsKeyDown(Key.Down)
                        || Keyboard.IsKeyDown(Key.PageUp)
                        || Keyboard.IsKeyDown(Key.PageDown)
                        || Keyboard.IsKeyDown(Key.Home)
                        || Keyboard.IsKeyDown(Key.End)))
                {
                    return;
                }

                FlushDeferredPlaylistSelectionLoudness();
            },
            DispatcherPriority.Background);
    }

    private void FlushDeferredPlaylistSelectionLoudness()
    {
        if (!_playlistSelectionLoudnessDeferred)
        {
            return;
        }

        _playlistSelectionLoudnessDeferred = false;
        _playlistSelectionLoudnessDeferTicket++;
        SyncPlaylistSelectionLoudness();
    }

    private void QueuePlaylistExtendStep(int delta, bool byGroup)
    {
        if (delta == 0)
        {
            return;
        }

        if (byGroup)
        {
            _playlistExtendPendingGroupSteps += delta;
        }
        else
        {
            _playlistExtendPendingDelta += delta;
        }

        if (_playlistExtendFlushQueued)
        {
            return;
        }

        _playlistExtendFlushQueued = true;
        // Input より下げ、同一フレームのメーター描画（Background）の前にまとめて進める。
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushPlaylistExtendPending);
    }

    private void FlushPlaylistExtendPending()
    {
        _playlistExtendFlushQueued = false;
        var delta = _playlistExtendPendingDelta;
        var groups = _playlistExtendPendingGroupSteps;
        _playlistExtendPendingDelta = 0;
        _playlistExtendPendingGroupSteps = 0;
        if (delta == 0 && groups == 0)
        {
            return;
        }

        // 溜まりすぎた分は次フレームへ。1フレームの大量 Add でメーターも選択も止まるのを防ぐ。
        var maxRows = LibraryPlayerMode.PlaylistExtendMaxRowsPerFrame;
        var maxGroups = LibraryPlayerMode.PlaylistExtendMaxGroupsPerFrame;
        var applyGroups = groups;
        if (Math.Abs(groups) > maxGroups)
        {
            applyGroups = Math.Sign(groups) * maxGroups;
            _playlistExtendPendingGroupSteps = groups - applyGroups;
        }

        var applyDelta = delta;
        if (Math.Abs(delta) > maxRows)
        {
            applyDelta = Math.Sign(delta) * maxRows;
            _playlistExtendPendingDelta = delta - applyDelta;
        }

        if (applyGroups != 0)
        {
            var direction = applyGroups > 0 ? 1 : -1;
            for (var i = 0; i < Math.Abs(applyGroups); i++)
            {
                LibraryBrowser.MoveSelectionByGroup(direction, extend: true);
            }
        }

        if (applyDelta != 0)
        {
            LibraryBrowser.MoveSelection(applyDelta, extend: true, wrap: false);
        }

        ScheduleDeferredPlaylistSelectionLoudness();
        if (_playlistExtendPendingDelta != 0 || _playlistExtendPendingGroupSteps != 0)
        {
            _playlistExtendFlushQueued = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Render, FlushPlaylistExtendPending);
        }
    }

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

        // テンキー 4/6 や ↑↓ は「押下で選択・離して再生」。ここで即 Play すると
        // 離し側の再生と二重になり、曲頭がジャジャーンと重なる。選択中はアートワーク
        // だけ更新し、本再生はキー離し（QueueInteractiveLibraryPlay）に任せる。
        if (_libraryPlayOnTrackKeyRelease || _libraryPlayOnArrowRelease)
        {
            ShowLibraryArtwork(session);
            return;
        }

        _ = PlayLibrarySessionAsync(session);
    }

    private void LibraryBrowser_SessionPlayRequested(object? sender, DocumentSession session)
    {
        _ = PlayLibrarySessionAsync(session);
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

        ForgetVstInserts(dropSet);
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

        // 画面のグループ／列ソート順で次を探す（_sessions 追加順だと次グループに飛ばない）。
        if (LibraryBrowser.TryGetDisplayedNeighbors(anchor, dropSet, out var next, out var prev))
        {
            return next ?? prev;
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

        QueueInteractiveLibraryPlay(session);
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
            QueueInteractiveLibraryPlay(session);
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
        ShowOptionToggleToast(UiStrings.LabelLibraryShuffle, LibraryBrowser.ShuffleEnabled);
        if (IsLibraryMaximized && IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void LibraryBrowser_AutoPlayChanged(object? sender, EventArgs e)
    {
        AppStorage.Settings.LibraryAutoPlay = LibraryBrowser.AutoPlayEnabled;
        AppStorage.Save();
        ShowOptionToggleToast(UiStrings.LabelLibraryAutoPlay, LibraryBrowser.AutoPlayEnabled);
    }

    private void LibraryBrowser_CheckSelectModeChanged(object? sender, EventArgs e)
    {
        ShowOptionToggleToast(UiStrings.LabelLibraryCheckSelect, LibraryBrowser.CheckSelectMode);
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
        ShowOptionToggleToast(UiStrings.LabelLibraryAutoLoop, LibraryBrowser.AutoLoopEnabled);
        Transport.SetAutoLoop(LibraryBrowser.AutoLoopEnabled);
        if (AutoLoopLatch.IsLatched != LibraryBrowser.AutoLoopEnabled)
        {
            AutoLoopLatch.IsLatched = LibraryBrowser.AutoLoopEnabled;
            AutoLoopLatch.RefreshAppearance();
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
        var session = new DocumentSession(document);
        // エディタへ戻したとき同じスロット＋プラグインが有効になるよう、開いた時点で載せる。
        ApplyDefaultVstInserts(session);
        return session;
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
            ForgetVstInserts(_sessions.ToArray());
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

    private async void LibraryBrowser_PlaylistVisualOpenFailed(object? sender, string message)
    {
        // 引数起動ではクローク中に開く。クローク中の ShowDialog は操作できないので、出てから知らせる。
        for (var i = 0; i < 100 && WindowPaintReveal.IsPending(this); i++)
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        if (!IsLoaded)
        {
            return;
        }

        OwnerCenteredMessageBox.Show(
            this,
            message,
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
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
