using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PickerChromeTests
{
    [Fact]
    public void Numbered_KeepsOriginalSpacing()
    {
        Assert.Equal("1  48000 Hz", PickerChrome.Numbered(1, "48000 Hz"));
        Assert.Equal("9  ", PickerChrome.Numbered(9, string.Empty));
    }

    [Fact]
    public void MeasureUiText_LongestPickerRowsStayInsidePanel()
    {
        var fade = PickerChrome.Numbered(1, UiStrings.LabelFadeCurve(1));
        var rate = PickerChrome.Numbered(8, "96000 Hz");
        var channels = PickerChrome.Numbered(2, "2 ch  Stereo");
        var bits = PickerChrome.Numbered(4, "24 bit");
        Assert.True(PickerChrome.MeasureUiText(fade) < PickerChrome.PanelWidth);
        Assert.True(PickerChrome.MeasureUiText(rate) < PickerChrome.PanelWidth);
        Assert.True(PickerChrome.MeasureUiText(channels) < PickerChrome.PanelWidth);
        Assert.True(PickerChrome.MeasureUiText(bits) < PickerChrome.PanelWidth);
    }

    [Fact]
    public void CharBoxWidth_FitsSevenDigits()
    {
        Assert.True(PickerChrome.CharBoxWidth(7) < 88);
        Assert.True(PickerChrome.CharBoxWidth(7) > PickerChrome.CharBoxWidth(6));
    }

    [Fact]
    public void MeasureUiText_ChannelRowIsUnderOldMinWidth()
    {
        var text = PickerChrome.MeasureUiText("2  2 ch  Stereo");
        Assert.True(text + 8 + 8 + 22 + 8 + 2 < 200);
    }

    [Fact]
    public void LabelColumnWidth_UsesLongestLabel()
    {
        var width = PickerChrome.LabelColumnWidth("音量", "LKFS");
        Assert.Equal(
            Math.Ceiling(Math.Max(PickerChrome.MeasureUiText("音量"), PickerChrome.MeasureUiText("LKFS"))),
            width);
    }

    [Fact]
    public void PrependClose_PinsButtonToTopRight()
    {
        RunSta(() =>
        {
            var menu = new ContextMenu();
            var root = PickerChrome.Panel();
            PickerChrome.PrependClose(root, menu);
            var bar = Assert.IsType<DockPanel>(root.Children[0]);
            Assert.Equal(HorizontalAlignment.Stretch, bar.HorizontalAlignment);
            Assert.Equal(-PickerChrome.FormItemPadding.Top, bar.Margin.Top);
            Assert.Equal(-PickerChrome.FormItemPadding.Right, bar.Margin.Right);
            var close = Assert.IsType<Border>(bar.Children[0]);
            Assert.Equal(Dock.Right, DockPanel.GetDock(close));
            Assert.Equal(OverlayCaption.CornerMargin, close.Margin);
            Assert.Equal(HorizontalAlignment.Right, close.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, close.VerticalAlignment);
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
