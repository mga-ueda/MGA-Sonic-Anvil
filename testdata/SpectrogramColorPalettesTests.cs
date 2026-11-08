using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrogramColorPalettesTests
{
    [Theory]
    [InlineData(null, "blue")]
    [InlineData("", "blue")]
    [InlineData("auto", "blue")]
    [InlineData("blue", "blue")]
    [InlineData("BLUE", "blue")]
    [InlineData("red", "red")]
    [InlineData("RED", "red")]
    public void Parse_DefaultsToBlue(string? stored, string formatted)
    {
        var palette = SpectrogramColorPalettes.Parse(stored);
        Assert.Equal(formatted, SpectrogramColorPalettes.Format(palette));
        Assert.Equal(palette, SpectrogramColorPalettes.Parse(formatted));
    }

    [Fact]
    public void Default_IsBlue()
    {
        Assert.Equal(SpectrogramColorPalette.Blue, SpectrogramColorPalettes.Default);
        Assert.Equal("blue", SpectrogramColorPalettes.Format(SpectrogramColorPalettes.Default));
        Assert.Equal("blue", new AppSettings().SpectrogramColorPalette);
        Assert.Equal(SpectrogramColorPalette.Blue, new AppSettings().ResolvedSpectrogramColorPalette());
        Assert.Equal(SpectrogramColorPalette.Blue, AppSettings.CreateDefault().ResolvedSpectrogramColorPalette());
    }

    [Fact]
    public void AppSettings_ApplyRoundTrips()
    {
        var settings = new AppSettings();
        settings.ApplySpectrogramColorPalette(SpectrogramColorPalette.Red);
        Assert.Equal("red", settings.SpectrogramColorPalette);
        Assert.Equal(SpectrogramColorPalette.Red, settings.ResolvedSpectrogramColorPalette());
        settings.ApplySpectrogramColorPalette(SpectrogramColorPalette.Blue);
        Assert.Equal("blue", settings.SpectrogramColorPalette);
        Assert.Equal(SpectrogramColorPalette.Blue, settings.ResolvedSpectrogramColorPalette());
    }
}
