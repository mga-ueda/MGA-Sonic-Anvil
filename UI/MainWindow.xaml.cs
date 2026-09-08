using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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
    private readonly List<DocumentSession> _sessions = [];
    private DocumentSession? _activeSession;
    private EditHistory _history = new();
    private readonly LevelMeterEngine _meter = new();
    private readonly Stopwatch _meterClock = Stopwatch.StartNew();
    private bool _meterRendering;
    private TimeSpan _lastRenderingTime;
    private readonly DispatcherTimer _playTimer;
    private readonly DispatcherTimer _markerDigitTimer;
    private readonly DispatcherTimer _markerNudgeTimer;
    private int _markerNudgeDirection;
    private bool _nudgeAtPlayhead;
    private bool _nudgeRepeatStarted;
    private bool _timelineNudgeOpen;
    private MarkerSnapshot[] _timelineNudgeMarkersBefore = [];
    private WaveRegion[] _timelineNudgeRegionsBefore = [];
    private WaveSelection _timelineNudgeLoopBefore;
    private int _markerNumber;
    private AudioDocument? _document;
    private AudioClip? _clipboard;
    private AudioOutputSettings _outputSettings;
    private long _lastPlaybackStart;
    private bool _syncingScroll;
    private bool _syncingChrome;
    private bool _syncingOutputCombos;
    private int _waveformHeightScale;
    private int _playbackGeneration;
    private bool _didRestoreLastDocument;
    private System.Windows.Controls.ContextMenu? _fadeMenu;
    private System.Windows.Controls.ContextMenu? _formatMenu;
    private FormatConvertKind _formatKind;
    private bool _formatPreviewing;
    private bool _formatPreviewToggling;
    private long _formatPreviewResumeFrame;
    private long _formatPreviewStartedAt;
    private long _formatSpaceTick;
    private bool _fadePreviewing;
    private bool _fadePreviewToggling;
    private bool _fadePromptIsIn;
    private long _fadePreviewResumeFrame;
    private long _fadePreviewStartedAt;
    private long _fadeSpaceTick;
    private bool _fadeReplayOnHighlight;
    private bool _resumeAfterScrub;
    private bool _startupRevealPending = true;
    private bool _closing;
    private bool _exitAfterFlush;

    public MainWindow()
    {
        InitializeComponent();
        TipService.Enabled = AppStorage.Settings.ShowTips;
        TipService.BindDisplay(TipsLabel, TipsPanel, TipsScroll);
        TipService.PinChanged += (_, _) => RefreshTipsHeader();
        RefreshTipsHeader();
        Transport.SetTipsEnabled(AppStorage.Settings.ShowTips);
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        _waveformHeightScale = Math.Clamp(AppStorage.Settings.WaveformHeightScale, 1, 3);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        AlwaysOnTopCheck.IsChecked = AppStorage.Settings.AlwaysOnTop;
        Topmost = AppStorage.Settings.AlwaysOnTop;

        Transport.CommandInvoked += (_, command) => ExecuteTransport(command);
        Transport.PositionSeeked += (_, seconds) => SeekToSeconds(seconds);
        Transport.RequestWaveformFocus += (_, _) => Waveform.Focus();
        Transport.LanguageToggleRequested += (_, _) => ToggleUiLanguage();
        Transport.TipsToggleRequested += (_, _) => ToggleTips();
        Transport.ManualHelpRequested += (_, _) => ManualViewer.Open(this);
        UiStrings.LanguageChanged += (_, _) => Dispatcher.BeginInvoke(RefreshLocalizedText);
        Waveform.CursorCommitted += (_, frame) => OnCursorCommitted(frame);
        Waveform.ScrubStarted += (_, frame) => OnScrubStarted(frame);
        Waveform.ScrubPreviewed += (_, frame) => OnScrubPreviewed(frame);
        Waveform.ScrubEnded += (_, e) => OnScrubEnded(e.Frame, e.Commit);
        Waveform.MarkerCommentCommitted += (_, e) => CommitMarkerComment(e.Frame, e.Comment);
        Waveform.RegionNameCommitted += (_, e) => CommitRegionName(e.Region, e.Name);
        Waveform.TimelineDragStarting += (_, _) => CommitTimelineNudgeSession();
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
        Waveform.ViewChanged += (_, _) => SyncViewChrome();
        Overview.ViewStartChanged += (_, start) =>
        {
            Waveform.UnlockCenter();
            Waveform.SetViewStartExternal(start);
            ScrubVisibleCenter();
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
        _player.ApplyOutputSettings(_outputSettings);
        Spectrum.Player = _player;
        VectorScope.Player = _player;
        PopulateOutputCombos();

        // 優先度は Input が唯一安全：Render だと追従描画が入力を飢餓させ操作不能になり
        // （深い拡大のスペクトログラム追従で実際に発生）、Background だと逆に
        // マウス移動がタイマーを飢餓させ再生ヘッドがカクつく。
        // Input はマウス入力と同列 FIFO で処理されるため、どちらの飢餓も起きない。
        _playTimer = new DispatcherTimer(DispatcherPriority.Input)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _playTimer.Tick += (_, _) => OnPlayTick();
        _markerDigitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _markerDigitTimer.Tick += (_, _) => ResetMarkerDigitEntry();
        _markerNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs) };
        _markerNudgeTimer.Tick += (_, _) => OnMarkerNudgeTick();

        Deactivated += (_, _) => StopMarkerNudge();
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
            _waapiPollTimer.Stop();
            StopMarkerNudge();
            ResetMarkerDigitEntry();
            // Closing で既に破棄済みでも安全（冪等）。
            Waveform.DisposeSpectrogram();
            _player.Dispose();
        };

        ApplyWaveformHeightScale();
        BindWorkspace(null);
        InitializeWaapi();
        RefreshLocalizedText();
        Loaded += OnStartupLoaded;
        ContentRendered += OnStartupContentRendered;
        MgaSonicAnvil.SingleInstance.StartWatch(() => Dispatcher.BeginInvoke(ActivateFromOtherInstance));
    }

    private void ActivateFromOtherInstance()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        if (Opacity < 1)
        {
            RevealStartupWindow();
        }

        Activate();
        var keepTop = AlwaysOnTopCheck.IsChecked == true;
        Topmost = true;
        Topmost = keepTop;
        OpenLaunchPaths(SingleInstance.TakePendingPaths());
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
        // 表示を先に出し、前回ドキュメントの読み込みは次のアイドルへ回す。
        Dispatcher.BeginInvoke(RestoreLastDocumentAfterReveal, DispatcherPriority.ApplicationIdle);
        Dispatcher.BeginInvoke(() => _ = StartWaapiAsync(), DispatcherPriority.ApplicationIdle);
        _ = CheckForAppUpdateAsync();
    }

    private void RestoreLastDocumentAfterReveal()
    {
        var launch = MergeLaunchPaths(LaunchFiles.TakeStartup(), SingleInstance.TakePendingPaths());
        if (launch.Length > 0)
        {
            _didRestoreLastDocument = true;
            OpenLaunchPaths(launch);
            return;
        }

        TryRestoreLastDocument();
        UpdateLayout();
        Waveform.Refresh();
        Overview.InvalidateVisual();
    }

    private void BindWorkspace(DocumentSession? session)
    {
        Overview.CancelDrag();
        if (Waveform.IsScrubbing)
        {
            Waveform.CancelScrub();
        }

        _player.Stop();
        _playTimer.Stop();
        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseEditHistory(commit: true);
        _resumeAfterScrub = false;
        StopMarkerNudge();
        ResetMarkerDigitEntry();
        StopMeterRendering();
        Waveform.UnlockCenter();
        _activeSession = session;
        _document = session?.Document;
        _history = session?.History ?? new EditHistory();
        Waveform.Document = _document;
        Overview.Document = _document;
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
        RebuildTabBar();
        RefreshTitle();
        SyncViewChrome();
        RefreshStatus();
    }

    private void ToggleUiLanguage()
    {
        var next = UiStrings.IsJapanese ? UiLanguage.English : UiLanguage.Japanese;
        UiStrings.SetLanguage(next);
        AppStorage.Settings.UiLanguage = UiStrings.ToStoredValue(next);
        AppStorage.Save();
        Waveform.Focus();
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
        AudioApiLabel.Text = UiStrings.LabelAudioApi;
        TipService.Set(AudioApiLabel, UiStrings.TipAudioApi);
        TipService.Set(ApiCombo, UiStrings.TipAudioApi);
        AudioDeviceLabel.Text = UiStrings.LabelAudioDevice;
        TipService.Set(AudioDeviceLabel, UiStrings.TipAudioDevice);
        TipService.Set(DeviceCombo, UiStrings.TipAudioDevice);
        AlwaysOnTopCheck.Content = UiStrings.LabelAlwaysOnTop;
        TipService.Set(AlwaysOnTopCheck, UiStrings.TipAlwaysOnTop);
        TipService.Set(Overview, UiStrings.TipOverview);
        TipService.Set(VectorScope, UiStrings.TipVectorScope);
        TipService.Set(Spectrum, UiStrings.TipSpectrum);
        TipService.Set(TabScrollLeft, UiStrings.TipTabScrollLeft);
        TipService.Set(TabScrollRight, UiStrings.TipTabScrollRight);
        Transport.ApplyLocalizedTips();
        Waveform.RefreshLocalizedTips();
        WaapiBar.ApplyLocalizedText();
        RefreshWaapiStatusDisplay();
        HistoryOverlay.ApplyLocalizedText();
        if (HistoryOpen)
        {
            RefreshHistoryOverlay();
        }

        RefreshTabLocalizedTips();
        RefreshStatus();
        LevelMeter.InvalidateVisual();
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
        if (_document is null)
        {
            StatusMeta.Text = string.Empty;
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
        var name = _activeSession?.DisplayName ?? UiStrings.UntitledDocument;
        var selection = _document.Selection;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{name} {UiStrings.FormatSampleRate(_document.SampleRate)} {UiStrings.FormatBitDepth(_document.BitsPerSample)} {UiStrings.FormatChannels(_document.Channels)} {kind} {UiStrings.FormatFileBytes(_document.FileBytes)}");
        if (_document.FileLastWriteTime is { } stamped)
        {
            text += $" {UiStrings.FormatFileTimestamp(stamped)}";
        }
        if (!selection.IsEmpty)
        {
            text += string.Create(
                CultureInfo.InvariantCulture,
                $" {UiStrings.StatusSelectionPrefix} {UiStrings.FormatDuration(selection.Length / (double)_document.SampleRate)}");
        }

        StatusMeta.Text = text;
        RefreshExportEnabled();
        SyncTransportPosition();
        RefreshTitle();
    }

    private void SyncTransportPosition(long? frame = null)
    {
        var current = FrameToSeconds(frame ?? Waveform.PlayheadFrame);
        Transport.SetPosition(current, _document?.DurationSeconds ?? 0);
    }

    private long VisibleCenterFrame() =>
        (long)Math.Round(Waveform.ViewStart + Waveform.ViewSpanFrames * 0.5);

    private void ScrubVisibleCenter()
    {
        if (_document is null)
        {
            return;
        }

        var frame = VisibleCenterFrame();
        if (Waveform.IsScrubbing)
        {
            Waveform.PreviewScrubAtFrame(frame);
            return;
        }

        Waveform.BeginScrubAtFrame(frame);
    }

    private void EndOverviewScrub()
    {
        if (!Waveform.IsScrubbing)
        {
            return;
        }

        Waveform.EndScrubAtFrame(VisibleCenterFrame(), commit: true);
    }

    private void SeekToSeconds(double seconds)
    {
        if (_document is null)
        {
            return;
        }

        var frame = (long)Math.Round(seconds * _document.SampleRate);
        SeekFrame(frame);
        Waveform.CenterViewOnPlayhead();
    }

    private double FrameToSeconds(long frame) =>
        _document is null || _document.SampleRate <= 0 ? 0 : frame / (double)_document.SampleRate;

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

    private void ConfirmAndExit()
    {
        if (_sessions.Any(session => session.Document.IsDirty))
        {
            Close();
            return;
        }

        var confirm = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.DialogExitBody,
            UiStrings.DialogExitTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (confirm == MessageBoxResult.Yes)
        {
            Close();
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_formatConvertBusy)
        {
            e.Cancel = true;
            return;
        }

        if (!OfferSaveAllDirty())
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
        RememberDocumentState();
        AppStorage.Settings.ApplyAudioOutput(_outputSettings);
        AppStorage.Settings.WaveformHeightScale = _waveformHeightScale;
        PersistWaapiSettings();
        AppStorage.Save();

        HideFromTaskAndFocus();
        StopMeterRendering();
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
        if (_formatConvertBusy)
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
        if (_formatConvertBusy)
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
