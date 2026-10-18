using System.Windows;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LoudnessPenHitTests
{
    [Fact]
    public void NearCurve_IncludesTheOneLuBand()
    {
        var profile = new LoudnessProfile(48000, 1, [-20f, -20f]);
        var wave = new Rect(10, 20, 200, 160);
        const double target = -14;
        var y = LoudnessOverlayRenderer.LufsToY(-20, wave, target);
        var onCurve = new Point(wave.X + 40, y);
        var inBand = new Point(
            wave.X + 80,
            LoudnessOverlayRenderer.LufsToY(-21, wave, target));
        var offCurve = new Point(wave.X + 40, wave.Bottom - 1);

        Assert.True(LoudnessOverlayRenderer.IsNearCurve(onCurve, wave, profile, target, 0, 100));
        Assert.True(LoudnessOverlayRenderer.IsNearCurve(inBand, wave, profile, target, 0, 100));
        Assert.False(LoudnessOverlayRenderer.IsNearCurve(offCurve, wave, profile, target, 0, 100));
        Assert.False(LoudnessOverlayRenderer.IsNearCurve(new Point(0, y), wave, profile, target, 0, 100));
    }

    [Fact]
    public void GapInProfile_IsNotAHit()
    {
        var profile = new LoudnessProfile(48000, 1, [float.NegativeInfinity]);
        var wave = new Rect(0, 0, 100, 80);
        var y = LoudnessOverlayRenderer.LufsToY(-14, wave, -14);

        Assert.False(LoudnessOverlayRenderer.IsNearCurve(new Point(20, y), wave, profile, -14, 0, 50));
    }
}
