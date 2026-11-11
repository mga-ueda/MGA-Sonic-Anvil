using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>配色で入れ替える背景／文字。意味のあるアクセントは含めない。</summary>
internal static class UiThemePalette
{
    private static readonly HashSet<string> Themeable = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrimaryForeBrush",
        "MutedForeBrush",
        "AccentCyanBrush",
        "SurfaceBackBrush",
        "WindowBackBrush",
        "ChromeBackBrush",
        "ChromeBorderBrush",
        "ChromeMidBrush",
        "ChromeDimBrush",
        "ScrollThumbBackBrush",
        "ScrollThumbHoverBackBrush",
        "ScrollThumbPressedBackBrush",
        "ScrollThumbGripBrush",
        "ControlHoverBorderBrush",
        "DialogInputBackBrush",
        "MenuSeparatorBrush",
        "MenuHighlightBackBrush",
        "MenuDisabledForeBrush",
        "MainMenuBarBackBrush",
        "MenuListBackBrush",
        "MenuPopupBackBrush",
        "WaveformBackBrush",
        "WaveformRecordBackBrush",
        "WaveFillBrush",
        "SilenceDetectWaveformBackBrush",
        "SilenceDetectWaveFillBrush",
        "SilenceDetectSelectionBackBrush",
        "SilenceDetectSelectionWaveFillBrush",
        "WaveZeroLineBrush",
        "TimelineWellBackBrush",
        "DocumentTabIdleBackBrush",
        "DocumentTabSelectedBackBrush",
        "DocumentTabSelectedDirtyBackBrush",
        "DocumentTabSelectedForeBrush",
        "WaveformTileDividerBrush",
        "MouseGuideBrush",
        "MouseGuideOnSelectionBrush",
        "WaveSelectionBackBrush",
        "WaveSelectionWaveFillBrush",
        "WaveSelectionFillBrush",
        "LoopRangeFillBrush",
        "SampleLoopTimeLabelForeBrush",
        "RegionTimeLabelForeBrush",
        "MarkerLabelForeBrush",
        "LevelMeterTrackBackBrush",
        "LevelMeterTrackBorderBrush",
        "LevelMeterHoldBorderBrush",
        "LevelMeterTickBrush",
        "PlayerFallbackWashNavyBrush",
        "PlayerFallbackWashBlueBrush",
        "PlayerFallbackWashCyanBrush",
        "PlayerGlowVeilBrush",
        "PlayerPaneFocusLineBrush",
        "PlayerSelectionFillBrush",
        "PlayerHoverFillBrush",
        "PlayerComboFillBrush",
        "PlayerComboHoverFillBrush",
        "PlayerComboDisabledFillBrush",
        "PlayerComboDropFillBrush",
        "PlayerComboDropBorderBrush",
        "PlayerWaveFillBrush",
        "PlayerAbInBrush",
        "PlayerAbOutBrush",
        "PlayerFormatMismatchForeBrush",
        "PlayerWaveSelectionEmptyBrush",
        "PlayerLevelMeterTrackBorderBrush",
        "PlayerLevelMeterTickBrush",
        "PlayerVectorScopeGridBrush",
        "PlayerSurroundHullStrokeBrush",
        "PlayerJacketReflectionBrush",
        "DbScaleForeBrush",
        "SpectrogramScaleForeBrush",
        "SurroundHullStrokeBrush",
        "LevelMeterClipOffBrush",
        "LevelMeterClipOffBorderBrush",
        "VectorScopeBackBrush",
        "VectorScopeGridBrush",
        "ProjectBarBackBrush",
        "OverviewOutsideFillBrush",
        "TransportBackBrush",
        "TransportToolbarGroupVeilBrush",
        "TransportToolbarSectionSeparatorBrush",
        "TransportForeBrush",
        "TransportDisabledForeBrush",
        "TransportHoverBackBrush",
        "TransportPressedBackBrush",
        "HistoryStripBackBrush",
        "HistoryStripHoverBackBrush",
        "HistoryStripCurrentForeBrush",
        "HistoryStripPastForeBrush",
        "HistoryStripFutureForeBrush",
        "VstInsertSlotDropBackBrush",
        "VstInsertSlotEmptyFillBrush",
        "VstInsertSlotEmptyFocusFillBrush",
        "VstInsertSlotEmptyHoverFillBrush",
        "VstInsertSlotEmptyBackBrush",
        "VstInsertSlotEmptyHoverBackBrush",
        "VstInsertSlotEmptyPressedBackBrush",
        "VstInsertSlotEmptyForeBrush",
        "VstInsertSlotFilledFillBrush",
        "VstInsertSlotFilledFocusFillBrush",
        "VstInsertSlotFilledHoverFillBrush",
        "VstInsertSlotFilledBackBrush",
        "VstInsertSlotFilledHoverBackBrush",
        "VstInsertSlotFilledPressedBackBrush",
        "VstInsertSlotFilledForeBrush",
        "VstInsertSlotPersistFillBrush",
        "VstInsertSlotPersistFocusFillBrush",
        "VstInsertSlotPersistHoverFillBrush",
        "VstInsertSlotPersistBackBrush",
        "VstInsertSlotPersistHoverBackBrush",
        "VstInsertSlotPersistPressedBackBrush",
        "VstInsertSlotPersistForeBrush",
        "LatchToggleOffBackBrush",
        "LatchToggleOffHoverBackBrush",
        "LatchToggleOffForeBrush",
        "ActionCopyrightForeBrush",
        "ActionLinkForeBrush",
        "ActionLinkHoverForeBrush",
        "StatusBarBackBrush",
        "StatusTimecodeFillBrush",
        "WaapiBarBackBrush",
        "StatusBarTitleForeBrush",
        "StatusBarDetailForeBrush",
        "KeepTargetLockForeBrush",
        "KeepTargetLockHoverForeBrush",
        "KeepTargetUnlockForeBrush",
        "KeepTargetUnlockHoverForeBrush",
        "ColorPanelBackBrush",
        "AcrylicDialogTintBrush",
        "AcrylicToastTintBrush",
        "ExportButtonFillBrush",
        "ExportButtonFocusFillBrush",
        "ExportButtonHoverFillBrush",
        "ExportButtonBackBrush",
        "ExportButtonHoverBackBrush",
        "ExportButtonPressedBackBrush",
        "ExportButtonForeBrush",
        "ClearButtonFillBrush",
        "ClearButtonFocusFillBrush",
        "ClearButtonHoverFillBrush",
        "ClearButtonBackBrush",
        "ClearButtonHoverBackBrush",
        "ClearButtonPressedBackBrush",
        "ClearButtonForeBrush",
        "StatusExportButtonFillBrush",
        "StatusExportButtonHoverFillBrush",
        "StatusExportButtonBackBrush",
        "StatusExportButtonHoverBackBrush",
        "StatusExportButtonPressedBackBrush",
        "StatusExportButtonForeBrush",
        "LatchToggleOnBackBrush",
        "LatchToggleOnHoverBackBrush",
        "StatusBarConnectedBadgeBackBrush",
        "StatusBarDisconnectedBadgeBackBrush",
        "StatusBarErrorDetailForeBrush",
        "RegionWaveFillAnacrusisBrush",
        "RegionWaveFillLoopBrush",
        "RegionWaveFillExitBrush",
        "RegionWaveFillExcludedBrush",
        "LoudnessChipForeBrush",
        "LoudnessGridBrush",
        "LoudnessTargetLineBrush",
    };

    private static readonly Dictionary<string, Color> Dark = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PrimaryForeBrush"] = Rgb(0xEB, 0xEB, 0xEB),
        ["MutedForeBrush"] = Rgb(0x96, 0x96, 0x96),
        ["StatusTimecodeFillBrush"] = Rgb(0x24, 0x24, 0x28),
        ["PlayerFallbackWashNavyBrush"] = Rgb(0x0B, 0x1B, 0x49),
        ["PlayerFallbackWashBlueBrush"] = Rgb(0x1A, 0x56, 0xA0),
        ["PlayerFallbackWashCyanBrush"] = Rgb(0x0B, 0x68, 0x95),
        ["PlayerGlowVeilBrush"] = Argb(0x6B, 0x1E, 0x1E, 0x1E),
        ["PlayerPaneFocusLineBrush"] = Argb(0x14, 0xFF, 0xFF, 0xFF),
        ["PlayerSelectionFillBrush"] = Argb(0x28, 0x00, 0xF5, 0xFF),
        ["PlayerHoverFillBrush"] = Argb(0x98, 0x68, 0x68, 0x70),
        ["PlayerComboFillBrush"] = Argb(0x2E, 0xEB, 0xEB, 0xEB),
        ["PlayerComboHoverFillBrush"] = Argb(0x52, 0xEB, 0xEB, 0xEB),
        ["PlayerComboDisabledFillBrush"] = Argb(0x14, 0xEB, 0xEB, 0xEB),
        ["PlayerComboDropFillBrush"] = Argb(0xEC, 0x22, 0x23, 0x2E),
        ["PlayerComboDropBorderBrush"] = Argb(0xA8, 0x68, 0x68, 0x70),
        ["WaveSelectionBackBrush"] = Rgb(0xC6, 0xD9, 0xFF),
        ["WaveSelectionWaveFillBrush"] = Rgb(0x26, 0x26, 0x26),
        ["WaveSelectionFillBrush"] = Argb(0x38, 0xFF, 0xFF, 0xFF),
        ["DocumentTabIdleBackBrush"] = Argb(0x18, 0xFF, 0xFF, 0xFF),
        ["DocumentTabSelectedBackBrush"] = Argb(0x30, 0xFF, 0xFF, 0xFF),
        ["DocumentTabSelectedDirtyBackBrush"] = Argb(0x40, 0xFF, 0x00, 0x2B),
        ["DocumentTabSelectedForeBrush"] = Rgb(0xFF, 0xFF, 0xFF),
        ["AccentCyanBrush"] = Rgb(0x00, 0xF5, 0xFF),
        ["SilenceDetectWaveformBackBrush"] = Rgb(0x26, 0x26, 0x26),
        ["SilenceDetectWaveFillBrush"] = Rgb(0x91, 0x9F, 0xBB),
        ["SilenceDetectSelectionBackBrush"] = Rgb(0x91, 0x9F, 0xBB),
        ["SilenceDetectSelectionWaveFillBrush"] = Rgb(0x26, 0x26, 0x26),
        ["PlayerWaveFillBrush"] = Argb(0x94, 0xC6, 0xD9, 0xFF),
        ["PlayerAbInBrush"] = Rgb(0x32, 0xCD, 0x32),
        ["PlayerAbOutBrush"] = Rgb(0xFF, 0x00, 0x00),
        ["PlayerFormatMismatchForeBrush"] = Rgb(0xFF, 0x8A, 0x1A),
        ["PlayerWaveSelectionEmptyBrush"] = Rgb(0xC6, 0xD9, 0xFF),
        ["PlayerLevelMeterTrackBorderBrush"] = Rgb(0x5A, 0x60, 0x5A),
        ["PlayerLevelMeterTickBrush"] = Argb(0x60, 0xFF, 0xFF, 0xFF),
        ["PlayerVectorScopeGridBrush"] = Rgb(0x71, 0x71, 0x74),
        ["PlayerSurroundHullStrokeBrush"] = Rgb(0x88, 0x88, 0x8C),
        ["PlayerJacketReflectionBrush"] = Argb(0x4E, 0xFF, 0xFF, 0xFF),
        ["SpectrogramScaleForeBrush"] = Rgb(0xEB, 0xEB, 0xEB),
        ["SpectrogramSelectionFillBrush"] = Argb(0x38, 0xFF, 0xFF, 0xFF),
        ["SpectrogramGridBrush"] = Argb(0x1A, 0xFF, 0xFF, 0xFF),
        ["SpectrogramScaleEdgeBrush"] = Rgb(0x00, 0x00, 0x00),
        ["SpectrogramBoostThumbFillBrush"] = Rgb(0xF4, 0xF4, 0xF4),
        ["SpectrogramBoostThumbStrokeBrush"] = Rgb(0x2A, 0x2A, 0x2A),
        ["SpectrogramBoostThumbHoverFillBrush"] = Rgb(0xFF, 0xFF, 0xFF),
        ["SpectrogramBoostThumbHoverStrokeBrush"] = Rgb(0x11, 0x11, 0x11),
        ["LoudnessSafeBrush"] = Rgb(0x00, 0xFF, 0xFF),
        ["LoudnessCautionBrush"] = Rgb(0xD8, 0x98, 0x20),
        ["LoudnessDangerBrush"] = Rgb(0xB8, 0x38, 0x40),
        ["LoudnessWaveFillBrush"] = Rgb(0x6F, 0x6F, 0x78),
        ["LoudnessChipForeBrush"] = Rgb(0xEB, 0xEB, 0xEB),
        ["LoudnessGridBrush"] = Argb(0x24, 0xFF, 0xFF, 0xFF),
        ["LoudnessTargetLineBrush"] = Argb(0xC8, 0xFF, 0xFF, 0xFF),
        ["LevelMeterClipOnBrush"] = Rgb(0xFF, 0x00, 0x00),
        ["LevelMeterClipOnOverlayBrush"] = Argb(0x50, 0xFF, 0x00, 0x00),
        ["SampleLoopGripBrush"] = Rgb(0x8E, 0xC4, 0xDC),
        ["TileSearchVeilBrush"] = Argb(0x8C, 0x00, 0x00, 0x00),
        ["ChannelLabelOnTintForeBrush"] = Rgb(0xFF, 0xFF, 0xFF),
        ["RecordLatchForeBrush"] = Rgb(0xE2, 0x4B, 0x4A),
        ["TransportToolbarGroupVeilBrush"] = Argb(0x10, 0xED, 0xED, 0xFF),
        ["TransportToolbarSectionSeparatorBrush"] = Argb(0x40, 0xEB, 0xEB, 0xEB),
    };

    public static IReadOnlyCollection<string> ThemeableKeys => Themeable;

    public static bool IsThemeable(string key) => Themeable.Contains(key);

    public static void Apply(UiTheme theme)
    {
        foreach (var key in Themeable)
        {
            UiColors.Set(key, ColorFor(theme, key));
        }
    }

    public static Color ColorFor(UiTheme theme, string key)
    {
        _ = theme;
        if (SharedPlayerColor(key, out var shared))
        {
            return shared;
        }

        if (Dark.TryGetValue(key, out var dark))
        {
            return dark;
        }

        return UiColors.Default(key);
    }

    internal static bool TryMap(UiTheme theme, string key, out Color color)
    {
        _ = theme;
        if (SharedPlayerColor(key, out color))
        {
            return true;
        }

        return Dark.TryGetValue(key, out color);
    }

    /// <summary>プレイヤー色はテーマに関係なくダークの値。</summary>
    private static bool SharedPlayerColor(string key, out Color color)
    {
        color = default;
        return ColorDevCatalog.IsPlayerShared(key) && Dark.TryGetValue(key, out color);
    }

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);
}
