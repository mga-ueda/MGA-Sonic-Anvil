using System.Windows;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ComboBoxFitTests
{
    [Fact]
    public void WidthFor_AddsChromeAndMargin()
    {
        var padding = new Thickness(6, 1, 6, 1);
        var border = new Thickness(1);
        Assert.Equal(
            100 + 6 + 6 + 1 + 1 + ComboBoxFit.ArrowColumnWidth + ComboBoxFit.ExtraMargin,
            ComboBoxFit.WidthFor(100, padding, border));
        Assert.Equal(
            100 + 6 + 6 + 1 + 1 + ComboBoxFit.ArrowColumnWidth + ComboBoxFit.ExtraMargin + 12,
            ComboBoxFit.WidthFor(100, padding, border, scrollBar: 12));
    }

    [Fact]
    public void ItemText_UsesToString()
    {
        Assert.Equal("Stereo (2ch)", ComboBoxFit.ItemText(new Named("Stereo (2ch)")));
        Assert.Equal(string.Empty, ComboBoxFit.ItemText(null));
        Assert.Equal("WASAPI", ComboBoxFit.ItemText("WASAPI"));
    }

    [Fact]
    public void WrapTabIndex_CyclesForwardAndBack()
    {
        Assert.Equal(1, AudioSettingsWindow.WrapTabIndex(0, 4, 1));
        Assert.Equal(0, AudioSettingsWindow.WrapTabIndex(3, 4, 1));
        Assert.Equal(3, AudioSettingsWindow.WrapTabIndex(0, 4, -1));
        Assert.Equal(0, AudioSettingsWindow.WrapTabIndex(0, 1, 1));
    }

    private sealed record Named(string Label)
    {
        public override string ToString() => Label;
    }
}
