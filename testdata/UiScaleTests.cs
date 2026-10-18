using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiScaleTests
{
    [Theory]
    [InlineData(0, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(125, 125)]
    [InlineData(200, 200)]
    [InlineData(201, 200)]
    [InlineData(-10, 100)]
    public void ClampPercent_StaysInEnlargeRange(int stored, int expected)
    {
        Assert.Equal(expected, UiScale.ClampPercent(stored));
    }

    [Fact]
    public void FactorFrom_IsPercentOver100()
    {
        Assert.Equal(1, UiScale.FactorFrom(100));
        Assert.Equal(1.5, UiScale.FactorFrom(150));
        Assert.Equal(2, UiScale.FactorFrom(200));
        Assert.Equal(1, UiScale.FactorFrom(0));
    }

    [Fact]
    public void FormatPercent_UsesInvariantSuffix()
    {
        Assert.Equal("100%", UiScale.FormatPercent(100));
        Assert.Equal("125%", UiScale.FormatPercent(125));
        Assert.Equal("100%", UiScale.FormatPercent(40));
    }

    [Fact]
    public void IsPreset_KnowsListedPercents()
    {
        Assert.True(UiScale.IsPreset(100));
        Assert.True(UiScale.IsPreset(150));
        Assert.False(UiScale.IsPreset(130));
    }

    [Fact]
    public void AppSettings_ResolvedUiScale_ClampsStoredValues()
    {
        var settings = new AppSettings { UiScalePercent = 80 };
        Assert.Equal(100, settings.ResolvedUiScalePercent());
        Assert.Equal(1, settings.ResolvedUiScale());

        settings.UiScalePercent = 250;
        Assert.Equal(200, settings.ResolvedUiScalePercent());
        Assert.Equal(2, settings.ResolvedUiScale());
    }

    [Fact]
    public void ScaleExtent_UsesFactorRatio()
    {
        Assert.Equal(1500, UiScaleService.ScaleExtent(1000, 1, 1.5));
        Assert.Equal(1000, UiScaleService.ScaleExtent(1500, 1.5, 1));
        Assert.Equal(1000, UiScaleService.ScaleExtent(1000, 0, 1.5));
    }

    [Fact]
    public void StoredExtent_RoundTripsAt150Percent()
    {
        Assert.Equal(1000, UiScaleService.ToStoredExtent(1500, 1.5));
        Assert.Equal(1500, UiScaleService.FromStoredExtent(1000, 1.5));
        Assert.Equal(1000, UiScaleService.ToStoredExtent(1000, 1));
    }

    [Fact]
    public void PublishedTransform_IsFrozenSoItCanBeShared()
    {
        Assert.True(UiScaleService.CreatePublishedTransform(1).IsFrozen);
        Assert.True(UiScaleService.CreatePublishedTransform(1.5).IsFrozen);
    }
}
