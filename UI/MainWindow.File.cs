using System.IO;
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
    private void OpenFromDialog()
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterOpenAudio,
            Title = UiStrings.MenuOpen,
            Multiselect = true,
        };
        var lastDir = Path.GetDirectoryName(AppStorage.Settings.LastDocumentPath);
        if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
        {
            dialog.InitialDirectory = lastDir;
        }
        if (dialog.ShowDialog(this) == true)
        {
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

        OpenPaths(paths);
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

    private void PumpUiAfterOpen()
    {
        UpdateLayout();
        Waveform.Refresh();
        Overview.InvalidateVisual();
        Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private string? _openStatusText;
    private double _openStatusRatio;
    private readonly List<string> _queuedOpenPaths = [];
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
        foreach (var path in paths)
        {
            if (!AudioCodec.IsOpenable(path)
                || _queuedOpenPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            _queuedOpenPaths.Add(path);
        }
    }

    private async Task OpenPathsAsync(IReadOnlyList<string> paths)
    {
        var targets = new List<string>();
        foreach (var path in paths)
        {
            if (AudioCodec.IsOpenable(path))
            {
                targets.Add(path);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        var showProgress = targets.Count > 1;
        DocumentSession? opened = null;
        DocumentSession? existingFirst = null;
        List<string>? errors = null;
        BeginOpenWork(targets.Count);
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var path = targets[i];
                if (showProgress)
                {
                    SetOpenStatus(i + 1, targets.Count, Path.GetFileName(path));
                }

                var existing = FindSessionByPath(path);
                if (existing is not null)
                {
                    existingFirst ??= existing;
                    continue;
                }

                try
                {
                    var document = await Task.Run(() => AudioCodec.Load(path)).ConfigureAwait(true);
                    var session = new DocumentSession(document);
                    _sessions.Add(session);
                    if (opened is null)
                    {
                        opened = session;
                        ActivateSession(session);
                    }
                    else
                    {
                        RebuildTabBar();
                    }

                    PumpUiAfterOpen();
                }
                catch (Exception ex)
                {
                    errors ??= [];
                    errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
                }
            }

            var active = LaunchFiles.PreferOpened(opened, existingFirst);
            if (active is not null)
            {
                if (!ReferenceEquals(_activeSession, active))
                {
                    ActivateSession(active);
                }

                if (active.Document.SourcePath is { } openedPath)
                {
                    RememberOpenedPath(openedPath, dirty: false, resetMarkers: false);
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

        if (errors is { Count: > 0 })
        {
            OwnerCenteredMessageBox.Show(
                this,
                $"{UiStrings.ErrorOpenFailed}\n{string.Join("\n", errors)}",
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        if (_queuedOpenPaths.Count > 0)
        {
            var queued = _queuedOpenPaths.ToArray();
            _queuedOpenPaths.Clear();
            await OpenPathsAsync(queued).ConfigureAwait(true);
        }
    }

    private void BeginOpenWork(int targetCount)
    {
        _openBusy = targetCount > 0;
        RefreshExportEnabled();
        StopWaveformLoadingHint(hide: false);
        if (targetCount <= 0)
        {
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
        StopWaveformLoadingHint(hide: true);
        if (showProgress)
        {
            ClearOpenStatus();
        }
        else
        {
            RefreshExportEnabled();
        }
    }

    private void SetOpenStatus(int current, int total, string name)
    {
        _openStatusText = UiStrings.StatusOpeningFiles(current, total, name);
        _openStatusRatio = total <= 0 ? 0 : current / (double)total;
        RefreshStatus();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    private void ClearOpenStatus()
    {
        _openStatusText = null;
        _openStatusRatio = 0;
        RefreshStatus();
    }

    private bool Save(bool saveAs, AudioDocument? target = null)
    {
        var document = target ?? _document;
        if (document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var path = document.SourcePath;
        var kind = path is null ? AudioFileKind.Wave : AudioCodec.DetectKind(path);
        if (saveAs
            || string.IsNullOrEmpty(path)
            || kind == AudioFileKind.Aiff
            || !AudioCodec.SaveExtensions.Any(ext =>
                ext.Equals(System.IO.Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)))
        {
            var dialog = new SaveFileDialog
            {
                Filter = UiStrings.FilterSaveAudio,
                Title = UiStrings.MenuSaveAs,
                FileName = string.IsNullOrEmpty(path)
                    ? "untitled.wav"
                    : Path.GetFileNameWithoutExtension(path) + ".wav",
            };
            var lastDir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
            {
                dialog.InitialDirectory = lastDir;
            }
            if (dialog.ShowDialog(this) != true)
            {
                return false;
            }

            path = dialog.FileName;
        }

        try
        {
            if (AudioCodec.DetectKind(path) == AudioFileKind.Mp3
                && !SameDocumentPath(document.SourcePath, path))
            {
                return ExportMp3WithoutReloading(document, path);
            }

            var encoder = AudioCodec.Save(document, path, AppStorage.Settings.ToMp3EncodeOptions());
            document.MarkSaved(path, AudioCodec.DetectKind(path));
            var history = _sessions.FirstOrDefault(item => ReferenceEquals(item.Document, document))?.History
                ?? (ReferenceEquals(document, _document) ? _history : null);
            history?.MarkClean();
            if (ReferenceEquals(document, _document))
            {
                RememberOpenedPath(path, dirty: false, resetMarkers: false);
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
        };
        var lastDir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
        {
            dialog.InitialDirectory = lastDir;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

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
                        new Progress<double>(p => tracker.Report(0, p, ExportJobState.Running)));
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
        var encoder = AudioCodec.SaveMp3(document, path, AppStorage.Settings.ToMp3EncodeOptions());
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
            settings.Mp3BitRate,
            settings.LameExePath,
            settings.LameOptions,
            settings.ExportParallelism,
            settings.SpeakerPresets,
            settings.ActiveSpeakerPresetId,
            settings.VisibleSpeakerPresetIds)
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
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
        UiThemeService.ApplyFromSettings(force: true);
        settings.ApplyDefaultFades(dialog.FadeInCurve, dialog.FadeOutCurve);
        settings.LoudnessTargetLufs = dialog.SelectedLoudnessTargetLufs;
        settings.Mp3BitRate = dialog.SelectedMp3BitRate;
        settings.LameExePath = dialog.SelectedLameExePath;
        settings.LameOptions = dialog.SelectedLameOptions;
        settings.ExportParallelism = dialog.SelectedExportParallelism;
        settings.ReplaceSpeakerPresets(dialog.SelectedPresets, dialog.SelectedActiveSpeakerId);
        settings.ApplyVisibleSpeakerIds(dialog.SelectedVisibleSpeakerIds);
        settings.RecordDeviceId = dialog.SelectedRecordDeviceId;
        ApplyPlayerRoute();
        LoudnessMeter.ApplyTargetFromSettings();
        Waveform.LoudnessTargetLufs = settings.ResolvedLoudnessTargetLufs();
        RefreshSpeakerMenu();
        SyncMonitorLayout();
        ApplyOutputSettings(dialog.SelectedSettings);
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

    private void RememberOpenedPath(string path, bool dirty, bool resetMarkers)
    {
        var settings = AppStorage.Settings;
        settings.LastDocumentPath = path;
        settings.LastDocumentDirty = dirty;
        if (resetMarkers)
        {
            StoreSessionMarkers([]);
            settings.LastSampleLoopStart = 0;
            settings.LastSampleLoopEnd = 0;
            StoreSessionRegions(settings, null);
        }
        else
        {
            StoreSessionMarkers(_document?.SnapshotMarkers());
            settings.LastSampleLoopStart = _document?.SampleLoop.StartFrame ?? 0;
            settings.LastSampleLoopEnd = _document?.SampleLoop.EndFrame ?? 0;
            StoreSessionRegions(settings, _document);
        }

        if (!dirty)
        {
            AppStorage.ClearSessionDocument();
        }

        AppStorage.Save();
    }

    private void RememberDocumentState()
    {
        try
        {
            CaptureActiveSessionView();
            var settings = AppStorage.Settings;
            if (_sessions.Count == 0)
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
                var session = _sessions[i];
                var view = new SessionViewState(
                    session.PlayheadFrame,
                    session.TimeZoom,
                    session.AmpZoom,
                    session.ViewStart,
                    session.LoopEnabled,
                    session.SelectedMarkerFrames);
                var snap = DocumentSessionStore.Capture(session.Document, view, i);
                snap.IsActive = ReferenceEquals(session, _activeSession);
                TrySaveSessionHistory(session, snap, i, keep);
                if (DocumentSessionStore.NeedsSessionAudio(snap.Dirty, snap.SourcePath))
                {
                    try
                    {
                        var name = DocumentSessionStore.SanitizeSessionFileName(snap.SessionFileName)
                            ?? DocumentSessionStore.FileNameForIndex(i);
                        snap.SessionFileName = name;
                        AudioCodec.SaveWave(session.Document, AppStorage.SessionFilePath(name));
                        keep.Add(name);
                    }
                    catch
                    {
                        snap.SessionFileName = string.Empty;
                    }
                }

                snapshots[i] = snap;
            }

            AppStorage.ReplaceSessionFiles(keep);
            settings.OpenDocuments = snapshots;
            var activeIndex = _activeSession is null ? 0 : _sessions.IndexOf(_activeSession);
            settings.ActiveDocumentIndex = Math.Clamp(activeIndex, 0, snapshots.Length - 1);
            DocumentSessionStore.MirrorActiveToLegacy(settings, snapshots[settings.ActiveDocumentIndex]);
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
        var docs = DocumentSessionStore.ResolveOpenDocuments(settings);
        if (docs.Length == 0)
        {
            return;
        }

        var restored = new List<(int SourceIndex, DocumentSession Session)>();
        var activeIndex = DocumentSessionStore.ResolveActiveIndex(docs, settings.ActiveDocumentIndex);
        var showProgress = docs.Length > 1;
        BeginOpenWork(docs.Length);
        try
        {
            for (var i = 0; i < docs.Length; i++)
            {
                if (showProgress)
                {
                    SetOpenStatus(i + 1, docs.Length, SnapshotDisplayName(docs[i]));
                }

                var session = await TryRestoreSessionAsync(docs[i]).ConfigureAwait(true);
                if (session is null)
                {
                    continue;
                }

                _sessions.Add(session);
                restored.Add((i, session));
                if (showProgress)
                {
                    PumpUiAfterOpen();
                }
            }

            if (_sessions.Count == 0)
            {
                return;
            }

            BindWorkspace(DocumentSessionStore.PickRestoredActive(restored, activeIndex) ?? _sessions[0]);
            Waveform.Refresh();
            Overview.InvalidateVisual();
        }
        finally
        {
            EndOpenWork(showProgress);
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
        var fromHistory = await TryRestoreSessionHistoryAsync(snap).ConfigureAwait(true);
        if (fromHistory is not null)
        {
            return fromHistory;
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
            return new DocumentSession(document)
            {
                LoopEnabled = snap.LoopEnabled,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void TrySaveSessionHistory(
        DocumentSession session,
        OpenDocumentSnapshot snap,
        int index,
        List<string> keep)
    {
        var exported = session.History.TryExport();
        if (exported is null)
        {
            return;
        }

        var originName = DocumentSessionStore.OriginFileNameForIndex(index);
        var historyName = DocumentSessionStore.HistoryFileNameForIndex(index);
        var currentIndex = session.History.CurrentIndex;
        try
        {
            session.History.JumpTo(session.Document, 0);
            AudioCodec.SaveWave(session.Document, AppStorage.SessionSidecarPath(originName));
            session.History.JumpTo(session.Document, currentIndex);
            if (!DocumentSessionStore.TryWriteHistory(AppStorage.SessionSidecarPath(historyName), exported))
            {
                return;
            }

            snap.OriginFileName = originName;
            snap.HistoryFileName = historyName;
            keep.Add(originName);
            keep.Add(historyName);
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
            || !File.Exists(originPath))
        {
            return null;
        }

        try
        {
            var document = await Task.Run(() => AudioCodec.Load(originPath)).ConfigureAwait(true);
            if (!EditHistory.TryImport(document, historySnap, out var history))
            {
                return null;
            }

            document.MarkUnsaved(string.IsNullOrWhiteSpace(snap.SourcePath) ? null : snap.SourcePath);
            document.SetDirty(!history.IsClean);
            DocumentSessionStore.ApplyMeta(document, snap);
            document.SetDirty(!history.IsClean);
            return new DocumentSession(document)
            {
                History = history,
                LoopEnabled = snap.LoopEnabled,
            };
        }
        catch
        {
            return null;
        }
    }

    private static void StoreSessionRegions(AppSettings settings, AudioDocument? document)
    {
        var regions = document?.SnapshotRegions();
        if (regions is null || regions.Length == 0)
        {
            settings.LastRegionStart = 0;
            settings.LastRegionEnd = 0;
            settings.LastRegionStarts = [];
            settings.LastRegionEnds = [];
            settings.LastRegionNames = [];
            return;
        }

        var starts = new long[regions.Length];
        var ends = new long[regions.Length];
        var names = new string[regions.Length];
        for (var i = 0; i < regions.Length; i++)
        {
            starts[i] = regions[i].StartFrame;
            ends[i] = regions[i].EndFrame;
            names[i] = regions[i].Name;
        }

        settings.LastRegionStarts = starts;
        settings.LastRegionEnds = ends;
        settings.LastRegionNames = names;
        settings.LastRegionStart = starts[0];
        settings.LastRegionEnd = ends[0];
    }

    private static void StoreSessionMarkers(IReadOnlyList<MarkerSnapshot>? markers)
    {
        if (markers is null || markers.Count == 0)
        {
            AppStorage.Settings.LastMarkerFrames = [];
            AppStorage.Settings.LastMarkerComments = [];
            return;
        }

        var frames = new long[markers.Count];
        var comments = new string[markers.Count];
        for (var i = 0; i < markers.Count; i++)
        {
            frames[i] = markers[i].Frame;
            comments[i] = markers[i].Comment ?? string.Empty;
        }

        AppStorage.Settings.LastMarkerFrames = frames;
        AppStorage.Settings.LastMarkerComments = comments;
    }

}
