using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LaunchFilesTests
{
    [Fact]
    public void Collect_KeepsOpenableFullPathsAndSkipsFlags()
    {
        var wav = Path.GetFullPath("launch-a.wav");
        var mp3 = Path.GetFullPath("launch-b.mp3");
        var collected = LaunchFiles.Collect(
        [
            "-top",
            "/silent",
            wav,
            "notes.txt",
            mp3,
            wav,
        ]);

        Assert.Equal([wav, mp3], collected);
        Assert.True(AudioCodec.IsOpenable(collected[0]));
    }

    [Fact]
    public void Collect_ResolvesRelativeWavePath()
    {
        var collected = LaunchFiles.Collect(["nested/sample.aiff"]);
        Assert.Single(collected);
        Assert.True(Path.IsPathRooted(collected[0]));
        Assert.EndsWith($"{Path.DirectorySeparatorChar}nested{Path.DirectorySeparatorChar}sample.aiff", collected[0]);
    }

    [Fact]
    public void Collect_ExpandsFoldersAndSkipsUnsupported()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-anvil-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wav = Path.Combine(root, "tone.wav");
        File.WriteAllText(wav, "wav");
        File.WriteAllText(Path.Combine(root, "readme.md"), "md");
        try
        {
            var collected = LaunchFiles.Collect(["-top", root, "notes.txt"]);
            Assert.Equal([wav], collected);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PreferOpened_PrefersNewlyOpenedOverExisting()
    {
        var opened = new object();
        var existing = new object();
        Assert.Same(opened, LaunchFiles.PreferOpened(opened, existing));
        Assert.Same(existing, LaunchFiles.PreferOpened<object>(opened: null, existing));
        Assert.Null(LaunchFiles.PreferOpened<object>(opened: null, existing: null));
    }

    [Fact]
    public void ContainsMp3_TrueIfAnyMp3MixedIn()
    {
        Assert.True(LaunchFiles.ContainsMp3(
            [Path.GetFullPath("a.wav"), Path.GetFullPath("b.MP3")]));
        Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("only.mp3")]));
        Assert.True(LaunchFiles.ContainsMp3([Path.GetFullPath("clip.m4a")]));
        Assert.False(LaunchFiles.ContainsMp3(
            [Path.GetFullPath("a.wav"), Path.GetFullPath("b.aiff")]));
        Assert.False(LaunchFiles.ContainsMp3([]));
        Assert.False(LaunchFiles.ContainsMp3(["", "  "]));
    }

    [Fact]
    public void HasStartup_FollowsSetAndTake()
    {
        try
        {
            var wav = Path.GetFullPath("startup-has.wav");
            LaunchFiles.SetStartup([wav]);
            Assert.True(LaunchFiles.HasStartup);
            Assert.Equal([wav], LaunchFiles.TakeStartup());
            Assert.False(LaunchFiles.HasStartup);
        }
        finally
        {
            LaunchFiles.SetStartup([]);
        }
    }
}
