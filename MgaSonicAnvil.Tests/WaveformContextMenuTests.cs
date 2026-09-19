using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Input;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformContextMenuTests
{
    [Fact]
    public void AccessKeys_ArePresentAndUnique_InBothLanguages()
    {
        var previous = UiStrings.Language;
        try
        {
            foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
            {
                UiStrings.SetLanguage(language);
                foreach (var hasSelectedTabs in new[] { false, true })
                {
                    var tree = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests(hasSelectedTabs));
                    foreach (var group in WaveformContextMenuBuilder.AccessKeyGroups(tree))
                    {
                        var keys = new List<char>();
                        foreach (var header in group)
                        {
                            var key = MenuAccessKeys.Read(header);
                            Assert.True(key is not null, $"{language}: missing access key: {header}");
                            keys.Add(key.Value);
                        }

                        var dupes = keys
                            .GroupBy(key => key)
                            .Where(g => g.Count() > 1)
                            .Select(g => $"{g.Key}: {string.Join(" / ", group.Where(h => MenuAccessKeys.Read(h) == g.Key))}");
                        Assert.True(keys.Count == keys.Distinct().Count(), $"{language} (selected={hasSelectedTabs}): {string.Join("; ", dupes)}");
                    }
                }
            }
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Build_IncludesCategoriesAndCoreCommands()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var tree = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests());
            var headers = tree.OfType<WaveMenuItemEntry>().Select(item => item.Header).ToArray();
            Assert.Contains(UiStrings.WaveMenuCatEdit, headers);
            Assert.Contains(UiStrings.WaveMenuCatAudio, headers);
            Assert.Contains(UiStrings.WaveMenuCatFormat, headers);
            Assert.Contains(UiStrings.WaveMenuCatTimeline, headers);
            Assert.Contains(UiStrings.WaveMenuCatPlayback, headers);
            Assert.Contains(UiStrings.WaveMenuCatView, headers);
            Assert.Contains(UiStrings.WaveMenuCatFile, headers);
            Assert.Contains(UiStrings.WaveMenuCatTabs, headers);
            Assert.Contains(UiStrings.WaveMenuCatExport, headers);
            Assert.Contains(UiStrings.WaveMenuCatHelp, headers);

            var commands = WaveformContextMenuBuilder.Commands(tree).ToHashSet();
            Assert.Contains(WaveMenuCommand.Undo, commands);
            Assert.Contains(WaveMenuCommand.DeleteSilence, commands);
            Assert.Contains(WaveMenuCommand.FadeIn, commands);
            Assert.Contains(WaveMenuCommand.AddMarker, commands);
            Assert.Contains(WaveMenuCommand.DeleteAllMarkers, commands);
            Assert.Contains(WaveMenuCommand.DeleteAllRegions, commands);
            Assert.Contains(WaveMenuCommand.TogglePlayback, commands);
            Assert.Contains(WaveMenuCommand.ViewSpectrogram, commands);
            Assert.Contains(WaveMenuCommand.ViewLoudness, commands);
            Assert.Contains(WaveMenuCommand.TileOff, commands);
            Assert.Contains(WaveMenuCommand.TileHorizontal, commands);
            Assert.Contains(WaveMenuCommand.TileVertical, commands);
            Assert.Contains(WaveMenuCommand.TileGrid, commands);
            Assert.Contains(WaveMenuCommand.MaximizeWaveform, commands);
            var maximize = Find(tree, WaveMenuCommand.MaximizeWaveform);
            Assert.True(maximize?.Checkable);
            Assert.False(maximize?.Checked);
            Assert.Equal("F11", maximize?.Gesture);
            Assert.Contains(WaveMenuCommand.MaximizeAnalyzers, commands);
            var analyzers = Find(tree, WaveMenuCommand.MaximizeAnalyzers);
            Assert.True(analyzers?.Checkable);
            Assert.False(analyzers?.Checked);
            Assert.Equal("F12", analyzers?.Gesture);
            Assert.Contains(WaveMenuCommand.MaximizeLibrary, commands);
            var library = Find(tree, WaveMenuCommand.MaximizeLibrary);
            Assert.True(library?.Checkable);
            Assert.False(library?.Checked);
            Assert.Equal("F10", library?.Gesture);
            var tileGrid = Find(tree, WaveMenuCommand.TileGrid);
            Assert.True(tileGrid?.Checkable);
            Assert.True(tileGrid?.Checked);
            var tileOff = Find(tree, WaveMenuCommand.TileOff);
            Assert.True(tileOff?.Checkable);
            Assert.False(tileOff?.Checked);
            Assert.Contains(WaveMenuCommand.SilentSkip, commands);
            var silentSkip = Find(tree, WaveMenuCommand.SilentSkip);
            Assert.Equal("Alt+S", silentSkip?.Gesture);
            Assert.Contains(WaveMenuCommand.Volume, commands);
            var volume = Find(tree, WaveMenuCommand.Volume);
            var loudness = Find(tree, WaveMenuCommand.ViewLoudness);
            Assert.Equal("V", volume?.Gesture);
            Assert.Equal("V", loudness?.Gesture);
            Assert.Contains(WaveMenuCommand.ExportWave, commands);
            Assert.Contains(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatExport));
            Assert.Contains(WaveMenuCommand.Channels, CommandsIn(tree, UiStrings.WaveMenuCatFormat));
            Assert.Contains(WaveMenuCommand.SaveMp3, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.RenameFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.DuplicateFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Equal("Ctrl+Shift+D", Find(tree, WaveMenuCommand.DuplicateFile)?.Gesture);
            Assert.Contains(WaveMenuCommand.MergeTabs, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Equal("Ctrl+Shift+B", Find(tree, WaveMenuCommand.MergeTabs)?.Gesture);
            Assert.Contains(WaveMenuCommand.DeleteFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.CloseTab, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.CloseOthers, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.SelectAllTabs, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.CopyAllTabTimes, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.DoesNotContain(WaveMenuCommand.CloseTab, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.MergeTabs, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.True(Find(tree, WaveMenuCommand.CloseOthers)?.Enabled);
            Assert.True(Find(tree, WaveMenuCommand.CloseTabsRight)?.Enabled);
            Assert.True(Find(tree, WaveMenuCommand.CloseTabsLeft)?.Enabled);
            Assert.Contains(WaveMenuCommand.WwiseExport, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatFormat));
            Assert.DoesNotContain(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.Open, commands);
            Assert.Contains(WaveMenuCommand.CloseOthers, commands);
            Assert.Contains(WaveMenuCommand.CloseTabsRight, commands);
            Assert.Contains(WaveMenuCommand.CloseTabsLeft, commands);
            Assert.Contains(WaveMenuCommand.CopyAllTabTimes, commands);
            Assert.Contains(WaveMenuCommand.ExportAllWave, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.ExportAllMp3, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.DoesNotContain(WaveMenuCommand.ExportAllWave, CommandsIn(tree, UiStrings.WaveMenuCatExport));
            Assert.Contains(WaveMenuCommand.TileOff, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.DoesNotContain(WaveMenuCommand.TileOff, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Contains(WaveMenuCommand.ClearMarkers, commands);
            Assert.Contains(WaveMenuCommand.PlayFromHere, commands);
            var root = tree.OfType<WaveMenuItemEntry>().Select(item => item.Command).ToArray();
            Assert.Contains(WaveMenuCommand.ClearMarkers, root);
            Assert.DoesNotContain(WaveMenuCommand.DeleteAllMarkers, root);
            Assert.Contains(WaveMenuCommand.DeleteAllMarkers, CommandsIn(tree, UiStrings.WaveMenuCatTimeline));
            Assert.Contains(WaveMenuCommand.DeleteAllRegions, CommandsIn(tree, UiStrings.WaveMenuCatTimeline));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Build_WithoutDocument_KeepsFileAndHelp()
    {
        var tree = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel());
        var commands = WaveformContextMenuBuilder.Commands(tree).ToHashSet();
        Assert.Contains(WaveMenuCommand.Open, commands);
        Assert.Contains(WaveMenuCommand.Manual, commands);
        Assert.DoesNotContain(WaveMenuCommand.PlayFromHere, commands);
        Assert.DoesNotContain(WaveMenuCommand.ClearMarkers, commands);
        Assert.False(Find(tree, WaveMenuCommand.DeleteAllMarkers)?.Enabled);
    }

    [Fact]
    public void Build_DisablesCommandsThatCannotRun()
    {
        var idle = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel());
        Assert.False(Find(idle, WaveMenuCommand.Undo)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.Cut)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.FadeIn)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.Save)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.MergeTabs)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.DeleteFile)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CopyAllTabTimes)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseOthers)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseTabsRight)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseTabsLeft)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.ExportAllWave)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.LoopPlay)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.TileHorizontal)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.TileGrid)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.Record)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.NormalizePerRegion)?.Enabled);
        Assert.True(Find(idle, WaveMenuCommand.Open)?.Enabled);
        Assert.True(Find(idle, WaveMenuCommand.Quit)?.Enabled);
        Assert.True(Find(idle, WaveMenuCommand.Manual)?.Enabled);

        var ready = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanEdit = true,
            CanNavigate = true,
            CanAddMarkerHere = true,
        });
        Assert.False(Find(ready, WaveMenuCommand.Undo)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.Cut)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.LoopPlay)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.DeleteAllMarkers)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.DeleteAllRegions)?.Enabled);
        var withMarkers = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanEdit = true,
            HasMarkers = true,
        });
        Assert.True(Find(withMarkers, WaveMenuCommand.DeleteAllMarkers)?.Enabled);
        Assert.Null(Find(withMarkers, WaveMenuCommand.ClearMarkers));
        Assert.True(Find(ready, WaveMenuCommand.CopyAllTabTimes)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.CloseOthers)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.FadeIn)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.AddMarkerHere)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.Record)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.NormalizePerRegion)?.Enabled);
        var withRegions = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanEdit = true,
            HasRegions = true,
            AllowsRegionsAndLoops = true,
        });
        Assert.True(Find(withRegions, WaveMenuCommand.NormalizePerRegion)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.Save)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.MergeTabs)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.DeleteFile)?.Enabled);
        var canMerge = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanMergeTabs = true,
        });
        Assert.True(Find(canMerge, WaveMenuCommand.MergeTabs)?.Enabled);
        Assert.False(Find(ready, WaveMenuCommand.PlayExit)?.Enabled);
        var recording = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            IsRecording = true,
        });
        Assert.False(Find(recording, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.False(Find(recording, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(recording, WaveMenuCommand.MergeTabs)?.Enabled);
        Assert.False(Find(recording, WaveMenuCommand.DeleteFile)?.Enabled);
        var waapiOn = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            WaapiVisible = true,
        });
        Assert.True(Find(waapiOn, WaveMenuCommand.PlayExit)?.Enabled);
        Assert.Equal("Alt+E", Find(waapiOn, WaveMenuCommand.PlayExit)?.Gesture);
    }

    [Fact]
    public void Build_LibraryMaximized_GraysEditAndKeepsPlayback()
    {
        var tree = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanEdit = false,
            CanNavigate = true,
            LibraryMaximized = true,
            WaapiVisible = true,
            WaapiExportEnabled = true,
            HasMultipleTabs = true,
            CanTileHorizontal = true,
            CanTileVertical = true,
            CanTileGrid = true,
            HasSelection = true,
            HasMarkers = true,
            AllowsRegionsAndLoops = true,
        });
        Assert.False(Find(tree, WaveMenuCommand.Undo)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.FadeIn)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.AddMarker)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TimeZoomIn)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.Record)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.NewDocument)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.DeleteFile)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TileGrid)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.SoloNext)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.ExportWave)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.FocusTime)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.TogglePlayback)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.SelectAll)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Open)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Save)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.MaximizeLibrary)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.SilentSkip)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.GoStart)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.ViewWaveform)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.ViewSpectrogram)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.ViewOverlay)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.ViewLoudness)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.WaapiPanel)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.WwiseExport)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.PlayExit)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.CloseTab)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.CloseAll)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.CloseOthers)?.Enabled);
    }

    [Fact]
    public void Build_DisablesTileArrangesThatDoNotFit()
    {
        var tree = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasMultipleTabs = true,
            CanTileHorizontal = false,
            CanTileVertical = true,
            CanTileGrid = false,
        });
        Assert.True(Find(tree, WaveMenuCommand.TileOff)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TileHorizontal)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.TileVertical)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TileGrid)?.Enabled);

        var current = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasMultipleTabs = true,
            WaveTileArrange = WaveformTileArrange.Horizontal,
            CanTileHorizontal = false,
            CanTileVertical = false,
            CanTileGrid = false,
        });
        Assert.True(Find(current, WaveMenuCommand.TileHorizontal)?.Enabled);
        Assert.True(Find(current, WaveMenuCommand.TileHorizontal)?.Checked);
        Assert.False(Find(current, WaveMenuCommand.TileVertical)?.Enabled);
    }

    [Fact]
    public void Create_KeepsSubmenuOpenOnHeaderClick()
    {
        RunSta(() =>
        {
            var menu = WaveformContextMenuBuilder.Create(
                WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests()),
                _ => { },
                new Border());
            var headers = menu.Items.OfType<MenuItem>().Where(item => item.HasItems).ToArray();
            Assert.NotEmpty(headers);
            Assert.All(headers, item => Assert.True(item.StaysOpenOnClick));
            var leaf = menu.Items.OfType<MenuItem>().First(item => !item.HasItems);
            Assert.False(leaf.StaysOpenOnClick);
        });
    }

    [Fact]
    public void MenuAccessKeys_IsContextMenuKey_MatchesAppsAndShiftF10()
    {
        Assert.True(MenuAccessKeys.IsContextMenuKey(Key.Apps, ModifierKeys.None));
        Assert.True(MenuAccessKeys.IsContextMenuKey(Key.F10, ModifierKeys.Shift));
        Assert.False(MenuAccessKeys.IsContextMenuKey(Key.F10, ModifierKeys.None));
        Assert.False(MenuAccessKeys.IsContextMenuKey(Key.Apps, ModifierKeys.Control));
        Assert.False(MenuAccessKeys.IsContextMenuKey(Key.Delete, ModifierKeys.None));
    }

    [Fact]
    public void MenuAccessKeys_ReadsPrefixAndSuffix()
    {
        Assert.Equal('E', MenuAccessKeys.Read("編集(_E)"));
        Assert.Equal('W', MenuAccessKeys.Read("Export _Wave"));
        Assert.Equal('P', MenuAccessKeys.Read("Stop to Start (_P)"));
        Assert.Null(MenuAccessKeys.Read("No key"));
    }

    private static HashSet<WaveMenuCommand> CommandsIn(IReadOnlyList<WaveMenuEntry> entries, string category)
    {
        var children = entries.OfType<WaveMenuItemEntry>()
            .First(item => item.Header == category)
            .Children ?? [];
        return WaveformContextMenuBuilder.Commands(children).ToHashSet();
    }

    private static WaveMenuItemEntry? Find(IReadOnlyList<WaveMenuEntry> entries, WaveMenuCommand command)
    {
        foreach (var entry in entries)
        {
            if (entry is not WaveMenuItemEntry item)
            {
                continue;
            }

            if (item.Command == command)
            {
                return item;
            }

            if (item.Children is { Count: > 0 } children
                && Find(children, command) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
