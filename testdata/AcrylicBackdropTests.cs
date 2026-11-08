using System.Windows.Media;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AcrylicBackdropTests
{
    [Fact]
    public void ToAccentGradient_PacksAbgrWithMinimumAlpha()
    {
        Assert.Equal(unchecked((int)0x40000000), AcrylicBackdrop.ToAccentGradient(Color.FromArgb(0x40, 0, 0, 0)));
        Assert.Equal(unchecked((int)0x01FFFFFF), AcrylicBackdrop.ToAccentGradient(Color.FromArgb(0, 255, 255, 255)));
        Assert.Equal(unchecked((int)0x80AABBCC), AcrylicBackdrop.ToAccentGradient(Color.FromArgb(0x80, 0xCC, 0xBB, 0xAA)));
        Assert.Equal(unchecked((int)0xFE0000FF), AcrylicBackdrop.ToAccentGradient(Color.FromArgb(255, 255, 0, 0)));
    }

    [Fact]
    public void DefaultTintAlpha_MatchesUiColorsXaml()
    {
        Assert.Equal(0xC0, AcrylicBackdrop.DefaultTintAlpha);
        Assert.Equal(0x80, AcrylicBackdrop.DefaultToastTintAlpha);
        Assert.Equal("AcrylicDialogTintBrush", AcrylicBackdrop.TintBrushKey);
        Assert.Equal("AcrylicToastTintBrush", AcrylicBackdrop.ToastTintBrushKey);
    }

    [Fact]
    public void ClearPopupTopmost_IgnoresZeroHandle()
    {
        AcrylicBackdrop.ClearPopupTopmost(IntPtr.Zero);
    }

    [Fact]
    public void CornerStyleForControlRadius_MapsDefaultToSmall()
    {
        Assert.Equal(AcrylicCornerStyle.None, AcrylicBackdrop.CornerStyleForControlRadius(0));
        Assert.Equal(AcrylicCornerStyle.RoundSmall, AcrylicBackdrop.CornerStyleForControlRadius(5));
        Assert.Equal(AcrylicCornerStyle.RoundSmall, AcrylicBackdrop.CornerStyleForControlRadius(6));
        Assert.Equal(AcrylicCornerStyle.Round, AcrylicBackdrop.CornerStyleForControlRadius(7));
        Assert.Equal(AcrylicCornerStyle.None, AcrylicBackdrop.CornerStyleForControlRadius(double.NaN));
        Assert.Equal(0, AcrylicBackdrop.BorderRadiusForCornerStyle(AcrylicCornerStyle.None));
        Assert.Equal(4, AcrylicBackdrop.BorderRadiusForCornerStyle(AcrylicCornerStyle.RoundSmall));
        Assert.Equal(8, AcrylicBackdrop.BorderRadiusForCornerStyle(AcrylicCornerStyle.Round));
    }
}
