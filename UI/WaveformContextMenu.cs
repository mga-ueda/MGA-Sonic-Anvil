using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum WaveformAnalysisView
{
    Waveform,
    Spectrogram,
    Overlay,
    Loudness,
}

internal enum WaveMenuCommand
{
    ClearMarkers,
    ClearRegion,
    ClearLoop,
    PlayFromHere,
    SeekHere,
    AddMarkerHere,
    SelectSpanHere,
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    PasteHistory,
    Delete,
    SilenceSelection,
    InsertSilence,
    DetectSilence,
    SelectAll,
    ClearSelection,
    SelectToStart,
    SelectToEnd,
    CopySelectionTime,
    CopySelectionTimePath,
    History,
    FadeIn,
    FadeOut,
    FadeAround,
    CrossfadeEdit,
    Normalize,
    NormalizePerRegion,
    Volume,
    Pitch,
    PlaybackPitchUp,
    PlaybackPitchDown,
    PlaybackPitchReset,
    TimeStretch,
    Reverse,
    SampleRate,
    BitDepth,
    Channels,
    AddMarker,
    RenameMarker,
    DeleteMarkers,
    DeleteAllMarkers,
    SetRegion,
    RenameRegion,
    DeleteRegions,
    DeleteAllRegions,
    SetLoop,
    ClearLoopItem,
    PrevMarker,
    NextMarker,
    TogglePlayback,
    PauseHere,
    Preroll,
    Restart,
    LoopPlay,
    PlayExit,
    Shuffle,
    AutoPlayNext,
    AutoPlay,
    CheckSelectMode,
    PreviousTrack,
    NextTrack,
    RestartTrack,
    Record,
    RangeClick,
    GoStart,
    GoEnd,
    ViewLeft,
    ViewRight,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomFit,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    CenterPlayhead,
    CenterLock,
    MaximizeWaveform,
    MaximizeAnalyzers,
    MaximizeLibrary,
    VideoMini,
    MinimalChrome,
    VideoFullscreen,
    VideoMiniZoomIn,
    VideoMiniZoomOut,
    VideoHud,
    VideoTimecode,
    PdfBackgroundPin,
    ExitRealPlayback,
    ForceVideoProxy,
    ViewWaveform,
    ViewSpectrogram,
    ViewOverlay,
    ViewLoudness,
    TileOff,
    TileHorizontal,
    TileVertical,
    TileGrid,
    SoloNext,
    SoloPrev,
    SoloClear,
    FocusTime,
    SilentSkip,
    AlwaysOnTop,
    NewDocument,
    Open,
    Save,
    SaveAs,
    RenameFile,
    CopyFileName,
    CopyFilePath,
    DuplicateFile,
    MergeTabs,
    DeleteFile,
    CloseTab,
    CloseOthers,
    CloseTabsRight,
    CloseTabsLeft,
    CloseAll,
    CopyAllTabTimes,
    ReopenTab,
    NextTab,
    PrevTab,
    FindTabs,
    ClearTileSearch,
    SelectAllTabs,
    Settings,
    ColorPanel,
    WaapiPanel,
    KeepTarget,
    AutoActive,
    OutputFolder,
    WwiseExport,
    LaunchWwise,
    Quit,
    ExportWave,
    ExportMp3,
    ExportByMarkers,
    ExportByRegions,
    ExportByChannels,
    Manual,
    GitHub,
}

internal sealed class WaveformContextHit
{
    public long Frame { get; init; }
    public IReadOnlyList<long> MarkerFrames { get; init; } = [];
    public WaveSelection Region { get; init; }
    public bool HitLoop { get; init; }
    public bool HitMarker => MarkerFrames.Count > 0;
    public bool HitRegion => !Region.IsEmpty;
}

