using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveformLaneGradientTests
{
    private const int Cyan = unchecked((int)0xFFC6D9FF);

    [Fact]
    public void Shade_Center_KeepsBrightness()
    {
        Assert.Equal(Cyan, WaveformLaneGradient.Shade(Cyan, mid: 50, y: 50, halfHeight: 40));
    }

    [Fact]
    public void Shade_Edge_IsDarker()
    {
        var edge = WaveformLaneGradient.Shade(Cyan, y: 10, mid: 50, halfHeight: 40);
        Assert.True(((edge >> 16) & 0xFF) < ((Cyan >> 16) & 0xFF));
        Assert.True(((edge >> 8) & 0xFF) < ((Cyan >> 8) & 0xFF));
        Assert.Equal(
            (int)Math.Round(0xC6 * WaveformLaneGradient.EdgeBrightness),
            (edge >> 16) & 0xFF);
    }

    [Fact]
    public void Shade_IsSymmetricUpAndDown()
    {
        var up = WaveformLaneGradient.Shade(Cyan, y: 20, mid: 50, halfHeight: 40);
        var down = WaveformLaneGradient.Shade(Cyan, y: 80, mid: 50, halfHeight: 40);
        Assert.Equal(up, down);
    }

    [Fact]
    public void PlayerFill_Light_IsPalerThanSource()
    {
        var navy = unchecked((int)0xFF405273);
        var pale = WaveformLaneGradient.PlayerFill(navy, UiTheme.Light);
        Assert.True(((pale >> 16) & 0xFF) > ((navy >> 16) & 0xFF));
        Assert.True(((pale >> 8) & 0xFF) > ((navy >> 8) & 0xFF));
        Assert.True((pale & 0xFF) > (navy & 0xFF));
        Assert.Equal(navy, WaveformLaneGradient.PlayerFill(navy, UiTheme.Dark));
    }

    [Fact]
    public void Shade_Light_EdgeIsPaler()
    {
        var navy = unchecked((int)0xFF405273);
        var edge = WaveformLaneGradient.Shade(navy, y: 10, mid: 50, halfHeight: 40, UiTheme.Light);
        Assert.True(((edge >> 16) & 0xFF) > ((navy >> 16) & 0xFF));
    }

    [Fact]
    public void PlayerWaveOpacity_LightIsPalerThanDark()
    {
        Assert.Equal(0.58, WaveformView.PlayerWaveOpacityFor(UiTheme.Dark));
        Assert.True(WaveformView.PlayerWaveOpacityFor(UiTheme.Light) < WaveformView.PlayerWaveOpacityFor(UiTheme.Dark));
        Assert.Equal(WaveformView.PlayerWaveOpacityLight, WaveformView.PlayerWaveOpacityFor(UiTheme.Light));
    }
}
