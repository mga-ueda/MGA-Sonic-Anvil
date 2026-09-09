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
