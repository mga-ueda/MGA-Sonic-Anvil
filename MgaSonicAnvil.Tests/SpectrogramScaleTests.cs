using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrogramScaleTests
{
    [Fact]
    public void FrequencyLabel_IsSoftWhiteWithBlackHalo()
    {
        Assert.Equal(Color.FromRgb(0xEB, 0xEB, 0xEB), SpectrogramRenderer.FrequencyLabelFill);
        Assert.Equal(Colors.Black, SpectrogramRenderer.FrequencyLabelEdge);
        Assert.Equal(8, SpectrogramRenderer.FrequencyLabelHalo.Length);
        Assert.Contains((-1, 0), SpectrogramRenderer.FrequencyLabelHalo);
        Assert.DoesNotContain((0, 0), SpectrogramRenderer.FrequencyLabelHalo);
    }

    [Fact]
    public void BoostBar_FitsInsideDbScaleWell()
    {
        Assert.Equal(DesignMetrics.WaveformScrollBarHeight * 0.5, DesignMetrics.SpectrogramBoostBarWidth);
        Assert.True(DesignMetrics.SpectrogramBoostThumbSize > DesignMetrics.SpectrogramBoostBarWidth);
        Assert.True(DesignMetrics.SpectrogramBoostThumbSize < DesignMetrics.SpectrogramBoostBarWidth * 2);
        Assert.True(DesignMetrics.DbScaleWidth > DesignMetrics.SpectrogramBoostThumbSize + 4);
        Assert.Equal(8, DesignMetrics.SpectrogramBoostBarPad);
        Assert.Equal(27, DesignMetrics.SpectrogramBoostLabelReserve(20));
        Assert.Equal((DesignMetrics.DbScaleWidth - DesignMetrics.SpectrogramBoostThumbSize) * 0.5, DesignMetrics.SpectrogramBoostBarLeft(), 5);
        Assert.Equal(0.5, DesignMetrics.SpectrogramBoostBarLeft(20), 5);
        Assert.True(DesignMetrics.SpectrogramBoostBarLeft(20) < DesignMetrics.SpectrogramBoostBarLeft());
        Assert.Equal(Color.FromRgb(0x9C, 0x2E, 0x00), SpectrogramBoostBar.TrackOrange);
        Assert.Equal(Colors.White, SpectrogramBoostBar.TrackWhite);
    }
}
