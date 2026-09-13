using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BrandLicenseNoticeTests
{
    [Fact]
    public void CopyrightText_IncludesImporterWwiseLineAndMitLameLinks()
    {
        var text = UiStrings.CopyrightText;
        Assert.Contains("© 2026 " + AppVersion.CompanyName, text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.CopyrightGitHub, text, StringComparison.Ordinal);
        Assert.Equal(
            "Wwise® and Audiokinetic® are trademarks of Audiokinetic Inc.",
            UiStrings.CopyrightWwiseLine);
        Assert.Contains(UiStrings.CopyrightWwiseLine, text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.CopyrightMitLink, text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.CopyrightLameLink, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL Open Font License", text, StringComparison.Ordinal);
        Assert.Equal(2, BrandLicenseAlign.LineCount(text));
        var first = BrandLicenseAlign.FirstLine(text);
        Assert.Contains(
            UiStrings.CopyrightGitHub + " / " + UiStrings.CopyrightMitLink + " / " + UiStrings.CopyrightLameLink,
            first,
            StringComparison.Ordinal);
        Assert.Equal(1, BrandLicenseAlign.FindLineIndex(text, UiStrings.CopyrightWwiseLine));
    }

    [Fact]
    public void TipCopyright_CoversMitLameAndWaapi()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Contains("MIT License", UiStrings.TipCopyright, StringComparison.Ordinal);
            Assert.Contains("LAME", UiStrings.TipCopyright, StringComparison.Ordinal);
            Assert.Contains("WAAPI", UiStrings.TipCopyright, StringComparison.Ordinal);
            Assert.Contains("Wwise", UiStrings.TipCopyright, StringComparison.Ordinal);
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Contains("MIT License", UiStrings.TipCopyright, StringComparison.Ordinal);
            Assert.Contains("LAME", UiStrings.TipCopyright, StringComparison.Ordinal);
            Assert.Contains("WAAPI", UiStrings.TipCopyright, StringComparison.Ordinal);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void LicenseUrls_PointAtMitAndLame()
    {
        Assert.Equal(AppVersion.RepositoryUrl + "/blob/main/LICENSE", AppVersion.LicenseUrl);
        Assert.Equal("https://lame.sourceforge.io/", AppVersion.LameProjectUrl);
    }

    [Fact]
    public void ForeForLink_HighlightsOnlyHoveredLink()
    {
        var normal = new SolidColorBrush(Colors.SteelBlue);
        var hover = new SolidColorBrush(Colors.Cyan);
        var rest = new SolidColorBrush(Colors.Gray);
        Assert.Same(hover, BrandLicenseLabel.ForeForLink(true, normal, hover, rest));
        Assert.Same(normal, BrandLicenseLabel.ForeForLink(false, normal, hover, rest));
    }

    [Fact]
    public void BrandRowHeight_FitsTwoLineLicenseBlock()
    {
        var need = Math.Max(DesignMetrics.BrandLogoHeight, DesignMetrics.BrandLicenseHeight)
            + DesignMetrics.BrandRowPad;
        Assert.Equal(need, DesignMetrics.BrandRowHeight);
    }
}
