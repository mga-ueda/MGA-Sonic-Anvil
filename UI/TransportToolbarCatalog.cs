using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ツールバーに載るトランスポートコマンドの一覧と、設定からの表示判定。
/// グループ・並び・グループ内の区切りは、対応するメニュー項目の位置（<see cref="WaveformContextMenuBuilder"/>）から決める。
/// </summary>
internal static class TransportToolbarCatalog
{
    /// <summary>レイアウト配列内のセパレータ（メニューのカテゴリ境界）。</summary>
    public const string SeparatorToken = "|";

    /// <summary>グループ内の区切り（メニューのセパレータ／サブメニュー境界）。</summary>
    public const string SectionSeparatorToken = "¦";

    internal readonly record struct Item(
        TransportCommand Command,
        TransportIcon Icon,
        WaveMenuCommand MenuCommand,
        Func<string> Label,
        Func<string> Tooltip);

    internal readonly record struct IconVariant(
        TransportIcon Icon,
        Func<string> State,
        bool Playing = false,
        bool Latched = false)
    {
        public string StateText => State();
    }

    /// <summary>ツールバーに出しうる全コマンド。</summary>
    public static IReadOnlyList<Item> All { get; } =
    [
        new(TransportCommand.TogglePlayback, TransportIcon.PlayPause,
            WaveMenuCommand.TogglePlayback, () => UiStrings.LabelToolbarPlay, () => UiStrings.TooltipPlay),
        new(TransportCommand.Record, TransportIcon.Record,
            WaveMenuCommand.Record, () => UiStrings.LabelToolbarRecord, () => UiStrings.TooltipRecord),
        new(TransportCommand.GoToStart, TransportIcon.GoToStart,
            WaveMenuCommand.GoStart, () => UiStrings.LabelToolbarGoToStart, () => UiStrings.TooltipGoToStart),
        new(TransportCommand.PreviousMarker, TransportIcon.PreviousMarker,
            WaveMenuCommand.PrevMarker, () => UiStrings.LabelToolbarPreviousMarker, () => UiStrings.TooltipPreviousMarker),
        new(TransportCommand.NextMarker, TransportIcon.NextMarker,
            WaveMenuCommand.NextMarker, () => UiStrings.LabelToolbarNextMarker, () => UiStrings.TooltipNextMarker),
        new(TransportCommand.GoToEnd, TransportIcon.GoToEnd,
            WaveMenuCommand.GoEnd, () => UiStrings.LabelToolbarGoToEnd, () => UiStrings.TooltipGoToEnd),

        new(TransportCommand.JumpToTime, TransportIcon.JumpToTime,
            WaveMenuCommand.FocusTime, () => UiStrings.LabelToolbarJumpToTime, () => UiStrings.TooltipJumpToTime),
        new(TransportCommand.SeekViewLeft, TransportIcon.SeekViewLeft,
            WaveMenuCommand.ViewLeft, () => UiStrings.LabelToolbarSeekViewLeft, () => UiStrings.TooltipSeekViewLeft),
        new(TransportCommand.SeekViewRight, TransportIcon.SeekViewRight,
            WaveMenuCommand.ViewRight, () => UiStrings.LabelToolbarSeekViewRight, () => UiStrings.TooltipSeekViewRight),
        new(TransportCommand.LoopPlay, TransportIcon.LoopPlay,
            WaveMenuCommand.LoopPlay, () => UiStrings.LabelToolbarLoopPlay, () => UiStrings.TooltipLoopPlay),
        new(TransportCommand.Restart, TransportIcon.Restart,
            WaveMenuCommand.Restart, () => UiStrings.LabelToolbarRestart, () => UiStrings.TooltipRestart),
        new(TransportCommand.Preroll, TransportIcon.Preroll,
            WaveMenuCommand.Preroll, () => UiStrings.LabelToolbarPreroll, () => UiStrings.TooltipPreroll),
        new(TransportCommand.RangeClick, TransportIcon.RangeClick,
            WaveMenuCommand.RangeClick, () => UiStrings.LabelToolbarMarkerClick, () => UiStrings.TooltipMarkerClick),
        new(TransportCommand.PreviousTrack, TransportIcon.PreviousTrack,
            WaveMenuCommand.PreviousTrack, () => UiStrings.LabelToolbarPreviousTrack, () => UiStrings.TooltipPreviousTrack),
        new(TransportCommand.NextTrack, TransportIcon.NextTrack,
            WaveMenuCommand.NextTrack, () => UiStrings.LabelToolbarNextTrack, () => UiStrings.TooltipNextTrack),

        new(TransportCommand.Undo, TransportIcon.Undo,
            WaveMenuCommand.Undo, () => UiStrings.LabelToolbarUndo, () => UiStrings.TooltipUndo),
        new(TransportCommand.Redo, TransportIcon.Redo,
            WaveMenuCommand.Redo, () => UiStrings.LabelToolbarRedo, () => UiStrings.TooltipRedo),
        new(TransportCommand.Cut, TransportIcon.Cut,
            WaveMenuCommand.Cut, () => UiStrings.LabelToolbarCut, () => UiStrings.TooltipCut),
        new(TransportCommand.Copy, TransportIcon.Copy,
            WaveMenuCommand.Copy, () => UiStrings.LabelToolbarCopy, () => UiStrings.TooltipCopy),
        new(TransportCommand.Paste, TransportIcon.Paste,
            WaveMenuCommand.Paste, () => UiStrings.LabelToolbarPaste, () => UiStrings.TooltipPaste),
        new(TransportCommand.Delete, TransportIcon.Delete,
            WaveMenuCommand.Delete, () => UiStrings.LabelToolbarDelete, () => UiStrings.TooltipDelete),
        new(TransportCommand.Trim, TransportIcon.Trim,
            WaveMenuCommand.Trim, () => UiStrings.LabelToolbarTrim, () => UiStrings.TooltipTrim),
        new(TransportCommand.SilenceSelection, TransportIcon.SilenceSelection,
            WaveMenuCommand.SilenceSelection, () => UiStrings.LabelToolbarSilenceSelection, () => UiStrings.TooltipSilenceSelection),
        new(TransportCommand.InsertSilence, TransportIcon.InsertSilence,
            WaveMenuCommand.InsertSilence, () => UiStrings.LabelToolbarInsertSilence, () => UiStrings.TooltipInsertSilence),
        new(TransportCommand.DetectSilence, TransportIcon.DetectSilence,
            WaveMenuCommand.DetectSilence, () => UiStrings.LabelToolbarDetectSilence, () => UiStrings.TooltipDetectSilence),
        new(TransportCommand.SelectAll, TransportIcon.SelectAll,
            WaveMenuCommand.SelectAll, () => UiStrings.LabelToolbarSelectAll, () => UiStrings.TooltipSelectAll),
        new(TransportCommand.ClearSelection, TransportIcon.ClearSelection,
            WaveMenuCommand.ClearSelection, () => UiStrings.LabelToolbarClearSelection, () => UiStrings.TooltipClearSelection),
        new(TransportCommand.SelectToStart, TransportIcon.SelectToStart,
            WaveMenuCommand.SelectToStart, () => UiStrings.LabelToolbarSelectToStart, () => UiStrings.TooltipSelectToStart),
        new(TransportCommand.SelectToEnd, TransportIcon.SelectToEnd,
            WaveMenuCommand.SelectToEnd, () => UiStrings.LabelToolbarSelectToEnd, () => UiStrings.TooltipSelectToEnd),
        new(TransportCommand.CopySelectionTime, TransportIcon.CopySelectionTime,
            WaveMenuCommand.CopySelectionTime, () => UiStrings.LabelToolbarCopySelectionTime, () => UiStrings.TooltipCopySelectionTime),
        new(TransportCommand.FadeIn, TransportIcon.FadeIn,
            WaveMenuCommand.FadeIn, () => UiStrings.LabelToolbarFadeIn, () => UiStrings.TooltipFadeIn),
        new(TransportCommand.FadeOut, TransportIcon.FadeOut,
            WaveMenuCommand.FadeOut, () => UiStrings.LabelToolbarFadeOut, () => UiStrings.TooltipFadeOut),
        new(TransportCommand.FadeAround, TransportIcon.FadeAround,
            WaveMenuCommand.FadeAround, () => UiStrings.LabelToolbarFadeAround, () => UiStrings.TooltipFadeAround),
        new(TransportCommand.CrossfadeEdit, TransportIcon.CrossfadeEdit,
            WaveMenuCommand.CrossfadeEdit, () => UiStrings.LabelToolbarCrossfadeEdit, () => UiStrings.TooltipCrossfadeEdit),
        new(TransportCommand.Normalize, TransportIcon.Normalize,
            WaveMenuCommand.Normalize, () => UiStrings.LabelToolbarNormalize, () => UiStrings.TooltipNormalize),
        new(TransportCommand.NormalizePerRegion, TransportIcon.NormalizePerRegion,
            WaveMenuCommand.NormalizePerRegion, () => UiStrings.LabelToolbarNormalizePerRegion, () => UiStrings.TooltipNormalizePerRegion),
        new(TransportCommand.Volume, TransportIcon.Volume,
            WaveMenuCommand.Volume, () => UiStrings.LabelToolbarVolume, () => UiStrings.TooltipVolume),
        new(TransportCommand.Pitch, TransportIcon.Pitch,
            WaveMenuCommand.Pitch, () => UiStrings.LabelToolbarPitch, () => UiStrings.TooltipPitch),
        new(TransportCommand.TimeStretch, TransportIcon.TimeStretch,
            WaveMenuCommand.TimeStretch, () => UiStrings.LabelToolbarTimeStretch, () => UiStrings.TooltipTimeStretch),
        new(TransportCommand.Reverse, TransportIcon.Reverse,
            WaveMenuCommand.Reverse, () => UiStrings.LabelToolbarReverse, () => UiStrings.TooltipReverse),
        new(TransportCommand.Bounce, TransportIcon.Bounce,
            WaveMenuCommand.Bounce, () => UiStrings.LabelToolbarBounce, () => UiStrings.TooltipBounce),

        new(TransportCommand.NewDocument, TransportIcon.NewDocument,
            WaveMenuCommand.NewDocument, () => UiStrings.LabelToolbarNew, () => UiStrings.TooltipNew),
        new(TransportCommand.Open, TransportIcon.Folder,
            WaveMenuCommand.Open, () => UiStrings.LabelToolbarOpen, () => UiStrings.TooltipOpen),
        new(TransportCommand.Save, TransportIcon.Save,
            WaveMenuCommand.Save, () => UiStrings.LabelToolbarSave, () => UiStrings.TooltipSave),
        new(TransportCommand.SaveAs, TransportIcon.SaveAs,
            WaveMenuCommand.SaveAs, () => UiStrings.LabelToolbarSaveAs, () => UiStrings.TooltipSaveAs),
        new(TransportCommand.SaveMp3, TransportIcon.SaveMp3,
            WaveMenuCommand.ExportMp3, () => UiStrings.LabelToolbarSaveMp3, () => UiStrings.TooltipSaveMp3),
        new(TransportCommand.ExportWave, TransportIcon.ExportWave,
            WaveMenuCommand.ExportWave, () => UiStrings.LabelToolbarExportWave, () => UiStrings.TooltipExportWave),
        new(TransportCommand.ExportByMarkers, TransportIcon.ExportByMarkers,
            WaveMenuCommand.ExportByMarkers, () => UiStrings.LabelToolbarExportByMarkers, () => UiStrings.TooltipExportByMarkers),
        new(TransportCommand.ExportByRegions, TransportIcon.ExportByRegions,
            WaveMenuCommand.ExportByRegions, () => UiStrings.LabelToolbarExportByRegions, () => UiStrings.TooltipExportByRegions),
        new(TransportCommand.ExportByChannels, TransportIcon.ExportByChannels,
            WaveMenuCommand.ExportByChannels, () => UiStrings.LabelToolbarExportByChannels, () => UiStrings.TooltipExportByChannels),
        new(TransportCommand.MergeTabs, TransportIcon.MergeTabs,
            WaveMenuCommand.MergeTabs, () => UiStrings.LabelToolbarMergeTabs, () => UiStrings.TooltipMergeTabs),
        new(TransportCommand.CopyFileName, TransportIcon.CopyFileName,
            WaveMenuCommand.CopyFileName, () => UiStrings.LabelToolbarCopyFileName, () => UiStrings.TooltipCopyFileName),
        new(TransportCommand.CopyFilePath, TransportIcon.CopyFilePath,
            WaveMenuCommand.CopyFilePath, () => UiStrings.LabelToolbarCopyFilePath, () => UiStrings.TooltipCopyFilePath),
        new(TransportCommand.CopyAllTabTimes, TransportIcon.CopyAllTabTimes,
            WaveMenuCommand.CopyAllTabTimes, () => UiStrings.LabelToolbarCopyAllTabTimes, () => UiStrings.TooltipCopyAllTabTimes),
        new(TransportCommand.CloseTab, TransportIcon.CloseTab,
            WaveMenuCommand.CloseTab, () => UiStrings.LabelToolbarCloseTab, () => UiStrings.TooltipCloseTab),

        new(TransportCommand.AddMarker, TransportIcon.AddMarker,
            WaveMenuCommand.AddMarker, () => UiStrings.LabelToolbarAddMarker, () => UiStrings.TooltipAddMarker),
        new(TransportCommand.SetLoop, TransportIcon.SetLoop,
            WaveMenuCommand.SetLoop, () => UiStrings.LabelToolbarSetLoop, () => UiStrings.TooltipSetLoop),
        new(TransportCommand.SetRegion, TransportIcon.SetRegion,
            WaveMenuCommand.SetRegion, () => UiStrings.LabelToolbarSetRegion, () => UiStrings.TooltipSetRegion),
        new(TransportCommand.DeleteMarkers, TransportIcon.DeleteMarkers,
            WaveMenuCommand.DeleteMarkers, () => UiStrings.LabelToolbarDeleteMarkers, () => UiStrings.TooltipDeleteMarkers),
        new(TransportCommand.DeleteAllMarkers, TransportIcon.DeleteAllMarkers,
            WaveMenuCommand.DeleteAllMarkers, () => UiStrings.LabelToolbarDeleteAllMarkers, () => UiStrings.TooltipDeleteAllMarkers),
        new(TransportCommand.DeleteRegions, TransportIcon.DeleteRegions,
            WaveMenuCommand.DeleteRegions, () => UiStrings.LabelToolbarDeleteRegions, () => UiStrings.TooltipDeleteRegions),
        new(TransportCommand.DeleteAllRegions, TransportIcon.DeleteAllRegions,
            WaveMenuCommand.DeleteAllRegions, () => UiStrings.LabelToolbarDeleteAllRegions, () => UiStrings.TooltipDeleteAllRegions),
        // 最前面・タイル・WAAPI 等のメニュー Check トグルは非掲載

        new(TransportCommand.SampleRate, TransportIcon.SampleRate,
            WaveMenuCommand.SampleRate, () => UiStrings.LabelToolbarSampleRate, () => UiStrings.TooltipSampleRate),
        new(TransportCommand.BitDepth, TransportIcon.BitDepth,
            WaveMenuCommand.BitDepth, () => UiStrings.LabelToolbarBitDepth, () => UiStrings.TooltipBitDepth),
        new(TransportCommand.Channels, TransportIcon.Channels,
            WaveMenuCommand.Channels, () => UiStrings.LabelToolbarChannels, () => UiStrings.TooltipChannels),

        new(TransportCommand.ToggleSpectrogram, TransportIcon.Analysis,
            WaveMenuCommand.ViewSpectrogram, () => UiStrings.LabelToolbarSpectrogram, () => UiStrings.TooltipSpectrogramView),
        new(TransportCommand.ToggleLoudnessView, TransportIcon.Loudness,
            WaveMenuCommand.ViewLoudness, () => UiStrings.LabelToolbarLoudness, () => UiStrings.TooltipLoudnessView),
        new(TransportCommand.CenterPlayhead, TransportIcon.Center,
            WaveMenuCommand.CenterPlayhead, () => UiStrings.LabelToolbarCenter, () => UiStrings.TooltipCenterPlayhead),
        new(TransportCommand.History, TransportIcon.History,
            WaveMenuCommand.History, () => UiStrings.LabelToolbarHistory, () => UiStrings.TooltipHistory),
        new(TransportCommand.ToggleMinimalChrome, TransportIcon.MinimalChrome,
            WaveMenuCommand.MinimalChrome, () => UiStrings.LabelToolbarMinimalChrome, () => UiStrings.TooltipMinimalChrome),
        new(TransportCommand.ToggleLibraryMaximize, TransportIcon.PlayerMode,
            WaveMenuCommand.MaximizeLibrary, () => UiStrings.LabelToolbarLibraryMaximize, () => UiStrings.TooltipLibraryMaximize),
        new(TransportCommand.MaximizeWaveform, TransportIcon.WaveformMaximize,
            WaveMenuCommand.MaximizeWaveform, () => UiStrings.LabelToolbarWaveformMaximize, () => UiStrings.TooltipWaveformMaximize),
        new(TransportCommand.ToggleAnalyzerMaximize, TransportIcon.AnalyzerMaximize,
            WaveMenuCommand.MaximizeAnalyzers, () => UiStrings.LabelToolbarAnalyzerMaximize, () => UiStrings.TooltipAnalyzerMaximize),
        new(TransportCommand.OpenColorPanel, TransportIcon.ColorPalette,
            WaveMenuCommand.ColorPanel, () => UiStrings.LabelToolbarColorPanel, () => UiStrings.TooltipColorPanel),

        new(TransportCommand.OpenSettings, TransportIcon.Settings,
            WaveMenuCommand.Settings, () => UiStrings.LabelToolbarSettings, () => UiStrings.TooltipSettings),
        new(TransportCommand.OpenManual, TransportIcon.Help,
            WaveMenuCommand.Manual, () => UiStrings.LabelToolbarManual, () => UiStrings.TooltipManualHelp),
    ];

