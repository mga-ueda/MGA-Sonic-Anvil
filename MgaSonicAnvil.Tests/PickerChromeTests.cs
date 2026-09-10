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
}
