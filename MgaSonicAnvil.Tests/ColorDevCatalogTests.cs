using MgaSonicAnvil.Domain;
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

    [Fact]
    public void Title_UsesModeNotDeveloper()
    {
        var dark = UiStrings.ColorDevTitleFor(UiTheme.Dark);
        var light = UiStrings.ColorDevTitleFor(UiTheme.Light);
        Assert.DoesNotContain("開発者", dark);
        Assert.DoesNotContain("開発者", light);
        Assert.DoesNotContain("developer", dark, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("developer", light, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(dark, light);
        Assert.True(dark.Contains("ダーク", StringComparison.Ordinal) || dark.Contains("Dark", StringComparison.Ordinal));
        Assert.True(light.Contains("ライト", StringComparison.Ordinal) || light.Contains("Light", StringComparison.Ordinal));
    }
}