    public static IReadOnlyList<TransportCommand> AllCommands { get; } =
        All.Select(item => item.Command).ToArray();

    /// <summary>
    /// メニュー上の位置。Group は上部メニューのカテゴリ、Section はカテゴリ内でセパレータ／サブメニューに区切られたかたまり。
    /// </summary>
    internal readonly record struct MenuPlacement(int Group, int Section, int Order);

    private static readonly Lazy<MenuLayout> Menu = new(BuildMenuLayout);

    private sealed record MenuLayout(
        IReadOnlyDictionary<WaveMenuCommand, MenuPlacement> Placements,
        IReadOnlyList<Item> Ordered);

    /// <summary>メニュー順（再生カテゴリのみ先頭）に並べた全コマンド。</summary>
    public static IReadOnlyList<Item> Ordered => Menu.Value.Ordered;

    /// <summary>メニューに無いコマンドは null（テストで検出する）。</summary>
    public static MenuPlacement? PlacementOf(Item item) =>
        Menu.Value.Placements.TryGetValue(item.MenuCommand, out var placement) ? placement : null;

    /// <summary>グループ名＝メニューのカテゴリ見出し（表示言語に追従）。</summary>
    public static IReadOnlyList<(Item Item, string GroupTitle)> OrderedWithGroupTitles()
    {
        var headers = MenuTree().OfType<WaveMenuItemEntry>()
            .Select(entry => UiStrings.WithoutAccessKey(entry.Header))
            .ToArray();
        return Ordered
            .Select(item => (item, PlacementOf(item) is { } placement && placement.Group < headers.Length
                ? headers[placement.Group]
                : UiStrings.WithoutAccessKey(UiStrings.WaveMenuCatOther)))
            .ToArray();
    }

