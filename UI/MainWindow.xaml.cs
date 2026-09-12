using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
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
    private TimeSpan _lastRenderingTime;
    private readonly DispatcherTimer _playTimer;
    private readonly DispatcherTimer _markerDigitTimer;
    private readonly DispatcherTimer _markerNudgeTimer;
    private readonly DispatcherTimer _placeRepeatTimer;
    private PlaceRepeatKind _placeRepeatKind;
    private bool _placeRepeatStarted;
    private bool _placeSessionOpen;
    private MarkerSnapshot[] _placeMarkersBefore = [];
    private WaveRegion[] _placeRegionsBefore = [];
    private int _markerNudgeDirection;
    private bool _nudgeAtPlayhead;
    private bool _nudgeRepeatStarted;
    private bool _timelineNudgeOpen;
    private MarkerSnapshot[] _timelineNudgeMarkersBefore = [];
    private WaveRegion[] _timelineNudgeRegionsBefore = [];
    private WaveSelection _timelineNudgeLoopBefore;
    private int _markerNumber;
    private AudioDocument? _document => _activeSession?.Document;
    private RangeDivideState? _markerDivide;
    private RangeDivideState? _regionDivide;
    private AudioClip? _clipboard;
    private AudioOutputSettings _outputSettings;
    private long _lastPlaybackStart;
    private bool _syncingScroll;
    private bool _syncingChrome;
    private bool _syncingSpeakers;
    private int _waveformHeightScale;
    private double _meterColumnPreferred;
    private int _playbackGeneration;
    private bool _didRestoreLastDocument;
    private TransportIconButton? _waapiToggle;
    private System.Windows.Controls.ContextMenu? _fadeMenu;
    private System.Windows.Controls.ContextMenu? _formatMenu;
    private System.Windows.Controls.ContextMenu? _volumeMenu;
    private System.Windows.Controls.ContextMenu? _pitchMenu;
    private System.Windows.Controls.ContextMenu? _timeStretchMenu;
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
    private bool _startupRevealPending = true;
    private bool _closing;
    private bool _exitAfterFlush;
    private bool _bindingWorkspace;

    public MainWindow()
    {
        InitializeComponent();
        if (!WindowPlacement.TryApply(this, AppStorage.Settings))
        {
            WindowPlacement.ApplyFirstLaunch(this);
        }
        TipService.Enabled = AppStorage.Settings.ShowTips;
        TipService.BindDisplay(TipsLabel, TipsPanel, TipsScroll);
        TipService.PinChanged += (_, _) => RefreshTipsHeader();
        RefreshTipsHeader();
        Transport.SetTipsEnabled(AppStorage.Settings.ShowTips);
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        _waveformHeightScale = Math.Clamp(AppStorage.Settings.WaveformHeightScale, 1, 3);
        ApplyMeterColumnWidth(AppStorage.Settings.MeterColumnWidth);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        AlwaysOnTopCheck.IsChecked = AppStorage.Settings.AlwaysOnTop;
        Topmost = AppStorage.Settings.AlwaysOnTop;

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
        Transport.TipsToggleRequested += (_, _) => ToggleTips();
        Transport.ManualHelpRequested += (_, _) => ManualViewer.Open(this);
        UiStrings.LanguageChanged += (_, _) => Dispatcher.BeginInvoke(RefreshLocalizedText);
        Waveform.CursorCommitted += (_, frame) => OnCursorCommitted(frame);
        Waveform.ScrubStarted += (_, frame) => OnScrubStarted(frame);
        Waveform.ScrubPreviewed += (_, frame) => OnScrubPreviewed(frame);
        Waveform.ScrubEnded += (_, e) => OnScrubEnded(e.Frame, e.Commit);
        Waveform.MarkerCommentCommitted += (_, e) => CommitMarkerComment(e.Frame, e.Comment);
        Waveform.RegionNameCommitted += (_, e) => CommitRegionName(e.Region, e.Name);
        Waveform.TimelineDragStarting += (_, _) =>
        {
            CommitTimelineNudgeSession();
            StopPlaceRepeat();
        };
        Waveform.TimelineLayoutCommitted += (_, e) =>
            CommitTimelineLayout(e.MarkersBefore, e.RegionsBefore, e.LoopBefore);
        Waveform.MarkersChanged += (_, _) =>
        {
            Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
            Overview.Refresh();
        };
        Waveform.SampleLoopClearRequested += (_, _) => ClearSampleLoop();
        Waveform.RegionClearRequested += (_, region) => ClearRegion(region);
        Waveform.MarkerClearRequested += (_, frames) => ClearMarkers(frames);
        Waveform.SelectionChanged += (_, _) => OnWaveformSelectionChanged();
        Waveform.ChannelLabelClicked += (_, e) => ToggleChannelSolo(e.Channel, e.Add, e.Mute);
        Waveform.ViewChanged += (_, _) => SyncViewChrome();
        Overview.ViewStartChanged += (_, start) =>
        {
            Waveform.UnlockCenter();
            ScrubVisibleCenterAt(start);
            Waveform.PanViewStart(start);
        };
        Overview.DragEnded += (_, _) => EndOverviewScrub();
        TimeScroll.ValueChanged += (_, _) =>
        {
            if (_syncingScroll || Waveform.CenterLocked)
            {
                return;
            }

            Waveform.SetViewStartExternal(TimeScroll.Value);
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
        VectorScope.Player = _player;

        // 優先度は Input が唯一安全：Render だと追従描画が入力を飢餓させ操作不能になり
        // （深い拡大のスペクトログラム追従で実際に発生）、Background だと逆に
        // マウス移動がタイマーを飢餓させ再生ヘッドがカクつく。
        // Input はマウス入力と同列 FIFO で処理されるため、どちらの飢餓も起きない。
        ApplyPlayerRoute();
        RefreshSpeakerMenu();
        _recordTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250),
        };
        _recordTimer.Tick += (_, _) => OnRecordTimerTick();
        _playTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _playTimer.Tick += (_, _) => OnPlayTick();
        _markerDigitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _markerDigitTimer.Tick += (_, _) => ResetMarkerDigitEntry();
        _markerNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _markerNudgeTimer.Tick += (_, _) => OnMarkerNudgeTick();
        _placeRepeatTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _placeRepeatTimer.Tick += (_, _) => OnPlaceRepeatTick();

        Deactivated += (_, _) =>
        {
            StopMarkerNudge();
            StopPlaceRepeat();
        };
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PreviewKeyUp += MainWindow_PreviewKeyUp;
        PreviewMouseWheel += MainWindow_PreviewMouseWheel;
        Drop += MainWindow_Drop;
        DragOver += MainWindow_DragOver;
        Closing += MainWindow_Closing;
        SizeChanged += (_, _) => SyncBusyGlassOverlayBounds();
        Closed += (_, _) =>
        {
            StopMeterRendering();
            _playTimer.Stop();
            _recordTimer.Stop();
            _recorder.Dispose();
            _waapiPollTimer.Stop();
            StopMarkerNudge();
            StopPlaceRepeat();
            ResetMarkerDigitEntry();
            // Closing で既に破棄済みでも安全（冪等）。
            Waveform.DisposeSpectrogram();
            _player.Dispose();
        };

        ApplyWaveformHeightScale();
        BindWorkspace(null);
        PlaceWaapiToggle();
        InitializeWaapi();
        RefreshLocalizedText();
        Loaded += OnStartupLoaded;
        ContentRendered += OnStartupContentRendered;
        MgaSonicAnvil.SingleInstance.StartWatch(() =>
            Dispatcher.BeginInvoke(ActivateFromOtherInstance, DispatcherPriority.Send));
    }

    private void ActivateFromOtherInstance()
    {
        if (Opacity < 1)
        {
            RevealStartupWindow();
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

    private void OnStartupLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnStartupLoaded;
        UpdateLayout();
        // 波形復元はせず、空のクロムが描ける状態にしてから表示する。
        Dispatcher.BeginInvoke(RevealStartupWindow, DispatcherPriority.Loaded);
    }

    private void OnStartupContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnStartupContentRendered;
        RevealStartupWindow();
    }

    private void RevealStartupWindow()
    {
        if (!_startupRevealPending)
        {
            return;
        }

        _startupRevealPending = false;
        Opacity = 1;
        if (LaunchFiles.HasStartup)
        {
            ForegroundActivation.BringToFront(this);
        }

        // 表示を先に出し、前回ドキュメントの読み込みは次のアイドルへ回す。
        Dispatcher.BeginInvoke(RestoreLastDocumentAfterReveal, DispatcherPriority.ApplicationIdle);
        Dispatcher.BeginInvoke(() => _ = StartWaapiAsync(), DispatcherPriority.ApplicationIdle);
        _ = CheckForAppUpdateAsync();
    }

    private async void RestoreLastDocumentAfterReveal()
    {
        var launch = MergeLaunchPaths(LaunchFiles.TakeStartup(), SingleInstance.TakePendingPaths());
        await TryRestoreLastDocumentAsync().ConfigureAwait(true);
        if (launch.Length > 0)
        {
            await OpenPathsAsync(launch).ConfigureAwait(true);
        }

        UpdateLayout();
        Waveform.Refresh();
        Overview.InvalidateVisual();
    }

    private void BindWorkspace(DocumentSession? session)
    {
        _bindingWorkspace = true;
        try
        {
            StatusTimes.CancelEdit();
            Overview.CancelDrag();
            if (Waveform.IsScrubbing)
            {
                Waveform.CancelScrub();
            }

            _player.Stop();
            _playTimer.Stop();
            CloseFadeCurvePicker();
            CloseFormatConvertPicker();
            CloseVolumeGainPicker();
            ClosePitchShiftPicker();
            CloseTimeStretchPicker();
            CloseEditHistory(commit: true);
            _resumeAfterScrub = false;
            StopMarkerNudge();
            ResetMarkerDigitEntry();
            StopMeterRendering();
            Waveform.UnlockCenter();
            _activeSession = session;
            Waveform.Document = _document;
            Overview.Document = _document;
            ApplyChannelSolo();
            if (session is not null)
            {
                Waveform.ApplyPersistedView(
                    session.TimeZoom,
                    session.AmpZoom,
                    session.ViewStart,
                    session.PlayheadFrame);
                Waveform.RestoreSelectedMarkers(session.SelectedMarkerFrames);
                Waveform.LoopEnabled = session.LoopEnabled;
                Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
                SyncOverviewPlayhead();
            }
            else
            {
                Overview.SetSelectedMarkerFrames(null);
                Waveform.LoopEnabled = true;
                SyncOverviewPlayhead();
            }

            Transport.SetPlaying(false);
            Transport.SetCommandsEnabled(_document is not null);
            ExtinguishMeter();
            SyncMonitorLayout();
            LoudnessMeter.Reset();
            RebuildTabBar();
            RefreshTitle();
            SyncViewChrome();
            RefreshStatus();
            RefreshHistoryStrip();
        }
        finally
        {
            _bindingWorkspace = false;
        }
    }

    private void ToggleTips()
    {
        var enabled = !AppStorage.Settings.ShowTips;
        AppStorage.Settings.ShowTips = enabled;
        AppStorage.Save();
        TipService.Enabled = enabled;
        Transport.SetTipsEnabled(enabled);
        Waveform.Focus();
    }

    private void TipsPin_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        TipService.TogglePinned();
    }

    private void RefreshTipsHeader()
    {
        TipsHeader.Text = TipService.Pinned ? UiStrings.LabelTipsPinned : UiStrings.LabelTips;
    }

    private void RefreshLocalizedText()
    {
        CopyrightText.Text = UiStrings.CopyrightText;
        GitHubLink.Text = UiStrings.CopyrightGitHub;
        RefreshTipsHeader();
        TipService.Set(GitHubLink, UiStrings.TipGitHub);
        TipService.Set(CopyrightText, UiStrings.TipCopyright);
        TipService.Set(SettingsGear, UiStrings.TipAudioSettings);
        SettingsGear.SetValue(System.Windows.Automation.AutomationProperties.NameProperty, UiStrings.AccessibleAudioSettingsButton);
        AlwaysOnTopCheck.Content = UiStrings.LabelAlwaysOnTop;
        TipService.Set(AlwaysOnTopCheck, UiStrings.TipAlwaysOnTop);
        StatusTimes.ApplyLocalizedText();
        TipService.Set(StatusMeta, UiStrings.TipStatusFormat);
        TipService.Set(Overview, UiStrings.TipOverview);
        TipService.Set(VectorScope, UiStrings.TipVectorScope);
        TipService.Set(Spectrum, UiStrings.TipSpectrum);
        TipService.Set(HistoryStrip, UiStrings.TipHistoryStrip);
        TipService.Set(LoudnessMeter, UiStrings.TipLoudness);
        TipService.Set(LevelMeter, UiStrings.TipLevelMeter);
        TipService.Set(TimeScroll, UiStrings.TipTimeScroll);
        TipService.Set(DocumentTabHost, UiStrings.TipOpen);
        TipService.Set(DocumentTabScroll, UiStrings.TipOpen);
        TipService.Set(DocumentTabs, UiStrings.TipOpen);
        TipService.Set(TipsHeader, UiStrings.TipTips, respectsEnabled: false);
        TipService.Set(TipsScroll, UiStrings.TipTips, respectsEnabled: false);
        TipService.Set(TipsLabel, UiStrings.TipTips, respectsEnabled: false);
        TipService.Set(HistoryOverlay, UiStrings.TipEditHistory);
        TipService.Set(TabScrollLeft, UiStrings.TipTabScrollLeft);
        TipService.Set(TabScrollRight, UiStrings.TipTabScrollRight);
        Transport.ApplyLocalizedTips();
        SpeakerMenuLabel.Text = UiStrings.LabelStatusSpeaker;
        TipService.Set(SpeakerMenuLabel, UiStrings.TipSpeakerSwitch, respectsEnabled: false);
        TipService.Set(SpeakerMenu, UiStrings.TipSpeakerSwitch);
        RefreshSpeakerMenu();
        if (_waapiToggle is not null)
        {
            TipService.Set(_waapiToggle, UiStrings.TipWaapiToggle);
            _waapiToggle.InvalidateVisual();
        }

        Waveform.RefreshLocalizedTips();
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

        RefreshTabLocalizedTips();
        RefreshStatus();
        LevelMeter.InvalidateVisual();
        LoudnessMeter.ApplyLocalizedText();
#if DEBUG
        _colorDevPanel?.ApplyLocalizedText();
#endif
    }

    private void RefreshTitle()
    {
        Title = AppVersion.FormTitle;
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
            _syncingScroll = true;
            TimeScroll.Sync(Waveform.ViewStart, Waveform.ViewSpanFrames, frames);
            _syncingScroll = false;
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

        AppendStatusRun(UiStrings.FormatSampleRate(rate), rate != _document.CommittedSampleRate, normal, edited);
        AppendStatusRun(UiStrings.FormatBitDepth(bits), bits != _document.CommittedBitsPerSample, normal, edited);
        AppendStatusRun(UiStrings.FormatChannels(channels), channels != _document.CommittedChannels, normal, edited);
        AppendStatusRun(kind, edited: false, normal, edited);
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
        StatusTimes.SetState(
            frame ?? Waveform.PlayheadFrame,
            _document?.Selection ?? WaveSelection.Empty,
            _document?.FrameCount ?? 0,
            _document?.SampleRate ?? 0,
            _document is not null);
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
        WindowPlacement.Capture(this, AppStorage.Settings);
        HideFromTaskAndFocus();
        StopMeterRendering();
        VectorScope.StopTicks();
        Spectrum.StopTicks();
        _playTimer.Stop();
        StopMarkerNudge();
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
            // Hide を描画してからセッション保存・デバイス洗い流しに入る。
            await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Render);
            RememberDocumentState();
            AppStorage.Settings.ApplyAudioOutput(_outputSettings);
            AppStorage.Settings.WaveformHeightScale = _waveformHeightScale;
            AppStorage.Settings.MeterColumnWidth = _meterColumnPreferred;
            PersistWaapiSettings();
            AppStorage.Save();
            await _player.DisposeAsync().ConfigureAwait(true);
        }
        catch
        {
            // 破棄失敗でもプロセスは終える。
        }

        Waveform.DisposeSpectrogram();
        _exitAfterFlush = true;
        Close();
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = TryGetDroppedAudio(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (IsUiBusy)
        {
            e.Handled = true;
            return;
        }

        if (TryGetDroppedAudio(e, out var paths))
        {
            OpenPaths(paths);
        }
    }

    private static bool TryGetDroppedAudio(DragEventArgs e, out string[] paths)
    {
        paths = [];
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files
            || files.Length == 0)
        {
            return false;
        }

        paths = files.Where(AudioCodec.IsOpenable).ToArray();
        return paths.Length > 0;
    }

    private void MeterColumnSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        PersistMeterColumnWidth();
    }

    private void ApplyMeterColumnWidth(double preferred)
    {
        _meterColumnPreferred = DesignMetrics.ClampMeterColumnWidth(preferred);
        var channels = _document?.Channels ?? 2;
        var max = LevelMeterSurroundLayout.FilledColumnWidth(channels);
        var width = Math.Min(_meterColumnPreferred, max);
        MeterColumnDef.MinWidth = DesignMetrics.LevelMeterWidth;
        MeterColumnDef.MaxWidth = max;
        MeterColumnDef.Width = new GridLength(width);
        var canResize = max > DesignMetrics.LevelMeterWidth + 0.5;
        MeterColumnSplitter.IsEnabled = canResize;
        MeterColumnSplitter.Cursor = canResize ? Cursors.SizeWE : Cursors.Arrow;
    }

    private double ReadMeterColumnWidth()
    {
        var raw = MeterColumnDef.ActualWidth > 0
            ? MeterColumnDef.ActualWidth
            : MeterColumnDef.Width.Value;
        return DesignMetrics.ClampMeterColumnWidth(raw, _document?.Channels ?? 2);
    }

    private void PersistMeterColumnWidth()
    {
        ApplyMeterColumnWidth(ReadMeterColumnWidth());
        AppStorage.Settings.MeterColumnWidth = _meterColumnPreferred;
        AppStorage.Save();
    }

    private void AlwaysOnTopCheck_Changed(object sender, RoutedEventArgs e)
    {
        var enabled = AlwaysOnTopCheck.IsChecked == true;
        Topmost = enabled;
        if (!IsLoaded)
        {
            return;
        }

        AppStorage.Settings.AlwaysOnTop = enabled;
        AppStorage.Save();
    }

    private void GitHubLink_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        TryOpenUrl(AppVersion.RepositoryUrl);
        e.Handled = true;
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
