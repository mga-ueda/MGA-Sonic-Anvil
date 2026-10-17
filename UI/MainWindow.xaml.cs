using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow : Window
{
    private readonly AudioPlayer _player = new();
    private readonly DocumentWorkspace _workspace = new();
    private readonly EditHistory _idleHistory = new();
    private List<DocumentSession> _sessions => _workspace.Sessions;
    private DocumentSession? _activeSession
    {
        get => _workspace.Active;
        set => _workspace.Active = value;
    }
    private EditHistory _history => _activeSession?.History ?? _idleHistory;
    private readonly LevelMeterEngine _meter = new();
    private readonly Stopwatch _meterClock = Stopwatch.StartNew();
    private bool _meterRendering;
    private DispatcherTimer? _meterReleaseTimer;
    private TimeSpan _lastRenderingTime;
    private long _lastPlaybackVisualStamp;
    private bool _analyzerTickQueued;
    private readonly DispatcherTimer _playTimer;
    private readonly DispatcherTimer _visualPlayheadTimer;
    private readonly DispatcherTimer _markerDigitTimer;
    private readonly DispatcherTimer _markerNudgeTimer;
    private readonly DispatcherTimer _placeRepeatTimer;
    private readonly DispatcherTimer _boostNudgeTimer;
    private readonly DispatcherTimer _seekNudgeTimer;
    private readonly DispatcherTimer _videoFrameStepTimer;
    private int _seekNudgeDirection;
    private bool _seekNudgeRepeatStarted;
    private long _seekNudgeLastAt;
    private int _videoFrameStepDirection;
    private bool _videoFrameStepRepeatStarted;
    private PlaceRepeatKind _placeRepeatKind;
    private bool _placeRepeatStarted;
    private int _boostNudgeDirection;
    private bool _boostRepeatStarted;
    private bool _placeSessionOpen;
    private MarkerSnapshot[] _placeMarkersBefore = [];
    private WaveRegion[] _placeRegionsBefore = [];
    private int _markerNudgeDirection;
    private int _playbackShuttleDirection;
    /// <summary>停止中に早送り／早戻しを始めたとき。キーを離したらその位置で停止のままにする。</summary>
    private bool _shuttleStartedWhileStopped;
    private bool _shuttleRestoreImmersivePause;
    private bool _nudgeAtPlayhead;
    private bool _nudgeRepeatStarted;
    private bool _timelineNudgeOpen;
    private MarkerSnapshot[] _timelineNudgeMarkersBefore = [];
    private WaveRegion[] _timelineNudgeRegionsBefore = [];
    private WaveSelection _timelineNudgeLoopBefore;
    private PlayerScratchSnapshot? _timelineNudgeScratchBefore;
    private int _markerNumber;
    private int _regionNumber;
    private int _pdfPageDigit;
    private AudioDocument? _document => _activeSession?.Document;
    private RangeDivideState? _markerDivide;
    private RangeDivideState? _regionDivide;
    /// <summary>範囲等分クリックのユーザー設定。既定オフ。自動ではオンにしない。</summary>
    private bool _rangeClickEnabled;
    private AudioClip? _clipboard;
    private AudioOutputSettings _outputSettings;
    private long _lastPlaybackStart;
    private bool _syncingScroll;
    private bool _syncingChrome;
    private bool _syncingSpeakers;
    private WaveformMaximizeMode _waveformMaximizeMode;
    private WindowState _windowStateBeforeWaveformMax;
    private WindowStyle _windowStyleBeforeWaveformMax;
    private ResizeMode _resizeModeBeforeWaveformMax;
    private Rect _boundsBeforeWaveformMax;
    private bool _minimalBeforeWaveformMax;
    private double _meterColumnPreferred;
    private int _playbackGeneration;
    private int _editorPlayAfterPcmTicket;
    private bool _didRestoreLastDocument;
    private TransportIconButton? _waapiToggle;
    private System.Windows.Controls.ContextMenu? _fadeMenu;
    private System.Windows.Controls.ContextMenu? _formatMenu;
    private System.Windows.Controls.ContextMenu? _volumeMenu;
    private System.Windows.Controls.ContextMenu? _pitchMenu;
    private System.Windows.Controls.ContextMenu? _timeStretchMenu;
    private System.Windows.Controls.ContextMenu? _silenceMenu;
    private System.Windows.Controls.ContextMenu? _silenceSelectionMenu;
    private long _silenceInsertFrame;
    private System.Windows.Controls.ContextMenu? _waveMenu;
    private FormatConvertKind _formatKind;
    private FormatSizePreview? _formatSizePreview;
    private readonly EffectPreviewState _formatPreview = new();
    private readonly EffectPreviewState _fadePreview = new();
    private bool _fadePromptIsIn;
    private bool _fadeReplayOnHighlight;
    private readonly EffectPreviewState _volumePreview = new();
    private readonly EffectPreviewState _pitchPreview = new();
    private readonly EffectPreviewState _timeStretchPreview = new();
    private bool _resumeAfterScrub;
    private bool _closing;
    private bool _exitAfterFlush;
    private bool _bindingWorkspace;
    /// <summary>動画ファイルの引数／関連付け起動。専用サイズ（原寸）と位置記憶。</summary>
    private bool _videoLaunchPlacement;
    /// <summary>動画ミニ窓の原寸に対する倍率（F8／引数起動中）。</summary>
    private double _videoLaunchScale = LibraryVideoWindowScale.Native;
    private bool _videoLaunchScaleLatched;
    private DispatcherTimer? _videoLaunchScaleToastTimer;
    private bool _videoLaunchScaleApplying;
    /// <summary>動画起動で映像が開くまでウィンドウを出さないための待ち。</summary>
    private TaskCompletionSource<bool>? _videoLaunchShowReady;
    private DispatcherTimer? _videoLaunchPlacementSaveTimer;
    /// <summary>A でファイル名を出したあと、フェード完了後にラウドネス回避をやり直す。</summary>
    private DispatcherTimer? _playlistVideoFileNamePlaceTimer;
    /// <summary>PDF ページ切替時のページ番号トースト（表示→フェードアウト）。</summary>
    private DispatcherTimer? _playlistPdfPageNumberToastTimer;
    /// <summary>動画／PDF 複数再生時のプレイリスト位置トースト（表示→フェードアウト）。</summary>
    private DispatcherTimer? _playlistTrackIndexToastTimer;
    /// <summary>直前にトーストした位置キー（同じ曲では再フラッシュしない）。</summary>
    private string? _playlistTrackIndexToastKey;
    private int _autoSpeakerSeenChannels = int.MinValue;
    private ImageSource? _brandLogoDark;
    private ImageSource? _brandLogoLight;
    private int _brandLogoDecodeWidth;
    private double _brandLogoInkBottomFrac = 1;
    private TimeScrollBar TimeScroll => TimeScrollStrip.Bar;
    private WaveformView Waveform => _tileActiveView ?? PrimaryWaveform;

    public MainWindow()
    {
        InitializeComponent();
        LibraryBrowser.BindWaveformGlow(PlayerWaveGlowHost);
        LibraryBrowser.BindWindowVisual(PlayerVisualHost);
        // エディタもプレイヤーと同じウォッシュ。モード切替（F11／F12）を待たない。
        LibraryBrowser.UseWindowFallbackWash();
        LibraryBrowser.SessionActivated += LibraryBrowser_SessionActivated;
        LibraryBrowser.PlaylistSelectionChanged += (_, _) => SyncPlaylistSelectionLoudness();
        LibraryBrowser.SessionPlayRequested += LibraryBrowser_SessionPlayRequested;
        LibraryBrowser.PlaylistVisualEnded += LibraryBrowser_PlaylistVisualEnded;
        LibraryBrowser.PlaylistVisualOpened += LibraryBrowser_PlaylistVisualOpened;
        LibraryBrowser.PlaylistVisualStateChanged += (_, _) => SyncPlaylistVideoChromeFade();
        LibraryBrowser.VisibleColumnPresetsChanged += LibraryBrowser_VisibleColumnPresetsChanged;
        LibraryBrowser.GroupChanged += LibraryBrowser_GroupChanged;
        LibraryBrowser.ExplorerFolderChanged += LibraryBrowser_ExplorerFolderChanged;
        LibraryBrowser.ExplorerExpandedChanged += LibraryBrowser_ExplorerExpandedChanged;
        LibraryBrowser.ExplorerFolderOpened += LibraryBrowser_ExplorerFolderOpened;
        LibraryBrowser.ExplorerFoldersOpened += LibraryBrowser_ExplorerFoldersOpened;
        LibraryBrowser.ExplorerWidthChanged += LibraryBrowser_ExplorerWidthChanged;
        LibraryBrowser.FavoritesSplitChanged += LibraryBrowser_FavoritesSplitChanged;
        LibraryBrowser.FavoritesChanged += LibraryBrowser_FavoritesChanged;
        LibraryBrowser.ExplorerRootsChanged += LibraryBrowser_ExplorerRootsChanged;
        LibraryBrowser.FavoritesActivated += LibraryBrowser_FavoritesActivated;
        LibraryBrowser.ShuffleChanged += LibraryBrowser_ShuffleChanged;
        LibraryBrowser.AutoPlayChanged += LibraryBrowser_AutoPlayChanged;
        LibraryBrowser.AutoPlayNextChanged += LibraryBrowser_AutoPlayNextChanged;
        LibraryBrowser.ExplorerReplacePlaylistRequested += (_, _) => ReplaceLibraryFromExplorerFolder();
        LibraryBrowser.ClearPlaylistRequested += LibraryBrowser_ClearPlaylistRequested;
        LibraryBrowser.MatchLoudnessRequested += (_, _) => PromptVolume();
        LibraryBrowser.ExportMp3Requested += (_, _) => ExportPlaylistSelectionMp3();
        LibraryBrowser.PlaylistPreviousRequested += (_, _) => TryStepLibraryPlaylist(-1);
        LibraryBrowser.PlaylistNextRequested += (_, _) => TryStepLibraryPlaylist(1);
        LibraryBrowser.PlaylistRestartRequested += (_, _) => RestartLibraryTrack();
        LibraryBrowser.PlaylistUndoRemoveRequested += (_, _) => TryUndoLibraryPlaylistRemove();
        LibraryBrowser.LibraryCatalogRebuildRequested += (_, _) => RebuildLibraryCatalog();
        LibraryBrowser.ListContextMenuOpened += LibraryBrowser_ListContextMenuOpened;
        LibraryBrowser.ListContextMenuClosed += LibraryBrowser_ListContextMenuClosed;
        LibraryBrowser.SetVisibleColumnPresets(
            AppStorage.Settings.ResolvedLibraryListColumnsWave(),
            AppStorage.Settings.ResolvedLibraryListColumnsMp3(),
            AppStorage.Settings.ResolvedLibraryListColumnsMixed(),
            mixedCustomized: AppStorage.Settings.LibraryListColumnsMixed.Length > 0,
            videoColumns: AppStorage.Settings.ResolvedLibraryListColumnsVideo());
        LibraryBrowser.SetPlaylistWaveformOptions(
            AppStorage.Settings.ResolvedLibraryPlaylistWaveformSizeWave(),
            AppStorage.Settings.ResolvedLibraryPlaylistWaveformSizeMp3());
        LibraryBrowser.SetDefaultAudioFormat(AppStorage.Settings.ResolvedDefaultAudioFormat());
        LibraryBrowser.SetGroup(AppStorage.Settings.ResolvedLibraryListGroup());
        LibraryBrowser.SetExplorerRoots(AppStorage.Settings.ResolvedLibraryExplorerRoots());
        ScheduleLibraryCatalogPrune();
        LibraryBrowser.SetExplorerExpanded(AppStorage.Settings.ResolvedLibraryExplorerExpanded());
        LibraryBrowser.SetExplorerFolder(AppStorage.Settings.ResolvedLibraryExplorerPath());
        LibraryBrowser.SetExplorerWidth(AppStorage.Settings.LibraryExplorerWidth);
        LibraryBrowser.SetFavoritesSplit(AppStorage.Settings.LibraryFavoritesSplit);
        LibraryBrowser.SetFavorites(AppStorage.Settings.ResolvedLibraryFavoritePaths());
        LibraryBrowser.AutoPlayEnabled = AppStorage.Settings.LibraryAutoPlay;
        DpiChanged += (_, _) =>
        {
            _brandLogoDark = null;
            _brandLogoLight = null;
            LoadBrandLogo();
        };
        LoadBrandLogo();
        Loaded += (_, _) => AlignBrandLicense();
        UipiFileDrop.Attach(this, OnUipiFilesDropped);
        if (LaunchFiles.StartupHasVideo())
        {
            BeginVideoLaunchSession();
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Normal;
        }
        else if (!WindowPlacement.TryApply(this, AppStorage.Settings))
        {
            WindowPlacement.ApplyFirstLaunch(this);
        }
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        ApplyMeterColumnWidth(AppStorage.Settings.MeterColumnWidth);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        // 動画の関連付け起動はミニプレイヤーへ切り替えてから出す（エディタが一瞬見えないように）。
        // ファイル無しのプレイヤー起動も、出す前にプレイヤーへ切り替える。
        WindowPaintReveal.Attach(
            this,
            OnStartupRevealed,
            autoReveal: !_videoLaunchPlacement && !HoldsRevealForStartupMode());
        // 編集履歴はその他ウィンドウと同じ扱いで等倍にする（ルートの表示倍率を打ち消す）。
        HistoryOverlay.LayoutTransform = UiScaleService.CreateCounterTransform();
        ApplyStatusFieldChrome();
        UiThemeService.Changed += (_, _) =>
        {
            ApplyStatusFieldChrome();
            Dispatcher.BeginInvoke(ApplyUiColors);
        };
        UiScaleService.Changed += (_, _) => Dispatcher.BeginInvoke(OnUiScaleChanged);
        AlwaysOnTopCheck.IsChecked = AppStorage.Settings.AlwaysOnTop;
        Topmost = AppStorage.Settings.AlwaysOnTop;
        SilentSkipCheck.IsChecked = AppStorage.Settings.SilentSkip;
        Mp3Gapless.Enabled = AppStorage.Settings.GaplessPlayback;
        ApplySilentSkipFromSettings();

        Transport.CommandInvoked += (_, command) => ExecuteTransport(command);
        StatusTimes.CurrentCommitted += (_, frame) =>
        {
            if (_bindingWorkspace)
            {
                return;
            }

            SeekFrame(frame);
            Waveform.CenterViewOnPlayhead();
        };
        StatusTimes.SelectionCommitted += (_, range) =>
        {
            if (_bindingWorkspace)
            {
                return;
            }

            Waveform.SetSelection(range);
        };
        StatusTimes.RequestWaveformFocus += (_, _) => Keyboard.Focus(Waveform);
        StatusTimes.SelectionCopyState = () =>
        {
            var items = CaptureSelectionClipboardItems();
            return (SelectionClipboardText.CanCopy(items), HasTabSelection);
        };
        StatusTimes.CopySelectionTimeRequested += (_, pathKind) => CopySelectionTimes(pathKind);
        UiStrings.LanguageChanged += (_, _) => Dispatcher.BeginInvoke(RefreshLocalizedText);
        HookWaveformEvents(PrimaryWaveform);
        Transport.SetAnalysisView(Waveform.AnalysisView);
        Overview.ViewStartChanged += (_, start) =>
        {
            Waveform.UnlockCenter();
            ScrubVisibleCenterAt(start);
            Waveform.PanViewStart(start);
        };
        Overview.DragEnded += (_, _) => EndOverviewScrub();
        TimeScroll.ValueChanged += (_, _) =>
        {
            if (_syncingScroll || TimeScroll.IsRangeResizing || Waveform.CenterLocked)
            {
                return;
            }

            Waveform.SetViewStartExternal(TimeScroll.Value);
        };
        TimeScroll.RangeChanged += (_, range) =>
        {
            Waveform.UnlockCenter();
            Waveform.SetVisibleRange(range.ViewStart, range.ViewSpan);
        };
        TimeScrollStrip.AmpZoomIn += (_, _) => Waveform.ZoomAmpIn();
        TimeScrollStrip.AmpZoomOut += (_, _) => Waveform.ZoomAmpOut();
        TimeScrollStrip.TimeZoomIn += (_, _) =>
        {
            Waveform.UnlockCenter();
            Waveform.ZoomTimeIn(anchorPlayhead: false);
        };
        TimeScrollStrip.TimeZoomOut += (_, _) =>
        {
            Waveform.UnlockCenter();
            Waveform.ZoomTimeOut(anchorPlayhead: false);
        };
        TimeScrollStrip.ScrollLeft += (_, _) =>
        {
            Waveform.UnlockCenter();
            Waveform.PanByVisibleFraction(-TimeScrollRange.ScrollStepFraction);
        };
        TimeScrollStrip.ScrollRight += (_, _) =>
        {
            Waveform.UnlockCenter();
            Waveform.PanByVisibleFraction(TimeScrollRange.ScrollStepFraction);
        };

        _player.PlaybackEnded += (_, generation) => Dispatcher.BeginInvoke(() => OnPlaybackEnded(generation));
        _player.Diagnostic += (_, message) => Dispatcher.BeginInvoke(() =>
            OwnerCenteredMessageBox.Show(this, message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning));
        AppStorage.Settings.EnsureSpeakerPresets();
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        _player.ApplyOutputSettings(_outputSettings);
        Spectrum.Player = _player;
        LoudnessMeter.Player = _player;
        Waveform.LoudnessTargetLufs = AppStorage.Settings.ResolvedLoudnessTargetLufs();
        Overview.SeekTrailSource = Waveform;
        VectorScope.Player = _player;

        // 優先度は Input が唯一安全：Render だと追従描画が入力を飢餓させ操作不能になり
        // （深い拡大のスペクトログラム追従で実際に発生）、Background だと逆に
        // マウス移動がタイマーを飢餓させ再生ヘッドがカクつく。
        // Input はマウス入力と同列 FIFO で処理されるため、どちらの飢餓も起きない。
        ApplyPlayerRoute();
        RefreshSpeakerMenu();
        _recordTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };
        _recordTimer.Tick += (_, _) => OnRecordTimerTick();
        _playTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _playTimer.Tick += (_, _) => OnPlayTick();
        _visualPlayheadTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _visualPlayheadTimer.Tick += (_, _) => OnVisualPlayheadTick();
        _markerDigitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _markerDigitTimer.Tick += (_, _) => ResetMarkerDigitEntry();
        _markerNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _markerNudgeTimer.Tick += (_, _) => OnMarkerNudgeTick();
        _placeRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _placeRepeatTimer.Tick += (_, _) => OnPlaceRepeatTick();
        _boostNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _boostNudgeTimer.Tick += (_, _) => OnSpectrogramBoostNudgeTick();
        _seekNudgeTimer = new DispatcherTimer(LibraryPlayerMode.SeekNudgeTimerPriority)
        {
            Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.SeekNudgeRepeatDelayMs),
        };
        _seekNudgeTimer.Tick += (_, _) => OnSeekNudgeTick();
        _videoFrameStepTimer = new DispatcherTimer(LibraryPlayerMode.SeekNudgeTimerPriority)
        {
            Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.VideoFrameStepRepeatDelayMs),
        };
        _videoFrameStepTimer.Tick += (_, _) => OnVideoFrameStepTick();

        Deactivated += (_, _) =>
        {
            StopMarkerNudge();
            StopPlaceRepeat();
            StopSpectrogramBoostNudge();
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            CloseEditHistory(commit: true);
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        // クラスハンドラが先に Handled にしても、Alt 解放直後のテンキーを見る。
        AddHandler(PreviewKeyUpEvent, new KeyEventHandler(MainWindow_PreviewKeyUp), handledEventsToo: true);
        AddHandler(KeyUpEvent, new KeyEventHandler(MainWindow_AltMenuKeyUp), handledEventsToo: true);
        AttachNumpadShiftHook();
        PreviewMouseDown += MainWindow_PreviewMouseDown;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        PreviewDrop += MainWindow_Drop;
        PreviewDragOver += MainWindow_DragOver;
        Closing += MainWindow_Closing;
        Activated += (_, _) => SyncShellFullscreen();
        SizeChanged += (_, _) =>
        {
            SyncBusyGlassOverlayBounds();
            RememberVideoLaunchPlacementIfNeeded();
            SyncVideoLaunchScaleAfterUserResize();
            SyncWindowProportionalOverlayScale();
            SchedulePlaylistVideoFileNamePlacement();
            if (IsFullscreenMaximizeMode(_waveformMaximizeMode))
            {
                PersistCurrentWindowPlacement();
            }
        };
        LocationChanged += (_, _) =>
        {
            RememberVideoLaunchPlacementIfNeeded();
            if (IsFullscreenMaximizeMode(_waveformMaximizeMode))
            {
                PersistCurrentWindowPlacement();
            }
        };
        VideoLaunchAspectResize.Attach(this, TryGetVideoLaunchAspectConstraints);
        Closed += (_, _) =>
        {
            StopMeterRendering();
            _meterReleaseTimer?.Stop();
            _playTimer.Stop();
            _visualPlayheadTimer.Stop();
            _recordTimer.Stop();
            _recorder.Dispose();
            _waapiPollTimer.Stop();
            StopMarkerNudge();
            StopPlaceRepeat();
            StopSpectrogramBoostNudge();
            StopPlaybackShuttle();
            StopSeekNudge();
            StopVideoFrameStep();
            ResetMarkerDigitEntry();
            // Closing で既に破棄済みでも安全（冪等）。
            DisposeAllWaveforms();
            _player.Dispose();
        };

        BindWorkspace(null);
        PlaceWaapiToggle();
        ApplyLibraryWashChrome(true);
        InitializeWaapi();
        RefreshLocalizedText();
        // 表示前にフルスクリーンへ入れる。出したあとに最大化を外すと作業領域のままタスクバーが残る。
        if (!LaunchFiles.StartupHasVideo())
        {
            EnterStartupMode();
        }
        MgaSonicAnvil.SingleInstance.StartWatch(() =>
            Dispatcher.BeginInvoke(ActivateFromOtherInstance, DispatcherPriority.Send));
    }

    private void ActivateFromOtherInstance()
    {
        if (_closing)
        {
            // 終了中の窓は出さない。パスはキューに残し、引き継いだプロセスが読む。
            SingleInstance.NotifyActivated();
            return;
        }

        if (Opacity < 1 || WindowPaintReveal.IsPending(this))
        {
            WindowPaintReveal.Reveal(this);
        }

        ForegroundActivation.BringToFront(this);
        SingleInstance.NotifyActivated();
        var pending = SingleInstance.TakePendingPaths();
        if (pending.Length == 0)
        {
            return;
        }

        if (!_didRestoreLastDocument)
        {
            LaunchFiles.SetStartup(MergeLaunchPaths(LaunchFiles.TakeStartup(), pending));
            return;
        }

        OpenLaunchPaths(pending);
    }

    private void OnStartupRevealed()
    {
        // クローク中（動画起動の遅延表示）は前面化しない。Reveal 後に行う。
        if (LaunchFiles.HasStartup && !WindowPaintReveal.IsPending(this))
        {
            ForegroundActivation.BringToFront(this);
        }

        // 動画起動はクロークのままミニプレイヤーへ先に入れる（アイドル待ちのあいだもエディタを残さない）。
        if (_videoLaunchPlacement && LaunchFiles.HasStartup)
        {
            var peek = LaunchFiles.PeekStartup();
            if (LaunchFiles.ContainsVideo(peek))
            {
                EnterLibraryPlayerIfLaunchHasMp3(peek);
                UpdateLayout();
            }
        }
        else if (HoldsRevealForStartupMode())
        {
            // クローク中に選んだ画面へ入れてから出す。表示後に切り替えるとエディタが一瞬見える。
            EnterStartupMode();
            UpdateLayout();
            // 配置をクローク中に描いてから出す。
            Dispatcher.BeginInvoke(
                () => Dispatcher.BeginInvoke(RevealStartupMode, DispatcherPriority.ContextIdle),
                DispatcherPriority.Render);
        }

        // 表示を先に出し、前回ドキュメントの読み込みは次のアイドルへ回す。
        // 動画起動は映像が開くまでクロークを維持する。
        Dispatcher.BeginInvoke(RestoreLastDocumentAfterReveal, DispatcherPriority.ApplicationIdle);
        Dispatcher.BeginInvoke(() => _ = StartWaapiAsync(), DispatcherPriority.ApplicationIdle);
        _ = CheckForAppUpdateAsync();
    }

    private async void RestoreLastDocumentAfterReveal()
    {
        var launch = MergeLaunchPaths(LaunchFiles.TakeStartup(), SingleInstance.TakePendingPaths());
        var videoLaunch = LaunchFiles.ContainsVideo(launch);
        if (videoLaunch)
        {
            BeginVideoLaunchSession();
            _videoLaunchShowReady = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            EnterLibraryPlayerIfLaunchHasMp3(launch);
            UpdateLayout();
            // 前回ドキュメントを戻すとエディタが一瞬見えるので、関連付け動画起動ではスキップする。
            _didRestoreLastDocument = true;
            // DWM クローク中は Opacity を戻して MediaElement を描けるようにする（画面には出ない）。
            if (Opacity < 1)
            {
                Opacity = 1;
            }

            if (launch.Length > 0)
            {
                await OpenLaunchPathsAsync(launch).ConfigureAwait(true);
                var ready = _videoLaunchShowReady.Task;
                var finished = await Task.WhenAny(ready, Task.Delay(3_000)).ConfigureAwait(true);
                if (finished == ready)
                {
                    await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
                }
            }

            _videoLaunchShowReady = null;
            NotifySettingsRecreatedIfNeeded();
            UpdateLayout();
            if (WindowPaintReveal.IsPending(this))
            {
                WindowPaintReveal.Reveal(this);
                ForegroundActivation.BringToFront(this);
            }

            return;
        }

        await TryRestoreLastDocumentAsync().ConfigureAwait(true);
        EnterLibraryPlayerIfLaunchHasMp3(launch);
        if (launch.Length > 0)
        {
            await OpenLaunchPathsAsync(launch).ConfigureAwait(true);
        }
        else
        {
            EnterStartupMode();
        }

        NotifySettingsRecreatedIfNeeded();
        UpdateLayout();
        Waveform.Refresh();
        Overview.InvalidateVisual();
        if (WindowPaintReveal.IsPending(this))
        {
            WindowPaintReveal.Reveal(this);
        }
    }

    private void SignalVideoLaunchShowReady() =>
        _videoLaunchShowReady?.TrySetResult(true);

    private void NotifySettingsRecreatedIfNeeded()
    {
        var reset = AppStorage.SettingsReset;
        if (reset is SettingsFileReset.None)
        {
            return;
        }

        AppStorage.AcknowledgeSettingsReset();
        var text = reset == SettingsFileReset.Outdated
            ? UiStrings.SettingsFileRecreatedOutdated
            : UiStrings.SettingsFileRecreatedInvalid;
        OwnerCenteredMessageBox.Show(
            this,
            text,
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void BindWorkspace(DocumentSession? session)
    {
        SyncPlayerMeterFade();
        if (session is not null
            && (session.Document.IsDeferredLoad
                || (!IsLibraryMaximized && session.Document.IsStreamPlayback)))
        {
            BindSingleWorkspace(session);
            _ = EnsureLibrarySessionLoadedAsync(session);
            return;
        }

        if (IsLibraryMaximized)
        {
            BindSingleWorkspace(session);
            return;
        }

        if (_tileMode)
        {
            if (session is null)
            {
                ExitWaveformTileMode(bindPrimary: false);
                BindSingleWorkspace(null);
                return;
            }

            if (_sessions.Count < 2)
            {
                ExitWaveformTileMode(bindPrimary: false);
                BindSingleWorkspace(session);
                return;
            }

            if (TryBindTiledWorkspace(session, resetInteraction: true))
            {
                TryApplyAutoSpeaker();
                return;
            }

            _activeSession = session;
            RebuildTabBar();
            RefreshTileChrome();
            TryApplyAutoSpeaker();
            return;
        }

        BindSingleWorkspace(session);
    }

    private void BindSingleWorkspace(DocumentSession? session)
    {
        _bindingWorkspace = true;
        try
        {
            var sessionChanged = !ReferenceEquals(_activeSession, session);
            ResetWorkspaceInteraction();
            _activeSession = session;
            if (IsLibraryMaximized && sessionChanged)
            {
                Waveform.ClearPlayerScratchAll();
            }

            Waveform.Document = _document;
            Overview.Document = _document;
            if (IsLibraryMaximized)
            {
                SyncPlayerScratchEnabled();
            }
            if (_recording && _recordSession is not null && ReferenceEquals(session, _recordSession))
            {
                Waveform.SetLiveRecording(true);
            }
            ApplyChannelSolo();
            if (session is not null)
            {
                if (IsLibraryMaximized)
                {
                    Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
                    Waveform.ResetTimeZoom();
                    Waveform.ResetAmpZoom();
                    Waveform.PlayheadFrame = 0;
                    Waveform.LoopEnabled = session.LoopEnabled;
                }
                else
                {
                    if (session.AnalysisView is { } mode)
                    {
                        Waveform.SetAnalysisView(mode);
                    }

                    Waveform.ApplyPersistedView(
                        session.TimeZoom,
                        session.AmpZoom,
                        session.ViewStart,
                        session.PlayheadFrame);
                    Waveform.RestoreSelectedMarkers(session.SelectedMarkerFrames);
                    Waveform.LoopEnabled = session.LoopEnabled;
                }

                Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
                SyncOverviewPlayhead();
                Transport.SetAnalysisView(Waveform.AnalysisView);
            }
            else
            {
                Overview.SetSelectedMarkerFrames(null);
                Waveform.LoopEnabled = true;
                // 表示対象が無いのにスペクトログラム／ラウドネスだけ残ると空の解析モードになる。
                Waveform.ExitAnalysisView();
                Transport.SetAnalysisView(WaveformAnalysisView.Waveform);
                SyncOverviewPlayhead();
            }

            Transport.SetPlaying(false);
            RefreshTransportCommandsEnabled();
            ExtinguishMeter();
            SyncMonitorLayout();
            LoudnessMeter.Document = IsLibraryMaximized ? null : _document;
            LoudnessMeter.ResetLive();
            if (IsLibraryMaximized)
            {
                CancelFileNameEdit();
            }
            else
            {
                // 4／6 連打ではタブを作り直さずヘッダだけ更新（RefreshTitle → RefreshTabHeaders）。
                SyncTabBar();
            }

            RefreshTitle();
            SyncViewChrome();
            RefreshStatus();
            RefreshHistoryStrip();
            TryApplyAutoSpeaker();
            SyncRangeClicks();
        }
        finally
        {
            _bindingWorkspace = false;
        }
    }

    private void ResetWorkspaceInteraction()
    {
        StatusTimes.CancelEdit();
        Overview.CancelDrag();
        if (Waveform.IsScrubbing)
        {
            Waveform.CancelScrub();
        }

        _player.Stop();
        _playTimer.Stop();
        if (!LibraryPlayerMode.PreservesVisualPlayheadOnBind(IsLibraryMaximized))
        {
            _visualPlayheadTimer.Stop();
            StopMeterRendering();
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        CloseEditHistory(commit: true);
        _resumeAfterScrub = false;
        StopMarkerNudge();
        StopSpectrogramBoostNudge();
        StopPlaybackShuttle();
        StopSeekNudge();
        StopVideoFrameStep();
        ResetMarkerDigitEntry();
        Waveform.UnlockCenter();
        ClearSeekTrails();
    }

    private void ClearSeekTrails()
    {
        ForEachWaveform(view =>
        {
            view.ExitPlayheadFrame = -1;
            view.SetTrailRecording(false);
        });
        Overview.InvalidateVisual();
    }

    private void RefreshLocalizedText()
    {
        BrandLicenseHost.LinkText = UiStrings.CopyrightText;
        QueueAlignBrandLicense();
        AlwaysOnTopCheck.Content = UiStrings.LabelAlwaysOnTop;
        SilentSkipCheck.Content = UiStrings.LabelSilentSkip;
        StatusTimes.ApplyLocalizedText();
        TimeScrollStrip.ApplyLocalizedText();
        Transport.ApplyLocalizedText();
        Transport.SetMaximizeMode(_waveformMaximizeMode);
        SpeakerMenuLabel.Text = UiStrings.LabelStatusSpeaker;
        RefreshSpeakerMenu();
        if (_waapiToggle is not null)
        {
            _waapiToggle.InvalidateVisual();
        }

        ForEachWaveform(view => view.ApplyLocalizedText());
        WaapiBar.ApplyLocalizedText();
        RefreshWaapiStatusDisplay();
        HistoryOverlay.ApplyLocalizedText();
        if (HistoryOpen)
        {
            RefreshHistoryOverlay();
        }
        else
        {
            RefreshHistoryStrip();
        }

        RefreshStatus();
        LevelMeter.InvalidateVisual();
        LoudnessMeter.ApplyLocalizedText();
        _colorDevPanel?.ApplyLocalizedText();
        _tabTimeTable?.ApplyLocalizedText();
        LibraryBrowser.ApplyLocalizedText();
    }

    private void RefreshTitle()
    {
        Title = AppVersion.FormTitleWithFile(_document?.SourcePath);
        RefreshTabHeaders();
    }

    private void SyncViewChrome()
    {
        if (_syncingChrome)
        {
            return;
        }

        _syncingChrome = true;
        try
        {
            var frames = _document?.FrameCount ?? 0;
            if (!TimeScroll.IsRangeResizing)
            {
                _syncingScroll = true;
                TimeScroll.Sync(Waveform.ViewStart, Waveform.ViewSpanFrames, frames);
                _syncingScroll = false;
            }
            Overview.SetView(Waveform.ViewStart, Waveform.ViewSpanFrames);
            if (!_playTimer.IsEnabled)
            {
                SyncTransportPosition();
            }
        }
        finally
        {
            _syncingChrome = false;
        }
    }

    private void RefreshStatus()
    {
        StatusMeta.Inlines.Clear();
        if (_openStatusText is not null)
        {
            StatusMeta.Inlines.Add(new Run(_openStatusText)
            {
                Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarDetailForeBrush")),
            });
            StatusOpenProgress.Visibility = Visibility.Visible;
            StatusOpenProgress.Value = _openStatusRatio;
            RefreshExportEnabled();
            RefreshTitle();
            return;
        }

        StatusOpenProgress.Visibility = Visibility.Collapsed;
        StatusOpenProgress.Value = 0;
        if (_document is null)
        {
            RefreshExportEnabled();
            SyncTransportPosition(0);
            RefreshTitle();
            return;
        }

        var kind = _document.SourceKind switch
        {
            AudioFileKind.Mp3 => "MP3",
            AudioFileKind.M4a => "M4A",
            AudioFileKind.Aiff => "AIFF",
            _ => "WAVE",
        };
        var normal = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarDetailForeBrush"));
        var edited = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarErrorDetailForeBrush"));
        var rate = _document.SampleRate;
        var bits = _document.BitsPerSample;
        var channels = _document.Channels;
        if (_formatSizePreview is { } preview)
        {
            switch (preview.Kind)
            {
                case FormatConvertKind.SampleRate when FormatConvert.IsValidSampleRate(preview.Value):
                    rate = preview.Value;
                    break;
                case FormatConvertKind.BitDepth when FormatConvert.IsValidBitDepth(preview.Value):
                    bits = preview.Value;
                    break;
                case FormatConvertKind.Channels when preview.Value is 1 or 2:
                    channels = preview.Value;
                    break;
            }
        }

        var estimatedBytes = _document.EstimateFileBytesFor(rate, bits, channels);
        var sizeEdited = estimatedBytes != _document.CommittedFileBytes;

        // 複数ファイルを開いているときだけ、冒頭に「n / m Files」を出す。
        if (_sessions.Count >= 2)
        {
            var number = _activeSession is null ? 0 : _sessions.IndexOf(_activeSession) + 1;
            AppendStatusRun(
                string.Create(CultureInfo.InvariantCulture, $"{number} / {_sessions.Count} Files"),
                edited: false,
                normal,
                edited);
        }

        AppendStatusRun(UiStrings.FormatSampleRate(rate), rate != _document.CommittedSampleRate, normal, edited);
        AppendStatusRun(UiStrings.FormatBitDepth(bits), bits != _document.CommittedBitsPerSample, normal, edited);
        AppendStatusRun(UiStrings.FormatChannels(channels), channels != _document.CommittedChannels, normal, edited);
        AppendStatusRun(kind, edited: false, normal, edited);
        if (_document.SourceKind is AudioFileKind.Mp3 or AudioFileKind.M4a)
        {
            AudioTagProbe.Ensure(_document);
            var bitRate = UiStrings.FormatBitRate(_document.CompressedBitRateKbps);
            if (bitRate.Length > 0)
            {
                AppendStatusRun(bitRate, edited: false, normal, edited);
            }
        }

        AppendStatusRun(
            UiStrings.FormatFileBytes(sizeEdited ? estimatedBytes : _document.FileBytes),
            sizeEdited,
            normal,
            edited);

        RefreshExportEnabled();
        SyncTransportPosition();
        RefreshTitle();
    }

    private void AppendStatusRun(string text, bool edited, Brush normal, Brush highlight)
    {
        if (StatusMeta.Inlines.Count > 0)
        {
            StatusMeta.Inlines.Add(new Run(" "));
        }

        StatusMeta.Inlines.Add(new Run(text)
        {
            Foreground = edited ? highlight : normal,
        });
    }

    private void SyncTransportPosition(long? frame = null)
    {
        if (_document is null)
        {
            // 遅延 ViewChanged が古い PlayheadFrame を sampleRate=0 で載せないよう、常に空表示へ戻す。
            StatusTimes.SetState(0, WaveSelection.Empty, 0, 0, hasDocument: false);
            PlaylistVideoNowEndText.Text = "00:00.00 / 00:00.00";
            PlaylistVideoSelText.Visibility = Visibility.Collapsed;
            return;
        }

        StatusTimes.SetState(
            frame ?? Waveform.PlayheadFrame,
            _document.Selection,
            _document.FrameCount,
            _document.SampleRate,
            hasDocument: true);
        RefreshPlaylistVideoTimecode(
            frame ?? Waveform.PlayheadFrame,
            _document.Selection,
            _document.FrameCount,
            _document.SampleRate);
    }

    private void RefreshPlaylistVideoTimecode(
        long nowFrame,
        WaveSelection selection,
        long endFrame,
        int sampleRate)
    {
        // 分：秒はそのまま、小数点以下だけフレーム（既定 30 fps。ステータスバーの ms 表示とは別）。
        var fps = LibraryPlayerMode.VideoTimecodeFramesPerSecond;
        var now = UiStrings.FormatTimecodeWithFrames(nowFrame, sampleRate, fps);
        var end = UiStrings.FormatTimecodeWithFrames(endFrame, sampleRate, fps);
        PlaylistVideoNowEndText.Text = now + " / " + end;
        if (selection.IsEmpty)
        {
            PlaylistVideoSelText.Visibility = Visibility.Collapsed;
            return;
        }

        var selStart = UiStrings.FormatTimecodeWithFrames(selection.StartFrame, sampleRate, fps);
        var selEnd = UiStrings.FormatTimecodeWithFrames(selection.EndFrame, sampleRate, fps);
        PlaylistVideoSelText.Text = selStart + " / " + selEnd;
        PlaylistVideoSelText.Visibility = Visibility.Visible;
    }

    private long VisibleCenterFrame() => VisibleCenterFrameAt(Overview.ViewStart);

    private long VisibleCenterFrameAt(double viewStart) =>
        (long)Math.Round(viewStart + Waveform.ViewSpanFrames * 0.5);

    private void ScrubVisibleCenterAt(double viewStart)
    {
        if (_document is null)
        {
            return;
        }

        var frame = VisibleCenterFrameAt(viewStart);
        if (Waveform.IsScrubbing)
        {
            Waveform.PreviewScrubAtFrame(frame);
            return;
        }

        Waveform.BeginScrubAtFrame(frame);
    }

    private void ApplyOverviewViewToWaveform()
    {
        Waveform.SetViewStartExternal(Overview.ViewStart);
        Waveform.CommitPannedView();
    }

    private void EndOverviewScrub()
    {
        ApplyOverviewViewToWaveform();
        if (!Waveform.IsScrubbing)
        {
            return;
        }

        Waveform.EndScrubAtFrame(VisibleCenterFrameAt(Overview.ViewStart), commit: true);
    }

    private WaveSelection ActiveRange()
    {
        if (_document is null)
        {
            return WaveSelection.Empty;
        }

        return _document.Selection.IsEmpty
            ? new WaveSelection(0, _document.FrameCount)
            : _document.Selection;
    }

    private bool DeletesWholeFile(WaveSelection range) =>
        _document is not null
        && range.StartFrame <= 0
        && range.EndFrame >= _document.FrameCount
        && !ChannelSamples.IsScoped(EditMask(), _document.Channels);

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        StopRecording();
        if (IsUiBusy)
        {
            e.Cancel = true;
            return;
        }

        if (_exitAfterFlush)
        {
            return;
        }

        e.Cancel = true;
        _closing = true;
        WindowPlacement.SetShellFullscreen(this, false);
        SingleInstance.MarkExiting();
        // 未保存の録音 WAV 書き出しより先に実音を止める。保存を先にすると長く鳴り続ける。
        _player.BeginShutdownFlush();
        if (_videoLaunchPlacement && IsLibraryMaximized && !_playlistVideoFullscreen)
        {
            WindowPlacement.CaptureVideoLaunchPosition(this, AppStorage.Settings);
        }
        else if (IsWaveformMaximized)
        {
            WindowPlacement.Capture(this, AppStorage.Settings, CurrentPlacementKind());
            WindowPlacement.Capture(
                _boundsBeforeWaveformMax,
                _windowStateBeforeWaveformMax == WindowState.Maximized,
                AppStorage.Settings);
        }
        else
        {
            PersistCurrentWindowPlacement();
        }
        HideFromTaskAndFocus();
        StopMeterRendering();
        VectorScope.StopTicks();
        Spectrum.StopTicks();
        _playTimer.Stop();
        _visualPlayheadTimer.Stop();
        StopMarkerNudge();
        StopSpectrogramBoostNudge();
        StopPlaybackShuttle();
        StopSeekNudge();
        StopVideoFrameStep();
        _ = FinishExitAfterFlushAsync();
    }

    private void HideFromTaskAndFocus()
    {
        Topmost = false;
        ShowInTaskbar = false;
        Hide();
    }

    private async Task FinishExitAfterFlushAsync()
    {
        try
        {
            // Hide を描画してからセッション保存。洗い流しは Closing で開始済み。
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            RememberDocumentState();
            AppStorage.Settings.ApplyAudioOutput(_outputSettings);
            AppStorage.Settings.MeterColumnWidth = _meterColumnPreferred;
            PersistWaapiSettings();
            AppStorage.Save();
        }
        catch
        {
            // 保存失敗でもプロセスは終える。
        }

        SingleInstance.ReleaseForHandoff();
        try
        {
            await _player.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // 破棄失敗でもプロセスは終える。
        }

        DisposeAllWaveforms();
        _exitAfterFlush = true;
        Close();
    }

    /// <summary>
    /// ファイル／フォルダのドロップ。F10 / F11 / F12 / 通常表示を問わず同じ。
    /// </summary>
    /// <summary>
    /// 管理者起動時など OLE が拒否されても、WM_DROPFILES 経由で来るドロップ。
    /// </summary>
    private void OnUipiFilesDropped(string[] paths)
    {
        if (paths.Length == 0 || IsUiBusy)
        {
            return;
        }

        // WndProc からでも UI スレッドで処理する。
        Dispatcher.BeginInvoke(() =>
        {
            if (IsUiBusy)
            {
                return;
            }

            var screen = GetCursorScreenPoint();
            if (IsLibraryMaximized)
            {
                if (LibraryBrowser.IsScreenPointOverExplorer(screen))
                {
                    LibraryBrowser.AddExplorerRootPaths(paths);
                    return;
                }

                if (LibraryBrowser.IsScreenPointOverFavorites(screen))
                {
                    LibraryBrowser.AddFavoritePaths(paths);
                    return;
                }
            }

            if (!IsLibraryMaximized)
            {
                OpenPaths(paths);
                return;
            }

            LibraryPlayerMode.ResolvePlaylistFolderDrop(
                DragDropKeyStates.None,
                Keyboard.Modifiers,
                out var clearPlaylist,
                out var recursive);
            OpenPaths(
                paths,
                folderRecursive: recursive,
                clearLibraryPlaylist: clearPlaylist,
                libraryPlay: clearPlaylist && LibraryBrowser.AutoPlayEnabled);
        });
    }

    private static Point GetCursorScreenPoint()
    {
        GetCursorPos(out var p);
        return new Point(p.X, p.Y);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        // ツリー: ルート並び替え／エクスプローラーからのフォルダ登録。
        // Preview で Effects を付けないと禁止カーソルのままになる。
        // Explorer は同ドライブ等で Move のみ許可することがあり、Copy 固定だと禁止になる。
        if (IsLibraryMaximized && LibraryBrowser.IsPointerOverExplorer(e))
        {
            if (LibraryBrowser.TryAcceptExplorerRootReorder(e))
            {
                return;
            }

            e.Effects = CanAcceptDroppedFolders(e)
                ? PickDropEffect(e.AllowedEffects)
                : DragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (IsLibraryMaximized
            && e.OriginalSource is System.Windows.DependencyObject favoritesOrigin
            && LibraryBrowser.IsFavoritesOrigin(favoritesOrigin))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
            return;
        }

        e.Effects = CanAcceptDroppedAudio(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Handled = true;
            return;
        }

        // ツリー: 並び替え、または外部フォルダのルート登録。プレイリストへは足さない。
        if (IsLibraryMaximized && LibraryBrowser.IsPointerOverExplorer(e))
        {
            if (LibraryBrowser.TryDropExplorerRootReorder(e))
            {
                return;
            }

            e.Handled = true;
            if (TryGetDroppedFolders(e, out var folders))
            {
                LibraryBrowser.AddExplorerRootPaths(folders);
            }

            return;
        }

        // お気に入りへのドロップは登録だけ。プレイリストへは足さない。
        if (IsLibraryMaximized
            && e.OriginalSource is System.Windows.DependencyObject favoritesOrigin
            && LibraryBrowser.IsFavoritesOrigin(favoritesOrigin))
        {
            return;
        }

        if (TryGetDroppedAudio(e, out var paths, out var clearPlaylist, out var recursive))
        {
            if (IsLibraryMaximized)
            {
                OpenPaths(
                    paths,
                    folderRecursive: false,
                    clearLibraryPlaylist: clearPlaylist,
                    libraryPlay: clearPlaylist && LibraryBrowser.AutoPlayEnabled);
            }
            else
            {
                OpenPaths(paths);
            }
        }
    }

    private bool CanAcceptDroppedAudio(DragEventArgs e)
    {
        if (!TryReadDroppedPaths(e, out var files))
        {
            return false;
        }

        return IsLibraryMaximized
            ? AudioCodec.CanAcceptPlayerDrop(files)
            : AudioCodec.CanAcceptDrop(files);
    }

    /// <summary>
    /// 登録は複製ではないが、DnD 効果は Copy / Move / Link のどれかがあれば受ける。
    /// </summary>
    private static DragDropEffects PickDropEffect(DragDropEffects allowed)
    {
        if ((allowed & DragDropEffects.Copy) != 0)
        {
            return DragDropEffects.Copy;
        }

        if ((allowed & DragDropEffects.Move) != 0)
        {
            return DragDropEffects.Move;
        }

        if ((allowed & DragDropEffects.Link) != 0)
        {
            return DragDropEffects.Link;
        }

        return DragDropEffects.None;
    }

    private static bool CanAcceptDroppedFolders(DragEventArgs e)
    {
        // DragOver 中の GetData は失敗することがある。FileDrop があれば一旦許可し、Drop でフォルダだけ取る。
        try
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                return true;
            }

            // シェルが FileDrop 以外で渡す場合。
            foreach (var format in e.Data.GetFormats(autoConvert: true))
            {
                if (format.Contains("FileName", StringComparison.OrdinalIgnoreCase)
                    || format.Contains("FileDrop", StringComparison.OrdinalIgnoreCase)
                    || format.Contains("FileGroupDescriptor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryGetDroppedFolders(DragEventArgs e, out string[] folders)
    {
        folders = [];
        if (!TryReadDroppedPaths(e, out var files))
        {
            return false;
        }

        folders = LibraryExplorerPaths.ExistingFolders(files);
        return folders.Length > 0;
    }

    private bool TryGetDroppedAudio(
        DragEventArgs e,
        out string[] paths,
        out bool clearPlaylist,
        out bool recursive)
    {
        paths = [];
        clearPlaylist = true;
        recursive = false;
        if (!TryReadDroppedPaths(e, out var files))
        {
            return false;
        }

        if (IsLibraryMaximized)
        {
            LibraryPlayerMode.ResolvePlaylistFolderDrop(
                e.KeyStates,
                Keyboard.Modifiers,
                out clearPlaylist,
                out recursive);
            // 展開済みパスを返すので、OpenPaths 側の再帰は不要。
            paths = AudioCodec.CollectPlayerOpenable(files, recursive);
        }
        else
        {
            paths = AudioCodec.CollectOpenable(files);
        }

        return paths.Length > 0;
    }

    private static bool TryReadDroppedPaths(DragEventArgs e, out string[] files)
    {
        files = [];
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] dropped
            || dropped.Length == 0)
        {
            return false;
        }

        files = dropped;
        return true;
    }

    private void MeterColumnSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        PersistMeterColumnWidth();
    }

    private void ApplyMeterColumnWidth(double preferred)
    {
        _meterColumnPreferred = DesignMetrics.ClampMeterColumnWidth(preferred);
        // F8／F9 ミニマムではメーター列を空けたままにしない（Bind / SyncMonitorLayout で幅が戻るのを防ぐ）。
        // F9 だけメーター列を畳む。F8 動画ミニはアナライザを出す。
        if (_libraryMinimalChrome && !_videoLaunchPlacement)
        {
            MeterColumnDef.MinWidth = 0;
            MeterColumnDef.MaxWidth = 0;
            MeterColumnDef.Width = new GridLength(0);
            return;
        }

        if (_waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            return;
        }

        var channels = _document?.Channels ?? 2;
        var max = LevelMeterSurroundLayout.FilledColumnWidth(channels);
        var width = Math.Min(_meterColumnPreferred, max);
        var scale = AnalyzerLayoutScale;
        var (colMin, colMax, colWidth) = LibraryPlayerMode.VideoMiniMeterColumnWidths(width, max, scale);
        MeterColumnDef.MinWidth = colMin;
        MeterColumnDef.MaxWidth = colMax;
        MeterColumnDef.Width = new GridLength(colWidth);
        var canResize = max > DesignMetrics.LevelMeterWidth + 0.5;
        MeterColumnSplitter.IsEnabled = canResize;
        MeterColumnSplitter.Cursor = canResize ? Cursors.SizeWE : Cursors.Arrow;
    }

    private double ReadMeterColumnWidth()
    {
        var raw = MeterColumnDef.ActualWidth > 0
            ? MeterColumnDef.ActualWidth
            : MeterColumnDef.Width.Value;
        var scale = AnalyzerLayoutScale;
        if (Math.Abs(scale - 1d) > 0.0001)
        {
            raw /= scale;
        }

        return DesignMetrics.ClampMeterColumnWidth(raw, _document?.Channels ?? 2);
    }

    private void PersistMeterColumnWidth()
    {
        if (_waveformMaximizeMode == WaveformMaximizeMode.Waveform)
        {
            return;
        }

        // F8／F9 中はメーター列幅を触らない（空のデッドスペースになる）。
        if (_libraryMinimalChrome)
        {
            return;
        }

        ApplyMeterColumnWidth(ReadMeterColumnWidth());
        AppStorage.Settings.MeterColumnWidth = _meterColumnPreferred;
        AppStorage.Save();
    }

    private void AlwaysOnTopCheck_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = AlwaysOnTopCheck.IsChecked == true;
        ApplyWindowTopmost();
        if (!IsLoaded)
        {
            return;
        }

        AppStorage.Settings.AlwaysOnTop = enabled;
        AppStorage.Save();
    }

    private void SilentSkipCheck_Changed(object sender, RoutedEventArgs e)
    {
        ApplySilentSkipFromSettings();
        if (!IsLoaded)
        {
            return;
        }

        AppStorage.Settings.SilentSkip = SilentSkipCheck.IsChecked == true;
        AppStorage.Save();
    }

    private void ApplySilentSkipFromSettings()
    {
        var enabled = SilentSkipCheck.IsChecked == true
            && !LibraryPlayerMode.IgnoresSilentSkipForVideo(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualIsVideo);
        var thresholdDb = AppStorage.Settings.ResolvedSilentSkipThresholdDb();
        _player.SetSilentSkip(enabled, thresholdDb);
        _recorder.SetSilentSkip(
            enabled,
            thresholdDb,
            AppStorage.Settings.ResolvedSilentSkipRecordPadMs());
    }

    private void BrandLicenseHost_LinkClick(object sender, BrandLicenseLinkClickEventArgs e)
    {
        var url = e.LinkId switch
        {
            "mit" => AppVersion.LicenseUrl,
            "lame" => AppVersion.LameProjectUrl,
            "ffmpeg" => AppVersion.FfmpegProjectUrl,
            _ => AppVersion.RepositoryUrl,
        };
        TryOpenUrl(url);
        e.Handled = true;
    }

    private void BrandLogo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TryOpenUrl(AppVersion.CompanyUrl);
        e.Handled = true;
    }

    private void LoadBrandLogo()
    {
        try
        {
            var decodeWidth = BrandLogoDecodePixelWidth();
            if (decodeWidth != _brandLogoDecodeWidth)
            {
                _brandLogoDark = null;
                _brandLogoLight = null;
                _brandLogoDecodeWidth = decodeWidth;
            }

            var theme = UiThemeService.Painted;
            var cached = theme == UiTheme.Light ? _brandLogoLight : _brandLogoDark;
            if (cached is null)
            {
                using var logoStream = AppEmbeddedResources.OpenLogo(theme);
                if (logoStream is null)
                {
                    BrandLogo.Visibility = Visibility.Collapsed;
                    return;
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bitmap.DecodePixelWidth = decodeWidth;
                bitmap.StreamSource = logoStream;
                bitmap.EndInit();
                bitmap.Freeze();
                cached = bitmap;
                if (theme == UiTheme.Light)
                {
                    _brandLogoLight = cached;
                }
                else
                {
                    _brandLogoDark = cached;
                }
            }

            RenderOptions.SetBitmapScalingMode(BrandLogo, BitmapScalingMode.HighQuality);
            BrandLogo.Source = cached;
            BrandLogo.Visibility = Visibility.Visible;
            if (cached is BitmapSource bitmapSource
                && BrandLicenseAlign.TryInkFractions(bitmapSource, out _, out var bottomFrac))
            {
                _brandLogoInkBottomFrac = bottomFrac;
            }
            else
            {
                _brandLogoInkBottomFrac = 1;
            }

            QueueAlignBrandLicense();
        }
        catch
        {
            BrandLogo.Visibility = Visibility.Collapsed;
        }
    }

    private void BrandLogo_SizeChanged(object sender, SizeChangedEventArgs e) => QueueAlignBrandLicense();

    private void BrandLicenseHost_SizeChanged(object sender, SizeChangedEventArgs e) => QueueAlignBrandLicense();

    private void QueueAlignBrandLicense() =>
        Dispatcher.BeginInvoke(AlignBrandLicense, DispatcherPriority.Loaded);

    private void AlignBrandLicense()
    {
        if (BrandLogo.Visibility != Visibility.Visible
            || BrandLogo.Source is not BitmapSource source)
        {
            return;
        }

        var dpi = UiDpi.Get(BrandLicenseHost).PixelsPerDip;
        var wwise = BrandLicenseAlign.MeasureLine(
            BrandLicenseHost,
            UiStrings.CopyrightWwiseLine,
            dpi);
        var lineBox = BrandLicenseHost.LineHeight > 0 ? BrandLicenseHost.LineHeight : wwise.Height;
        var wwiseIndex = BrandLicenseAlign.FindLineIndex(
            UiStrings.CopyrightText,
            UiStrings.CopyrightWwiseLine);
        var boxWidth = BrandLogo.ActualWidth > 0 ? BrandLogo.ActualWidth : DesignMetrics.BrandLogoWidth;
        var boxHeight = BrandLogo.ActualHeight > 0 ? BrandLogo.ActualHeight : DesignMetrics.BrandLogoHeight;
        var logoInkBottom = BrandLicenseAlign.LogoInkBottomInBox(
            boxWidth,
            boxHeight,
            source.PixelWidth,
            source.PixelHeight,
            _brandLogoInkBottomFrac);
        var shift = BrandLicenseAlign.LineBaselineShiftY(
            logoInkBottom,
            lineBox,
            wwiseIndex < 0 ? 0 : wwiseIndex,
            wwise.Baseline);
        BrandLicenseHost.RenderTransform = null;
        if (shift >= 0)
        {
            BrandLicenseHost.Margin = new Thickness(0, shift, 0, 0);
            BrandLogo.Margin = new Thickness(0, 0, DesignMetrics.BrandLogoTextGap, 0);
        }
        else
        {
            BrandLicenseHost.Margin = new Thickness(0, 0, 0, 0);
            BrandLogo.Margin = new Thickness(0, -shift, DesignMetrics.BrandLogoTextGap, 0);
        }
    }

    private int BrandLogoDecodePixelWidth()
    {
        var scale = UiDpi.Get(this).DpiScaleX;
        return Math.Max(1, (int)Math.Round(DesignMetrics.BrandLogoWidth * scale));
    }

    private async Task CheckForAppUpdateAsync()
    {
        try
        {
            var update = await GitHubUpdateChecker.TryGetNewerReleaseAsync().ConfigureAwait(true);
            if (_closing || update is null)
            {
                return;
            }

            var remoteSemVer = update.Value.RemoteSemVer;
            var skipped = AppVersion.NormalizeTag(AppStorage.Settings.SkippedUpdateVersion);
            if (skipped.Length > 0
                && string.Equals(skipped, remoteSemVer, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.DialogUpdateAvailableBody(
                    AppVersion.Current,
                    remoteSemVer,
                    update.Value.IsPrerelease),
                UiStrings.DialogUpdateAvailableTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Information,
                MessageBoxResult.Yes);

            if (answer == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(
                        new ProcessStartInfo(update.Value.ReleaseUrl)
                        {
                            UseShellExecute = true,
                        });
                }
                catch (Exception ex)
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        ex.Message,
                        UiStrings.DialogOpenGithubFailed,
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            else
            {
                AppStorage.Settings.SkippedUpdateVersion = remoteSemVer;
                AppStorage.Save();
            }
        }
        catch
        {
            // オフライン・API 制限などは起動を妨げない。
        }
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // ブラウザ起動失敗は無視する。
        }
    }
}
