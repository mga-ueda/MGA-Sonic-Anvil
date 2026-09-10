using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelRouterTests
{
    [Fact]
    public void Parse_UnknownFallsBackToStereo()
    {
        Assert.Equal("Stereo", ChannelLayout.Parse(null).Id);
        Assert.Equal("9.1.6", ChannelLayout.Parse("9.1.6").Id);
        Assert.Equal(16, ChannelLayout.Parse("9.1.6").Channels);
        Assert.Equal(12, ChannelLayout.All.Length);
    }

    [Fact]
    public void Gather_MapsPortsOntoLogicalChannels()
    {
        var source = new float[] { 0.1f, 0.2f, 0.3f };
        var dest = new float[2];
        ChannelRouter.Gather(source, dest, [2, ChannelRouter.Off]);
        Assert.Equal(0.3f, dest[0]);
        Assert.Equal(0f, dest[1]);
    }

    [Fact]
    public void Scatter_SendsLogicalChannelsToPorts()
    {
        var source = new float[] { 0.5f, 0.25f };
        var dest = new float[4];
        ChannelRouter.Scatter(source, dest, [3, 1]);
        Assert.Equal(0f, dest[0]);
        Assert.Equal(0.25f, dest[1]);
        Assert.Equal(0f, dest[2]);
        Assert.Equal(0.5f, dest[3]);
    }

    [Fact]
    public void ShouldDownmix_OnlyWhenStereoDeviceAndNoMap()
    {
        Assert.True(ChannelRouter.ShouldDownmix(6, 2, []));
        Assert.False(ChannelRouter.ShouldDownmix(6, 8, []));
        Assert.False(ChannelRouter.ShouldDownmix(6, 2, [0, 1, 0, 1, 0, 1]));
        Assert.False(ChannelRouter.ShouldDownmix(2, 2, []));
    }

    [Fact]
    public void MapInterleaved_GatherBuildsDestFrames()
    {
        var source = new float[] { 1f, 2f, 3f, 4f };
        var mapped = ChannelRouter.MapInterleaved(source, 2, 4, [1, 0, ChannelRouter.Off, 1], gather: true);
        Assert.Equal([2f, 1f, 0f, 2f, 4f, 3f, 0f, 4f], mapped);
    }
}