    private static IReadOnlyList<WaveMenuEntry> MenuTree() =>
        WaveformContextMenuBuilder.Build(new WaveformContextMenuModel());

    private static MenuLayout BuildMenuLayout()
    {
        var placements = new Dictionary<WaveMenuCommand, MenuPlacement>();
        var order = 0;
        var tree = MenuTree();
        for (var group = 0; group < tree.Count; group++)
        {
            if (tree[group] is not WaveMenuItemEntry { Children: { } children })
            {
                continue;
            }

            var section = 0;
            Walk(children, group, ref section, ref order, placements);
        }

        var playbackGroup = placements.TryGetValue(WaveMenuCommand.TogglePlayback, out var play) ? play.Group : -1;
        var ordered = All
            .Select((item, index) => (item, index))
            .OrderBy(pair => SortKey(pair.item, placements, playbackGroup, pair.index))
            .Select(pair => pair.item)
            .ToArray();
        return new MenuLayout(placements, ordered);
    }

    private static void Walk(
        IReadOnlyList<WaveMenuEntry> entries,
        int group,
        ref int section,
        ref int order,
        Dictionary<WaveMenuCommand, MenuPlacement> placements)
    {
        foreach (var entry in entries)
        {
            switch (entry)
            {
                case WaveMenuSeparatorEntry:
                    section++;
                    break;
                case WaveMenuItemEntry { Children.Count: > 0 } sub:
                    section++;
                    Walk(sub.Children, group, ref section, ref order, placements);
                    section++;
                    break;
                case WaveMenuItemEntry { Command: { } command }:
                    placements.TryAdd(command, new MenuPlacement(group, section, order++));
                    break;
            }
        }
    }

