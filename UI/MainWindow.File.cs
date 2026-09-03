using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            Filter = "Audio|*.wav;*.wave;*.aif;*.aiff;*.mp3|Wave|*.wav;*.wave|AIFF|*.aif;*.aiff|MP3|*.mp3|All|*.*",
            Title = UiStrings.MenuOpen,
        };
        var lastDir = Path.GetDirectoryName(AppStorage.Settings.LastDocumentPath);
        if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
        {
            dialog.InitialDirectory = lastDir;
        }
        if (dialog.ShowDialog(this) == true)
        {
            OpenPath(dialog.FileName);
        }
    }

    private void OpenPath(string path)
    {
        if (!OfferSaveIfDirty())
        {
            return;
        }

        try
        {
            var document = AudioCodec.Load(path);
            SetDocument(document);
            RememberOpenedPath(path, dirty: false, resetMarkers: false);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, $"{UiStrings.ErrorOpenFailed}\n{ex.Message}", UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool Save(bool saveAs)
    {
        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        var path = _document.SourcePath;
        var kind = path is null ? AudioFileKind.Wave : AudioCodec.DetectKind(path);
        if (saveAs
            || string.IsNullOrEmpty(path)
            || kind == AudioFileKind.Aiff
            || !AudioCodec.SaveExtensions.Any(ext =>
                ext.Equals(System.IO.Path.GetExtension(path), StringComparison.OrdinalIgnoreCase)))
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Wave|*.wav|MP3|*.mp3",
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
            AudioCodec.Save(_document, path, AppStorage.Settings.Mp3BitRate);
            _document.MarkSaved(path, AudioCodec.DetectKind(path));
            RememberOpenedPath(path, dirty: false, resetMarkers: false);
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
        if (_document is null)
        {
            return;
        }

        if (!OfferSaveIfDirty())
        {
            return;
        }

        SetDocument(null);
        ForgetClosedDocument();
    }

    private bool OfferSaveIfDirty()
    {
        if (_document is not { IsDirty: true })
        {
            return true;
        }

        var result = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmSave,
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

        return Save(saveAs: false);
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
        }
        else
        {
            StoreSessionMarkers(_document?.SnapshotMarkers());
            settings.LastSampleLoopStart = _document?.SampleLoop.StartFrame ?? 0;
            settings.LastSampleLoopEnd = _document?.SampleLoop.EndFrame ?? 0;
        }

        if (!dirty)
        {
            AppStorage.ClearSessionDocument();
        }

        AppStorage.Save();
    }

    private void RememberDocumentState()
    {
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
        settings.LastSampleLoopStart = _document.SampleLoop.StartFrame;
        settings.LastSampleLoopEnd = _document.SampleLoop.EndFrame;
        StoreSessionMarkers(_document.SnapshotMarkers());
        if (_document.IsDirty)
        {
            try
            {
                AudioCodec.SaveWave(_document, AppStorage.SessionDocumentPath);
                settings.LastDocumentDirty = true;
            }
            catch
            {
                settings.LastDocumentDirty = false;
            }
        }
        else
        {
            settings.LastDocumentDirty = false;
            AppStorage.ClearSessionDocument();
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

            SetDocument(document);
            document.ReplaceMarkers(LoadSessionMarkers(settings), markDirty: false);
            document.SetSampleLoop(
                new WaveSelection(settings.LastSampleLoopStart, settings.LastSampleLoopEnd));
            document.Selection = new WaveSelection(settings.LastSelectionStart, settings.LastSelectionEnd)
                .Clamp(document.FrameCount);
            Waveform.ApplyPersistedView(
                settings.LastTimeZoom,
                settings.LastAmpZoom,
                settings.LastViewStart,
                settings.LastCursorFrame);
            Waveform.LoopEnabled = true;
            Waveform.Refresh();
            Overview.InvalidateVisual();
            RefreshStatus();
            SyncViewChrome();
        }
        catch
        {
            // 前回ファイルが無い・壊れているときは空のまま起動する。
        }
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
