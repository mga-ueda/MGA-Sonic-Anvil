using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LevelColorThemeTests
{
    [Fact]
    public void ColorNorm_FollowsPlotDbAndStaysMonotone()
    {
        Assert.Equal(0, LevelColorTheme.ColorNorm(SpectrumAnalyzer.FloorDb), 6);
        Assert.Equal(1, LevelColorTheme.ColorNorm(SpectrumAnalyzer.CeilingDb), 6);
        Assert.Equal(0, LevelColorTheme.ColorNorm(LevelMeterEngine.DbMin), 6);
        Assert.Equal(0.5, LevelColorTheme.ColorNorm((SpectrumAnalyzer.FloorDb + SpectrumAnalyzer.CeilingDb) / 2f), 3);

        var prev = LevelColorTheme.ColorNorm(SpectrumAnalyzer.FloorDb);
        for (var db = SpectrumAnalyzer.FloorDb + 1; db <= SpectrumAnalyzer.CeilingDb; db++)
        {
            var t = LevelColorTheme.ColorNorm(db);
            Assert.True(t + 1e-9 >= prev, $"db={db}");
            prev = t;
        }
    }

    [Fact]
    public void Color_KeepsEndsAndAvoidsSuddenLumaJumps()
    {
        Assert.Equal(new ColorRgb(16, 62, 86), LevelColorTheme.Of(SpectrumAnalyzer.FloorDb));
        Assert.Equal(new ColorRgb(248, 254, 255), LevelColorTheme.Of(SpectrumAnalyzer.CeilingDb));

        var prev = Luma(LevelColorTheme.Of(SpectrumAnalyzer.FloorDb));
        var maxStep = 0d;
        for (var db = SpectrumAnalyzer.FloorDb + 1; db <= SpectrumAnalyzer.CeilingDb; db++)
        {
            var luma = Luma(LevelColorTheme.Of(db));
            Assert.True(luma + 1e-6 >= prev, $"db={db}");
            maxStep = Math.Max(maxStep, luma - prev);
            prev = luma;
        }

        Assert.True(maxStep < 0.035, $"maxStep={maxStep}");
    }

    [Fact]
    public void Light_DarkensPeakHoldOnly()
    {
        foreach (var db in new[] { -28d, -10d, 0d })
        {
            Assert.Equal(LevelColorTheme.Of(db), LevelColorTheme.PeakHold(db, UiTheme.Dark));
            Assert.True(
                Luma(LevelColorTheme.PeakHold(db, UiTheme.Light))
                < Luma(LevelColorTheme.PeakHold(db, UiTheme.Dark)));
        }
    }

    private static double Luma(ColorRgb color)
    {
        static double Lin(byte channel)
        {
            var s = channel / 255d;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Lin(color.R)) + (0.7152 * Lin(color.G)) + (0.0722 * Lin(color.B));
    }
}