    private static (int Group, int Order, int CatalogIndex) SortKey(
        Item item,
        IReadOnlyDictionary<WaveMenuCommand, MenuPlacement> placements,
        int playbackGroup,
        int catalogIndex)
    {
        if (!placements.TryGetValue(item.MenuCommand, out var placement))
        {
            return (int.MaxValue, int.MaxValue, catalogIndex);
        }

        return (GroupRank(placement.Group, playbackGroup), placement.Order, catalogIndex);
    }

    /// <summary>再生カテゴリだけツールバー先頭へ。他はメニューのカテゴリ順。</summary>
    private static int GroupRank(int group, int playbackGroup) =>
        group == playbackGroup ? -1 : group;

    private static readonly Dictionary<string, Item> ItemsByName =
        All.ToDictionary(
            item => item.Command.ToString(),
            item => item,
            StringComparer.OrdinalIgnoreCase);

    /// <summary>機能説明。モード限定のときはその旨を末尾に付ける。</summary>
    public static string Describe(Item item)
    {
        var text = item.Tooltip();
        var note = ModeLimitationNote(item.Command);
        return note is null ? text : $"{text}  {note}";
    }

    /// <summary>
    /// 状態で絵柄が変わるコマンドの全アイコン（1 種類だけなら空）。
    /// TransportBar で Icon／IsPlaying／IsLatched を切り替えるコマンドを増やしたら、ここにも足す。
    /// </summary>
    public static IReadOnlyList<IconVariant> IconVariants(TransportCommand command) =>
        command switch
        {
            TransportCommand.TogglePlayback =>
            [
                new(TransportIcon.PlayPause, () => UiStrings.IconStateStopped),
                new(TransportIcon.PlayPause, () => UiStrings.IconStatePlaying, Playing: true),
            ],
            TransportCommand.Record =>
            [
                new(TransportIcon.Record, () => UiStrings.IconStateRecordIdle),
                new(TransportIcon.Record, () => UiStrings.IconStateRecording, Latched: true),
            ],
            TransportCommand.SetLoop =>
            [
                new(TransportIcon.SetLoop, () => UiStrings.IconStateNoLoop),
                new(TransportIcon.ClearLoop, () => UiStrings.IconStateHasLoop, Latched: true),
            ],
            TransportCommand.RangeClick =>
            [
                new(TransportIcon.RangeClick, () => UiStrings.IconStateMarkerClickOff),
                new(TransportIcon.RangeClick, () => UiStrings.IconStateMarkerClickOn, Latched: true),
            ],
            _ => [],
        };

