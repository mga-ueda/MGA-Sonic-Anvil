using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ツールバーに載るトランスポートコマンドの一覧と、設定からの表示判定。
/// </summary>
internal static class TransportToolbarCatalog
{
    /// <summary>レイアウト配列内のセパレータ（カテゴリ境界）。</summary>
    public const string SeparatorToken = "|";

    internal readonly record struct Item(
        TransportCommand Command,
        TransportIcon Icon,
        Func<string> GroupTitle,
        Func<string> Label,
        Func<string> Tooltip,
        bool InDefaultLayout = true);

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
        // TRANS
        new(TransportCommand.TogglePlayback, TransportIcon.PlayPause,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarPlay, () => UiStrings.TooltipPlay),
        new(TransportCommand.Record, TransportIcon.Record,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarRecord, () => UiStrings.TooltipRecord),
        new(TransportCommand.GoToStart, TransportIcon.GoToStart,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarGoToStart, () => UiStrings.TooltipGoToStart,
            InDefaultLayout: false),
        new(TransportCommand.PreviousMarker, TransportIcon.PreviousMarker,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarPreviousMarker, () => UiStrings.TooltipPreviousMarker,
            InDefaultLayout: false),
        new(TransportCommand.NextMarker, TransportIcon.NextMarker,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarNextMarker, () => UiStrings.TooltipNextMarker,
            InDefaultLayout: false),
        new(TransportCommand.GoToEnd, TransportIcon.GoToEnd,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarGoToEnd, () => UiStrings.TooltipGoToEnd,
            InDefaultLayout: false),

        new(TransportCommand.JumpToTime, TransportIcon.JumpToTime,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarJumpToTime, () => UiStrings.TooltipJumpToTime,
            InDefaultLayout: false),
        new(TransportCommand.SeekViewLeft, TransportIcon.SeekViewLeft,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarSeekViewLeft, () => UiStrings.TooltipSeekViewLeft,
            InDefaultLayout: false),
        new(TransportCommand.SeekViewRight, TransportIcon.SeekViewRight,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarSeekViewRight, () => UiStrings.TooltipSeekViewRight,
            InDefaultLayout: false),
        new(TransportCommand.LoopPlay, TransportIcon.LoopPlay,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarLoopPlay, () => UiStrings.TooltipLoopPlay,
            InDefaultLayout: false),
        new(TransportCommand.Restart, TransportIcon.Restart,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarRestart, () => UiStrings.TooltipRestart,
            InDefaultLayout: false),
        new(TransportCommand.Preroll, TransportIcon.Preroll,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarPreroll, () => UiStrings.TooltipPreroll,
            InDefaultLayout: false),
        new(TransportCommand.PreviousTrack, TransportIcon.PreviousTrack,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarPreviousTrack, () => UiStrings.TooltipPreviousTrack,
            InDefaultLayout: false),
        new(TransportCommand.NextTrack, TransportIcon.NextTrack,
            () => UiStrings.LabelTransportGroup, () => UiStrings.LabelToolbarNextTrack, () => UiStrings.TooltipNextTrack,
            InDefaultLayout: false),

