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
    }

    [Fact]
    public void DefaultTintAlpha_MatchesUiColorsXaml()
    {
        Assert.Equal(0xC0, AcrylicBackdrop.DefaultTintAlpha);
        Assert.Equal("AcrylicDialogTintBrush", AcrylicBackdrop.TintBrushKey);
    }
}
