using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelWavePaintTests
{
    private const int OpaqueGray = unchecked((int)0xFF626262);

    [Fact]
    public void Dim_Dark_DarkensTowardBlack()
    {
        var dim = ChannelWavePaint.Dim(OpaqueGray, bright: false);
        Assert.Equal(0x62 * 2 / 7, (dim >> 16) & 0xFF);
        Assert.Equal(0x62 * 2 / 7, (dim >> 8) & 0xFF);
        Assert.Equal(0x62 * 2 / 7, dim & 0xFF);
    }

    [Fact]
    public void Dim_Bright_BecomesLightGray()
    {
        var dim = ChannelWavePaint.Dim(OpaqueGray, bright: true);
        var r = (dim >> 16) & 0xFF;
        Assert.InRange(r, 0xB8, 0xE0);
        Assert.Equal(r, (dim >> 8) & 0xFF);
        Assert.Equal(r, dim & 0xFF);
        Assert.True(r > 0x62);
    }

    [Fact]
    public void Dim_Bright_LiftsAlreadyDarkWave()
    {
        var dim = ChannelWavePaint.Dim(unchecked((int)0xFF2F2F2F), bright: true);
        Assert.True(((dim >> 16) & 0xFF) >= 0xB0);
    }
}
