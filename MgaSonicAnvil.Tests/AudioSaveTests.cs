using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioSaveTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("tone.wav", true)]
    [InlineData("tone.WAVE", true)]
    [InlineData("tone.mp3", true)]
    [InlineData("tone.aiff", false)]
    [InlineData("tone.aif", false)]
    [InlineData("tone.m4a", false)]
    [InlineData("tone.txt", false)]
    public void CanOverwrite_AllowsWaveAndMp3Only(string? path, bool expected)
    {
        Assert.Equal(expected, AudioSave.CanOverwrite(path));
    }

    [Fact]
    public void PlanFolderWavePaths_KeepsNamesAndAvoidsClashes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var paths = AudioSave.PlanFolderWavePaths(
                [
                    (Path.Combine(dir, "kick.wav"), "kick.wav"),
                    (Path.Combine(@"D:\other", "kick.wav"), "kick.wav"),
                    (null, "untitled"),
                ],
                dir);
            Assert.Equal(3, paths.Length);
            Assert.Equal(Path.Combine(dir, "kick.wav"), paths[0], StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(dir, "kick 2.wav"), paths[1], StringComparer.OrdinalIgnoreCase);
            Assert.Equal(Path.Combine(dir, "untitled.wav"), paths[2], StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void PlanFolderWavePaths_SkipsReservedInPlaceNames()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-save-res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(Path.Combine(dir, "kick.wav")),
            };
            var paths = AudioSave.PlanFolderWavePaths(
                [(null, "kick")],
                dir,
                reserved);
            Assert.Equal(
                Path.GetFullPath(Path.Combine(dir, "kick 2.wav")),
                paths[0],
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
