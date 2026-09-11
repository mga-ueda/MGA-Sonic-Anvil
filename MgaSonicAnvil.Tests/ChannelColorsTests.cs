using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelColorsTests
{
    [Fact]
    public void Count_MatchesAtmosNineOneSix()
    {
        Assert.Equal(16, ChannelColors.Count);
        Assert.Equal(ChannelLayout.MaxChannels, ChannelColors.Count);
        Assert.Equal(16, ChannelLayout.Parse("9.1.6").Channels);
    }

    [Fact]
    public void Order_HasDistinctColors()
    {
        var seen = new HashSet<(byte, byte, byte)>();
        for (var i = 0; i < ChannelColors.Count; i++)
        {
            var c = ChannelColors.At(i);
            Assert.True(seen.Add((c.R, c.G, c.B)), $"色 {i} が重複");
        }
    }

    [Fact]
    public void At_WrapsPastAtmos()
    {
        Assert.Equal(ChannelColors.At(0), ChannelColors.At(16));
        Assert.Equal(ChannelColors.At(1), ChannelColors.At(-15));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(16, true)]
    public void UsesLaneTint_OnlyAboveStereo(int channels, bool expected)
    {
        Assert.Equal(expected, ChannelColors.UsesLaneTint(channels));
    }

    [Fact]
    public void Dim_KeepsHueButDarker()
    {
        var lit = ChannelColors.At(0);
        var dim = ChannelColors.Dim(0);
        Assert.True(dim.R + dim.G + dim.B < lit.R + lit.G + lit.B);
        Assert.NotEqual(lit, dim);
    }
}
