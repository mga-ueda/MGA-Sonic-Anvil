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
    public void ShouldMirrorMono_WhenMonoHasTwoOrMorePorts()
    {
        Assert.True(ChannelRouter.ShouldMirrorMono(1, 2));
        Assert.True(ChannelRouter.ShouldMirrorMono(1, 8));
        Assert.False(ChannelRouter.ShouldMirrorMono(2, 2));
        Assert.False(ChannelRouter.ShouldMirrorMono(1, 1));
    }

    [Fact]
    public void MonoPorts_UsesMappedStereoPortsWhenAvailable()
    {
        Assert.Equal((0, 1), ChannelRouter.MonoPorts(8, null));
        Assert.Equal((0, 1), ChannelRouter.MonoPorts(8, []));
        // 保存済みステレオマップ（例: ASIO の再生ポート 0/1）はそのままミラー先になる。
        Assert.Equal((0, 1), ChannelRouter.MonoPorts(18, [0, 1]));
        Assert.Equal((2, 3), ChannelRouter.MonoPorts(8, [2, 3]));
        // マップが 1 要素しか無ければ隣のポートへ。
        Assert.Equal((4, 5), ChannelRouter.MonoPorts(8, [4]));
        Assert.Equal((0, 1), ChannelRouter.MonoPorts(2, [0, 5]));
    }

    [Fact]
    public void MapInterleaved_GatherBuildsDestFrames()
    {
        var source = new float[] { 1f, 2f, 3f, 4f };
        var mapped = ChannelRouter.MapInterleaved(source, 2, 4, [1, 0, ChannelRouter.Off, 1], gather: true);
        Assert.Equal([2f, 1f, 0f, 2f, 4f, 3f, 0f, 4f], mapped);
    }
}