    public static string? ModeLimitationNote(TransportCommand command) =>
        command switch
        {
            TransportCommand.PreviousTrack
                or TransportCommand.NextTrack =>
                UiStrings.NoteToolbarPlayerMode,
            TransportCommand.ToggleMinimalChrome
                or TransportCommand.ToggleLibraryMaximize
                or TransportCommand.MaximizeWaveform
                or TransportCommand.ToggleAnalyzerMaximize =>
                UiStrings.NoteToolbarLayoutModeSwitch,
            _ when LibraryPlayerMode.BlocksTransport(command) =>
                UiStrings.NoteToolbarEditorModeOnly,
            _ => null,
        };

    public static bool TryGetItem(TransportCommand command, out Item item)
    {
        foreach (var entry in All)
        {
            if (entry.Command == command)
            {
                item = entry;
                return true;
            }
        }

        item = default;
        return false;
    }

    /// <summary>メニューコマンドに対応するツールバー項目（アイコン共有用）。</summary>
    public static bool TryGetByMenuCommand(WaveMenuCommand command, out Item item)
    {
        foreach (var entry in All)
        {
            if (entry.MenuCommand == command)
            {
                item = entry;
                return true;
            }
        }

        item = default;
        return false;
    }

    public static bool TryGetItem(string? name, out Item item)
    {
        if (!string.IsNullOrWhiteSpace(name)
            && ItemsByName.TryGetValue(MapLegacyCommandName(name.Trim()), out item))
        {
            return true;
        }

        item = default;
        return false;
    }