internal sealed class WaveformContextMenuModel
{
    public bool HasDocument { get; init; }
    public bool CanEdit { get; init; }
    public bool CanNavigate { get; init; }
    public bool IsBusy { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsRecording { get; init; }
    public bool HasSelection { get; init; }
    public bool HasAnySelection { get; init; }

    /// <summary>アクティブ／選択タブのいずれかに波形選択があるか（選択開始・終了のコピー可否）。</summary>
    public bool CanCopySelectionTimes { get; init; }
    public bool CanUndo { get; init; }
    public bool CanRedo { get; init; }
    public bool CanPasteAudio { get; init; }
    public bool CanPasteHistory { get; init; }
    public bool HasMarkers { get; init; }
    public bool HasRegions { get; init; }
    public bool HasSelectedMarkers { get; init; }
    public bool HasSelectedRegions { get; init; }
    public bool HasSampleLoop { get; init; }
    public bool AllowsRegionsAndLoops { get; init; }
    public bool CanRenameMarker { get; init; }
    public bool CanRenameRegion { get; init; }
    public bool HasSolo { get; init; }
    public bool CenterLocked { get; init; }
    public bool SilentSkip { get; init; }
    public bool AlwaysOnTop { get; init; }
    public bool WaapiVisible { get; init; }
    public bool PlayExit { get; init; }
    public bool RangeClick { get; init; }
    public bool Shuffle { get; init; }
    public bool AutoPlayNext { get; init; }
    public bool AutoPlay { get; init; }
    public bool CheckSelectMode { get; init; }
    public bool CanStepPlaylist { get; init; }
    public bool CanRestartTrack { get; init; }
    public bool VideoMini { get; init; }
    public bool MinimalChrome { get; init; }
    public bool CanToggleVideoMini { get; init; }
    public bool VideoFullscreen { get; init; }
    public bool CanToggleVideoFullscreen { get; init; }
    public bool CanScaleVideoMini { get; init; }
    public bool VideoHud { get; init; }
    public bool CanToggleVideoHud { get; init; }
    public bool VideoTimecode { get; init; }
    public bool CanToggleVideoTimecode { get; init; }
    public bool PdfBackgroundPinned { get; init; }
    public bool CanTogglePdfBackground { get; init; }
    public bool CanExitRealPlayback { get; init; }
    public bool CanForceVideoProxy { get; init; }
    public bool TileMode { get; init; }
    public bool TileSearchActive { get; init; }
    public bool KeepTarget { get; init; }
    public bool AutoActive { get; init; }
    public bool CanKeepTarget { get; init; }
    public bool WaapiExportEnabled { get; init; }
    public bool CanReopenTab { get; init; }
    public bool HasMultipleTabs { get; init; }
    public bool CanCloseOtherTabs { get; init; }
    public bool CanCloseTabsToRight { get; init; }
    public bool CanCloseTabsToLeft { get; init; }
    public bool CanMergeTabs { get; init; }

    /// <summary>タブが選択されているか。編集・書き出し・履歴ペーストが「選択ファイル／選択タブ」対象へ切り替わる。</summary>
    public bool HasSelectedTabs { get; init; }

    /// <summary>複数タブが選択されているか。ファイルの時間表示は 2 件以上のときだけ選択分に絞る。</summary>
    public bool HasMultipleSelectedTabs { get; init; }

    /// <summary>すべてのタブが選択されているか（履歴ペーストの文言と全選択の有効判定）。</summary>
    public bool AllTabsSelected { get; init; }

    /// <summary>選択タブのどれかがマーカーを持つか（選択タブのセパレート書き出しの有効判定）。</summary>
    public bool SelectedTabsHaveMarkers { get; init; }

    /// <summary>選択タブのどれかがリージョンを持つか（同上）。</summary>
    public bool SelectedTabsHaveRegions { get; init; }

    /// <summary>選択タブのどれかがサンプルループを持つか。</summary>
    public bool SelectedTabsHaveSampleLoop { get; init; }

    public WaveformTileArrange WaveTileArrange { get; init; }
    public bool CanTileHorizontal { get; init; }
    public bool CanTileVertical { get; init; }
    public bool CanTileGrid { get; init; }
    public bool WaveformMaximized { get; init; }
    public bool AnalyzersMaximized { get; init; }
    public bool LibraryMaximized { get; init; }
    /// <summary>プレイヤーで選択に MP3 以外の書き出し候補があるか。無いときは MP3 変換をグレーアウト。</summary>
    public bool CanExportMp3 { get; init; } = true;
    public bool CanLoopPlay { get; init; }
    public bool CanAddMarkerHere { get; init; }
    public WaveformAnalysisView AnalysisView { get; init; }
    public WaveformContextHit Hit { get; init; } = new();

    public static WaveformContextMenuModel AllEnabledForTests(bool hasSelectedTabs = false) => new()
    {
        HasSelectedTabs = hasSelectedTabs,
        SelectedTabsHaveMarkers = hasSelectedTabs,
        SelectedTabsHaveRegions = hasSelectedTabs,
        SelectedTabsHaveSampleLoop = hasSelectedTabs,
        HasDocument = true,
        CanEdit = true,
        CanNavigate = true,
        IsPlaying = true,
        HasSelection = true,
        HasAnySelection = true,
        CanCopySelectionTimes = true,
        CanUndo = true,
        CanRedo = true,
        CanPasteAudio = true,
        CanPasteHistory = true,
        HasMarkers = true,
        HasRegions = true,
        HasSelectedMarkers = true,
        HasSelectedRegions = true,
        HasSampleLoop = true,
        AllowsRegionsAndLoops = true,
        CanRenameMarker = true,
        CanRenameRegion = true,
        HasSolo = true,
        CenterLocked = true,
        SilentSkip = true,
        AlwaysOnTop = true,
        WaapiVisible = true,
        PlayExit = true,
        RangeClick = true,
        Shuffle = true,
        AutoPlayNext = true,
        AutoPlay = true,
        CheckSelectMode = true,
        KeepTarget = true,
        AutoActive = true,
        CanKeepTarget = true,
        WaapiExportEnabled = true,
        CanReopenTab = true,
        HasMultipleTabs = true,
        CanCloseOtherTabs = true,
        CanCloseTabsToRight = true,
        CanCloseTabsToLeft = true,
        CanMergeTabs = true,
        WaveTileArrange = WaveformTileArrange.Grid,
        CanTileHorizontal = true,
        CanTileVertical = true,
        CanTileGrid = true,
        CanLoopPlay = true,
        CanAddMarkerHere = true,
        AnalysisView = WaveformAnalysisView.Waveform,
        Hit = new WaveformContextHit
        {
            Frame = 0,
            MarkerFrames = [0],
            Region = new WaveSelection(0, 1),
            HitLoop = true,
        },
    };
}

internal abstract record WaveMenuEntry;

internal sealed record WaveMenuSeparatorEntry : WaveMenuEntry
{
    public static WaveMenuSeparatorEntry Instance { get; } = new();
}

internal sealed record WaveMenuItemEntry(
    string Header,
    WaveMenuCommand? Command,
    string? Gesture = null,
    bool Enabled = true,
    bool Checked = false,
    bool Checkable = false,
    IReadOnlyList<WaveMenuEntry>? Children = null) : WaveMenuEntry;

internal static class MenuAccessKeys
{
    public static bool IsContextMenuKey(Key key, ModifierKeys modifiers) =>
        key == Key.Apps && modifiers == ModifierKeys.None
        || key == Key.F10 && modifiers == ModifierKeys.Shift;

    /// <summary>A〜Z のキーをメニューアクセスキー文字へ。それ以外は null。</summary>
    public static char? FromKey(Key key) =>
        key is >= Key.A and <= Key.Z
            ? (char)('A' + (key - Key.A))
            : null;

    public static char? Read(string header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return null;
        }

        for (var i = 0; i < header.Length - 1; i++)
        {
            if (header[i] != '_')
            {
                continue;
            }

            if (header[i + 1] == '_')
            {
                i++;
                continue;
            }

            return char.ToUpperInvariant(header[i + 1]);
        }

        return null;
    }
}

/// <summary>波形右クリック。カテゴリはサブメニュー、各項目にアクセスキー。</summary>
internal static class WaveformContextMenuBuilder
{
    /// <param name="includeHere">
    /// クリック位置依存の「この位置」。波形右クリックのみ true。
    /// メニューバーとプレイリスト行右クリックは位置が無い／メニューバー例外のため false。
    /// </param>
    public static IReadOnlyList<WaveMenuEntry> Build(WaveformContextMenuModel m, bool includeHere = true)
    {
        var items = new List<WaveMenuEntry>();
        if (includeHere)
        {
            var here = HereItems(m);
            if (here.Count > 0)
            {
                // クリック位置依存のため先頭。定位置カテゴリは下の順。
                items.Add(Sub(UiStrings.WaveMenuCatHere, here));
            }
        }

        items.Add(Sub(UiStrings.WaveMenuCatFile, FileItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatEdit, EditItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatFormat, FormatItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatExport, ExportItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatView, ViewItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatTimeline, TimelineItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatTabs, TabItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatPlayback, PlaybackItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatAudio, AudioItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatWwise, WwiseItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatOther, OtherItems(m)));
        return m.LibraryMaximized
            ? StripDestructiveGestures(items)
            : items;
    }