        // EDIT
        new(TransportCommand.Undo, TransportIcon.Undo,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarUndo, () => UiStrings.TooltipUndo,
            InDefaultLayout: false),
        new(TransportCommand.Redo, TransportIcon.Redo,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarRedo, () => UiStrings.TooltipRedo,
            InDefaultLayout: false),
        new(TransportCommand.Cut, TransportIcon.Cut,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarCut, () => UiStrings.TooltipCut,
            InDefaultLayout: false),
        new(TransportCommand.Copy, TransportIcon.Copy,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarCopy, () => UiStrings.TooltipCopy,
            InDefaultLayout: false),
        new(TransportCommand.Paste, TransportIcon.Paste,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarPaste, () => UiStrings.TooltipPaste,
            InDefaultLayout: false),
        new(TransportCommand.Delete, TransportIcon.Delete,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarDelete, () => UiStrings.TooltipDelete),
        new(TransportCommand.SilenceSelection, TransportIcon.SilenceSelection,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarSilenceSelection, () => UiStrings.TooltipSilenceSelection,
            InDefaultLayout: false),
        new(TransportCommand.InsertSilence, TransportIcon.InsertSilence,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarInsertSilence, () => UiStrings.TooltipInsertSilence,
            InDefaultLayout: false),
        new(TransportCommand.DetectSilence, TransportIcon.DetectSilence,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarDetectSilence, () => UiStrings.TooltipDetectSilence,
            InDefaultLayout: false),
        new(TransportCommand.SelectAll, TransportIcon.SelectAll,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarSelectAll, () => UiStrings.TooltipSelectAll,
            InDefaultLayout: false),
        new(TransportCommand.ClearSelection, TransportIcon.ClearSelection,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarClearSelection, () => UiStrings.TooltipClearSelection,
            InDefaultLayout: false),
        new(TransportCommand.SelectToStart, TransportIcon.SelectToStart,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarSelectToStart, () => UiStrings.TooltipSelectToStart,
            InDefaultLayout: false),
        new(TransportCommand.SelectToEnd, TransportIcon.SelectToEnd,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarSelectToEnd, () => UiStrings.TooltipSelectToEnd,
            InDefaultLayout: false),
        new(TransportCommand.CopySelectionTime, TransportIcon.CopySelectionTime,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarCopySelectionTime, () => UiStrings.TooltipCopySelectionTime,
            InDefaultLayout: false),
        new(TransportCommand.FadeIn, TransportIcon.FadeIn,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeIn, () => UiStrings.TooltipFadeIn),
        new(TransportCommand.FadeOut, TransportIcon.FadeOut,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeOut, () => UiStrings.TooltipFadeOut),
        new(TransportCommand.FadeAround, TransportIcon.FadeAround,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarFadeAround, () => UiStrings.TooltipFadeAround),
        new(TransportCommand.CrossfadeEdit, TransportIcon.CrossfadeEdit,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarCrossfadeEdit, () => UiStrings.TooltipCrossfadeEdit,
            InDefaultLayout: false),
        new(TransportCommand.Normalize, TransportIcon.Normalize,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarNormalize, () => UiStrings.TooltipNormalize),
        new(TransportCommand.NormalizePerRegion, TransportIcon.NormalizePerRegion,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarNormalizePerRegion, () => UiStrings.TooltipNormalizePerRegion,
            InDefaultLayout: false),
        new(TransportCommand.Volume, TransportIcon.Volume,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarVolume, () => UiStrings.TooltipVolume),
        new(TransportCommand.Pitch, TransportIcon.Pitch,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarPitch, () => UiStrings.TooltipPitch),
        new(TransportCommand.TimeStretch, TransportIcon.TimeStretch,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarTimeStretch, () => UiStrings.TooltipTimeStretch),
        new(TransportCommand.Reverse, TransportIcon.Reverse,
            () => UiStrings.LabelEditGroup, () => UiStrings.LabelToolbarReverse, () => UiStrings.TooltipReverse),

        // FILE
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
        new(TransportCommand.ExportWave, TransportIcon.ExportWave,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarExportWave, () => UiStrings.TooltipExportWave),
        new(TransportCommand.ExportByMarkers, TransportIcon.ExportByMarkers,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarExportByMarkers, () => UiStrings.TooltipExportByMarkers,
            InDefaultLayout: false),
        new(TransportCommand.ExportByRegions, TransportIcon.ExportByRegions,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarExportByRegions, () => UiStrings.TooltipExportByRegions,
            InDefaultLayout: false),
        new(TransportCommand.ExportByChannels, TransportIcon.ExportByChannels,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarExportByChannels, () => UiStrings.TooltipExportByChannels,
            InDefaultLayout: false),
        new(TransportCommand.MergeTabs, TransportIcon.MergeTabs,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarMergeTabs, () => UiStrings.TooltipMergeTabs,
            InDefaultLayout: false),
        new(TransportCommand.CopyFileName, TransportIcon.CopyFileName,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarCopyFileName, () => UiStrings.TooltipCopyFileName,
            InDefaultLayout: false),
        new(TransportCommand.CopyFilePath, TransportIcon.CopyFilePath,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarCopyFilePath, () => UiStrings.TooltipCopyFilePath,
            InDefaultLayout: false),
        new(TransportCommand.CopyAllTabTimes, TransportIcon.CopyAllTabTimes,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarCopyAllTabTimes, () => UiStrings.TooltipCopyAllTabTimes,
            InDefaultLayout: false),
        new(TransportCommand.CloseTab, TransportIcon.CloseTab,
            () => UiStrings.LabelFileGroup, () => UiStrings.LabelToolbarCloseTab, () => UiStrings.TooltipCloseTab),

