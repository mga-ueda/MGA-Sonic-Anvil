using System.Diagnostics;
using System.IO;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Video.Dxgi;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VideoPlaybackFallbackTests
{
    private const string IphoneProbeLog = """
        Input #0, mov,mp4,m4a,3gp,3g2,mj2, from 'D:\Downloads\IMG_7161.MOV':
          Metadata:
            major_brand     : qt
          Duration: 00:00:13.95, start: 0.000000, bitrate: 64491 kb/s
          Stream #0:0[0x1](und): Video: hevc (Main) (hvc1 / 0x31637668), yuv420p(tv, bt709), 3840x2160, 64219 kb/s, 60.02 fps, 60 tbr, 600 tbn (default)
            Metadata:
              handler_name    : Core Media Video
            Side data:
              Display Matrix: rotation of -90.00 degrees
          Stream #0:1[0x2](und): Audio: aac (mp4a / 0x6134706D), 44100 Hz, stereo, fltp, 182 kb/s (default)
        At least one output file must be specified
        """;

    [Fact]
    public void ProbeParse_ReadsCodecSizeFpsDurationAndAppliesRotation()
    {
        Assert.True(FfmpegPipeFrameSource.TryParseProbe(IphoneProbeLog, out var info));
        Assert.Equal("hevc", info.Codec);
        // 縦撮り（-90°）は表示上の向きへ入れ替える。
        Assert.Equal(2160, info.DisplayWidth);
        Assert.Equal(3840, info.DisplayHeight);
        Assert.Equal(60.02, info.Fps, 3);
        Assert.Equal(13.95, info.Duration.TotalSeconds, 3);
    }

    [Fact]
    public void ProbeParse_SkipsAttachedPictureAndFallsBackToTbr()
    {
        const string log = """
              Duration: 00:01:02.50, start: 0.000000, bitrate: 320 kb/s
              Stream #0:0: Video: mjpeg (Baseline), yuvj420p(pc, bt470bg/unknown/unknown), 600x600 [SAR 1:1 DAR 1:1], 90k tbr, 90k tbn (attached pic)
              Stream #0:1: Video: prores (HQ) (apch / 0x68637061), yuv422p10le(tv, bt709, progressive), 1920x1080, 176000 kb/s, SAR 1:1 DAR 16:9, 23.98 tbr, 24k tbn (default)
            """;
        Assert.True(FfmpegPipeFrameSource.TryParseProbe(log, out var info));
        Assert.Equal("prores", info.Codec);
        Assert.Equal(1920, info.DisplayWidth);
        Assert.Equal(1080, info.DisplayHeight);
        Assert.Equal(23.98, info.Fps, 3);
        Assert.Equal(62.5, info.Duration.TotalSeconds, 3);
    }

    [Fact]
    public void ProbeParse_NoVideoStreamFails()
    {
        const string log = """
            D:\x.mp4: Invalid data found when processing input
            """;
        Assert.False(FfmpegPipeFrameSource.TryParseProbe(log, out _));
        Assert.Equal(@"D:\x.mp4: Invalid data found when processing input", FfmpegPipeFrameSource.LastMeaningfulLine(log));
        Assert.Equal(string.Empty, FfmpegPipeFrameSource.LastMeaningfulLine("At least one output file must be specified\n"));
    }

    [Theory]
    [InlineData(3840, 2160, 1920, 1080)]
    [InlineData(2160, 3840, 1080, 1920)]
    [InlineData(1920, 1080, 1920, 1080)]
    [InlineData(1280, 720, 1280, 720)]
    [InlineData(4096, 2160, 1920, 1012)]
    [InlineData(721, 481, 722, 482)]
    public void FitOutputSize_FitsBoxKeepsAspectAndEvenSizes(int w, int h, int expectedW, int expectedH) =>
        Assert.Equal((expectedW, expectedH), DxgiPrototypeFrameSource.FitOutputSize(w, h));

    [Fact]
    public void DecodeArguments_SeekOnlyWhenPositiveAndRawNv12ToStdout()
    {
        var atStart = FfmpegPipeFrameSource.BuildDecodeArguments("in.mov", 0, 1280, 720, "29.97");
        Assert.DoesNotContain("-ss", atStart);
        Assert.Equal("pipe:1", atStart[^1]);
        Assert.Contains("rawvideo", atStart);
        Assert.Contains(atStart, a => a.StartsWith("scale=1280:720:", StringComparison.Ordinal) && a.EndsWith("format=nv12", StringComparison.Ordinal));

        var seeking = FfmpegPipeFrameSource.BuildDecodeArguments("in.mov", TimeSpan.FromSeconds(12.5).Ticks, 1280, 720, "29.97");
        var ss = seeking.ToList().IndexOf("-ss");
        Assert.True(ss >= 0 && ss < seeking.ToList().IndexOf("-i"), "-ss は入力側（-i の前）");
        Assert.Equal("12.5", seeking[ss + 1]);
    }

    [Fact]
    public void FailureText_NamesCodecStoreExtensionAndFfmpegSetting()
    {
        var text = VideoOpenFailureText.Build("IMG_7161.MOV", "hvc1", VideoOpenFailureText.FfmpegState.NotConfigured, string.Empty, null, timedOut: false);
        Assert.Contains("IMG_7161.MOV", text);
        Assert.Contains("HEVC (H.265)", text);
        Assert.Contains(VideoOpenFailureText.StoreExtensionName("hvc1")!, text);
        Assert.Contains(UiStrings.LabelFfmpegPath, text);
        Assert.Contains(UiStrings.LabelSettingsTabExport, text);
    }

    [Fact]
    public void FailureText_ShowsMissingPathAndFfmpegError()
    {
        var missing = VideoOpenFailureText.Build("a.mov", "apcn", VideoOpenFailureText.FfmpegState.Missing, @"X:\nope\ffmpeg.exe", null, timedOut: false);
        Assert.Contains("Apple ProRes", missing);
        Assert.Contains(@"X:\nope\ffmpeg.exe", missing);
        Assert.Null(VideoOpenFailureText.StoreExtensionName("apcn"));

        var failed = VideoOpenFailureText.Build("b.mp4", string.Empty, VideoOpenFailureText.FfmpegState.Failed, null, "Invalid data found when processing input", timedOut: false);
        Assert.Contains("ffmpeg: Invalid data found when processing input", failed);
        // ffmpeg まで試して駄目なら、ffmpeg を設定せよという案内は出さない。
        Assert.DoesNotContain(UiStrings.LabelSettingsTabExport, failed);
    }

    [Fact]
    public void Pump_WithoutFfmpegReportsFailureNotTried()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-vfallback-" + Guid.NewGuid().ToString("N") + ".mp4");
        File.WriteAllText(path, "not a video");
        try
        {
            using var pump = new DxgiAsyncFramePump();
            Assert.False(pump.Open(path));
            Assert.NotNull(pump.LastOpenFailure);
            Assert.False(pump.LastOpenFailure!.FfmpegTried);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>PATH に ffmpeg があるときだけ: MF で読めない ProRes を ffmpeg パイプで読み、Seek 先の枚が出る。</summary>
    [Fact]
    public void Pump_FallsBackToFfmpegForProResWhenFfmpegOnPath()
    {
        var ffmpeg = FindFfmpegOnPath();
        if (ffmpeg is null)
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "mga-vfallback-" + Guid.NewGuid().ToString("N") + ".mov");
        try
        {
            using (var make = Process.Start(new ProcessStartInfo(ffmpeg)
                   {
                       UseShellExecute = false,
                       CreateNoWindow = true,
                       ArgumentList =
                       {
                           "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=25",
                           "-t", "3", "-c:v", "prores_ks", path,
                       },
                   }))
            {
                Assert.NotNull(make);
                Assert.True(make!.WaitForExit(60_000));
            }

            Assert.True(File.Exists(path));
            using var pump = new DxgiAsyncFramePump();
            Assert.True(pump.Open(path, ffmpegExe: ffmpeg));
            Assert.True(pump.UsesFfmpeg);
            Assert.Equal(640, pump.Width);
            Assert.Equal(360, pump.Height);
            Assert.Equal(3, pump.Duration.TotalSeconds, 1);

            var target = TimeSpan.FromSeconds(2);
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            DxgiDecodedFrame? frame = null;
            while (DateTime.UtcNow < deadline)
            {
                pump.RequestPosition(target);
                if (pump.TryGetFrame(target, out frame) && frame is not null && frame.Covers(target.Ticks))
                {
                    break;
                }

                Thread.Sleep(10);
            }

            Assert.NotNull(frame);
            Assert.True(frame!.Covers(target.Ticks));
            Assert.Equal(DxgiFramePixelFormat.Nv12, frame.Format);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static string? FindFfmpegOnPath()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
