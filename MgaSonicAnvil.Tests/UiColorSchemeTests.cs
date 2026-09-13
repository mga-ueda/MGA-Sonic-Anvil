using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiColorSchemeTests
{
    [Fact]
    public void Write_ThenRead_RoundTripsMaps()
    {
        var json = UiColorScheme.Write(new UiColorScheme
        {
            Light = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#405273",
            },
            Dark = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#7CA7FF",
            },
        });

        Assert.Contains(UiColorScheme.FormatId, json);
        Assert.True(UiColorScheme.TryRead(json, out var scheme));
        Assert.Equal("#405273", scheme.Light!["WaveFillBrush"]);
        Assert.Equal("#7CA7FF", scheme.Dark!["WaveFillBrush"]);
    }

    [Fact]
    public void TryRead_RejectsUnknownFormat()
    {
        Assert.False(UiColorScheme.TryRead(
            """{"Format":"Nope","Light":{"WaveFillBrush":"#000000"}}""",
            out _));
    }

    [Fact]
    public void TryRead_AcceptsSettingsBags()
    {
        Assert.True(UiColorScheme.TryRead(
            """{"ColorsLight":{"LevelGradFloorBrush":"#005C8C"},"ColorsDark":{"WaveFillBrush":"#7CA7FF"}}""",
            out var scheme));
        Assert.Equal("#005C8C", scheme.Light!["LevelGradFloorBrush"]);
        Assert.Equal("#7CA7FF", scheme.Dark!["WaveFillBrush"]);
        Assert.Null(scheme.Flat);
    }

    [Fact]
    public void TryRead_AcceptsLegacyColorsBag()
    {
        Assert.True(UiColorScheme.TryRead(
            """{"Colors":{"PlayheadBrush":"#FF0000"}}""",
            out var scheme));
        Assert.Equal("#FF0000", scheme.Flat!["PlayheadBrush"]);
    }

    [Fact]
    public void TryRead_AcceptsFlatBrushMap()
    {
        Assert.True(UiColorScheme.TryRead(
            """{"WaveFillBrush":"#112233"}""",
            out var scheme));
        Assert.Equal("#112233", scheme.Flat!["WaveFillBrush"]);
        Assert.Null(scheme.Light);
        Assert.Null(scheme.Dark);
    }

    [Fact]
    public void TryRead_RejectsUnrelatedJson()
    {
        Assert.False(UiColorScheme.TryRead(
            """{"AudioApi":"Wasapi","UiTheme":"dark"}""",
            out _));
    }

    [Fact]
    public void CollectOverrides_DropsImportedDefaults()
    {
        var current = new (string Key, System.Windows.Media.Color Value)[]
        {
            ("WaveFillBrush", System.Windows.Media.Color.FromRgb(0x7C, 0xA7, 0xFF)),
            ("PlayheadBrush", System.Windows.Media.Color.FromRgb(0xFF, 0x00, 0x00)),
        };
        var defaults = new Dictionary<string, System.Windows.Media.Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["WaveFillBrush"] = System.Windows.Media.Color.FromRgb(0x7C, 0xA7, 0xFF),
            ["PlayheadBrush"] = System.Windows.Media.Color.FromRgb(0x00, 0xF5, 0xFF),
        };

        var saved = UiColors.CollectOverrides(current, key => defaults[key]);
        Assert.False(saved.ContainsKey("WaveFillBrush"));
        Assert.Equal("#FF0000", saved["PlayheadBrush"]);
    }
}
