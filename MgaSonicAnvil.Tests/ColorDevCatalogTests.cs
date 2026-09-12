using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ColorDevCatalogTests
{
    [Theory]
    [InlineData("共通・標準文字", "共通")]
    [InlineData("Shared · standard text", "Shared")]
    [InlineData("波形", "波形")]
    [InlineData("Waveform", "Waveform")]
    public void GroupOf_UsesLocalizedSeparator(string label, string group)
    {
        Assert.Equal(group, ColorDevCatalog.GroupOf(label));
    }

    [Fact]
    public void Matches_LabelKeyAndHex()
    {
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#B6B6B6", "波形"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#B6B6B6", "wave"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#B6B6B6", "b6"));
        Assert.False(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#B6B6B6", "playhead"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#B6B6B6", " "));
    }
}
