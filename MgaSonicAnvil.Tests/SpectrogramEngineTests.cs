using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrogramEngineTests
{
    [Fact]
    public void HertzToUnit_InvertsWithUnitToHertz()
    {
        var hz = SpectrogramEngine.UnitToHertz(0.4, 20, 20000);
        Assert.Equal(0.4, SpectrogramEngine.HertzToUnit(hz, 20, 20000), 8);
    }

    [Fact]
    public void DisplayMaxHertz_StaysAtTwentyKiloRegardlessOfSampleRate()
    {
        Assert.Equal(20000, SpectrogramEngine.DisplayMaxHertz);
        Assert.True(SpectrogramEngine.ContentNyquist(8000) < 5000);
        Assert.True(SpectrogramEngine.ContentNyquist(44100) > 20000);
    }

    [Fact]
    public void FormatHertz_UsesKiloAboveOneThousand()
    {
        Assert.Equal("20", SpectrogramEngine.FormatHertz(20));
        Assert.Equal("700", SpectrogramEngine.FormatHertz(700));
        Assert.Equal("1k", SpectrogramEngine.FormatHertz(1000));
        Assert.Equal("20k", SpectrogramEngine.FormatHertz(20000));
    }

    [Fact]
    public void ColumnIndex_MapsHopWindows()
    {
        Assert.Equal(1, SpectrogramEngine.ColumnCount(1));
        Assert.Equal(2, SpectrogramEngine.ColumnCount(SpectrogramEngine.Hop + 1));
        Assert.Equal(0, SpectrogramEngine.ColumnIndex(0, 10));
        Assert.Equal(1, SpectrogramEngine.ColumnIndex(SpectrogramEngine.Hop, 10));
        Assert.Equal(9, SpectrogramEngine.ColumnIndex(1_000_000, 10));
    }

    [Fact]
    public void DbToLutByte_MapsFloorAndCeiling()
    {
        Assert.Equal(0, SpectrogramEngine.DbToLutByte(SpectrogramEngine.FloorDb));
        Assert.Equal(255, SpectrogramEngine.DbToLutByte(SpectrogramEngine.CeilingDb));
    }

    [Fact]
    public void ColorBgra_FloorIsDarkerThanCeiling()
    {
        var floor = SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb);
        var peak = SpectrogramEngine.ColorBgra(SpectrogramEngine.CeilingDb);
        Assert.True((floor & 0xFF) + ((floor >> 8) & 0xFF) + ((floor >> 16) & 0xFF)
            < (peak & 0xFF) + ((peak >> 8) & 0xFF) + ((peak >> 16) & 0xFF));
    }

    [Fact]
    public void ColorBgra_MidHighIsYellowishAndPeakIsNearWhite()
    {
        static (int R, int G, int B) Unpack(int bgra) =>
            ((bgra >> 16) & 0xFF, (bgra >> 8) & 0xFF, bgra & 0xFF);

        var mid = Unpack(SpectrogramEngine.ColorBgra(-18f));
        Assert.True(mid.R > 220 && mid.G > 160 && mid.B < 120);

        var peak = Unpack(SpectrogramEngine.ColorBgra(SpectrogramEngine.CeilingDb));
        Assert.True(peak.R > 250 && peak.G > 250 && peak.B > 250);
    }
}
