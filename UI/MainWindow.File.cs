using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void NewDocument()
    {
        if (IsUiBusy)
        {
            return;
        }

        StopRecording();
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        var settings = AppStorage.Settings;
        var format = NewDocumentWindow.Show(
            this,
            settings.ResolvedLastNewAudioFormat(),
            settings.ResolvedVisibleSpeakerIds());
        if (format is null)
        {
            return;
        }

        settings.RememberNewAudioFormat(format.Value);
        AppStorage.Save();
        var session = CreateEmptyDocument(format.Value);
        ActivateSession(session);
        AfterEdit();
    }

    private DocumentSession CreateEmptyDocument(DefaultAudioFormat.Spec format)
    {
        var document = new AudioDocument(
            [],
            format.SampleRate,
            Math.Max(1, format.Channels),
            format.BitsPerSample,
            AudioFileKind.Wave,
            null);
        var session = new DocumentSession(document);
        _sessions.Add(session);
        return session;
    }

    private void OpenFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterOpenAudio,
            Title = UiStrings.MenuOpen,
            Multiselect = true,
        };
        var initial = ResolveOpenInitialDirectory();
        if (initial.Length > 0)
        {
            dialog.InitialDirectory = initial;
        }
        if (dialog.ShowDialog(this) == true)
        {
            RememberOpenFolder(Path.GetDirectoryName(dialog.FileName));
            OpenPaths(dialog.FileNames);
        }
    }

    private void OpenPath(string path) => OpenPaths([path]);

    private void OpenLaunchPaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        EnterLibraryPlayerIfLaunchHasMp3(paths);
        OpenPaths(paths);
    }

    private void EnterLibraryPlayerIfLaunchHasMp3(IReadOnlyList<string> paths)
    {
        if (IsLibraryMaximized || !LaunchFiles.ContainsMp3(paths))
        {
            return;
        }

        SetWaveformMaximizeMode(WaveformMaximizeMode.Library);
    }

    private static string[] MergeLaunchPaths(params IReadOnlyList<string>[] groups)
    {
        var merged = new List<string>();
        foreach (var group in groups)
        {
            foreach (var path in group)
            {
                if (!string.IsNullOrWhiteSpace(path)
                    && !merged.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    merged.Add(path);
                }
            }
        }

        return merged.ToArray();
    }

    private string? _openStatusText;
    private double _openStatusRatio;
    private readonly List<string> _queuedOpenPaths = [];
    private string[] _openJobNames = [];
    private double[] _openJobProgress = [];
    private DispatcherTimer? _waveformLoadingHint;
    private const int WaveformLoadingHintMs = 300;

    private void OpenPaths(IReadOnlyList<string> paths)
    {
        if (_openBusy)
        {
            QueueOpenPaths(paths);
            return;
        }

        if (IsUiBusy)
        {
            return;
        }

        _ = OpenPathsAsync(paths);
    }

    private void QueueOpenPaths(IReadOnlyList<string> paths)
    {
        foreach (var path in (IsLibraryMaximized
                     ? AudioCodec.CollectPlayerOpenable(paths)
                     : AudioCodec.CollectOpenable(paths)))
        {
            if (_queuedOpenPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _queuedOpenPaths.Add(path);
        }
    }

    private async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var targets = (IsLibraryMaximized
                ? AudioCodec.CollectPlayerOpenable(paths)
                : AudioCodec.CollectOpenable(paths))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            RegisterLibraryPaths(targets);
            if (_queuedOpenPaths.Count > 0)
            {
                var queued = _queuedOpenPaths.ToArray();
                _queuedOpenPaths.Clear();
                await OpenPathsAsync(queued).ConfigureAwait(true);
            }

            return;
        }

        var showProgress = targets.Count > 1;
        DocumentSession? opened = null;
        DocumentSession? existingFirst = null;
        List<string>? errors = null;
        var added = 0;
        BeginOpenWork(targets.Select(path => Path.GetFileName(path) ?? path).ToArray());
        var cancelled = false;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (_openCancelRequested)
                {
                    cancelled = true;
                    break;
                }

                var path = targets[i];
                if (showProgress)
                {
                    SetOpenJobRunning(i);
                }

                var existing = FindSessionByPath(path);
                if (existing is not null)
                {
                    existingFirst ??= existing;
                    if (showProgress)
                    {
                        SetOpenJobDone(i);
                    }

                    continue;
                }

                try
                {
                    var document = await Task.Run(() => AudioCodec.Load(path)).ConfigureAwait(true);
                    var session = new DocumentSession(document);
                    _sessions.Add(session);
                    added++;
                    opened ??= session;
                }
                catch (Exception ex)
                {
                    errors ??= [];
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }

                if (showProgress)
                {
                    SetOpenJobDone(i);
                }
            }

            var active = LaunchFiles.PreferOpened(opened, existingFirst);
            if (active is not null)
            {
                if (!ReferenceEquals(_activeSession, active))
                {
                    ActivateSession(active);
                }
                else if (added > 0)
                {
                    RebuildTabBar();
                }

                if (active.Document.SourcePath is { } openedPath)
                {
                    RememberOpenedPath(openedPath);
                }
            }
            else
            {
                RebuildTabBar();
            }
        }
        finally
        {
            EndOpenWork(showProgress);
        }

        ApplyPreferredMultiFileArrange(added);

        if (errors is { Count: > 0 })
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorOpenFailed}\n{string.Join("\n", errors)}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        if (cancelled)
        {
            _queuedOpenPaths.Clear();
            return;
        }

        if (_queuedOpenPaths.Count > 0)
        {
            var queued = _queuedOpenPaths.ToArray();
            _queuedOpenPaths.Clear();
            await OpenPathsAsync(queued).ConfigureAwait(true);
        }
    }

    private void BeginOpenWork(IReadOnlyList<string> displayNames)
    {
        var targetCount = displayNames.Count;
        _openCancelRequested = false;
        _openBusy = targetCount > 0;
        _openJobNames = displayNames.ToArray();
        _openJobProgress = new double[targetCount];
        RefreshExportEnabled();
        StopWaveformLoadingHint(hide: false);
        if (targetCount <= 0)
        {
            return;
        }

        if (targetCount > 1)
        {
            ShowOpenBusyGlass();
            ApplyOpenBusyGlass();
            return;
        }

        _waveformLoadingHint = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(WaveformLoadingHintMs),
        };
        _waveformLoadingHint.Tick += OnWaveformLoadingHint;
        _waveformLoadingHint.Start();
    }

    private void OnWaveformLoadingHint(object? sender, EventArgs e)
    {
        StopWaveformLoadingHint(hide: false);
        Waveform.SetLoadingVisible(true);
    }

    private void StopWaveformLoadingHint(bool hide)
    {
        if (_waveformLoadingHint is not null)
        {
            _waveformLoadingHint.Stop();
            _waveformLoadingHint.Tick -= OnWaveformLoadingHint;
            _waveformLoadingHint = null;
        }

        if (hide)
        {
            Waveform.SetLoadingVisible(false);
        }
    }

    private void EndOpenWork(bool showProgress)
    {
        _openBusy = false;
        _openCancelRequested = false;
        StopWaveformLoadingHint(hide: true);
        _openJobNames = [];
        _openJobProgress = [];
        if (showProgress)
        {
            ClearOpenStatus();
            _busyGlass.BeginFadeOut();
        }
        else
        {
            RefreshExportEnabled();
        }
    }

    private void SetOpenJobRunning(int index)
    {
        if ((uint)index >= (uint)_openJobProgress.Length)
        {
            return;
        }

        for (var i = 0; i < index; i++)
        {
            _openJobProgress[i] = 1;
        }

        _openJobProgress[index] = Math.Max(_openJobProgress[index], 0.08);
        ApplyOpenBusyGlass();
    }

    private void SetOpenJobDone(int index)
    {
        if ((uint)index >= (uint)_openJobProgress.Length)
        {
            return;
        }

        _openJobProgress[index] = 1;
        ApplyOpenBusyGlass();
    }

    private void ApplyOpenBusyGlass()
    {
        if (!_busyGlass.IsShowingBusy || _openJobNames.Length == 0)
        {
            return;
        }

        var finished = 0;
        var jobs = new ExportJobProgress[_openJobNames.Length];
        for (var i = 0; i < _openJobNames.Length; i++)
        {
            var progress = i < _openJobProgress.Length ? _openJobProgress[i] : 0;
            if (progress >= 1)
            {
                finished++;
            }

            jobs[i] = new ExportJobProgress(_openJobNames[i], progress);
        }

        var overall = jobs.Length == 0
            ? 0
            : jobs.Sum(job => job.Progress) / jobs.Length;
        _busyGlass.SetExportView(
            overall,
            UiStrings.OverlayExportCount(finished, jobs.Length),
            jobs);
    }

    private void ClearOpenStatus()
    {
        _openStatusText = null;
        _openStatusRatio = 0;
        RefreshStatus();
    }

    private bool TryRequestOpenCancel()
    {
        if (!_openBusy || _openJobNames.Length <= 1)
        {
            return false;
        }

        _openCancelRequested = true;
        return true;
    }

    private bool Save(bool saveAs, AudioDocument? target = null)
    {
        if (target is not null)
        {
            return SaveDocument(saveAs, target);
        }

        var sessions = HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null ? [] : [_activeSession];
        if (sessions.Length >= 2)
        {
            _ = SaveSessionsAsync(sessions, saveAs);
            return true;
        }

        return SaveDocument(saveAs, sessions.Length == 1 ? sessions[0].Document : _document);
    }

    private bool SaveDocument(bool saveAs, AudioDocument? document)
    {
        if (document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var path = document.SourcePath;
        if (saveAs || !AudioSave.CanOverwrite(path))
        {
            var dialog = new SaveFileDialog
            {
                Filter = UiStrings.FilterSaveAudio,
                Title = UiStrings.MenuSaveAs,
                FileName = string.IsNullOrEmpty(path)
                    ? "untitled.wav"
                    : Path.GetFileNameWithoutExtension(path) + ".wav",
                InitialDirectory = ResolveExportInitialDirectory(path),
            };
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
            RememberExportFolder(Path.GetDirectoryName(path));
        }

        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        try
        {
            if (AudioCodec.DetectKind(path) == AudioFileKind.Mp3
                && !SameDocumentPath(document.SourcePath, path))
            {
                return ExportMp3WithoutReloading(document, path);
            }

            var encoder = AudioCodec.Save(
                document,
                path,
                AppStorage.Settings.ToMp3EncodeOptions(),
                AppStorage.Settings.ToMp3SpeakerMix());
            document.MarkSaved(path, AudioCodec.DetectKind(path));
            var history = _sessions.FirstOrDefault(item => ReferenceEquals(item.Document, document))?.History
                ?? (ReferenceEquals(document, _document) ? _history : null);
            history?.MarkClean();
            if (ReferenceEquals(document, _document))
            {
                RememberOpenedPath(path);
            }

            RefreshTitle();
            RefreshStatus();
            if (encoder is { } used)
            {
                ShowMp3EncoderResult(used);
            }

            return true;
        }
        catch (NotSupportedException)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorAiffExport, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, $"{UiStrings.ErrorSaveFailed}\n{ex.Message}", UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        return false;
    }

    private async Task SaveSessionsAsync(IReadOnlyList<DocumentSession> sessions, bool saveAs)
    {
        if (IsUiBusy)
        {
            return;
        }

        if (sessions.Count == 0)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryPlanSaveJobs(sessions, saveAs, out var jobs) || jobs.Count == 0)
        {
            return;
        }

        var overwrite = ConfirmNewPathOverwrites(jobs);
        if (overwrite == MessageBoxResult.Cancel)
        {
            return;
        }

        if (overwrite == MessageBoxResult.No)
        {
            jobs.RemoveAll(job => File.Exists(job.Path) && !SameDocumentPath(job.Document.SourcePath, job.Path));
            if (jobs.Count == 0)
            {
                return;
            }
        }

        StopPlaybackForExport();
        var options = AppStorage.Settings.ToMp3EncodeOptions();
        var mix = AppStorage.Settings.ToMp3SpeakerMix();
        var outcomes = new ExportOutcome[jobs.Count];
        var anyMp3 = jobs.Any(job => AudioCodec.DetectKind(job.Path) == AudioFileKind.Mp3);
        var parallelism = anyMp3 && !Mp3Encode.UsesLame(options.LameExePath)
            ? 1
            : AudioExport.WorkerCount(jobs.Count, AppStorage.Settings.ExportParallelism);
        var tracker = new ExportProgressTracker(
            jobs.Select(job => job.Name).ToArray(),
            jobs.Select(job => job.ExportFrameCount).ToArray(),
            new Progress<ExportProgressSnapshot>(ApplyExportProgress));

        _tabExportBusy = true;
        try
        {
            ShowSaveBusyGlass();
            ApplyExportProgress(tracker.Capture());
            await Task.Run(() =>
            {
                Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
                {
                    var job = jobs[i];
                    tracker.Report(i, 0, ExportJobState.Running);
                    try
                    {
                        outcomes[i] = new ExportOutcome(
                            AudioCodec.Save(job.Document, job.Path, options, mix),
                            null);
                        tracker.Report(i, 1, ExportJobState.Done);
                    }
                    catch (Exception ex)
                    {
                        tracker.Report(i, 1, ExportJobState.Failed);
                        outcomes[i] = new ExportOutcome(null, ex);
                    }
                });
            }).ConfigureAwait(true);
        }
        finally
        {
            _tabExportBusy = false;
            _busyGlass.HideOverlay();
        }

        var failed = 0;
        var errors = new StringBuilder();
        Mp3EncoderKind? encoder = null;
        for (var i = 0; i < outcomes.Length; i++)
        {
            var outcome = outcomes[i];
            if (outcome.Error is { } ex)
            {
                failed++;
                if (errors.Length > 0)
                {
                    errors.AppendLine();
                }

                errors.Append(jobs[i].Name).Append(": ").Append(ex.Message);
                continue;
            }

            var job = jobs[i];
            var destKind = AudioCodec.DetectKind(job.Path);
            if (destKind == AudioFileKind.Mp3 && !SameDocumentPath(job.Document.SourcePath, job.Path))
            {
                encoder ??= outcome.Encoder;
                continue;
            }

            job.Document.MarkSaved(job.Path, destKind);
            var session = _sessions.FirstOrDefault(item => ReferenceEquals(item.Document, job.Document));
            session?.History.MarkClean();
            if (ReferenceEquals(job.Document, _document))
            {
                RememberOpenedPath(job.Path);
            }

            encoder ??= outcome.Encoder;
        }

        RebuildTabBar();
        RefreshTitle();
        RefreshStatus();
        if (failed > 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorSaveFailed}\n{errors}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        if (encoder is { } used)
        {
            ShowMp3EncoderResult(used);
        }
    }

    private bool TryPlanSaveJobs(IReadOnlyList<DocumentSession> sessions, bool saveAs, out List<ExportJob> jobs)
    {
        jobs = [];
        if (saveAs)
        {
            if (!TryPickExportFolder(sessions, out var folder, UiStrings.SaveFolderTitle))
            {
                return false;
            }

            var paths = AudioSave.PlanFolderWavePaths(
                sessions.Select(session => (session.Document.SourcePath, session.DisplayName)).ToArray(),
                folder);
            for (var i = 0; i < sessions.Count; i++)
            {
                jobs.Add(new ExportJob(sessions[i].DisplayName, paths[i], sessions[i].Document));
            }

            return true;
        }

        var needFolder = new List<DocumentSession>();
        foreach (var session in sessions)
        {
            if (AudioSave.CanOverwrite(session.Document.SourcePath))
            {
                jobs.Add(new ExportJob(session.DisplayName, session.Document.SourcePath!, session.Document));
            }
            else
            {
                needFolder.Add(session);
            }
        }

        if (needFolder.Count == 0)
        {
            return true;
        }

        if (!TryPickExportFolder(needFolder, out var destFolder, UiStrings.SaveFolderTitle))
        {
            return false;
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var job in jobs)
        {
            reserved.Add(Path.GetFullPath(job.Path));
        }

        var planned = AudioSave.PlanFolderWavePaths(
            needFolder.Select(session => (session.Document.SourcePath, session.DisplayName)).ToArray(),
            destFolder,
            reserved);
        for (var i = 0; i < needFolder.Count; i++)
        {
            jobs.Add(new ExportJob(needFolder[i].DisplayName, planned[i], needFolder[i].Document));
        }

        return true;
    }

    private MessageBoxResult ConfirmNewPathOverwrites(List<ExportJob> jobs)
    {
        var existing = jobs
            .Where(job => File.Exists(job.Path) && !SameDocumentPath(job.Document.SourcePath, job.Path))
            .Select(job => Path.GetFileName(job.Path))
            .ToArray();
        if (existing.Length == 0)
        {
            return MessageBoxResult.Yes;
        }

        return OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmOverwriteFiles(existing.Length, AudioExport.FormatOverwritePreview(existing)),
            UiStrings.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    private void ShowSaveBusyGlass()
    {
        RootChrome.UpdateLayout();
        RootDock.UpdateLayout();
        _busyGlass.ShowOverlay(
            RootChrome,
            RootDock,
            GetBusyGlassCoverBounds(),
            UiStrings.OverlaySave);
    }

    private bool SaveAsMp3(AudioDocument? target = null)
    {
        _ = SaveAsMp3Async(target);
        return true;
    }

    private async Task SaveAsMp3Async(AudioDocument? target)
    {
        var document = target ?? _document;
        if (document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (IsUiBusy)
        {
            return;
        }

        var path = document.SourcePath;
        var dialog = new SaveFileDialog
        {
            Filter = UiStrings.FilterSaveMp3,
            Title = UiStrings.MenuSaveMp3,
            FileName = string.IsNullOrEmpty(path)
                ? "untitled.mp3"
                : Path.GetFileNameWithoutExtension(path) + ".mp3",
            InitialDirectory = ResolveExportInitialDirectory(path),
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        RememberExportFolder(Path.GetDirectoryName(dialog.FileName));
        await ExportSingleMp3WithGlassAsync(document, dialog.FileName);
    }

    private async Task ExportSingleMp3WithGlassAsync(AudioDocument document, string path)
    {
        if (IsUiBusy)
        {
            return;
        }

        StopPlaybackForExport();
        var options = AppStorage.Settings.ToMp3EncodeOptions();
        var name = Path.GetFileName(path);
        var tracker = new ExportProgressTracker(
            [name],
            [document.FrameCount],
            new Progress<ExportProgressSnapshot>(ApplyExportProgress));

        _tabExportBusy = true;
        try
        {
            ShowExportBusyGlass(AudioFileKind.Mp3);
            ApplyExportProgress(tracker.Capture());
            var encoder = await Task.Run(() =>
            {
                tracker.Report(0, 0, ExportJobState.Running);
                try
                {
                    var used = AudioCodec.SaveMp3(
                        document,
                        path,
                        options,
                        new Progress<double>(p => tracker.Report(0, p, ExportJobState.Running)),
                        AppStorage.Settings.ToMp3SpeakerMix());
                    tracker.Report(0, 1, ExportJobState.Done);
                    return used;
                }
                catch
                {
                    tracker.Report(0, 1, ExportJobState.Failed);
                    throw;
                }
            }).ConfigureAwait(true);

            _busyGlass.HideOverlay();
            ShowMp3EncoderResult(encoder);
        }
        catch (Exception ex)
        {
            _busyGlass.HideOverlay();
            OwnerCenteredMessageBox.Show(this, $"{UiStrings.ErrorSaveFailed}\n{ex.Message}", UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _tabExportBusy = false;
        }
    }

    private bool ExportMp3WithoutReloading(AudioDocument document, string path)
    {
        var encoder = AudioCodec.SaveMp3(
            document,
            path,
            AppStorage.Settings.ToMp3EncodeOptions(),
            mix: AppStorage.Settings.ToMp3SpeakerMix());
        ShowMp3EncoderResult(encoder);
        return true;
    }

    private static bool SameDocumentPath(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void CloseDocument()
    {
        if (_activeSession is null)
        {
            return;
        }

        CloseSession(_activeSession);
    }

    private bool TryCommitFileName(DocumentSession session, string typedName)
    {
        var document = session.Document;
        var fallbackDir = ResolveOpenInitialDirectory();
        if (!DocumentFileNames.TryBuildRenamePath(
            document.SourcePath,
            typedName,
            fallbackDir,
            ".wav",
            out var destination,
            out var error))
        {
            ShowFileNameError(error);
            return false;
        }

        if (document.SourcePath is { } source
            && DocumentFileNames.IsSamePath(source, destination)
            && string.Equals(Path.GetFileName(source), Path.GetFileName(destination), StringComparison.Ordinal))
        {
            return true;
        }

        var opened = FindSessionByPath(destination);
        if (opened is not null && !ReferenceEquals(opened, session))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorFileNameOpen,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        try
        {
            if (document.SourcePath is { } path && File.Exists(path))
            {
                if (File.Exists(destination) && !DocumentFileNames.IsSamePath(path, destination))
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorFileNameExists,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }

                if (IsPlaybackActive() && ReferenceEquals(session, _activeSession))
                {
                    StopPlayback();
                }

                MoveDocumentFile(path, destination);
                document.SourcePath = destination;
                document.RefreshFileBytes();
            }
            else
            {
                if (File.Exists(destination))
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorFileNameExists,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return false;
                }

                var folder = Path.GetDirectoryName(destination);
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return Save(saveAs: true, document);
                }

                var encoder = AudioCodec.Save(
                    document,
                    destination,
                    AppStorage.Settings.ToMp3EncodeOptions(),
                    AppStorage.Settings.ToMp3SpeakerMix());
                document.MarkSaved(destination, AudioCodec.DetectKind(destination));
                session.History.MarkClean();
                if (encoder is { } used)
                {
                    ShowMp3EncoderResult(used);
                }
            }

            if (ReferenceEquals(session, _activeSession) && document.SourcePath is { } saved)
            {
                RememberOpenedPath(saved);
            }

            RefreshTitle();
            RefreshStatus();
            return true;
        }
        catch (NotSupportedException)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorAiffExport,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorRenameFailed}\n{ex.Message}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        return false;
    }

    private static void MoveDocumentFile(string source, string destination)
    {
        if (DocumentFileNames.IsCaseOnlyChange(source, destination))
        {
            var dir = Path.GetDirectoryName(source) ?? Path.GetTempPath();
            var temp = Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            File.Move(source, temp);
            File.Move(temp, destination);
            return;
        }

        File.Move(source, destination);
    }

    private void ShowFileNameError(DocumentFileNameError error)
    {
        var text = error == DocumentFileNameError.Empty
            ? UiStrings.ErrorFileNameEmpty
            : UiStrings.ErrorFileNameInvalid;
        OwnerCenteredMessageBox.Show(
            this,
            text,
            UiStrings.AppName,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private bool ConfirmFileAction(string text, MessageBoxImage icon, MessageBoxResult defaultResult)
    {
        OwnerCenteredMessageBox.PlayFor(icon);
        return ConfirmChoiceWindow.Show(
            this,
            text,
            UiStrings.AppName,
            MessageBoxButton.YesNo,
            defaultResult) == MessageBoxResult.Yes;
    }

    private void DuplicateSession(DocumentSession session)
    {
        if (IsUiBusy)
        {
            return;
        }

        string? destPath = null;
        var confirm = UiStrings.ConfirmDuplicateFile(session.DisplayName);
        if (session.Document.SourcePath is { } path && File.Exists(path))
        {
            destPath = UniqueCopyPath(path);
            confirm = UiStrings.ConfirmDuplicateFileAs(session.DisplayName, Path.GetFileName(destPath));
        }

        if (!ConfirmFileAction(confirm, MessageBoxImage.Question, MessageBoxResult.Yes))
        {
            return;
        }

        if (IsRecording && ReferenceEquals(session, _recordSession))
        {
            StopRecording();
        }

        if (IsPlaybackActive() && ReferenceEquals(session, _activeSession))
        {
            StopPlayback();
        }

        try
        {
            var copy = CreateDuplicateSession(session, destPath);
            CopyViewState(session, copy);
            InsertSessionAdjacent(session, copy);
            if (copy.Document.SourcePath is { } opened)
            {
                RememberOpenedPath(opened);
            }
        }
        catch (NotSupportedException)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorAiffExport,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorDuplicateFailed}\n{ex.Message}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>選択中のタブをまとめて複製する。確認ダイアログは 1 回にまとめる。</summary>
    private void DuplicateSessions(IReadOnlyList<DocumentSession> sessions)
    {
        if (IsUiBusy || sessions.Count == 0)
        {
            return;
        }

        if (sessions.Count == 1)
        {
            DuplicateSession(sessions[0]);
            return;
        }

        if (!ConfirmFileAction(
            UiStrings.ConfirmDuplicateSelectedFiles(sessions.Count),
            MessageBoxImage.Question,
            MessageBoxResult.Yes))
        {
            return;
        }

        if (IsRecording && _recordSession is { } recording && sessions.Contains(recording))
        {
            StopRecording();
        }

        if (IsPlaybackActive() && _activeSession is { } active && sessions.Contains(active))
        {
            StopPlayback();
        }

        foreach (var session in sessions)
        {
            try
            {
                var copy = CreateDuplicateSession(session, destPath: null);
                CopyViewState(session, copy);
                InsertSessionAdjacent(session, copy);
                if (copy.Document.SourcePath is { } opened)
                {
                    RememberOpenedPath(opened);
                }
            }
            catch (NotSupportedException)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorAiffExport,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            catch (Exception ex)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorDuplicateFailed}\n{ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }
        }
    }

    private DocumentSession CreateDuplicateSession(DocumentSession session, string? destPath)
    {
        var document = session.Document;
        if (document.SourcePath is { } path && File.Exists(path))
        {
            var dest = destPath ?? UniqueCopyPath(path);
            if (!document.IsDirty)
            {
                File.Copy(path, dest);
            }
            else if (AudioCodec.DetectKind(path) == AudioFileKind.Aiff)
            {
                return new DocumentSession(document.CopyWorking());
            }
            else
            {
                AudioCodec.Save(
                    document,
                    dest,
                    AppStorage.Settings.ToMp3EncodeOptions(),
                    AppStorage.Settings.ToMp3SpeakerMix());
            }

            return new DocumentSession(AudioCodec.Load(dest));
        }

        return new DocumentSession(document.CopyWorking());
    }

    private string UniqueCopyPath(string sourcePath)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        return UniquePathInDirectory(
            dir,
            Path.GetFileNameWithoutExtension(sourcePath),
            Path.GetExtension(sourcePath));
    }

    private string UniquePathInDirectory(string? directory, string baseName, string extension)
    {
        var dir = directory;
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            dir = ResolveOpenInitialDirectory();
        }

        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            dir = Path.GetTempPath();
        }

        return AudioExport.UniqueInDirectory(
            dir,
            baseName,
            extension,
            CollectReservedDocumentPaths(),
            skipExistingFiles: true);
    }

    private HashSet<string> CollectReservedDocumentPaths()
    {
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _sessions)
        {
            if (item.Document.SourcePath is { } path
                && DocumentWorkspace.TryNormalizePath(path, out var full))
            {
                reserved.Add(full);
            }
        }

        return reserved;
    }

    private static void CopyViewState(DocumentSession from, DocumentSession to)
    {
        to.TimeZoom = from.TimeZoom;
        to.AmpZoom = from.AmpZoom;
        to.ViewStart = from.ViewStart;
        to.PlayheadFrame = from.PlayheadFrame;
        to.LoopEnabled = from.LoopEnabled;
        to.AnalysisView = from.AnalysisView;
        to.SoloMask = from.SoloMask;
        to.SelectedMarkerFrames.Clear();
        to.SelectedMarkerFrames.AddRange(from.SelectedMarkerFrames);
    }

    private void InsertSessionAdjacent(DocumentSession source, DocumentSession copy)
    {
        var index = _sessions.IndexOf(source);
        InsertSessionAt(index < 0 ? _sessions.Count : index + 1, copy);
    }

    private void InsertSessionAt(int insertAt, DocumentSession session)
    {
        insertAt = Math.Clamp(insertAt, 0, _sessions.Count);
        _sessions.Insert(insertAt, session);

        // タイル検索フィルターにヒットしない新しいタブ（バウンス結果など）は前面化しない。
        // アクティブにするとハイライトされて選択状態に見えるうえ、すりガラスの下の
        // 操作できないタイルがアクティブになってしまう。すりガラス側に並べるだけにする。
        if (IsTileSearchVeiled(session))
        {
            ClearTabSelection();
            RebuildTabBar();
            RefreshStatus();
            ApplyPreferredMultiFileArrange(1);
            return;
        }

        ActivateSession(session);
        ApplyPreferredMultiFileArrange(1);
    }

    private void MergeSelectedTabs() => _ = MergeSelectedTabsAsync();

    private async Task MergeSelectedTabsAsync()
    {
        if (IsUiBusy || IsRecording)
        {
            return;
        }

        var sources = SelectedTabsInOrder();
        if (sources.Length < 2)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = UiStrings.FilterSaveWave,
            Title = UiStrings.TitleMergeTabs,
            FileName = TabMerge.MergedBaseName + ".wav",
            OverwritePrompt = true,
            InitialDirectory = ResolveMergeInitialDirectory(sources),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var destPath = dialog.FileName;
        RememberExportFolder(Path.GetDirectoryName(destPath));
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        _tabMergeBusy = true;
        RefreshExportEnabled();
        try
        {
            ShowBusyGlass(UiStrings.OverlayMerge);
            var documents = sources.Select(session => session.Document).ToArray();
            var progress = new Progress<double>(value => _busyGlass.SetProgress(value));
            var merged = await Task.Run(() => TabMerge.Mix(documents, progress))
                .ConfigureAwait(true);
            await Task.Run(() => AudioCodec.SaveWave(merged, destPath, progress))
                .ConfigureAwait(true);
            merged.MarkSaved(destPath, AudioFileKind.Wave);

            var selectedIndices = sources.Select(session => _sessions.IndexOf(session)).ToArray();
            var insertAt = TabMerge.InsertIndex(_sessions.Count, selectedIndices);
            InsertSessionAt(insertAt, new DocumentSession(merged));
            if (merged.SourcePath is { } opened)
            {
                RememberOpenedPath(opened);
            }

            _busyGlass.BeginFadeOut();
        }
        catch (Exception ex)
        {
            _busyGlass.HideOverlay();
            TryDeleteAbandonedMergeFile(destPath);
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorMergeFailed}\n{ex.Message}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _tabMergeBusy = false;
            RefreshExportEnabled();
        }
    }

    /// <summary>保存ダイアログの初期フォルダ。ファイルを持つ最初の選択タブ、無ければ前回の書き出し先。</summary>
    private static string ResolveMergeInitialDirectory(IReadOnlyList<DocumentSession> sources)
    {
        foreach (var session in sources)
        {
            if (session.Document.SourcePath is { } path
                && File.Exists(path)
                && Path.GetDirectoryName(path) is { Length: > 0 } directory)
            {
                return directory;
            }
        }

        return ResolveExportInitialDirectory((string?)null);
    }

    private void TryDeleteAbandonedMergeFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !File.Exists(path)
            || !DocumentWorkspace.TryNormalizePath(path, out var full)
            || CollectReservedDocumentPaths().Contains(full))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void DeleteSessionFile(DocumentSession session)
    {
        if (IsUiBusy)
        {
            return;
        }

        var path = session.Document.SourcePath;
        var fromDisk = !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        var confirm = fromDisk
            ? UiStrings.ConfirmDeleteFile(session.DisplayName)
            : UiStrings.ConfirmDeleteUntitled(session.DisplayName);
        if (!ConfirmFileAction(confirm, MessageBoxImage.Warning, MessageBoxResult.No))
        {
            return;
        }

        if (!fromDisk)
        {
            session.Document.SetDirty(false);
            CloseSession(session, rememberClosed: false);
            return;
        }

        if (IsRecording && ReferenceEquals(session, _recordSession))
        {
            StopRecording();
        }

        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        try
        {
            File.Delete(path!);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorDeleteFailed}\n{ex.Message}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        session.Document.SetDirty(false);
        session.Document.SourcePath = null;
        CloseSession(session, rememberClosed: false);
    }

    /// <summary>選択中のタブのファイルをまとめて削除する。確認は 1 回、失敗はまとめて表示する。</summary>
    private void DeleteSessionFiles(IReadOnlyList<DocumentSession> sessions)
    {
        if (IsUiBusy || sessions.Count == 0)
        {
            return;
        }

        if (sessions.Count == 1)
        {
            DeleteSessionFile(sessions[0]);
            return;
        }

        if (!ConfirmFileAction(
            UiStrings.ConfirmDeleteSelectedFiles(sessions.Count),
            MessageBoxImage.Warning,
            MessageBoxResult.No))
        {
            return;
        }

        if (IsRecording && _recordSession is { } recording && sessions.Contains(recording))
        {
            StopRecording();
        }

        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        var failed = new List<string>();
        foreach (var session in sessions)
        {
            var path = session.Document.SourcePath;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    failed.Add(session.DisplayName);
                    continue;
                }

                session.Document.SourcePath = null;
            }

            session.Document.SetDirty(false);
            CloseSession(session, rememberClosed: false);
        }

        if (failed.Count > 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorDeleteFailed}\n{string.Join(Environment.NewLine, failed)}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private enum DirtyClosePolicy
    {
        Ask,
        SaveAll,
        DiscardAll,
    }

    private DirtyClosePolicy _dirtyClosePolicy;
    private IReadOnlyList<DocumentSession>? _closingBatch;

    private bool OfferSaveIfDirty() => OfferSaveIfDirty(_activeSession);

    private bool OfferSaveIfDirty(DocumentSession? session)
    {
        if (session?.Document is not { IsDirty: true } document)
        {
            return true;
        }

        if (_dirtyClosePolicy == DirtyClosePolicy.DiscardAll)
        {
            return true;
        }

        if (_dirtyClosePolicy == DirtyClosePolicy.SaveAll)
        {
            return Save(saveAs: false, document);
        }

        var remaining = CountRemainingDirtyInCloseBatch();
        var choice = remaining >= 2
            ? ConfirmSaveWindow.Show(this, session.DisplayName, remaining)
            : MapSaveMessageBox(session.DisplayName);
        switch (choice)
        {
            case ConfirmSaveChoice.Cancel:
                return false;
            case ConfirmSaveChoice.Discard:
                return true;
            case ConfirmSaveChoice.DiscardAll:
                _dirtyClosePolicy = DirtyClosePolicy.DiscardAll;
                return true;
            case ConfirmSaveChoice.SaveAll:
                _dirtyClosePolicy = DirtyClosePolicy.SaveAll;
                return Save(saveAs: false, document);
            default:
                return Save(saveAs: false, document);
        }
    }

    private ConfirmSaveChoice MapSaveMessageBox(string displayName)
    {
        var result = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmSaveFor(displayName),
            UiStrings.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => ConfirmSaveChoice.Save,
            MessageBoxResult.No => ConfirmSaveChoice.Discard,
            _ => ConfirmSaveChoice.Cancel,
        };
    }

    private int CountRemainingDirtyInCloseBatch()
    {
        if (_closingBatch is null)
        {
            return 1;
        }

        var n = 0;
        foreach (var session in _closingBatch)
        {
            if (_sessions.Contains(session) && session.Document.IsDirty)
            {
                n++;
            }
        }

        return n;
    }

    private void RunCloseBatch(IReadOnlyList<DocumentSession> targets, Action body)
    {
        _closingBatch = targets;
        _dirtyClosePolicy = DirtyClosePolicy.Ask;
        try
        {
            body();
        }
        finally
        {
            _closingBatch = null;
            _dirtyClosePolicy = DirtyClosePolicy.Ask;
        }
    }

    private void ForgetClosedDocument()
    {
        DocumentSessionStore.ClearOpenDocuments(AppStorage.Settings);
        AppStorage.ClearAllSessionAudio();
        AppStorage.Save();
    }

    private void OpenSettings()
    {
        var settings = AppStorage.Settings;
        StopRecording();
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        _player.ReleaseOutput();
        var dialog = new AudioSettingsWindow(
            _outputSettings,
            settings.ResolvedFadeInCurve(),
            settings.ResolvedFadeOutCurve(),
            UiStrings.ParseLanguageChoice(settings.UiLanguage),
            UiThemes.ParseChoice(settings.UiTheme),
            settings.ResolvedLoudnessTargetLufs(),
            settings.ResolvedSilentSkipThresholdDb(),
            settings.ResolvedSilentSkipRecordPadMs(),
            settings.ResolvedClickGuardFadeMs(),
            settings.Mp3BitRate,
            settings.LameExePath,
            settings.LameOptions,
            settings.ExportParallelism,
            settings.SpeakerPresets,
            settings.ActiveSpeakerPresetId,
            settings.VisibleSpeakerPresetIds,
            settings.ResolvedDefaultSampleRate(),
            settings.ResolvedDefaultBitsPerSample(),
            settings.ResolvedDefaultChannelLayout().Id,
            settings.ResolvedWwisePrefetchLengthMs(),
            settings.ResolvedWwiseLookAheadTimeMs(),
            settings.ResolvedUiScalePercent(),
            settings.MultiFileArrange,
            settings.AutoSpeakerSelect,
            settings.ResolvedLibraryExplorerRoots(),
            settings.ResolvedLibraryListColumns())
        {
            Owner = this,
        };

        if (WindowPaintReveal.ShowDialogWhenPainted(dialog) != true)
        {
            try
            {
                _player.ApplyOutputSettings(_outputSettings);
            }
            catch
            {
                // 次の再生で開き直す。
            }

            return;
        }

        settings.UiLanguage = UiStrings.ToStoredValue(dialog.SelectedLanguage);
        UiStrings.SetLanguage(UiStrings.ResolveLanguage(dialog.SelectedLanguage));
        settings.UiTheme = UiThemes.ToStoredValue(dialog.SelectedTheme);
        settings.MultiFileArrange = WaveformTileLayout.Format(dialog.SelectedMultiFileArrange);
        settings.ApplyLibraryExplorerRoots(dialog.SelectedLibraryExplorerRoots);
        LibraryBrowser.SetExplorerRoots(settings.ResolvedLibraryExplorerRoots());
        settings.ApplyLibraryListColumns(dialog.SelectedLibraryListColumns);
        LibraryBrowser.SetVisibleColumns(settings.ResolvedLibraryListColumns());
        UiThemeService.ApplyFromSettings(force: true);
        settings.UiScalePercent = dialog.SelectedUiScalePercent;
        UiScaleService.ApplyFromSettings();
        settings.ApplyDefaultFades(dialog.FadeInCurve, dialog.FadeOutCurve);
        settings.LoudnessTargetLufs = dialog.SelectedLoudnessTargetLufs;
        settings.SilentSkipThresholdDb = dialog.SelectedSilentSkipThresholdDb;
        settings.SilentSkipRecordPadMs = dialog.SelectedSilentSkipRecordPadMs;
        settings.ClickGuardFadeMs = dialog.SelectedClickGuardFadeMs;
        settings.WwisePrefetchLengthMs = dialog.SelectedWwisePrefetchLengthMs;
        settings.WwiseLookAheadTimeMs = dialog.SelectedWwiseLookAheadTimeMs;
        settings.Mp3BitRate = dialog.SelectedMp3BitRate;
        settings.LameExePath = dialog.SelectedLameExePath;
        settings.LameOptions = dialog.SelectedLameOptions;
        settings.ExportParallelism = dialog.SelectedExportParallelism;
        settings.ReplaceSpeakerPresets(dialog.SelectedPresets, dialog.SelectedActiveSpeakerId);
        settings.ApplyVisibleSpeakerIds(dialog.SelectedVisibleSpeakerIds);
        settings.AutoSpeakerSelect = dialog.SelectedAutoSpeakerSelect;
        settings.RecordDeviceId = dialog.SelectedRecordDeviceId;
        settings.DefaultSampleRate = dialog.SelectedDefaultSampleRate;
        settings.DefaultBitsPerSample = dialog.SelectedDefaultBitsPerSample;
        settings.DefaultChannelLayout = dialog.SelectedDefaultChannelLayout;
        ApplyPlayerRoute();
        LoudnessMeter.ApplyTargetFromSettings();
        ForEachWaveform(view => view.LoudnessTargetLufs = settings.ResolvedLoudnessTargetLufs());
        ApplySilentSkipFromSettings();
        RefreshSpeakerMenu();
        SyncMonitorLayout();
        ApplyOutputSettings(dialog.SelectedSettings);
        TryApplyAutoSpeaker();
    }

    private void ApplySpeakerPreset(string id, bool persist)
    {
        var settings = AppStorage.Settings;
        if (settings.FindSpeaker(id) is null)
        {
            return;
        }

        if (string.Equals(settings.ActiveSpeakerPresetId, id, StringComparison.OrdinalIgnoreCase)
            && persist)
        {
            RefreshSpeakerMenu();
            return;
        }

        StopRecording();
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        settings.ActiveSpeakerPresetId = id;
        settings.EnsureSpeakerPresets();
        var speaker = settings.ResolvedSpeaker();
        ApplyPlayerRoute();
        ApplyOutputSettings(speaker.ToAudioOutputSettings());
        RefreshSpeakerMenu();
        SyncMonitorLayout();
        if (persist)
        {
            AppStorage.Save();
        }
    }

    private void TryApplyAutoSpeaker()
    {
        var channels = _document?.Channels ?? 0;
        if (channels < 1)
        {
            _autoSpeakerSeenChannels = int.MinValue;
            return;
        }

        var settings = AppStorage.Settings;
        if (IsRecording || !settings.AutoSpeakerSelect)
        {
            _autoSpeakerSeenChannels = channels;
            return;
        }

        var id = SpeakerPreset.ResolveAutoSpeakerId(
            channels,
            settings.SpeakerPresets,
            settings.ResolvedVisibleSpeakerIds(),
            settings.ActiveSpeakerPresetId);
        if (id is not null)
        {
            ApplySpeakerPreset(id, persist: true);
        }

        _autoSpeakerSeenChannels = channels;
    }

    private void RefreshSpeakerMenu()
    {
        var settings = AppStorage.Settings;
        SetSpeakerMenu(settings.MenuSpeakers(), settings.ActiveSpeakerPresetId);
    }

    private void SetSpeakerMenu(IReadOnlyList<SpeakerPreset> presets, string activeId)
    {
        _syncingSpeakers = true;
        try
        {
            SpeakerMenu.Items.Clear();
            SpeakerChoice? selected = null;
            foreach (var preset in presets)
            {
                var item = new SpeakerChoice(preset.Id, preset.DisplayName());
                SpeakerMenu.Items.Add(item);
                if (preset.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = item;
                }
            }

            SpeakerMenu.SelectedItem = selected
                ?? (SpeakerMenu.Items.Count > 0 ? SpeakerMenu.Items[0] : null);
            ComboBoxFit.Apply(SpeakerMenu);
        }
        finally
        {
            _syncingSpeakers = false;
        }
    }

    private void SpeakerMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSpeakers || SpeakerMenu.SelectedItem is not SpeakerChoice item)
        {
            return;
        }

        ApplySpeakerPreset(item.Id, persist: true);
    }

    private sealed record SpeakerChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private void ApplyOutputSettings(AudioOutputSettings settings)
    {
        _outputSettings = settings;
        AppStorage.Settings.ApplyAudioOutput(_outputSettings);
        AppStorage.Save();
        if (_player.IsPlaying)
        {
            StopPlayback();
        }

        try
        {
            _player.ApplyOutputSettings(_outputSettings);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string ResolveOpenInitialDirectory()
    {
        var settings = AppStorage.Settings;
        return ExportFolderMemory.Resolve(settings.LastOpenFolder, settings.LastDocumentPath);
    }

    private static void RememberOpenFolder(string? folder)
    {
        if (!ExportFolderMemory.TryNormalize(folder, out var path))
        {
            return;
        }

        AppStorage.Settings.LastOpenFolder = path;
        AppStorage.Save();
    }

    private static void RememberOpenedPath(string path)
    {
        AppStorage.Settings.LastDocumentPath = path;
        AppStorage.ClearSessionDocument();
        AppStorage.Save();
    }

    private void RememberDocumentState()
    {
        try
        {
            CaptureActiveSessionView();
            var settings = AppStorage.Settings;
            if (!DocumentSessionStore.ShouldPersistOpenDocuments(IsLibraryMaximized, _sessions.Count))
            {
                DocumentSessionStore.ClearOpenDocuments(settings);
                AppStorage.ClearAllSessionAudio();
                return;
            }

            var snapshots = new OpenDocumentSnapshot[_sessions.Count];
            var keep = new List<string>();
            Directory.CreateDirectory(AppStorage.SessionDirectory);
            for (var i = 0; i < _sessions.Count; i++)
            {
                snapshots[i] = CaptureSessionSnapshot(
                    _sessions[i],
                    i,
                    DocumentSessionStore.FileNameForIndex(i),
                    DocumentSessionStore.OriginFileNameForIndex(i),
                    DocumentSessionStore.HistoryFileNameForIndex(i),
                    keep);
                snapshots[i].IsActive = ReferenceEquals(_sessions[i], _activeSession);
            }

            AppStorage.ReplaceSessionFiles(keep);
            settings.OpenDocuments = snapshots;
            settings.ClosedDocuments = [];

            var activeIndex = _activeSession is null ? 0 : _sessions.IndexOf(_activeSession);
            settings.ActiveDocumentIndex = Math.Clamp(activeIndex, 0, snapshots.Length - 1);
            settings.WaveformTileArrange = _sessions.Count >= 2
                ? WaveformTileLayout.Format(_tileArrange)
                : WaveformTileLayout.StoredOff;
            var activePath = snapshots[settings.ActiveDocumentIndex].SourcePath;
            if (!string.IsNullOrWhiteSpace(activePath))
            {
                settings.LastDocumentPath = activePath;
            }
        }
        catch
        {
            // セッション保存失敗でも終了は止めない。
        }
    }

    private async Task TryRestoreLastDocumentAsync()
    {
        if (_didRestoreLastDocument)
        {
            return;
        }

        _didRestoreLastDocument = true;
        var settings = AppStorage.Settings;
        var root = AppStorage.RootDirectory;
        var docs = DocumentSessionStore.ResolveOpenDocuments(settings);
        if (docs.Length == 0)
        {
            return;
        }

        var restored = new List<(int SourceIndex, DocumentSession Session)>();
        var activeIndex = DocumentSessionStore.ResolveActiveIndex(docs, settings.ActiveDocumentIndex);
        var restorable = new List<(int SourceIndex, OpenDocumentSnapshot Snap)>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            if (DocumentSessionStore.CanRestoreDocument(root, docs[i]))
            {
                restorable.Add((i, docs[i]));
            }
        }

        var showProgress = restorable.Count > 1;
        BeginOpenWork(restorable.Select(item => SnapshotDisplayName(item.Snap)).ToArray());
        var cancelled = false;
        try
        {
            int JobIndex(int sourceIndex)
            {
                for (var i = 0; i < restorable.Count; i++)
                {
                    if (restorable[i].SourceIndex == sourceIndex)
                    {
                        return i;
                    }
                }

                return 0;
            }

            async Task<DocumentSession?> RestoreIndexedAsync(int sourceIndex, OpenDocumentSnapshot snap)
            {
                var job = JobIndex(sourceIndex);
                if (showProgress)
                {
                    SetOpenJobRunning(job);
                }

                var session = await TryRestoreSessionAsync(snap).ConfigureAwait(true);
                if (showProgress)
                {
                    SetOpenJobDone(job);
                }

                return session;
            }

            var activeSnap = (uint)activeIndex < (uint)docs.Length ? docs[activeIndex] : null;
            if (activeSnap is not null && DocumentSessionStore.CanRestoreDocument(root, activeSnap))
            {
                var session = await RestoreIndexedAsync(activeIndex, activeSnap).ConfigureAwait(true);
                if (session is not null)
                {
                    DocumentSessionStore.InsertRestoredBySourceIndex(
                        restored, _sessions, activeIndex, session);
                }
            }

            if (_openCancelRequested)
            {
                cancelled = true;
            }

            foreach (var (sourceIndex, snap) in restorable)
            {
                if (cancelled || _openCancelRequested)
                {
                    cancelled = true;
                    break;
                }

                if (sourceIndex == activeIndex)
                {
                    continue;
                }

                var session = await RestoreIndexedAsync(sourceIndex, snap).ConfigureAwait(true);
                if (session is null)
                {
                    continue;
                }

                DocumentSessionStore.InsertRestoredBySourceIndex(
                    restored, _sessions, sourceIndex, session);
            }

            if (_sessions.Count == 0)
            {
                return;
            }

            if (_activeSession is null)
            {
                BindWorkspace(DocumentSessionStore.PickRestoredActive(restored, activeIndex) ?? _sessions[0]);
            }
            else
            {
                RebuildTabBar();
            }
        }
        finally
        {
            EndOpenWork(showProgress);
        }

        ApplyPreferredOrRestoredTileArrange(settings);

        if (cancelled)
        {
            _queuedOpenPaths.Clear();
            return;
        }

        if (_queuedOpenPaths.Count > 0)
        {
            var queued = _queuedOpenPaths.ToArray();
            _queuedOpenPaths.Clear();
            await OpenPathsAsync(queued).ConfigureAwait(true);
        }
    }

    private static string SnapshotDisplayName(OpenDocumentSnapshot snap)
    {
        if (!string.IsNullOrWhiteSpace(snap.SourcePath))
        {
            return Path.GetFileName(snap.SourcePath);
        }

        return UiStrings.UntitledDocument;
    }

    private async Task<DocumentSession?> TryRestoreSessionAsync(OpenDocumentSnapshot snap)
    {
        if (DocumentSessionStore.NeedsHistoryReplay(AppStorage.RootDirectory, snap))
        {
            var fromHistory = await TryRestoreSessionHistoryAsync(snap).ConfigureAwait(true);
            if (fromHistory is not null && fromHistory.Document.FrameCount > 0)
            {
                return fromHistory;
            }
        }

        try
        {
            if (!DocumentSessionStore.TryResolveLoadPath(
                    AppStorage.RootDirectory,
                    snap,
                    out var path,
                    out var fromSession))
            {
                return null;
            }

            var document = await Task.Run(() => AudioCodec.Load(path)).ConfigureAwait(true);
            if (fromSession)
            {
                document.MarkUnsaved(string.IsNullOrWhiteSpace(snap.SourcePath) ? null : snap.SourcePath);
            }

            DocumentSessionStore.ApplyMeta(document, snap);
            var session = new DocumentSession(document)
            {
                LoopEnabled = snap.LoopEnabled,
            };
            RememberPersistedSessionFiles(session, snap, fromSession);
            return session;
        }
        catch
        {
            return null;
        }
    }

    private static void RememberPersistedSessionFiles(
        DocumentSession session,
        OpenDocumentSnapshot snap,
        bool fromSessionAudio)
    {
        if (fromSessionAudio
            || DocumentSessionStore.HasUsableSessionAudio(AppStorage.SessionDirectory, snap.SessionFileName))
        {
            session.PersistedSessionAudioName = DocumentSessionStore.SanitizeSessionFileName(snap.SessionFileName);
            session.PersistedSampleRevision = session.Document.SampleRevision;
        }

        if (DocumentSessionStore.HasUsableSidecar(AppStorage.SessionDirectory, snap.OriginFileName)
            && DocumentSessionStore.HasUsableSidecar(AppStorage.SessionDirectory, snap.HistoryFileName))
        {
            session.PersistedOriginName = DocumentSessionStore.SanitizeSidecarName(snap.OriginFileName);
            session.PersistedHistoryName = DocumentSessionStore.SanitizeSidecarName(snap.HistoryFileName);
        }
    }

    private OpenDocumentSnapshot CaptureSessionSnapshot(
        DocumentSession session,
        int index,
        string audioName,
        string originName,
        string historyName,
        List<string> keep)
    {
        var view = new SessionViewState(
            session.PlayheadFrame,
            session.TimeZoom,
            session.AmpZoom,
            session.ViewStart,
            session.LoopEnabled,
            session.SelectedMarkerFrames);
        var snap = DocumentSessionStore.Capture(session.Document, view, index);
        var write = DocumentSessionStore.DecideSessionAudioWrite(
            DocumentSessionStore.NeedsSessionAudio(snap.Dirty, snap.SourcePath, snap.CanContinueRecording),
            DocumentSessionStore.HasWorkingAudio(session.Document),
            !string.IsNullOrWhiteSpace(session.Document.SourcePath),
            session.Document.SampleRevision,
            session.PersistedSampleRevision,
            DocumentSessionStore.HasUsableSessionAudio(
                AppStorage.SessionDirectory,
                session.PersistedSessionAudioName));
        var hasSessionAudio = false;
        if (write == SessionAudioWriteKind.Skip)
        {
            snap.SessionFileName = string.Empty;
        }
        else if (write == SessionAudioWriteKind.Reuse)
        {
            var reused = DocumentSessionStore.SanitizeSessionFileName(session.PersistedSessionAudioName);
            if (reused is not null)
            {
                snap.SessionFileName = reused;
                keep.Add(reused);
                hasSessionAudio = true;
            }
        }

        if (write == SessionAudioWriteKind.Rewrite || (write == SessionAudioWriteKind.Reuse && !hasSessionAudio))
        {
            try
            {
                var name = DocumentSessionStore.SanitizeSessionFileName(audioName)
                    ?? DocumentSessionStore.FileNameForIndex(index);
                snap.SessionFileName = name;
                AudioCodec.SaveWave(session.Document, AppStorage.SessionFilePath(name));
                keep.Add(name);
                session.PersistedSessionAudioName = name;
                session.PersistedSampleRevision = session.Document.SampleRevision;
                hasSessionAudio = true;
            }
            catch
            {
                var written = DocumentSessionStore.SanitizeSessionFileName(snap.SessionFileName);
                if (written is not null)
                {
                    try
                    {
                        var path = AppStorage.SessionFilePath(written);
                        if (File.Exists(path) && new FileInfo(path).Length > 0)
                        {
                            keep.Add(written);
                            hasSessionAudio = true;
                        }
                    }
                    catch
                    {
                        // 作業コピーが残っていなければ捨てる。
                    }
                }

                if (!hasSessionAudio)
                {
                    snap.SessionFileName = string.Empty;
                }
            }
        }

        if (!DocumentSessionStore.ShouldPersistHistorySidecars(hasSessionAudio, snap.Dirty, snap.SourcePath))
        {
            snap.OriginFileName = string.Empty;
            snap.HistoryFileName = string.Empty;
            return snap;
        }

        TrySaveSessionHistory(session, snap, originName, historyName, keep);
        return snap;
    }

    private static void TrySaveSessionHistory(
        DocumentSession session,
        OpenDocumentSnapshot snap,
        string originName,
        string historyName,
        List<string> keep)
    {
        var exported = session.History.TryExport();
        if (exported is null)
        {
            return;
        }

        var persistedOrigin = DocumentSessionStore.SanitizeSidecarName(session.PersistedOriginName);
        var persistedHistory = DocumentSessionStore.SanitizeSidecarName(session.PersistedHistoryName);
        if (persistedOrigin is not null
            && DocumentSessionStore.HasUsableSidecar(AppStorage.SessionDirectory, persistedOrigin))
        {
            snap.OriginFileName = persistedOrigin;
            keep.Add(persistedOrigin);
        }

        if (persistedHistory is not null
            && DocumentSessionStore.HistoryMatchesFile(
                AppStorage.SessionSidecarPath(persistedHistory),
                exported))
        {
            snap.HistoryFileName = persistedHistory;
            keep.Add(persistedHistory);
            if (!string.IsNullOrEmpty(snap.OriginFileName))
            {
                return;
            }
        }

        if (!string.IsNullOrEmpty(snap.OriginFileName) && !string.IsNullOrEmpty(snap.HistoryFileName))
        {
            return;
        }

        var currentIndex = session.History.CurrentIndex;
        try
        {
            if (string.IsNullOrEmpty(snap.OriginFileName))
            {
                session.History.JumpTo(session.Document, 0);
                AudioCodec.SaveWave(session.Document, AppStorage.SessionSidecarPath(originName));
                session.History.JumpTo(session.Document, currentIndex);
                snap.OriginFileName = originName;
                keep.Add(originName);
                session.PersistedOriginName = originName;
            }

            if (string.IsNullOrEmpty(snap.HistoryFileName))
            {
                if (!DocumentSessionStore.TryWriteHistory(AppStorage.SessionSidecarPath(historyName), exported))
                {
                    return;
                }

                snap.HistoryFileName = historyName;
                keep.Add(historyName);
                session.PersistedHistoryName = historyName;
            }
        }
        catch
        {
            // 履歴が残らなくても作業コピーは残す。
        }
        finally
        {
            session.History.JumpTo(session.Document, currentIndex);
        }
    }

    private async Task<DocumentSession?> TryRestoreSessionHistoryAsync(OpenDocumentSnapshot snap)
    {
        var originName = DocumentSessionStore.SanitizeSidecarName(snap.OriginFileName);
        var historyName = DocumentSessionStore.SanitizeSidecarName(snap.HistoryFileName);
        if (originName is null || historyName is null)
        {
            return null;
        }

        var originPath = Path.Combine(AppStorage.SessionDirectory, originName);
        var historyPath = Path.Combine(AppStorage.SessionDirectory, historyName);
        if (!DocumentSessionStore.TryReadHistory(historyPath, out var historySnap)
            || !HistoryRecipes.CanImport(historySnap)
            || !File.Exists(originPath))
        {
            return null;
        }

        try
        {
            var imported = await Task.Run(() =>
            {
                var document = AudioCodec.Load(originPath);
                return EditHistory.TryImport(document, historySnap, out var history)
                    ? (document, history)
                    : default((AudioDocument Document, EditHistory History)?);
            }).ConfigureAwait(true);
            if (imported is not { } pair || pair.Document.FrameCount <= 0)
            {
                return null;
            }

            var document = pair.Document;
            var history = pair.History;
            document.MarkUnsaved(string.IsNullOrWhiteSpace(snap.SourcePath) ? null : snap.SourcePath);
            document.SetDirty(!history.IsClean);
            DocumentSessionStore.ApplyMeta(document, snap);
            document.SetDirty(!history.IsClean);
            var session = new DocumentSession(document)
            {
                History = history,
                LoopEnabled = snap.LoopEnabled,
            };
            RememberPersistedSessionFiles(session, snap, fromSessionAudio: false);
            return session;
        }
        catch
        {
            return null;
        }
    }

}
