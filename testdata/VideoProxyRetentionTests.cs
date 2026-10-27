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
    public void CanForceEncodeProxy_IgnoresSuppressAndDisableSettings()
    {
        var previous = AppStorage.Settings.FfmpegExePath;
        var previousDisable = AppStorage.Settings.VideoProxyDisableAutoEncode;
        var previousSuppress = VideoProxy.SuppressAutoEncode;
        var path = Path.GetTempFileName();
        try
        {
            AppStorage.Settings.FfmpegExePath = path;
            AppStorage.Settings.VideoProxyDisableAutoEncode = true;
            VideoProxy.SuppressAutoEncode = true;
            Assert.False(VideoProxy.CanEncodeProxy());
            Assert.True(VideoProxy.CanForceEncodeProxy());

            AppStorage.Settings.FfmpegExePath = string.Empty;
            Assert.False(VideoProxy.CanForceEncodeProxy());
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

        // Anvil は AVI に音声も同梱する（TimeCaster の -an とは違う）。
        Assert.Contains("0:a:0?", args);
        Assert.DoesNotContain("-an", args);

        // 既定は CPU デコード（-hwaccel なし）。
        Assert.DoesNotContain("-hwaccel", args);
    }

    [Fact]
    public void FfmpegArgs_Max1080pTvRangeYuv420p()
    {
        var args = VideoProxy.BuildFfmpegArguments(@"C:\clip.mov", @"C:\cache\ab.avi.part");

        // 1920x1080 の箱に収める（拡大はしない）、偶数寸法、TV レンジ yuv420p。
        var vf = IndexOf(args, "-vf");
        Assert.True(vf >= 0);
        var filter = args[vf + 1];
        Assert.Equal(VideoProxy.ProxyVideoFilter, filter);
        Assert.Contains("min(1920,iw)", filter);
        Assert.Contains("min(1080,ih)", filter);
        Assert.Contains("force_original_aspect_ratio=decrease", filter);
        Assert.Contains("force_divisible_by=2", filter);
        Assert.Contains("in_range=auto:out_range=tv", filter);
        Assert.EndsWith(",format=yuv420p", filter);

        var pix = IndexOf(args, "-pix_fmt");
        Assert.True(pix >= 0);
        Assert.Equal("yuv420p", args[pix + 1]);
        Assert.DoesNotContain("yuvj420p", args);

        var range = IndexOf(args, "-color_range");
        Assert.True(range >= 0);
        Assert.Equal("tv", args[range + 1]);

        // FFmpeg 8+ の MJPEG はリミテッドレンジに -strict unofficial が要る。
        var strict = IndexOf(args, "-strict");
        Assert.True(strict >= 0);
        Assert.Equal("unofficial", args[strict + 1]);

        // 映像フィルタと色指定は -i の後、出力の前。
        Assert.True(vf > IndexOf(args, "-i"));
        Assert.True(range < args.Count - 1);
    }

    [Fact]
    public void FfmpegArgs_HwaccelGoesBeforeInput()
    {
        var args = VideoProxy.BuildFfmpegArguments(@"C:\clip.mov", @"C:\cache\ab.avi.part", "d3d11va");
        var hw = IndexOf(args, "-hwaccel");
        Assert.True(hw >= 0);
        Assert.Equal("d3d11va", args[hw + 1]);
        Assert.True(hw < IndexOf(args, "-i"));

        // エンコードは CPU の mjpeg のまま（hwaccel はソースデコードだけ）。
        var codec = IndexOf(args, "-c:v");
        Assert.Equal("mjpeg", args[codec + 1]);

        // 空／空白は CPU と同じ。
        Assert.DoesNotContain("-hwaccel", VideoProxy.BuildFfmpegArguments(@"C:\clip.mov", @"C:\x.avi.part", ""));
        Assert.DoesNotContain("-hwaccel", VideoProxy.BuildFfmpegArguments(@"C:\clip.mov", @"C:\x.avi.part", "  "));
    }

    [Fact]
    public void HwaccelAttemptOrder_TriesNewestFirstThenCpu_AndRemembersProven()
    {
        // 未確定: TimeCaster と同じ順で GPU を試し、最後に CPU（null）。
        var fresh = VideoProxy.HwaccelAttemptOrder(hasProven: false, proven: null);
        Assert.Equal(["d3d12va", "d3d11va", "cuda", "vulkan", "auto", null], fresh);

        // 覚えた GPU 経路を先頭に、失敗時のために残りと CPU を後ろへ。
        var proven = VideoProxy.HwaccelAttemptOrder(hasProven: true, proven: "cuda");
        Assert.Equal("cuda", proven[0]);
        Assert.Equal(1, proven.Count(p => p == "cuda"));
        Assert.Null(proven[^1]);
        Assert.Equal(fresh.Count, proven.Count);

        // CPU が確定していれば GPU を毎回試し直さない。
        Assert.Equal([null], VideoProxy.HwaccelAttemptOrder(hasProven: true, proven: null));
    }

    [Fact]
    public void EncodeVersion_InvalidatesFullResolutionCaches()
    {
        // 旧レシピ（フル解像度・yuvj420p）のキャッシュ名と衝突しない。
        Assert.NotEqual("mjpeg-avi-srcfps", VideoProxy.EncodeVersion);
        Assert.Contains("max1080p", VideoProxy.EncodeVersion);
        Assert.Contains("tv", VideoProxy.EncodeVersion);
        Assert.Equal(1920, VideoProxy.ProxyMaxWidth);
        Assert.Equal(1080, VideoProxy.ProxyMaxHeight);
    }

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == value)
            {
                return i;
            }
        }

        return -1;
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
    public void CachePath_SharesByFileNameAndSize_IgnoresFolderAndWriteTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-proxy-src-" + Guid.NewGuid().ToString("N"));
        var dirA = Path.Combine(root, "a");
        var dirB = Path.Combine(root, "b");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);
        try
        {
            var pathA = Path.Combine(dirA, "Clip.MOV");
            var pathB = Path.Combine(dirB, "clip.mov");
            File.WriteAllBytes(pathA, [1, 2, 3, 4]);
            File.WriteAllBytes(pathB, [1, 2, 3, 4]);
            var first = VideoProxy.CachePath(pathA);
            File.SetLastWriteTimeUtc(pathA, DateTime.UtcNow.AddDays(-3));
            Assert.Equal(first, VideoProxy.CachePath(pathA));
            Assert.Equal(first, VideoProxy.CachePath(pathB));
            Assert.Equal(VideoProxy.SourceKey(pathA), VideoProxy.SourceKey(pathB));

            File.WriteAllBytes(pathA, [1, 2, 3, 4, 5]);
            Assert.NotEqual(first, VideoProxy.CachePath(pathA));
            Assert.Equal(first, VideoProxy.CachePath(pathB));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsurePlayable_DirectPlayCodec_PrefersExistingProxy()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-proxy-pref-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        VideoProxy.UseCacheDirectoryForTests(cache);
        try
        {
            var source = Path.Combine(root, "clip.mov");
            File.WriteAllBytes(source, VideoCodecProbeTests.BuildMovie("jpeg"));
            Assert.True(VideoCodecProbe.CanPlayWithoutProxy(source));

            // プロキシ無し: 原ファイルを返し、表示先も原ファイル。
            Assert.Equal(source, await VideoProxy.EnsurePlayableAsync(source, null, CancellationToken.None));
            Assert.True(VideoProxy.TryGetDisplayPath(source, out var display));
            Assert.Equal(source, display);

            // プロキシがあれば Ensure の戻り値も表示先もプロキシ（原ファイルの記録で潰さない）。
            var proxy = VideoProxy.CachePath(source);
            File.WriteAllBytes(proxy, [1, 2, 3]);
            Assert.Equal(proxy, await VideoProxy.EnsurePlayableAsync(source, null, CancellationToken.None));
            Assert.True(VideoProxy.TryGetDisplayPath(source, out display));
            Assert.Equal(proxy, display);
            Assert.True(VideoProxy.TryGetCached(source, out var cached));
            Assert.Equal(proxy, cached);
        }
        finally
        {
            VideoProxy.ResetCacheDirectoryForTests();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IsProxyPath_OnlyMatchesCacheDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-proxy-path-" + Guid.NewGuid().ToString("N"));
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        VideoProxy.UseCacheDirectoryForTests(cache);
        try
        {
            var source = Path.Combine(root, "clip.mp4");
            File.WriteAllBytes(source, [1, 2, 3, 4]);
            Assert.True(VideoProxy.IsProxyPath(VideoProxy.CachePath(source)));
            Assert.False(VideoProxy.IsProxyPath(source));
            Assert.False(VideoProxy.IsProxyPath(Path.Combine(cache, "sub", "x.avi")));
            Assert.False(VideoProxy.IsProxyPath(null));
            Assert.False(VideoProxy.IsProxyPath(" "));
        }
        finally
        {
            VideoProxy.ResetCacheDirectoryForTests();
            Directory.Delete(root, recursive: true);
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
