using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstInsertPluginMenuTests
{
    [Fact]
    public void GroupPickerByVendor_OrdersVendors()
    {
        var plugins = new[]
        {
            Entry("B Plug", "Zebra", recognized: true),
            Entry("A Plug", "Acme", recognized: true),
            Entry("No Vendor", "", recognized: true),
            Entry("Another Acme", "Acme", recognized: true),
        };

        var flat = VstInsertRackView.CollectPickerEntries(plugins);
        var groups = VstInsertPluginMenu.GroupPickerByVendor(flat);
        Assert.Equal(3, groups.Count);
        Assert.Equal("Acme", groups[0].Key);
        Assert.Equal(2, groups[0].Count());
        var keys = groups.Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Acme", keys);
        Assert.Contains("Zebra", keys);
        Assert.Contains(UiStrings.LabelVstInsertVendorUnknown, keys);
    }

    [Fact]
    public void ResolveMaxHeight_FitsWorkAreaAndIsScrollable()
    {
        var max = VstInsertPluginMenu.ResolveMaxHeight();
        Assert.True(max >= 160);
        Assert.True(max <= System.Windows.SystemParameters.WorkArea.Height);
    }

    [Fact]
    public void CollectPickerEntries_KeepsNonShell()
    {
        var plugins = new[]
        {
            Entry("Soft Limit", "Apogee", recognized: true),
            Entry("Other", "Acme", recognized: true),
        };

        var flat = VstInsertRackView.CollectPickerEntries(plugins);
        Assert.Equal(2, flat.Count);
        Assert.Contains(flat, e => e.Name == "Soft Limit" && e.Vendor == "Apogee");
        Assert.Contains(flat, e => e.Name == "Other" && e.Vendor == "Acme");
        Assert.Null(flat.First(e => e.Name == "Soft Limit").ClassId);
    }

    [Fact]
    public void CollectPickerEntries_SkipsRawWaveShellWithoutClassId()
    {
        var shell = new Vst3PluginEntry
        {
            Name = "WaveShell1",
            Vendor = "Waves",
            Path = @"C:\VST3\WaveShell1-VST3 99.0_x64.vst3",
            Recognition = Vst3RecognitionKind.Recognized,
            Category = "Fx",
            ClassId = null,
        };
        var expanded = new Vst3PluginEntry
        {
            Name = "CLA-2A",
            Vendor = "Waves",
            Path = shell.Path,
            Recognition = Vst3RecognitionKind.Recognized,
            Category = "Fx|Dynamics",
            ClassId = "AAAAAAAAAAAAAAAAAAAAAA==",
        };

        var flat = VstInsertRackView.CollectPickerEntries([shell, expanded]);
        Assert.Single(flat);
        Assert.Equal("CLA-2A", flat[0].Name);
        Assert.Equal("Waves", flat[0].Vendor);
        Assert.Equal(expanded.ClassId, flat[0].ClassId);
    }

    [Fact]
    public void CollectPickerEntries_OnMonoTrack_KeepsMonoSiblingNotStereo()
    {
        var plugins = new[]
        {
            Entry("L316 Mono", "Waves", recognized: true),
            Entry("L316 Stereo", "Waves", recognized: true),
            Entry("Soft Limit", "Apogee", recognized: true),
            Entry("PS22 XSplit Mono/Stereo", "Waves", recognized: true),
        };

        var names = VstInsertRackView.CollectPickerEntries(plugins, hostChannels: 1)
            .Select(e => e.Name)
            .ToHashSet();
        Assert.Contains("L316 Mono", names);
        Assert.DoesNotContain("L316 Stereo", names);
        Assert.Contains("PS22 XSplit Mono/Stereo", names);
        Assert.Contains("Soft Limit", names);
    }

    [Fact]
    public void CollectPickerEntries_OnStereoTrack_KeepsStereoSiblingNotMono()
    {
        var plugins = new[]
        {
            Entry("CLA-2A Mono", "Waves", recognized: true),
            Entry("CLA-2A Stereo", "Waves", recognized: true),
        };

        var names = VstInsertRackView.CollectPickerEntries(plugins, hostChannels: 2)
            .Select(e => e.Name)
            .ToArray();
        Assert.Equal(["CLA-2A Stereo"], names);
    }

    [Fact]
    public void CollectPickerEntries_StereoOnlyOnMonoTrack_StillShown()
    {
        // 併記が無い Stereo 専用は残す（DAW と同様、挿してバス交渉する）。
        var plugins = new[]
        {
            Entry("Pacific Dual Tremolo", "Softube", recognized: true),
            Entry("Only Stereo", "Acme", recognized: true),
        };

        var names = VstInsertRackView.CollectPickerEntries(plugins, hostChannels: 1)
            .Select(e => e.Name)
            .ToHashSet();
        Assert.Contains("Pacific Dual Tremolo", names);
        Assert.Contains("Only Stereo", names);
    }

    [Theory]
    [InlineData(null, "vendor")]
    [InlineData("", "vendor")]
    [InlineData("vendor", "vendor")]
    [InlineData("category", "category")]
    [InlineData("CATEGORY", "category")]
    public void ParseGroupMode_ReadsSetting(string? setting, string expected) =>
        Assert.Equal(
            expected,
            VstInsertPluginMenu.ToGroupModeSetting(VstInsertPluginMenu.ParseGroupMode(setting)));

    [Fact]
    public void CategorySegments_SplitsPipe()
    {
        Assert.Equal(["Fx", "Reverb"], VstInsertPluginMenu.CategorySegments("Fx|Reverb"));
        Assert.Empty(VstInsertPluginMenu.CategorySegments(""));
        Assert.Empty(VstInsertPluginMenu.CategorySegments(null));
    }

    [Theory]
    [InlineData("Fx|Reverb", new[] { "Reverb" })]
    [InlineData("Fx|Dynamics|Compressor", new[] { "Dynamics", "Compressor" })]
    [InlineData("Fx", new[] { "Fx" })]
    [InlineData("Instrument|Synth", new[] { "Instrument", "Synth" })]
    [InlineData("OnlyARA", new[] { "OnlyARA" })]
    public void CategoryTreeSegments_SkipsLeadingFxRoot(string input, string[] expected) =>
        Assert.Equal(expected, VstInsertPluginMenu.CategoryTreeSegments(input));

    [Fact]
    public void CategoryTreePaths_SplitsMergedCategoriesIntoSeparatePlacements()
    {
        var paths = VstInsertPluginMenu.CategoryTreePaths("Fx|Reverb · Fx|Delay · Fx|EQ");
        Assert.Equal(3, paths.Count);
        Assert.Equal(["Reverb"], paths[0]);
        Assert.Equal(["Delay"], paths[1]);
        Assert.Equal(["EQ"], paths[2]);
    }

    [Fact]
    public void CategoryTreePaths_FlattensNestedPipeSegmentsIntoSiblingCategories()
    {
        var paths = VstInsertPluginMenu.CategoryTreePaths("Fx|Pitch Shift|Spatial");
        Assert.Equal(2, paths.Count);
        Assert.Equal(["Pitch Shift"], paths[0]);
        Assert.Equal(["Spatial"], paths[1]);
    }

    [Fact]
    public void CategoryTreeHierarchyPath_KeepsNestingForVendorView()
    {
        Assert.Equal(
            ["Pitch Shift", "Spatial"],
            VstInsertPluginMenu.CategoryTreeHierarchyPath("Fx|Pitch Shift|Spatial"));
        Assert.Equal(
            ["Reverb"],
            VstInsertPluginMenu.CategoryTreeHierarchyPath("Fx|Reverb · Fx|Delay"));
    }

    [Fact]
    public void CategoryTreePaths_DedupesIdenticalPathsAfterSkippingFx()
    {
        var paths = VstInsertPluginMenu.CategoryTreePaths("Fx|Reverb · Reverb");
        Assert.Single(paths);
        Assert.Equal(["Reverb"], paths[0]);
    }

    [Fact]
    public void MatchesPickerFilter_CategoryModeIgnoresVendor()
    {
        var plugin = new VstPickerEntry("EQ Soft", @"C:\a.vst3", "FabFilter", "Fx|EQ", null);
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("Fab"), VstInsertPickerGroupMode.Category));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("EQ"), VstInsertPickerGroupMode.Category));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("Soft"), VstInsertPickerGroupMode.Category));
    }

    [Fact]
    public void MatchesPickerFilter_VendorModeIgnoresCategory()
    {
        var plugin = new VstPickerEntry("Pro-Q", @"C:\a.vst3", "FabFilter", "Fx|EQ", null);
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("EQ"), VstInsertPickerGroupMode.Vendor));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("Fab"), VstInsertPickerGroupMode.Vendor));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("Pro"), VstInsertPickerGroupMode.Vendor));
    }

    [Fact]
    public void MatchesPickerFilter_UsesLibraryAndOrAcrossFields()
    {
        var plugin = new VstPickerEntry("Pro-Q 3", @"C:\a.vst3", "FabFilter", "Fx|EQ", null);
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("fab pro"), VstInsertPickerGroupMode.Vendor));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("waves|pro"), VstInsertPickerGroupMode.Vendor));
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("waves soft"), VstInsertPickerGroupMode.Vendor));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("eq soft|pro"), VstInsertPickerGroupMode.Category));
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("fab eq"), VstInsertPickerGroupMode.Category));
    }

    [Fact]
    public void MatchesPickerFilter_SupportsLibraryQuotesAndWildcards()
    {
        var plugin = new VstPickerEntry("Pro-Q 3", @"C:\a.vst3", "FabFilter", "Fx|EQ", null);
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("\"pro-q 3\""), VstInsertPickerGroupMode.Vendor));
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("\"pro q\""), VstInsertPickerGroupMode.Vendor));
        Assert.True(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("Pro*"), VstInsertPickerGroupMode.Vendor));
        Assert.False(VstInsertPluginMenu.MatchesPickerFilter(
            plugin, LibrarySearchQuery.Parse("*.wav"), VstInsertPickerGroupMode.Vendor));
    }

    [Fact]
    public void CategoryFilterLabels_UsesVisibleSegmentsNotRawFx()
    {
        Assert.Equal(["Reverb"], VstInsertPluginMenu.CategoryFilterLabels("Fx|Reverb").ToArray());
        Assert.Equal(
            [UiStrings.LabelVstInsertCategoryUnknown],
            VstInsertPluginMenu.CategoryFilterLabels(null).ToArray());
        Assert.True(TileSearchQuery.MatchesAny(
            VstInsertPluginMenu.CategoryFilterLabels("Fx|Reverb"),
            LibrarySearchQuery.Parse("rev*")));
        Assert.False(TileSearchQuery.MatchesAny(
            VstInsertPluginMenu.CategoryFilterLabels("Fx|Reverb"),
            LibrarySearchQuery.Parse("Fx")));
    }

    [Theory]
    [InlineData("L316 Mono", "L316", (int)VstChannelHint.Mono)]
    [InlineData("CLA-2A Stereo", "CLA-2A", (int)VstChannelHint.Stereo)]
    [InlineData("PS22 XSplit Mono/Stereo", "PS22 XSplit", (int)VstChannelHint.MonoOrStereo)]
    [InlineData("LoAir 5.1", "LoAir", (int)VstChannelHint.Surround51)]
    [InlineData("Soft Limit", "Soft Limit", (int)VstChannelHint.Unknown)]
    public void ChannelFit_GuessesBaseNameAndHint(string name, string baseName, int hint)
    {
        Assert.Equal(baseName, VstPluginChannelFit.BaseName(name));
        Assert.Equal((VstChannelHint)hint, VstPluginChannelFit.GuessFromName(name));
    }

    private static Vst3PluginEntry Entry(
        string name,
        string vendor,
        bool recognized,
        string category = "Fx|Reverb") => new()
    {
        Name = name,
        Vendor = vendor,
        Path = @"C:\VST3\" + name + ".vst3",
        Recognition = recognized
            ? Vst3RecognitionKind.Recognized
            : Vst3RecognitionKind.ManualExcluded,
        Category = category,
    };
}
