using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VectorScopeLayoutTests
{
    [Fact]
    public void SurroundScope_SharesGoniometerTop()
    {
        var bounds = new Rect(0, 0, DesignMetrics.LevelMeterWidth, DesignMetrics.VectorScopeHeight);
        var gonio = VectorScopeView.MeasureLayout(bounds).Scope;
        var surround = VectorScopeView.SurroundScopeRect(bounds);
        Assert.Equal(gonio, surround);
        Assert.Equal(bounds.Top, surround.Top);
        Assert.Equal(DesignMetrics.LevelMeterWidth, surround.Height, 3);
    }

    [Fact]
    public void MeasureLayout_KeepsCorrelationUnderScope()
    {
        var bounds = new Rect(0, 0, DesignMetrics.LevelMeterWidth, DesignMetrics.VectorScopeHeight);
        var layout = VectorScopeView.MeasureLayout(bounds);
        Assert.Equal(layout.Scope.Bottom, layout.Correlation.Top);
        Assert.Equal(DesignMetrics.VectorScopeCorrelationHeight, layout.Correlation.Height);
        Assert.True(
            VectorScopeView.SurroundScopeRect(layout.Scope).Height < layout.Scope.Height,
            "scope rect is already square; measuring it again would shrink the surround view");
    }

    [Fact]
    public void FadePixel_KeepsRgbAndLowersAlpha()
    {
        var pixel = (200 << 24) | (0x3A << 16) | (0xB8 << 8) | 0xE8;
        var faded = VectorScopeView.FadePixel(pixel, fade: 0.5f, cutoff: 6);
        Assert.Equal(100, (faded >> 24) & 0xFF);
        Assert.Equal(0x3A, (faded >> 16) & 0xFF);
        Assert.Equal(0xB8, (faded >> 8) & 0xFF);
        Assert.Equal(0xE8, faded & 0xFF);
    }

    [Fact]
    public void StampBlend_KeepsTraceHue()
    {
        var color = Color.FromRgb(0x3A, 0xB8, 0xE8);
        var stamped = VectorScopeView.StampBlend(0, color, 180);
        Assert.Equal(180, (stamped >> 24) & 0xFF);
        Assert.Equal(color.R, (stamped >> 16) & 0xFF);
        Assert.Equal(color.G, (stamped >> 8) & 0xFF);
        Assert.Equal(color.B, stamped & 0xFF);
    }
}
