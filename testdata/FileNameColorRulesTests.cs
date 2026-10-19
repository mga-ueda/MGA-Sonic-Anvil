using System.IO;
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

        var root = Path.Combine(Path.GetTempPath(), "mga-color-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "gun_bt_fire.wav");
        var destination = Path.Combine(root, "gun_fire.wav");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(destination, []);
            manual = FileNameColorAssignments.With(null, [source], 10);
            Assert.True(FileNameColorAssignments.TryRetarget(manual, source, destination, out var moved));
            Assert.False(FileNameColorAssignments.TryGet(moved, source, out _));
            Assert.True(FileNameColorAssignments.TryGet(moved, destination, out color));
            Assert.Equal(10, color);

            var cleared = FileNameColorAssignments.Without(moved, [destination]);
            Assert.Empty(cleared);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MoveTree_RetargetsExistingFiles_AndDropsOrphansUnderTheMove()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-color-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "guns");
        var destDir = Path.Combine(root, "library", "guns");
        var shot = Path.Combine(destDir, "shot.wav");
        var oldShot = Path.Combine(sourceDir, "shot.wav");
        var ghost = Path.Combine(sourceDir, "ghost.wav");
        var untouched = Path.Combine(root, "elsewhere", "click.wav");
        Directory.CreateDirectory(destDir);
        try
        {
            File.WriteAllBytes(shot, []);
            var current = FileNameColorAssignments.With(null, [oldShot, ghost, untouched], 10);
            current = FileNameColorAssignments.With(current, [oldShot], 4);

            var moved = FileNameColorAssignments.MoveTree(current, sourceDir, destDir);

            Assert.NotNull(moved);
            Assert.True(FileNameColorAssignments.TryGet(moved, shot, out var shotColor));
            Assert.Equal(4, shotColor);
            Assert.False(FileNameColorAssignments.TryGet(moved, oldShot, out _));
            Assert.False(FileNameColorAssignments.TryGet(moved, ghost, out _));
            Assert.True(FileNameColorAssignments.TryGet(moved, untouched, out var click));
            Assert.Equal(10, click);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CopyTree_DuplicatesExistingColor_AndDropsOrphansUnderTheCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-color-" + Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(root, "guns");
        var source = Path.Combine(sourceDir, "shot.wav");
        var ghost = Path.Combine(sourceDir, "missing.wav");
        var untouched = Path.Combine(root, "other", "keep.wav");
        Directory.CreateDirectory(sourceDir);
        try
        {
            File.WriteAllBytes(source, []);
            var current = FileNameColorAssignments.With(null, [source, ghost, untouched], 4);

            var copied = FileNameColorAssignments.CopyTree(current, sourceDir, Path.Combine(root, "copies"));

            Assert.NotNull(copied);
            Assert.True(FileNameColorAssignments.TryGet(copied, source, out var sourceColor));
            Assert.Equal(4, sourceColor);
            Assert.True(FileNameColorAssignments.TryGet(copied, Path.Combine(root, "copies", "shot.wav"), out var copy));
            Assert.Equal(4, copy);
            Assert.False(FileNameColorAssignments.TryGet(copied, ghost, out _));
            Assert.True(FileNameColorAssignments.TryGet(copied, untouched, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ForgetTree_RemovesOnlyMissingPathsUnderTheDelete()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-color-" + Guid.NewGuid().ToString("N"));
        var guns = Path.Combine(root, "guns");
        var shot = Path.Combine(guns, "shot.wav");
        var ghost = Path.Combine(guns, "ghost.wav");
        var untouched = Path.Combine(root, "ui", "click.wav");
        Directory.CreateDirectory(guns);
        try
        {
            File.WriteAllBytes(shot, []);
            var current = FileNameColorAssignments.With(null, [shot, ghost, untouched], 3);

            var file = FileNameColorAssignments.ForgetTree(current, ghost);
            Assert.NotNull(file);
            Assert.False(FileNameColorAssignments.TryGet(file, ghost, out _));
            Assert.True(FileNameColorAssignments.TryGet(file, shot, out _));
            Assert.True(FileNameColorAssignments.TryGet(file, untouched, out _));

            var folder = FileNameColorAssignments.ForgetTree(current, guns);
            Assert.NotNull(folder);
            Assert.True(FileNameColorAssignments.TryGet(folder, shot, out _));
            Assert.False(FileNameColorAssignments.TryGet(folder, ghost, out _));
            Assert.True(FileNameColorAssignments.TryGet(folder, untouched, out _));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
