using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal partial class AudioSettingsWindow : Window
{
    private readonly FadeCurveRow _fadeInRow;
    private readonly FadeCurveRow _fadeOutRow;
    private ContextMenu? _fadeCurveMenu;

    public AudioOutputSettings SelectedSettings { get; private set; }

    public FadeShape FadeInCurve => _fadeInRow.Curve;

    public FadeShape FadeOutCurve => _fadeOutRow.Curve;

    public UiLanguageChoice SelectedLanguage { get; private set; }

    public double SelectedLoudnessTargetLufs { get; private set; }

    public int SelectedMp3BitRate { get; private set; }

    public string SelectedLameExePath { get; private set; } = string.Empty;

    public string SelectedLameOptions { get; private set; } = Mp3Encode.DefaultLameOptions;

    public int SelectedExportParallelism { get; private set; }

    public SpeakerPreset[] SelectedPresets { get; private set; } = [];

    public string[] SelectedVisibleSpeakerIds { get; private set; } = SpeakerPreset.DefaultVisibleIds;

    public string SelectedActiveSpeakerId { get; private set; } = string.Empty;

    public string SelectedRecordDeviceId { get; private set; } = string.Empty;

    private readonly List<SpeakerPreset> _presets;
    private readonly HashSet<string> _visibleIds;
    private SpeakerPreset? _editingSpeaker;
    private readonly ChannelRoutingEditor _inputEditor;
    private readonly ChannelRoutingEditor _outputEditor;
    private readonly SettingsIoProbe _probe = new();
    private readonly DispatcherTimer _meterTimer;
    private bool _syncingAssociations;
    private bool _syncingSpeaker;
    private bool _syncingVisibility;
    private bool _speakerDirty;
    private int[] _recordInputMap;
    private int[] _playbackOutputMap;
    private int[] _fileChannelMap;
    private string[] _inputPortNames = [];
    private string[] _outputPortNames = [];
    private float[] _meterPeaks = [];

    public AudioSettingsWindow(
        AudioOutputSettings current,
        FadeShape fadeIn,
        FadeShape fadeOut,
        UiLanguageChoice language,
        double loudnessTargetLufs,
        int mp3BitRate,
        string lameExePath,
        string lameOptions,
        int exportParallelism,
        IEnumerable<SpeakerPreset> presets,
        string activeSpeakerId,
        IEnumerable<string>? visibleSpeakerIds = null)
    {
        SelectedSettings = current;
        SelectedLanguage = language;
        SelectedLoudnessTargetLufs = LoudnessMeterEngine.ClampTargetLufs(loudnessTargetLufs);
        SelectedMp3BitRate = Mp3Encode.ClampWindowsBitRate(mp3BitRate);
        SelectedLameExePath = lameExePath ?? string.Empty;
        SelectedLameOptions = lameOptions ?? string.Empty;
        SelectedExportParallelism = exportParallelism;
        _presets = [.. SpeakerPreset.CloneAll(presets)];
        var visible = SpeakerPreset.NormalizeVisibleIds(visibleSpeakerIds);
        _visibleIds = new HashSet<string>(visible, StringComparer.OrdinalIgnoreCase);
        SelectedVisibleSpeakerIds = visible;
        SelectedActiveSpeakerId = string.IsNullOrWhiteSpace(activeSpeakerId)
            ? _presets[0].Id
            : activeSpeakerId;
        var speaker = FindPreset(SelectedActiveSpeakerId) ?? _presets[0];
        SelectedActiveSpeakerId = speaker.Id;
        SelectedSettings = speaker.ToAudioOutputSettings();
        SelectedRecordDeviceId = AudioCaptureFactory.ResolveRecordDeviceId(
            SelectedSettings.Api,
            SelectedSettings.DeviceId);
        _recordInputMap = [.. speaker.RecordInputMap ?? []];
        _playbackOutputMap = [.. speaker.PlaybackOutputMap ?? []];
        _fileChannelMap = [.. speaker.FileChannelMap ?? []];
        InitializeComponent();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _meterTimer.Tick += (_, _) => RefreshInputMeters();
        _inputEditor = new ChannelRoutingEditor(RecordInputHost, showMeters: true);
        _outputEditor = new ChannelRoutingEditor(PlaybackOutputHost, showTestButtons: true);
        _inputEditor.MapChanged += OnInputMapChanged;
        _outputEditor.MapChanged += OnOutputMapChanged;
        _outputEditor.TestClicked += OnOutputTest;
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        VerticalResizeOnly.LockWidth(this);
        Title = UiStrings.DialogSettingsTitle;

        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Auto, UiStrings.LabelLanguageAuto));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Japanese, UiStrings.LabelLanguageJapanese));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.English, UiStrings.LabelLanguageEnglish));
        SelectLanguage(language);

        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.WaveOut, UiStrings.LabelAudioApiWaveOut));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Wasapi, UiStrings.LabelAudioApiWasapi));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Asio, UiStrings.LabelAudioApiAsio));

        ActionButtonLooks.ApplyAccent(OkButton);
        ActionButtonLooks.ApplyClear(CancelButton);
        ActionButtonLooks.ApplyClear(LameBrowseButton);

        _fadeInRow = CreateFadeRow(UiStrings.LabelDefaultFadeIn, fadeIn, isFadeIn: true);
        _fadeOutRow = CreateFadeRow(UiStrings.LabelDefaultFadeOut, fadeOut, isFadeIn: false);
        FadeRowsHost.Children.Add(_fadeInRow.Host);
        FadeRowsHost.Children.Add(_fadeOutRow.Host);

        FillSpeakers(SelectedActiveSpeakerId);
        FillSpeakerVisibility();
        LoadSpeakerEditors(FindPreset(SelectedActiveSpeakerId) ?? _presets[0], releaseDevice: false);
        RebuildRouting();
        LoudnessTargetBox.Text = SelectedLoudnessTargetLufs.ToString("0.#", CultureInfo.InvariantCulture);
        FillWindowsBitRates(SelectedMp3BitRate);
        LamePathBox.Text = SelectedLameExePath;
        LameOptionsBox.Text = Mp3Encode.ResolveLameOptions(SelectedLameOptions);
        FillExportParallelism(SelectedExportParallelism);
        FillAssociations();
        ApplyTips();
        ReflowSettingsWindow();
        WindowPlacement.TryApplySettings(this, AppStorage.Settings);
        Loaded += (_, _) =>
        {
            if (Owner is { Topmost: true })
            {
                Topmost = true;
            }

            ReflowSettingsWindow();
            if (IsAudioTabSelected())
            {
                StartProbe();
            }
        };
    }

    private void ApplyTips()
    {
        TipService.Set(LanguageLabel, UiStrings.TipUiLanguage);
        TipService.Set(LanguageCombo, UiStrings.TipUiLanguage);
        TipService.Set(AssociationHeader, UiStrings.TipFileAssociations);
        TipService.Set(SpeakerLabel, UiStrings.TipSpeakerPreset);
        TipService.Set(SpeakerCombo, UiStrings.TipSpeakerPreset);
        TipService.Set(SpeakerVisibilityHeader, UiStrings.TipSpeakerVisibility);
        TipService.Set(SpeakerVisibilityHost, UiStrings.TipSpeakerVisibility);
        TipService.Set(ApiLabel, UiStrings.TipAudioApi);
        TipService.Set(ApiCombo, UiStrings.TipAudioApi);
        TipService.Set(DeviceLabel, UiStrings.TipAudioDevice);
        TipService.Set(DeviceCombo, UiStrings.TipAudioDevice);
        TipService.Set(InputHeader, UiStrings.TipSettingsInput);
        TipService.Set(OutputHeader, UiStrings.TipSettingsOutput);
        TipService.Set(RecordInputHost, UiStrings.TipRecordInputMap);
        TipService.Set(InputStatus, UiStrings.TipInputLevel);
        TipService.Set(PlaybackOutputHost, UiStrings.TipPlaybackOutputMap);
        TipService.Set(LoudnessTargetLabel, UiStrings.TipLoudnessTarget);
        TipService.Set(LoudnessTargetBox, UiStrings.TipLoudnessTarget);
        TipService.Set(LoudnessTargetUnit, UiStrings.TipLoudnessTarget);
        TipService.Set(FadeDefaultsHeader, UiStrings.TipFadeCurveDefaults);
        TipService.Set(Mp3Header, UiStrings.TipMp3Encode);
        TipService.Set(WindowsBitRateLabel, UiStrings.TipWindowsMp3BitRate);
        TipService.Set(WindowsBitRateCombo, UiStrings.TipWindowsMp3BitRate);
        TipService.Set(LamePathLabel, UiStrings.TipLamePath);
        TipService.Set(LamePathBox, UiStrings.TipLamePath);
        TipService.Set(LameBrowseButton, UiStrings.TipLameBrowse);
        TipService.Set(LameOptionsLabel, UiStrings.TipLameOptions);
        TipService.Set(LameOptionsBox, UiStrings.TipLameOptions);
        TipService.Set(ExportParallelLabel, UiStrings.TipExportParallel);
        TipService.Set(ExportParallelCombo, UiStrings.TipExportParallel);
        TipService.Set(OkButton, UiStrings.TipSettingsOk);
        TipService.Set(CancelButton, UiStrings.TipSettingsCancel);
    }

    private void ApiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingSpeaker)
        {
            return;
        }

        MarkSpeakerDirty();
        ReloadDevices(preferredDeviceId: null);
        RefreshRouting(releaseDevice: true);
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingSpeaker)
        {
            return;
        }

        MarkSpeakerDirty();
        RefreshRouting(releaseDevice: true);
    }

    private void SpeakerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _syncingSpeaker || SpeakerCombo.SelectedItem is not SpeakerItem next)
        {
            return;
        }

        var currentId = _editingSpeaker?.Id;
        if (!ShouldConfirmSpeakerSave(_speakerDirty, currentId, next.Preset.Id))
        {
            LoadSpeakerEditors(next.Preset, releaseDevice: true);
            return;
        }

        var answer = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmSpeakerSettingsSave(_editingSpeaker!.DisplayName()),
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Cancel)
        {
            RestoreSpeakerCombo(currentId);
            return;
        }

        if (answer == MessageBoxResult.Yes)
        {
            FlushCurrentSpeaker();
        }

        LoadSpeakerEditors(next.Preset, releaseDevice: true);
    }

    private void FillAssociations()
    {
        AssociationHost.Children.Clear();
        var style = TryFindResource("DarkCheckBoxStyle") as Style;
        _syncingAssociations = true;
        try
        {
            foreach (var ext in FileAssociations.Extensions)
            {
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = FileAssociations.FormatLabel(ext) },
                    IsChecked = FileAssociations.IsAssociated(ext),
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = ext,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                if (style is not null)
                {
                    box.Style = style;
                }

                box.Checked += Association_Changed;
                box.Unchecked += Association_Changed;
                TipService.Set(box, UiStrings.TipFileAssociations);
                AssociationHost.Children.Add(box);
            }
        }
        finally
        {
            _syncingAssociations = false;
        }
    }

    private void Association_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAssociations || sender is not CheckBox box || box.Tag is not string ext)
        {
            return;
        }

        var want = box.IsChecked == true;
        try
        {
            FileAssociations.SetAssociated(ext, want);
        }
        catch (Exception ex)
        {
            var text = ex is InvalidOperationException
                ? ex.Message
                : UiStrings.ErrFileAssociationFailed(ex.Message);
            OwnerCenteredMessageBox.Show(
                this,
                text,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        var actual = FileAssociations.IsAssociated(ext);
        if (box.IsChecked == actual)
        {
            return;
        }

        _syncingAssociations = true;
        try
        {
            box.IsChecked = actual;
        }
        finally
        {
            _syncingAssociations = false;
        }
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApiCombo.SelectedItem is not ApiItem api)
        {
            DialogResult = false;
            return;
        }

        var deviceId = DeviceCombo.SelectedItem is DeviceItem device
            ? device.Id
            : string.Empty;
        SelectedSettings = new AudioOutputSettings(api.Api, deviceId);
        SelectedLanguage = LanguageCombo.SelectedItem is LanguageItem item
            ? item.Choice
            : UiLanguageChoice.Auto;
        if (!LoudnessMeterEngine.TryParseTargetLufs(LoudnessTargetBox.Text, out var target))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorLoudnessTargetRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LoudnessTargetBox.Focus();
            LoudnessTargetBox.SelectAll();
            return;
        }

        SelectedLoudnessTargetLufs = target;
        SelectedMp3BitRate = WindowsBitRateCombo.SelectedItem is BitRateItem bitRate
            ? bitRate.Kbps
            : Mp3Encode.DefaultWindowsBitRateKbps;
        SelectedLameExePath = LamePathBox.Text.Trim();
        SelectedLameOptions = Mp3Encode.ResolveLameOptions(LameOptionsBox.Text);
        SelectedExportParallelism = ExportParallelCombo.SelectedItem is ParallelismItem parallel
            ? parallel.Value
            : AudioExport.AutoParallelism;
        FlushCurrentSpeaker();
        SelectedSettings = ReadOutputSettings();
        SelectedPresets = SpeakerPreset.CloneAll(_presets);
        SelectedVisibleSpeakerIds = SpeakerPreset.NormalizeVisibleIds(_visibleIds);
        SelectedActiveSpeakerId = CurrentSpeaker()?.Id ?? _presets[0].Id;
        SelectedRecordDeviceId = ReadRecordDeviceId();
        StopProbeUi();
        DialogResult = true;
    }

    private void LameBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterLameExe,
            Title = UiStrings.LabelLamePath,
            CheckFileExists = true,
        };
        var current = LamePathBox.Text.Trim().Trim('"');
        var dir = Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            LamePathBox.Text = dialog.FileName;
        }
    }

    private void FillWindowsBitRates(int currentKbps)
    {
        WindowsBitRateCombo.Items.Clear();
        BitRateItem? selected = null;
        var seen = new HashSet<int>();
        foreach (var kbps in Mp3Encode.WindowsBitRates)
        {
            var item = new BitRateItem(kbps);
            WindowsBitRateCombo.Items.Add(item);
            seen.Add(kbps);
            if (kbps == currentKbps)
            {
                selected = item;
            }
        }

        if (!seen.Contains(currentKbps))
        {
            var extra = new BitRateItem(currentKbps);
            WindowsBitRateCombo.Items.Add(extra);
            selected = extra;
        }

        WindowsBitRateCombo.SelectedItem = selected ?? WindowsBitRateCombo.Items[0];
    }

    private void FillExportParallelism(int current)
    {
        var cores = Environment.ProcessorCount;
        var max = AudioExport.MaxWorkers(cores);
        var stored = current <= AudioExport.AutoParallelism
            ? AudioExport.AutoParallelism
            : Math.Clamp(current, 1, max);
        ExportParallelCombo.Items.Clear();
        ParallelismItem? selected = null;
        var auto = new ParallelismItem(
            AudioExport.AutoParallelism,
            UiStrings.LabelExportParallelAuto(AudioExport.AutoWorkers(cores)));
        ExportParallelCombo.Items.Add(auto);
        if (stored == AudioExport.AutoParallelism)
        {
            selected = auto;
        }

        for (var n = 1; n <= max; n++)
        {
            var item = new ParallelismItem(n, n.ToString(CultureInfo.InvariantCulture));
            ExportParallelCombo.Items.Add(item);
            if (n == stored)
            {
                selected = item;
            }
        }

        ExportParallelCombo.SelectedItem = selected ?? auto;
        ComboBoxFit.ApplySelected(ExportParallelCombo);
    }

    private void ExportParallelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ExportParallelCombo.IsLoaded)
        {
            ComboBoxFit.ApplySelected(ExportParallelCombo);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StopProbeUi();
        DialogResult = false;
    }

    private void SettingsTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || e.OriginalSource is not (TabControl or TabItem))
        {
            return;
        }

        if (IsAudioTabSelected())
        {
            StartProbe();
            return;
        }

        StopProbeUi();
    }

    private void OnOutputTest(int channel, SettingsProbeKind kind)
    {
        var labels = _outputEditor.ChannelNames;
        var label = (uint)channel < (uint)labels.Length ? labels[channel] : string.Empty;
        if (kind == SettingsProbeKind.Voice && SettingsTone.IsLfe(label))
        {
            return;
        }

        if (_probe.TonePlaying && _probe.ToneKind == kind && _probe.ToneChannel == channel)
        {
            _probe.SetTone(
                false,
                kind,
                channel,
                ReadOutputSettings(),
                ReadPlaybackLayout(),
                _outputEditor.ReadMap());
            RefreshTestButtons();
            return;
        }

        Func<int, float[]>? voiceFactory = null;
        if (kind == SettingsProbeKind.Voice)
        {
            voiceFactory = rate =>
            {
                if (!SettingsChannelVoice.TryRender(label, rate, out var samples, out var voiceError))
                {
                    throw new InvalidOperationException(voiceError ?? UiStrings.ErrorChannelVoiceFailed);
                }

                return samples;
            };
        }

        var error = _probe.SetTone(
            true,
            kind,
            channel,
            ReadOutputSettings(),
            ReadPlaybackLayout(),
            _outputEditor.ReadMap(),
            voiceFactory);
        if (error is not null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        RefreshTestButtons();
    }

    private void OnInputMapChanged()
    {
        MarkSpeakerDirty();
        _recordInputMap = _inputEditor.ReadMap();
        SyncFileChannelMap(_inputEditor.ReadFileMap(), fromInput: true);
        _probe.SetInputMap(_recordInputMap);
    }

    private void OnOutputMapChanged()
    {
        MarkSpeakerDirty();
        _playbackOutputMap = _outputEditor.ReadMap();
        SyncFileChannelMap(_outputEditor.ReadFileMap(), fromInput: false);
        _probe.SetOutputMap(_playbackOutputMap);
    }

    private void SyncFileChannelMap(int[] map, bool fromInput)
    {
        if (_fileChannelMap.AsSpan().SequenceEqual(map))
        {
            return;
        }

        _fileChannelMap = map;
        if (fromInput)
        {
            _outputEditor.SetFileMap(map);
        }
        else
        {
            _inputEditor.SetFileMap(map);
        }
    }

    private void RestartProbeIfVisible()
    {
        if (!IsLoaded || !IsAudioTabSelected())
        {
            return;
        }

        StartProbe();
    }

    private void StartProbe()
    {
        _recordInputMap = _inputEditor.ReadMap();
        _playbackOutputMap = _outputEditor.ReadMap();
        _probe.SetOutputMap(_playbackOutputMap);
        var error = _probe.StartMonitor(
            ReadOutputSettings(),
            ReadRecordDeviceId(),
            ReadRecordLayout(),
            ReadPlaybackLayout(),
            _recordInputMap);
        SetInputStatus(error);
        RefreshTestButtons();
        if (!_meterTimer.IsEnabled)
        {
            _meterTimer.Start();
        }

        ApplyLiveDevicePortNames();
        ReflowSettingsWindow();
    }

    internal static bool ShouldCaptureEditorMaps(bool loadingSpeaker, bool syncingSpeaker = false) =>
        !loadingSpeaker && !syncingSpeaker;

    private void RefreshRouting(bool releaseDevice, bool loadingSpeaker = false)
    {
        if (ShouldCaptureEditorMaps(loadingSpeaker, _syncingSpeaker))
        {
            if (_inputEditor.ChannelNames.Length > 0)
            {
                _recordInputMap = _inputEditor.ReadMap();
                _fileChannelMap = _inputEditor.ReadFileMap();
            }

            if (_outputEditor.ChannelNames.Length > 0)
            {
                _playbackOutputMap = _outputEditor.ReadMap();
                _fileChannelMap = _outputEditor.ReadFileMap();
            }
        }

        if (releaseDevice)
        {
            _probe.Stop();
        }

        RebuildRouting();
        ReflowSettingsWindow();
        RestartProbeIfVisible();
    }

    private void ApplyLiveDevicePortNames()
    {
        if (_probe.TryGetPortNames(input: true, out var inputs))
        {
            _inputPortNames = inputs;
            _inputEditor.ReplacePortNames(inputs);
            _recordInputMap = ChannelRouter.Normalize(
                _inputEditor.ReadMap(),
                ReadRecordLayout().Channels,
                inputs.Length);
            _probe.SetInputMap(_recordInputMap);
        }

        if (_probe.TryGetPortNames(input: false, out var outputs))
        {
            _outputPortNames = outputs;
            _outputEditor.ReplacePortNames(outputs);
            _playbackOutputMap = ChannelRouter.Normalize(
                _outputEditor.ReadMap(),
                ReadPlaybackLayout().Channels,
                outputs.Length);
            _probe.SetOutputMap(_playbackOutputMap);
        }
    }

    private void StopProbeUi()
    {
        _meterTimer.Stop();
        _probe.Stop();
        SetInputStatus(null);
        RefreshTestButtons();
        _inputEditor.ApplyPeaks([]);
    }

    private void RefreshInputMeters()
    {
        var n = _inputEditor.ChannelNames.Length;
        if (_meterPeaks.Length != n)
        {
            _meterPeaks = new float[n];
        }

        _probe.CopyPeaks(_meterPeaks);
        _inputEditor.ApplyPeaks(_meterPeaks);
    }

    private void RefreshTestButtons()
    {
        if (_probe.TonePlaying)
        {
            _outputEditor.SetTestState(_probe.ToneChannel, _probe.ToneKind);
            return;
        }

        _outputEditor.SetTestState(ChannelRouter.Off, kind: null);
    }

    private void SetInputStatus(string? message)
    {
        var text = string.IsNullOrWhiteSpace(message) ? string.Empty : message;
        InputStatus.Text = text;
        InputStatus.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClampToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        var maxH = Math.Max(MinHeight, Math.Min(MaxHeight, work.Height - 32));
        MaxHeight = maxH;
        if (Height > maxH)
        {
            Height = maxH;
        }

        if (Left + Width > work.Right)
        {
            Left = Math.Max(work.X, work.Right - Width);
        }
    }

    private bool IsAudioTabSelected() =>
        ReferenceEquals(SettingsTabs.SelectedItem, AudioTab);

    private AudioOutputSettings ReadOutputSettings()
    {
        var api = ApiCombo.SelectedItem is ApiItem item ? item.Api : AudioOutputApi.WaveOut;
        var deviceId = DeviceCombo.SelectedItem is DeviceItem device ? device.Id : string.Empty;
        return new AudioOutputSettings(api, deviceId);
    }

    private string ReadRecordDeviceId()
    {
        var settings = ReadOutputSettings();
        return AudioCaptureFactory.ResolveRecordDeviceId(settings.Api, settings.DeviceId);
    }

    private string[] ResolvePortNames(bool input)
    {
        var api = ApiCombo.SelectedItem is ApiItem item ? item.Api : AudioOutputApi.WaveOut;
        var id = input
            ? ReadRecordDeviceId()
            : ReadOutputSettings().DeviceId;
        if (api == AudioOutputApi.Asio && _probe.TryGetPortNames(input, out var live))
        {
            return live;
        }

        var queried = AudioCaptureFactory.QueryPortNames(api, id, input);
        if (queried.Length > 0)
        {
            return queried;
        }

        var cache = input ? _inputPortNames : _outputPortNames;
        return cache.Length > 0 ? cache : DevicePortNames.Numbered(ChannelLayout.MaxChannels, input);
    }

    private ChannelLayout ReadRecordLayout() =>
        ChannelLayout.Parse((_editingSpeaker ?? CurrentSpeaker())?.Id);

    private ChannelLayout ReadPlaybackLayout() =>
        ReadRecordLayout();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Tab || (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return;
        }

        var delta = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
        CycleSettingsTab(delta);
        e.Handled = true;
    }

    private void CycleSettingsTab(int delta)
    {
        var count = SettingsTabs.Items.Count;
        var next = WrapTabIndex(SettingsTabs.SelectedIndex, count, delta);
        if (next != SettingsTabs.SelectedIndex)
        {
            SettingsTabs.SelectedIndex = next;
        }
    }

    internal static int WrapTabIndex(int index, int count, int delta)
    {
        if (count < 2)
        {
            return Math.Max(0, index);
        }

        if (index < 0)
        {
            index = 0;
        }

        return (index + delta % count + count) % count;
    }

    internal static bool ShouldConfirmSpeakerSave(bool dirty, string? currentId, string? nextId)
    {
        if (!dirty || string.IsNullOrWhiteSpace(nextId))
        {
            return false;
        }

        return currentId is null
            || !currentId.Equals(nextId, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkSpeakerDirty()
    {
        if (!IsLoaded || _syncingSpeaker)
        {
            return;
        }

        _speakerDirty = true;
    }

    private void RestoreSpeakerCombo(string? id)
    {
        _syncingSpeaker = true;
        try
        {
            foreach (SpeakerItem item in SpeakerCombo.Items)
            {
                if (item.Preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    SpeakerCombo.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _syncingSpeaker = false;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_fadeCurveMenu is { IsOpen: true })
        {
            _fadeCurveMenu.IsOpen = false;
            e.Handled = true;
            return;
        }

        StopProbeUi();
        DialogResult = false;
        Close();
        e.Handled = true;
    }

    private void SelectLanguage(UiLanguageChoice language)
    {
        foreach (LanguageItem item in LanguageCombo.Items)
        {
            if (item.Choice == language)
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }

        LanguageCombo.SelectedIndex = 0;
    }

    private void SelectApi(AudioOutputApi api)
    {
        foreach (ApiItem item in ApiCombo.Items)
        {
            if (item.Api == api)
            {
                ApiCombo.SelectedItem = item;
                return;
            }
        }

        ApiCombo.SelectedIndex = 0;
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

    private void FitSettingCombos()
    {
        var deviceMax = Math.Max(80, SystemParameters.WorkArea.Width - 200);
        ComboBoxFit.Apply(LanguageCombo);
        ComboBoxFit.Apply(SpeakerCombo);
        ComboBoxFit.Apply(ApiCombo);
        ComboBoxFit.Apply(DeviceCombo, deviceMax);
        ComboBoxFit.Apply(WindowsBitRateCombo);
        ComboBoxFit.ApplySelected(ExportParallelCombo);
    }

    private void ReflowSettingsWindow()
    {
        FitSettingCombos();
        _inputEditor.Refit();
        _outputEditor.Refit();
        FitWindowToAudio();
        ClampToWorkArea();
    }

    private void FitWindowToAudio()
    {
        var pad = DesignMetrics.AudioPad.Left + DesignMetrics.AudioPad.Right;
        var chrome = WindowChromeWidth();
        var content = Math.Max(AudioTabContentWidth(), SettingsTabBarWidth());
        var width = Math.Ceiling(
            content + pad + chrome + DesignMetrics.SettingsWindowContentMargin);
        var max = Math.Max(DesignMetrics.SettingsWindowMinWidth, SystemParameters.WorkArea.Width - 32);
        width = Math.Clamp(width, DesignMetrics.SettingsWindowMinWidth, max);
        MinWidth = width;
        MaxWidth = width;
        Width = width;
    }

    private double AudioTabContentWidth()
    {
        var speaker = LabeledComboWidth(SpeakerLabel, SpeakerCombo);
        var device = LabeledComboWidth(ApiLabel, ApiCombo)
            + 16
            + LabeledComboWidth(DeviceLabel, DeviceCombo);
        var top = Math.Max(speaker, device);
        var gutter = DesignMetrics.SettingsScrollBarGap + DesignMetrics.SettingsScrollBarWidth;
        var record = Max(
            LabelWidth(InputHeader),
            _inputEditor.FittedRowWidth) + gutter;
        var play = Max(
            LabelWidth(OutputHeader),
            _outputEditor.FittedRowWidth) + gutter;
        var columns = record + DesignMetrics.SettingsColumnGap + play;
        return Math.Max(top, columns);
    }

    private double SettingsTabBarWidth()
    {
        var headers = new[]
        {
            UiStrings.LabelSettingsTabGeneral,
            UiStrings.LabelSettingsTabLayouts,
            UiStrings.LabelSettingsTabAudio,
            UiStrings.LabelSettingsTabEditing,
            UiStrings.LabelSettingsTabExport,
        };
        var width = 0d;
        foreach (var header in headers)
        {
            width += ComboBoxFit.MeasureText(this, header, 12) + 24;
        }

        return width;
    }

    private double LabeledComboWidth(TextBlock label, ComboBox combo) =>
        LabelWidth(label) + DesignMetrics.SettingsLabelComboGap + combo.Width;

    private double LabelWidth(TextBlock label)
    {
        if (label.ActualWidth > 1)
        {
            return label.ActualWidth;
        }

        return ComboBoxFit.MeasureText(label, label.Text ?? string.Empty, label.FontSize, label.FontWeight);
    }

    private static double Max(params double[] values)
    {
        var max = 0d;
        foreach (var value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
    }

    private double WindowChromeWidth()
    {
        if (Content is FrameworkElement content && content.ActualWidth > 1 && ActualWidth > content.ActualWidth)
        {
            return ActualWidth - content.ActualWidth;
        }

        return SystemParameters.ResizeFrameVerticalBorderWidth * 2 + 2;
    }

    private void FillSpeakers(string selectedId)
    {
        _syncingSpeaker = true;
        try
        {
            SpeakerCombo.Items.Clear();
            SpeakerItem? selected = null;
            foreach (var preset in SpeakerPreset.FilterMenu(_presets, _visibleIds, selectedId))
            {
                var item = new SpeakerItem(preset);
                SpeakerCombo.Items.Add(item);
                if (preset.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
                {
                    selected = item;
                }
            }

            SpeakerCombo.SelectedItem = selected ?? (SpeakerCombo.Items.Count > 0 ? SpeakerCombo.Items[0] : null);
        }
        finally
        {
            _syncingSpeaker = false;
        }
    }

    private void FillSpeakerVisibility()
    {
        SpeakerVisibilityHost.Children.Clear();
        var style = TryFindResource("DarkCheckBoxStyle") as Style;
        _syncingVisibility = true;
        try
        {
            foreach (var preset in _presets)
            {
                var box = new CheckBox
                {
                    Content = new TextBlock { Text = preset.DisplayName() },
                    IsChecked = _visibleIds.Contains(preset.Id),
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = preset.Id,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                if (style is not null)
                {
                    box.Style = style;
                }

                box.Checked += SpeakerVisibility_Changed;
                box.Unchecked += SpeakerVisibility_Changed;
                TipService.Set(box, UiStrings.TipSpeakerVisibility);
                SpeakerVisibilityHost.Children.Add(box);
            }
        }
        finally
        {
            _syncingVisibility = false;
        }
    }

    private void SpeakerVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingVisibility || sender is not CheckBox box || box.Tag is not string id)
        {
            return;
        }

        if (box.IsChecked == true)
        {
            _visibleIds.Add(id);
        }
        else if (CheckedVisibilityCount() == 0)
        {
            _syncingVisibility = true;
            try
            {
                box.IsChecked = true;
            }
            finally
            {
                _syncingVisibility = false;
            }

            return;
        }
        else
        {
            _visibleIds.Remove(id);
        }

        FillSpeakers(SelectedActiveSpeakerId);
        ComboBoxFit.Apply(SpeakerCombo);
    }

    private int CheckedVisibilityCount()
    {
        var n = 0;
        foreach (var child in SpeakerVisibilityHost.Children)
        {
            if (child is CheckBox box && box.IsChecked == true)
            {
                n++;
            }
        }

        return n;
    }

    private void LoadSpeakerEditors(SpeakerPreset speaker, bool releaseDevice)
    {
        _syncingSpeaker = true;
        try
        {
            SelectedActiveSpeakerId = speaker.Id;
            SelectApi(AudioOutputSettings.ParseApi(speaker.AudioApi));
            ReloadDevices(speaker.AudioDeviceId);
            _recordInputMap = [.. speaker.RecordInputMap ?? []];
            _playbackOutputMap = [.. speaker.PlaybackOutputMap ?? []];
            _fileChannelMap = [.. speaker.FileChannelMap ?? []];
            _editingSpeaker = speaker;
        }
        finally
        {
            _syncingSpeaker = false;
        }

        RefreshRouting(releaseDevice, loadingSpeaker: true);
        _speakerDirty = false;
    }

    private void FlushCurrentSpeaker()
    {
        if ((_editingSpeaker ?? CurrentSpeaker()) is not { } speaker)
        {
            return;
        }

        if (_inputEditor.ChannelNames.Length > 0)
        {
            _recordInputMap = _inputEditor.ReadMap();
            _fileChannelMap = _inputEditor.ReadFileMap();
        }

        if (_outputEditor.ChannelNames.Length > 0)
        {
            _playbackOutputMap = _outputEditor.ReadMap();
            _fileChannelMap = _outputEditor.ReadFileMap();
        }

        speaker.ApplyAudioOutput(ReadOutputSettings());
        speaker.RecordInputMap = SpeakerPreset.SnapshotMap(_recordInputMap, speaker.Channels);
        speaker.PlaybackOutputMap = SpeakerPreset.SnapshotMap(_playbackOutputMap, speaker.Channels);
        speaker.FileChannelMap = SpeakerPreset.SnapshotMap(_fileChannelMap, speaker.Channels);
        speaker.Normalized();
    }

    private SpeakerPreset? CurrentSpeaker() =>
        SpeakerCombo.SelectedItem is SpeakerItem item ? item.Preset : FindPreset(SelectedActiveSpeakerId);

    private SpeakerPreset? FindPreset(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        foreach (var preset in _presets)
        {
            if (preset.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                return preset;
            }
        }

        return null;
    }

    private int ReadSpeakerChannels() => ReadRecordLayout().Channels;

    private void RebuildRouting()
    {
        var layout = ReadRecordLayout();
        var inputPorts = ResolvePortNames(input: true);
        var outputPorts = ResolvePortNames(input: false);
        _inputPortNames = inputPorts;
        _outputPortNames = outputPorts;
        var fileMap = ChannelRouter.Normalize(_fileChannelMap, layout.Channels, ChannelLayout.MaxChannels);
        _inputEditor.Rebuild(
            layout.Labels,
            inputPorts,
            ChannelRouter.Normalize(_recordInputMap, layout.Channels, inputPorts.Length),
            fileMap);
        _outputEditor.Rebuild(
            layout.Labels,
            outputPorts,
            ChannelRouter.Normalize(_playbackOutputMap, layout.Channels, outputPorts.Length),
            fileMap);
        RefreshTestButtons();
    }

    private FadeCurveRow CreateFadeRow(string labelText, FadeShape curve, bool isFadeIn)
    {
        var rowHeight = DesignMetrics.FadeOptionRowHeight;
        var iconSide = FadeCurveIcons.WidthFor((int)Math.Round(rowHeight));

        var host = new Grid
        {
            Height = rowHeight,
            Margin = new Thickness(0, 0, 0, 2),
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iconSide) });

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")),
        };

        var iconHost = new Border
        {
            Width = iconSide,
            Height = rowHeight,
            Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush")),
            BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };

        var icon = new Image
        {
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconHost.Child = icon;
        iconHost.MouseEnter += (_, _) =>
            iconHost.Background = WpfControlHelpers.FrozenBrush(Theme.Get("TransportHoverBackBrush"));
        iconHost.MouseLeave += (_, _) =>
            iconHost.Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush"));

        host.Children.Add(label);
        host.Children.Add(iconHost);
        Grid.SetColumn(iconHost, 1);

        var row = new FadeCurveRow(host, label, icon, iconHost, curve, isFadeIn);
        RefreshFadeRowIcon(row);
        iconHost.MouseLeftButtonUp += (_, _) => ShowFadeCurvePicker(row);
        return row;
    }

    private void ShowFadeCurvePicker(FadeCurveRow row)
    {
        FadeCurveIcons.ShowPicker(
            row.IconHost,
            new Point(0, row.IconHost.ActualHeight),
            row.Curve,
            row.IsFadeIn,
            kind =>
            {
                row.Curve = kind;
                RefreshFadeRowIcon(row);
            },
            ref _fadeCurveMenu);
    }

    private static void RefreshFadeRowIcon(FadeCurveRow row)
    {
        row.Icon.Source = FadeCurveIcons.Create(row.Curve, row.IsFadeIn);
        TipService.Set(row.IconHost, UiStrings.LabelFadeCurve((int)row.Curve));
        TipService.Set(row.Label, UiStrings.LabelFadeCurve((int)row.Curve));
    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeCurveMenu = null;
        StopProbeUi();
        _probe.Dispose();
        WindowPlacement.CaptureSettings(this, AppStorage.Settings);
        AppStorage.Save();
        base.OnClosed(e);
    }

    private sealed class FadeCurveRow(
        Grid host,
        TextBlock label,
        Image icon,
        Border iconHost,
        FadeShape curve,
        bool isFadeIn)
    {
        public Grid Host { get; } = host;

        public TextBlock Label { get; } = label;

        public Image Icon { get; } = icon;

        public Border IconHost { get; } = iconHost;

        public FadeShape Curve { get; set; } = curve;

        public bool IsFadeIn { get; } = isFadeIn;
    }

    private sealed record LanguageItem(UiLanguageChoice Choice, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ApiItem(AudioOutputApi Api, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SpeakerItem(SpeakerPreset Preset)
    {
        public override string ToString() => Preset.DisplayName();
    }

    private sealed record DeviceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record BitRateItem(int Kbps)
    {
        public override string ToString() => $"{Kbps} {UiStrings.LabelKbps}";
    }

    private sealed record ParallelismItem(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
