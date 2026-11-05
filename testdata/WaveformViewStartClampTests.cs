using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// 後半削除などで FrameCount が減ったあと、ViewStart が古い末尾付近のままだと
/// 波形ラスタが空になり見切れる。再クランプの回帰。
/// </summary>
public sealed class WaveformViewStartClampTests
{
    [Fact]
    public void ClampViewStart_WhenPastNewEnd_PullsBackToMax()
    {
        // 拡大表示で末尾付近（viewStart=90000）のまま後半を削り FrameCount=50000 になった典型。
        const long newFrameCount = 50_000;
        const double viewSpan = 5_000;
        const double staleViewStart = 90_000;

        var clamped = WaveformView.ClampViewStart(staleViewStart, newFrameCount, viewSpan);
        Assert.Equal(newFrameCount - viewSpan, clamped);
    }

    [Fact]
    public void ClampViewStart_WhenAlreadyValid_Unchanged()
    {
        Assert.Equal(1_000, WaveformView.ClampViewStart(1_000, 50_000, 5_000));
    }

    [Fact]
    public void ClampViewStart_WhenSpanCoversWholeFile_ForcesZero()
    {
        Assert.Equal(0, WaveformView.ClampViewStart(12_000, 10_000, 10_000));
    }

    [Fact]
    public void ClampViewStart_NegativeStart_BecomesZero()
    {
        Assert.Equal(0, WaveformView.ClampViewStart(-100, 50_000, 5_000));
    }

    [Fact]
    public void ClampCenteredViewStart_AtFileStart_KeepsHalfSpanOfLeadIn()
    {
        Assert.Equal(-2_500, WaveformView.ClampCenteredViewStart(-2_500, 50_000, 5_000));
        Assert.Equal(-2_500, WaveformView.ClampCenteredViewStart(-9_000, 50_000, 5_000));
    }

    [Fact]
    public void ClampCenteredViewStart_AtFileEnd_KeepsPlayheadCentered()
    {
        Assert.Equal(47_500, WaveformView.ClampCenteredViewStart(47_500, 50_000, 5_000));
        Assert.Equal(47_500, WaveformView.ClampCenteredViewStart(90_000, 50_000, 5_000));
    }

    [Fact]
    public void ClampCenteredViewStart_InTheMiddle_Unchanged()
    {
        Assert.Equal(1_000, WaveformView.ClampCenteredViewStart(1_000, 50_000, 5_000));
    }

    [Fact]
    public void TryMapWaveColumns_InsideFile_MatchesLegacyColumnFormula()
    {
        var mapped = WaveformView.TryMapWaveColumns(
            viewStart: 1_000,
            viewSpan: 5_000,
            bitmapWidth: 100,
            frameCount: 50_000,
            pixelStart: 10,
            pixelEnd: 20,
            out var frameStart,
            out var frameEnd,
            out var columnPixel,
            out var columnCount);

        Assert.True(mapped);
        Assert.Equal(1_500, frameStart);
        Assert.Equal(2_000, frameEnd);
        Assert.Equal(10, columnPixel);
        Assert.Equal(10, columnCount);
    }

    [Fact]
    public void TryMapWaveColumns_LeadIn_LeavesLeftColumnsBlank()
    {
        var blank = WaveformView.TryMapWaveColumns(
            viewStart: -2_500,
            viewSpan: 5_000,
            bitmapWidth: 100,
            frameCount: 50_000,
            pixelStart: 0,
            pixelEnd: 40,
            out _,
            out _,
            out _,
            out _);
        Assert.False(blank);

        var audio = WaveformView.TryMapWaveColumns(
            viewStart: -2_500,
            viewSpan: 5_000,
            bitmapWidth: 100,
            frameCount: 50_000,
            pixelStart: 0,
            pixelEnd: 100,
            out var frameStart,
            out var frameEnd,
            out var columnPixel,
            out var columnCount);
        Assert.True(audio);
        Assert.Equal(50, columnPixel);
        Assert.Equal(50, columnCount);
        Assert.Equal(0, frameStart);
        Assert.Equal(2_500, frameEnd);
    }
}
