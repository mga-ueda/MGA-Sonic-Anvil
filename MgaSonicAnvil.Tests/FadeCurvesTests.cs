using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class FadeCurvesTests
{
    [Fact]
    public void MenuOrder_MatchesTimeCasterFadeIn()
    {
        FadeShape[] expected =
        [
            FadeShape.Log3,
            FadeShape.Sine,
            FadeShape.Log1,
            FadeShape.InvSCurve,
            FadeShape.Linear,
            FadeShape.SCurve,
            FadeShape.Exp1,
            FadeShape.ReciprocalSine,
            FadeShape.Exp3,
        ];
        Assert.Equal(expected, FadeCurves.MenuOrderFadeIn);
    }

    [Fact]
    public void MenuOrder_MatchesTimeCasterFadeOut()
    {
        FadeShape[] expected =
        [
            FadeShape.Exp3,
            FadeShape.ReciprocalSine,
            FadeShape.Exp1,
            FadeShape.InvSCurve,
            FadeShape.Linear,
            FadeShape.SCurve,
            FadeShape.Log1,
            FadeShape.Sine,
            FadeShape.Log3,
        ];
        Assert.Equal(expected, FadeCurves.MenuOrderFadeOut);
    }

    [Fact]
    public void Default_IsSCurve()
    {
        Assert.Equal(FadeShape.SCurve, FadeCurves.Default);
        Assert.DoesNotContain(FadeShape.Constant, FadeCurves.MenuOrderFadeIn);
        Assert.DoesNotContain(FadeShape.Constant, FadeCurves.MenuOrderFadeOut);
        Assert.Equal(5, FadeCurves.MenuOrderFadeIn.ToList().IndexOf(FadeCurves.Default));
        Assert.Equal(5, FadeCurves.MenuOrderFadeOut.ToList().IndexOf(FadeCurves.Default));
    }

    [Theory]
    [InlineData(0d, 0f)]
    [InlineData(0.5d, 0.5f)]
    [InlineData(1d, 1f)]
    public void SCurve_HasExpectedEndpointsAndMid(double t, float expected)
    {
        Assert.Equal(expected, FadeCurves.Apply01(FadeShape.SCurve, t), 5);
    }

    [Fact]
    public void FadeOutGain_IsOneMinusRising()
    {
        Assert.Equal(1f, FadeCurves.Gain(FadeShape.SCurve, fadeIn: false, 0d), 5);
        Assert.Equal(0.5f, FadeCurves.Gain(FadeShape.SCurve, fadeIn: false, 0.5d), 5);
        Assert.Equal(0f, FadeCurves.Gain(FadeShape.SCurve, fadeIn: false, 1d), 5);
    }

    [Fact]
    public void Exp3_RisesSlowerThanLinear()
    {
        Assert.Equal(0.125f, FadeCurves.Apply01(FadeShape.Exp3, 0.5d), 5);
        Assert.True(FadeCurves.Apply01(FadeShape.Exp3, 0.5d) < FadeCurves.Apply01(FadeShape.Linear, 0.5d));
    }

    [Fact]
    public void Log3_RisesFasterThanLinear()
    {
        Assert.Equal(0.875f, FadeCurves.Apply01(FadeShape.Log3, 0.5d), 5);
        Assert.True(FadeCurves.Apply01(FadeShape.Log3, 0.5d) > FadeCurves.Apply01(FadeShape.Linear, 0.5d));
    }
}