        // MARK
        new(TransportCommand.AddMarker, TransportIcon.AddMarker,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarAddMarker, () => UiStrings.TooltipAddMarker),
        new(TransportCommand.SetLoop, TransportIcon.SetLoop,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarSetLoop, () => UiStrings.TooltipSetLoop),
        new(TransportCommand.ClearLoop, TransportIcon.ClearLoop,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarClearLoop, () => UiStrings.TooltipClearLoop,
            InDefaultLayout: false),
        new(TransportCommand.SetRegion, TransportIcon.SetRegion,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarSetRegion, () => UiStrings.TooltipSetRegion),
        new(TransportCommand.DeleteMarkers, TransportIcon.DeleteMarkers,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarDeleteMarkers, () => UiStrings.TooltipDeleteMarkers,
            InDefaultLayout: false),
        new(TransportCommand.DeleteAllMarkers, TransportIcon.DeleteAllMarkers,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarDeleteAllMarkers, () => UiStrings.TooltipDeleteAllMarkers,
            InDefaultLayout: false),
        new(TransportCommand.DeleteRegions, TransportIcon.DeleteRegions,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarDeleteRegions, () => UiStrings.TooltipDeleteRegions,
            InDefaultLayout: false),
        new(TransportCommand.DeleteAllRegions, TransportIcon.DeleteAllRegions,
            () => UiStrings.LabelMarkerGroup, () => UiStrings.LabelToolbarDeleteAllRegions, () => UiStrings.TooltipDeleteAllRegions,
            InDefaultLayout: false),
        // マーカークリック・最前面・タイル・WAAPI 等のメニュー Check トグルは非掲載

        // FMT
        new(TransportCommand.SampleRate, TransportIcon.SampleRate,
            () => UiStrings.LabelFormatGroup, () => UiStrings.LabelToolbarSampleRate, () => UiStrings.TooltipSampleRate,
            InDefaultLayout: false),
        new(TransportCommand.BitDepth, TransportIcon.BitDepth,
            () => UiStrings.LabelFormatGroup, () => UiStrings.LabelToolbarBitDepth, () => UiStrings.TooltipBitDepth,
            InDefaultLayout: false),
        new(TransportCommand.Channels, TransportIcon.Channels,
            () => UiStrings.LabelFormatGroup, () => UiStrings.LabelToolbarChannels, () => UiStrings.TooltipChannels,
            InDefaultLayout: false),

        // VIEW（解析表示・レイアウトモードはアクション／モード切替として掲載。設定系 Check は非掲載）
        new(TransportCommand.ToggleSpectrogram, TransportIcon.Analysis,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarSpectrogram, () => UiStrings.TooltipSpectrogramView),
        new(TransportCommand.ToggleLoudnessView, TransportIcon.Loudness,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarLoudness, () => UiStrings.TooltipLoudnessView),
        new(TransportCommand.CenterPlayhead, TransportIcon.Center,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarCenter, () => UiStrings.TooltipCenterPlayhead),
        new(TransportCommand.History, TransportIcon.History,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarHistory, () => UiStrings.TooltipHistory),
        new(TransportCommand.ToggleMinimalChrome, TransportIcon.MinimalChrome,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarMinimalChrome, () => UiStrings.TooltipMinimalChrome),
        new(TransportCommand.ToggleLibraryMaximize, TransportIcon.PlayerMode,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarLibraryMaximize, () => UiStrings.TooltipLibraryMaximize),
        new(TransportCommand.MaximizeWaveform, TransportIcon.WaveformMaximize,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarWaveformMaximize, () => UiStrings.TooltipWaveformMaximize),
        new(TransportCommand.ToggleAnalyzerMaximize, TransportIcon.AnalyzerMaximize,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarAnalyzerMaximize, () => UiStrings.TooltipAnalyzerMaximize),
        new(TransportCommand.OpenColorPanel, TransportIcon.ColorPalette,
            () => UiStrings.LabelViewGroup, () => UiStrings.LabelToolbarColorPanel, () => UiStrings.TooltipColorPanel),

