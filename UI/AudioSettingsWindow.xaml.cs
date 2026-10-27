using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.Wwise;

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

    public WaveformTileArrange SelectedMultiFileArrange { get; private set; }

    public WindowLayoutSlot SelectedLaunchMode { get; private set; } = AppLaunchModes.DefaultSlot;

    public int SelectedUiScalePercent { get; private set; } = UiScale.DefaultPercent;

    public double SelectedLoudnessTargetLufs { get; private set; }

    public double SelectedMonitorDimDb { get; private set; } =
        PlaybackSampleProvider.DefaultMonitorDimDb;

    public double SelectedMonitorLowCutHz { get; private set; } =
        PlaybackSampleProvider.DefaultMonitorLowCutHz;

    public double SelectedSilentSkipThresholdDb { get; private set; }

    public int SelectedSilentSkipRecordPadMs { get; private set; }

    public int SelectedClickGuardFadeMs { get; private set; }

    public int SelectedWwisePrefetchLengthMs { get; private set; }

    public int SelectedWwiseLookAheadTimeMs { get; private set; }

    public FileNameColorRule[] SelectedFileNameColorRules { get; private set; } = [];

    /// <summary>色設定パネル（Ctrl+Shift+C）を開く。メインウィンドウ側で接続する。</summary>
    public Action? OpenColorSettings { get; set; }

    private FileNameColorMarkerSettingsEditor? _fileNameColorMarkers;

    public int SelectedMp3BitRate { get; private set; }

    public string SelectedLameExePath { get; private set; } = string.Empty;

    public string SelectedLameOptions { get; private set; } = Mp3Encode.DefaultLameOptions;

    public int SelectedExportParallelism { get; private set; }

    public SpeakerPreset[] SelectedPresets { get; private set; } = [];

    public string[] SelectedVisibleSpeakerIds { get; private set; } = SpeakerPreset.DefaultVisibleIds;

    public string[] SelectedLibraryExplorerRoots { get; private set; } = [];
    public LibraryFileColumn[] SelectedLibraryListColumnsWave { get; private set; } =
        [.. LibraryColumnFilter.WaveDefaults];

    public LibraryFileColumn[] SelectedLibraryListColumnsMp3 { get; private set; } =
        [.. LibraryColumnFilter.Mp3Defaults];

    public LibraryFileColumn[] SelectedLibraryListColumnsVideo { get; private set; } =
        [.. LibraryColumnFilter.VideoDefaults];

    public LibraryPlaylistWaveformSize SelectedLibraryPlaylistWaveformSizeWave { get; private set; } =
        LibraryPlaylistWaveformSize.L;

    public LibraryPlaylistWaveformSize SelectedLibraryPlaylistWaveformSizeMp3 { get; private set; } =
        LibraryPlaylistWaveformSize.L;

    public bool SelectedGaplessPlayback { get; private set; } = true;

    public bool SelectedShowMainMenuBar { get; private set; } = true;

    public WindowLayoutPreset SelectedLayoutPresetF9 { get; private set; } =
        WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F9);

    public WindowLayoutPreset SelectedLayoutPresetF10 { get; private set; } =
        WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F10);

    public WindowLayoutPreset SelectedLayoutPresetF11 { get; private set; } =
        WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F11);

    public WindowLayoutPreset SelectedLayoutPresetF12 { get; private set; } =
        WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F12);

    private readonly List<LayoutPresetEditor> _layoutPresetEditors = [];
    private readonly WindowLayoutPreset _layoutPresetF9OpenedAt;
    private readonly WindowLayoutPreset _layoutPresetF10OpenedAt;
    private readonly WindowLayoutPreset _layoutPresetF11OpenedAt;
    private readonly WindowLayoutPreset _layoutPresetF12OpenedAt;
    private bool _layoutPreviewReady;

    public bool SelectedLibraryShowPlaylistPdf { get; private set; } = true;

    public bool SelectedLibraryShowPlaylistMov { get; private set; }

    public bool SelectedLibraryShowPlaylistMp4 { get; private set; }

    public bool SelectedLibraryShowPlaylistAvi { get; private set; }

    public bool SelectedLibraryShowPlaylistMkv { get; private set; }

    public bool SelectedLibraryShowPlaylistWebm { get; private set; }

    public bool SelectedLibraryShowPlaylistMpg { get; private set; }

    public int SelectedVideoProxyRetentionDays { get; private set; } = VideoProxy.DefaultRetentionDays;

    public string SelectedFfmpegExePath { get; private set; } = string.Empty;

    public bool SelectedVideoProxyDisableAutoEncode { get; private set; }

    public bool SelectedUseDxgiVideoPresenter { get; private set; }

    public string SelectedWallpaperPath { get; private set; } = string.Empty;

    public double SelectedWallpaperOpacity { get; private set; } = AppSettings.WallpaperOpacityDefault;

    public bool SelectedAutoSpeakerSelect { get; private set; }

    public string SelectedActiveSpeakerId { get; private set; } = string.Empty;

    public string SelectedRecordDeviceId { get; private set; } = string.Empty;

    public int SelectedDefaultSampleRate { get; private set; } = DefaultAudioFormat.SampleRate;

    public int SelectedDefaultBitsPerSample { get; private set; } = DefaultAudioFormat.BitsPerSample;

    public string SelectedDefaultChannelLayout { get; private set; } = DefaultAudioFormat.ChannelLayoutId;

    private readonly List<SpeakerPreset> _presets;
    private readonly HashSet<string> _visibleIds;
    private SpeakerPreset? _editingSpeaker;
    private readonly ChannelRoutingEditor _inputEditor;
    private readonly ChannelRoutingEditor _outputEditor;
    private readonly SettingsIoProbe _probe = new();
    private readonly DispatcherTimer _meterTimer;
    private bool _syncingAssociations;
    private TextBlock? _videoAssociationHeader;
    private readonly List<CheckBox> _associationBoxes = [];
    private readonly List<CheckBox> _videoAssociationBoxes = [];
    private bool _syncingSpeaker;
    private bool _syncingVisibility;
    private bool _speakerDirty;
    private int[] _recordInputMap;
    private int[] _playbackOutputMap;
    private int[] _fileChannelMap;
    private string[] _inputPortNames = [];
    private string[] _outputPortNames = [];
    private float[] _meterPeaks = [];
    private readonly int _uiScaleOpenedAt;
    private readonly bool _showMainMenuBarOpenedAt;
    private readonly string _wallpaperPathOpenedAt;
    private readonly double _wallpaperOpacityOpenedAt;
    private readonly Dictionary<LibraryFileColumn, CheckBox> _libraryColumnChecksWave = [];
    private readonly Dictionary<LibraryFileColumn, CheckBox> _libraryColumnChecksMp3 = [];
    private readonly Dictionary<LibraryFileColumn, CheckBox> _libraryColumnChecksVideo = [];

    public AudioSettingsWindow(
        AudioOutputSettings current,
        FadeShape fadeIn,
        FadeShape fadeOut,
        UiLanguageChoice language,
        double loudnessTargetLufs,
        double monitorDimDb,
        double monitorLowCutHz,
        double silentSkipThresholdDb,
        int silentSkipRecordPadMs,
        int clickGuardFadeMs,
        int mp3BitRate,
        string lameExePath,
        string lameOptions,
        int exportParallelism,
        IEnumerable<SpeakerPreset> presets,
        string activeSpeakerId,
        IEnumerable<string>? visibleSpeakerIds = null,
        int defaultSampleRate = DefaultAudioFormat.SampleRate,
        int defaultBitsPerSample = DefaultAudioFormat.BitsPerSample,
        string? defaultChannelLayout = null,
        int wwisePrefetchLengthMs = WwiseTrackTiming.DefaultPrefetchLengthMs,
        int wwiseLookAheadTimeMs = WwiseTrackTiming.DefaultLookAheadTimeMs,
        int uiScalePercent = UiScale.DefaultPercent,
        string? multiFileArrange = null,
        bool autoSpeakerSelect = false,
        IEnumerable<string>? libraryExplorerRoots = null,
        IEnumerable<LibraryFileColumn>? libraryListColumnsWave = null,
        IEnumerable<LibraryFileColumn>? libraryListColumnsMp3 = null,
        IEnumerable<LibraryFileColumn>? libraryListColumnsVideo = null,
        LibraryPlaylistWaveformSize libraryPlaylistWaveformSizeWave = LibraryPlaylistWaveformSize.L,
        LibraryPlaylistWaveformSize libraryPlaylistWaveformSizeMp3 = LibraryPlaylistWaveformSize.L,
        bool gaplessPlayback = true,
        bool showMainMenuBar = true,
        bool libraryShowPlaylistPdf = true,
        bool libraryShowPlaylistMov = false,
        bool libraryShowPlaylistMp4 = false,
        bool libraryShowPlaylistAvi = false,
        bool libraryShowPlaylistMkv = false,
        bool libraryShowPlaylistWebm = false,
        bool libraryShowPlaylistMpg = false,
        int videoProxyRetentionDays = VideoProxy.DefaultRetentionDays,
        string? ffmpegExePath = null,
        bool videoProxyDisableAutoEncode = false,
        bool useDxgiVideoPresenter = false,
        WindowLayoutSlot launchMode = WindowLayoutSlot.F12,
        string? wallpaperPath = null,
        double wallpaperOpacity = AppSettings.WallpaperOpacityDefault,
        WindowLayoutPreset? layoutPresetF9 = null,
        WindowLayoutPreset? layoutPresetF10 = null,
        WindowLayoutPreset? layoutPresetF11 = null,
        WindowLayoutPreset? layoutPresetF12 = null)
    {
        SelectedSettings = current;
        SelectedLanguage = language;
        SelectedMultiFileArrange = WaveformTileLayout.Parse(multiFileArrange);
        SelectedLaunchMode = launchMode;
        SelectedUiScalePercent = UiScale.ClampPercent(uiScalePercent);
        _uiScaleOpenedAt = SelectedUiScalePercent;
        SelectedLoudnessTargetLufs = LoudnessMeterEngine.ClampTargetLufs(loudnessTargetLufs);
        SelectedMonitorDimDb = PlaybackSampleProvider.ClampMonitorDimDb(monitorDimDb);
        SelectedMonitorLowCutHz = PlaybackSampleProvider.ClampMonitorLowCutHz(monitorLowCutHz);
        SelectedSilentSkipThresholdDb = SilentSkip.ClampThresholdDb(silentSkipThresholdDb);
        SelectedSilentSkipRecordPadMs = SilentSkip.ClampRecordPadMs(silentSkipRecordPadMs);
        SelectedClickGuardFadeMs = ClickGuard.ClampFadeMs(clickGuardFadeMs);
        SelectedWwisePrefetchLengthMs = WwiseTrackTiming.ClampPrefetchLengthMs(wwisePrefetchLengthMs);
        SelectedWwiseLookAheadTimeMs = WwiseTrackTiming.ClampLookAheadTimeMs(wwiseLookAheadTimeMs);
        SelectedMp3BitRate = Mp3Encode.ClampWindowsBitRate(mp3BitRate);
        SelectedLameExePath = lameExePath ?? string.Empty;
        SelectedLameOptions = lameOptions ?? string.Empty;
        SelectedExportParallelism = exportParallelism;
        _presets = [.. SpeakerPreset.CloneAll(presets)];
        var visible = SpeakerPreset.NormalizeVisibleIds(visibleSpeakerIds);
        _visibleIds = new HashSet<string>(visible, StringComparer.OrdinalIgnoreCase);
        SelectedVisibleSpeakerIds = visible;
        SelectedLibraryExplorerRoots = LibraryExplorerPaths.ResolveRoots(libraryExplorerRoots?.ToArray());
        SelectedLibraryListColumnsWave = LibraryColumnFilter.ResolveWave(
            libraryListColumnsWave is null
                ? null
                : LibraryColumnFilter.Serialize(libraryListColumnsWave, LibraryColumnFilter.WaveDefaults));
        SelectedLibraryListColumnsMp3 = LibraryColumnFilter.ResolveMp3(
            libraryListColumnsMp3 is null
                ? null
                : LibraryColumnFilter.Serialize(libraryListColumnsMp3, LibraryColumnFilter.Mp3Defaults));
        SelectedLibraryListColumnsVideo = LibraryColumnFilter.ResolveVideo(
            libraryListColumnsVideo is null
                ? null
                : LibraryColumnFilter.Serialize(libraryListColumnsVideo, LibraryColumnFilter.VideoDefaults));
        SelectedLibraryPlaylistWaveformSizeWave =
            LibraryPlaylistWaveformSizes.Clamp(libraryPlaylistWaveformSizeWave);
        SelectedLibraryPlaylistWaveformSizeMp3 =
            LibraryPlaylistWaveformSizes.Clamp(libraryPlaylistWaveformSizeMp3);
        SelectedGaplessPlayback = gaplessPlayback;
        SelectedShowMainMenuBar = showMainMenuBar;
        _showMainMenuBarOpenedAt = SelectedShowMainMenuBar;
        SelectedLayoutPresetF9 = (layoutPresetF9 ?? WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F9)).Clone();
        SelectedLayoutPresetF10 = (layoutPresetF10 ?? WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F10)).Clone();
        SelectedLayoutPresetF11 = (layoutPresetF11 ?? WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F11)).Clone();
        SelectedLayoutPresetF12 = (layoutPresetF12 ?? WindowLayoutPreset.CreateDefault(WindowLayoutSlot.F12)).Clone();
        _layoutPresetF9OpenedAt = SelectedLayoutPresetF9.Clone();
        _layoutPresetF10OpenedAt = SelectedLayoutPresetF10.Clone();
        _layoutPresetF11OpenedAt = SelectedLayoutPresetF11.Clone();
        _layoutPresetF12OpenedAt = SelectedLayoutPresetF12.Clone();
        SelectedLibraryShowPlaylistPdf = libraryShowPlaylistPdf;
        SelectedLibraryShowPlaylistMov = libraryShowPlaylistMov;
        SelectedLibraryShowPlaylistMp4 = libraryShowPlaylistMp4;
        SelectedLibraryShowPlaylistAvi = libraryShowPlaylistAvi;
        SelectedLibraryShowPlaylistMkv = libraryShowPlaylistMkv;
        SelectedLibraryShowPlaylistWebm = libraryShowPlaylistWebm;
        SelectedLibraryShowPlaylistMpg = libraryShowPlaylistMpg;
        SelectedVideoProxyRetentionDays = VideoProxy.ClampRetentionDays(videoProxyRetentionDays);
        SelectedFfmpegExePath = ffmpegExePath ?? string.Empty;
        SelectedVideoProxyDisableAutoEncode = videoProxyDisableAutoEncode;
        SelectedUseDxgiVideoPresenter = useDxgiVideoPresenter;
        SelectedWallpaperPath = string.IsNullOrWhiteSpace(wallpaperPath) ? string.Empty : wallpaperPath.Trim();
        SelectedWallpaperOpacity = AppSettings.ClampWallpaperOpacity(wallpaperOpacity);
        _wallpaperPathOpenedAt = SelectedWallpaperPath;
        _wallpaperOpacityOpenedAt = SelectedWallpaperOpacity;
        SelectedAutoSpeakerSelect = autoSpeakerSelect;
        SelectedActiveSpeakerId = string.IsNullOrWhiteSpace(activeSpeakerId)
            ? _presets[0].Id
            : activeSpeakerId;
        var speaker = FindPreset(SelectedActiveSpeakerId) ?? _presets[0];
        SelectedActiveSpeakerId = speaker.Id;
        SelectedSettings = speaker.ToAudioOutputSettings();
        SelectedRecordDeviceId = AudioCaptureFactory.ResolveRecordDeviceId(
            SelectedSettings.Api,
            SelectedSettings.DeviceId);
        var format = DefaultAudioFormat.Resolve(
            defaultSampleRate,
            defaultBitsPerSample,
            defaultChannelLayout);
        SelectedDefaultSampleRate = format.SampleRate;
        SelectedDefaultBitsPerSample = format.BitsPerSample;
        SelectedDefaultChannelLayout = format.Layout.Id;
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
        Title = UiStrings.DialogSettingsTitle;
        AcrylicDialogChrome.Prepare(
            this,
            ResizeMode.CanResizeWithGrip,
            SizeToContent.Manual);
        AcrylicDialogChrome.AdoptContent(this);
        VerticalResizeOnly.LockWidth(this);

        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Auto, UiStrings.LabelLanguageAuto));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Japanese, UiStrings.LabelLanguageJapanese));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.English, UiStrings.LabelLanguageEnglish));
        SelectLanguage(language);
        FillMultiFileArrange(SelectedMultiFileArrange);
        FillLaunchMode(SelectedLaunchMode);
        FillUiScale(SelectedUiScalePercent);
        UiScaleCombo.SelectionChanged += UiScaleCombo_SelectionChanged;

        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.WaveOut, UiStrings.LabelAudioApiWaveOut));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Wasapi, UiStrings.LabelAudioApiWasapi));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Asio, UiStrings.LabelAudioApiAsio));

        ActionButtonLooks.ApplyAccent(OkButton);
        ActionButtonLooks.ApplyClear(CancelButton);
        ActionButtonLooks.ApplyClear(LameBrowseButton);
        ActionButtonLooks.ApplyClear(FfmpegBrowseButton);
        ActionButtonLooks.ApplyClear(VideoProxyClearAllButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsWaveResetButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsMp3ResetButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsVideoResetButton);
        ActionButtonLooks.ApplyClear(AssociationAllOnButton);
        ActionButtonLooks.ApplyClear(AssociationAllOffButton);
        ActionButtonLooks.ApplyClear(WallpaperBrowseButton);
        ActionButtonLooks.ApplyClear(WallpaperClearButton);
        ApplyLibraryExplorerRootButtons();
        ApplyLibraryExplorerRootsChrome();
        AppDialogKeys.PrepareActionButton(OkButton, isDefault: true);
        AppDialogKeys.PrepareActionButton(CancelButton, isCancel: true);
        AppDialogKeys.PrepareActionButton(LameBrowseButton);
        AppDialogKeys.PrepareActionButton(FfmpegBrowseButton);
        AppDialogKeys.PrepareActionButton(VideoProxyClearAllButton);
        AppDialogKeys.PrepareActionButton(LibraryColumnsWaveResetButton);
        AppDialogKeys.PrepareActionButton(LibraryColumnsMp3ResetButton);
        AppDialogKeys.PrepareActionButton(LibraryColumnsVideoResetButton);
        AppDialogKeys.PrepareActionButton(LibraryExplorerRootAddButton);
        AppDialogKeys.PrepareActionButton(LibraryExplorerRootRemoveButton);
        AppDialogKeys.PrepareActionButton(LibraryExplorerRootUpButton);
        AppDialogKeys.PrepareActionButton(LibraryExplorerRootDownButton);
        AppDialogKeys.PrepareActionButton(LibraryCatalogClearButton);
        AppDialogKeys.PrepareActionButton(WallpaperBrowseButton);
        AppDialogKeys.PrepareActionButton(WallpaperClearButton);
        AppDialogKeys.AllowTabToLeave(SettingsTabs);
        AppDialogKeys.Attach(
            this,
            () =>
            {
                StopProbeUi();
                DialogResult = false;
                Close();
            },
            interceptEscape: () =>
            {
                if (_fadeCurveMenu is not { IsOpen: true })
                {
                    return false;
                }

                _fadeCurveMenu.IsOpen = false;
                return true;
            });

        _fadeInRow = CreateFadeRow(UiStrings.LabelDefaultFadeIn, fadeIn, isFadeIn: true, row: 0);
        _fadeOutRow = CreateFadeRow(UiStrings.LabelDefaultFadeOut, fadeOut, isFadeIn: false, row: 1);
        UiThemeService.Changed += OnUiThemeChanged;
        Closed += (_, _) => UiThemeService.Changed -= OnUiThemeChanged;
        ApplySettingsTabChrome();

        FillSpeakers(SelectedActiveSpeakerId);
        FillSpeakerVisibility();
        AutoSpeakerSelectBox.IsChecked = SelectedAutoSpeakerSelect;
        LoadSpeakerEditors(FindPreset(SelectedActiveSpeakerId) ?? _presets[0], releaseDevice: false);
        RebuildRouting();
        LoudnessTargetBox.Text = SelectedLoudnessTargetLufs.ToString("0.#", CultureInfo.InvariantCulture);
        MonitorDimBox.Text = SelectedMonitorDimDb.ToString("0.#", CultureInfo.InvariantCulture);
        MonitorLowCutBox.Text = SelectedMonitorLowCutHz.ToString("0.#", CultureInfo.InvariantCulture);
        SilentSkipThresholdBox.Text = SelectedSilentSkipThresholdDb.ToString("0.#", CultureInfo.InvariantCulture);
        SilentSkipRecordPadBox.Text = SelectedSilentSkipRecordPadMs.ToString(CultureInfo.InvariantCulture);
        ClickGuardFadeBox.Text = SelectedClickGuardFadeMs.ToString(CultureInfo.InvariantCulture);
        WwisePrefetchLengthBox.Text = SelectedWwisePrefetchLengthMs.ToString(CultureInfo.InvariantCulture);
        WwiseLookAheadTimeBox.Text = SelectedWwiseLookAheadTimeMs.ToString(CultureInfo.InvariantCulture);
        _fileNameColorMarkers = new FileNameColorMarkerSettingsEditor(
            FileNameColorMarkerHost,
            FileNameColorMarkerAddButton);
        _fileNameColorMarkers.Load(AppStorage.Settings.FileNameColorRules);
        ActionButtonLooks.ApplyClear(OpenColorSettingsButton);
        ActionButtonLooks.ApplyClear(FileNameColorMarkerAddButton);
        AppDialogKeys.PrepareActionButton(OpenColorSettingsButton);
        AppDialogKeys.PrepareActionButton(FileNameColorMarkerAddButton);
        FillWindowsBitRates(SelectedMp3BitRate);
        LamePathBox.Text = SelectedLameExePath;
        LameOptionsBox.Text = Mp3Encode.ResolveLameOptions(SelectedLameOptions);
        FillExportParallelism(SelectedExportParallelism);
        WallpaperPathBox.Text = SelectedWallpaperPath;
        WallpaperOpacityBox.Text = AppSettings.FormatWallpaperOpacityPercent(SelectedWallpaperOpacity);
        WallpaperPathBox.TextChanged += (_, _) => PreviewWallpaperPath();
        WallpaperOpacityBox.TextChanged += (_, _) => PreviewWallpaperOpacity();
        WallpaperOpacityBox.PreviewMouseWheel += (_, e) =>
        {
            if (e.Delta == 0)
            {
                return;
            }

            NudgeWallpaperOpacity(Math.Sign(e.Delta));
            e.Handled = true;
        };
        WallpaperOpacityBox.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is not (Key.Up or Key.Down))
            {
                return;
            }

            NudgeWallpaperOpacity(key == Key.Up ? 1 : -1);
            e.Handled = true;
        };
        FillAssociations();
        FillLibraryExplorerRoots();
        FillLibraryColumns();
        FillLibraryPlaylistWaveformOptions();
        FillVideoProxyRetention();
        RefreshVideoProxyUsage();
        RefreshLibraryCatalogUsage();
        FfmpegPathBox.Text = SelectedFfmpegExePath;
        VideoProxyDisableAutoEncodeBox.IsChecked = SelectedVideoProxyDisableAutoEncode;
        UseDxgiVideoPresenterBox.IsChecked = SelectedUseDxgiVideoPresenter;
        SyncFfmpegDependentControls();
        FillDefaultAudioFormat();
        RefreshSilentSkipThresholdFloorText();
        ReflowSettingsWindow();
        WindowPlacement.TryApplySettings(this, AppStorage.Settings);
        Loaded += (_, _) =>
        {
            if (Owner is { Topmost: true })
            {
                Topmost = true;
            }

            ReflowSettingsWindow();
            if (IsProbeTabSelected())
            {
                StartProbe();
            }
        };
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

    private void FillLibraryExplorerRoots()
    {
        LibraryExplorerRootsList.Items.Clear();
        foreach (var path in SelectedLibraryExplorerRoots)
        {
            LibraryExplorerRootsList.Items.Add(new LibraryExplorerRootItem(path));
        }

        GaplessPlaybackBox.IsChecked = SelectedGaplessPlayback;
        ShowMainMenuBarBox.IsChecked = SelectedShowMainMenuBar;
        ShowMainMenuBarBox.Checked += (_, _) => PreviewShowMainMenuBar(true);
        ShowMainMenuBarBox.Unchecked += (_, _) => PreviewShowMainMenuBar(false);
        FillLayoutPresets();
        _layoutPreviewReady = true;
        RefreshLibraryExplorerRootButtons();
    }

    private void FillLayoutPresets()
    {
        LayoutPresetsHost.Children.Clear();
        _layoutPresetEditors.Clear();
        AddLayoutPresetEditor(WindowLayoutSlot.F9, UiStrings.LabelLayoutPresetF9, SelectedLayoutPresetF9);
        AddLayoutPresetEditor(WindowLayoutSlot.F10, UiStrings.LabelLayoutPresetF10, SelectedLayoutPresetF10);
        AddLayoutPresetEditor(WindowLayoutSlot.F11, UiStrings.LabelLayoutPresetF11, SelectedLayoutPresetF11);
        AddLayoutPresetEditor(WindowLayoutSlot.F12, UiStrings.LabelLayoutPresetF12, SelectedLayoutPresetF12);
    }

    private void AddLayoutPresetEditor(WindowLayoutSlot slot, string title, WindowLayoutPreset preset)
    {
        var editor = LayoutPresetEditor.Create(slot, title, preset, PreviewLayoutPresetChanged);
        _layoutPresetEditors.Add(editor);
        LayoutPresetsHost.Children.Add(editor.Root);
    }

    private void PreviewLayoutPresetChanged(WindowLayoutSlot slot)
    {
        if (!_layoutPreviewReady)
        {
            return;
        }

        ReadLayoutPresets();
        var preset = slot switch
        {
            WindowLayoutSlot.F9 => SelectedLayoutPresetF9,
            WindowLayoutSlot.F10 => SelectedLayoutPresetF10,
            WindowLayoutSlot.F11 => SelectedLayoutPresetF11,
            _ => SelectedLayoutPresetF12,
        };
        AppStorage.Settings.ApplyLayoutPreset(slot, preset);
        if (Owner is MainWindow window)
        {
            window.PreviewLayoutPresetFromSettings(slot);
        }
    }

    private void RevertLayoutPresetPreviews()
    {
        SelectedLayoutPresetF9 = _layoutPresetF9OpenedAt.Clone();
        SelectedLayoutPresetF10 = _layoutPresetF10OpenedAt.Clone();
        SelectedLayoutPresetF11 = _layoutPresetF11OpenedAt.Clone();
        SelectedLayoutPresetF12 = _layoutPresetF12OpenedAt.Clone();
        AppStorage.Settings.ApplyLayoutPreset(WindowLayoutSlot.F9, SelectedLayoutPresetF9);
        AppStorage.Settings.ApplyLayoutPreset(WindowLayoutSlot.F10, SelectedLayoutPresetF10);
        AppStorage.Settings.ApplyLayoutPreset(WindowLayoutSlot.F11, SelectedLayoutPresetF11);
        AppStorage.Settings.ApplyLayoutPreset(WindowLayoutSlot.F12, SelectedLayoutPresetF12);
        if (Owner is MainWindow window)
        {
            window.PreviewLayoutPresetFromSettings(WindowLayoutSlot.F9);
            window.PreviewLayoutPresetFromSettings(WindowLayoutSlot.F10);
            window.PreviewLayoutPresetFromSettings(WindowLayoutSlot.F11);
            window.PreviewLayoutPresetFromSettings(WindowLayoutSlot.F12);
        }
    }

    private void ReadLayoutPresets()
    {
        foreach (var editor in _layoutPresetEditors)
        {
            var preset = editor.Read();
            switch (editor.Slot)
            {
                case WindowLayoutSlot.F9:
                    SelectedLayoutPresetF9 = preset;
                    break;
                case WindowLayoutSlot.F10:
                    SelectedLayoutPresetF10 = preset;
                    break;
                case WindowLayoutSlot.F11:
                    SelectedLayoutPresetF11 = preset;
                    break;
                default:
                    SelectedLayoutPresetF12 = preset;
                    break;
            }
        }
    }

    private void FillLibraryColumns()
    {
        LibraryShowPlaylistPdfBox.IsChecked = SelectedLibraryShowPlaylistPdf;
        LibraryShowPlaylistMovBox.IsChecked = SelectedLibraryShowPlaylistMov;
        LibraryShowPlaylistMp4Box.IsChecked = SelectedLibraryShowPlaylistMp4;
        LibraryShowPlaylistAviBox.IsChecked = SelectedLibraryShowPlaylistAvi;
        LibraryShowPlaylistMkvBox.IsChecked = SelectedLibraryShowPlaylistMkv;
        LibraryShowPlaylistWebmBox.IsChecked = SelectedLibraryShowPlaylistWebm;
        LibraryShowPlaylistMpgBox.IsChecked = SelectedLibraryShowPlaylistMpg;
        FillLibraryColumnHost(
            LibraryColumnsWaveHost,
            _libraryColumnChecksWave,
            SelectedLibraryListColumnsWave,
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.WaveSettingsColumns);
        FillLibraryColumnHost(
            LibraryColumnsMp3Host,
            _libraryColumnChecksMp3,
            SelectedLibraryListColumnsMp3,
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.Mp3SettingsColumns);
        FillLibraryColumnHost(
            LibraryColumnsVideoHost,
            _libraryColumnChecksVideo,
            SelectedLibraryListColumnsVideo,
            LibraryColumnFilter.VideoDefaults,
            LibraryColumnFilter.VideoSettingsColumns);
    }

    private void FillLibraryColumnHost(
        Panel host,
        Dictionary<LibraryFileColumn, CheckBox> checks,
        IReadOnlyList<LibraryFileColumn> selected,
        IReadOnlyList<LibraryFileColumn> defaults,
        IReadOnlyList<LibraryFileColumn> settingsColumns)
    {
        host.Children.Clear();
        checks.Clear();
        var style = TryFindResource("DarkCheckBoxStyle") as Style;
        var visible = new HashSet<LibraryFileColumn>(selected);
        foreach (var column in LibraryColumnFilter.SettingsCheckOrder(defaults, settingsColumns))
        {
            var box = new CheckBox
            {
                Content = CreateCheckAccessContent(UiStrings.LibraryColumnCheckLabel(column)),
                IsChecked = visible.Contains(column),
                Margin = new Thickness(0, 0, 0, 6),
                Tag = column,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (style is not null)
            {
                box.Style = style;
            }

                        checks[column] = box;
            host.Children.Add(box);
        }
    }

    private LibraryFileColumn[] ReadLibraryColumns(
        Dictionary<LibraryFileColumn, CheckBox> checks,
        IReadOnlyList<LibraryFileColumn> previous)
    {
        var set = new HashSet<LibraryFileColumn> { LibraryFileColumn.Name };
        foreach (var pair in checks)
        {
            if (pair.Value.IsChecked == true)
            {
                set.Add(pair.Key);
            }
        }

        return LibraryColumnFilter.Merge(previous, set);
    }

    private void LibraryColumnsWaveResetButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedLibraryListColumnsWave = [.. LibraryColumnFilter.WaveDefaults];
        SelectedLibraryPlaylistWaveformSizeWave = LibraryPlaylistWaveformSize.L;
        FillLibraryColumnHost(
            LibraryColumnsWaveHost,
            _libraryColumnChecksWave,
            SelectedLibraryListColumnsWave,
            LibraryColumnFilter.WaveDefaults,
            LibraryColumnFilter.WaveSettingsColumns);
        SelectLibraryPlaylistWaveformSize(
            LibraryPlaylistWaveformSizeWaveCombo,
            SelectedLibraryPlaylistWaveformSizeWave);
    }

    private void LibraryColumnsMp3ResetButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedLibraryListColumnsMp3 = [.. LibraryColumnFilter.Mp3Defaults];
        SelectedLibraryPlaylistWaveformSizeMp3 = LibraryPlaylistWaveformSize.L;
        FillLibraryColumnHost(
            LibraryColumnsMp3Host,
            _libraryColumnChecksMp3,
            SelectedLibraryListColumnsMp3,
            LibraryColumnFilter.Mp3Defaults,
            LibraryColumnFilter.Mp3SettingsColumns);
        SelectLibraryPlaylistWaveformSize(
            LibraryPlaylistWaveformSizeMp3Combo,
            SelectedLibraryPlaylistWaveformSizeMp3);
    }

    private void LibraryColumnsVideoResetButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedLibraryListColumnsVideo = [.. LibraryColumnFilter.VideoDefaults];
        FillLibraryColumnHost(
            LibraryColumnsVideoHost,
            _libraryColumnChecksVideo,
            SelectedLibraryListColumnsVideo,
            LibraryColumnFilter.VideoDefaults,
            LibraryColumnFilter.VideoSettingsColumns);
    }

    private void FillLibraryPlaylistWaveformOptions()
    {
        FillLibraryPlaylistWaveformCombo(
            LibraryPlaylistWaveformSizeWaveCombo,
            SelectedLibraryPlaylistWaveformSizeWave);
        FillLibraryPlaylistWaveformCombo(
            LibraryPlaylistWaveformSizeMp3Combo,
            SelectedLibraryPlaylistWaveformSizeMp3);
    }

    private void FillLibraryPlaylistWaveformCombo(
        ComboBox combo,
        LibraryPlaylistWaveformSize selected)
    {
        combo.Items.Clear();
        combo.Items.Add(
            new LibraryPlaylistWaveformSizeItem(
                LibraryPlaylistWaveformSize.S,
                UiStrings.LabelLibraryPlaylistWaveformSizeS));
        combo.Items.Add(
            new LibraryPlaylistWaveformSizeItem(
                LibraryPlaylistWaveformSize.M,
                UiStrings.LabelLibraryPlaylistWaveformSizeM));
        combo.Items.Add(
            new LibraryPlaylistWaveformSizeItem(
                LibraryPlaylistWaveformSize.L,
                UiStrings.LabelLibraryPlaylistWaveformSizeL));
        SelectLibraryPlaylistWaveformSize(combo, selected);
    }

    private void FillVideoProxyRetention()
    {
        VideoProxyRetentionCombo.Items.Clear();
        VideoProxyRetentionItem? selected = null;
        foreach (var days in VideoProxy.RetentionChoices)
        {
            var item = new VideoProxyRetentionItem(days, UiStrings.LabelVideoProxyRetentionDays(days));
            VideoProxyRetentionCombo.Items.Add(item);
            if (days == SelectedVideoProxyRetentionDays)
            {
                selected = item;
            }
        }

        VideoProxyRetentionCombo.SelectedItem = selected ?? VideoProxyRetentionCombo.Items[0];
    }

    private int ReadVideoProxyRetentionDays() =>
        VideoProxyRetentionCombo.SelectedItem is VideoProxyRetentionItem item
            ? VideoProxy.ClampRetentionDays(item.Days)
            : VideoProxy.DefaultRetentionDays;

    private void RefreshVideoProxyUsage() =>
        VideoProxyUsageText.Text = UiStrings.LabelVideoProxyUsage(VideoProxy.GetUsageBytes());

    private void RefreshLibraryCatalogUsage() =>
        LibraryCatalogUsageText.Text = UiStrings.LabelLibraryCatalogUsage(LibraryCatalog.GetUsageBytes());

    private static void SelectLibraryPlaylistWaveformSize(
        ComboBox combo,
        LibraryPlaylistWaveformSize selected)
    {
        foreach (LibraryPlaylistWaveformSizeItem item in combo.Items)
        {
            if (item.Size == selected)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private void ApplyLibraryExplorerRootButtons()
    {
        ActionButtonLooks.ApplyClear(LibraryExplorerRootAddButton);
        ActionButtonLooks.ApplyClear(LibraryExplorerRootRemoveButton);
        ActionButtonLooks.ApplyClear(LibraryExplorerRootUpButton);
        ActionButtonLooks.ApplyClear(LibraryExplorerRootDownButton);
        ActionButtonLooks.ApplyClear(LibraryCatalogClearButton);
    }

    private void ApplyLibraryExplorerRootsChrome()
    {
        LibraryExplorerRootsList.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        LibraryExplorerRootsList.SetResourceReference(BackgroundProperty, "DialogInputBackBrush");
        LibraryExplorerRootsList.SetResourceReference(BorderBrushProperty, "ChromeBorderBrush");
        LibraryExplorerRootsList.BorderThickness = new Thickness(1);

        var highlight = ResolveThemeBrush("MenuHighlightBackBrush");
        var fore = ResolveThemeBrush("PrimaryForeBrush");
        LibraryExplorerRootsList.Resources[SystemColors.HighlightBrushKey] = highlight;
        LibraryExplorerRootsList.Resources[SystemColors.HighlightTextBrushKey] = fore;
        LibraryExplorerRootsList.Resources[SystemColors.InactiveSelectionHighlightBrushKey] = highlight;
        LibraryExplorerRootsList.Resources[SystemColors.InactiveSelectionHighlightTextBrushKey] = fore;
        LibraryExplorerRootsList.Resources[SystemColors.ControlBrushKey] = Brushes.Transparent;

        var itemStyle = new Style(typeof(ListBoxItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 3, 6, 3)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
        LibraryExplorerRootsList.ItemContainerStyle = itemStyle;
        // DisplayMemberPath と ItemTemplate は同時に使えない（欠落パス色付け用テンプレートを使う）
        ApplyLibraryExplorerRootTemplate(LibraryExplorerRootsList);
    }

    /// <summary>設定のエクスプローラールート一覧へ、欠落パスを赤くする ItemTemplate を載せる。</summary>
    internal static void ApplyLibraryExplorerRootTemplate(ListBox list)
    {
        list.DisplayMemberPath = null;
        list.ItemTemplate = LibraryExplorerRootTemplate();
    }

    /// <summary>
    /// パス文字に色を載せる。ListBoxItem.Foreground や選択色だと上書きされやすいので TextBlock 側で持つ。
    /// </summary>
    private static DataTemplate LibraryExplorerRootTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(LibraryExplorerRootItem.Path)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.ToolTipProperty, new Binding(nameof(LibraryExplorerRootItem.Path)));

        // TargetName 付き DataTemplate.Triggers より、TextBlock 自身の Style の方が確実。
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension("PrimaryForeBrush")));
        var missing = new DataTrigger
        {
            Binding = new Binding(nameof(LibraryExplorerRootItem.IsMissing)),
            Value = true,
        };
        missing.Setters.Add(new Setter(
            TextBlock.ForegroundProperty,
            new DynamicResourceExtension(LibraryBrowserView.MissingRegisteredPathForeBrushKey)));
        style.Triggers.Add(missing);
        text.SetValue(FrameworkElement.StyleProperty, style);

        return new DataTemplate(typeof(LibraryExplorerRootItem)) { VisualTree = text };
    }

    private static Brush ResolveThemeBrush(string key) =>
        WpfControlHelpers.FrozenBrush(Theme.Get(key));

    private void LibraryExplorerRootAddButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = UiStrings.TitleLibraryExplorerRootAdd,
        };
        if (dialog.ShowDialog(this) != true || string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(dialog.FolderName)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        foreach (var existing in LibraryExplorerRootsList.Items.OfType<LibraryExplorerRootItem>())
        {
            if (existing.Path.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                LibraryExplorerRootsList.SelectedItem = existing;
                return;
            }
        }

        var item = new LibraryExplorerRootItem(full);
        LibraryExplorerRootsList.Items.Add(item);
        LibraryExplorerRootsList.SelectedItem = item;
    }

    private void LibraryExplorerRootRemoveButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = LibraryExplorerRootsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var item in selected)
        {
            LibraryExplorerRootsList.Items.Remove(item);
        }

        if (LibraryExplorerRootsList.Items.Count == 0)
        {
            foreach (var path in LibraryExplorerPaths.ResolveRoots(null))
            {
                LibraryExplorerRootsList.Items.Add(new LibraryExplorerRootItem(path));
            }
        }

        RefreshLibraryExplorerRootButtons();
    }

    private void LibraryExplorerRootUpButton_Click(object sender, RoutedEventArgs e) =>
        MoveLibraryExplorerRoots(-1);

    private void LibraryExplorerRootDownButton_Click(object sender, RoutedEventArgs e) =>
        MoveLibraryExplorerRoots(1);

    private void LibraryExplorerRootsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshLibraryExplorerRootButtons();

    private void MoveLibraryExplorerRoots(int direction)
    {
        var items = LibraryExplorerRootsList.Items.OfType<LibraryExplorerRootItem>().ToList();
        var selected = LibraryExplorerRootsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        LibraryExplorerPaths.MoveSelected(items, selected, direction);
        LibraryExplorerRootsList.Items.Clear();
        foreach (var item in items)
        {
            LibraryExplorerRootsList.Items.Add(item);
        }

        LibraryExplorerRootsList.SelectedItems.Clear();
        foreach (var item in selected)
        {
            LibraryExplorerRootsList.SelectedItems.Add(item);
        }

        if (LibraryExplorerRootsList.SelectedItem is LibraryExplorerRootItem focus)
        {
            LibraryExplorerRootsList.ScrollIntoView(focus);
        }

        RefreshLibraryExplorerRootButtons();
    }

    private void RefreshLibraryExplorerRootButtons()
    {
        var items = LibraryExplorerRootsList.Items.OfType<LibraryExplorerRootItem>().ToList();
        var selected = LibraryExplorerRootsList.SelectedItems.OfType<LibraryExplorerRootItem>().ToHashSet();
        var min = int.MaxValue;
        var max = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (!selected.Contains(items[i]))
            {
                continue;
            }

            min = Math.Min(min, i);
            max = Math.Max(max, i);
        }

        LibraryExplorerRootRemoveButton.IsEnabled = selected.Count > 0;
        LibraryExplorerRootUpButton.IsEnabled = selected.Count > 0 && min > 0;
        LibraryExplorerRootDownButton.IsEnabled = selected.Count > 0 && max >= 0 && max < items.Count - 1;
    }

    private const string RootDragFormat = "Mga.LibraryExplorerRoot";
    private Point _rootsDragOrigin;
    private LibraryExplorerRootItem? _rootsDragItem;

    private void LibraryExplorerRootsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _rootsDragOrigin = e.GetPosition(LibraryExplorerRootsList);
        _rootsDragItem = RootItemAt(_rootsDragOrigin);
    }

    private void LibraryExplorerRootsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_rootsDragItem is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(LibraryExplorerRootsList);
        if (Math.Abs(pos.X - _rootsDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _rootsDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var dragged = _rootsDragItem;
        _rootsDragItem = null;
        DragDrop.DoDragDrop(
            LibraryExplorerRootsList,
            new DataObject(RootDragFormat, dragged),
            DragDropEffects.Move);
    }

    private void LibraryExplorerRootsList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(RootDragFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void LibraryExplorerRootsList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(RootDragFormat) is not LibraryExplorerRootItem dragged)
        {
            return;
        }

        var items = LibraryExplorerRootsList.Items;
        var from = items.IndexOf(dragged);
        if (from < 0)
        {
            return;
        }

        var to = DropIndex(e.GetPosition(LibraryExplorerRootsList));
        if (to > from)
        {
            to--;
        }

        if (to == from || to < 0)
        {
            return;
        }

        items.RemoveAt(from);
        items.Insert(to, dragged);
        LibraryExplorerRootsList.SelectedItem = dragged;
        e.Handled = true;
    }

    private int DropIndex(Point point)
    {
        if (RootContainerAt(point) is not ListBoxItem container)
        {
            return LibraryExplorerRootsList.Items.Count;
        }

        var index = LibraryExplorerRootsList.ItemContainerGenerator.IndexFromContainer(container);
        if (index < 0)
        {
            return LibraryExplorerRootsList.Items.Count;
        }

        var top = container.TranslatePoint(new Point(0, 0), LibraryExplorerRootsList).Y;
        return point.Y > top + (container.ActualHeight / 2d) ? index + 1 : index;
    }

    private LibraryExplorerRootItem? RootItemAt(Point point)
    {
        if (RootContainerAt(point) is not ListBoxItem container)
        {
            return null;
        }

        return container.DataContext as LibraryExplorerRootItem
            ?? container.Content as LibraryExplorerRootItem;
    }

    private ListBoxItem? RootContainerAt(Point point)
    {
        var hit = LibraryExplorerRootsList.InputHitTest(point) as DependencyObject;
        while (hit != null && hit is not ListBoxItem)
        {
            hit = VisualTreeHelper.GetParent(hit);
        }

        return hit as ListBoxItem;
    }

    private void FillAssociations()
    {
        AssociationHost.Children.Clear();
        _videoAssociationHeader = null;
        _associationBoxes.Clear();
        _videoAssociationBoxes.Clear();
        var style = TryFindResource("DarkCheckBoxStyle") as Style;
        _syncingAssociations = true;
        try
        {
            var audio = BuildAssociationCategory(
                UiStrings.LabelFileAssociationsAudio,
                FileAssociations.AudioExtensions,
                style,
                ffmpegRequired: false);
            var video = BuildAssociationCategory(
                UiStrings.LabelFileAssociationsVideo,
                FileAssociations.VideoExtensions,
                style,
                ffmpegRequired: true);
            Grid.SetColumn(audio, 0);
            Grid.SetColumn(video, 2);
            AssociationHost.Children.Add(audio);
            AssociationHost.Children.Add(video);
        }
        finally
        {
            _syncingAssociations = false;
        }
    }

    private StackPanel BuildAssociationCategory(
        string title,
        IReadOnlyList<string> extensions,
        Style? style,
        bool ffmpegRequired)
    {
        var column = new StackPanel();
        var header = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 11.333,
            Margin = new Thickness(0, 0, 0, 6),
        };
                column.Children.Add(header);
        if (ffmpegRequired)
        {
            _videoAssociationHeader = header;
        }

        for (var i = 0; i < extensions.Count; i++)
        {
            var ext = extensions[i];
            var last = i == extensions.Count - 1;
            var box = new CheckBox
            {
                Content = CreateCheckAccessContent(FileAssociations.FormatCheckLabel(ext)),
                IsChecked = FileAssociations.IsAssociated(ext),
                Margin = new Thickness(0, 0, 0, last ? 0 : 6),
                Tag = ext,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            if (style is not null)
            {
                box.Style = style;
            }

            box.Checked += Association_Changed;
            box.Unchecked += Association_Changed;
            column.Children.Add(box);
            _associationBoxes.Add(box);
            if (ffmpegRequired)
            {
                _videoAssociationBoxes.Add(box);
            }
        }

        return column;
    }

    private void AssociationAllOnButton_Click(object sender, RoutedEventArgs e) =>
        SetEnabledAssociations(want: true);

    private void AssociationAllOffButton_Click(object sender, RoutedEventArgs e) =>
        SetEnabledAssociations(want: false);

    /// <summary>
    /// 操作できる関連付けだけをまとめて付ける／外す。灰色（FFmpeg 未設定の動画）は触らない。
    /// </summary>
    private void SetEnabledAssociations(bool want)
    {
        string? error = null;
        for (var i = 0; i < _associationBoxes.Count; i++)
        {
            var box = _associationBoxes[i];
            if (!ShouldBulkSetAssociation(box.IsEnabled, box.IsChecked == true, want))
            {
                continue;
            }

            if (!TryCommitAssociation(box, want, out var message) && error is null)
            {
                error = message;
            }
        }

        if (error is null)
        {
            return;
        }

        OwnerCenteredMessageBox.Show(
            this,
            error,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>一括ボタンがこのチェックを変えるか。無効な行と、すでに希望どおりの行は飛ばす。</summary>
    internal static bool ShouldBulkSetAssociation(bool enabled, bool isChecked, bool want) =>
        enabled && isChecked != want;

    private void Association_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingAssociations || sender is not CheckBox box || box.Tag is not string)
        {
            return;
        }

        if (!TryCommitAssociation(box, box.IsChecked == true, out var message) && message is not null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                message,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool TryCommitAssociation(CheckBox box, bool want, out string? error)
    {
        error = null;
        if (box.Tag is not string ext)
        {
            return false;
        }

        try
        {
            FileAssociations.SetAssociated(ext, want);
        }
        catch (Exception ex)
        {
            error = ex is InvalidOperationException
                ? ex.Message
                : UiStrings.ErrFileAssociationFailed(ex.Message);
        }

        var actual = FileAssociations.IsAssociated(ext);
        if (box.IsChecked != actual)
        {
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

        return error is null && actual == want;
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
        SelectedMultiFileArrange = MultiFileArrangeCombo.SelectedItem is MultiFileArrangeItem arrangeItem
            ? arrangeItem.Arrange
            : WaveformTileArrange.Off;
        SelectedLaunchMode = LaunchModeCombo.SelectedItem is LaunchModeItem launchItem
            ? launchItem.Slot
            : AppLaunchModes.DefaultSlot;
        if (!AppSettings.TryParseWallpaperOpacityPercent(WallpaperOpacityBox.Text, out var wallpaperOpacity))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorWallpaperOpacityRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            WallpaperOpacityBox.Focus();
            WallpaperOpacityBox.SelectAll();
            return;
        }

        SelectedWallpaperPath = WallpaperPathBox.Text.Trim().Trim('"');
        SelectedWallpaperOpacity = wallpaperOpacity;
        SelectedUiScalePercent = UiScaleCombo.SelectedItem is ScaleItem scaleItem
            ? scaleItem.Percent
            : UiScale.DefaultPercent;
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
        if (!PlaybackSampleProvider.TryParseMonitorDimDb(MonitorDimBox.Text, out var dimDb))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorMonitorDimDbRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            MonitorDimBox.Focus();
            MonitorDimBox.SelectAll();
            return;
        }

        SelectedMonitorDimDb = dimDb;
        if (!PlaybackSampleProvider.TryParseMonitorLowCutHz(MonitorLowCutBox.Text, out var lowCutHz))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorMonitorLowCutHzRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            MonitorLowCutBox.Focus();
            MonitorLowCutBox.SelectAll();
            return;
        }

        SelectedMonitorLowCutHz = lowCutHz;
        if (!SilentSkip.TryParseThresholdDb(SilentSkipThresholdBox.Text, out var silenceDb))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorSilentSkipThresholdRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SilentSkipThresholdBox.Focus();
            SilentSkipThresholdBox.SelectAll();
            return;
        }

        SelectedSilentSkipThresholdDb = silenceDb;
        if (!SilentSkip.TryParseRecordPadMs(SilentSkipRecordPadBox.Text, out var recordPadMs))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorSilentSkipRecordPadRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            SilentSkipRecordPadBox.Focus();
            SilentSkipRecordPadBox.SelectAll();
            return;
        }

        SelectedSilentSkipRecordPadMs = recordPadMs;
        if (!ClickGuard.TryParseFadeMs(ClickGuardFadeBox.Text, out var clickFadeMs))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorClickGuardFadeRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ClickGuardFadeBox.Focus();
            ClickGuardFadeBox.SelectAll();
            return;
        }

        SelectedClickGuardFadeMs = clickFadeMs;
        if (!WwiseTrackTiming.TryParsePrefetchLengthMs(WwisePrefetchLengthBox.Text, out var prefetchMs))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorWwisePrefetchLengthRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            WwisePrefetchLengthBox.Focus();
            WwisePrefetchLengthBox.SelectAll();
            return;
        }

        SelectedWwisePrefetchLengthMs = prefetchMs;
        if (!WwiseTrackTiming.TryParseLookAheadTimeMs(WwiseLookAheadTimeBox.Text, out var lookAheadMs))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorWwiseLookAheadTimeRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            WwiseLookAheadTimeBox.Focus();
            WwiseLookAheadTimeBox.SelectAll();
            return;
        }

        SelectedWwiseLookAheadTimeMs = lookAheadMs;
        SelectedFileNameColorRules = _fileNameColorMarkers?.Read() ?? [];
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
        SelectedLibraryExplorerRoots = LibraryExplorerRootsList.Items
            .OfType<LibraryExplorerRootItem>()
            .Select(item => item.Path)
            .ToArray();
        SelectedLibraryExplorerRoots = LibraryExplorerPaths.ResolveRoots(SelectedLibraryExplorerRoots);
        SelectedLibraryListColumnsWave = ReadLibraryColumns(
            _libraryColumnChecksWave,
            SelectedLibraryListColumnsWave);
        SelectedLibraryListColumnsMp3 = ReadLibraryColumns(
            _libraryColumnChecksMp3,
            SelectedLibraryListColumnsMp3);
        SelectedLibraryListColumnsVideo = ReadLibraryColumns(
            _libraryColumnChecksVideo,
            SelectedLibraryListColumnsVideo);
        SelectedLibraryPlaylistWaveformSizeWave = ReadLibraryPlaylistWaveformSize(
            LibraryPlaylistWaveformSizeWaveCombo,
            LibraryPlaylistWaveformSize.L);
        SelectedLibraryPlaylistWaveformSizeMp3 = ReadLibraryPlaylistWaveformSize(
            LibraryPlaylistWaveformSizeMp3Combo,
            LibraryPlaylistWaveformSize.L);
        SelectedGaplessPlayback = GaplessPlaybackBox.IsChecked != false;
        SelectedShowMainMenuBar = ShowMainMenuBarBox.IsChecked != false;
        ReadLayoutPresets();
        SelectedLibraryShowPlaylistPdf = LibraryShowPlaylistPdfBox.IsChecked != false;
        SelectedLibraryShowPlaylistMov = LibraryShowPlaylistMovBox.IsChecked != false;
        SelectedLibraryShowPlaylistMp4 = LibraryShowPlaylistMp4Box.IsChecked != false;
        SelectedLibraryShowPlaylistAvi = LibraryShowPlaylistAviBox.IsChecked != false;
        SelectedLibraryShowPlaylistMkv = LibraryShowPlaylistMkvBox.IsChecked != false;
        SelectedLibraryShowPlaylistWebm = LibraryShowPlaylistWebmBox.IsChecked != false;
        SelectedLibraryShowPlaylistMpg = LibraryShowPlaylistMpgBox.IsChecked != false;
        SelectedVideoProxyRetentionDays = ReadVideoProxyRetentionDays();
        SelectedFfmpegExePath = FfmpegPathBox.Text.Trim();
        SelectedVideoProxyDisableAutoEncode = VideoProxyDisableAutoEncodeBox.IsChecked == true;
        SelectedUseDxgiVideoPresenter = UseDxgiVideoPresenterBox.IsChecked == true;
        SelectedAutoSpeakerSelect = AutoSpeakerSelectBox.IsChecked == true;
        SelectedActiveSpeakerId = CurrentSpeaker()?.Id ?? _presets[0].Id;
        SelectedRecordDeviceId = ReadRecordDeviceId();
        var format = AudioFormatComboFill.Read(
            DefaultSampleRateCombo,
            DefaultBitDepthCombo,
            DefaultChannelLayoutCombo,
            _visibleIds);
        SelectedDefaultSampleRate = format.SampleRate;
        SelectedDefaultBitsPerSample = format.BitsPerSample;
        SelectedDefaultChannelLayout = format.Layout.Id;
        StopProbeUi();
        DialogResult = true;
    }

    private void PreviewWallpaperOpacity()
    {
        if (!AppSettings.TryParseWallpaperOpacityPercent(WallpaperOpacityBox.Text, out _))
        {
            return;
        }

        PreviewWallpaperPath();
    }

    private void NudgeWallpaperOpacity(int direction)
    {
        var current = AppSettings.TryParseWallpaperOpacityPercent(WallpaperOpacityBox.Text, out var opacity)
            ? (int)Math.Round(opacity * 100d)
            : (int)Math.Round(_wallpaperOpacityOpenedAt * 100d);
        WallpaperOpacityBox.Text = AppSettings.NudgeWallpaperOpacityPercent(current, direction)
            .ToString(CultureInfo.InvariantCulture);
        WallpaperOpacityBox.SelectAll();
    }

    private void PreviewWallpaperPath()
    {
        if (Owner is not MainWindow window
            || !WallpaperBitmap.TryPreviewPath(WallpaperPathBox.Text, out var path))
        {
            return;
        }

        var opacity = AppSettings.TryParseWallpaperOpacityPercent(WallpaperOpacityBox.Text, out var parsed)
            ? parsed
            : _wallpaperOpacityOpenedAt;
        window.LibraryBrowser.ApplyWallpaper(path, opacity);
    }

    private void RevertWallpaperPreview()
    {
        if (Owner is not MainWindow window)
        {
            return;
        }

        window.LibraryBrowser.ApplyWallpaper(_wallpaperPathOpenedAt, _wallpaperOpacityOpenedAt);
    }

    private void OpenColorSettingsButton_Click(object sender, RoutedEventArgs e) =>
        OpenColorSettings?.Invoke();

    private void WallpaperBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterWallpaperImage,
            Title = UiStrings.LabelWallpaper,
            CheckFileExists = true,
        };
        var current = WallpaperPathBox.Text.Trim().Trim('"');
        var dir = Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            WallpaperPathBox.Text = dialog.FileName;
        }
    }

    private void WallpaperClearButton_Click(object sender, RoutedEventArgs e) =>
        WallpaperPathBox.Text = string.Empty;

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

    private void FfmpegBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterFfmpegExe,
            Title = UiStrings.LabelFfmpegPath,
            CheckFileExists = true,
        };
        var current = FfmpegPathBox.Text.Trim().Trim('"');
        var dir = Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            FfmpegPathBox.Text = dialog.FileName;
        }
    }

    private void FfmpegPathBox_TextChanged(object sender, TextChangedEventArgs e) =>
        SyncFfmpegDependentControls();

    private void SyncFfmpegDependentControls()
    {
        var enabled = VideoProxy.TryResolveFfmpegExe(FfmpegPathBox.Text, out _);
        LibraryShowPlaylistAviBox.IsEnabled = enabled;
        LibraryShowPlaylistMkvBox.IsEnabled = enabled;
        LibraryShowPlaylistWebmBox.IsEnabled = enabled;
        LibraryShowPlaylistMpgBox.IsEnabled = enabled;
        // 関連付けのビデオも FFmpeg 未設定時は操作不可（見た目＋チェック不可）。
        var associationOpacity = enabled ? 1 : 0.45;
        if (_videoAssociationHeader is not null)
        {
            _videoAssociationHeader.Opacity = associationOpacity;
        }

        for (var i = 0; i < _videoAssociationBoxes.Count; i++)
        {
            var box = _videoAssociationBoxes[i];
            box.IsEnabled = enabled;
            box.Opacity = associationOpacity;
        }
    }

    private void LibraryCatalogClearButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmLibraryCatalogClear,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        LibraryCatalog.Clear();
        RefreshLibraryCatalogUsage();
        OwnerCenteredMessageBox.Show(
            this,
            UiStrings.InfoLibraryCatalogCleared,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void VideoProxyClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmVideoProxyClearAll,
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        var removed = VideoProxy.ClearAll();
        RefreshVideoProxyUsage();
        OwnerCenteredMessageBox.Show(
            this,
            UiStrings.InfoVideoProxyCleared(removed),
            UiStrings.DialogSettingsTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void FillDefaultAudioFormat()
    {
        var current = DefaultAudioFormat.Resolve(
            SelectedDefaultSampleRate,
            SelectedDefaultBitsPerSample,
            SelectedDefaultChannelLayout,
            _visibleIds);
        SelectedDefaultSampleRate = current.SampleRate;
        SelectedDefaultBitsPerSample = current.BitsPerSample;
        SelectedDefaultChannelLayout = current.Layout.Id;
        AudioFormatComboFill.Fill(
            DefaultSampleRateCombo,
            DefaultBitDepthCombo,
            DefaultChannelLayoutCombo,
            current,
            _visibleIds);
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

        if (IsProbeTabSelected())
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
        if (!IsLoaded || !IsProbeTabSelected())
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
        SilentSkipThresholdMeter.Reset();
        RefreshSilentSkipThresholdFloorText();
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
        SilentSkipThresholdMeter.ApplyLinearPeak(_probe.TakeMonoPeak());
        SilentSkipThresholdMeter.ApplyFloor(_probe.TakeMonoFloor());
        RefreshSilentSkipThresholdFloorText();
    }

    private void RefreshSilentSkipThresholdFloorText()
    {
        SilentSkipThresholdFloorText.Text =
            SilentSkip.FormatPeakDb(SilentSkipThresholdMeter.DisplayFloorDb) + " " + UiStrings.LabelDb;
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
        // プライマリ固定だとサブモニタ上でタブ切替（reflow）時に窓が飛ぶ。
        var work = ResolveSettingsWorkArea();
        var designMax = DesignMetrics.SettingsWindowMaxHeight;
        var maxH = Math.Max(MinHeight, Math.Min(designMax, work.Height - 32));
        MaxHeight = maxH;
        if (Height > maxH)
        {
            Height = maxH;
        }
        else if (Height < DesignMetrics.SettingsWindowHeight)
        {
            Height = Math.Min(DesignMetrics.SettingsWindowHeight, maxH);
        }

        // 幅は FitWindowToAudio 済み。位置だけ当該モニタ内へ押し込む。
        var placed = WindowPlacement.ClampRectToWorkArea(new Rect(Left, Top, Width, Height), work);
        Left = placed.X;
        Top = placed.Y;
    }

    /// <summary>今の窓がある（または最寄りの）モニタの作業領域。取れなければプライマリ。</summary>
    private Rect ResolveSettingsWorkArea()
    {
        if (WindowPlacement.TryGetContainingMonitorWorkDip(this, out var containing))
        {
            return containing;
        }

        var cx = Left + (Width > 1 ? Width * 0.5 : 0);
        var cy = Top + (Height > 1 ? Height * 0.5 : 0);
        if (WindowPlacement.TryGetWorkAreaDipForPoint(cx, cy, out var nearest))
        {
            return nearest;
        }

        return SystemParameters.WorkArea;
    }

    private bool IsAudioTabSelected() =>
        ReferenceEquals(SettingsTabs.SelectedItem, AudioTab);

    private bool IsProbeTabSelected() =>
        IsAudioTabSelected() || ReferenceEquals(SettingsTabs.SelectedItem, EditingTab);

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
        if (!TrySettingsTabDelta(key, Keyboard.Modifiers, out var delta))
        {
            return;
        }

        CycleSettingsTab(delta, wrap: !e.IsRepeat);
        e.Handled = true;
    }

    /// <summary>
    /// 設定タブの送り。Ctrl+Tab は次、Ctrl+Shift+Tab は前。
    /// Ctrl+PageDown／Ctrl+PageUp も同じ（エディタのタブ切替と同じ）。
    /// </summary>
    internal static bool TrySettingsTabDelta(Key key, ModifierKeys modifiers, out int delta)
    {
        delta = 0;
        if (key == Key.Tab && (modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            delta = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? -1 : 1;
            return true;
        }

        if (modifiers == ModifierKeys.Control && key is Key.PageDown or Key.PageUp)
        {
            delta = key == Key.PageDown ? 1 : -1;
            return true;
        }

        return false;
    }

    private void CycleSettingsTab(int delta, bool wrap = true)
    {
        var count = SettingsTabs.Items.Count;
        var next = StepTabIndex(SettingsTabs.SelectedIndex, count, delta, wrap);
        if (next != SettingsTabs.SelectedIndex)
        {
            SettingsTabs.SelectedIndex = next;
        }
    }

    /// <summary>
    /// 送り／戻し。wrap なら端で反対側へ。長押し連続では wrap=false で端に一旦止まる。
    /// </summary>
    internal static int StepTabIndex(int index, int count, int delta, bool wrap = true)
    {
        if (count < 1)
        {
            return 0;
        }

        if (index < 0)
        {
            index = 0;
        }

        if (count == 1 || delta == 0)
        {
            return Math.Clamp(index, 0, count - 1);
        }

        var step = Math.Sign(delta);
        if (!wrap)
        {
            return Math.Clamp(index + step, 0, count - 1);
        }

        return (index + step + count) % count;
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

    private void FillMultiFileArrange(WaveformTileArrange arrange)
    {
        MultiFileArrangeCombo.Items.Add(
            new MultiFileArrangeItem(WaveformTileArrange.Off, UiStrings.LabelMultiFileArrangeTabs));
        MultiFileArrangeCombo.Items.Add(
            new MultiFileArrangeItem(WaveformTileArrange.Horizontal, UiStrings.LabelMultiFileArrangeHorizontal));
        MultiFileArrangeCombo.Items.Add(
            new MultiFileArrangeItem(WaveformTileArrange.Vertical, UiStrings.LabelMultiFileArrangeVertical));
        MultiFileArrangeCombo.Items.Add(
            new MultiFileArrangeItem(WaveformTileArrange.Grid, UiStrings.LabelMultiFileArrangeGrid));
        foreach (MultiFileArrangeItem item in MultiFileArrangeCombo.Items)
        {
            if (item.Arrange == arrange)
            {
                MultiFileArrangeCombo.SelectedItem = item;
                return;
            }
        }

        MultiFileArrangeCombo.SelectedIndex = 0;
    }

    private void FillLaunchMode(WindowLayoutSlot slot)
    {
        LaunchModeCombo.Items.Add(new LaunchModeItem(WindowLayoutSlot.F9, UiStrings.LabelLayoutPresetF9));
        LaunchModeCombo.Items.Add(new LaunchModeItem(WindowLayoutSlot.F10, UiStrings.LabelLayoutPresetF10));
        LaunchModeCombo.Items.Add(new LaunchModeItem(WindowLayoutSlot.F11, UiStrings.LabelLayoutPresetF11));
        LaunchModeCombo.Items.Add(new LaunchModeItem(WindowLayoutSlot.F12, UiStrings.LabelLayoutPresetF12));
        foreach (LaunchModeItem item in LaunchModeCombo.Items)
        {
            if (item.Slot == slot)
            {
                LaunchModeCombo.SelectedItem = item;
                return;
            }
        }

        LaunchModeCombo.SelectedIndex = LaunchModeCombo.Items.Count - 1;
    }

    private void FillUiScale(int percent)
    {
        var current = UiScale.ClampPercent(percent);
        UiScaleCombo.Items.Clear();
        ScaleItem? selected = null;
        foreach (var preset in UiScale.Percents)
        {
            var item = new ScaleItem(preset, UiScale.FormatPercent(preset));
            UiScaleCombo.Items.Add(item);
            if (preset == current)
            {
                selected = item;
            }
        }

        if (selected is null)
        {
            selected = new ScaleItem(current, UiScale.FormatPercent(current));
            UiScaleCombo.Items.Add(selected);
        }

        UiScaleCombo.SelectedItem = selected;
    }

    private void UiScaleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || UiScaleCombo.SelectedItem is not ScaleItem item)
        {
            return;
        }

        PreviewUiScale(item.Percent);
        ComboBoxFit.Apply(UiScaleCombo);
    }

    private void PreviewUiScale(int percent)
    {
        percent = UiScale.ClampPercent(percent);
        SelectedUiScalePercent = percent;
        if (AppStorage.Settings.UiScalePercent == percent)
        {
            return;
        }

        AppStorage.Settings.UiScalePercent = percent;
        UiScaleService.ApplyFromSettings();
        ReflowSettingsWindow();
    }

    private void RevertUiScalePreview()
    {
        if (AppStorage.Settings.UiScalePercent == _uiScaleOpenedAt)
        {
            return;
        }

        AppStorage.Settings.UiScalePercent = _uiScaleOpenedAt;
        SelectedUiScalePercent = _uiScaleOpenedAt;
        UiScaleService.ApplyFromSettings();
    }

    private void PreviewShowMainMenuBar(bool show)
    {
        SelectedShowMainMenuBar = show;
        if (AppStorage.Settings.ShowMainMenuBar == show)
        {
            return;
        }

        AppStorage.Settings.ShowMainMenuBar = show;
        if (Owner is MainWindow window)
        {
            window.EnsureMainMenuBar();
        }
    }

    private void RevertShowMainMenuBarPreview()
    {
        if (AppStorage.Settings.ShowMainMenuBar == _showMainMenuBarOpenedAt)
        {
            return;
        }

        AppStorage.Settings.ShowMainMenuBar = _showMainMenuBarOpenedAt;
        SelectedShowMainMenuBar = _showMainMenuBarOpenedAt;
        if (Owner is MainWindow window)
        {
            window.EnsureMainMenuBar();
        }
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
        var deviceMax = Math.Max(80, ResolveSettingsWorkArea().Width - 200);
        ComboBoxFit.Apply(LanguageCombo);
        ComboBoxFit.Apply(UiScaleCombo);
        ComboBoxFit.Apply(MultiFileArrangeCombo);
        ComboBoxFit.Apply(LaunchModeCombo);
        ComboBoxFit.Apply(LibraryPlaylistWaveformSizeWaveCombo);
        ComboBoxFit.Apply(LibraryPlaylistWaveformSizeMp3Combo);
        ComboBoxFit.Apply(VideoProxyRetentionCombo);
        ComboBoxFit.Apply(DefaultSampleRateCombo);
        ComboBoxFit.Apply(DefaultBitDepthCombo);
        ComboBoxFit.Apply(DefaultChannelLayoutCombo);
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
        // 設定ウィンドウは表示倍率の対象外（常に等倍）。
        // 幅は文字計測だけから決める。ActualWidth を拾うと、広げたあとにまた広がる。
        var pad = DesignMetrics.AudioPad.Left + DesignMetrics.AudioPad.Right;
        var chrome = SettingsWindowChromeWidth();
        var content = Max(
            AudioTabContentWidth(),
            SettingsTabBarWidth(),
            LayoutTabContentWidth(),
            LayoutsTabContentWidth(),
            PlayerTabContentWidth());
        var inner = content + pad + DesignMetrics.SettingsWindowContentMargin;
        var width = Math.Ceiling(inner + chrome);
        var min = DesignMetrics.SettingsWindowMinWidth;
        var max = Math.Max(min, ResolveSettingsWorkArea().Width - 32);
        width = Math.Clamp(width, min, max);
        MinWidth = width;
        MaxWidth = width;
        Width = width;
    }

    /// <summary>
    /// 枠だけ。ActualWidth − Content は、レイアウト前や中身が左寄せのとき膨らみ、タブ切替で幅が増え続ける。
    /// </summary>
    internal static double SettingsWindowChromeWidth() =>
        SystemParameters.ResizeFrameVerticalBorderWidth * 2 + 2;

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

    private double LayoutsTabContentWidth()
    {
        var gutter = DesignMetrics.SettingsScrollBarGap + DesignMetrics.SettingsScrollBarWidth;
        var speakers = Max(
            LabelWidth(SpeakerVisibilityHeader),
            MeasureSpeakerVisibilityWidth()) + gutter;
        var waveformCombo = Math.Max(
            LibraryPlaylistWaveformSizeWaveCombo.Width > 1
                ? LibraryPlaylistWaveformSizeWaveCombo.Width
                : DesignMetrics.From96(72),
            LibraryPlaylistWaveformSizeMp3Combo.Width > 1
                ? LibraryPlaylistWaveformSizeMp3Combo.Width
                : DesignMetrics.From96(72));
        var waveformRow = LabelWidth(LibraryPlaylistWaveformSizeWaveHeader) + 8 + waveformCombo;
        var wave = Max(
            LabelWidth(LibraryColumnsWaveHeader) + 8 + DesignMetrics.From96(88),
            waveformRow,
            MeasureLibraryColumnChecksWidth()) + gutter;
        var mp3 = Max(
            LabelWidth(LibraryColumnsMp3Header) + 8 + DesignMetrics.From96(88),
            waveformRow,
            MeasureLibraryColumnChecksWidth()) + gutter;
        var playlistDocs = Max(
            LabelWidth(LibraryPlaylistDocumentsHeader) + gutter,
            MeasureCheckBoxContentWidth(LibraryShowPlaylistPdfBox) + gutter);
        var playlist = Max(
            LabelWidth(LibraryColumnsHeader) + gutter,
            playlistDocs,
            wave + DesignMetrics.SettingsColumnGap + mp3);
        return speakers + DesignMetrics.SettingsColumnGap + playlist;
    }

    private double PlayerTabContentWidth()
    {
        var gutter = DesignMetrics.SettingsScrollBarGap + DesignMetrics.SettingsScrollBarWidth;
        return Max(
            MeasureCheckBoxContentWidth(GaplessPlaybackBox),
            LabelWidth(LibraryExplorerRootsHeader)) + gutter;
    }

    private double MeasureSpeakerVisibilityWidth()
    {
        var widest = 0d;
        foreach (var child in SpeakerVisibilityHost.Children)
        {
            if (child is CheckBox box)
            {
                widest = Math.Max(widest, MeasureCheckBoxContentWidth(box));
            }
        }

        return widest;
    }

    private double MeasureLibraryColumnChecksWidth()
    {
        var widest = 0d;
        foreach (var column in LibraryColumnFilter.All)
        {
            if (LibraryColumnFilter.IsLocked(column))
            {
                continue;
            }

            widest = Math.Max(
                widest,
                ComboBoxFit.MeasureText(this, UiStrings.LibraryColumnLabel(column), 11.333) + 28);
        }

        return widest;
    }

    private double MeasureCheckBoxContentWidth(CheckBox box)
    {
        var text = box.Content switch
        {
            TextBlock block => block.Text ?? string.Empty,
            string s => s,
            _ => box.Content?.ToString() ?? string.Empty,
        };
        if (string.IsNullOrEmpty(text))
        {
            return DesignMetrics.From96(120);
        }

        return ComboBoxFit.MeasureText(this, text, 11.333) + 28;
    }

    private double LayoutTabContentWidth()
    {
        var gutter = DesignMetrics.SettingsScrollBarGap + DesignMetrics.SettingsScrollBarWidth;
        // 全画面行。
        var fullscreenRow = ComboBoxFit.MeasureText(this, UiStrings.LabelLayoutFullscreen, 11.333)
            + 8 + 120 + 8
            + ComboBoxFit.MeasureText(this, UiStrings.LabelLayoutFullscreenToggleStart, 11.333)
            + 8 + 100;
        // チェック行：波形・レベル・ステータス・アナライザー・位置・既定に戻す。
        double CheckWidth(string label) => ComboBoxFit.MeasureText(this, label, 11.333) + 28;
        var checksRow = CheckWidth(UiStrings.LabelLayoutShowWaveform)
            + 12
            + CheckWidth(UiStrings.LabelLayoutLevelMeter)
            + 12
            + CheckWidth(UiStrings.LabelLayoutStatusBar)
            + 12
            + CheckWidth(UiStrings.LabelLayoutAnalyzerArea)
            + 12
            + ComboBoxFit.MeasureText(this, UiStrings.LabelLayoutAnalyzerDock, 11.333)
            + 8 + 100 + 12
            + ComboBoxFit.MeasureText(this, UiStrings.LabelLayoutPresetReset, 11.333)
            + 24;
        return Max(
            MeasureCheckBoxContentWidth(ShowMainMenuBarBox),
            fullscreenRow,
            checksRow) + gutter;
    }

    private double SettingsTabBarWidth()
    {
        var headers = new[]
        {
            UiStrings.LabelSettingsTabGeneral,
            UiStrings.LabelSettingsTabLayout,
            UiStrings.LabelSettingsTabLayouts,
            UiStrings.LabelSettingsTabColor,
            UiStrings.LabelSettingsTabAudio,
            UiStrings.LabelSettingsTabPlayer,
            UiStrings.LabelSettingsTabEditing,
            UiStrings.LabelSettingsTabExport,
            UiStrings.LabelSettingsTabWwise,
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

    private double LabelWidth(TextBlock label) =>
        // Wrap 後の ActualWidth は列幅いっぱいに伸びるので、文字幅だけ使う。
        ComboBoxFit.MeasureText(label, label.Text ?? string.Empty, label.FontSize, label.FontWeight);

    private static double Max(params double[] values)
    {
        var max = 0d;
        foreach (var value in values)
        {
            max = Math.Max(max, value);
        }

        return max;
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
            var usedAccessKeys = new HashSet<char>();
            foreach (var preset in _presets)
            {
                var box = new CheckBox
                {
                    Content = CreateCheckAccessContent(
                        WithUniqueAccessKey(preset.DisplayName(), usedAccessKeys)),
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

        var format = AudioFormatComboFill.Read(
            DefaultSampleRateCombo,
            DefaultBitDepthCombo,
            DefaultChannelLayoutCombo,
            _visibleIds);
        SelectedDefaultSampleRate = format.SampleRate;
        SelectedDefaultBitsPerSample = format.BitsPerSample;
        SelectedDefaultChannelLayout = format.Layout.Id;
        FillSpeakers(SelectedActiveSpeakerId);
        FillDefaultAudioFormat();
        ComboBoxFit.Apply(SpeakerCombo);
        ComboBoxFit.Apply(DefaultSampleRateCombo);
        ComboBoxFit.Apply(DefaultBitDepthCombo);
        ComboBoxFit.Apply(DefaultChannelLayoutCombo);
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
        var fileMap = ChannelRouter.Normalize(_fileChannelMap, layout.Channels, layout.Channels);
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

    private void OnUiThemeChanged(object? sender, EventArgs e)
    {
        AcrylicBackdrop.Apply(this);
        ApplySettingsTabChrome();
        SlidingTabAccent.RefreshAllBrushes();
        RefreshFadeRowChrome(_fadeInRow);
        RefreshFadeRowIcon(_fadeInRow);
        RefreshFadeRowChrome(_fadeOutRow);
        RefreshFadeRowIcon(_fadeOutRow);
        ActionButtonLooks.ApplyAccent(OkButton);
        ActionButtonLooks.ApplyClear(CancelButton);
        ActionButtonLooks.ApplyClear(LameBrowseButton);
        ActionButtonLooks.ApplyClear(FfmpegBrowseButton);
        ActionButtonLooks.ApplyClear(VideoProxyClearAllButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsWaveResetButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsMp3ResetButton);
        ActionButtonLooks.ApplyClear(LibraryColumnsVideoResetButton);
        ActionButtonLooks.ApplyClear(AssociationAllOnButton);
        ActionButtonLooks.ApplyClear(AssociationAllOffButton);
        ActionButtonLooks.ApplyClear(WallpaperBrowseButton);
        ActionButtonLooks.ApplyClear(WallpaperClearButton);
        ActionButtonLooks.ApplyClear(OpenColorSettingsButton);
        ActionButtonLooks.ApplyClear(FileNameColorMarkerAddButton);
        ApplyLibraryExplorerRootButtons();
        ApplyLibraryExplorerRootsChrome();
        RefreshTestButtons();
    }

    /// <summary>
    /// 設定タブは DynamicResource を使わず固定ブラシにする。
    /// DynamicResource の再評価などで下線がちらつくため。
    /// </summary>
    private void ApplySettingsTabChrome()
    {
        var muted = ResolveThemeBrush("MutedForeBrush");
        var primary = ResolveThemeBrush("PrimaryForeBrush");
        var hover = ResolveThemeBrush("PlayerHoverFillBrush");

        var itemStyle = new Style(typeof(TabItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, muted));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Stretch));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12d));
        itemStyle.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));

        var template = new ControlTemplate(typeof(TabItem));
        var borderFactory = new FrameworkElementFactory(typeof(Border), "Bd");
        // TabItem.IsMouseOver は本文上でも true になるので、ヘッダー Border だけ見る。
        borderFactory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
        borderFactory.SetValue(Border.BorderThicknessProperty, new Thickness(0));
        borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
        borderFactory.SetValue(Border.SnapsToDevicePixelsProperty, true);
        var contentFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        contentFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        contentFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        contentFactory.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        contentFactory.SetValue(TextElement.ForegroundProperty, new TemplateBindingExtension(Control.ForegroundProperty));
        borderFactory.AppendChild(contentFactory);
        template.VisualTree = borderFactory;

        var selectedTrigger = new Trigger { Property = TabItem.IsSelectedProperty, Value = true };
        // 下線は SlidingTabAccent が隣からスライドさせる。ここでは文字色だけ。
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, primary));
        template.Triggers.Add(selectedTrigger);

        var hoverTrigger = new Trigger
        {
            SourceName = "Bd",
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover, "Bd"));
        template.Triggers.Add(hoverTrigger);

        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
        SettingsTabs.ItemContainerStyle = itemStyle;
        SlidingTabAccent.Attach(SettingsTabs);
    }

    private static void RefreshFadeRowChrome(FadeCurveRow row)
    {
        row.Label.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush"));
        row.IconHost.Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush"));
        row.IconHost.BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush"));
    }

    private FadeCurveRow CreateFadeRow(string labelText, FadeShape curve, bool isFadeIn, int row)
    {
        var rowHeight = DesignMetrics.FadeOptionRowHeight;
        var iconSide = FadeCurveIcons.WidthFor((int)Math.Round(rowHeight));

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 2),
            Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")),
        };

        var iconHost = new Border
        {
            Width = iconSide,
            Height = rowHeight,
            Margin = new Thickness(0, 0, 0, 2),
            Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush")),
            BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
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

        Grid.SetRow(label, row);
        Grid.SetRow(iconHost, row);
        Grid.SetColumn(iconHost, 1);
        FadeRowsHost.Children.Add(label);
        FadeRowsHost.Children.Add(iconHost);

        var fadeRow = new FadeCurveRow(label, icon, iconHost, curve, isFadeIn);
        RefreshFadeRowIcon(fadeRow);
        iconHost.MouseLeftButtonUp += (_, _) => ShowFadeCurvePicker(fadeRow);
        return fadeRow;
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
                    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeCurveMenu = null;
        StopProbeUi();
        _probe.Dispose();
        if (DialogResult != true)
        {
            RevertUiScalePreview();
            RevertWallpaperPreview();
            RevertShowMainMenuBarPreview();
            RevertLayoutPresetPreviews();
        }

        WindowPlacement.CaptureSettings(this, AppStorage.Settings);
        AppStorage.Save();
        base.OnClosed(e);
    }

    private sealed class FadeCurveRow(
        TextBlock label,
        Image icon,
        Border iconHost,
        FadeShape curve,
        bool isFadeIn)
    {
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

    internal sealed record LibraryExplorerRootItem(string Path)
    {
        public bool IsMissing => LibraryExplorerPaths.IsMissing(Path);

        public override string ToString() => Path;
    }

    private static LibraryPlaylistWaveformSize ReadLibraryPlaylistWaveformSize(
        ComboBox combo,
        LibraryPlaylistWaveformSize fallback) =>
        combo.SelectedItem is LibraryPlaylistWaveformSizeItem sizeItem
            ? sizeItem.Size
            : fallback;

    /// <summary>CheckBox の Content。文字列の _ を AccessText として効かせ、折り返しも可。</summary>
    private static AccessText CreateCheckAccessContent(string text) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        };

    /// <summary>一覧内で一意なアクセスキーをラベルへ付与（スピーカー表示チェック用）。</summary>
    private static string WithUniqueAccessKey(string label, HashSet<char> used)
    {
        for (var i = 0; i < label.Length; i++)
        {
            var c = label[i];
            if (!char.IsAsciiLetterOrDigit(c))
            {
                continue;
            }

            var key = char.ToUpperInvariant(c);
            if (!used.Add(key))
            {
                continue;
            }

            return label.Insert(i, "_");
        }

        for (var d = '0'; d <= '9'; d++)
        {
            if (!used.Add(d))
            {
                continue;
            }

            return $"{label} (_{d})";
        }

        return label;
    }

    private sealed record LaunchModeItem(WindowLayoutSlot Slot, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record MultiFileArrangeItem(WaveformTileArrange Arrange, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record LibraryPlaylistWaveformSizeItem(LibraryPlaylistWaveformSize Size, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record VideoProxyRetentionItem(int Days, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ScaleItem(int Percent, string Label)
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

    private sealed class LayoutPresetEditor
    {
        private readonly ComboBox _fullscreen;
        private readonly ComboBox _toggleStart;
        private readonly CheckBox? _waveform;
        private readonly CheckBox _analyzer;
        private readonly ComboBox _analyzerDock;
        private readonly CheckBox _levelMeter;
        private readonly CheckBox _statusBar;
        private readonly Action<WindowLayoutSlot>? _onChanged;
        private bool _suppressNotify;

        private LayoutPresetEditor(
            WindowLayoutSlot slot,
            ComboBox fullscreen,
            ComboBox toggleStart,
            CheckBox? waveform,
            CheckBox analyzer,
            ComboBox analyzerDock,
            CheckBox levelMeter,
            CheckBox statusBar,
            Action<WindowLayoutSlot>? onChanged)
        {
            Slot = slot;
            _fullscreen = fullscreen;
            _toggleStart = toggleStart;
            _waveform = waveform;
            _analyzer = analyzer;
            _analyzerDock = analyzerDock;
            _levelMeter = levelMeter;
            _statusBar = statusBar;
            _onChanged = onChanged;
        }

        public WindowLayoutSlot Slot { get; }

        public FrameworkElement Root { get; private set; } = null!;

        public static LayoutPresetEditor Create(
            WindowLayoutSlot slot,
            string title,
            WindowLayoutPreset preset,
            Action<WindowLayoutSlot>? onChanged = null)
        {
            var fullscreen = CreateLayoutCombo(new Thickness(0, 0, 8, 0));
            fullscreen.Items.Add(new NamedModeItem(LayoutFullscreenMode.Off, UiStrings.LabelLayoutFullscreenOff));
            fullscreen.Items.Add(new NamedModeItem(LayoutFullscreenMode.On, UiStrings.LabelLayoutFullscreenOn));
            fullscreen.Items.Add(new NamedModeItem(LayoutFullscreenMode.Toggle, UiStrings.LabelLayoutFullscreenToggle));
            SelectFullscreen(fullscreen, preset.ResolvedFullscreenMode());
            ComboBoxFit.Apply(fullscreen);

            var toggleStart = CreateLayoutCombo(new Thickness(0, 0, 0, 0));
            toggleStart.Items.Add(new NamedBoolItem(false, UiStrings.LabelLayoutOff));
            toggleStart.Items.Add(new NamedBoolItem(true, UiStrings.LabelLayoutOn));
            toggleStart.SelectedIndex = preset.FullscreenToggleStartsOn ? 1 : 0;
            ComboBoxFit.Apply(toggleStart);

            // 波形表示はプレイヤー（F9／F10）のみ。エディタは常に波形あり。
            CheckBox? waveform = null;
            if (WindowLayoutPreset.IsPlayerLayoutSlot(slot))
            {
                waveform = new CheckBox
                {
                    Style = (Style)Application.Current.FindResource("DarkCheckBoxStyle"),
                    Content = UiStrings.LabelLayoutShowWaveform,
                    IsChecked = preset.ShowWaveform,
                    Margin = new Thickness(0, 2, 12, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }

            var levelMeter = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("DarkCheckBoxStyle"),
                Content = UiStrings.LabelLayoutLevelMeter,
                IsChecked = preset.ShowLevelMeter,
                Margin = new Thickness(0, 2, 12, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var statusBar = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("DarkCheckBoxStyle"),
                Content = UiStrings.LabelLayoutStatusBar,
                IsChecked = preset.ResolvedShowStatusBar(slot),
                Margin = new Thickness(0, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var analyzer = new CheckBox
            {
                Style = (Style)Application.Current.FindResource("DarkCheckBoxStyle"),
                Content = UiStrings.LabelLayoutAnalyzerArea,
                IsChecked = preset.ShowAnalyzerArea,
                Margin = new Thickness(12, 2, 0, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var analyzerDock = CreateLayoutCombo(new Thickness(8, 0, 0, 0));
            analyzerDock.Items.Add(new NamedDockItem(LayoutAnalyzerDock.Bottom, UiStrings.LabelLayoutAnalyzerDockBottom));
            analyzerDock.Items.Add(new NamedDockItem(LayoutAnalyzerDock.Top, UiStrings.LabelLayoutAnalyzerDockTop));
            analyzerDock.SelectedIndex = preset.ResolvedAnalyzerDock() == LayoutAnalyzerDock.Top ? 1 : 0;
            ComboBoxFit.Apply(analyzerDock);

            var editor = new LayoutPresetEditor(
                slot,
                fullscreen,
                toggleStart,
                waveform,
                analyzer,
                analyzerDock,
                levelMeter,
                statusBar,
                onChanged);

            void SyncDependent() => editor.SyncDependent();
            void Notify() => editor.NotifyChanged();

            fullscreen.SelectionChanged += (_, _) =>
            {
                SyncDependent();
                Notify();
            };
            toggleStart.SelectionChanged += (_, _) => Notify();
            analyzer.Checked += (_, _) =>
            {
                SyncDependent();
                Notify();
            };
            analyzer.Unchecked += (_, _) =>
            {
                SyncDependent();
                Notify();
            };
            // DropDownClosed も拾う（位置上下の即時反映漏れ防止）。
            analyzerDock.SelectionChanged += (_, _) => Notify();
            analyzerDock.DropDownClosed += (_, _) => Notify();
            levelMeter.Checked += (_, _) => Notify();
            levelMeter.Unchecked += (_, _) => Notify();
            statusBar.Checked += (_, _) => Notify();
            statusBar.Unchecked += (_, _) => Notify();
            if (waveform is not null)
            {
                waveform.Checked += (_, _) => Notify();
                waveform.Unchecked += (_, _) => Notify();
            }

            SyncDependent();

            var resetButton = new RoundedButton
            {
                Content = UiStrings.LabelLayoutPresetReset,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(10, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ActionButtonLooks.ApplyClear(resetButton);
            AppDialogKeys.PrepareActionButton(resetButton);
            resetButton.Click += (_, _) => editor.ResetToDefaults();

            var root = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            root.Children.Add(new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.Bold,
                FontSize = 11.333,
                Margin = new Thickness(0, 0, 0, 6),
                VerticalAlignment = VerticalAlignment.Center,
            });
            var fullscreenRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
            fullscreenRow.Children.Add(new TextBlock
            {
                Text = UiStrings.LabelLayoutFullscreen,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 72,
            });
            fullscreenRow.Children.Add(fullscreen);
            fullscreenRow.Children.Add(new TextBlock
            {
                Text = UiStrings.LabelLayoutFullscreenToggleStart,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            fullscreenRow.Children.Add(toggleStart);
            root.Children.Add(fullscreenRow);

            var checksRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 2),
            };
            if (waveform is not null)
            {
                checksRow.Children.Add(waveform);
            }

            checksRow.Children.Add(levelMeter);
            checksRow.Children.Add(statusBar);
            checksRow.Children.Add(analyzer);
            checksRow.Children.Add(new TextBlock
            {
                Text = UiStrings.LabelLayoutAnalyzerDock,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
            });
            checksRow.Children.Add(analyzerDock);
            checksRow.Children.Add(resetButton);
            root.Children.Add(checksRow);
            editor.Root = root;
            return editor;
        }

        public WindowLayoutPreset Read()
        {
            var preset = new WindowLayoutPreset
            {
                ShowLibraryAndFavorites = WindowLayoutPreset.CreateDefault(Slot).ShowLibraryAndFavorites,
                // エディタは常に波形オン（UI 無し）。プレイヤーのみチェック反映。
                ShowWaveform = WindowLayoutPreset.IsPlayerLayoutSlot(Slot)
                    ? _waveform?.IsChecked != false
                    : true,
                ShowAnalyzerArea = _analyzer.IsChecked == true,
                ShowLevelMeter = _levelMeter.IsChecked == true,
                ShowStatusBar = _statusBar.IsChecked == true,
                FullscreenToggleStartsOn = _toggleStart.SelectedItem is NamedBoolItem { Value: true },
            };
            preset.ApplyFullscreenMode(
                _fullscreen.SelectedItem is NamedModeItem mode
                    ? mode.Mode
                    : LayoutFullscreenMode.Off);
            preset.ApplyAnalyzerDock(
                _analyzerDock.SelectedItem is NamedDockItem dock
                    ? dock.Dock
                    : LayoutAnalyzerDock.Bottom);
            return preset;
        }

        public void ResetToDefaults()
        {
            var defaults = WindowLayoutPreset.CreateDefault(Slot);
            ApplyPreset(defaults);
        }

        private void NotifyChanged()
        {
            if (_suppressNotify)
            {
                return;
            }

            _onChanged?.Invoke(Slot);
        }

        private void ApplyPreset(WindowLayoutPreset preset)
        {
            _suppressNotify = true;
            try
            {
                SelectFullscreen(_fullscreen, preset.ResolvedFullscreenMode());
                _toggleStart.SelectedIndex = preset.FullscreenToggleStartsOn ? 1 : 0;
                if (_waveform is not null)
                {
                    _waveform.IsChecked = preset.ShowWaveform;
                }

                _analyzer.IsChecked = preset.ShowAnalyzerArea;
                _analyzerDock.SelectedIndex =
                    preset.ResolvedAnalyzerDock() == LayoutAnalyzerDock.Top ? 1 : 0;
                _levelMeter.IsChecked = preset.ShowLevelMeter;
                _statusBar.IsChecked = preset.ResolvedShowStatusBar(Slot);
                SyncDependent();
            }
            finally
            {
                _suppressNotify = false;
            }

            NotifyChanged();
        }

        private void SyncDependent()
        {
            var isToggle = _fullscreen.SelectedItem is NamedModeItem { Mode: LayoutFullscreenMode.Toggle };
            _toggleStart.IsEnabled = isToggle;
            // 位置はアナライザオフ中も選べる（オンにした瞬間の上下を即反映するため）。
            _analyzerDock.IsEnabled = true;
        }

        private static void SelectFullscreen(ComboBox combo, LayoutFullscreenMode mode)
        {
            for (var i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is NamedModeItem item && item.Mode == mode)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }

            combo.SelectedIndex = 0;
        }

        /// <summary>全画面／トグル開始／位置の高さを他設定コンボと同じにする。</summary>
        private static ComboBox CreateLayoutCombo(Thickness margin) =>
            new()
            {
                Margin = margin,
                Height = DesignMetrics.AudioInputHeight,
                Padding = new Thickness(6, 1, 6, 1),
                FontSize = 11.333,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

        private sealed record NamedModeItem(LayoutFullscreenMode Mode, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record NamedBoolItem(bool Value, string Label)
        {
            public override string ToString() => Label;
        }

        private sealed record NamedDockItem(LayoutAnalyzerDock Dock, string Label)
        {
            public override string ToString() => Label;
        }
    }
}
