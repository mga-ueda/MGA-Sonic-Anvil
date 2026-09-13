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
        Assert.Contains("Ctrl+Shift+M", UiStrings.TooltipSaveMp3);
        Assert.Contains("Ctrl+R", UiStrings.TooltipRecord);
        Assert.Contains("(X)", UiStrings.TooltipFadeAround);
        Assert.Contains("(V)", UiStrings.TooltipVolume);
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
        Assert.Contains("Ctrl+O", UiStrings.TooltipOpen);
        Assert.Contains("Ctrl+Shift+O", UiStrings.TooltipSettings);
        Assert.Contains("Ctrl+Shift+S", UiStrings.TooltipSaveAs);
        Assert.Contains("(A)", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Esc", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+A", UiStrings.TooltipSpectrogramView);
        Assert.Contains("Shift+V", UiStrings.TooltipSpectrogramView);
        Assert.Contains("(Z)", UiStrings.TooltipCenterPlayhead);
        Assert.Contains("(U)", UiStrings.TooltipHistory);
        Assert.Contains("Ctrl+Shift+C", UiStrings.TooltipColorPanel);
        Assert.Contains("Alt+S", UiStrings.TipSilentSkip);
        Assert.Contains("Shift", UiStrings.TipPlay);
    }

    [Fact]
    public void GroupLabels_AreShortAndMatchImporter()
    {
        Assert.Equal("TRANS", UiStrings.LabelTransportGroup);
        Assert.Equal("EDIT", UiStrings.LabelEditGroup);
        Assert.Equal("MARK", UiStrings.LabelMarkerGroup);
        Assert.Equal("FILE", UiStrings.LabelFileGroup);
        Assert.Equal("VIEW", UiStrings.LabelViewGroup);
        Assert.Equal("HELP", UiStrings.LabelHelpGroup);
        foreach (var label in new[]
        {
            UiStrings.LabelTransportGroup,
            UiStrings.LabelEditGroup,
            UiStrings.LabelMarkerGroup,
            UiStrings.LabelFileGroup,
            UiStrings.LabelViewGroup,
            UiStrings.LabelHelpGroup,
        })
        {
            Assert.InRange(label.Length, 3, 5);
        }
    }

    [Fact]
    public void Tooltips_StayOnOneLine()
    {
        Assert.DoesNotContain("\n", UiStrings.TooltipPlay);
        Assert.DoesNotContain("\n", UiStrings.TooltipSave);
        Assert.DoesNotContain("\n", UiStrings.TooltipSaveMp3);
        Assert.DoesNotContain("\n", UiStrings.TooltipDelete);
        Assert.DoesNotContain("\n", UiStrings.TooltipUiThemeToggle);
        Assert.DoesNotContain("\n", UiStrings.TooltipColorPanel);
    }
}
