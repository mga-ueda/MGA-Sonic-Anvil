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
                var tree = WaveformContextMenuBuilder.Build(WaveformContextMenuModel.AllEnabledForTests());
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
                    Assert.True(keys.Count == keys.Distinct().Count(), $"{language}: {string.Join("; ", dupes)}");
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
            Assert.Contains(WaveMenuCommand.SilentSkip, commands);
            var silentSkip = Find(tree, WaveMenuCommand.SilentSkip);
            Assert.Equal("Alt+S", silentSkip?.Gesture);
            Assert.Contains(WaveMenuCommand.Volume, commands);
            var volume = Find(tree, WaveMenuCommand.Volume);
            var loudness = Find(tree, WaveMenuCommand.ViewLoudness);
            Assert.Equal("V", volume?.Gesture);
            Assert.Equal("V", loudness?.Gesture);
            Assert.Contains(WaveMenuCommand.ExportWave, commands);
            Assert.Contains(WaveMenuCommand.Open, commands);
            Assert.Contains(WaveMenuCommand.CopyAllTabTimes, commands);
            Assert.Contains(WaveMenuCommand.ClearMarkers, commands);
            Assert.Contains(WaveMenuCommand.PlayFromHere, commands);
            var root = tree.OfType<WaveMenuItemEntry>().Select(item => item.Command).ToArray();
            Assert.Contains(WaveMenuCommand.ClearMarkers, root);
            Assert.Contains(WaveMenuCommand.DeleteAllMarkers, root);
            Assert.Equal(
                Array.IndexOf(root, WaveMenuCommand.ClearMarkers) + 1,
                Array.IndexOf(root, WaveMenuCommand.DeleteAllMarkers));
            Assert.DoesNotContain(
                WaveMenuCommand.DeleteAllMarkers,
                WaveformContextMenuBuilder.Commands(
                    tree.OfType<WaveMenuItemEntry>().First(item => item.Header == UiStrings.WaveMenuCatTimeline).Children
                    ?? []).ToHashSet());
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
        Assert.DoesNotContain(WaveMenuCommand.DeleteAllMarkers, commands);
    }

    [Fact]
    public void Build_DisablesCommandsThatCannotRun()
    {
        var idle = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel());
        Assert.False(Find(idle, WaveMenuCommand.Undo)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.Cut)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.FadeIn)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.Save)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.CopyAllTabTimes)?.Enabled);
        Assert.False(Find(idle, WaveMenuCommand.LoopPlay)?.Enabled);
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
        Assert.False(Find(ready, WaveMenuCommand.PlayExit)?.Enabled);
        var waapiOn = WaveformContextMenuBuilder.Build(new WaveformContextMenuModel
        {
            HasDocument = true,
            WaapiVisible = true,
        });
        Assert.True(Find(waapiOn, WaveMenuCommand.PlayExit)?.Enabled);
        Assert.Equal("Alt+E", Find(waapiOn, WaveMenuCommand.PlayExit)?.Gesture);
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