    /// <summary>プレイヤーでは破壊編集のショートカット表記だけ消す（項目は有効のまま）。</summary>
    private static IReadOnlyList<WaveMenuEntry> StripDestructiveGestures(IReadOnlyList<WaveMenuEntry> entries)
    {
        var list = new List<WaveMenuEntry>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry is WaveMenuSeparatorEntry)
            {
                list.Add(entry);
                continue;
            }

            if (entry is not WaveMenuItemEntry item)
            {
                continue;
            }

            var children = item.Children is { Count: > 0 }
                ? StripDestructiveGestures(item.Children)
                : item.Children;
            var gesture = item.Command is { } command
                ? LibraryPlayerMode.MenuGesture(playerMode: true, command, item.Gesture)
                : item.Gesture;
            list.Add(item with { Gesture = gesture, Children = children });
        }

        return list;
    }

    public static IEnumerable<IReadOnlyList<string>> AccessKeyGroups(IReadOnlyList<WaveMenuEntry> entries)
    {
        yield return Headers(entries);
        foreach (var item in entries.OfType<WaveMenuItemEntry>())
        {
            if (item.Children is { Count: > 0 })
            {
                foreach (var group in AccessKeyGroups(item.Children))
                {
                    yield return group;
                }
            }
        }
    }

    public static IEnumerable<WaveMenuCommand> Commands(IReadOnlyList<WaveMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is not WaveMenuItemEntry item)
            {
                continue;
            }

            if (item.Command is { } command)
            {
                yield return command;
            }

            if (item.Children is { Count: > 0 })
            {
                foreach (var child in Commands(item.Children))
                {
                    yield return child;
                }
            }
        }
    }

    public static ContextMenu Create(
        IReadOnlyList<WaveMenuEntry> entries,
        Action<WaveMenuCommand> onCommand,
        System.Windows.FrameworkElement placementTarget)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = PlacementMode.MousePoint,
        };
        Populate(menu.Items, entries, onCommand);
        return menu;
    }

    /// <summary>
    /// メニューバー／コンテキストメニュー共通の中身入れ替え。
    /// <see cref="Build"/> の結果をそのまま渡す（右クリックと上部メニューを同一ツリーに保つ）。
    /// </summary>
    public static void Populate(
        ItemCollection items,
        IReadOnlyList<WaveMenuEntry> entries,
        Action<WaveMenuCommand> onCommand)
    {
        items.Clear();
        Append(items, entries, onCommand);
    }

    /// <summary>既存項目の後ろへ <see cref="Build"/> ツリーを足す（プレイリストのカラー等の先頭固定用）。</summary>
    public static void Append(
        ItemCollection items,
        IReadOnlyList<WaveMenuEntry> entries,
        Action<WaveMenuCommand> onCommand) =>
        AddItems(items, entries, onCommand);

    /// <summary>ファイルが開いているとき、またはマーカー／リージョン／ループの上のときだけ出す。</summary>
    private static List<WaveMenuEntry> HereItems(WaveformContextMenuModel m)
    {
        var items = new List<WaveMenuEntry>();
        AddClickItems(items, m);
        var hit = new List<WaveMenuEntry>();
        AddContextual(hit, m);
        if (items.Count > 0 && hit.Count > 0)
        {
            items.Add(WaveMenuSeparatorEntry.Instance);
        }

        items.AddRange(hit);
        return items;
    }

    /// <summary>複数ファイル選択時は、単体の波形／再生位置にしか効かない項目を無効化する。</summary>
    private static bool SingleFileOnly(WaveformContextMenuModel m) => !m.HasMultipleSelectedTabs;

    private static void AddContextual(List<WaveMenuEntry> items, WaveformContextMenuModel m)
    {
        if (m.Hit.HitMarker)
        {
            items.Add(Cmd(
                m.Hit.MarkerFrames.Count > 1 ? UiStrings.WaveMenuClearMarkers : UiStrings.WaveMenuClearMarker,
                WaveMenuCommand.ClearMarkers,
                enabled: SingleFileOnly(m) && (m.CanEdit || m.LibraryMaximized)));
        }

        if (m.Hit.HitRegion)
        {
            items.Add(Cmd(
                UiStrings.WaveMenuClearRegion,
                WaveMenuCommand.ClearRegion,
                enabled: SingleFileOnly(m) && m.CanEdit));
        }

        if (m.Hit.HitLoop)
        {
            items.Add(Cmd(
                UiStrings.WaveMenuClearLoop,
                WaveMenuCommand.ClearLoop,
                enabled: SingleFileOnly(m) && (m.CanEdit || m.LibraryMaximized)));
        }
    }

    private static void AddClickItems(List<WaveMenuEntry> items, WaveformContextMenuModel m)
    {
        if (!m.HasDocument)
        {
            return;
        }

        items.Add(Cmd(
            UiStrings.WaveMenuPlayFromHere,
            WaveMenuCommand.PlayFromHere,
            enabled: SingleFileOnly(m) && m.CanNavigate));
        items.Add(Cmd(
            UiStrings.WaveMenuSeekHere,
            WaveMenuCommand.SeekHere,
            enabled: SingleFileOnly(m) && m.CanNavigate));
        items.Add(Cmd(
            UiStrings.WaveMenuAddMarkerHere,
            WaveMenuCommand.AddMarkerHere,
            enabled: SingleFileOnly(m) && m.CanAddMarkerHere));
        items.Add(Cmd(
            UiStrings.WaveMenuSelectSpanHere,
            WaveMenuCommand.SelectSpanHere,
            enabled: SingleFileOnly(m) && m.CanNavigate));
        items.Add(WaveMenuSeparatorEntry.Instance);
        items.Add(Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuCopySelectionTimeSelected : UiStrings.WaveMenuCopySelectionTime,
            WaveMenuCommand.CopySelectionTime,
            enabled: m.CanCopySelectionTimes));
        items.Add(Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuCopySelectionTimePathSelected : UiStrings.WaveMenuCopySelectionTimePath,
            WaveMenuCommand.CopySelectionTimePath,
            enabled: m.CanCopySelectionTimes));
    }

    private static IReadOnlyList<WaveMenuEntry> EditItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuUndoSelected : UiStrings.WaveMenuUndo,
            WaveMenuCommand.Undo,
            "Ctrl+Z",
            m.CanUndo && (m.CanEdit || m.LibraryMaximized)),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuRedoSelected : UiStrings.WaveMenuRedo,
            WaveMenuCommand.Redo,
            MenuGestures.Redo,
            m.CanRedo && (m.CanEdit || m.LibraryMaximized)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuCut, WaveMenuCommand.Cut, "Ctrl+X", SingleFileOnly(m) && m.CanEdit && m.HasSelection),
        Cmd(UiStrings.WaveMenuCopy, WaveMenuCommand.Copy, "Ctrl+C", SingleFileOnly(m) && m.CanEdit && m.HasSelection),
        Cmd(UiStrings.WaveMenuPaste, WaveMenuCommand.Paste, "Ctrl+V", SingleFileOnly(m) && m.CanEdit && m.CanPasteAudio),
        Cmd(
            m.HasSelectedTabs
                ? (m.AllTabsSelected ? UiStrings.WaveMenuPasteHistoryAll : UiStrings.WaveMenuPasteHistorySelected)
                : UiStrings.WaveMenuPasteHistory,
            WaveMenuCommand.PasteHistory,
            enabled: m.CanEdit && m.CanPasteHistory),
        Cmd(
            UiStrings.WaveMenuDelete,
            WaveMenuCommand.Delete,
            MenuGestures.Delete,
            SingleFileOnly(m) && m.CanEdit && (m.HasSelection || m.HasSelectedMarkers || m.HasSelectedRegions)),
        Cmd(
            UiStrings.WaveMenuSilenceSelection,
            WaveMenuCommand.SilenceSelection,
            "Shift+Delete",
            SingleFileOnly(m) && m.CanEdit && m.HasSelection),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuInsertSilenceSelected : UiStrings.WaveMenuInsertSilence,
            WaveMenuCommand.InsertSilence,
            "Ctrl+Ins",
            m.CanEdit || m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSelectAll, WaveMenuCommand.SelectAll, "Ctrl+A", SingleFileOnly(m) && m.CanNavigate),
        Cmd(
            UiStrings.WaveMenuClearSelection,
            WaveMenuCommand.ClearSelection,
            "Esc",
            SingleFileOnly(m) && m.CanNavigate && m.HasAnySelection),
        Cmd(
            UiStrings.WaveMenuSelectToStart,
            WaveMenuCommand.SelectToStart,
            "Ctrl+Shift+Home",
            SingleFileOnly(m) && m.CanNavigate),
        Cmd(
            UiStrings.WaveMenuSelectToEnd,
            WaveMenuCommand.SelectToEnd,
            "Ctrl+Shift+End",
            SingleFileOnly(m) && m.CanNavigate),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuDetectSilenceSelected : UiStrings.WaveMenuDetectSilence,
            WaveMenuCommand.DetectSilence,
            "Ctrl+D",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            UiStrings.WaveMenuHistory,
            WaveMenuCommand.History,
            "U",
            SingleFileOnly(m) && m.CanEdit && m.HasDocument && !m.IsBusy),
    ];

    private static IReadOnlyList<WaveMenuEntry> AudioItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuFadeInSelected : UiStrings.WaveMenuFadeIn,
            WaveMenuCommand.FadeIn,
            "I",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuFadeOutSelected : UiStrings.WaveMenuFadeOut,
            WaveMenuCommand.FadeOut,
            "O",
            m.CanEdit || m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuFadeAround, WaveMenuCommand.FadeAround, "X", SingleFileOnly(m) && m.CanEdit),
        Cmd(
            UiStrings.WaveMenuCrossfadeEdit,
            WaveMenuCommand.CrossfadeEdit,
            MenuGestures.CrossfadeEdit,
            SingleFileOnly(m) && m.CanEdit && m.AllowsRegionsAndLoops && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            m.LibraryMaximized
                ? (m.HasMultipleSelectedTabs
                    ? UiStrings.LibraryMenuMatchLoudnessSelected
                    : UiStrings.LibraryMenuMatchLoudness)
                : (m.HasSelectedTabs ? UiStrings.WaveMenuVolumeSelected : UiStrings.WaveMenuVolume),
            WaveMenuCommand.Volume,
            MenuGestures.Volume,
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuNormalizeSelected : UiStrings.WaveMenuNormalize,
            WaveMenuCommand.Normalize,
            "N",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuNormalizePerRegionSelected : UiStrings.WaveMenuNormalizePerRegion,
            WaveMenuCommand.NormalizePerRegion,
            enabled: (m.CanEdit || m.LibraryMaximized)
                && (m.HasSelectedTabs || m.LibraryMaximized || (m.HasRegions && m.AllowsRegionsAndLoops))),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuPitchSelected : UiStrings.WaveMenuPitch,
            WaveMenuCommand.Pitch,
            "P",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuTimeStretchSelected : UiStrings.WaveMenuTimeStretch,
            WaveMenuCommand.TimeStretch,
            "T",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuReverseSelected : UiStrings.WaveMenuReverse,
            WaveMenuCommand.Reverse,
            "R",
            m.CanEdit || m.LibraryMaximized),
    ];

    private static IReadOnlyList<WaveMenuEntry> FormatItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuSampleRateSelected : UiStrings.WaveMenuSampleRate,
            WaveMenuCommand.SampleRate,
            "S",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuBitDepthSelected : UiStrings.WaveMenuBitDepth,
            WaveMenuCommand.BitDepth,
            "B",
            m.CanEdit || m.LibraryMaximized),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuChannelsSelected : UiStrings.WaveMenuChannels,
            WaveMenuCommand.Channels,
            "C",
            m.CanEdit || m.LibraryMaximized),
    ];

    private static IReadOnlyList<WaveMenuEntry> TimelineItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            UiStrings.WaveMenuPrevMarker,
            WaveMenuCommand.PrevMarker,
            "Ctrl+Left",
            SingleFileOnly(m) && m.CanNavigate && m.HasMarkers),
        Cmd(
            UiStrings.WaveMenuNextMarker,
            WaveMenuCommand.NextMarker,
            "Ctrl+Right",
            SingleFileOnly(m) && m.CanNavigate && m.HasMarkers),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.WaveMenuAddMarker,
            WaveMenuCommand.AddMarker,
            MenuGestures.AddMarker,
            SingleFileOnly(m) && (m.CanEdit || m.LibraryMaximized)),
        Cmd(
            UiStrings.WaveMenuRenameMarker,
            WaveMenuCommand.RenameMarker,
            "Ctrl+Shift+R",
            SingleFileOnly(m) && (m.CanEdit || m.LibraryMaximized) && m.CanRenameMarker),
        Cmd(
            UiStrings.WaveMenuDeleteMarkers,
            WaveMenuCommand.DeleteMarkers,
            "Ctrl+Delete",
            SingleFileOnly(m) && (m.CanEdit || m.LibraryMaximized) && m.HasMarkers),
        Cmd(
            UiStrings.WaveMenuDeleteAllMarkers,
            WaveMenuCommand.DeleteAllMarkers,
            enabled: (m.CanEdit || m.LibraryMaximized)
                && (m.HasMarkers || m.HasSelectedTabs || m.LibraryMaximized)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.WaveMenuSetRegion,
            WaveMenuCommand.SetRegion,
            "Shift+R",
            SingleFileOnly(m) && m.CanEdit && m.HasSelection && m.AllowsRegionsAndLoops),
        Cmd(
            UiStrings.WaveMenuRenameRegion,
            WaveMenuCommand.RenameRegion,
            enabled: SingleFileOnly(m) && m.CanEdit && m.CanRenameRegion),
        Cmd(
            UiStrings.WaveMenuDeleteRegions,
            WaveMenuCommand.DeleteRegions,
            MenuGestures.Delete,
            SingleFileOnly(m) && m.CanEdit && m.HasSelectedRegions),
        Cmd(
            UiStrings.WaveMenuDeleteAllRegions,
            WaveMenuCommand.DeleteAllRegions,
            enabled: (m.CanEdit && m.HasRegions && m.AllowsRegionsAndLoops)
                || ((m.CanEdit || m.LibraryMaximized) && (m.SelectedTabsHaveRegions || m.LibraryMaximized))),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.WaveMenuSetLoop,
            WaveMenuCommand.SetLoop,
            "Shift+L",
            SingleFileOnly(m) && m.CanEdit && m.HasSelection && m.AllowsRegionsAndLoops),
        Cmd(
            UiStrings.WaveMenuClearLoopItem,
            WaveMenuCommand.ClearLoopItem,
            enabled: (m.CanEdit && m.HasSampleLoop)
                || ((m.CanEdit || m.LibraryMaximized) && (m.SelectedTabsHaveSampleLoop || m.LibraryMaximized))),
    ];

    private static IReadOnlyList<WaveMenuEntry> PlaybackItems(WaveformContextMenuModel m) =>
    [
        Cmd(m.IsPlaying ? UiStrings.WaveMenuStopToStart : UiStrings.WaveMenuPlay, WaveMenuCommand.TogglePlayback, "Space", m.HasDocument && !m.IsBusy),
        Cmd(
            UiStrings.WaveMenuPauseHere,
            WaveMenuCommand.PauseHere,
            "Enter",
            SingleFileOnly(m) && m.IsPlaying && !m.IsBusy),
        Cmd(UiStrings.WaveMenuLoopPlay, WaveMenuCommand.LoopPlay, "L", SingleFileOnly(m) && m.CanLoopPlay),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.WaveMenuPitchPreviewUp,
            WaveMenuCommand.PlaybackPitchUp,
            MenuGestures.PitchUp,
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy),
        Cmd(
            UiStrings.WaveMenuPitchPreviewDown,
            WaveMenuCommand.PlaybackPitchDown,
            MenuGestures.PitchDown,
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy),
        Cmd(
            UiStrings.WaveMenuPitchPreviewReset,
            WaveMenuCommand.PlaybackPitchReset,
            MenuGestures.PitchReset,
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Sub(UiStrings.WaveMenuCatSeek, SeekPlaybackItems(m)),
        Cmd(
            UiStrings.WaveMenuFocusTime,
            WaveMenuCommand.FocusTime,
            "G",
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
        Sub(UiStrings.WaveMenuCatPlaylist, PlaylistPlaybackItems(m)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuPreroll, WaveMenuCommand.Preroll, "Ctrl+Space", SingleFileOnly(m) && m.CanNavigate),
        Cmd(UiStrings.WaveMenuRestart, WaveMenuCommand.Restart, "Alt+Enter", SingleFileOnly(m) && m.CanNavigate),
        Check(UiStrings.WaveMenuSilentSkip, WaveMenuCommand.SilentSkip, "Alt+Shift+S", m.SilentSkip, enabled: true),
        Check(UiStrings.WaveMenuRangeClick, WaveMenuCommand.RangeClick, "Q", m.RangeClick, SingleFileOnly(m) && m.CanEdit),
    ];

    private static IReadOnlyList<WaveMenuEntry> PlaylistPlaybackItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            UiStrings.WaveMenuPreviousTrack,
            WaveMenuCommand.PreviousTrack,
            MenuGestures.PreviousTrack,
            SingleFileOnly(m) && m.LibraryMaximized && m.CanStepPlaylist && !m.IsBusy),
        Cmd(
            UiStrings.WaveMenuNextTrack,
            WaveMenuCommand.NextTrack,
            MenuGestures.NextTrack,
            SingleFileOnly(m) && m.LibraryMaximized && m.CanStepPlaylist && !m.IsBusy),
        Cmd(
            UiStrings.WaveMenuRestartTrack,
            WaveMenuCommand.RestartTrack,
            MenuGestures.RestartTrack,
            SingleFileOnly(m) && m.LibraryMaximized && m.CanRestartTrack && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Check(UiStrings.WaveMenuAutoPlay, WaveMenuCommand.AutoPlay, "Alt+Shift+P", m.AutoPlay, m.LibraryMaximized && !m.IsBusy),
        Check(UiStrings.WaveMenuAutoPlayNext, WaveMenuCommand.AutoPlayNext, "Alt+Shift+N", m.AutoPlayNext, m.LibraryMaximized && !m.IsBusy),
        Check(UiStrings.WaveMenuShuffle, WaveMenuCommand.Shuffle, "Alt+Shift+R", m.Shuffle, m.LibraryMaximized && !m.IsBusy),
        Check(
            UiStrings.WaveMenuCheckSelectMode,
            WaveMenuCommand.CheckSelectMode,
            "Shift+Space",
            m.CheckSelectMode,
            m.LibraryMaximized && !m.IsBusy),
    ];

    private static IReadOnlyList<WaveMenuEntry> SeekPlaybackItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuGoStart, WaveMenuCommand.GoStart, "Ctrl+Home", SingleFileOnly(m) && m.CanNavigate),
        Cmd(UiStrings.WaveMenuGoEnd, WaveMenuCommand.GoEnd, "Ctrl+End", SingleFileOnly(m) && m.CanNavigate),
        Cmd(UiStrings.WaveMenuViewLeft, WaveMenuCommand.ViewLeft, "Home", SingleFileOnly(m) && m.CanNavigate),
        Cmd(UiStrings.WaveMenuViewRight, WaveMenuCommand.ViewRight, "End", SingleFileOnly(m) && m.CanNavigate),
    ];

    private static IReadOnlyList<WaveMenuEntry> ViewItems(WaveformContextMenuModel m) =>
    [
        Sub(UiStrings.WaveMenuCatZoom, ZoomViewItems(m)),
        Sub(UiStrings.WaveMenuCatAnalysis, AnalysisViewItems(m)),
        Sub(UiStrings.WaveMenuCatScreen, ScreenViewItems(m)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.WaveMenuCenterPlayhead,
            WaveMenuCommand.CenterPlayhead,
            MenuGestures.CenterPlayhead,
            SingleFileOnly(m) && m.CanNavigate),
        Check(
            UiStrings.WaveMenuCenterLock,
            WaveMenuCommand.CenterLock,
            MenuGestures.CenterPlayhead,
            m.CenterLocked,
            SingleFileOnly(m) && m.IsPlaying && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Sub(UiStrings.WaveMenuCatSolo, SoloViewItems(m)),
        Sub(UiStrings.WaveMenuCatVideo, VideoChromeItems(m)),
    ];

    private static IReadOnlyList<WaveMenuEntry> AnalysisViewItems(WaveformContextMenuModel m) =>
    [
        Check(UiStrings.WaveMenuViewWaveform, WaveMenuCommand.ViewWaveform, MenuGestures.ViewWaveform, m.AnalysisView == WaveformAnalysisView.Waveform, m.HasDocument && !m.IsBusy),
        Check(UiStrings.WaveMenuViewSpectrogram, WaveMenuCommand.ViewSpectrogram, "A", m.AnalysisView == WaveformAnalysisView.Spectrogram, m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
        Check(UiStrings.WaveMenuViewOverlay, WaveMenuCommand.ViewOverlay, "A", m.AnalysisView == WaveformAnalysisView.Overlay, m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
        Check(UiStrings.WaveMenuViewLoudness, WaveMenuCommand.ViewLoudness, "V", m.AnalysisView == WaveformAnalysisView.Loudness, m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
    ];

    private static IReadOnlyList<WaveMenuEntry> ZoomViewItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuTimeZoomIn, WaveMenuCommand.TimeZoomIn, "Up", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuTimeZoomOut, WaveMenuCommand.TimeZoomOut, "Down", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuTimeZoomFit, WaveMenuCommand.TimeZoomFit, "Ctrl+Down", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuTimeZoomMax, WaveMenuCommand.TimeZoomMax, "Ctrl+Up", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuAmpZoomIn, WaveMenuCommand.AmpZoomIn, "Shift+Up", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuAmpZoomOut, WaveMenuCommand.AmpZoomOut, "Shift+Down", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuAmpZoomReset, WaveMenuCommand.AmpZoomReset, "Ctrl+Shift+Down", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuAmpZoomMax, WaveMenuCommand.AmpZoomMax, "Ctrl+Shift+Up", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
    ];

    private static IReadOnlyList<WaveMenuEntry> ScreenViewItems(WaveformContextMenuModel m) =>
    [
        Check(UiStrings.WaveMenuMaximizeWaveform, WaveMenuCommand.MaximizeWaveform, "F11", m.WaveformMaximized, enabled: true),
        Check(UiStrings.WaveMenuMaximizeAnalyzers, WaveMenuCommand.MaximizeAnalyzers, "F12", m.AnalyzersMaximized, enabled: true),
        Check(UiStrings.WaveMenuMaximizeLibrary, WaveMenuCommand.MaximizeLibrary, "F10", m.LibraryMaximized, enabled: true),
        Check(UiStrings.WaveMenuMinimalChrome, WaveMenuCommand.MinimalChrome, "F9", m.MinimalChrome, !m.IsBusy),
        Check(UiStrings.WaveMenuVideoMini, WaveMenuCommand.VideoMini, "F8", m.VideoMini, m.CanToggleVideoMini && !m.IsBusy),
        Check(UiStrings.WaveMenuAlwaysOnTop, WaveMenuCommand.AlwaysOnTop, "Alt+Shift+T", m.AlwaysOnTop, enabled: true),
    ];

    private static IReadOnlyList<WaveMenuEntry> VideoChromeItems(WaveformContextMenuModel m) =>
    [
        Check(UiStrings.WaveMenuVideoFullscreen, WaveMenuCommand.VideoFullscreen, "F", m.VideoFullscreen, m.CanToggleVideoFullscreen && !m.IsBusy),
        Cmd(UiStrings.WaveMenuVideoMiniZoomIn, WaveMenuCommand.VideoMiniZoomIn, MenuGestures.VideoZoomIn, m.CanScaleVideoMini && !m.IsBusy),
        Cmd(UiStrings.WaveMenuVideoMiniZoomOut, WaveMenuCommand.VideoMiniZoomOut, MenuGestures.VideoZoomOut, m.CanScaleVideoMini && !m.IsBusy),
        Check(UiStrings.WaveMenuVideoHud, WaveMenuCommand.VideoHud, "A", m.VideoHud, m.CanToggleVideoHud && !m.IsBusy),
        Check(UiStrings.WaveMenuVideoTimecode, WaveMenuCommand.VideoTimecode, "T", m.VideoTimecode, m.CanToggleVideoTimecode && !m.IsBusy),
        Check(UiStrings.WaveMenuPdfBackgroundPin, WaveMenuCommand.PdfBackgroundPin, "W", m.PdfBackgroundPinned, m.CanTogglePdfBackground && !m.IsBusy),
        Cmd(UiStrings.WaveMenuExitRealPlayback, WaveMenuCommand.ExitRealPlayback, MenuGestures.ExitRealPlayback, m.CanExitRealPlayback && !m.IsBusy),
        Cmd(UiStrings.WaveMenuForceVideoProxy, WaveMenuCommand.ForceVideoProxy, "Ctrl+Shift+P", m.CanForceVideoProxy && !m.IsBusy),
    ];

    private static IReadOnlyList<WaveMenuEntry> SoloViewItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuSoloNext, WaveMenuCommand.SoloNext, "Tab", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuSoloPrev, WaveMenuCommand.SoloPrev, "Shift+Tab", SingleFileOnly(m) && m.CanNavigate && !m.LibraryMaximized),
        Cmd(
            UiStrings.WaveMenuSoloClear,
            WaveMenuCommand.SoloClear,
            enabled: SingleFileOnly(m) && m.CanNavigate && m.HasSolo && !m.LibraryMaximized),
    ];

    /// <summary>ファイルそのものへの操作だけ。タブ管理は TabItems（タブカテゴリ）に分ける。</summary>
    private static IReadOnlyList<WaveMenuEntry> FileItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuNew, WaveMenuCommand.NewDocument, "Ctrl+N", !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuOpen, WaveMenuCommand.Open, "Ctrl+O", !m.IsBusy),
        Check(
            UiStrings.WaveMenuRecord,
            WaveMenuCommand.Record,
            "Ctrl+R",
            m.IsRecording,
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
        Cmd(m.HasSelectedTabs ? UiStrings.WaveMenuSaveSelected : UiStrings.WaveMenuSave, WaveMenuCommand.Save, "Ctrl+S", m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(m.HasSelectedTabs ? UiStrings.WaveMenuSaveAsSelected : UiStrings.WaveMenuSaveAs, WaveMenuCommand.SaveAs, "Ctrl+Shift+S", m.HasDocument && !m.IsBusy && !m.IsRecording),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuCopyFileNameSelected : UiStrings.WaveMenuCopyFileName,
            WaveMenuCommand.CopyFileName,
            enabled: m.HasDocument),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuCopyFilePathSelected : UiStrings.WaveMenuCopyFilePath,
            WaveMenuCommand.CopyFilePath,
            enabled: m.HasDocument),
        Cmd(
            UiStrings.WaveMenuRenameFile,
            WaveMenuCommand.RenameFile,
            enabled: SingleFileOnly(m) && m.HasDocument && !m.IsBusy && !m.IsRecording && !m.LibraryMaximized),
        Cmd(m.HasSelectedTabs ? UiStrings.WaveMenuDuplicateFileSelected : UiStrings.WaveMenuDuplicateFile, WaveMenuCommand.DuplicateFile, "Ctrl+Shift+D", m.HasDocument && !m.IsBusy && !m.IsRecording && !m.LibraryMaximized),
        Cmd(m.HasSelectedTabs ? UiStrings.WaveMenuDeleteFileSelected : UiStrings.WaveMenuDeleteFile, WaveMenuCommand.DeleteFile, enabled: m.HasDocument && !m.IsBusy && !m.IsRecording && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuQuit, WaveMenuCommand.Quit, "Ctrl+Q"),
    ];

    private static IReadOnlyList<WaveMenuEntry> WwiseItems(WaveformContextMenuModel m) =>
    [
        Check(UiStrings.WaveMenuWaapiPanel, WaveMenuCommand.WaapiPanel, "Alt+Shift+W", m.WaapiVisible, !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuWwiseExport, WaveMenuCommand.WwiseExport, "Ctrl+Shift+E", m.WaapiExportEnabled && !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuLaunchWwise, WaveMenuCommand.LaunchWwise, "Ctrl+Alt+Shift+W", !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuOutputFolder, WaveMenuCommand.OutputFolder, enabled: m.WaapiVisible && !m.IsBusy && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Check(UiStrings.WaveMenuKeepTarget, WaveMenuCommand.KeepTarget, checkedState: m.KeepTarget, enabled: m.WaapiVisible && m.CanKeepTarget && !m.IsBusy && !m.LibraryMaximized),
        Check(UiStrings.WaveMenuAutoActive, WaveMenuCommand.AutoActive, "Alt+Shift+A", m.AutoActive, m.WaapiVisible && !m.IsBusy && !m.LibraryMaximized),
        Check(UiStrings.WaveMenuPlayExit, WaveMenuCommand.PlayExit, "Alt+Shift+E", m.PlayExit, m.WaapiVisible && m.HasDocument && !m.IsBusy && !m.LibraryMaximized),
    ];

    /// <summary>
    /// タブ管理。閉じる系／全選択／再開／タイルはタブ右クリックと同じ文言・アクセスキー。
    /// 加えて次／前のタブとファイルの時間。リネーム・複製・削除は FileItems、バウンスと書き出しは ExportItems。
    /// </summary>
    private static IReadOnlyList<WaveMenuEntry> TabItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuNextTab, WaveMenuCommand.NextTab, MenuGestures.NextTab, m.HasMultipleTabs && !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuPrevTab, WaveMenuCommand.PrevTab, MenuGestures.PrevTab, m.HasMultipleTabs && !m.IsBusy && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            UiStrings.TabMenuCloseThis,
            WaveMenuCommand.CloseTab,
            "Ctrl+W",
            SingleFileOnly(m) && m.HasDocument && !m.IsBusy && !m.IsRecording && !m.LibraryMaximized),
        Cmd(
            UiStrings.TabMenuCloseOthers,
            WaveMenuCommand.CloseOthers,
            enabled: SingleFileOnly(m) && m.CanCloseOtherTabs && !m.LibraryMaximized),
        Cmd(
            UiStrings.TabMenuCloseRight,
            WaveMenuCommand.CloseTabsRight,
            enabled: SingleFileOnly(m) && m.CanCloseTabsToRight && !m.LibraryMaximized),
        Cmd(
            UiStrings.TabMenuCloseLeft,
            WaveMenuCommand.CloseTabsLeft,
            enabled: SingleFileOnly(m) && m.CanCloseTabsToLeft && !m.LibraryMaximized),
        Cmd(UiStrings.TabMenuCloseAllNormal, WaveMenuCommand.CloseAll, "Ctrl+Shift+W", m.HasDocument && !m.IsBusy && !m.IsRecording && !m.LibraryMaximized),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.TabMenuSelectAll, WaveMenuCommand.SelectAllTabs, "Ctrl+Shift+A", m.HasMultipleTabs && !m.AllTabsSelected && !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuReopenTab, WaveMenuCommand.ReopenTab, "Ctrl+Shift+T", m.CanReopenTab && !m.IsBusy && !m.LibraryMaximized),
        Cmd(m.HasMultipleSelectedTabs ? UiStrings.WaveMenuCopySelectedTabTimes : UiStrings.WaveMenuCopyAllTabTimes, WaveMenuCommand.CopyAllTabTimes, enabled: m.HasDocument),
        WaveMenuSeparatorEntry.Instance,
        Check(UiStrings.WaveMenuTileOff, WaveMenuCommand.TileOff, null, m.WaveTileArrange == WaveformTileArrange.Off, CanPickTile(m, WaveformTileArrange.Off)),
        Check(UiStrings.WaveMenuTileHorizontal, WaveMenuCommand.TileHorizontal, null, m.WaveTileArrange == WaveformTileArrange.Horizontal, CanPickTile(m, WaveformTileArrange.Horizontal)),
        Check(UiStrings.WaveMenuTileVertical, WaveMenuCommand.TileVertical, null, m.WaveTileArrange == WaveformTileArrange.Vertical, CanPickTile(m, WaveformTileArrange.Vertical)),
        Check(UiStrings.WaveMenuTileGrid, WaveMenuCommand.TileGrid, null, m.WaveTileArrange == WaveformTileArrange.Grid, CanPickTile(m, WaveformTileArrange.Grid)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuFindTabs, WaveMenuCommand.FindTabs, "Ctrl+F", m.TileMode && !m.IsBusy && !m.LibraryMaximized),
        Cmd(UiStrings.WaveMenuClearTileSearch, WaveMenuCommand.ClearTileSearch, "Ctrl+Shift+F", m.TileSearchActive && !m.IsBusy && !m.LibraryMaximized),
    ];

    private static IReadOnlyList<WaveMenuEntry> ExportItems(WaveformContextMenuModel m) =>
    [
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuExportWaveSelected : UiStrings.WaveMenuExportWave,
            WaveMenuCommand.ExportWave,
            enabled: m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(
            m.LibraryMaximized
                ? UiStrings.LibraryMenuSaveMp3
                : (m.HasSelectedTabs ? UiStrings.WaveMenuExportMp3Selected : UiStrings.WaveMenuExportMp3),
            WaveMenuCommand.ExportMp3,
            "Ctrl+Shift+M",
            m.HasDocument && !m.IsBusy && !m.IsRecording && (!m.LibraryMaximized || m.CanExportMp3)),
        Cmd(UiStrings.WaveMenuMergeTabs, WaveMenuCommand.MergeTabs, "Ctrl+Shift+B", m.CanMergeTabs),
        WaveMenuSeparatorEntry.Instance,
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuExportByMarkersSelected : UiStrings.WaveMenuExportByMarkers,
            WaveMenuCommand.ExportByMarkers,
            enabled: m.HasDocument
                && !m.IsBusy
                && !m.IsRecording
                && ((m.HasSelectedTabs || m.LibraryMaximized)
                    ? m.SelectedTabsHaveMarkers
                    : m.HasMarkers)),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuExportByRegionsSelected : UiStrings.WaveMenuExportByRegions,
            WaveMenuCommand.ExportByRegions,
            enabled: m.HasDocument
                && !m.IsBusy
                && !m.IsRecording
                && ((m.HasSelectedTabs || m.LibraryMaximized)
                    ? m.SelectedTabsHaveRegions
                    : m.HasRegions && m.AllowsRegionsAndLoops)),
        Cmd(
            m.HasSelectedTabs ? UiStrings.WaveMenuExportByChannelsSelected : UiStrings.WaveMenuExportByChannels,
            WaveMenuCommand.ExportByChannels,
            enabled: m.HasDocument && !m.IsBusy && !m.IsRecording),
    ];

    private static IReadOnlyList<WaveMenuEntry> OtherItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuSettings, WaveMenuCommand.Settings, "Ctrl+Shift+O", !m.IsBusy),
        Cmd(UiStrings.WaveMenuColorPanel, WaveMenuCommand.ColorPanel, "Ctrl+Shift+C"),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuManual, WaveMenuCommand.Manual),
        Cmd(UiStrings.WaveMenuGitHub, WaveMenuCommand.GitHub),
    ];

    private static WaveMenuItemEntry Sub(string header, IReadOnlyList<WaveMenuEntry> children) =>
        new(header, Command: null, Enabled: HasExecutableItem(children), Children: children);

    /// <summary>葉で有効なコマンド、またはその子孫が一つでもあれば true。区切りは数えない。</summary>
    private static bool HasExecutableItem(IReadOnlyList<WaveMenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry is not WaveMenuItemEntry item)
            {
                continue;
            }

            if (item.Children is { Count: > 0 })
            {
                if (item.Enabled && HasExecutableItem(item.Children))
                {
                    return true;
                }

                continue;
            }

            if (item.Enabled)
            {
                return true;
            }
        }

        return false;
    }

    private static bool CanPickTile(WaveformContextMenuModel m, WaveformTileArrange arrange)
    {
        if (m.LibraryMaximized || !m.HasMultipleTabs || m.IsBusy)
        {
            return false;
        }

        if (arrange == WaveformTileArrange.Off || m.WaveTileArrange == arrange)
        {
            return true;
        }

        return arrange switch
        {
            WaveformTileArrange.Horizontal => m.CanTileHorizontal,
            WaveformTileArrange.Vertical => m.CanTileVertical,
            WaveformTileArrange.Grid => m.CanTileGrid,
            _ => false,
        };
    }

    private static WaveMenuItemEntry Cmd(
        string header,
        WaveMenuCommand command,
        string? gesture = null,
        bool enabled = true) =>
        new(header, command, gesture, enabled);

    private static WaveMenuItemEntry Check(
        string header,
        WaveMenuCommand command,
        string? gesture = null,
        bool checkedState = false,
        bool enabled = true) =>
        new(header, command, gesture, enabled, checkedState, Checkable: true);

    private static IReadOnlyList<string> Headers(IReadOnlyList<WaveMenuEntry> entries)
    {
        var list = new List<string>();
        foreach (var entry in entries)
        {
            if (entry is WaveMenuItemEntry item)
            {
                list.Add(item.Header);
            }
        }

        return list;
    }

    private static void AddItems(
        ItemCollection items,
        IReadOnlyList<WaveMenuEntry> entries,
        Action<WaveMenuCommand> onCommand)
    {
        foreach (var entry in entries)
        {
            if (entry is WaveMenuSeparatorEntry)
            {
                items.Add(new Separator());
                continue;
            }

            if (entry is not WaveMenuItemEntry item)
            {
                continue;
            }

            var menuItem = new MenuItem
            {
                Header = item.Header,
                IsEnabled = item.Enabled,
                IsCheckable = item.Checkable,
                IsChecked = item.Checked,
            };
            if (item.Gesture is not null)
            {
                menuItem.InputGestureText = item.Gesture;
            }

            if (item.Children is { Count: > 0 })
            {
                menuItem.StaysOpenOnClick = true;
                AddItems(menuItem.Items, item.Children, onCommand);
            }
            else if (item.Command is { } command)
            {
                menuItem.Click += (_, _) => onCommand(command);
            }

                        items.Add(menuItem);
        }
    }
}
