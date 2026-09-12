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
        var typeface = new Typeface("Consolas");
        double WidthOf(string text)
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

        var value = "-99.9";
        var rows = new[]
        {
            (UiStrings.LabelLoudnessShortTerm, UiStrings.LabelLufs),
            (UiStrings.LabelLoudnessIntegrated, UiStrings.LabelLufs),
            (UiStrings.LabelLoudnessMomentary, UiStrings.LabelMaxLufs),
            (UiStrings.LabelLra, UiStrings.LabelLu),
            (UiStrings.LabelTruePeak, UiStrings.LabelDb),
        };
        var need = rows.Max(row => WidthOf(row.Item1) + 6 + WidthOf(value) + 4 + WidthOf(row.Item2)) + 4 + 6;
        Assert.True(DesignMetrics.LoudnessMeterWidth + 0.5 >= need, $"need {need}, have {DesignMetrics.LoudnessMeterWidth}");
        Assert.Equal(176, DesignMetrics.HistoryStripWidth);
    }
}
