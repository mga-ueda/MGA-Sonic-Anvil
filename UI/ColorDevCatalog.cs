using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>色パネルのグループ。大枠の共通色のあと、メイン画面の並び（上→下、左→右）に合わせる。</summary>
internal enum ColorDevGroup
{
    Shared,
    Overview,
    Waveform,
    Guides,
    Selection,
    SampleLoop,
    Region,
    Marker,
    Loudness,
    Meter,
    Spectrum,
    VectorScope,
    Player,
    Transport,
    StatusBar,
    Dialog,
    Other,
}

/// <summary>色パネルのグループ分けと検索。</summary>
internal static class ColorDevCatalog
{
    private static readonly Dictionary<string, ColorDevGroup> Groups = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> Ranks = new(StringComparer.OrdinalIgnoreCase);

    static ColorDevCatalog()
    {
        var catalog = new (string Key, ColorDevGroup Group)[]
        {
            ("SurfaceBackBrush", ColorDevGroup.Shared),
            ("ChromeBackBrush", ColorDevGroup.Shared),
            ("ChromeBorderBrush", ColorDevGroup.Shared),
            ("LibraryExplorerSplitterBrush", ColorDevGroup.Shared),
            ("ChromeMidBrush", ColorDevGroup.Shared),
            ("ChromeDimBrush", ColorDevGroup.Shared),
            ("PrimaryForeBrush", ColorDevGroup.Shared),
            ("MutedForeBrush", ColorDevGroup.Shared),
            ("AccentCyanBrush", ColorDevGroup.Shared),
            ("DirtyAccentBrush", ColorDevGroup.Shared),
            ("ScrollThumbBackBrush", ColorDevGroup.Shared),
            ("ScrollThumbHoverBackBrush", ColorDevGroup.Shared),
            ("ScrollThumbPressedBackBrush", ColorDevGroup.Shared),
            ("ScrollThumbGripBrush", ColorDevGroup.Shared),
            ("ControlHoverBorderBrush", ColorDevGroup.Shared),
            ("DialogInputBackBrush", ColorDevGroup.Shared),
            ("MenuSeparatorBrush", ColorDevGroup.Shared),
            ("MenuHighlightBackBrush", ColorDevGroup.Shared),
            ("MenuDisabledForeBrush", ColorDevGroup.Shared),

            ("ProjectBarBackBrush", ColorDevGroup.Overview),
            ("OverviewOutsideFillBrush", ColorDevGroup.Overview),

            ("WaveformBackBrush", ColorDevGroup.Waveform),
            ("WaveformRecordBackBrush", ColorDevGroup.Waveform),
            ("WaveformTileActiveHeaderBrush", ColorDevGroup.Waveform),
            ("TileSearchVeilBrush", ColorDevGroup.Waveform),
            ("TimelineWellBackBrush", ColorDevGroup.Waveform),
            ("WaveFillBrush", ColorDevGroup.Waveform),
            ("WaveFillOverlayBrush", ColorDevGroup.Waveform),
            ("WaveZeroLineBrush", ColorDevGroup.Waveform),
            ("ChannelLabelOnTintForeBrush", ColorDevGroup.Waveform),
            ("DbScaleForeBrush", ColorDevGroup.Waveform),
            ("SpectrogramGridBrush", ColorDevGroup.Waveform),
            ("SpectrogramSelectionFillBrush", ColorDevGroup.Waveform),
            ("SpectrogramScaleForeBrush", ColorDevGroup.Waveform),
            ("SpectrogramScaleEdgeBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostFloorBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostMidBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostCeilBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostThumbFillBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostThumbStrokeBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostThumbHoverFillBrush", ColorDevGroup.Waveform),
            ("SpectrogramBoostThumbHoverStrokeBrush", ColorDevGroup.Waveform),

            ("PlayheadBrush", ColorDevGroup.Guides),
            ("SeekExitBrush", ColorDevGroup.Guides),
            ("MouseGuideBrush", ColorDevGroup.Guides),
            ("MouseGuideOnSelectionBrush", ColorDevGroup.Guides),

            ("WaveSelectionFillBrush", ColorDevGroup.Selection),
            ("LoopRangeFillBrush", ColorDevGroup.Selection),

            ("SampleLoopTimelineBrush", ColorDevGroup.SampleLoop),
            ("SampleLoopGripBrush", ColorDevGroup.SampleLoop),
            ("SampleLoopWaveFillBrush", ColorDevGroup.SampleLoop),
            ("SampleLoopTimeLabelForeBrush", ColorDevGroup.SampleLoop),

            ("RegionTimelineBrush", ColorDevGroup.Region),
            ("RegionWaveFillBrush", ColorDevGroup.Region),
            ("RegionTimeLabelForeBrush", ColorDevGroup.Region),
            ("RegionWaveFillAnacrusisBrush", ColorDevGroup.Region),
            ("RegionWaveFillLoopBrush", ColorDevGroup.Region),
            ("RegionWaveFillExitBrush", ColorDevGroup.Region),

            ("MarkerBrush", ColorDevGroup.Marker),
            ("MarkerSelectedBrush", ColorDevGroup.Marker),
            ("MarkerSelectedBorderBrush", ColorDevGroup.Marker),
            ("MarkerLabelForeBrush", ColorDevGroup.Marker),

            ("LoudnessWaveFillBrush", ColorDevGroup.Loudness),
            ("LoudnessGridBrush", ColorDevGroup.Loudness),
            ("LoudnessTargetLineBrush", ColorDevGroup.Loudness),
            ("LoudnessSafeBrush", ColorDevGroup.Loudness),
            ("LoudnessCautionBrush", ColorDevGroup.Loudness),
            ("LoudnessDangerBrush", ColorDevGroup.Loudness),
            ("LoudnessChipForeBrush", ColorDevGroup.Loudness),

            ("LevelMeterTrackBackBrush", ColorDevGroup.Meter),
            ("LevelMeterTrackBorderBrush", ColorDevGroup.Meter),
            ("LevelMeterHoldBorderBrush", ColorDevGroup.Meter),
            ("LevelMeterTickBrush", ColorDevGroup.Meter),
            ("LevelMeterClipOffBrush", ColorDevGroup.Meter),
            ("LevelMeterClipOffBorderBrush", ColorDevGroup.Meter),
            ("LevelMeterClipOnBrush", ColorDevGroup.Meter),
            ("LevelMeterClipOnOverlayBrush", ColorDevGroup.Meter),
            ("SurroundHullStrokeBrush", ColorDevGroup.Meter),

            ("LevelGradFloorBrush", ColorDevGroup.Spectrum),
            ("LevelGradLowBrush", ColorDevGroup.Spectrum),
            ("LevelGradMidBrush", ColorDevGroup.Spectrum),
            ("LevelGradHighBrush", ColorDevGroup.Spectrum),
            ("LevelGradCeilBrush", ColorDevGroup.Spectrum),

            ("VectorScopeBackBrush", ColorDevGroup.VectorScope),
            ("VectorScopeTraceBrush", ColorDevGroup.VectorScope),
            ("VectorScopeGridBrush", ColorDevGroup.VectorScope),
            ("VectorScopeCorrelationBrush", ColorDevGroup.VectorScope),

            ("PlayerPlaceholderJacketTopBrush", ColorDevGroup.Player),
            ("PlayerPlaceholderJacketBottomBrush", ColorDevGroup.Player),
            ("PlayerPlaceholderJacketForeBrush", ColorDevGroup.Player),
            ("PlayerJacketReflectionBrush", ColorDevGroup.Player),
            ("PlayerFallbackWashNavyBrush", ColorDevGroup.Player),
            ("PlayerFallbackWashCyanBrush", ColorDevGroup.Player),
            ("PlayerFallbackWashWhiteBrush", ColorDevGroup.Player),
            ("PlayerGlowVeilBrush", ColorDevGroup.Player),
            ("PlayerPaneFocusLineBrush", ColorDevGroup.Player),
            ("PlayerSelectionFillBrush", ColorDevGroup.Player),
            ("PlayerHoverFillBrush", ColorDevGroup.Player),
            ("PlayerComboFillBrush", ColorDevGroup.Player),
            ("PlayerComboHoverFillBrush", ColorDevGroup.Player),
            ("PlayerComboDisabledFillBrush", ColorDevGroup.Player),
            ("PlayerComboDropFillBrush", ColorDevGroup.Player),
            ("PlayerComboDropBorderBrush", ColorDevGroup.Player),
            ("PlayerWaveFillBrush", ColorDevGroup.Player),
            ("PlayerWaveSelectionEmptyBrush", ColorDevGroup.Player),
            ("PlayerLevelMeterTrackBorderBrush", ColorDevGroup.Player),
            ("PlayerLevelMeterTickBrush", ColorDevGroup.Player),
            ("PlayerVectorScopeGridBrush", ColorDevGroup.Player),
            ("PlayerSurroundHullStrokeBrush", ColorDevGroup.Player),
            ("PlayerWaapiToggleOffBackBrush", ColorDevGroup.Player),
            ("PlayerWaapiToggleOffHoverBackBrush", ColorDevGroup.Player),
            ("PlayerWaapiToggleOnBackBrush", ColorDevGroup.Player),
            ("PlayerWaapiToggleOnHoverBackBrush", ColorDevGroup.Player),

            ("TransportBackBrush", ColorDevGroup.Transport),
            ("TransportForeBrush", ColorDevGroup.Transport),
            ("TransportDisabledForeBrush", ColorDevGroup.Transport),
            ("TransportHoverBackBrush", ColorDevGroup.Transport),
            ("TransportPressedBackBrush", ColorDevGroup.Transport),
            ("RecordLatchForeBrush", ColorDevGroup.Transport),
            ("HistoryStripBackBrush", ColorDevGroup.Transport),
            ("HistoryStripHoverBackBrush", ColorDevGroup.Transport),
            ("HistoryStripCurrentForeBrush", ColorDevGroup.Transport),
            ("HistoryStripPastForeBrush", ColorDevGroup.Transport),
            ("HistoryStripFutureForeBrush", ColorDevGroup.Transport),
            ("ActionCopyrightForeBrush", ColorDevGroup.Transport),
            ("ActionLinkForeBrush", ColorDevGroup.Transport),
            ("ActionLinkHoverForeBrush", ColorDevGroup.Transport),
            ("WaapiToggleOffBackBrush", ColorDevGroup.Transport),
            ("WaapiToggleOffHoverBackBrush", ColorDevGroup.Transport),
            ("WaapiToggleOffForeBrush", ColorDevGroup.Transport),
            ("WaapiToggleOnBackBrush", ColorDevGroup.Transport),
            ("WaapiToggleOnHoverBackBrush", ColorDevGroup.Transport),
            ("WaapiToggleOnForeBrush", ColorDevGroup.Transport),

            ("StatusBarBackBrush", ColorDevGroup.StatusBar),
            ("StatusTimecodeFillBrush", ColorDevGroup.StatusBar),
            ("WaapiBarBackBrush", ColorDevGroup.StatusBar),
            ("StatusBarTitleForeBrush", ColorDevGroup.StatusBar),
            ("StatusBarDetailForeBrush", ColorDevGroup.StatusBar),
            ("StatusBarErrorDetailForeBrush", ColorDevGroup.StatusBar),
            ("StatusBarConnectedBadgeBackBrush", ColorDevGroup.StatusBar),
            ("StatusBarDisconnectedBadgeBackBrush", ColorDevGroup.StatusBar),
            ("KeepTargetLockForeBrush", ColorDevGroup.StatusBar),
            ("KeepTargetLockHoverForeBrush", ColorDevGroup.StatusBar),
            ("KeepTargetUnlockForeBrush", ColorDevGroup.StatusBar),
            ("KeepTargetUnlockHoverForeBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonFillBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonHoverFillBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonBackBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonHoverBackBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonPressedBackBrush", ColorDevGroup.StatusBar),
            ("StatusExportButtonForeBrush", ColorDevGroup.StatusBar),

            ("WindowBackBrush", ColorDevGroup.Dialog),
            ("ColorPanelBackBrush", ColorDevGroup.Dialog),
            ("ExportButtonFillBrush", ColorDevGroup.Dialog),
            ("ExportButtonHoverFillBrush", ColorDevGroup.Dialog),
            ("ExportButtonBackBrush", ColorDevGroup.Dialog),
            ("ExportButtonHoverBackBrush", ColorDevGroup.Dialog),
            ("ExportButtonPressedBackBrush", ColorDevGroup.Dialog),
            ("ExportButtonForeBrush", ColorDevGroup.Dialog),
            ("ClearButtonFillBrush", ColorDevGroup.Dialog),
            ("ClearButtonHoverFillBrush", ColorDevGroup.Dialog),
            ("ClearButtonBackBrush", ColorDevGroup.Dialog),
            ("ClearButtonHoverBackBrush", ColorDevGroup.Dialog),
            ("ClearButtonPressedBackBrush", ColorDevGroup.Dialog),
            ("ClearButtonForeBrush", ColorDevGroup.Dialog),
        };

        for (var i = 0; i < catalog.Length; i++)
        {
            Groups[catalog[i].Key] = catalog[i].Group;
            Ranks[catalog[i].Key] = i;
        }
    }

