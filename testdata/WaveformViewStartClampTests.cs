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
}
