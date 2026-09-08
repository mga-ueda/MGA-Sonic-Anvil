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
        var settings = AppStorage.Settings;
        settings.LastDocumentPath = string.Empty;
        settings.LastDocumentDirty = false;
        settings.LastCursorFrame = 0;
        settings.LastSelectionStart = 0;
        settings.LastSelectionEnd = 0;
        settings.LastSampleLoopStart = 0;
        settings.LastSampleLoopEnd = 0;
        StoreSessionRegions(settings, null);
        StoreSessionMarkers([]);
        AppStorage.ClearSessionDocument();
        AppStorage.Save();
    }

    private void PopulateOutputCombos()
    {
        _syncingOutputCombos = true;
        ApiCombo.Items.Clear();
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.WaveOut, UiStrings.LabelAudioApiWaveOut));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Wasapi, UiStrings.LabelAudioApiWasapi));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Asio, UiStrings.LabelAudioApiAsio));
        foreach (ApiItem item in ApiCombo.Items)
        {
            if (item.Api == _outputSettings.Api)
            {
                ApiCombo.SelectedItem = item;
                break;
            }
        }

        if (ApiCombo.SelectedItem is null)
        {
            ApiCombo.SelectedIndex = 0;
        }

        ReloadDevices(_outputSettings.DeviceId);
        _syncingOutputCombos = false;
    }

    private void ReloadDevices(string? preferredDeviceId)
    {
        var api = ApiCombo.SelectedItem is ApiItem item ? item.Api : AudioOutputApi.WaveOut;
        DeviceCombo.Items.Clear();
        DeviceItem? selected = null;
        foreach (var device in AudioOutputFactory.EnumerateDevices(api))
        {
            var entry = new DeviceItem(device.Id, device.DisplayName);
            DeviceCombo.Items.Add(entry);
            if (preferredDeviceId is not null
                && string.Equals(device.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selected = entry;
            }
        }

        DeviceCombo.SelectedItem = selected ?? (DeviceCombo.Items.Count > 0 ? DeviceCombo.Items[0] : null);
    }

    private void ApiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOutputCombos || !IsLoaded)
        {
            return;
        }

        _syncingOutputCombos = true;
        ReloadDevices(preferredDeviceId: null);
        _syncingOutputCombos = false;
        ApplyOutputFromCombos();
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingOutputCombos || !IsLoaded)
        {
            return;
        }

        ApplyOutputFromCombos();
    }

    private void ApplyOutputFromCombos()
    {
        if (ApiCombo.SelectedItem is not ApiItem api)
        {
            return;
        }

        var deviceId = DeviceCombo.SelectedItem is DeviceItem device ? device.Id : string.Empty;
        _outputSettings = new AudioOutputSettings(api.Api, deviceId);
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
        CaptureActiveSessionView();
        var settings = AppStorage.Settings;
        if (_document is null)
        {
            return;
        }

        settings.LastDocumentPath = _document.SourcePath ?? settings.LastDocumentPath;
        settings.LastCursorFrame = Waveform.PlayheadFrame;
        settings.LastSelectionStart = _document.Selection.StartFrame;
        settings.LastSelectionEnd = _document.Selection.EndFrame;
        settings.LastTimeZoom = Waveform.TimeZoom;
        settings.LastAmpZoom = Waveform.AmpZoom;
        settings.LastViewStart = Waveform.ViewStart;
        settings.LastLoop = true;
        if (_document.IsDirty)
        {
            // 保存せず終了した変更は捨て、次回はファイル側の埋め込みを使う。
            settings.LastSampleLoopStart = 0;
            settings.LastSampleLoopEnd = 0;
            StoreSessionRegions(settings, null);
            StoreSessionMarkers([]);
        }
        else
        {
            settings.LastSampleLoopStart = _document.SampleLoop.StartFrame;
            settings.LastSampleLoopEnd = _document.SampleLoop.EndFrame;
            StoreSessionRegions(settings, _document);
            StoreSessionMarkers(_document.SnapshotMarkers());
        }

        settings.LastDocumentDirty = false;
        AppStorage.ClearSessionDocument();
    }

    private void TryRestoreLastDocument()
    {
        if (_didRestoreLastDocument)
        {
            return;
        }

        _didRestoreLastDocument = true;
        var settings = AppStorage.Settings;
        try
        {
            AudioDocument? document = null;
            if (settings.LastDocumentDirty && File.Exists(AppStorage.SessionDocumentPath))
            {
                document = AudioCodec.Load(AppStorage.SessionDocumentPath);
                document.MarkUnsaved(
                    string.IsNullOrWhiteSpace(settings.LastDocumentPath) ? null : settings.LastDocumentPath);
            }
            else if (!string.IsNullOrWhiteSpace(settings.LastDocumentPath)
                && File.Exists(settings.LastDocumentPath))
            {
                document = AudioCodec.Load(settings.LastDocumentPath);
            }

            if (document is null)
            {
                return;
            }

            var sessionMarkers = LoadSessionMarkers(settings);
            if (sessionMarkers.Length > 0)
            {
                document.ReplaceMarkers(sessionMarkers, markDirty: false);
            }

            var sessionLoop = new WaveSelection(settings.LastSampleLoopStart, settings.LastSampleLoopEnd);
            if (!sessionLoop.IsEmpty || settings.LastDocumentDirty)
            {
                document.SetSampleLoop(sessionLoop, markDirty: false);
            }

            var sessionRegions = LoadSessionRegions(settings);
            if (sessionRegions.Length > 0 || settings.LastDocumentDirty)
            {
                document.SetRegions(sessionRegions, markDirty: false);
            }
            document.Selection = new WaveSelection(settings.LastSelectionStart, settings.LastSelectionEnd)
                .Clamp(document.FrameCount);
            var session = new DocumentSession(document)
            {
                TimeZoom = settings.LastTimeZoom,
                AmpZoom = settings.LastAmpZoom,
                ViewStart = settings.LastViewStart,
                PlayheadFrame = settings.LastCursorFrame,
            };
            _sessions.Add(session);
            BindWorkspace(session);
            Waveform.Refresh();
            Overview.InvalidateVisual();
        }
        catch
        {
            // 前回ファイルが無い・壊れているときは空のまま起動する。
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

    private static WaveRegion[] LoadSessionRegions(AppSettings settings)
    {
        var starts = settings.LastRegionStarts ?? [];
        var ends = settings.LastRegionEnds ?? [];
        var names = settings.LastRegionNames ?? [];
        if (starts.Length > 0 && starts.Length == ends.Length)
        {
            var regions = new WaveRegion[starts.Length];
            for (var i = 0; i < starts.Length; i++)
            {
                var name = i < names.Length ? names[i] : string.Empty;
                regions[i] = new WaveRegion(new WaveSelection(starts[i], ends[i]), name);
            }

            return regions;
        }

        var legacy = new WaveSelection(settings.LastRegionStart, settings.LastRegionEnd);
        return legacy.IsEmpty ? [] : [new WaveRegion(legacy, string.Empty)];
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

    private static MarkerSnapshot[] LoadSessionMarkers(AppSettings settings)
    {
        var frames = settings.LastMarkerFrames ?? [];
        var comments = settings.LastMarkerComments ?? [];
        var markers = new MarkerSnapshot[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            markers[i] = new MarkerSnapshot(frames[i], i < comments.Length ? comments[i] : string.Empty);
        }

        return markers;
    }

    private sealed record ApiItem(AudioOutputApi Api, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DeviceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
