using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolTipTests
{
    [Theory]
    [InlineData("Space")]
    public void Play_IncludesSpace(string key)
    {
        Assert.Contains(key, UiStrings.TooltipPlay);
    }

    [Fact]
    public void Tooltips_IncludePrimaryShortcuts()
    {
        Assert.Contains("(I)", UiStrings.TooltipFadeIn);
        Assert.Contains("(O)", UiStrings.TooltipFadeOut);
        Assert.Contains("(N)", UiStrings.TooltipNormalize);
        Assert.Contains("Delete", UiStrings.TooltipDelete);
        Assert.Contains("Ctrl+S", UiStrings.TooltipSave);
        Assert.Contains("Ctrl+Shift+S", UiStrings.TooltipSaveAs);
        Assert.Contains("Ctrl+Shift+M", UiStrings.TooltipSaveMp3);
        Assert.Contains("Ctrl+R", UiStrings.TooltipRecord);
        Assert.Contains("(X)", UiStrings.TooltipFadeAround);
        Assert.Contains("(V / Ctrl+Shift+V)", UiStrings.TooltipVolume);
        Assert.Contains("(E)", UiStrings.TooltipLoudnessPen);
        var language = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("Eキーで切替", UiStrings.LabelLoudnessPenToggle);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Press E to toggle", UiStrings.LabelLoudnessPenToggle);
        }
        finally
        {
            UiStrings.SetLanguage(language);
        }
        Assert.Contains("(V)", UiStrings.TooltipLoudnessView);
        Assert.Contains("Esc", UiStrings.TooltipLoudnessView);
        Assert.Contains("Shift+A", UiStrings.TooltipLoudnessView);
        Assert.Contains("Shift+V", UiStrings.TooltipLoudnessView);
        Assert.Contains("(P)", UiStrings.TooltipPitch);
        Assert.Contains("(T)", UiStrings.TooltipTimeStretch);
        Assert.Contains("(R)", UiStrings.TooltipReverse);
        Assert.Contains("(M)", UiStrings.TooltipAddMarker);
        Assert.Contains("Shift+L", UiStrings.TooltipSetLoop);
        Assert.Contains("Shift+R", UiStrings.TooltipSetRegion);
        Assert.Contains("Ctrl+N", UiStrings.TooltipNew);
        Assert.Contains("Ctrl+O", UiStrings.TooltipOpen);
        Assert.Contains("Ctrl+Shift+O", UiStrings.TooltipSettings);
        Assert.Contains("(A)", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Esc", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+A", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+V", UiStrings.TooltipSpectrogramView);
        Assert.Contains("(Z", UiStrings.TooltipCenterLock);
        Assert.Contains("(U)", UiStrings.TooltipHistory);
        Assert.Contains("F10", UiStrings.TooltipLibraryMaximize);
        Assert.Contains("F12", UiStrings.TooltipAnalyzerMaximize);
        Assert.Contains("Ctrl+Shift+C", UiStrings.TooltipColorPanel);
        Assert.Contains("Esc", UiStrings.OverlayOpening);
    }

    [Fact]
    public void Tooltips_StayOnOneLine()
    {
        Assert.DoesNotContain("\n", UiStrings.TooltipPlay);
        Assert.DoesNotContain("\n", UiStrings.TooltipSave);
        Assert.DoesNotContain("\n", UiStrings.TooltipSaveMp3);
        Assert.DoesNotContain("\n", UiStrings.TooltipDelete);
        Assert.DoesNotContain("\n", UiStrings.TooltipColorPanel);
    }
}
