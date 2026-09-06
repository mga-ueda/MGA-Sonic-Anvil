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
    private bool _resumeAfterScrub;
    private bool _startupRevealPending = true;

    public MainWindow()
    {
        InitializeComponent();
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        _waveformHeightScale = Math.Clamp(AppStorage.Settings.WaveformHeightScale, 1, 3);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        AlwaysOnTopCheck.IsChecked = AppStorage.Settings.AlwaysOnTop;
        Topmost = AppStorage.Settings.AlwaysOnTop;

        Transport.CommandInvoked += (_, command) => ExecuteTransport(command);
        Transport.PositionSeeked += (_, seconds) => SeekToSeconds(seconds);
        Transport.RequestWaveformFocus += (_, _) => Waveform.Focus();
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
        Waveform.ViewChanged += (_, _) =>
        {
            SyncViewChrome();
            RefreshStatus();
        };
        Overview.ViewStartChanged += (_, start) => Waveform.SetViewStartExternal(start);
        TimeScroll.ValueChanged += (_, _) =>
        {
            if (_syncingScroll)
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
        PopulateOutputCombos();

        _playTimer = new DispatcherTimer(DispatcherPriority.Render)
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
        Closed += (_, _) =>
        {
            StopMeterRendering();
            _playTimer.Stop();
            StopMarkerNudge();
            ResetMarkerDigitEntry();
            // Closing で既に破棄済みでも安全（冪等）。
            _player.Dispose();
        };

        ApplyWaveformHeightScale();
        BindWorkspace(null);
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
            _startupRevealPending = false;
            Opacity = 1;
        }

        Activate();
        var keepTop = AlwaysOnTopCheck.IsChecked == true;
        Topmost = true;
        Topmost = keepTop;
    }

    private void OnStartupLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnStartupLoaded;
        TryRestoreLastDocument();
        UpdateLayout();
        Waveform.Refresh();
        Overview.InvalidateVisual();
        // Loaded 直後の描画・コンボ反映が終わるまで待ってから表示する。
        Dispatcher.BeginInvoke(RevealStartupWindow, DispatcherPriority.ApplicationIdle);
    }

    private void OnStartupContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= OnStartupContentRendered;
        Dispatcher.BeginInvoke(RevealStartupWindow, DispatcherPriority.ApplicationIdle);
    }

    private void RevealStartupWindow()
    {
        if (!_startupRevealPending)
        {
            return;
        }

        _startupRevealPending = false;
        Opacity = 1;
    }

    private void BindWorkspace(DocumentSession? session)
    {
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
        }
        else
        {
            Overview.SetSelectedMarkerFrames(null);
            Waveform.LoopEnabled = true;
        }

        Transport.SetPlaying(false);
        Transport.SetCommandsEnabled(_document is not null);
        ExtinguishMeter();
        RebuildTabBar();
        RefreshTitle();
        SyncViewChrome();
        RefreshStatus();
    }

    private void RefreshTitle()
    {
        Title = AppVersion.FormTitle;
        RefreshTabHeaders();
    }

    private void SyncViewChrome()
    {
        var frames = _document?.FrameCount ?? 0;
        _syncingScroll = true;
        TimeScroll.Sync(Waveform.ViewStart, Waveform.ViewSpanFrames, frames);
        _syncingScroll = false;
        Overview.SetView(Waveform.ViewStart, Waveform.ViewSpanFrames);
        SyncTransportPosition();
    }

    private void RefreshStatus()
    {
        if (_document is null)
        {
            StatusMeta.Text = string.Empty;
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
        var selection = _document.Selection;
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{_document.SampleRate} Hz   {_document.BitsPerSample} bit   {_document.Channels} ch   {kind}   {UiStrings.FormatFileBytes(_document.FileBytes)}");
        if (!selection.IsEmpty)
        {
            text += string.Create(
                CultureInfo.InvariantCulture,
                $"   Sel {UiStrings.FormatDuration(selection.Length / (double)_document.SampleRate)}");
        }

        StatusMeta.Text = text;
        SyncTransportPosition();
        RefreshTitle();
    }

    private void SyncTransportPosition(long? frame = null)
    {
        var current = FrameToSeconds(frame ?? Waveform.PlayheadFrame);
        Transport.SetPosition(current, _document?.DurationSeconds ?? 0);
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
        if (!OfferSaveAllDirty())
        {
            e.Cancel = true;
            return;
        }

        RememberDocumentState();
        AppStorage.Settings.ApplyAudioOutput(_outputSettings);
        AppStorage.Settings.WaveformHeightScale = _waveformHeightScale;
        AppStorage.Save();

        // メッセージポンプが生きているうちに無音フラッシュ＋デバイス破棄する。
        StopMeterRendering();
        _playTimer.Stop();
        StopMarkerNudge();
        _player.Dispose();
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedAudio(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
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