    public static IReadOnlyCollection<string> Keys => Groups.Keys;

    public static ColorDevGroup GroupOf(string key) =>
        Groups.TryGetValue(key, out var group) ? group : ColorDevGroup.Other;

    /// <summary>プレイヤー色はライト／ダークで分けず、ダークの色を両方で使う。</summary>
    public static bool IsPlayerShared(string key) => GroupOf(key) == ColorDevGroup.Player;

    public static int Rank(string key) =>
        Ranks.TryGetValue(key, out var rank) ? rank : int.MaxValue;

    public static string GroupTitle(ColorDevGroup group) => group switch
    {
        ColorDevGroup.Shared => UiStrings.ColorDevGroupShared,
        ColorDevGroup.Overview => UiStrings.ColorDevGroupOverview,
        ColorDevGroup.Waveform => UiStrings.ColorDevGroupWaveform,
        ColorDevGroup.Loudness => UiStrings.ColorDevGroupLoudness,
        ColorDevGroup.Guides => UiStrings.ColorDevGroupGuides,
        ColorDevGroup.Selection => UiStrings.ColorDevGroupSelection,
        ColorDevGroup.SampleLoop => UiStrings.ColorDevGroupSampleLoop,
        ColorDevGroup.Region => UiStrings.ColorDevGroupRegion,
        ColorDevGroup.Marker => UiStrings.ColorDevGroupMarker,
        ColorDevGroup.Meter => UiStrings.ColorDevGroupMeter,
        ColorDevGroup.Spectrum => UiStrings.ColorDevGroupSpectrum,
        ColorDevGroup.VectorScope => UiStrings.ColorDevGroupVectorScope,
        ColorDevGroup.Player => UiStrings.ColorDevGroupPlayer,
        ColorDevGroup.Transport => UiStrings.ColorDevGroupTransport,
        ColorDevGroup.StatusBar => UiStrings.ColorDevGroupStatus,
        ColorDevGroup.Dialog => UiStrings.ColorDevGroupDialog,
        _ => UiStrings.ColorDevGroupOther,
    };

    public static string GroupTitleOf(string key) => GroupTitle(GroupOf(key));

    public static string Caption(string groupTitle, string item) =>
        UiStrings.IsJapanese ? $"{groupTitle}・{item}" : $"{groupTitle} · {item}";

    public static IEnumerable<UiColorEntry> Sort(IEnumerable<UiColorEntry> entries) =>
        entries
            .OrderBy(entry => Rank(entry.Key))
            .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase);

    public static bool Matches(string label, string key, string hex, string? query) =>
        Matches(label, key, hex, groupTitle: null, query);

    public static bool Matches(string label, string key, string hex, string? groupTitle, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        return label.Contains(q, StringComparison.OrdinalIgnoreCase)
            || key.Contains(q, StringComparison.OrdinalIgnoreCase)
            || hex.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrEmpty(groupTitle)
                && groupTitle.Contains(q, StringComparison.OrdinalIgnoreCase));
    }
}
