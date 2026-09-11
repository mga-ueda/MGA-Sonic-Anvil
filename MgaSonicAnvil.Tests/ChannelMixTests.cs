using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelMixTests
{
    [Fact]
    public void Mid_InvertedStereoCancels()
    {
        Assert.Equal(0f, ChannelMix.Mid([0.82f, -0.82f]), 5);
    }

    [Fact]
    public void Mid_IncludesEverySurroundChannel()
    {
        Assert.True(Math.Abs(ChannelMix.Mid([0f, 0f, 0.8f, 0f, 0f, 0f])) > 0.1f);
        Assert.True(Math.Abs(ChannelMix.Mid([0f, 0f, 0f, 0f, 0f, 0.8f])) > 0.1f);
        Assert.True(Math.Abs(ChannelMix.Mid([0f, 0f, 0f, 0.8f, 0f, 0f, 0f, 0f])) > 0.1f);
    }

    [Fact]
    public void FrameEnvelope_KeepsInvertedStereo()
    {
        var samples = new float[] { 0.82f, -0.74f };
        ChannelMix.FrameEnvelope(samples, 2, 0, 1, out var min, out var max);
        Assert.Equal(-0.74f, min, 5);
        Assert.Equal(0.82f, max, 5);
    }

    [Fact]
    public void FoldPackedPeaksToMid_AveragesChannelPeaks()
    {
        var mins = new[] { 0.20f, 0.40f };
        var maxs = new[] { 0.60f, 0.80f };

        ChannelMix.FoldPackedPeaksToMid(mins, maxs, count: 1, channels: 2);

        Assert.Equal(0.30f, mins[0], 5);
        Assert.Equal(0.70f, maxs[0], 5);
    }

    [Fact]
    public void FoldPackedPeaksToUnion_OppositeChannelsStayVisible()
    {
        var mins = new[] { 0.55f, -0.91f, 0.40f, -0.88f };
        var maxs = new[] { 0.90f, -0.50f, 0.85f, -0.42f };

        ChannelMix.FoldPackedPeaksToUnion(mins, maxs, count: 2, channels: 2);

        Assert.Equal(-0.91f, mins[0], 5);
        Assert.Equal(0.90f, maxs[0], 5);
        Assert.Equal(-0.88f, mins[1], 5);
        Assert.Equal(0.85f, maxs[1], 5);
    }

    [Fact]
    public void FoldPackedPeaksToUnion_MidOfPeaksWouldHideOppositeChannels()
    {
        var mins = new[] { 0.55f, -0.91f };
        var maxs = new[] { 0.90f, -0.50f };

        var midMin = ChannelMix.Mid(mins);
        var midMax = ChannelMix.Mid(maxs);
        Assert.InRange(midMin, -0.2f, 0.2f);
        Assert.InRange(midMax, -0.2f, 0.2f);

        ChannelMix.FoldPackedPeaksToUnion(mins, maxs, count: 1, channels: 2);
        Assert.True(maxs[0] - mins[0] > 1.4f);
    }
}
