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
    private readonly EditHistory _history = new();
    private readonly LevelMeterEngine _meter = new();
    private readonly Stopwatch _meterClock = Stopwatch.StartNew();
    private bool _meterRendering;
    private readonly DispatcherTimer _playTimer;
    private readonly DispatcherTimer _markerDigitTimer;
    private readonly DispatcherTimer _markerNudgeTimer;
    private int _markerNudgeDirection;
    private int _markerNumber;
    private AudioDocument? _document;
    private AudioOutputSettings _outputSettings;
    private long _lastPlaybackStart;
    private bool _syncingScroll;
    private bool _syncingOutputCombos;
    private int _waveformHeightScale;
    private int _playbackGeneration;
    private bool _didRestoreLastDocument;
    private System.Windows.Controls.ContextMenu? _fadeMenu;
    private bool _fadePreviewing;
    private bool _fadePreviewToggling;
    private bool _fadePromptIsIn;
    private long _fadePreviewResumeFrame;
    private long _fadeSpaceTick;
    private bool _resumeAfterScrub;

    public MainWindow()
    {
        InitializeComponent();
        _outputSettings = AppStorage.Settings.ToAudioOutputSettings();
        _waveformHeightScale = Math.Clamp(AppStorage.Settings.WaveformHeightScale, 1, 3);
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        AlwaysOnTopCheck.IsChecked = AppStorage.Settings.AlwaysOnTop;
        Topmost = AppStorage.Settings.AlwaysOnTop;

        Transport.CommandInvoked += (_, command) => ExecuteTransport(command);
        Waveform.CursorCommitted += (_, frame) => OnCursorCommitted(frame);
        Waveform.ScrubStarted += (_, frame) => OnScrubStarted(frame);
        Waveform.ScrubPreviewed += (_, frame) => OnScrubPreviewed(frame);
        Waveform.ScrubEnded += (_, e) => OnScrubEnded(e.Frame, e.Commit);
        Waveform.MarkerCommentCommitted += (_, e) => CommitMarkerComment(e.Frame, e.Comment);
        Waveform.MarkerLayoutCommitted += (_, e) => CommitMarkerLayout(e.Before, e.After);
        Waveform.MarkersChanged += (_, _) =>
        {
            Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
            Overview.Refresh();
        };
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
        _markerNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _markerNudgeTimer.Tick += (_, _) => OnMarkerNudgeTick();

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
            _player.Dispose();
        };

        ApplyWaveformHeightScale();
        SetDocument(null);
        Loaded += (_, _) => TryRestoreLastDocument();
    }

    private void SetDocument(AudioDocument? document)
    {
        _player.Stop();
        _playTimer.Stop();
        CloseFadeCurvePicker();
        _resumeAfterScrub = false;
        StopMarkerNudge();
        ResetMarkerDigitEntry();
        StopMeterRendering();
        Overview.SetSelectedMarkerFrames(null);
        Waveform.UnlockCenter();
        _document = document;
        _history.Clear();
        Waveform.Document = document;
        Overview.Document = document;
        Waveform.LoopEnabled = true;
        Transport.SetPlaying(false);
        Transport.SetCommandsEnabled(document is not null);
        ExtinguishMeter();
        RefreshTitle();
        SyncViewChrome();
        RefreshStatus();
    }

    private void RefreshTitle()
    {
        var name = _document?.SourcePath is { } path
            ? System.IO.Path.GetFileName(path)
            : UiStrings.DropHint;
        if (_document?.IsDirty == true)
        {
            name = "* " + name;
        }

        FileNameText.Text = name;
        Title = AppVersion.FormTitle;
    }

    private void SyncViewChrome()
    {
        var frames = _document?.FrameCount ?? 0;
        _syncingScroll = true;
        TimeScroll.Sync(Waveform.ViewStart, Waveform.ViewSpanFrames, frames);
        _syncingScroll = false;
        Overview.SetView(Waveform.ViewStart, Waveform.ViewSpanFrames);
        Transport.SetPosition(FrameToSeconds(Waveform.PlayheadFrame));
    }

    private void RefreshStatus()
    {
        if (_document is null)
        {
            StatusMeta.Text = UiStrings.StatusEmpty;
            Transport.SetPosition(0);
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
            $"{_document.SampleRate} Hz   {_document.BitsPerSample} bit   {_document.Channels} ch   {kind}   {UiStrings.FormatFileBytes(_document.FileBytes)}   {UiStrings.FormatDuration(_document.DurationSeconds)}   Time {FormatZoom(Waveform.TimeZoom)}   Amp {FormatZoom(Waveform.AmpZoom)}");
        if (!selection.IsEmpty)
        {
            text += string.Create(
                CultureInfo.InvariantCulture,
                $"   Sel {UiStrings.FormatDuration(selection.Length / (double)_document.SampleRate)}");
        }

        StatusMeta.Text = text;
        Transport.SetPosition(FrameToSeconds(Waveform.PlayheadFrame));
        RefreshTitle();
    }

    private static string FormatZoom(double zoom)
    {
        var clamped = Math.Max(1d, zoom);
        if (clamped >= 100 || Math.Abs(clamped - Math.Round(clamped)) < 0.05)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{clamped:0}×");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{clamped:0.0}×");
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
        RememberDocumentState();
        AppStorage.Settings.ApplyAudioOutput(_outputSettings);
        AppStorage.Settings.WaveformHeightScale = _waveformHeightScale;
        AppStorage.Save();
    }

    private void MainWindow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = TryGetDroppedAudio(e, out _) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(object sender, DragEventArgs e)
    {
        if (TryGetDroppedAudio(e, out var path))
        {
            OpenPath(path);
        }
    }

    private static bool TryGetDroppedAudio(DragEventArgs e, out string path)
    {
        path = string.Empty;
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)
            || e.Data.GetData(DataFormats.FileDrop) is not string[] files
            || files.Length == 0)
        {
            return false;
        }

        path = files[0];
        return AudioCodec.IsOpenable(path);
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
