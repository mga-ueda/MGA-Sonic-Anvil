using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Mp3EncodeTests
{
    [Fact]
    public void DefaultLameOptions_AreV2()
    {
        Assert.Equal("-V2 --noreplaygain", Mp3Encode.DefaultLameOptions);
        Assert.Equal(192, Mp3Encode.DefaultWindowsBitRateKbps);
        var settings = new AppSettings();
        Assert.Equal(string.Empty, settings.LameExePath);
        Assert.Equal(Mp3Encode.DefaultLameOptions, settings.LameOptions);
        Assert.Equal(192, settings.Mp3BitRate);
        Assert.Equal(0, settings.ExportParallelism);
        Assert.Equal("Stereo", settings.RecordLayout);
        Assert.Equal(string.Empty, settings.PlaybackLayout);
        Assert.Equal("Stereo", settings.ResolvedPlaybackLayout().Id);
        settings.RecordLayout = "5.1";
        Assert.Equal("5.1", settings.ResolvedPlaybackLayout().Id);
        settings.PlaybackLayout = "7.1";
        Assert.Equal("7.1", settings.ResolvedPlaybackLayout().Id);
        Assert.Empty(settings.RecordInputMap);
        Assert.Empty(settings.PlaybackOutputMap);
        Assert.True(settings.WaapiPanelVisible);
        var options = settings.ToMp3EncodeOptions();
        Assert.Equal(192, options.WindowsBitRateKbps);
        Assert.Equal(string.Empty, options.LameExePath);
        Assert.Equal(Mp3Encode.DefaultLameOptions, options.LameOptions);
    }

    [Fact]
    public void SettingsJson_MissingLameFields_UsesDefaults()
    {
        var settings = JsonSerializer.Deserialize(
            """{"AudioApi":"WaveOut","Mp3BitRate":256}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.NotNull(settings);
        Assert.Equal(256, settings.Mp3BitRate);
        Assert.Equal(string.Empty, settings.LameExePath);
        Assert.Equal(Mp3Encode.DefaultLameOptions, settings.LameOptions);
        Assert.Equal(0, settings.ExportParallelism);
        Assert.True(settings.WaapiPanelVisible);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void TryResolveLameExe_Empty_IsWindows(string? path)
    {
        Assert.False(Mp3Encode.UsesLame(path));
        Assert.False(Mp3Encode.TryResolveLameExe(path, out var exe));
        Assert.Equal(string.Empty, exe);
    }

    [Fact]
    public void TryResolveLameExe_MissingFile_IsWindows()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-lame-{Guid.NewGuid():N}.exe");
        Assert.False(Mp3Encode.UsesLame(missing));
        Assert.False(Mp3Encode.TryResolveLameExe(missing, out _));
    }

    [Fact]
    public void TryResolveLameExe_Directory_IsWindows()
    {
        Assert.False(Mp3Encode.UsesLame(Path.GetTempPath()));
    }

    [Fact]
    public void TryResolveLameExe_ExistingFile_UsesLame()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.True(Mp3Encode.TryResolveLameExe(path, out var exe));
            Assert.True(Mp3Encode.UsesLame("\"" + path + "\""));
            Assert.Equal(Path.GetFullPath(path), exe);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(192, 192)]
    [InlineData(191, 192)]
    [InlineData(200, 192)]
    [InlineData(320, 320)]
    [InlineData(8, 32)]
    [InlineData(400, 320)]
    [InlineData(128, 128)]
    public void ClampWindowsBitRate_SnapsToMpeg1(int input, int expected)
    {
        Assert.Equal(expected, Mp3Encode.ClampWindowsBitRate(input));
    }

    [Fact]
    public void SplitArguments_KeepsQuotedGroups()
    {
        var args = Mp3Encode.SplitArguments("--preset cbr 192 --noreplaygain");
        Assert.Equal(["--preset", "cbr", "192", "--noreplaygain"], args);

        var quoted = Mp3Encode.SplitArguments("-b 192 -q 0 \"--scale 1\"");
        Assert.Equal(["-b", "192", "-q", "0", "--scale 1"], quoted);
    }

    [Fact]
    public void SplitArguments_Empty_IsEmpty()
    {
        Assert.Empty(Mp3Encode.SplitArguments(null));
        Assert.Empty(Mp3Encode.SplitArguments("   "));
    }

    [Fact]
    public void BuildLameArguments_AppendsFilesWithoutDashDash()
    {
        var args = Mp3Encode.BuildLameArguments("-b 192", @"C:\in.wav", @"D:\out.mp3");
        Assert.Equal(["-b", "192", @"C:\in.wav", @"D:\out.mp3"], args);
        Assert.DoesNotContain("--", args);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ResolveLameOptions_Empty_UsesDefault(string? options)
    {
        Assert.Equal(Mp3Encode.DefaultLameOptions, Mp3Encode.ResolveLameOptions(options));
        Assert.Equal(
            ["-V2", "--noreplaygain", @"C:\in.wav", @"D:\out.mp3"],
            Mp3Encode.BuildLameArguments(options, @"C:\in.wav", @"D:\out.mp3"));
    }

    [Theory]
    [InlineData("  45%", 0.45)]
    [InlineData("1234/5678 (22%)", 0.22)]
    [InlineData("Frame  100/400", 0.25)]
    public void TryParseLameProgress_ReadsPercentOrFrames(string line, double expected)
    {
        Assert.True(Mp3Encode.TryParseLameProgress(line, out var progress));
        Assert.Equal(expected, progress, 3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("LAME 3.100")]
    public void TryParseLameProgress_IgnoresNoise(string? line)
    {
        Assert.False(Mp3Encode.TryParseLameProgress(line, out _));
    }

    [Fact]
    public void ToMp3EncodeOptions_EmptyLameOptions_UsesDefault()
    {
        var settings = new AppSettings { LameOptions = "   " };
        Assert.Equal(Mp3Encode.DefaultLameOptions, settings.ToMp3EncodeOptions().LameOptions);
    }
}
