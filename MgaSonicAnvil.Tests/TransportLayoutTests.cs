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
    public void WindowMinWidth_FitsTopRowWithoutWrapping()
    {
        var needed = DesignMetrics.TransportFixedRowWidth
            + DesignMetrics.TransportSideChromeWidth
            + DesignMetrics.WindowFramePad;
        Assert.True(DesignMetrics.WindowMinWidth >= needed);
        Assert.Equal(DesignMetrics.WindowMinWidth, DesignMetrics.WindowDefaultWidth);
        Assert.Equal(214 * 2 / 3.0, DesignMetrics.BrandLogoWidth);
        Assert.Equal(32 * 2 / 3.0, DesignMetrics.BrandLogoHeight);
        Assert.True(DesignMetrics.TransportChromeHeight
            >= DesignMetrics.DocumentTabBarHeight + DesignMetrics.TransportHostMinHeight);
        Assert.True(DesignMetrics.TransportChromeHeight >= DesignMetrics.VectorScopeHeight);
        Assert.True(DesignMetrics.MinimalPlayerWindowMinWidth < DesignMetrics.WindowMinWidth);
        Assert.True(DesignMetrics.MinimalPlayerWindowMinWidth >= DesignMetrics.From96(280));
        Assert.True(DesignMetrics.MinimalPlayerWindowMinHeight < DesignMetrics.WindowMinHeight);
        Assert.True(DesignMetrics.MinimalPlayerWindowMinHeight
            >= DesignMetrics.LibraryPaneMinHeight + DesignMetrics.LibraryWaveformHeight);
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
            (UiStrings.LabelLoudnessIntegrated, UiStrings.LabelLufs),
            (UiStrings.LabelLoudnessMomentary, UiStrings.LabelMaxLufs),
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
