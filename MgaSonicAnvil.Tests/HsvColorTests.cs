using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class HsvColorTests
{
    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    [InlineData(255, 255, 255)]
    [InlineData(0, 0, 0)]
    [InlineData(128, 128, 128)]
    [InlineData(235, 235, 235)]
    [InlineData(0, 245, 255)]
    [InlineData(255, 51, 85)]
    [InlineData(48, 48, 50)]
    public void FromRgb_ToRgb_Roundtrips(byte r, byte g, byte b)
    {
        var back = HsvColor.FromRgb(Color.FromRgb(r, g, b)).ToRgb();
        Assert.InRange(back.R, r - 1, r + 1);
        Assert.InRange(back.G, g - 1, g + 1);
        Assert.InRange(back.B, b - 1, b + 1);
    }

    [Fact]
    public void FromRgb_PrimaryHues()
    {
        Assert.Equal(0, HsvColor.FromRgb(Color.FromRgb(255, 0, 0)).H, 6);
        Assert.Equal(120, HsvColor.FromRgb(Color.FromRgb(0, 255, 0)).H, 6);
        Assert.Equal(240, HsvColor.FromRgb(Color.FromRgb(0, 0, 255)).H, 6);
        Assert.Equal(0, HsvColor.FromRgb(Colors.White).S, 6);
        Assert.Equal(0, HsvColor.FromRgb(Colors.Black).V, 6);
    }

    [Fact]
    public void SvFromPoint_MapsCorners()
    {
        var size = new System.Windows.Size(100, 50);
        Assert.Equal((0, 1), HsvColorPicker.SvFromPoint(0, 0, size));
        Assert.Equal((1, 0), HsvColorPicker.SvFromPoint(100, 50, size));
        Assert.Equal((0.5, 0.5), HsvColorPicker.SvFromPoint(50, 25, size));
    }

    [Fact]
    public void HueFromPoint_MapsBar()
    {
        Assert.Equal(0, HsvColorPicker.HueFromPoint(0, 180), 6);
        Assert.Equal(180, HsvColorPicker.HueFromPoint(90, 180), 6);
        Assert.Equal(360, HsvColorPicker.HueFromPoint(180, 180), 6);
    }

    [Fact]
    public void ChannelFromPoint_MapsByteRange()
    {
        Assert.Equal(0, HsvColorPicker.ChannelFromPoint(0, 255), 6);
        Assert.Equal(255, HsvColorPicker.ChannelFromPoint(255, 255), 6);
        Assert.Equal(127.5, HsvColorPicker.ChannelFromPoint(127.5, 255), 6);
    }
}
