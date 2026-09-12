using System.Windows;
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
}
