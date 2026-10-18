using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrogramEngineTests
{
    [Fact]
    public void HertzToUnit_InvertsWithUnitToHertz()
    {
        var hz = SpectrogramEngine.UnitToHertz(0.4, SpectrogramEngine.MinHertz, SpectrogramEngine.MaxHertz);
        Assert.Equal(0.4, SpectrogramEngine.HertzToUnit(hz, SpectrogramEngine.MinHertz, SpectrogramEngine.MaxHertz), 8);
    }

    [Fact]
    public void DisplayMaxHertz_StaysAtTwentyFourKiloRegardlessOfSampleRate()
    {
        Assert.Equal(24000, SpectrogramEngine.DisplayMaxHertz);
        Assert.True(SpectrogramEngine.ContentNyquist(8000) < 5000);
        Assert.True(SpectrogramEngine.ContentNyquist(44100) > 20000);
        Assert.True(SpectrogramEngine.ContentNyquist(48000) > 22000);
    }

    [Fact]
    public void HertzToUnit_CompressesBassAndLeavesRoomAboveTwentyKilo()
    {
        var min = SpectrogramEngine.MinHertz;
        var max = SpectrogramEngine.MaxHertz;
        var at100 = SpectrogramEngine.HertzToUnit(100, min, max);
        var at200 = SpectrogramEngine.HertzToUnit(200, min, max);
        var at10k = SpectrogramEngine.HertzToUnit(10000, min, max);
        var at20k = SpectrogramEngine.HertzToUnit(20000, min, max);
        // 100 Hz 以下はほぼ底。低域の 1oct は高域の 1oct より狭い。
        Assert.True(at100 < 0.04);
        Assert.True(at200 - at100 < at20k - at10k);
        Assert.True(at20k < 0.98);
        Assert.Equal(1, SpectrogramEngine.HertzToUnit(max, min, max), 5);
    }

    [Fact]
    public void FormatHertz_UsesKiloAboveOneThousand()
    {
        Assert.Equal("20", SpectrogramEngine.FormatHertz(20));
        Assert.Equal("700", SpectrogramEngine.FormatHertz(700));
        Assert.Equal("1k", SpectrogramEngine.FormatHertz(1000));
        Assert.Equal("20k", SpectrogramEngine.FormatHertz(20000));
        Assert.Equal("24k", SpectrogramEngine.FormatHertz(24000));
    }

    [Fact]
    public void FillMonoMix_ReflectsOutsideFileInsteadOfZero()
    {
        float[] samples = [1, 2, 3, 4, 5];
        var dest = new float[5];
        SpectrogramEngine.FillMonoMix(samples, 1, origin: -2, frames: 5, dest);
        Assert.Equal([3f, 2f, 1f, 2f, 3f], dest);
        SpectrogramEngine.FillMonoMix(samples, 1, origin: 3, frames: 5, dest);
        Assert.Equal([4f, 5f, 4f, 3f, 2f], dest);
    }

    [Fact]
    public void AnalyzeWindow_FileStartDoesNotSprayBroadband()
    {
        const int rate = 48000;
        var samples = new float[rate];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate);
        }

        var window = new float[SpectrogramEngine.FftSize];
        SpectrogramEngine.FillHann(window, out var sum);
        var mix = new float[SpectrogramEngine.FftSize];
        var re = new double[SpectrogramEngine.FftSize];
        var im = new double[SpectrogramEngine.FftSize];

        float HighBinDb(long center)
        {
            SpectrogramEngine.FillMonoMix(samples, 1, center - SpectrogramEngine.FftSize / 2, samples.Length, mix);
            SpectrogramEngine.AnalyzeWindow(mix, window, sum, re, im);
            var bin = 8000d * SpectrogramEngine.FftSize / rate;
            return SpectrogramEngine.BinDb(re.AsSpan(0, SpectrogramEngine.BinCount), bin);
        }

        // ゼロ埋めだと先頭だけ全帯域に漏れ、フロアより上に出て赤い帯になる。
        // 折り返しなら 8 kHz はフロア以下のまま（中盤の数値ノイズよりは高い）。
        var start = HighBinDb(0);
        var mid = HighBinDb(rate / 2);
        Assert.True(start <= SpectrogramEngine.FloorDb, $"start {start} dB");
        Assert.True(mid <= SpectrogramEngine.FloorDb, $"mid {mid} dB");
        Assert.Equal(0, SpectrogramEngine.DbToLutByte(start));
        Assert.Equal(0, SpectrogramEngine.DbToLutByte(mid));
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
    public void ColorBgraFromMagnitude_SilenceStaysFloorEvenWithDisplayGain()
    {
        // ピークが小さいファイルは +20 dB など乗る。無音に足すとフロアが赤帯になる。
        var silent = SpectrogramEngine.ColorBgraFromMagnitude(0, 20f);
        var floor = SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb);
        var liftedFloor = SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb + 20f);
        Assert.Equal(floor, silent);
        Assert.NotEqual(floor, liftedFloor);
        Assert.Equal(0, SpectrogramEngine.LutByteFromMagnitude(0, 20f));
        Assert.Equal(SpectrogramEngine.DbToLutByte(-20f), SpectrogramEngine.LutByteFromMagnitude(0.1, 0f));
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
    public void LiftDisplayUnit_RaisesShadowsAndKeepsEnds()
    {
        Assert.Equal(0, SpectrogramEngine.LiftDisplayUnit(0), 5);
        Assert.Equal(1, SpectrogramEngine.LiftDisplayUnit(1), 5);
        // 暗部ほど線形より上。ハイライト側の持ち上げはそれより小さい。
        Assert.True(SpectrogramEngine.LiftDisplayUnit(0.2f) > 0.2f);
        Assert.True(SpectrogramEngine.LiftDisplayUnit(0.2f) - 0.2f
            > SpectrogramEngine.LiftDisplayUnit(0.8f) - 0.8f);
    }

    [Fact]
    public void DbToLutByte_MapsFloorAndCeiling()
    {
        Assert.Equal(0, SpectrogramEngine.DbToLutByte(SpectrogramEngine.FloorDb));
        Assert.Equal(255, SpectrogramEngine.DbToLutByte(SpectrogramEngine.CeilingDb));
    }

    [Fact]
    public void DbToLutByte_LiftsShadowsAboveLinear()
    {
        // -45 dB はフロアからの 25%。暗部持ち上げで線形より高い LUT に載る。
        var linear = (byte)Math.Round(0.25f * 255);
        Assert.True(SpectrogramEngine.DbToLutByte(-45f) > linear);
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

    [Fact]
    public void DisplayBoost_DefaultIsOffAndTopIsMax()
    {
        Assert.Equal(32f * 2f / 3f, SpectrogramEngine.DisplayBoostMaxDb);
        Assert.Equal(0, SpectrogramEngine.DisplayBoostDbFromUnit(0), 5);
        Assert.Equal(SpectrogramEngine.DisplayBoostMaxDb, SpectrogramEngine.DisplayBoostDbFromUnit(1), 5);
        Assert.Equal(0, SpectrogramEngine.DisplayBoostUnitFromDb(0), 5);
        Assert.Equal(1, SpectrogramEngine.DisplayBoostUnitFromDb(SpectrogramEngine.DisplayBoostMaxDb), 5);
        Assert.Equal(0, SpectrogramEngine.ClampDisplayBoostDb(-4), 5);
        Assert.Equal(SpectrogramEngine.DisplayBoostMaxDb, SpectrogramEngine.ClampDisplayBoostDb(99), 5);
    }

    [Fact]
    public void InvertLiftDisplayUnit_RoundTripsMidTones()
    {
        Assert.Equal(0, SpectrogramEngine.InvertLiftDisplayUnit(0), 5);
        Assert.Equal(1, SpectrogramEngine.InvertLiftDisplayUnit(1), 5);
        Assert.Equal(0.2, SpectrogramEngine.InvertLiftDisplayUnit(SpectrogramEngine.LiftDisplayUnit(0.2f)), 4);
        Assert.Equal(0.8, SpectrogramEngine.InvertLiftDisplayUnit(SpectrogramEngine.LiftDisplayUnit(0.8f)), 4);
    }

    [Fact]
    public void ApplyDisplayBoostToLut_ZeroKeepsIdentityAndSilenceStaysFloor()
    {
        Assert.Equal(80, SpectrogramEngine.ApplyDisplayBoostToLut(80, 0));
        Assert.Equal(0, SpectrogramEngine.ApplyDisplayBoostToLut(0, SpectrogramEngine.DisplayBoostMaxDb));
        Assert.Equal(
            SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb),
            SpectrogramEngine.ColorFromLutByte(
                SpectrogramEngine.ApplyDisplayBoostToLut(0, 36f)));
    }

    [Fact]
    public void ApplyDisplayBoostToLut_RaisesQuietEnergyTowardYellowWhite()
    {
        var quiet = SpectrogramEngine.LutByteFromMagnitude(0.01); // -40 dB
        var boosted = SpectrogramEngine.ApplyDisplayBoostToLut(quiet, 24f);
        var direct = SpectrogramEngine.LutByteFromMagnitude(0.01, 24f);
        Assert.True(boosted > quiet);
        Assert.InRange(boosted, direct - 2, direct + 2);

        static int Luma(int bgra) =>
            (bgra & 0xFF) + ((bgra >> 8) & 0xFF) + ((bgra >> 16) & 0xFF);

        Assert.True(
            Luma(SpectrogramEngine.ColorFromLutByte(boosted))
            > Luma(SpectrogramEngine.ColorFromLutByte(quiet)));
    }

    [Fact]
    public void FillBoostColorMap_RemapsLutWithoutTouchingSilence()
    {
        var map = new int[256];
        SpectrogramEngine.FillBoostColorMap(map, 0);
        Assert.Equal(SpectrogramEngine.ColorFromLutByte(80), map[80]);
        SpectrogramEngine.FillBoostColorMap(map, 20f);
        Assert.Equal(
            SpectrogramEngine.ColorFromLutByte(SpectrogramEngine.ApplyDisplayBoostToLut(80, 20f)),
            map[80]);
        Assert.Equal(SpectrogramEngine.ColorFromLutByte(0), map[0]);
    }

    [Fact]
    public void ColorFromLinearUnit_BoostMatchesMagnitudePath()
    {
        var packed = SpectrogramEngine.LinearUnitFromMagnitude(0.01);
        Assert.True(packed > 0);
        Assert.Equal(0, SpectrogramEngine.LinearUnitFromMagnitude(0));
        Assert.Equal(
            SpectrogramEngine.ColorBgraFromMagnitude(0.01, 0f),
            SpectrogramEngine.ColorFromLinearUnit(packed));
        Assert.Equal(
            SpectrogramEngine.ColorBgraFromMagnitude(0.01, 24f),
            SpectrogramEngine.ColorFromLinearUnit(packed, 24f));
        Assert.Equal(
            SpectrogramEngine.ColorBgra(SpectrogramEngine.FloorDb),
            SpectrogramEngine.ColorFromLinearUnit(0, 24f));
    }

    [Fact]
    public void LinearUnitFromLifted_KeepsSubByteSteps()
    {
        var a = SpectrogramEngine.LinearUnitFromLifted(3f);
        var b = SpectrogramEngine.LinearUnitFromLifted(3.6f);
        Assert.True(b > a);
        Assert.True(a > 0);
    }
}
