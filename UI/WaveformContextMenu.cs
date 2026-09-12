using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    SelectAll,
    ClearSelection,
    SelectToStart,
    SelectToEnd,
    History,
    FadeIn,
    FadeOut,
    FadeAround,
    Normalize,
    Volume,
    Pitch,
    TimeStretch,
    Reverse,
    SampleRate,
    BitDepth,
    Channels,
    AddMarker,
    RenameMarker,
    DeleteMarkers,
    SetRegion,
    RenameRegion,
    DeleteRegions,
    SetLoop,
    ClearLoopItem,
    PrevMarker,
    NextMarker,
    TogglePlayback,
    Stop,
    PauseHere,
    Preroll,
    Restart,
    LoopPlay,
    PlayExit,
    Record,
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
    ViewWaveform,
    ViewSpectrogram,
    ViewOverlay,
    ViewLoudness,
    SoloNext,
    SoloPrev,
    SoloClear,
    FocusTime,
    AlwaysOnTop,
    Open,
    Save,
    SaveAs,
    SaveMp3,
    CloseTab,
    CloseAll,
    ReopenTab,
    NextTab,
    PrevTab,
    Settings,
    WaapiPanel,
    WwiseExport,
    Quit,
    ExportWave,
    ExportMp3,
    ExportByMarkers,
    ExportByRegions,
    ExportAllMp3,
    Tips,
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
    public bool AlwaysOnTop { get; init; }
    public bool TipsVisible { get; init; }
    public bool WaapiVisible { get; init; }
    public bool PlayExit { get; init; }
    public bool WaapiExportEnabled { get; init; }
    public bool CanReopenTab { get; init; }
    public bool HasMultipleTabs { get; init; }
    public WaveformAnalysisView AnalysisView { get; init; }
    public WaveformContextHit Hit { get; init; } = new();

    public static WaveformContextMenuModel AllEnabledForTests() => new()
    {
        HasDocument = true,
        CanEdit = true,
        CanNavigate = true,
        IsPlaying = true,
        HasSelection = true,
        HasAnySelection = true,
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
        AlwaysOnTop = true,
        TipsVisible = true,
        WaapiVisible = true,
        PlayExit = true,
        WaapiExportEnabled = true,
        CanReopenTab = true,
        HasMultipleTabs = true,
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
    public static IReadOnlyList<WaveMenuEntry> Build(WaveformContextMenuModel m)
    {
        var items = new List<WaveMenuEntry>();
        AddContextual(items, m);
        AddClickItems(items, m);
        if (items.Count > 0)
        {
            items.Add(WaveMenuSeparatorEntry.Instance);
        }

        items.Add(Sub(UiStrings.WaveMenuCatEdit, EditItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatAudio, AudioItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatFormat, FormatItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatTimeline, TimelineItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatPlayback, PlaybackItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatView, ViewItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatFile, FileItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatExport, ExportItems(m)));
        items.Add(Sub(UiStrings.WaveMenuCatHelp, HelpItems(m)));
        return items;
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
        AddItems(menu.Items, entries, onCommand);
        return menu;
    }

    private static void AddContextual(List<WaveMenuEntry> items, WaveformContextMenuModel m)
    {
        if (m.Hit.HitMarker)
        {
            items.Add(Cmd(
                m.Hit.MarkerFrames.Count > 1 ? UiStrings.WaveMenuClearMarkers : UiStrings.WaveMenuClearMarker,
                WaveMenuCommand.ClearMarkers,
                enabled: m.CanEdit));
        }

        if (m.Hit.HitRegion)
        {
            items.Add(Cmd(UiStrings.WaveMenuClearRegion, WaveMenuCommand.ClearRegion, enabled: m.CanEdit));
        }

        if (m.Hit.HitLoop)
        {
            items.Add(Cmd(UiStrings.WaveMenuClearLoop, WaveMenuCommand.ClearLoop, enabled: m.CanEdit));
        }
    }

    private static void AddClickItems(List<WaveMenuEntry> items, WaveformContextMenuModel m)
    {
        if (!m.HasDocument)
        {
            return;
        }

        items.Add(Cmd(UiStrings.WaveMenuPlayFromHere, WaveMenuCommand.PlayFromHere, enabled: m.CanNavigate));
        items.Add(Cmd(UiStrings.WaveMenuSeekHere, WaveMenuCommand.SeekHere, enabled: m.CanNavigate));
        items.Add(Cmd(UiStrings.WaveMenuAddMarkerHere, WaveMenuCommand.AddMarkerHere, enabled: m.CanEdit));
        items.Add(Cmd(UiStrings.WaveMenuSelectSpanHere, WaveMenuCommand.SelectSpanHere, enabled: m.CanNavigate));
    }

    private static IReadOnlyList<WaveMenuEntry> EditItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuUndo, WaveMenuCommand.Undo, "Ctrl+Z", m.CanEdit && m.CanUndo),
        Cmd(UiStrings.WaveMenuRedo, WaveMenuCommand.Redo, "Ctrl+Y", m.CanEdit && m.CanRedo),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuCut, WaveMenuCommand.Cut, "Ctrl+X", m.CanEdit && m.HasSelection),
        Cmd(UiStrings.WaveMenuCopy, WaveMenuCommand.Copy, "Ctrl+C", m.CanEdit && m.HasSelection),
        Cmd(UiStrings.WaveMenuPaste, WaveMenuCommand.Paste, "Ctrl+V", m.CanEdit && m.CanPasteAudio),
        Cmd(UiStrings.WaveMenuPasteHistory, WaveMenuCommand.PasteHistory, enabled: m.CanEdit && m.CanPasteHistory),
        Cmd(UiStrings.WaveMenuDelete, WaveMenuCommand.Delete, "Delete", m.CanEdit && (m.HasSelection || m.HasSelectedMarkers || m.HasSelectedRegions)),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSelectAll, WaveMenuCommand.SelectAll, "Ctrl+A", m.CanNavigate),
        Cmd(UiStrings.WaveMenuClearSelection, WaveMenuCommand.ClearSelection, "Esc", m.CanNavigate && m.HasAnySelection),
        Cmd(UiStrings.WaveMenuSelectToStart, WaveMenuCommand.SelectToStart, "Ctrl+Shift+Home", m.CanNavigate),
        Cmd(UiStrings.WaveMenuSelectToEnd, WaveMenuCommand.SelectToEnd, "Ctrl+Shift+End", m.CanNavigate),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuHistory, WaveMenuCommand.History, "U", m.HasDocument && !m.IsBusy),
    ];

    private static IReadOnlyList<WaveMenuEntry> AudioItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuFadeIn, WaveMenuCommand.FadeIn, "I", m.CanEdit),
        Cmd(UiStrings.WaveMenuFadeOut, WaveMenuCommand.FadeOut, "O", m.CanEdit),
        Cmd(UiStrings.WaveMenuFadeAround, WaveMenuCommand.FadeAround, "X", m.CanEdit),
        Cmd(UiStrings.WaveMenuNormalize, WaveMenuCommand.Normalize, "N", m.CanEdit),
        Cmd(UiStrings.WaveMenuVolume, WaveMenuCommand.Volume, "V", m.CanEdit),
        Cmd(UiStrings.WaveMenuPitch, WaveMenuCommand.Pitch, "P", m.CanEdit),
        Cmd(UiStrings.WaveMenuTimeStretch, WaveMenuCommand.TimeStretch, "T", m.CanEdit),
        Cmd(UiStrings.WaveMenuReverse, WaveMenuCommand.Reverse, "R", m.CanEdit),
    ];

    private static IReadOnlyList<WaveMenuEntry> FormatItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuSampleRate, WaveMenuCommand.SampleRate, "S", m.CanEdit),
        Cmd(UiStrings.WaveMenuBitDepth, WaveMenuCommand.BitDepth, "B", m.CanEdit),
        Cmd(UiStrings.WaveMenuChannels, WaveMenuCommand.Channels, "C", m.CanEdit),
    ];

    private static IReadOnlyList<WaveMenuEntry> TimelineItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuAddMarker, WaveMenuCommand.AddMarker, "M", m.CanEdit),
        Cmd(UiStrings.WaveMenuRenameMarker, WaveMenuCommand.RenameMarker, "Ctrl+Shift+R", m.CanEdit && m.CanRenameMarker),
        Cmd(UiStrings.WaveMenuDeleteMarkers, WaveMenuCommand.DeleteMarkers, "Ctrl+Delete", m.CanEdit && m.HasMarkers),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSetRegion, WaveMenuCommand.SetRegion, "Shift+R", m.CanEdit && m.HasSelection && m.AllowsRegionsAndLoops),
        Cmd(UiStrings.WaveMenuRenameRegion, WaveMenuCommand.RenameRegion, enabled: m.CanEdit && m.CanRenameRegion),
        Cmd(UiStrings.WaveMenuDeleteRegions, WaveMenuCommand.DeleteRegions, "Delete", m.CanEdit && m.HasSelectedRegions),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSetLoop, WaveMenuCommand.SetLoop, "Shift+L", m.CanEdit && m.HasSelection && m.AllowsRegionsAndLoops),
        Cmd(UiStrings.WaveMenuClearLoopItem, WaveMenuCommand.ClearLoopItem, enabled: m.CanEdit && m.HasSampleLoop),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuPrevMarker, WaveMenuCommand.PrevMarker, "Ctrl+Left", m.CanNavigate && m.HasMarkers),
        Cmd(UiStrings.WaveMenuNextMarker, WaveMenuCommand.NextMarker, "Ctrl+Right", m.CanNavigate && m.HasMarkers),
    ];

    private static IReadOnlyList<WaveMenuEntry> PlaybackItems(WaveformContextMenuModel m) =>
    [
        Cmd(m.IsPlaying ? UiStrings.WaveMenuStopToStart : UiStrings.WaveMenuPlay, WaveMenuCommand.TogglePlayback, "Space", m.HasDocument && !m.IsBusy),
        Cmd(UiStrings.WaveMenuPauseHere, WaveMenuCommand.PauseHere, "Enter", m.IsPlaying && !m.IsBusy),
        Cmd(UiStrings.WaveMenuPreroll, WaveMenuCommand.Preroll, "Ctrl+Space", m.CanNavigate),
        Cmd(UiStrings.WaveMenuRestart, WaveMenuCommand.Restart, "Alt+Enter", m.CanNavigate),
        Cmd(UiStrings.WaveMenuLoopPlay, WaveMenuCommand.LoopPlay, "L", m.CanNavigate),
        Check(UiStrings.WaveMenuPlayExit, WaveMenuCommand.PlayExit, "E", m.PlayExit, m.HasDocument && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Check(UiStrings.WaveMenuRecord, WaveMenuCommand.Record, "Ctrl+R", m.IsRecording, !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuGoStart, WaveMenuCommand.GoStart, "Ctrl+Home", m.CanNavigate),
        Cmd(UiStrings.WaveMenuGoEnd, WaveMenuCommand.GoEnd, "Ctrl+End", m.CanNavigate),
        Cmd(UiStrings.WaveMenuViewLeft, WaveMenuCommand.ViewLeft, "Home", m.CanNavigate),
        Cmd(UiStrings.WaveMenuViewRight, WaveMenuCommand.ViewRight, "End", m.CanNavigate),
    ];

    private static IReadOnlyList<WaveMenuEntry> ViewItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuTimeZoomIn, WaveMenuCommand.TimeZoomIn, "Up", m.CanNavigate),
        Cmd(UiStrings.WaveMenuTimeZoomOut, WaveMenuCommand.TimeZoomOut, "Down", m.CanNavigate),
        Cmd(UiStrings.WaveMenuTimeZoomMax, WaveMenuCommand.TimeZoomMax, "Ctrl+Up", m.CanNavigate),
        Cmd(UiStrings.WaveMenuTimeZoomFit, WaveMenuCommand.TimeZoomFit, "Ctrl+Down", m.CanNavigate),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuAmpZoomIn, WaveMenuCommand.AmpZoomIn, "Shift+Up", m.CanNavigate),
        Cmd(UiStrings.WaveMenuAmpZoomOut, WaveMenuCommand.AmpZoomOut, "Shift+Down", m.CanNavigate),
        Cmd(UiStrings.WaveMenuAmpZoomMax, WaveMenuCommand.AmpZoomMax, "Ctrl+Shift+Up", m.CanNavigate),
        Cmd(UiStrings.WaveMenuAmpZoomReset, WaveMenuCommand.AmpZoomReset, "Ctrl+Shift+Down", m.CanNavigate),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuCenterPlayhead, WaveMenuCommand.CenterPlayhead, "Z", m.CanNavigate),
        Check(UiStrings.WaveMenuCenterLock, WaveMenuCommand.CenterLock, "Z", m.CenterLocked, m.IsPlaying && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Check(UiStrings.WaveMenuViewWaveform, WaveMenuCommand.ViewWaveform, "Shift+A", m.AnalysisView == WaveformAnalysisView.Waveform, m.HasDocument && !m.IsBusy),
        Check(UiStrings.WaveMenuViewSpectrogram, WaveMenuCommand.ViewSpectrogram, "A", m.AnalysisView == WaveformAnalysisView.Spectrogram, m.HasDocument && !m.IsBusy),
        Check(UiStrings.WaveMenuViewOverlay, WaveMenuCommand.ViewOverlay, "A", m.AnalysisView == WaveformAnalysisView.Overlay, m.HasDocument && !m.IsBusy),
        Check(UiStrings.WaveMenuViewLoudness, WaveMenuCommand.ViewLoudness, "V", m.AnalysisView == WaveformAnalysisView.Loudness, m.HasDocument && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSoloNext, WaveMenuCommand.SoloNext, "Tab", m.CanNavigate),
        Cmd(UiStrings.WaveMenuSoloPrev, WaveMenuCommand.SoloPrev, "Shift+Tab", m.CanNavigate),
        Cmd(UiStrings.WaveMenuSoloClear, WaveMenuCommand.SoloClear, enabled: m.CanNavigate && m.HasSolo),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuFocusTime, WaveMenuCommand.FocusTime, "G", m.HasDocument && !m.IsBusy),
        Check(UiStrings.WaveMenuAlwaysOnTop, WaveMenuCommand.AlwaysOnTop, checkedState: m.AlwaysOnTop, enabled: true),
    ];

    private static IReadOnlyList<WaveMenuEntry> FileItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuOpen, WaveMenuCommand.Open, "Ctrl+O", !m.IsBusy),
        Cmd(UiStrings.WaveMenuSave, WaveMenuCommand.Save, "Ctrl+S", m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuSaveAs, WaveMenuCommand.SaveAs, "Ctrl+Shift+S", m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuSaveMp3, WaveMenuCommand.SaveMp3, "Ctrl+Shift+M", m.HasDocument && !m.IsBusy && !m.IsRecording),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuCloseTab, WaveMenuCommand.CloseTab, "Ctrl+W", m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuCloseAll, WaveMenuCommand.CloseAll, "Ctrl+Shift+W", m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuReopenTab, WaveMenuCommand.ReopenTab, "Ctrl+Shift+T", m.CanReopenTab && !m.IsBusy),
        Cmd(UiStrings.WaveMenuNextTab, WaveMenuCommand.NextTab, "Ctrl+Tab", m.HasMultipleTabs && !m.IsBusy),
        Cmd(UiStrings.WaveMenuPrevTab, WaveMenuCommand.PrevTab, "Ctrl+Shift+Tab", m.HasMultipleTabs && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuSettings, WaveMenuCommand.Settings, "Ctrl+Shift+O", !m.IsBusy),
        Check(UiStrings.WaveMenuWaapiPanel, WaveMenuCommand.WaapiPanel, "W", m.WaapiVisible, !m.IsBusy),
        Cmd(UiStrings.WaveMenuWwiseExport, WaveMenuCommand.WwiseExport, "Ctrl+Shift+E", m.WaapiExportEnabled && !m.IsBusy),
        WaveMenuSeparatorEntry.Instance,
        Cmd(UiStrings.WaveMenuQuit, WaveMenuCommand.Quit, "Ctrl+Q"),
    ];

    private static IReadOnlyList<WaveMenuEntry> ExportItems(WaveformContextMenuModel m) =>
    [
        Cmd(UiStrings.WaveMenuExportWave, WaveMenuCommand.ExportWave, enabled: m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuExportMp3, WaveMenuCommand.ExportMp3, enabled: m.HasDocument && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuExportByMarkers, WaveMenuCommand.ExportByMarkers, enabled: m.HasDocument && m.HasMarkers && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuExportByRegions, WaveMenuCommand.ExportByRegions, enabled: m.HasDocument && m.HasRegions && m.AllowsRegionsAndLoops && !m.IsBusy && !m.IsRecording),
        Cmd(UiStrings.WaveMenuExportAllMp3, WaveMenuCommand.ExportAllMp3, "Ctrl+Shift+Alt+M", m.HasDocument && !m.IsBusy && !m.IsRecording),
    ];

    private static IReadOnlyList<WaveMenuEntry> HelpItems(WaveformContextMenuModel m) =>
    [
        Check(UiStrings.WaveMenuTips, WaveMenuCommand.Tips, checkedState: m.TipsVisible, enabled: true),
        Cmd(UiStrings.WaveMenuManual, WaveMenuCommand.Manual),
        Cmd(UiStrings.WaveMenuGitHub, WaveMenuCommand.GitHub),
    ];

    private static WaveMenuItemEntry Sub(string header, IReadOnlyList<WaveMenuEntry> children) =>
        new(header, Command: null, Children: children);

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
                AddItems(menuItem.Items, item.Children, onCommand);
            }
            else if (item.Command is { } command)
            {
                menuItem.Click += (_, _) => onCommand(command);
            }

            TipService.Set(menuItem, item.Gesture is null ? item.Header : $"{item.Header}\n{item.Gesture}");
            items.Add(menuItem);
        }
    }
}
