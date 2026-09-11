using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DevicePortNamesTests
{
    [Fact]
    public void FromChannelMask_UsesSpeakerOrder()
    {
        Assert.Equal(["FL", "FR"], DevicePortNames.FromChannelMask(0x3, 2));
        Assert.Equal(["FL", "FR", "FC", "LFE", "BL", "BR"], DevicePortNames.FromChannelMask(0x3F, 6));
        Assert.Equal(["FL", "FR", "FC", "LFE", "BL", "BR", "SL", "SR"], DevicePortNames.FromChannelMask(0x63F, 8));
    }

    [Fact]
    public void FromChannelMask_EmptyFallsBackToChannelLabels()
    {
        Assert.Equal(["1", "2"], DevicePortNames.FromChannelMask(0, 2));
        Assert.Equal(["1"], DevicePortNames.FromChannelMask(0, 1));
    }

    [Fact]
    public void Numbered_UsesInOutPrefix()
    {
        Assert.Equal(["In 1", "In 2"], DevicePortNames.Numbered(2, input: true));
        Assert.Equal(["Out 1", "Out 2", "Out 3"], DevicePortNames.Numbered(3, input: false));
    }

    [Fact]
    public void FromChannelCount_MatchesChannelLabels()
    {
        Assert.Equal(["1", "2", "3", "4", "5", "6"], DevicePortNames.FromChannelCount(6));
    }
}
