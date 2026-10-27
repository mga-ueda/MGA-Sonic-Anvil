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
                            // Alt+Shift ジェスチャ専用（例: Auto Active）は AccessKey なしでよい。
                            if (key is null)
                            {
                                continue;
                            }

                            keys.Add(key.Value);
                        }

                        var dupes = keys
                            .GroupBy(key => key)
                            .Where(g => g.Count() > 1)
                            .Select(g => $"{g.Key}: {string.Join(" / ", group.Where(h => MenuAccessKeys.Read(h) == g.Key))}");
                        Assert.True(keys.Count == keys.Distinct().Count(), $"{language} (selected={hasSelectedTabs}): {string.Join("; ", dupes)}");
                    }

                    Assert.Null(MenuAccessKeys.Read(UiStrings.WaveMenuAutoActive));
                }
            }
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FileSave_UsesSelectedLabels()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var single = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests());
            Assert.Equal(UiStrings.WaveMenuSave, Find(single, WaveMenuCommand.Save)?.Header);
            Assert.Equal(UiStrings.WaveMenuSaveAs, Find(single, WaveMenuCommand.SaveAs)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopyFileName, Find(single, WaveMenuCommand.CopyFileName)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopyFilePath, Find(single, WaveMenuCommand.CopyFilePath)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopySelectionTime, Find(single, WaveMenuCommand.CopySelectionTime)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopySelectionTimePath, Find(single, WaveMenuCommand.CopySelectionTimePath)?.Header);
            var selected = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests(hasSelectedTabs: true));
            Assert.Equal(UiStrings.WaveMenuSaveSelected, Find(selected, WaveMenuCommand.Save)?.Header);
            Assert.Equal(UiStrings.WaveMenuSaveAsSelected, Find(selected, WaveMenuCommand.SaveAs)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopyFileNameSelected, Find(selected, WaveMenuCommand.CopyFileName)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopyFilePathSelected, Find(selected, WaveMenuCommand.CopyFilePath)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopySelectionTimeSelected, Find(selected, WaveMenuCommand.CopySelectionTime)?.Header);
            Assert.Equal(UiStrings.WaveMenuCopySelectionTimePathSelected, Find(selected, WaveMenuCommand.CopySelectionTimePath)?.Header);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Build_MultipleSelection_GraysSingleFileOnlyCommands()
    {
        var multi = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
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
            CanAddMarkerHere = true,
            CanLoopPlay = true,
            HasSolo = true,
            CanCloseOtherTabs = true,
            CanCloseTabsToRight = true,
            CanCloseTabsToLeft = true,
            HasSelectedTabs = true,
            HasMultipleSelectedTabs = true,
            CanCopySelectionTimes = true,
            Hit = new WaveformContextHit
            {
                MarkerFrames = [0],
                Region = new WaveSelection(0, 10),
                HitLoop = true,
            },
        });

        Assert.False(Find(multi, WaveMenuCommand.PlayFromHere)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.SeekHere)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.AddMarkerHere)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.SelectSpanHere)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.FadeAround)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.Cut)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.Undo)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.Redo)?.Enabled);
        Assert.Equal(UiStrings.WaveMenuUndoSelected, Find(multi, WaveMenuCommand.Undo)?.Header);
        Assert.False(Find(multi, WaveMenuCommand.AddMarker)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.SetRegion)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.SetLoop)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.PauseHere)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.LoopPlay)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.CloseTab)?.Enabled);
        Assert.False(Find(multi, WaveMenuCommand.ClearMarkers)?.Enabled);

        Assert.True(Find(multi, WaveMenuCommand.Normalize)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.PasteHistory)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.DeleteAllMarkers)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.CopySelectionTime)?.Enabled);
        Assert.True(Find(multi, WaveMenuCommand.TogglePlayback)?.Enabled);
    }

    [Fact]
    public void Build_MenuBarOmitsHereCategory()
    {
        var tree = WaveformContextMenuBuilder.Build(
            WaveformContextMenuModel.AllEnabledForTests(),
            includeHere: false);
        Assert.DoesNotContain(
            UiStrings.WaveMenuCatHere,
            tree.OfType<WaveMenuItemEntry>().Select(item => item.Header));
        Assert.DoesNotContain(WaveMenuCommand.PlayFromHere, WaveformContextMenuBuilder.Commands(tree));
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
            Assert.Contains(UiStrings.WaveMenuCatHere, headers);
            Assert.Contains(UiStrings.WaveMenuCatEdit, headers);
            Assert.Contains(UiStrings.WaveMenuCatAudio, headers);
            Assert.Contains(UiStrings.WaveMenuCatFormat, headers);
            Assert.Contains(UiStrings.WaveMenuCatTimeline, headers);
            Assert.Contains(UiStrings.WaveMenuCatPlayback, headers);
            Assert.Contains(UiStrings.WaveMenuCatView, headers);
            Assert.Contains(UiStrings.WaveMenuCatFile, headers);
            Assert.Contains(UiStrings.WaveMenuCatTabs, headers);
            Assert.Contains(UiStrings.WaveMenuCatExport, headers);
            Assert.Contains(UiStrings.WaveMenuCatWwise, headers);
            Assert.Contains(UiStrings.WaveMenuCatOther, headers);

            var commands = WaveformContextMenuBuilder.Commands(tree).ToHashSet();
            Assert.Contains(WaveMenuCommand.Undo, commands);
            Assert.Equal(MenuGestures.Redo, Find(tree, WaveMenuCommand.Redo)?.Gesture);
            Assert.Equal(MenuGestures.Delete, Find(tree, WaveMenuCommand.Delete)?.Gesture);
            Assert.Contains(WaveMenuCommand.DetectSilence, commands);
            Assert.Contains(WaveMenuCommand.InsertSilence, commands);
            Assert.Equal("Ctrl+Ins", Find(tree, WaveMenuCommand.InsertSilence)?.Gesture);
            Assert.Equal("Shift+Delete", Find(tree, WaveMenuCommand.SilenceSelection)?.Gesture);
            Assert.Contains(WaveMenuCommand.FadeIn, commands);
            Assert.Contains(WaveMenuCommand.AddMarker, commands);
            Assert.Equal(MenuGestures.AddMarker, Find(tree, WaveMenuCommand.AddMarker)?.Gesture);
            Assert.Equal(MenuGestures.CenterPlayhead, Find(tree, WaveMenuCommand.CenterPlayhead)?.Gesture);
            Assert.Equal(MenuGestures.NextTab, Find(tree, WaveMenuCommand.NextTab)?.Gesture);
            Assert.Equal(MenuGestures.PrevTab, Find(tree, WaveMenuCommand.PrevTab)?.Gesture);
            Assert.Equal(MenuGestures.ViewWaveform, Find(tree, WaveMenuCommand.ViewWaveform)?.Gesture);
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
            Assert.Contains(WaveMenuCommand.PlaybackPitchUp, commands);
            Assert.Contains(WaveMenuCommand.PlaybackPitchDown, commands);
            Assert.Contains(WaveMenuCommand.PlaybackPitchReset, commands);
            Assert.Equal(MenuGestures.PitchUp, Find(tree, WaveMenuCommand.PlaybackPitchUp)?.Gesture);
            Assert.Equal(MenuGestures.PitchDown, Find(tree, WaveMenuCommand.PlaybackPitchDown)?.Gesture);
            Assert.Equal(MenuGestures.PitchReset, Find(tree, WaveMenuCommand.PlaybackPitchReset)?.Gesture);
            Assert.Contains(WaveMenuCommand.SilentSkip, commands);
            var silentSkip = Find(tree, WaveMenuCommand.SilentSkip);
            Assert.Equal("Alt+Shift+S", silentSkip?.Gesture);
            Assert.Contains(WaveMenuCommand.AlwaysOnTop, commands);
            var alwaysOnTop = Find(tree, WaveMenuCommand.AlwaysOnTop);
            Assert.Equal("Alt+Shift+T", alwaysOnTop?.Gesture);
            Assert.Contains(WaveMenuCommand.Volume, commands);
            var volume = Find(tree, WaveMenuCommand.Volume);
            var loudness = Find(tree, WaveMenuCommand.ViewLoudness);
            Assert.Equal(MenuGestures.Volume, volume?.Gesture);
            Assert.Equal("V", loudness?.Gesture);
            Assert.Contains(WaveMenuCommand.ExportWave, commands);
            Assert.Contains(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatExport));
            Assert.Contains(WaveMenuCommand.Channels, CommandsIn(tree, UiStrings.WaveMenuCatFormat));
            Assert.DoesNotContain(WaveMenuCommand.ExportMp3, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.RenameFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.DuplicateFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Equal("Ctrl+Shift+D", Find(tree, WaveMenuCommand.DuplicateFile)?.Gesture);
            Assert.Contains(WaveMenuCommand.MergeTabs, CommandsIn(tree, UiStrings.WaveMenuCatExport));
            Assert.Equal("Ctrl+Shift+B", Find(tree, WaveMenuCommand.MergeTabs)?.Gesture);
            Assert.Contains(WaveMenuCommand.DeleteFile, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.CloseTab, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.CloseOthers, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.SelectAllTabs, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.CopyAllTabTimes, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.Settings, CommandsIn(tree, UiStrings.WaveMenuCatOther));
            Assert.Contains(WaveMenuCommand.ColorPanel, CommandsIn(tree, UiStrings.WaveMenuCatOther));
            Assert.Contains(WaveMenuCommand.License, CommandsIn(tree, UiStrings.WaveMenuCatOther));
            Assert.Contains(WaveMenuCommand.About, CommandsIn(tree, UiStrings.WaveMenuCatOther));
            Assert.Contains(WaveMenuCommand.TimeZoomIn, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Contains(WaveMenuCommand.SilentSkip, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Contains(WaveMenuCommand.AlwaysOnTop, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Equal("Ctrl+Shift+C", Find(tree, WaveMenuCommand.ColorPanel)?.Gesture);
            Assert.Contains(WaveMenuCommand.RangeClick, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Equal("Q", Find(tree, WaveMenuCommand.RangeClick)?.Gesture);
            Assert.True(Find(tree, WaveMenuCommand.RangeClick)?.Checkable);
            Assert.Contains(WaveMenuCommand.Shuffle, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Equal("Alt+Shift+R", Find(tree, WaveMenuCommand.Shuffle)?.Gesture);
            Assert.Contains(WaveMenuCommand.AutoPlayNext, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Equal("Alt+Shift+N", Find(tree, WaveMenuCommand.AutoPlayNext)?.Gesture);
            Assert.Contains(WaveMenuCommand.AutoPlay, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Equal("Alt+Shift+P", Find(tree, WaveMenuCommand.AutoPlay)?.Gesture);
            Assert.Contains(WaveMenuCommand.CheckSelectMode, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.Equal("Shift+Space", Find(tree, WaveMenuCommand.CheckSelectMode)?.Gesture);
            Assert.True(Find(tree, WaveMenuCommand.CheckSelectMode)?.Checkable);
            var view = Category(tree, UiStrings.WaveMenuCatView).Children!;
            Assert.Contains(UiStrings.WaveMenuCatAnalysis, view.OfType<WaveMenuItemEntry>().Select(item => item.Header));
            Assert.Contains(UiStrings.WaveMenuCatScreen, view.OfType<WaveMenuItemEntry>().Select(item => item.Header));
            Assert.Contains(UiStrings.WaveMenuCatVideo, view.OfType<WaveMenuItemEntry>().Select(item => item.Header));
            Assert.Contains(UiStrings.WaveMenuCatSolo, view.OfType<WaveMenuItemEntry>().Select(item => item.Header));
            Assert.Contains(WaveMenuCommand.ViewSpectrogram, CommandsIn(view, UiStrings.WaveMenuCatAnalysis));
            Assert.Contains(WaveMenuCommand.VideoMini, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Equal("F8", Find(tree, WaveMenuCommand.VideoMini)?.Gesture);
            Assert.Contains(WaveMenuCommand.MinimalChrome, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Equal("F9", Find(tree, WaveMenuCommand.MinimalChrome)?.Gesture);
            Assert.Contains(WaveMenuCommand.FindTabs, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.Contains(WaveMenuCommand.LaunchWwise, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Equal("Ctrl+Alt+Shift+W", Find(tree, WaveMenuCommand.LaunchWwise)?.Gesture);
            Assert.Contains(WaveMenuCommand.WaapiPanel, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Equal("Alt+Shift+W", Find(tree, WaveMenuCommand.WaapiPanel)?.Gesture);
            Assert.Contains(WaveMenuCommand.KeepTarget, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Contains(WaveMenuCommand.AutoActive, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Contains(WaveMenuCommand.PlayExit, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Contains(WaveMenuCommand.WwiseExport, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.Contains(WaveMenuCommand.OutputFolder, CommandsIn(tree, UiStrings.WaveMenuCatWwise));
            Assert.DoesNotContain(WaveMenuCommand.CloseTab, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.MergeTabs, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.MergeTabs, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.DoesNotContain(WaveMenuCommand.Settings, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.WaapiPanel, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.WaapiPanel, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.DoesNotContain(WaveMenuCommand.PlayExit, CommandsIn(tree, UiStrings.WaveMenuCatPlayback));
            Assert.DoesNotContain(WaveMenuCommand.WwiseExport, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.DoesNotContain(WaveMenuCommand.CopyAllTabTimes, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.DoesNotContain(WaveMenuCommand.Settings, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.DoesNotContain(WaveMenuCommand.TimeZoomIn, CommandsIn(tree, UiStrings.WaveMenuCatTimeline));
            Assert.True(Find(tree, WaveMenuCommand.CloseOthers)?.Enabled);
            Assert.True(Find(tree, WaveMenuCommand.CloseTabsRight)?.Enabled);
            Assert.True(Find(tree, WaveMenuCommand.CloseTabsLeft)?.Enabled);
            Assert.DoesNotContain(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatFormat));
            Assert.DoesNotContain(WaveMenuCommand.ExportByChannels, CommandsIn(tree, UiStrings.WaveMenuCatFile));
            Assert.Contains(WaveMenuCommand.Open, commands);
            Assert.Contains(WaveMenuCommand.CloseOthers, commands);
            Assert.Contains(WaveMenuCommand.CloseTabsRight, commands);
            Assert.Contains(WaveMenuCommand.CloseTabsLeft, commands);
            Assert.Contains(WaveMenuCommand.CopyAllTabTimes, commands);
            Assert.Contains(WaveMenuCommand.ExportMp3, CommandsIn(tree, UiStrings.WaveMenuCatExport));
            Assert.Equal("Ctrl+Shift+M", Find(tree, WaveMenuCommand.ExportMp3)?.Gesture);
            Assert.Contains(WaveMenuCommand.TileOff, CommandsIn(tree, UiStrings.WaveMenuCatTabs));
            Assert.DoesNotContain(WaveMenuCommand.TileOff, CommandsIn(tree, UiStrings.WaveMenuCatView));
            Assert.Contains(WaveMenuCommand.ClearMarkers, CommandsIn(tree, UiStrings.WaveMenuCatHere));
            Assert.Contains(WaveMenuCommand.PlayFromHere, CommandsIn(tree, UiStrings.WaveMenuCatHere));
            Assert.Contains(WaveMenuCommand.CopySelectionTime, CommandsIn(tree, UiStrings.WaveMenuCatHere));
            Assert.Contains(WaveMenuCommand.CopySelectionTimePath, CommandsIn(tree, UiStrings.WaveMenuCatHere));
            var root = tree.OfType<WaveMenuItemEntry>().Select(item => item.Command).ToArray();
            Assert.All(root, command => Assert.Null(command));
            Assert.DoesNotContain(WaveMenuCommand.CopySelectionTime, CommandsIn(tree, UiStrings.WaveMenuCatEdit));
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
        Assert.DoesNotContain(UiStrings.WaveMenuCatHere, tree.OfType<WaveMenuItemEntry>().Select(item => item.Header));
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
        Assert.False(Find(idle, WaveMenuCommand.CopyFileName)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CopyFilePath)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.MergeTabs)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.DeleteFile)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CopyAllTabTimes)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseOthers)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseTabsRight)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CloseTabsLeft)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.ExportMp3)?.Enabled);
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
        Assert.True(Find(ready, WaveMenuCommand.CopyFileName)?.Enabled);
        Assert.True(Find(ready, WaveMenuCommand.CopyFilePath)?.Enabled);
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
        Assert.Equal("Alt+Shift+E", Find(waapiOn, WaveMenuCommand.PlayExit)?.Gesture);
        Assert.True(Find(waapiOn, WaveMenuCommand.AutoActive)?.Enabled);
        Assert.Equal("Alt+Shift+A", Find(waapiOn, WaveMenuCommand.AutoActive)?.Gesture);
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
        Assert.True(Find(tree, WaveMenuCommand.FadeIn)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.FadeOut)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.InsertSilence)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.DetectSilence)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.FadeAround)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.AddMarker)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.DeleteMarkers)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.DeleteAllMarkers)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TimeZoomIn)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.Record)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.NewDocument)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.RenameFile)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.CopyFileName)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.CopyFilePath)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.DuplicateFile)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.DeleteFile)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.TileGrid)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.SoloNext)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.ExportWave)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Volume)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Normalize)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Pitch)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.TimeStretch)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Reverse)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.SampleRate)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.BitDepth)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.Channels)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.ExportByChannels)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.DeleteAllRegions)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.ClearLoopItem)?.Enabled);
        Assert.True(Find(tree, WaveMenuCommand.ExportMp3)?.Enabled);
        Assert.Equal("Ctrl+Shift+M", Find(tree, WaveMenuCommand.ExportMp3)?.Gesture);
        // 破壊編集はメニューから実行可だが、ショートカット表記は出さない（音量は Ctrl+Shift+V のみ残す）。
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.FadeIn)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.FadeOut)?.Gesture));
        Assert.Equal(MenuGestures.VolumePlayer, Find(tree, WaveMenuCommand.Volume)?.Gesture);
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.Normalize)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.Pitch)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.TimeStretch)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.Reverse)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.SampleRate)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.BitDepth)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.Channels)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.InsertSilence)?.Gesture));
        Assert.True(string.IsNullOrEmpty(Find(tree, WaveMenuCommand.DetectSilence)?.Gesture));
        Assert.Equal("Ctrl+Shift+B", Find(tree, WaveMenuCommand.MergeTabs)?.Gesture);
        var mp3Only = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            LibraryMaximized = true,
            CanExportMp3 = false,
        });
        Assert.False(Find(mp3Only, WaveMenuCommand.ExportMp3)?.Enabled);
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
        Assert.False(Find(tree, WaveMenuCommand.NextTab)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.PrevTab)?.Enabled);
        Assert.False(Find(tree, WaveMenuCommand.ReopenTab)?.Enabled);
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
    public void Build_DisablesCategoryWhenNoChildCanRun()
    {
        var idle = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel());
        Assert.False(Category(idle, UiStrings.WaveMenuCatEdit).Enabled);
        Assert.False(Category(idle, UiStrings.WaveMenuCatAudio).Enabled);
        Assert.False(Category(idle, UiStrings.WaveMenuCatFormat).Enabled);
        Assert.False(Category(idle, UiStrings.WaveMenuCatTimeline).Enabled);
        Assert.True(Category(idle, UiStrings.WaveMenuCatPlayback).Enabled);
        Assert.False(Category(idle, UiStrings.WaveMenuCatTabs).Enabled);
        Assert.False(Category(idle, UiStrings.WaveMenuCatExport).Enabled);
        Assert.True(Category(idle, UiStrings.WaveMenuCatFile).Enabled);
        Assert.True(Category(idle, UiStrings.WaveMenuCatView).Enabled);
        Assert.True(Category(idle, UiStrings.WaveMenuCatWwise).Enabled);
        Assert.True(Category(idle, UiStrings.WaveMenuCatOther).Enabled);

        var ready = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests());
        Assert.All(
            ready.OfType<WaveMenuItemEntry>().Where(item => item.Children is { Count: > 0 }),
            item => Assert.True(item.Enabled));

        var player = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            CanNavigate = true,
            LibraryMaximized = true,
            HasMarkers = true,
            CanExportMp3 = true,
        });
        Assert.True(Category(player, UiStrings.WaveMenuCatFormat).Enabled);
        Assert.False(Category(player, UiStrings.WaveMenuCatWwise).Enabled);
        Assert.True(Category(player, UiStrings.WaveMenuCatTabs).Enabled);
        Assert.True(Category(player, UiStrings.WaveMenuCatAudio).Enabled);
        Assert.True(Category(player, UiStrings.WaveMenuCatTimeline).Enabled);
        Assert.True(Category(player, UiStrings.WaveMenuCatExport).Enabled);
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
            var leaf = headers
                .SelectMany(item => item.Items.OfType<MenuItem>())
                .First(item => !item.HasItems);
            Assert.False(leaf.StaysOpenOnClick);
        });
    }

    [Fact]
    public void Populate_FillsMenuFromSameBuildAsContextMenu()
    {
        RunSta(() =>
        {
            var tree = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests());
            var context = WaveformContextMenuBuilder.Create(tree, _ => { }, new Border());
            var bar = new Menu();
            WaveformContextMenuBuilder.Populate(bar.Items, tree, _ => { });
            Assert.Equal(context.Items.Count, bar.Items.Count);
            var contextHeaders = context.Items.OfType<MenuItem>().Select(item => item.Header).ToArray();
            var barHeaders = bar.Items.OfType<MenuItem>().Select(item => item.Header).ToArray();
            Assert.Equal(contextHeaders, barHeaders);
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

    [Fact]
    public void MenuAccessKeys_FromKey_MapsLettersOnly()
    {
        Assert.Equal('F', MenuAccessKeys.FromKey(Key.F));
        Assert.Equal('A', MenuAccessKeys.FromKey(Key.A));
        Assert.Equal('Z', MenuAccessKeys.FromKey(Key.Z));
        Assert.Null(MenuAccessKeys.FromKey(Key.Enter));
        Assert.Null(MenuAccessKeys.FromKey(Key.D1));
        Assert.Null(MenuAccessKeys.FromKey(Key.F10));
    }

    [Fact]
    public void TopLevelMenuAccessKeys_DocumentAltShortcutConflicts()
    {
        // メニューバー表示時は Alt+文字がルートメニューを開く。
        // アプリ側トグルは Alt+Shift＋文字なのでルートと衝突しない。
        var previous = UiStrings.Language;
        try
        {
            foreach (var language in new[] { UiLanguage.Japanese, UiLanguage.English })
            {
                UiStrings.SetLanguage(language);
                var roots = WaveformContextMenuBuilder.Build(
                        WaveformContextMenuModel.AllEnabledForTests(),
                        includeHere: false)
                    .OfType<WaveMenuItemEntry>()
                    .Select(item => (item.Header, Key: MenuAccessKeys.Read(item.Header)))
                    .ToList();
                Assert.All(roots, r => Assert.True(r.Key is not null, r.Header));

                var byKey = roots.ToDictionary(r => r.Key!.Value, r => r.Header);
                Assert.Equal(UiStrings.WaveMenuCatFile, byKey['F']);
                Assert.Equal(UiStrings.WaveMenuCatEdit, byKey['E']);
                Assert.Equal(UiStrings.WaveMenuCatFormat, byKey['N']);
                Assert.Equal(UiStrings.WaveMenuCatExport, byKey['X']);
                Assert.Equal(UiStrings.WaveMenuCatView, byKey['V']);
                Assert.Equal(UiStrings.WaveMenuCatTimeline, byKey['T']);
                Assert.Equal(UiStrings.WaveMenuCatTabs, byKey['B']);
                Assert.Equal(UiStrings.WaveMenuCatPlayback, byKey['P']);
                Assert.Equal(UiStrings.WaveMenuCatAudio, byKey['A']);
                Assert.Equal(UiStrings.WaveMenuCatWwise, byKey['W']);
                Assert.Equal(UiStrings.WaveMenuCatOther, byKey['O']);

                // アプリ側トグルは Alt+Shift＋文字。ルートの Alt＋文字はメニュー専用で衝突しない。
                Assert.Contains('E', byKey.Keys); // 編集（アプリは Alt+Shift+E）
                Assert.Contains('W', byKey.Keys); // Wwise（アプリは Alt+Shift+W）
                Assert.Contains('T', byKey.Keys); // タイムライン（アプリは Alt+Shift+T）
                Assert.Contains('B', byKey.Keys); // タブ（アプリは Alt+Shift+B）
                Assert.Contains('N', byKey.Keys); // 変換（アプリは Alt+Shift+N）
                Assert.Contains('P', byKey.Keys); // 再生（アプリは Alt+Shift+P）
                Assert.Contains('F', byKey.Keys);
                Assert.Contains('V', byKey.Keys);
                Assert.Contains('X', byKey.Keys);
                Assert.Contains('A', byKey.Keys);
                Assert.Contains('O', byKey.Keys);
            }
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    private static WaveMenuItemEntry Category(IReadOnlyList<WaveMenuEntry> entries, string header) =>
        entries.OfType<WaveMenuItemEntry>().First(item => item.Header == header);

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
