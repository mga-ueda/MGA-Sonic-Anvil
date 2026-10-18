using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiLanguageTests
{
    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("auto", "Auto")]
    [InlineData("ja", "Japanese")]
    [InlineData("japanese", "Japanese")]
    [InlineData("en", "English")]
    [InlineData("english", "English")]
    public void ParseLanguageChoice_ReadsStoredValues(string? stored, string expected)
    {
        Assert.Equal(expected, UiStrings.ParseLanguageChoice(stored).ToString());
    }

    [Fact]
    public void ResolveLanguage_Auto_UsesOsJapanese()
    {
        Assert.Equal("Japanese", UiStrings.ResolveLanguage(UiLanguageChoice.Auto, "ja").ToString());
        Assert.Equal("English", UiStrings.ResolveLanguage(UiLanguageChoice.Auto, "en").ToString());
        Assert.Equal("English", UiStrings.ResolveLanguage(UiLanguageChoice.Auto, "de").ToString());
    }

    [Fact]
    public void ResolveLanguage_Explicit_IgnoresOs()
    {
        Assert.Equal("Japanese", UiStrings.ResolveLanguage(UiLanguageChoice.Japanese, "en").ToString());
        Assert.Equal("English", UiStrings.ResolveLanguage(UiLanguageChoice.English, "ja").ToString());
    }

    [Fact]
    public void ToStoredValue_RoundTripsChoices()
    {
        Assert.Equal("auto", UiStrings.ToStoredValue(UiLanguageChoice.Auto));
        Assert.Equal("ja", UiStrings.ToStoredValue(UiLanguageChoice.Japanese));
        Assert.Equal("en", UiStrings.ToStoredValue(UiLanguageChoice.English));
        Assert.Equal("Auto", UiStrings.ParseLanguageChoice("auto").ToString());
        Assert.Equal("Japanese", UiStrings.ParseLanguageChoice("ja").ToString());
        Assert.Equal("English", UiStrings.ParseLanguageChoice("en").ToString());
    }
}
