using System.IO;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VideoProxyRetentionTests
{
    [Fact]
    public void Clamp_AllowsOnlyPresetDays()
    {
        Assert.Equal(7, VideoProxy.ClampRetentionDays(0));
        Assert.Equal(7, VideoProxy.ClampRetentionDays(3));
        Assert.Equal(1, VideoProxy.ClampRetentionDays(1));
        Assert.Equal(7, VideoProxy.ClampRetentionDays(7));
        Assert.Equal(14, VideoProxy.ClampRetentionDays(14));
        Assert.Equal(30, VideoProxy.ClampRetentionDays(30));
        Assert.Equal([1, 7, 14, 30], VideoProxy.RetentionChoices);
        Assert.Equal(7, VideoProxy.DefaultRetentionDays);
        Assert.Equal(7, new AppSettings().VideoProxyRetentionDays);
        Assert.Equal(7, AppSettings.CreateDefault().ResolvedVideoProxyRetentionDays());
    }

    [Fact]
    public void Settings_RoundTripAndMissingDefault()
    {
        var json = JsonSerializer.Serialize(
            new AppSettings { VideoProxyRetentionDays = 14 },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(14, back!.VideoProxyRetentionDays);
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(7, missing!.ResolvedVideoProxyRetentionDays());
        Assert.Equal(string.Empty, missing.FfmpegExePath);
        Assert.False(missing.VideoProxyDisableAutoEncode);
        Assert.False(new AppSettings().VideoProxyDisableAutoEncode);
        Assert.False(AppSettings.CreateDefault().VideoProxyDisableAutoEncode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    public void TryResolveFfmpegExe_Empty_IsMissing(string? path)
    {
        Assert.False(VideoProxy.TryResolveFfmpegExe(path, out var exe));
        Assert.Equal(string.Empty, exe);
    }

    [Fact]
    public void TryResolveFfmpegExe_MissingFile_IsMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"missing-ffmpeg-{Guid.NewGuid():N}.exe");
        Assert.False(VideoProxy.TryResolveFfmpegExe(missing, out _));
    }

    [Fact]
    public void TryResolveFfmpegExe_ExistingFile_UsesPath()
    {
        var path = Path.GetTempFileName();
        try
        {
            Assert.True(VideoProxy.TryResolveFfmpegExe(path, out var exe));
            Assert.Equal(Path.GetFullPath(path), exe);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Settings_RoundTripFfmpegExePath()
    {
        var json = JsonSerializer.Serialize(
            new AppSettings { FfmpegExePath = @"C:\tools\ffmpeg.exe" },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(@"C:\tools\ffmpeg.exe", back!.FfmpegExePath);
        Assert.Equal(string.Empty, AppSettings.CreateDefault().FfmpegExePath);
    }

    [Fact]
    public void Settings_RoundTripVideoProxyDisableAutoEncode()
    {
        var json = JsonSerializer.Serialize(
            new AppSettings { VideoProxyDisableAutoEncode = true },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.True(back!.VideoProxyDisableAutoEncode);
        Assert.False(AppSettings.CreateDefault().VideoProxyDisableAutoEncode);
    }

    [Fact]
    public void AllowsAutoEncodeProxy_LaunchSuppressOverridesSettings()
    {
        Assert.True(VideoProxy.AllowsAutoEncodeProxy(suppressLaunchEncode: false, disableInSettings: false));
        Assert.False(VideoProxy.AllowsAutoEncodeProxy(suppressLaunchEncode: true, disableInSettings: false));
        Assert.False(VideoProxy.AllowsAutoEncodeProxy(suppressLaunchEncode: false, disableInSettings: true));
        Assert.False(VideoProxy.AllowsAutoEncodeProxy(suppressLaunchEncode: true, disableInSettings: true));
    }

    [Fact]
    public void SuppressAutoEncode_BlocksCanEncodeProxyEvenWhenFfmpegSet()
    {
        var previous = AppStorage.Settings.FfmpegExePath;
        var previousDisable = AppStorage.Settings.VideoProxyDisableAutoEncode;
        var previousSuppress = VideoProxy.SuppressAutoEncode;
        var path = Path.GetTempFileName();
        try
        {
            AppStorage.Settings.FfmpegExePath = path;
            AppStorage.Settings.VideoProxyDisableAutoEncode = false;
            VideoProxy.SuppressAutoEncode = false;
            Assert.True(VideoProxy.CanEncodeProxy());
            VideoProxy.SuppressAutoEncode = true;
            Assert.False(VideoProxy.CanEncodeProxy());
        }
        finally
        {
            VideoProxy.SuppressAutoEncode = previousSuppress;
            AppStorage.Settings.FfmpegExePath = previous;
            AppStorage.Settings.VideoProxyDisableAutoEncode = previousDisable;
            File.Delete(path);
        }
    }

    [Fact]
    public void PruneExpired_RemovesOldMp4AndStalePart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-proxy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var now = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
            var keep = Path.Combine(dir, "keep.avi");
            var old = Path.Combine(dir, "old.avi");
            var oldMp4 = Path.Combine(dir, "legacy.mp4");
            var part = Path.Combine(dir, "job.avi.part");
            var freshPart = Path.Combine(dir, "fresh.avi.part");
            File.WriteAllText(keep, "k");
            File.WriteAllText(old, "o");
            File.WriteAllText(oldMp4, "m");
            File.WriteAllText(part, "p");
            File.WriteAllText(freshPart, "f");
            File.SetLastWriteTimeUtc(keep, now.AddDays(-6));
            File.SetLastWriteTimeUtc(old, now.AddDays(-8));
            File.SetLastWriteTimeUtc(oldMp4, now.AddDays(-8));
            File.SetLastWriteTimeUtc(part, now.AddHours(-7));
            File.SetLastWriteTimeUtc(freshPart, now.AddHours(-1));

            var removed = VideoProxy.PruneExpired(7, now, dir);
            Assert.Equal(3, removed);
            Assert.True(File.Exists(keep));
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(oldMp4));
            Assert.False(File.Exists(part));
            Assert.True(File.Exists(freshPart));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ClearAll_RemovesEveryProxyAndPart()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-proxy-clear-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var keepAge = Path.Combine(dir, "fresh.avi");
            var old = Path.Combine(dir, "old.avi");
            var part = Path.Combine(dir, "job.avi.part");
            File.WriteAllText(keepAge, "k");
            File.WriteAllText(old, "o");
            File.WriteAllText(part, "p");
            File.SetLastWriteTimeUtc(keepAge, DateTime.UtcNow);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-30));

            Assert.Equal(3, VideoProxy.ClearAll(dir));
            Assert.False(File.Exists(keepAge));
            Assert.False(File.Exists(old));
            Assert.False(File.Exists(part));
            Assert.Equal(0, VideoProxy.ClearAll(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void GetUsageBytes_SumsProxyAndPartFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-proxy-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Equal(0, VideoProxy.GetUsageBytes(dir));
            Assert.Equal(0, VideoProxy.GetUsageBytes(Path.Combine(dir, "missing")));

            File.WriteAllBytes(Path.Combine(dir, "a.avi"), new byte[100]);
            File.WriteAllBytes(Path.Combine(dir, "b.wav"), new byte[250]);
            File.WriteAllBytes(Path.Combine(dir, "c.avi.part"), new byte[50]);
            Assert.Equal(400, VideoProxy.GetUsageBytes(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FfmpegArgs_ForceAviMjpegForPartFile()
    {
        var args = VideoProxy.BuildFfmpegArguments(@"C:\clip.mov", @"C:\cache\ab.avi.part");
        var formatAt = -1;
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-f")
            {
                formatAt = i;
                break;
            }
        }

        Assert.True(formatAt >= 0);
        Assert.Equal("avi", args[formatAt + 1]);
        Assert.Equal(@"C:\cache\ab.avi.part", args[^1]);
        Assert.Contains("mjpeg", args);
        Assert.DoesNotContain("fps=30", args);
        Assert.Contains("pcm_s16le", args);
    }

    [Fact]
    public void FfmpegArgs_StillExtractIsSingleJpegFrame()
    {
        var args = VideoProxy.BuildStillExtractArguments(@"C:\clip.mov", @"C:\tmp\frame.jpg", 256);
        Assert.Contains("-frames:v", args);
        Assert.Contains("1", args);
        Assert.Contains("scale=256:-1", args);
        Assert.Contains("image2", args);
        Assert.DoesNotContain("mjpeg", args);
        Assert.Equal(@"C:\tmp\frame.jpg", args[^1]);
    }

    [Fact]
    public void FfmpegArgs_AudioExtractIsWavWithoutVideo()
    {
        var args = VideoProxy.BuildAudioExtractArguments(@"C:\clip.mp4", @"C:\cache\ab.wav");
        Assert.Contains("-vn", args);
        Assert.Contains("0:a:0", args);
        Assert.Contains("pcm_s16le", args);
        Assert.Contains("wav", args);
        Assert.DoesNotContain("mjpeg", args);
        Assert.Equal(@"C:\cache\ab.wav", args[^1]);
    }

    [Fact]
    public void CachePath_IgnoresCasingAndWriteTime()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-proxy-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "Clip.MOV");
            File.WriteAllBytes(path, [1, 2, 3, 4]);
            var first = VideoProxy.CachePath(path);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-3));
            var lower = Path.Combine(dir, "clip.mov");
            Assert.Equal(first, VideoProxy.CachePath(path));
            Assert.Equal(first, VideoProxy.CachePath(lower));
            Assert.Equal(VideoProxy.SourceKey(path), VideoProxy.SourceKey(lower));

            File.WriteAllBytes(path, [1, 2, 3, 4, 5]);
            Assert.NotEqual(first, VideoProxy.CachePath(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LastFfmpegError_UsesLastUsefulLine()
    {
        Assert.Equal(
            "Error opening output files: Invalid argument",
            VideoProxy.LastFfmpegError(
                "frame= 1\nUnable to choose an output format\nError opening output files: Invalid argument\n"));
        Assert.Equal(string.Empty, VideoProxy.LastFfmpegError("  \nframe= 3\n"));
    }

    [Fact]
    public void MapEncodeProgress_ReservesShareForFinalize()
    {
        Assert.Equal(0, VideoProxy.MapEncodeProgress(0, 1_000_000), 6);
        Assert.Equal(VideoProxy.EncodeProgressShare * 0.5, VideoProxy.MapEncodeProgress(500_000, 1_000_000), 6);
        Assert.Equal(VideoProxy.EncodeProgressShare, VideoProxy.MapEncodeProgress(1_000_000, 1_000_000), 6);
        Assert.True(VideoProxy.MapEncodeProgress(1_200_000, 1_000_000) > VideoProxy.EncodeProgressShare);
        Assert.True(VideoProxy.MapEncodeProgress(1_200_000, 1_000_000) <= VideoProxy.FinalizeProgressCap);
    }

    [Fact]
    public void MapFinalizeProgress_AdvancesWithTimeAndSize()
    {
        var start = VideoProxy.MapFinalizeProgress(sizeAtEncodeEnd: 100_000_000, currentSize: 100_000_000, elapsedSeconds: 0);
        Assert.Equal(VideoProxy.EncodeProgressShare, start, 6);

        var later = VideoProxy.MapFinalizeProgress(100_000_000, 100_000_000, elapsedSeconds: 20);
        Assert.True(later > start);
        Assert.True(later <= VideoProxy.FinalizeProgressCap);

        var grown = VideoProxy.MapFinalizeProgress(100_000_000, 110_000_000, elapsedSeconds: 0.1);
        Assert.True(grown > start);
    }
}
