using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class StatusTimecodeLayoutTests
{
    [Theory]
    [InlineData("00:00.000")]
    [InlineData("000:00.000")]
    public void StatusTimecodeWidth_FitsCenteredTimecode(string text)
    {
        var measured = Measure(text);
        var content = DesignMetrics.StatusTimecodeWidth - DesignMetrics.StatusTimecodePadX * 2;
        Assert.True(content >= measured.Width + 8, $"need {measured.Width + 8}, have {content}");
        Assert.True(DesignMetrics.StatusTimecodeBoxHeight >= measured.Height);
        Assert.True(DesignMetrics.StatusBarHeight >= DesignMetrics.StatusTimecodeBoxHeight);
    }

    private static (double Width, double Height) Measure(string text)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            DesignMetrics.StatusTimecodeFontSize,
            Brushes.Black,
            1);
        return (formatted.WidthIncludingTrailingWhitespace, formatted.Height);
    }
}
