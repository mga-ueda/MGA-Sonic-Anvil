using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

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

    private void OpenPaths(IReadOnlyList<string> paths)
    {
        DocumentSession? first = null;
        List<string>? errors = null;
        foreach (var path in paths)
        {
            if (!AudioCodec.IsOpenable(path))
            {
                continue;
            }

            var existing = FindSessionByPath(path);
            if (existing is not null)
            {
                if (first is null)
                {
                    first = existing;
                    ActivateSession(existing);
                    PumpUiAfterOpen();
                }

                continue;
            }

            try
            {
                var document = AudioCodec.Load(path);
                var session = new DocumentSession(document);
                _sessions.Add(session);
                if (first is null)
                {
                    first = session;
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

        if (first is not null)
        {
            if (first.Document.SourcePath is { } opened)
            {
                RememberOpenedPath(opened, dirty: false, resetMarkers: false);
            }
        }
        else
        {
            RebuildTabBar();
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
            AudioCodec.Save(document, path, AppStorage.Settings.Mp3BitRate);
            document.MarkSaved(path, AudioCodec.DetectKind(path));
            var history = _sessions.FirstOrDefault(item => ReferenceEquals(item.Document, document))?.History
                ?? (ReferenceEquals(document, _document) ? _history : null);
            history?.MarkClean();
            if (ReferenceEquals(document, _document))
            {
                RememberOpenedPath(path, dirty: false, resetMarkers: false);
            }

            RefreshTitle();
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

    private void CloseDocument()
    {
        if (_activeSession is null)
        {
            return;
        }

        CloseSession(_activeSession);
    }

    private bool OfferSaveIfDirty() => OfferSaveIfDirty(_activeSession);

    private bool OfferSaveIfDirty(DocumentSession? session)
    {
        if (session?.Document is not { IsDirty: true } document)
        {
            return true;
        }

        var result = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmSaveFor(session.DisplayName),
            UiStrings.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (result == MessageBoxResult.Cancel)
        {
            return false;
        }

        if (result == MessageBoxResult.No)
        {
            return true;
        }

        return Save(saveAs: false, document);
    }

    private void ForgetClosedDocument()
    {
        DocumentSessionStore.ClearOpenDocuments(AppStorage.Settings);
        AppStorage.ClearAllSessionAudio();
        AppStorage.Save();
    }

    private void SettingsGearButton_Click(object sender, RoutedEventArgs e)
    {
        var settings = AppStorage.Settings;
        var dialog = new AudioSettingsWindow(
            _outputSettings,
            settings.ResolvedFadeInCurve(),
            settings.ResolvedFadeOutCurve(),
            UiStrings.ParseLanguageChoice(settings.UiLanguage))
        {
            Owner = this,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        settings.UiLanguage = UiStrings.ToStoredValue(dialog.SelectedLanguage);
        UiStrings.SetLanguage(UiStrings.ResolveLanguage(dialog.SelectedLanguage));
        settings.ApplyDefaultFades(dialog.FadeInCurve, dialog.FadeOutCurve);
        ApplyOutputSettings(dialog.SelectedSettings);
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

    private void TryRestoreLastDocument()
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

        DocumentSession? active = null;
        var activeIndex = Math.Clamp(settings.ActiveDocumentIndex, 0, docs.Length - 1);
        for (var i = 0; i < docs.Length; i++)
        {
            if (!TryRestoreSession(docs[i], out var session))
            {
                continue;
            }

            _sessions.Add(session);
            if (i == activeIndex)
            {
                active = session;
            }
        }

        if (_sessions.Count == 0)
        {
            return;
        }

        BindWorkspace(active ?? _sessions[0]);
        Waveform.Refresh();
        Overview.InvalidateVisual();
    }

    private static bool TryRestoreSession(OpenDocumentSnapshot snap, out DocumentSession session)
    {
        session = null!;
        try
        {
            if (!DocumentSessionStore.TryResolveLoadPath(
                    AppStorage.RootDirectory,
                    snap,
                    out var path,
                    out var fromSession))
            {
                return false;
            }

            var document = AudioCodec.Load(path);
            if (fromSession)
            {
                document.MarkUnsaved(string.IsNullOrWhiteSpace(snap.SourcePath) ? null : snap.SourcePath);
            }

            DocumentSessionStore.ApplyMeta(document, snap);
            session = new DocumentSession(document)
            {
                TimeZoom = snap.TimeZoom <= 0 ? 1 : snap.TimeZoom,
                AmpZoom = snap.AmpZoom <= 0 ? 1 : snap.AmpZoom,
                ViewStart = snap.ViewStart,
                PlayheadFrame = snap.CursorFrame,
                LoopEnabled = snap.LoopEnabled,
            };
            if (snap.SelectedMarkerFrames is { Length: > 0 } selected)
            {
                session.SelectedMarkerFrames.AddRange(selected);
            }

            return true;
        }
        catch
        {
            session = null!;
            return false;
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