        // OTHER
        new(TransportCommand.OpenSettings, TransportIcon.Settings,
            () => UiStrings.LabelOtherGroup, () => UiStrings.LabelToolbarSettings, () => UiStrings.TooltipSettings),
        new(TransportCommand.OpenManual, TransportIcon.Help,
            () => UiStrings.LabelOtherGroup, () => UiStrings.LabelToolbarManual, () => UiStrings.TooltipManualHelp),
    ];

    public static IReadOnlyList<TransportCommand> AllCommands { get; } =
        All.Select(item => item.Command).ToArray();

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

    public static bool TryGetItem(string? name, out Item item)
    {
        if (!string.IsNullOrWhiteSpace(name) && ItemsByName.TryGetValue(name.Trim(), out item))
        {
            return true;
        }

        item = default;
        return false;
    }

    public static bool IsSeparator(string? entry) =>
        string.Equals(entry?.Trim(), SeparatorToken, StringComparison.Ordinal);

    /// <summary>カタログ順＋既定表示コマンドのみ＋グループ境界。</summary>
    public static string[] CreateDefaultLayout()
    {
        var result = new List<string>();
        string? currentGroup = null;
        foreach (var item in All)
        {
            if (!item.InDefaultLayout)
            {
                continue;
            }

            var group = item.GroupTitle();
            if (currentGroup is not null
                && !string.Equals(currentGroup, group, StringComparison.Ordinal)
                && result.Count > 0
                && !IsSeparator(result[^1]))
            {
                result.Add(SeparatorToken);
            }

            currentGroup = group;
            result.Add(item.Command.ToString());
        }

        return [.. result];
    }

    /// <summary>
    /// 設定から実際の並びを解決。Layout が空なら既定＋ Hidden で移行。
    /// </summary>
    public static string[] ResolveLayout(AppSettings settings) =>
        ResolveLayout(settings.TransportToolbarLayout, settings.HiddenTransportToolbarCommands);

    public static string[] ResolveLayout(IEnumerable<string>? layout, IEnumerable<string>? hidden)
    {
        var stored = layout?.ToArray() ?? [];
        if (stored.Length > 0)
        {
            return NormalizeLayout(stored);
        }

        var defaults = CreateDefaultLayout();
        var hiddenSet = new HashSet<string>(
            NormalizeHidden(hidden),
            StringComparer.OrdinalIgnoreCase);
        if (hiddenSet.Count == 0)
        {
            return defaults;
        }

        return NormalizeLayout(
            defaults.Where(entry => IsSeparator(entry) || !hiddenSet.Contains(entry)));
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

            var trimmed = raw.Trim();
            if (IsSeparator(trimmed))
            {
                if (result.Count == 0 || IsSeparator(result[^1]))
                {
                    continue;
                }

                result.Add(SeparatorToken);
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

    public static bool IsCommandVisible(AppSettings settings, TransportCommand command)
    {
        var layout = settings.TransportToolbarLayout;
        if (layout is { Length: > 0 })
        {
            return IsCommandInLayout(layout, command);
        }

        // 空 Layout の既定は InDefaultLayout のみ表示。
        if (TryGetItem(command, out var item) && !item.InDefaultLayout)
        {
            return false;
        }

        return IsCommandVisible(settings.HiddenTransportToolbarCommands, command);
    }

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

    /// <summary>Layout を正本にし Hidden を同期した組を返す。</summary>
    public static (string[] Layout, string[] Hidden) ApplyLayout(IEnumerable<string>? layout)
    {
        var normalized = NormalizeLayout(layout);
        return (normalized, HiddenFromLayout(normalized));
    }
}
