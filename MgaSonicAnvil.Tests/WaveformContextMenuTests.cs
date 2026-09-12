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
            Assert.Contains(WaveMenuCommand.FadeIn, commands);
            Assert.Contains(WaveMenuCommand.AddMarker, commands);
            Assert.Contains(WaveMenuCommand.TogglePlayback, commands);
            Assert.Contains(WaveMenuCommand.ViewSpectrogram, commands);
            Assert.Contains(WaveMenuCommand.ViewLoudness, commands);
            Assert.Contains(WaveMenuCommand.Volume, commands);
            var volume = Find(tree, WaveMenuCommand.Volume);
            var loudness = Find(tree, WaveMenuCommand.ViewLoudness);
            Assert.Equal("V", volume?.Gesture);
            Assert.Equal("V", loudness?.Gesture);
            Assert.Contains(WaveMenuCommand.ExportWave, commands);
            Assert.Contains(WaveMenuCommand.Open, commands);
            Assert.Contains(WaveMenuCommand.ClearMarkers, commands);
            Assert.Contains(WaveMenuCommand.PlayFromHere, commands);
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
}
