using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolbarIconScaleTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(150, 150)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(400, 200)]
    [InlineData(-10, 100)]
    public void ClampPercent_StaysInEnlargeRange(int stored, int expected)
    {
        Assert.Equal(expected, TransportToolbarIconScale.ClampPercent(stored));
    }

    [Fact]
    public void FactorFrom_IsPercentOver100()
    {
        Assert.Equal(1, TransportToolbarIconScale.FactorFrom(100));
        Assert.Equal(1.5, TransportToolbarIconScale.FactorFrom(150));
        Assert.Equal(2, TransportToolbarIconScale.FactorFrom(200));
        Assert.Equal(1, TransportToolbarIconScale.FactorFrom(0));
    }

    [Fact]
    public void FormatPercent_UsesInvariantSuffix()
    {
        Assert.Equal("100%", TransportToolbarIconScale.FormatPercent(100));
        Assert.Equal("200%", TransportToolbarIconScale.FormatPercent(200));
        Assert.Equal("100%", TransportToolbarIconScale.FormatPercent(40));
    }

    [Fact]
    public void IsPreset_KnowsListedPercents()
    {
        Assert.True(TransportToolbarIconScale.IsPreset(100));
        Assert.True(TransportToolbarIconScale.IsPreset(200));
        Assert.False(TransportToolbarIconScale.IsPreset(130));
        Assert.False(TransportToolbarIconScale.IsPreset(400));
    }

    [Fact]
    public void AppSettings_ResolvedScale_ClampsStoredValues()
    {
        var settings = new AppSettings { TransportToolbarIconScalePercent = 80 };
        Assert.Equal(100, settings.ResolvedTransportToolbarIconScalePercent());
        Assert.Equal(1, settings.ResolvedTransportToolbarIconScale());

        settings.TransportToolbarIconScalePercent = 500;
        Assert.Equal(200, settings.ResolvedTransportToolbarIconScalePercent());
        Assert.Equal(2, settings.ResolvedTransportToolbarIconScale());
    }

    [Fact]
    public void Default_MatchesCurrentButtonSide()
    {
        Assert.Equal(TransportToolbarIconScale.DefaultPercent, 100);
        Assert.Equal(
            DesignMetrics.TransportButtonSide,
            DesignMetrics.TransportButtonSideAt(1));
        Assert.Equal(
            DesignMetrics.TransportButtonSide * 2,
            DesignMetrics.TransportButtonSideAt(2));
        Assert.Equal(
            DesignMetrics.From96(2) + DesignMetrics.TransportButtonSide * 2,
            DesignMetrics.TransportRowHeightAt(2));
    }
}