    /// <summary>グループ境界・グループ内区切りのどちらか。</summary>
    public static bool IsSeparator(string? entry) =>
        IsGroupSeparator(entry) || IsSectionSeparator(entry);

    public static bool IsGroupSeparator(string? entry) =>
        string.Equals(entry?.Trim(), SeparatorToken, StringComparison.Ordinal);

    public static bool IsSectionSeparator(string? entry) =>
        string.Equals(entry?.Trim(), SectionSeparatorToken, StringComparison.Ordinal);

    /// <summary>既定は全コマンドをメニュー順＋境界で並べる。</summary>
    public static string[] CreateDefaultLayout() =>
        BuildAutoLayout(AllCommands.Select(command => command.ToString()));

    /// <summary>
    /// 表示コマンドをメニュー順に並べ、カテゴリが変わるところへ <see cref="SeparatorToken"/>、
    /// カテゴリ内でセパレータ／サブメニューをまたぐところへ <see cref="SectionSeparatorToken"/> を入れる（おまかせ配置）。
    /// </summary>
    public static string[] BuildAutoLayout(IEnumerable<string>? shownCommands)
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in shownCommands ?? [])
        {
            if (IsSeparator(raw))
            {
                continue;
            }

            if (TryGetItem(raw, out var item))
            {
                shown.Add(item.Command.ToString());
            }
        }

        if (shown.Count == 0)
        {
            return [];
        }

        var result = new List<string>();
        MenuPlacement? previous = null;
        foreach (var item in Ordered)
        {
            var name = item.Command.ToString();
            if (!shown.Contains(name))
            {
                continue;
            }

            var placement = PlacementOf(item) ?? new MenuPlacement(int.MaxValue, 0, 0);
            if (previous is { } prev)
            {
                if (prev.Group != placement.Group)
                {
                    result.Add(SeparatorToken);
                }
                else if (prev.Section != placement.Section)
                {
                    result.Add(SectionSeparatorToken);
                }
            }

            previous = placement;
            result.Add(name);
        }

        return [.. result];
    }

    /// <summary>
    /// 設定から実際の並びを解決。既定は全コマンド表示。
    /// Layout が空なら Hidden だけ除外。Layout があるときも、Layout に無い新規コマンドは
    /// Hidden に明示されていなければ足す（カタログ追加分が隠れないようにする）。
    /// 表示コマンドは常にカタログ順＋カテゴリ境界へ組み直す。
    /// </summary>
    public static string[] ResolveLayout(AppSettings settings) =>
        ResolveLayout(settings.TransportToolbarLayout, settings.HiddenTransportToolbarCommands);

    public static string[] ResolveLayout(IEnumerable<string>? layout, IEnumerable<string>? hidden)
    {
        var hiddenSet = new HashSet<string>(
            NormalizeHidden(hidden),
            StringComparer.OrdinalIgnoreCase);
        var stored = layout?.ToArray() ?? [];
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Layout にあるものは表示（同名が Hidden でも Layout を正とする）。
        if (stored.Length > 0)
        {
            foreach (var entry in NormalizeLayout(stored))
            {
                if (!IsSeparator(entry))
                {
                    shown.Add(entry);
                }
            }
        }

        // Hidden に無いカタログ項目は表示が既定（Layout 未記載の新規コマンドも出す）。
        foreach (var command in AllCommands)
        {
            var name = command.ToString();
            if (!hiddenSet.Contains(name))
            {
                shown.Add(name);
            }
        }

        return BuildAutoLayout(shown);
    }

    /// <summary>Layout を正本にし Hidden を同期した組を返す（並びはおまかせ配置へ正規化）。</summary>
    public static (string[] Layout, string[] Hidden) ApplyLayout(IEnumerable<string>? layout)
    {
        var normalized = BuildAutoLayout(NormalizeLayout(layout));
        return (normalized, HiddenFromLayout(normalized));
    }

    /// <summary>未知名除去・コマンド重複除去・端／連続セパレータ整理。</summary>
    public static string[] NormalizeLayout(IEnumerable<string>? layout)
    {
        if (layout is null)
        {
            return CreateDefaultLayout();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in layout)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var trimmed = MapLegacyCommandName(raw.Trim());
            if (IsSeparator(trimmed))
            {
                var token = IsGroupSeparator(trimmed) ? SeparatorToken : SectionSeparatorToken;
                if (result.Count == 0)
                {
                    continue;
                }

                if (IsSeparator(result[^1]))
                {
                    if (token == SeparatorToken)
                    {
                        result[^1] = SeparatorToken;
                    }

                    continue;
                }

                result.Add(token);
                continue;
            }

            if (!ItemsByName.TryGetValue(trimmed, out var item) || !seen.Add(item.Command.ToString()))
            {
                continue;
            }

            result.Add(item.Command.ToString());
        }

        while (result.Count > 0 && IsSeparator(result[^1]))
        {
            result.RemoveAt(result.Count - 1);
        }

        return [.. result];
    }

    public static string[] HiddenFromLayout(IEnumerable<string>? layout)
    {
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in layout ?? [])
        {
            if (IsSeparator(entry))
            {
                continue;
            }

            if (TryGetItem(entry, out var item))
            {
                shown.Add(item.Command.ToString());
            }
        }

        return AllCommands
            .Where(command => !shown.Contains(command.ToString()))
            .Select(command => command.ToString())
            .ToArray();
    }

    public static bool IsCommandVisible(AppSettings settings, TransportCommand command) =>
        IsCommandInLayout(ResolveLayout(settings), command);

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

    public static bool IsCommandInLayout(IEnumerable<string>? layout, TransportCommand command)
    {
        var name = command.ToString();
        foreach (var entry in layout ?? [])
        {
            if (IsSeparator(entry))
            {
                continue;
            }

            if (string.Equals(entry?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

            var trimmed = MapLegacyCommandName(entry.Trim());
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

    /// <summary>旧 Bounce Single／All、および分離していた ClearLoop を現行名へ寄せる。</summary>
    private static string MapLegacyCommandName(string name) =>
        name switch
        {
            "BounceSingle" or "BounceAll" => nameof(TransportCommand.Bounce),
            "ClearLoop" => nameof(TransportCommand.SetLoop),
            _ => name,
        };
}
