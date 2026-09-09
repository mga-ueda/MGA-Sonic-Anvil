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
    public void PreferLiveFft_ZoomedOutUsesCacheAsLod()
    {
        Assert.False(SpectrogramEngine.PreferLiveFft(SpectrogramEngine.Hop, 10_000_000, 10_000_000));
        Assert.False(SpectrogramEngine.PreferLiveFft(SpectrogramEngine.Hop * 4, 10_000_000, 10_000_000));
    }

    [Fact]
    public void PreferLiveFft_ShortFileFitStaysLive()
    {
        // 6 秒 @ 48 kHz を 1500px に載せると 1px あたり約 192 フレーム。全体が見えているのでライブ。
        Assert.True(SpectrogramEngine.PreferLiveFft(287088d / 1500d, 287088, 287088));
        // 0.5 秒の SE でもフィット表示ならライブ（スクロールしないので重くならない）。
        Assert.True(SpectrogramEngine.PreferLiveFft(24000d / 1500d, 24000, 24000));
    }

    [Fact]
    public void NormalizeGainDb_LiftsQuietContentToFullScale()
    {
        // ピーク 0.5 は約 +6 dB、ピーク 0.1 は +20 dB 持ち上げる。
        Assert.Equal(6.02, SpectrogramEngine.NormalizeGainDb([0.5f, -0.25f]), 2);
        Assert.Equal(20.0, SpectrogramEngine.NormalizeGainDb([0.1f]), 2);
        // フルスケールは変化なし。無音も変化なし（持ち上げない）。
        Assert.Equal(0.0, SpectrogramEngine.NormalizeGainDb([1f, -1f]), 5);
        Assert.Equal(0.0, SpectrogramEngine.NormalizeGainDb([0f, 0f]), 5);
        // 極端に小さいピークは +96 dB で頭打ち。
        Assert.Equal(96.0, SpectrogramEngine.NormalizeGainDb([1e-7f]), 2);
    }

    [Fact]
    public void WriteColumnLut_AppliesDisplayGain()
    {
        Span<byte> plain = stackalloc byte[1];
        Span<byte> lifted = stackalloc byte[1];
        ReadOnlySpan<double> magnitude = [0.01]; // -40 dB
        SpectrogramEngine.WriteColumnLut(magnitude, plain);
        SpectrogramEngine.WriteColumnLut(magnitude, lifted, 40f);
        Assert.True(lifted[0] > plain[0]);
        Assert.Equal(SpectrogramEngine.DbToLutByte(0f), lifted[0]);
    }

    [Fact]
    public void PreferLiveFft_AnyZoomedViewUsesCacheColumns()
    {
        // 一部だけ表示しているとき（＝スクロールが起こりうる）は、深さによらずキャッシュ列で軽く描く。
        Assert.False(SpectrogramEngine.PreferLiveFft(8, 8 * 1500d, 10_000_000));
        Assert.False(SpectrogramEngine.PreferLiveFft(100, 100 * 1500d, 10_000_000));
        Assert.False(SpectrogramEngine.PreferLiveFft(SpectrogramEngine.Hop - 1, 700_000, 10_000_000));
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
