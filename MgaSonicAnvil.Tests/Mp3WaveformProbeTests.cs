using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Mp3WaveformProbeTests
{
    private const string Kurokawa02 =
        @"V:\マイドライブ\My Musics\Demo\黒川 仁美\黒川仁美デモ_02.mp3";

    private const string Sodeoka01 =
        @"V:\マイドライブ\My Musics\Demo\袖岡 隆泰\01 トラック 01.mp3";

    [Fact]
    public void Kurokawa02_ProbeAndPeaks_HaveAudibleTail() =>
        AssertAudibleTail(Kurokawa02);

    [Fact]
    public void Sodeoka01_ProbeAndPeaks_HaveAudibleTail() =>
        AssertAudibleTail(Sodeoka01);

    private static void AssertAudibleTail(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        Assert.True(AudioTagProbe.TryRead(path, out var tags));
        Assert.True(AudioCodec.TryProbeStreamFormat(path, out var rate, out _, out _, out var frames));
        Assert.True(rate > 0, $"rate={rate} tagRate={tags.SampleRate} tagDur={tags.DurationSeconds}");
        Assert.True(frames > 1000, $"frames={frames} tagDur={tags.DurationSeconds}");

        var peaks = PeakPyramid.BuildPlayerDisplayFromPath(path);
        Assert.False(peaks.IsEmpty);
        Assert.False(peaks.IsBuilding);
        Assert.True(
            peaks.FrameCount > 1000,
            $"peakFrames={peaks.FrameCount} filled={peaks.FilledFrames} probeFrames={frames}");
        Assert.True(
            peaks.FrameCount >= frames * 95 / 100,
            $"peakFrames={peaks.FrameCount} probe={frames} tagDur={tags.DurationSeconds}");

        var mins = new float[64];
        var maxs = new float[64];
        Assert.Equal(64, peaks.ReadRange(0, peaks.FrameCount, 64, 0, mins, maxs));
        var peak = 0f;
        var lastAudible = -1;
        for (var i = 0; i < 64; i++)
        {
            var amp = Math.Max(Math.Abs(mins[i]), Math.Abs(maxs[i]));
            if (amp > peak)
            {
                peak = amp;
            }

            if (amp > 0.05f)
            {
                lastAudible = i;
            }
        }

        Assert.True(
            peak > 0.2f,
            $"peak={peak} lastAudible={lastAudible} frames={peaks.FrameCount} filled={peaks.FilledFrames} probe={frames} tagDur={tags.DurationSeconds}");
        Assert.True(lastAudible >= 32, $"lastAudible={lastAudible} peak={peak}");
    }
}
