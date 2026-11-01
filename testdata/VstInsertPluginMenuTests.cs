using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstInsertPluginMenuTests
{
    [Theory]
    [InlineData("Fx|Reverb", "Fx / Reverb")]
    [InlineData("Fx|Dynamics|Compressor", "Fx / Dynamics / Compressor")]
    [InlineData("Fx", "Fx")]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("  Fx | Reverb  ", "Fx / Reverb")]
    public void FormatCategory_ReplacesPipeWithSlash(string? input, string expected) =>
        Assert.Equal(expected, VstInsertPluginMenu.FormatCategory(input));

    [Fact]
    public void GroupByVendor_OrdersVendorsAndSkipsUnrecognized()
    {
        var plugins = new[]
        {
            Entry("B Plug", "Zebra", recognized: true),
            Entry("A Plug", "Acme", recognized: true),
            Entry("Hidden", "Acme", recognized: false),
            Entry("No Vendor", "", recognized: true),
            Entry("Another Acme", "Acme", recognized: true),
        };

        var groups = VstInsertPluginMenu.GroupByVendor(plugins);
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
    public void FormatCategory_DoesNotLeaveRawPipeInDisplay()
    {
        var formatted = VstInsertPluginMenu.FormatCategory("Fx|Spatial|Stereo");
        Assert.DoesNotContain("|", formatted);
        Assert.Contains(" / ", formatted);
    }

    [Fact]
    public void BuildPickerEntries_GroupsByVendorAndKeepsNonShell()
    {
        var plugins = new[]
        {
            Entry("Soft Limit", "Apogee", recognized: true),
            Entry("Other", "Acme", recognized: true),
        };

        var groups = VstInsertRackView.BuildPickerEntries(plugins);
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, g => g.Key == "Apogee" && g.Value.Count == 1);
        Assert.Contains(groups, g => g.Key == "Acme" && g.Value.Count == 1);
        Assert.Null(groups.SelectMany(g => g.Value).First(e => e.Name == "Soft Limit").ClassId);
    }

    [Fact]
    public void BuildPickerEntries_SkipsRawWaveShellWithoutClassId()
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

        var groups = VstInsertRackView.BuildPickerEntries([shell, expanded]);
        Assert.Single(groups);
        Assert.Equal("Waves", groups[0].Key);
        Assert.Single(groups[0].Value);
        Assert.Equal("CLA-2A", groups[0].Value[0].Name);
        Assert.Equal(expanded.ClassId, groups[0].Value[0].ClassId);
    }

    private static Vst3PluginEntry Entry(string name, string vendor, bool recognized) => new()
    {
        Name = name,
        Vendor = vendor,
        Path = @"C:\VST3\" + name + ".vst3",
        Recognition = recognized
            ? Vst3RecognitionKind.Recognized
            : Vst3RecognitionKind.ManualExcluded,
        Category = "Fx|Reverb",
    };
}
