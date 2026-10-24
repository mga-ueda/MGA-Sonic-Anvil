using System.IO;
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
        Assert.Contains(UiStrings.CopyrightFfmpegLink, text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.CopyrightEffetuneLink, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL Open Font License", text, StringComparison.Ordinal);
        Assert.Equal(2, BrandLicenseAlign.LineCount(text));
        var first = BrandLicenseAlign.FirstLine(text);
        Assert.Contains(
            UiStrings.CopyrightGitHub + " / " + UiStrings.CopyrightMitLink + " / " + UiStrings.CopyrightLameLink
                + " / " + UiStrings.CopyrightFfmpegLink + " / " + UiStrings.CopyrightEffetuneLink,
            first,
            StringComparison.Ordinal);
        Assert.Equal(1, BrandLicenseAlign.FindLineIndex(text, UiStrings.CopyrightWwiseLine));
    }

    [Fact]
    public void LicenseUrls_PointAtMitLameFfmpegAndThirdPartyNotices()
    {
        Assert.Equal(AppVersion.RepositoryUrl + "/blob/main/LICENSE", AppVersion.LicenseUrl);
        Assert.Equal(
            AppVersion.RepositoryUrl + "/blob/main/THIRD_PARTY_NOTICES.md",
            AppVersion.ThirdPartyNoticesUrl);
        Assert.Equal("https://github.com/Frieve-A/effetune", AppVersion.EffetuneProjectUrl);
        Assert.Equal(
            "https://github.com/Frieve-A/effetune/blob/main/LICENSE",
            AppVersion.EffetuneLicenseUrl);
        Assert.Equal("https://lame.sourceforge.io/", AppVersion.LameProjectUrl);
        Assert.Equal("https://ffmpeg.org/", AppVersion.FfmpegProjectUrl);
    }

    [Fact]
    public void ThirdPartyNoticesFile_IncludesEffetuneMitAndDatasetAttribution()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.Contains("Copyright (c) 2025-2026, Yoshiyuki Kobayashi", text, StringComparison.Ordinal);
        Assert.Contains("MIT License", text, StringComparison.Ordinal);
        Assert.Contains("Slakh2100-redux", text, StringComparison.Ordinal);
        Assert.Contains("Groove MIDI Dataset", text, StringComparison.Ordinal);
        Assert.Contains("CC BY 4.0", text, StringComparison.Ordinal);
        Assert.Contains("Ballroom", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EffetuneLicenseCopy_MatchesMitCopyright()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "LICENSE-EffeTune.txt");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.StartsWith("MIT License", text.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2025-2026, Yoshiyuki Kobayashi", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveEffetuneNoticesTarget_PrefersBundledThirdPartyNotices()
    {
        var target = MainWindow.ResolveEffetuneNoticesTarget();
        Assert.True(File.Exists(target), target);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md")),
            Path.GetFullPath(target));
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
