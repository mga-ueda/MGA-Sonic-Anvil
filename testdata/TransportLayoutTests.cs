using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
    public void EditorPositionTimecodeFontSize_IsLargerThanStatusStrip()
    {
        Assert.True(DesignMetrics.EditorPositionTimecodeFontSize > DesignMetrics.StatusTimecodeFontSize);
        Assert.True(DesignMetrics.EditorPositionTimecodeFontSize >= DesignMetrics.PlaylistVideoTimecodeFontSize);
        Assert.Equal(0.75, DesignMetrics.EditorPositionTimecodeWidthRatio);
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

    [Fact]
    public void AlignedWrapPanel_Center_CentersSecondLineUnderFirst()
    {
        RunSta(() =>
        {
            var panel = new AlignedWrapPanel
            {
                LineAlignment = HorizontalAlignment.Center,
            };
            // 幅 100 に 40+40 が 1 行目、余り 40 が 2 行目。中央なら 2 行目 x=(100-40)/2=30。
            panel.Children.Add(new Border { Width = 40, Height = 10 });
            panel.Children.Add(new Border { Width = 40, Height = 10 });
            panel.Children.Add(new Border { Width = 40, Height = 10 });
            panel.Measure(new Size(100, 40));
            panel.Arrange(new Rect(0, 0, 100, 40));

            Point Origin(int index) =>
                ((FrameworkElement)panel.Children[index]).TransformToAncestor(panel).Transform(new Point(0, 0));

            Assert.Equal(10, Origin(0).X, 0.5); // (100-80)/2
            Assert.Equal(50, Origin(1).X, 0.5);
            Assert.Equal(30, Origin(2).X, 0.5);
            Assert.True(Origin(2).Y > Origin(0).Y);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
