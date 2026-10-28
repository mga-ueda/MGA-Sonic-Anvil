using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportLayoutTests
{
    [Fact]
    public void WindowMinSize_IsSharedAcrossModes()
    {
        Assert.Equal(960, DesignMetrics.WindowMinWidth);
        Assert.Equal(540, DesignMetrics.WindowMinHeight);
        Assert.Equal(DesignMetrics.WindowMinWidth, DesignMetrics.WindowDefaultWidth);
        Assert.True(DesignMetrics.TransportChromeHeight
            >= DesignMetrics.TransportHostMinHeight);
        Assert.True(DesignMetrics.TransportChromeHeight >= DesignMetrics.VectorScopeHeight);
        // F8／F9 も通常と同じ下限。
        Assert.Equal(DesignMetrics.WindowMinWidth, DesignMetrics.MinimalPlayerWindowMinWidth);
        Assert.Equal(DesignMetrics.WindowMinHeight, DesignMetrics.MinimalPlayerWindowMinHeight);
        Assert.Equal(DesignMetrics.WindowMinWidth, DesignMetrics.VideoMiniWindowMinWidth);
        Assert.Equal(DesignMetrics.WindowMinHeight, DesignMetrics.VideoMiniWindowMinHeight);
    }

    [Fact]
    public void TransportBarHeight_CoversTwoRows()
    {
        var twoRows = DesignMetrics.From96(2)
            + DesignMetrics.TransportButtonSide * 2
            + DesignMetrics.TransportRowGap;
        Assert.Equal(0, DesignMetrics.TransportRowGap);
        Assert.Equal(twoRows, DesignMetrics.TransportBarHeight);
    }

    [Fact]
    public void TransportTopRow_IsWiderThanBottomRow()
    {
        Assert.True(DesignMetrics.TransportTopRowWidth >= DesignMetrics.TransportBottomRowWidth);
        Assert.Equal(DesignMetrics.TransportTopRowWidth, DesignMetrics.TransportFixedRowWidth);
    }

    [Fact]
    public void LoudnessMeterWidth_FitsLongestCaptionValueUnit()
    {
        var regular = new Typeface("Consolas");
        var bold = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        double WidthOf(Typeface typeface, string text)
        {
            return new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                12,
                Brushes.Black,
                1).WidthIncludingTrailingWhitespace;
        }

        var rows = new[]
        {
            (UiStrings.LabelLoudnessShortTerm, UiStrings.LabelLufs),
            (UiStrings.LabelLoudnessIntegrated, UiStrings.LabelLufs + " Avg"),
            (UiStrings.LabelLoudnessMomentary, UiStrings.LabelMaxLufs + " Avg"),
            (UiStrings.LabelLra, UiStrings.LabelLu),
            (UiStrings.LabelTruePeak, UiStrings.LabelDb),
        };
        var caption = rows.Max(row => WidthOf(regular, row.Item1));
        var value = 6 + 3 + WidthOf(bold, "-99.9") + 3 + 4;
        var unit = rows.Max(row => WidthOf(regular, row.Item2));
        var need = caption + value + unit + DesignMetrics.LoudnessMeterCharWidth * 2;
        Assert.True(DesignMetrics.LoudnessMeterWidth + 0.5 >= need, $"need {need}, have {DesignMetrics.LoudnessMeterWidth}");
        Assert.True(DesignMetrics.LoudnessMeterWidth <= need + 8, $"slack {DesignMetrics.LoudnessMeterWidth - need}, have {DesignMetrics.LoudnessMeterWidth}");
        Assert.Equal(264, DesignMetrics.HistoryStripWidth);
    }

    [Fact]
    public void AnalyzerMaximizeScale_IsOnePointFiveOnDipMetrics()
    {
        Assert.Equal(1.5, DesignMetrics.AnalyzerMaximizeScale);
        var transform = UiScaleService.CreatePublishedTransform(DesignMetrics.AnalyzerMaximizeScale);
        var scale = Assert.IsType<ScaleTransform>(transform);
        Assert.Equal(1.5, scale.ScaleX);
        Assert.Equal(1.5, scale.ScaleY);
    }
}
