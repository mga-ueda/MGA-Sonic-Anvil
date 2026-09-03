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
}
