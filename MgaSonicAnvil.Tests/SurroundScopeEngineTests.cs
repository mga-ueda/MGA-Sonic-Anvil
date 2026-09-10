using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SurroundScopeEngineTests
{
    [Fact]
    public void IsSurround_StartsAtThreeChannels()
    {
        Assert.False(SurroundScopeEngine.IsSurround(2));
        Assert.True(SurroundScopeEngine.IsSurround(6));
    }

    [Theory]
    [InlineData("C", 0)]
    [InlineData("L", -30)]
    [InlineData("R", 30)]
    [InlineData("Ls", -110)]
    [InlineData("Rs", 110)]
    public void TryAzimuthDegrees_UsesIuFrontZero(string label, double expected)
    {
        Assert.True(SurroundScopeEngine.TryAzimuthDegrees(label, out var degrees));
        Assert.Equal(expected, degrees, 6);
    }

    [Fact]
    public void TryAzimuthDegrees_RejectsLfe()
    {
        Assert.False(SurroundScopeEngine.TryAzimuthDegrees("LFE", out _));
        Assert.True(SurroundScopeEngine.IsLfeLabel("LFE"));
    }

    [Fact]
    public void FillEnvelope_CenterEnergyPeaksAtFront()
    {
        var layout = ChannelLayout.FiveOne;
        var levels = new float[layout.Channels];
        levels[2] = 1f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillEnvelope(levels, layout, envelope);

        var front = envelope[0];
        var rear = envelope[SurroundScopeEngine.EnvelopeSteps / 2];
        Assert.True(front > 0.9f);
        Assert.True(front > rear + 0.5f);
        Assert.Equal(0f, SurroundScopeEngine.LfeLevel(levels, layout));
    }

    [Fact]
    public void LfeLevel_ReadsLfeChannelOnly()
    {
        var layout = ChannelLayout.FiveOne;
        var levels = new float[layout.Channels];
        levels[3] = 0.6f;
        levels[0] = 1f;
        Assert.Equal(0.6f, SurroundScopeEngine.LfeLevel(levels, layout), 5);

        var lfeOnly = new float[layout.Channels];
        lfeOnly[3] = 1f;
        var envelope = new float[SurroundScopeEngine.EnvelopeSteps];
        SurroundScopeEngine.FillEnvelope(lfeOnly, layout, envelope);
        Assert.All(envelope, value => Assert.Equal(0f, value));
    }

    [Fact]
    public void RadiusFromLinear_UsesMeterDbScale()
    {
        Assert.Equal(0, SurroundScopeEngine.RadiusFromLinear(0), 4);
        Assert.Equal(1, SurroundScopeEngine.RadiusFromLinear(1), 4);
        Assert.Equal(LevelMeterEngine.KneeNorm, SurroundScopeEngine.RadiusFromLinear(0.1f), 4);
        Assert.True(SurroundScopeEngine.RadiusFromLinear(0.1f) > 0.4f);
    }

    [Fact]
    public void PolarToXy_FrontIsUp()
    {
        SurroundScopeEngine.PolarToXy(0, 1, 0, 0, 10, out var x, out var y);
        Assert.Equal(0, x, 6);
        Assert.Equal(-10, y, 6);
    }
}
