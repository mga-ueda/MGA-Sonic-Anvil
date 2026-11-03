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
        Assert.True(DesignMetrics.TransportChromeHeight >= DesignMetrics.VectorScopeHeight);
        // F8 動画ミニの実効下限は映像原寸×LibraryVideoWindowScale.Min。
        Assert.Equal(0.25, LibraryVideoWindowScale.Min, 6);
    }

    [Fact]
    public void TransportRowHeight_CoversOneButtonRow()
    {
        var oneRow = DesignMetrics.From96(2) + DesignMetrics.TransportButtonSide;
        Assert.Equal(oneRow, DesignMetrics.TransportRowHeight);
    }

    [Fact]
    public void TransportChromeHeight_MatchesAnalyzerBand()
    {
        Assert.Equal(DesignMetrics.VectorScopeHeight, DesignMetrics.TransportChromeHeight);
        Assert.Equal(DesignMetrics.SpectrumHeight, DesignMetrics.TransportChromeHeight);
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
