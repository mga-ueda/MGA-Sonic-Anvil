using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VectorScopeEngineTests
{
    [Fact]
    public void MidSide_MonoStaysVertical()
    {
        VectorScopeEngine.MidSide(0.8f, 0.8f, out var mid, out var side);
        Assert.Equal(0.8f, mid, 5);
        Assert.Equal(0f, side, 5);
    }

    [Fact]
    public void MidSide_AntiPhaseStaysHorizontal()
    {
        VectorScopeEngine.MidSide(0.6f, -0.6f, out var mid, out var side);
        Assert.Equal(0f, mid, 5);
        Assert.Equal(0.6f, side, 5);
    }

    [Fact]
    public void Correlation_IdenticalChannelsIsPlusOne()
    {
        var left = new float[] { 0.2f, -0.4f, 0.8f, 0.1f };
        Assert.Equal(1d, VectorScopeEngine.Correlation(left, left), 9);
    }

    [Fact]
    public void Correlation_InvertedChannelIsMinusOne()
    {
        var left = new float[] { 0.2f, -0.4f, 0.8f, 0.1f };
        var right = new float[] { -0.2f, 0.4f, -0.8f, -0.1f };
        Assert.Equal(-1d, VectorScopeEngine.Correlation(left, right), 9);
    }

    [Fact]
    public void Correlation_SilenceIsZero()
    {
        var zeros = new float[8];
        Assert.Equal(0d, VectorScopeEngine.Correlation(zeros, zeros), 9);
    }

    [Fact]
    public void FormatCorrelation_KeepsSignAndTwoDecimals()
    {
        Assert.Equal("+0.69", VectorScopeEngine.FormatCorrelation(0.691));
        Assert.Equal("-0.12", VectorScopeEngine.FormatCorrelation(-0.124));
        Assert.Equal("+0.00", VectorScopeEngine.FormatCorrelation(0));
    }
}
