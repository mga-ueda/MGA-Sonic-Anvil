using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PeakPyramidTests
{
    [Fact]
    public void Build_Empty_IsEmpty()
    {
        var peaks = PeakPyramid.Build([], 2);
        Assert.True(peaks.IsEmpty);
        Assert.Equal(0, peaks.ReadRange(0, 10, 8, 0, new float[8], new float[8]));
    }

    [Fact]
    public void ReadRange_WhenFewerFramesThanColumns_FillsAllColumns()
    {
        const int frames = 100;
        var samples = new float[frames];
        samples[0] = 0.95f;
        samples[frames - 1] = -0.9f;
        var peaks = PeakPyramid.Build(samples, 1);
        var mins = new float[500];
        var maxs = new float[500];
        Assert.Equal(500, peaks.ReadRange(0, frames, 500, 0, mins, maxs));
        Assert.True(maxs[0] >= 0.9f);
        Assert.True(mins[^1] <= -0.85f);
        Assert.Contains(maxs, v => v >= 0.9f);
    }

    [Fact]
    public void ReadRange_PreservesSpikeInOwningColumn()
    {
        var frames = 4096;
        var samples = new float[frames];
        samples[2000] = 0.93f;
        samples[2001] = -0.87f;

        var peaks = PeakPyramid.Build(samples, 1);
        var mins = new float[64];
        var maxs = new float[64];
        var count = peaks.ReadRange(0, frames, 64, 0, mins, maxs);

        Assert.Equal(64, count);
        var column = 2000 * 64 / frames;
        Assert.True(maxs[column] >= 0.93f - 1e-6);
        Assert.True(mins[column] <= -0.87f + 1e-6);
    }

    [Fact]
    public void ReadRangePacked_LowFrameCount_MatchesPixelWidth()
    {
        // 4000Hz・短いクリップで画面幅よりフレームが少ない典型。
        const int frames = 800;
        var samples = new float[frames];
        Array.Fill(samples, 0.5f);
        samples[0] = 0.9f;
        samples[^1] = -0.9f;
        var peaks = PeakPyramid.BuildPlayerDisplay(samples, channels: 1, sampleCount: frames);
        const int width = 1920;
        var mins = new float[width];
        var maxs = new float[width];
        Assert.Equal(width, peaks.ReadRangePacked(0, frames, width, mins, maxs));
        Assert.True(maxs[0] >= 0.8f);
        Assert.True(mins[^1] <= -0.8f);
        // 全カラムに値が入っている（未描画の 0 埋め残りがない）。
        Assert.DoesNotContain(0f, maxs);
    }

    [Fact]
    public void ReadRange_Stereo_KeepsChannelsIndependent()
    {
        var frames = 512;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.25f;
            samples[i * 2 + 1] = -0.5f;
        }

        var peaks = PeakPyramid.Build(samples, 2);
        var leftMin = new float[16];
        var leftMax = new float[16];
        var rightMin = new float[16];
        var rightMax = new float[16];
        Assert.Equal(16, peaks.ReadRange(0, frames, 16, 0, leftMin, leftMax));
        Assert.Equal(16, peaks.ReadRange(0, frames, 16, 1, rightMin, rightMax));

        Assert.All(leftMax, v => Assert.InRange(v, 0.24f, 0.26f));
        Assert.All(rightMin, v => Assert.InRange(v, -0.51f, -0.49f));
    }

    [Fact]
    public void ReadRangePacked_MatchesPerChannel()
    {
        var frames = 2048;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = MathF.Sin(i * 0.05f);
            samples[i * 2 + 1] = MathF.Cos(i * 0.05f);
        }

        samples[400 * 2] = 0.97f;
        samples[900 * 2 + 1] = -0.91f;

        var peaks = PeakPyramid.Build(samples, 2);
        var packedMin = new float[64 * 2];
        var packedMax = new float[64 * 2];
        var leftMin = new float[64];
        var leftMax = new float[64];
        var rightMin = new float[64];
        var rightMax = new float[64];

        Assert.Equal(64, peaks.ReadRangePacked(0, frames, 64, packedMin, packedMax));
        Assert.Equal(64, peaks.ReadRange(0, frames, 64, 0, leftMin, leftMax));
        Assert.Equal(64, peaks.ReadRange(0, frames, 64, 1, rightMin, rightMax));

        for (var i = 0; i < 64; i++)
        {
            Assert.Equal(leftMin[i], packedMin[i * 2]);
            Assert.Equal(leftMax[i], packedMax[i * 2]);
            Assert.Equal(rightMin[i], packedMin[i * 2 + 1]);
            Assert.Equal(rightMax[i], packedMax[i * 2 + 1]);
        }
    }

    [Fact]
    public void Build_SampleCount_IgnoresSpareCapacity()
    {
        var samples = new float[8];
        samples[0] = 0.1f;
        samples[1] = 0.1f;
        samples[2] = 0.1f;
        samples[3] = 0.1f;
        samples[4] = 1f;
        samples[5] = 1f;
        samples[6] = 1f;
        samples[7] = 1f;

        var peaks = PeakPyramid.Build(samples, 1, 4);
        Assert.Equal(4, peaks.FrameCount);

        var mins = new float[1];
        var maxs = new float[1];
        Assert.Equal(1, peaks.ReadRange(0, 4, 1, 0, mins, maxs));
        Assert.InRange(maxs[0], 0.09f, 0.11f);
    }

    [Fact]
    public void ReadRange_ZoomedWindow_MatchesRawMinMax()
    {
        var frames = 8000;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = MathF.Sin(i * 0.07f);
        }

        samples[3500] = 1f;
        var peaks = PeakPyramid.Build(samples, 1);
        const int start = 3200;
        const int end = 3800;
        var mins = new float[1];
        var maxs = new float[1];
        Assert.Equal(1, peaks.ReadRange(start, end, 1, 0, mins, maxs));
        Assert.Equal(1f, maxs[0], 5);
    }

    [Fact]
    public void BuildDisplay_KeepsShortFilesDense_CapsLongFiles()
    {
        var shortSamples = new float[2400];
        shortSamples[1200] = 0.88f;
        var shortPeaks = PeakPyramid.BuildDisplay(shortSamples, 1, shortSamples.Length);
        Assert.Equal(1, shortPeaks.BaseBucketFrames);
        Assert.False(shortPeaks.NeedsEditorDetail);
        Assert.False(shortPeaks.NeedsEditorRebuild(1));
        Assert.True(shortPeaks.NeedsEditorRebuild(2));

        var longFrames = PeakPyramid.DisplayBaseBuckets * 8;
        var longSamples = new float[longFrames];
        longSamples[100] = 0.77f;
        var longPeaks = PeakPyramid.BuildDisplay(longSamples, 1, longSamples.Length);
        Assert.True(longPeaks.BaseBucketFrames > 1);
        Assert.True(longPeaks.NeedsEditorDetail);
        Assert.True(longPeaks.BaseBucketFrames >= longFrames / PeakPyramid.DisplayBaseBuckets);

        var editor = PeakPyramid.Build(longSamples, 1, longSamples.Length);
        Assert.True(editor.BaseBucketFrames < longPeaks.BaseBucketFrames);
        Assert.False(editor.NeedsEditorDetail);

        var mins = new float[64];
        var maxs = new float[64];
        Assert.Equal(64, shortPeaks.ReadRange(0, shortSamples.Length, 64, 0, mins, maxs));
        var column = 1200 * 64 / shortSamples.Length;
        Assert.True(maxs[column] >= 0.88f - 1e-6);
    }

    [Fact]
    public void BuildPlayerDisplay_IsMonoCoarse_ButKeepsTransientPeak()
    {
        // ステレオ逆相でも Envelope ならピークが残る。
        var frames = PeakPyramid.PlayerDisplayBaseBuckets * 4;
        var samples = new float[frames * 2];
        var peakAt = frames / 3;
        samples[peakAt * 2] = 0.95f;
        samples[peakAt * 2 + 1] = -0.95f;

        var peaks = PeakPyramid.BuildPlayerDisplay(samples, 2, samples.Length);
        Assert.Equal(1, peaks.Channels);
        Assert.Equal(frames, peaks.FrameCount);
        Assert.Equal(frames, peaks.FilledFrames);
        Assert.False(peaks.IsBuilding);
        Assert.True(peaks.BaseBucketFrames >= 4);
        Assert.True(peaks.NeedsEditorDetail);
        Assert.True(peaks.NeedsEditorRebuild(2));
        Assert.True(peaks.NeedsEditorRebuild(1));

        var shortStereo = new float[128];
        var shortPeaks = PeakPyramid.BuildPlayerDisplay(shortStereo, 2, shortStereo.Length);
        Assert.Equal(1, shortPeaks.Channels);
        Assert.False(shortPeaks.NeedsEditorDetail);
        Assert.True(shortPeaks.NeedsEditorRebuild(2));
        Assert.False(shortPeaks.NeedsEditorRebuild(1));

        var mins = new float[128];
        var maxs = new float[128];
        Assert.Equal(128, peaks.ReadRange(0, frames, 128, 0, mins, maxs));
        var column = (int)(peakAt * 128L / frames);
        Assert.True(maxs[column] >= 0.95f - 1e-5);
        Assert.True(mins[column] <= -0.95f + 1e-5);
    }

    [Fact]
    public void FindNextAudibleFrame_SkipsSilentPrefix()
    {
        var frames = PeakPyramid.PlayerDisplayBaseBuckets * 4;
        var samples = new float[frames];
        var audibleAt = frames / 2;
        samples[audibleAt] = 0.5f;
        var peaks = PeakPyramid.BuildPlayerDisplay(samples, 1, samples.Length);
        var threshold = SilentSkip.LinearFromDb(-60);

        Assert.True(peaks.TryIsSilent(0, threshold, holdFrames: 0, out var leading));
        Assert.True(leading);

        var next = peaks.FindNextAudibleFrame(0, frames, threshold);
        Assert.InRange(next, audibleAt - peaks.BaseBucketFrames, audibleAt);

        Assert.True(peaks.TryIsSilent(audibleAt, threshold, holdFrames: 0, out var atAudio));
        Assert.False(atAudio);
    }

    [Fact]
    public void TryIsSilent_EmptyPeaks_CannotDecide()
    {
        Assert.False(PeakPyramid.Empty.TryIsSilent(0, 0.001f, 0, out _));
        Assert.Equal(0, PeakPyramid.Empty.FindNextAudibleFrame(0, 100, 0.001f));
    }
}
