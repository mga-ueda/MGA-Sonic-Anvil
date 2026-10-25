using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// 短尺の標準表示を長尺と同じ PeakPyramid エンベロープ系統に寄せる方針の回帰。
/// </summary>
public sealed class WaveformEnvelopePathTests
{
    // 約2秒 @ 48kHz、波形幅 1500px（標準倍率で全体表示）。
    private const int ShortClipFrames = 48_000 * 2;
    private const int DefaultWaveWidth = 1500;

    [Fact]
    public void ShortClip_DefaultView_UsesPeakPyramidNotRaw_WhenPeaksMatch()
    {
        Assert.False(
            WaveformView.ShouldUseRawWaveColumns(
                seekAndSelectOnly: false,
                hasPcm: true,
                polylineZoom: false,
                peaksUsableForEnvelope: true,
                rangeFrames: ShortClipFrames,
                width: DefaultWaveWidth));
    }

    [Fact]
    public void ShortClip_DefaultView_FallsBackToRaw_WhenPeaksMissing()
    {
        Assert.True(
            WaveformView.ShouldUseRawWaveColumns(
                seekAndSelectOnly: false,
                hasPcm: true,
                polylineZoom: false,
                peaksUsableForEnvelope: false,
                rangeFrames: ShortClipFrames,
                width: DefaultWaveWidth));
    }

    [Fact]
    public void LongClip_DefaultView_NeverUsesRaw_EvenWithoutPeaksFlag()
    {
        // 約1分は Raw 予算外。ピーク無しでも Raw に入らない。
        const long oneMinuteFrames = 48_000L * 60;
        Assert.False(
            WaveformView.ShouldUseRawWaveColumns(
                seekAndSelectOnly: false,
                hasPcm: true,
                polylineZoom: false,
                peaksUsableForEnvelope: false,
                rangeFrames: oneMinuteFrames,
                width: DefaultWaveWidth));
    }

    [Fact]
    public void ShortClip_DefaultView_ConnectsEnvelopeNeighbors()
    {
        // 旧 width×8（spp≤8）だとオフになる帯。いまは Raw 予算と同じ spp≤96 でつなぐ。
        Assert.False(ShortClipFrames <= (long)DefaultWaveWidth * 8);
        Assert.True(WaveformView.ShouldConnectEnvelopeNeighbors(ShortClipFrames, DefaultWaveWidth));
    }

    [Fact]
    public void LongClip_DefaultView_SkipsNeighborConnect_StillHeavyAggregation()
    {
        const long oneMinuteFrames = 48_000L * 60;
        Assert.False(WaveformView.ShouldConnectEnvelopeNeighbors(oneMinuteFrames, DefaultWaveWidth));
    }

    [Fact]
    public void ShortClip_PeakPyramid_ReadRangePacked_FillsWidth_AndKeepsSpike()
    {
        var samples = new float[ShortClipFrames];
        for (var i = 0; i < ShortClipFrames; i++)
        {
            samples[i] = MathF.Sin(i * 0.11f) * 0.4f;
        }

        const int spikeAt = ShortClipFrames / 3;
        samples[spikeAt] = 0.97f;
        samples[spikeAt + 1] = -0.93f;

        var peaks = PeakPyramid.Build(samples, channels: 1);
        var mins = new float[DefaultWaveWidth];
        var maxs = new float[DefaultWaveWidth];
        Assert.Equal(
            DefaultWaveWidth,
            peaks.ReadRangePacked(0, ShortClipFrames, DefaultWaveWidth, mins, maxs));

        var column = (int)(spikeAt * (long)DefaultWaveWidth / ShortClipFrames);
        Assert.True(maxs[column] >= 0.97f - 1e-5f);
        Assert.True(mins[column] <= -0.93f + 1e-5f);
        Assert.DoesNotContain(float.MaxValue, mins);
        Assert.DoesNotContain(float.MinValue, maxs);
    }

    [Fact]
    public void ConnectNeighbors_SmoothsColumnEdgeJumps()
    {
        // RasterPeakEnvelope と同じ隣接マージ: 次列の hi/lo と和集合。
        var hi = new[] { 10, 40, 12 };
        var lo = new[] { 50, 55, 48 };
        var connectedHi = new int[hi.Length];
        var connectedLo = new int[lo.Length];
        for (var px = 0; px < hi.Length; px++)
        {
            var h = hi[px];
            var l = lo[px];
            if (px + 1 < hi.Length)
            {
                h = Math.Min(h, hi[px + 1]);
                l = Math.Max(l, lo[px + 1]);
            }

            connectedHi[px] = h;
            connectedLo[px] = l;
        }

        // 中列の飛び出し（hi=40）が隣とつながって緩和される。
        Assert.True(connectedHi[0] <= hi[0]);
        Assert.Equal(10, connectedHi[0]);
        Assert.True(connectedHi[1] < hi[1]);
        Assert.Equal(12, connectedHi[1]);
        Assert.True(connectedLo[0] >= lo[0]);
    }
}
