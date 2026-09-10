using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LevelMeterSurroundLayoutTests
{
    private const double MeterWidth = 100;
    private const double ScaleWidth = 22;
    private const double Eps = 1e-9;

    /// <summary>Consolas 相当の等幅送り。字数 × フォント × 0.55。</summary>
    private static Func<double, double[]> Measure(string[] names) =>
        font => names.Select(n => n.Length * font * 0.55).ToArray();

    private static string[] Names(int channels)
    {
        var layout = ChannelLayout.Guess(channels);
        return Enumerable.Range(0, channels).Select(layout.LabelAt).ToArray();
    }

    private static (double BarW, double BarsLeft) Bars(int channels)
    {
        var barW = LevelMeterSurroundLayout.BarWidth(channels);
        return (barW, LevelMeterSurroundLayout.BarsLeft(0, MeterWidth, barW, channels));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void BarStrip_LeavesRoomForBothScales(int channels)
    {
        var (barW, barsLeft) = Bars(channels);

        Assert.True(barW >= LevelMeterSurroundLayout.MinBarWidth);
        Assert.Equal(Math.Floor(barW), barW);
        // 左右とも目盛り列 22px が丸ごと入る。
        Assert.True(barsLeft >= ScaleWidth, $"バー左端 {barsLeft} が目盛り幅 {ScaleWidth} 未満");
        Assert.True(
            barsLeft + (barW * channels) + ScaleWidth <= MeterWidth + Eps,
            $"バー右端 {barsLeft + (barW * channels)} + 目盛り {ScaleWidth} が {MeterWidth} を超えた");
    }

    [Fact]
    public void BarWidth_ShrinksWithChannelCount()
    {
        Assert.True(LevelMeterSurroundLayout.BarWidth(16) < LevelMeterSurroundLayout.BarWidth(6));
    }

    [Fact]
    public void BarWidth_NeverExceedsStereoBar()
    {
        Assert.True(LevelMeterSurroundLayout.BarWidth(3) <= LevelMeterSurroundLayout.MaxBarWidth);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    [InlineData(14)]
    [InlineData(16)]
    public void Labels_FitWithoutClippingOrOverlap(int channels)
    {
        var names = Names(channels);
        var measure = Measure(names);
        var (barW, barsLeft) = Bars(channels);

        var labels = LevelMeterSurroundLayout.SolveLabels(
            MeterWidth, barsLeft, barW, channels, measure, leftEdge: 1);

        Assert.True(labels.HasLabels);
        AssertNoClipOrOverlap(labels, measure(labels.FontSize), channels);
    }

    [Fact]
    public void FiveOne_KeepsOneRowAtFullFont()
    {
        var names = Names(6);
        var (barW, barsLeft) = Bars(6);

        var labels = LevelMeterSurroundLayout.SolveLabels(
            MeterWidth, barsLeft, barW, 6, Measure(names), leftEdge: 1);

        Assert.Equal(1, labels.Rows);
        Assert.Equal(LevelMeterSurroundLayout.MaxFont, labels.FontSize);
        Assert.All(labels.Row, row => Assert.Equal(0, row));
    }

    [Fact]
    public void LastLabel_NeverTouchesRightEdge()
    {
        var names = Names(6);
        var measure = Measure(names);
        var (barW, barsLeft) = Bars(6);

        var labels = LevelMeterSurroundLayout.SolveLabels(
            MeterWidth, barsLeft, barW, 6, measure, leftEdge: 1);
        var widths = measure(labels.FontSize);
        var last = labels.X[5] + widths[5];

        Assert.True(last <= MeterWidth - 1 + Eps, $"Rs の右端 {last} が {MeterWidth - 1} を超えた");
    }

    [Fact]
    public void NineOneSix_FallsBackToTwoStaggeredRows()
    {
        var names = Names(16);
        var measure = Measure(names);
        var (barW, barsLeft) = Bars(16);

        var labels = LevelMeterSurroundLayout.SolveLabels(
            MeterWidth, barsLeft, barW, 16, measure, leftEdge: 1);

        Assert.Equal(2, labels.Rows);
        Assert.Equal(0, labels.Row[0]);
        Assert.Equal(1, labels.Row[1]);
        AssertNoClipOrOverlap(labels, measure(labels.FontSize), 16);
    }

    [Fact]
    public void ImpossibleWidth_ReportsNoLabels()
    {
        var names = Names(16);
        Func<double, double[]> wide = font => names.Select(_ => font * 40).ToArray();

        var labels = LevelMeterSurroundLayout.SolveLabels(MeterWidth, 26, 3, 16, wide, leftEdge: 1);

        Assert.False(labels.HasLabels);
    }

    [Fact]
    public void DegenerateInput_ReportsNoLabels()
    {
        Assert.False(LevelMeterSurroundLayout.SolveLabels(MeterWidth, 22, 12, 0, _ => []).HasLabels);
        Assert.False(LevelMeterSurroundLayout.SolveLabels(MeterWidth, 22, 0, 6, _ => new double[6]).HasLabels);
    }

    private static void AssertNoClipOrOverlap(SurroundLabelLayout labels, double[] widths, int channels)
    {
        for (var row = 0; row < labels.Rows; row++)
        {
            var previousRight = double.NegativeInfinity;
            for (var i = 0; i < channels; i++)
            {
                if (labels.Row[i] != row)
                {
                    continue;
                }

                var left = labels.X[i];
                var right = left + widths[i];
                Assert.True(left >= 1 - Eps, $"ch{i} の左端 {left} が列の左端より左");
                Assert.True(right <= MeterWidth - 1 + Eps, $"ch{i} の右端 {right} が {MeterWidth - 1} を超えた");
                Assert.True(
                    left >= previousRight + LevelMeterSurroundLayout.LabelGap - Eps,
                    $"ch{i} が前のラベルと重なった ({left} < {previousRight + LevelMeterSurroundLayout.LabelGap})");
                previousRight = right;
            }
        }
    }
}
