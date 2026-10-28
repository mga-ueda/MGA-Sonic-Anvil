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
        Assert.Contains(UiStrings.CopyrightWinBlurLink, text, StringComparison.Ordinal);
        Assert.DoesNotContain("SIL Open Font License", text, StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.Equal("© 2026 " + AppVersion.CompanyName, lines[0]);
        Assert.Equal(UiStrings.CopyrightWwiseLine, lines[1]);
        Assert.Equal(
            UiStrings.CopyrightGitHub + " / " + UiStrings.CopyrightMitLink + " / "
                + UiStrings.CopyrightLameLink + " / " + UiStrings.CopyrightFfmpegLink
                + " / " + UiStrings.CopyrightEffetuneLink
                + " / " + UiStrings.CopyrightWinBlurLink,
            lines[2]);
    }

    [Fact]
    public void LicenseUrls_PointAtMitLameFfmpegAndThirdPartyNotices()
    {
        Assert.Equal(AppVersion.RepositoryUrl + "/releases", AppVersion.ReleasesUrl);
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
        Assert.Equal("https://github.com/xauuzd/WinBlur", AppVersion.WinBlurProjectUrl);
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
        Assert.Contains("WinBlur", text, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) 2022 Pronner", text, StringComparison.Ordinal);
        Assert.Contains("https://github.com/xauuzd/WinBlur", text, StringComparison.Ordinal);
        Assert.Contains(UiStrings.CopyrightWwiseLine, text, StringComparison.Ordinal);
        Assert.Contains("Audiokinetic Inc.", text, StringComparison.Ordinal);
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
    public void ThirdPartyNotices_ResolvePath_IsAlongsideExecutable()
    {
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "THIRD_PARTY_NOTICES.md"),
            ThirdPartyNotices.ResolvePath());
        Assert.True(ThirdPartyNotices.TryRead(out var text));
        Assert.Contains("MIT License", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdPartyNotices_TryRead_FallsBackToEmbeddedWhenFileMissing()
    {
        var path = ThirdPartyNotices.ResolvePath();
        var backup = path + ".bak-test";
        Assert.True(File.Exists(path), path);
        File.Move(path, backup, overwrite: true);
        try
        {
            Assert.False(File.Exists(path));
            Assert.True(ThirdPartyNotices.TryRead(out var text));
            Assert.Contains("MIT License", text, StringComparison.Ordinal);
            Assert.Contains("WinBlur", text, StringComparison.Ordinal);
            Assert.Equal(
                "MgaSonicAnvil.THIRD_PARTY_NOTICES.md",
                ThirdPartyNotices.EmbeddedResourceName);
        }
        finally
        {
            if (File.Exists(backup))
            {
                File.Move(backup, path, overwrite: true);
            }
        }
    }

    [Fact]
    public void VersionThirdPartyLinks_OpenUpstreamGitHub()
    {
        Assert.Equal("https://github.com/Frieve-A/effetune", AppVersion.EffetuneProjectUrl);
        Assert.Equal("https://github.com/xauuzd/WinBlur", AppVersion.WinBlurProjectUrl);
        Assert.Equal("THIRD_PARTY_NOTICES.md", ThirdPartyNotices.FileName);
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
    public void TransportHostMinHeight_IsTransportBarOnly()
    {
        Assert.Equal(DesignMetrics.TransportBarHeight, DesignMetrics.TransportHostMinHeight);
    }
}
