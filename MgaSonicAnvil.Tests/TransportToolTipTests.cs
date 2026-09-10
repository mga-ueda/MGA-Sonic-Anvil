using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolTipTests
{
    [Theory]
    [InlineData("Space")]
    public void PlayAndStop_IncludeSpace(string key)
    {
        Assert.Contains(key, UiStrings.TooltipPlay);
        Assert.Contains(key, UiStrings.TooltipStop);
    }

    [Fact]
    public void Tooltips_IncludePrimaryShortcuts()
    {
        Assert.Contains("Ctrl+Home", UiStrings.TooltipGoToStart);
        Assert.Contains("Ctrl+End", UiStrings.TooltipGoToEnd);
        Assert.Contains("↑", UiStrings.TooltipTimeZoomIn);
        Assert.Contains("↓", UiStrings.TooltipTimeZoomOut);
        Assert.Contains("Ctrl+↑", UiStrings.TooltipTimeZoomMax);
        Assert.Contains("Ctrl+↓", UiStrings.TooltipTimeZoomReset);
        Assert.Contains("Shift+↑", UiStrings.TooltipAmpZoomIn);
        Assert.Contains("Shift+↓", UiStrings.TooltipAmpZoomOut);
        Assert.Contains("Ctrl+Shift+↑", UiStrings.TooltipAmpZoomMax);
        Assert.Contains("Ctrl+Shift+↓", UiStrings.TooltipAmpZoomReset);
        Assert.Contains("(I)", UiStrings.TooltipFadeIn);
        Assert.Contains("(O)", UiStrings.TooltipFadeOut);
        Assert.Contains("(N)", UiStrings.TooltipNormalize);
        Assert.Contains("Delete", UiStrings.TooltipDelete);
        Assert.Contains("Ctrl+S", UiStrings.TooltipSave);
        Assert.Contains("Ctrl+Shift+M", UiStrings.TooltipSaveMp3);
        Assert.Contains("Ctrl+R", UiStrings.TooltipRecord);
    }

    [Fact]
    public void Tooltips_StayOnOneLine()
    {
        Assert.DoesNotContain("\n", UiStrings.TooltipPlay);
        Assert.DoesNotContain("\n", UiStrings.TooltipSave);
        Assert.DoesNotContain("\n", UiStrings.TooltipSaveMp3);
        Assert.DoesNotContain("\n", UiStrings.TooltipDelete);
    }
}
