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

    public ChannelLayout SelectedRecordLayout { get; private set; } = ChannelLayout.Stereo;

    public ChannelLayout SelectedPlaybackLayout { get; private set; } = ChannelLayout.Stereo;

    public string SelectedRecordDeviceId { get; private set; } = string.Empty;

    public int[] SelectedRecordInputMap { get; private set; } = [];

    public int[] SelectedPlaybackOutputMap { get; private set; } = [];

    private readonly ChannelRoutingEditor _inputEditor;
    private readonly ChannelRoutingEditor _outputEditor;
    private readonly SettingsIoProbe _probe = new();
    private readonly DispatcherTimer _meterTimer;
    private int[] _recordInputMap;
    private int[] _playbackOutputMap;
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
        ChannelLayout recordLayout,
        ChannelLayout playbackLayout,
        int[] recordInputMap,
        int[] playbackOutputMap)
    {
        SelectedSettings = current;
        SelectedLanguage = language;
        SelectedLoudnessTargetLufs = LoudnessMeterEngine.ClampTargetLufs(loudnessTargetLufs);
        SelectedMp3BitRate = Mp3Encode.ClampWindowsBitRate(mp3BitRate);
        SelectedLameExePath = lameExePath ?? string.Empty;
        SelectedLameOptions = lameOptions ?? string.Empty;
        SelectedExportParallelism = exportParallelism;
        SelectedRecordLayout = recordLayout;
        SelectedPlaybackLayout = playbackLayout;
        SelectedRecordDeviceId = AudioCaptureFactory.ResolveRecordDeviceId(current.Api, current.DeviceId);
        _recordInputMap = recordInputMap ?? [];
        _playbackOutputMap = playbackOutputMap ?? [];
        InitializeComponent();
        _meterTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _meterTimer.Tick += (_, _) => RefreshInputMeters();
        _inputEditor = new ChannelRoutingEditor(RecordInputHost, showMeters: true);
        _outputEditor = new ChannelRoutingEditor(PlaybackOutputHost);
        _inputEditor.MapChanged += OnInputMapChanged;
        _outputEditor.MapChanged += OnOutputMapChanged;
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
        ActionButtonLooks.ApplyClear(SineButton);

        _fadeInRow = CreateFadeRow(UiStrings.LabelDefaultFadeIn, fadeIn, isFadeIn: true);
        _fadeOutRow = CreateFadeRow(UiStrings.LabelDefaultFadeOut, fadeOut, isFadeIn: false);
        FadeRowsHost.Children.Add(_fadeInRow.Host);
        FadeRowsHost.Children.Add(_fadeOutRow.Host);

        FillLayouts(RecordLayoutCombo, recordLayout);
        FillLayouts(PlaybackLayoutCombo, playbackLayout);
        SelectApi(current.Api);
        ReloadDevices(current.DeviceId);
        RebuildRouting();
        LoudnessTargetBox.Text = SelectedLoudnessTargetLufs.ToString("0.#", CultureInfo.InvariantCulture);
        FillWindowsBitRates(SelectedMp3BitRate);
        LamePathBox.Text = SelectedLameExePath;
        LameOptionsBox.Text = Mp3Encode.ResolveLameOptions(SelectedLameOptions);
        FillExportParallelism(SelectedExportParallelism);
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
        TipService.Set(ApiLabel, UiStrings.TipAudioApi);
        TipService.Set(ApiCombo, UiStrings.TipAudioApi);
        TipService.Set(DeviceLabel, UiStrings.TipAudioDevice);
        TipService.Set(DeviceCombo, UiStrings.TipAudioDevice);
        TipService.Set(InputHeader, UiStrings.TipSettingsInput);
        TipService.Set(OutputHeader, UiStrings.TipSettingsOutput);
        TipService.Set(RecordLayoutLabel, UiStrings.TipRecordLayout);
        TipService.Set(RecordLayoutCombo, UiStrings.TipRecordLayout);
        TipService.Set(PlaybackLayoutLabel, UiStrings.TipPlaybackLayout);
        TipService.Set(PlaybackLayoutCombo, UiStrings.TipPlaybackLayout);
        TipService.Set(RecordInputMapLabel, UiStrings.TipRecordInputMap);
        TipService.Set(RecordInputHost, UiStrings.TipRecordInputMap);
        TipService.Set(InputStatus, UiStrings.TipInputLevel);
        TipService.Set(PlaybackOutputMapLabel, UiStrings.TipPlaybackOutputMap);
        TipService.Set(PlaybackOutputHost, UiStrings.TipPlaybackOutputMap);
        TipService.Set(SineButton, UiStrings.TipSineMinusTwenty);
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
        if (!IsLoaded)
        {
            return;
        }

        ReloadDevices(preferredDeviceId: null);
        RefreshRouting(releaseDevice: true);
    }

    private void DeviceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshRouting(releaseDevice: true);
    }

    private void RecordLayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshRouting(releaseDevice: false);
    }

    private void PlaybackLayoutCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        RefreshRouting(releaseDevice: false);
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
        SelectedRecordLayout = RecordLayoutCombo.SelectedItem is LayoutItem layout
            ? layout.Layout
            : ChannelLayout.Stereo;
        SelectedPlaybackLayout = PlaybackLayoutCombo.SelectedItem is LayoutItem playLayout
            ? playLayout.Layout
            : ChannelLayout.Stereo;
        SelectedRecordDeviceId = ReadRecordDeviceId();
        SelectedRecordInputMap = _inputEditor.ReadMap();
        SelectedPlaybackOutputMap = _outputEditor.ReadMap();
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

    private void SineButton_Click(object sender, RoutedEventArgs e)
    {
        if (_probe.TonePlaying)
        {
            _probe.SetTone(false, ReadOutputSettings(), ReadPlaybackLayout(), _outputEditor.ReadMap());
            RefreshSineButton();
            return;
        }

        var error = _probe.SetTone(true, ReadOutputSettings(), ReadPlaybackLayout(), _outputEditor.ReadMap());
        if (error is not null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        RefreshSineButton();
    }

    private void OnInputMapChanged()
    {
        _recordInputMap = _inputEditor.ReadMap();
        _probe.SetInputMap(_recordInputMap);
    }

    private void OnOutputMapChanged()
    {
        _playbackOutputMap = _outputEditor.ReadMap();
        _probe.SetOutputMap(_playbackOutputMap);
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
        RefreshSineButton();
        if (!_meterTimer.IsEnabled)
        {
            _meterTimer.Start();
        }

        ApplyLiveDevicePortNames();
        ReflowSettingsWindow();
    }

    private void RefreshRouting(bool releaseDevice)
    {
        if (_inputEditor.ChannelNames.Length > 0)
        {
            _recordInputMap = _inputEditor.ReadMap();
        }

        if (_outputEditor.ChannelNames.Length > 0)
        {
            _playbackOutputMap = _outputEditor.ReadMap();
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
        RefreshSineButton();
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

    private void RefreshSineButton()
    {
        if (_probe.TonePlaying)
        {
            ActionButtonLooks.ApplyAccent(SineButton);
            return;
        }

        ActionButtonLooks.ApplyClear(SineButton);
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
        RecordLayoutCombo.SelectedItem is LayoutItem item ? item.Layout : ChannelLayout.Stereo;

    private ChannelLayout ReadPlaybackLayout() =>
        PlaybackLayoutCombo.SelectedItem is LayoutItem item ? item.Layout : ChannelLayout.Stereo;

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
        ComboBoxFit.Apply(ApiCombo);
        ComboBoxFit.Apply(DeviceCombo, deviceMax);
        ComboBoxFit.Apply(RecordLayoutCombo);
        ComboBoxFit.Apply(PlaybackLayoutCombo);
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
        var top = LabeledComboWidth(ApiLabel, ApiCombo)
            + 16
            + LabeledComboWidth(DeviceLabel, DeviceCombo);
        var gutter = DesignMetrics.SettingsScrollBarGap + DesignMetrics.SettingsScrollBarWidth;
        var record = Max(
            LabelWidth(InputHeader),
            LabeledComboWidth(RecordLayoutLabel, RecordLayoutCombo),
            LabelWidth(RecordInputMapLabel),
            _inputEditor.FittedRowWidth) + gutter;
        var play = Max(
            LabelWidth(OutputHeader),
            LabeledComboWidth(PlaybackLayoutLabel, PlaybackLayoutCombo),
            LabelWidth(PlaybackOutputMapLabel),
            _outputEditor.FittedRowWidth,
            DesignMetrics.SettingsSineButtonWidth) + gutter;
        var columns = record + DesignMetrics.SettingsColumnGap + play;
        return Math.Max(top, columns);
    }

    private double SettingsTabBarWidth()
    {
        var headers = new[]
        {
            UiStrings.LabelSettingsTabGeneral,
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

    private static void FillLayouts(ComboBox combo, ChannelLayout current)
    {
        combo.Items.Clear();
        LayoutItem? selected = null;
        foreach (var layout in ChannelLayout.All)
        {
            var item = new LayoutItem(layout);
            combo.Items.Add(item);
            if (layout.Id == current.Id)
            {
                selected = item;
            }
        }

        combo.SelectedItem = selected ?? combo.Items[0];
    }

    private void RebuildRouting()
    {
        var recordLayout = ReadRecordLayout();
        var playLayout = ReadPlaybackLayout();
        var inputPorts = ResolvePortNames(input: true);
        var outputPorts = ResolvePortNames(input: false);
        _inputPortNames = inputPorts;
        _outputPortNames = outputPorts;
        _inputEditor.Rebuild(
            recordLayout.Labels,
            inputPorts,
            ChannelRouter.Normalize(_recordInputMap, recordLayout.Channels, inputPorts.Length));
        _outputEditor.Rebuild(
            playLayout.Labels,
            outputPorts,
            ChannelRouter.Normalize(_playbackOutputMap, playLayout.Channels, outputPorts.Length));
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

    private sealed record LayoutItem(ChannelLayout Layout)
    {
        public override string ToString() => Layout.DisplayName;
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
