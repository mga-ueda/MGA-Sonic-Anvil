using System.Text.Json;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class FileNameColorRulesTests
{
    [Fact]
    public void Palette_HasTwentyEightFlatWwiseColors()
    {
        Assert.Equal(28, FileNameColorPalette.Count);
        Assert.Equal(28, FileNameColorPalette.Colors.Length);
        Assert.Equal(28, FileNameColorPalette.Colors.ToArray().Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.True(FileNameColorPalette.TryRgb(0, out var r, out var g, out var b));
        Assert.Equal((82, 82, 82), (r, g, b));
        Assert.True(FileNameColorPalette.TryRgb(10, out r, out g, out b));
        Assert.Equal((169, 45, 45), (r, g, b));
        Assert.Equal("8462D5", FileNameColorPalette.Colors[27]);
        Assert.False(FileNameColorPalette.TryRgb(28, out _, out _, out _));
    }

    [Fact]
    public void Match_UsesFirstContainingPattern_IgnoringCase()
    {
        var rules = new FileNameColorRule[]
        {
            new() { Pattern = "_ui_", ColorIndex = 2 },
            new() { Pattern = "_bt_", ColorIndex = 10 },
            new() { Pattern = "BT", ColorIndex = 11 },
        };

        Assert.Equal(10, FileNameColorRules.Match("gun_bt_fire.wav", rules));
        Assert.Equal(10, FileNameColorRules.Match("GUN_BT_FIRE.WAV", rules));
        Assert.Equal(2, FileNameColorRules.Match("menu_ui_open.wav", rules));
        Assert.Equal(-1, FileNameColorRules.Match("ambience.wav", rules));
        Assert.Equal(-1, FileNameColorRules.Match("", rules));
        Assert.Equal(-1, FileNameColorRules.Match("gun_bt_fire.wav", null));
    }

    [Fact]
    public void Normalize_DropsEmptyAndOutOfRange_WithoutCountCap()
    {
        var rules = new List<FileNameColorRule>
        {
            new() { Pattern = "  ", ColorIndex = 1 },
            new() { Pattern = " _bt_ ", ColorIndex = 10 },
            new() { Pattern = "bad", ColorIndex = 28 },
            new() { Pattern = "ok", ColorIndex = -1 },
        };
        for (var i = 0; i < 30; i++)
        {
            rules.Add(new FileNameColorRule { Pattern = "p" + i, ColorIndex = i % 28 });
        }

        var normalized = FileNameColorRules.Normalize(rules);
        Assert.Equal(31, normalized.Length);
        Assert.Equal("_bt_", normalized[0].Pattern);
        Assert.Equal(10, normalized[0].ColorIndex);
        Assert.Equal("p0", normalized[1].Pattern);
        Assert.Equal("p29", normalized[^1].Pattern);
        Assert.DoesNotContain(normalized, rule => rule.Pattern == "bad");
    }

    [Fact]
    public void Settings_RoundTrip_KeepsRules()
    {
        var settings = AppSettings.CreateDefault();
        settings.FileNameColorRules =
        [
            new FileNameColorRule { Pattern = "_bt_", ColorIndex = 10 },
        ];

        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var loaded = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);

        Assert.NotNull(loaded);
        Assert.Single(loaded.FileNameColorRules);
        Assert.Equal("_bt_", loaded.FileNameColorRules[0].Pattern);
        Assert.Equal(10, loaded.FileNameColorRules[0].ColorIndex);
    }

    [Fact]
    public void ManualAssignment_OverridesFilenameRule_AndRetargetsOnRename()
    {
        var rules = new FileNameColorRule[] { new() { Pattern = "_bt_", ColorIndex = 2 } };
        var manual = FileNameColorAssignments.With(null, [@"D:\sfx\gun_bt_fire.wav"], 10);

        Assert.True(FileNameColorResolve.TryResolve(
            "gun_bt_fire.wav",
            @"D:\sfx\gun_bt_fire.wav",
            rules,
            manual,
            out var color,
            out var pattern));
        Assert.Equal(10, color);
        Assert.Equal("#A92D2D", pattern);

        Assert.True(FileNameColorResolve.TryResolve(
            "gun_bt_fire.wav",
            @"D:\sfx\other.wav",
            rules,
            manual,
            out color,
            out pattern));
        Assert.Equal(2, color);
        Assert.Equal("_bt_", pattern);

        Assert.True(FileNameColorAssignments.TryRetarget(
            manual,
            @"d:\sfx\gun_bt_fire.wav",
            @"D:\sfx\gun_fire.wav",
            out var moved));
        Assert.False(FileNameColorAssignments.TryGet(moved, @"D:\sfx\gun_bt_fire.wav", out _));
        Assert.True(FileNameColorAssignments.TryGet(moved, @"D:\sfx\gun_fire.wav", out color));
        Assert.Equal(10, color);

        var cleared = FileNameColorAssignments.Without(moved, [@"D:\sfx\gun_fire.wav"]);
        Assert.Empty(cleared);
    }
}
