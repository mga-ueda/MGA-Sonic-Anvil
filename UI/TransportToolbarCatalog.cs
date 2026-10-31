using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// メニュー下ツールバーに載るトランスポートコマンドの一覧と、設定からの表示判定。
/// </summary>
internal static class TransportToolbarCatalog
{
    internal readonly record struct Item(
        TransportCommand Command,
        TransportIcon Icon,
        Func<string> GroupTitle,
        Func<string> Label,
        Func<string> Tooltip);

    /// <summary>ツールバーに出しうる全コマンド（既定はすべて表示）。</summary>
    public static IReadOnlyList<Item> All { get; } =
    [
        new(TransportCommand.TogglePlayback, TransportIcon.PlayPause,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarPlay, () => UiStrings.TooltipPlay),
        new(TransportCommand.Record, TransportIcon.Record,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarRecord, () => UiStrings.TooltipRecord),

        new(TransportCommand.FadeIn, TransportIcon.FadeIn,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeIn, () => UiStrings.TooltipFadeIn),
        new(TransportCommand.FadeOut, TransportIcon.FadeOut,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeOut, () => UiStrings.TooltipFadeOut),
        new(TransportCommand.FadeAround, TransportIcon.FadeAround,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeAround, () => UiStrings.TooltipFadeAround),
        new(TransportCommand.Normalize, TransportIcon.Normalize,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarNormalize, () => UiStrings.TooltipNormalize),
        new(TransportCommand.Volume, TransportIcon.Volume,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarVolume, () => UiStrings.TooltipVolume),
        new(TransportCommand.Pitch, TransportIcon.Pitch,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarPitch, () => UiStrings.TooltipPitch),
        new(TransportCommand.TimeStretch, TransportIcon.TimeStretch,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarTimeStretch, () => UiStrings.TooltipTimeStretch),
        new(TransportCommand.Reverse, TransportIcon.Reverse,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarReverse, () => UiStrings.TooltipReverse),
        new(TransportCommand.Delete, TransportIcon.Delete,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarDelete, () => UiStrings.TooltipDelete),

        new(TransportCommand.NewDocument, TransportIcon.NewDocument,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarNew, () => UiStrings.TooltipNew),
        new(TransportCommand.Open, TransportIcon.Folder,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarOpen, () => UiStrings.TooltipOpen),
        new(TransportCommand.Save, TransportIcon.Save,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarSave, () => UiStrings.TooltipSave),
        new(TransportCommand.SaveAs, TransportIcon.SaveAs,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarSaveAs, () => UiStrings.TooltipSaveAs),
        new(TransportCommand.SaveMp3, TransportIcon.SaveMp3,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarSaveMp3, () => UiStrings.TooltipSaveMp3),

        new(TransportCommand.AddMarker, TransportIcon.AddMarker,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarAddMarker, () => UiStrings.TooltipAddMarker),
        new(TransportCommand.SetLoop, TransportIcon.SetLoop,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarSetLoop, () => UiStrings.TooltipSetLoop),
        new(TransportCommand.SetRegion, TransportIcon.SetRegion,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarSetRegion, () => UiStrings.TooltipSetRegion),
        new(TransportCommand.ToggleRangeClick, TransportIcon.RangeClick,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarRangeClick, () => UiStrings.TooltipRangeClick),

        new(TransportCommand.ToggleSpectrogram, TransportIcon.Analysis,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarSpectrogram, () => UiStrings.TooltipSpectrogramView),
        new(TransportCommand.ToggleLoudnessView, TransportIcon.Loudness,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarLoudness, () => UiStrings.TooltipLoudnessView),
        new(TransportCommand.CenterPlayhead, TransportIcon.Center,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarCenter, () => UiStrings.TooltipCenterPlayhead),
        new(TransportCommand.History, TransportIcon.History,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarHistory, () => UiStrings.TooltipHistory),
        new(TransportCommand.ToggleLibraryMaximize, TransportIcon.PlayerMode,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarLibraryMaximize, () => UiStrings.TooltipLibraryMaximize),
        new(TransportCommand.ToggleAnalyzerMaximize, TransportIcon.AnalyzerMaximize,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarAnalyzerMaximize, () => UiStrings.TooltipAnalyzerMaximize),
        new(TransportCommand.OpenColorPanel, TransportIcon.ColorPalette,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarColorPanel, () => UiStrings.TooltipColorPanel),

        new(TransportCommand.OpenSettings, TransportIcon.Settings,
            () => UiStrings.LabelOtherGroup, () => UiStrings.LabelToolbarSettings, () => UiStrings.TooltipSettings),
        new(TransportCommand.OpenManual, TransportIcon.Help,
            () => UiStrings.LabelOtherGroup, () => UiStrings.LabelToolbarManual, () => UiStrings.TooltipManualHelp),
    ];

    public static IReadOnlyList<TransportCommand> AllCommands { get; } =
        All.Select(item => item.Command).ToArray();

    public static bool IsCommandVisible(AppSettings settings, TransportCommand command) =>
        IsCommandVisible(settings.HiddenTransportToolbarCommands, command);

    public static bool IsCommandVisible(IEnumerable<string>? hidden, TransportCommand command)
    {
        if (hidden is null)
        {
            return true;
        }

        var name = command.ToString();
        foreach (var entry in hidden)
        {
            if (string.Equals(entry, name, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static string[] NormalizeHidden(IEnumerable<string>? hidden)
    {
        if (hidden is null)
        {
            return [];
        }

        var known = new HashSet<string>(
            AllCommands.Select(command => command.ToString()),
            StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in hidden)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            var trimmed = entry.Trim();
            if (!known.Contains(trimmed) || !seen.Add(trimmed))
            {
                continue;
            }

            // 保存は enum 名に揃える。
            var match = AllCommands.First(command =>
                string.Equals(command.ToString(), trimmed, StringComparison.OrdinalIgnoreCase));
            result.Add(match.ToString());
        }

        return [.. result];
    }

    public static string[] HiddenFromVisible(IEnumerable<TransportCommand> visible)
    {
        var shown = new HashSet<TransportCommand>(visible);
        return AllCommands
            .Where(command => !shown.Contains(command))
            .Select(command => command.ToString())
            .ToArray();
    }
}
