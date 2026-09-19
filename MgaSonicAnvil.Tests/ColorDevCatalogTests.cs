using System.IO;
using System.Text.RegularExpressions;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ColorDevCatalogTests
{
    [Theory]
    [InlineData("SurfaceBackBrush", "Shared")]
    [InlineData("MenuHighlightBackBrush", "Shared")]
    [InlineData("MenuDisabledForeBrush", "Shared")]
    [InlineData("ProjectBarBackBrush", "Overview")]
    [InlineData("WaveFillBrush", "Waveform")]
    [InlineData("PlayheadBrush", "Guides")]
    [InlineData("WaveSelectionFillBrush", "Selection")]
    [InlineData("LoopRangeFillBrush", "Selection")]
    [InlineData("SampleLoopTimelineBrush", "SampleLoop")]
    [InlineData("RegionTimelineBrush", "Region")]
    [InlineData("MarkerBrush", "Marker")]
    [InlineData("LevelMeterTrackBackBrush", "Meter")]
    [InlineData("LevelGradFloorBrush", "Spectrum")]
    [InlineData("LevelGradCeilBrush", "Spectrum")]
    [InlineData("VectorScopeBackBrush", "VectorScope")]
    [InlineData("TransportBackBrush", "Transport")]
    [InlineData("HistoryStripBackBrush", "Transport")]
    [InlineData("ActionCopyrightForeBrush", "Transport")]
    [InlineData("WaapiToggleOnBackBrush", "Transport")]
    [InlineData("StatusBarBackBrush", "StatusBar")]
    [InlineData("WaapiBarBackBrush", "StatusBar")]
    [InlineData("WindowBackBrush", "Dialog")]
    [InlineData("ColorPanelBackBrush", "Dialog")]
    [InlineData("UnknownBrush", "Other")]
    public void GroupOf_UsesKeyNotLabelPrefix(string key, string group)
    {
        Assert.Equal(group, ColorDevCatalog.GroupOf(key).ToString());
    }

    [Fact]
    public void Order_SharedThenScreenTopToBottom()
    {
        Assert.True(ColorDevCatalog.Rank("SurfaceBackBrush") < ColorDevCatalog.Rank("ProjectBarBackBrush"));
        Assert.True(ColorDevCatalog.Rank("ProjectBarBackBrush") < ColorDevCatalog.Rank("WaveFillBrush"));
        Assert.True(ColorDevCatalog.Rank("WaveFillBrush") < ColorDevCatalog.Rank("PlayheadBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayheadBrush") < ColorDevCatalog.Rank("WaveSelectionFillBrush"));
        Assert.True(ColorDevCatalog.Rank("WaveSelectionFillBrush") < ColorDevCatalog.Rank("LoopRangeFillBrush"));
        Assert.True(ColorDevCatalog.Rank("LoopRangeFillBrush") < ColorDevCatalog.Rank("SampleLoopTimelineBrush"));
        Assert.True(ColorDevCatalog.Rank("SampleLoopTimelineBrush") < ColorDevCatalog.Rank("RegionTimelineBrush"));
        Assert.True(ColorDevCatalog.Rank("RegionTimelineBrush") < ColorDevCatalog.Rank("MarkerBrush"));
        Assert.True(ColorDevCatalog.Rank("MarkerBrush") < ColorDevCatalog.Rank("LevelMeterTrackBackBrush"));
        Assert.True(ColorDevCatalog.Rank("LevelMeterTrackBackBrush") < ColorDevCatalog.Rank("LevelGradFloorBrush"));
        Assert.True(ColorDevCatalog.Rank("LevelGradFloorBrush") < ColorDevCatalog.Rank("VectorScopeBackBrush"));
        Assert.True(ColorDevCatalog.Rank("VectorScopeBackBrush") < ColorDevCatalog.Rank("TransportBackBrush"));
        Assert.True(ColorDevCatalog.Rank("TransportBackBrush") < ColorDevCatalog.Rank("StatusBarBackBrush"));
        Assert.True(ColorDevCatalog.Rank("StatusBarBackBrush") < ColorDevCatalog.Rank("WindowBackBrush"));
        Assert.True(ColorDevCatalog.Rank("WindowBackBrush") < ColorDevCatalog.Rank("UnknownBrush"));
    }

    [Fact]
    public void GroupTitle_FollowsLanguage()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("共通", ColorDevCatalog.GroupTitle(ColorDevGroup.Shared));
            Assert.Equal("波形", ColorDevCatalog.GroupTitle(ColorDevGroup.Waveform));
            Assert.Equal("ガイド", ColorDevCatalog.GroupTitle(ColorDevGroup.Guides));
            Assert.Equal("トランスポート", ColorDevCatalog.GroupTitle(ColorDevGroup.Transport));

            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Shared", ColorDevCatalog.GroupTitle(ColorDevGroup.Shared));
            Assert.Equal("Waveform", ColorDevCatalog.GroupTitle(ColorDevGroup.Waveform));
            Assert.Equal("Guides", ColorDevCatalog.GroupTitle(ColorDevGroup.Guides));
            Assert.Equal("Transport", ColorDevCatalog.GroupTitle(ColorDevGroup.Transport));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Catalog_CoversEveryXamlBrush()
    {
        var xaml = File.ReadAllText(FindUiColorsXaml());
        Assert.Contains("x:Key=\"WaveFillBrush\" Color=\"#FFC6D9FF\"", xaml);
        Assert.Contains("x:Key=\"LevelGradFloorBrush\" Color=\"#FF005C8C\"", xaml);
        Assert.Contains("x:Key=\"LevelGradLowBrush\" Color=\"#FF0071AC\"", xaml);
        Assert.Contains("x:Key=\"LevelGradCeilBrush\" Color=\"#FFC8EFFF\"", xaml);
        var keys = Regex.Matches(xaml, @"x:Key=""(?<key>\w+Brush)""")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.NotEmpty(keys);
        foreach (var key in keys)
        {
            Assert.True(
                ColorDevCatalog.GroupOf(key) != ColorDevGroup.Other,
                key);
        }

        Assert.Equal(keys.Length, ColorDevCatalog.Keys.Count);
    }

    [Fact]
    public void Labels_AreItemNamesNotRawKeys()
    {
        foreach (var key in ColorDevCatalog.Keys)
        {
            var token = key.EndsWith("Brush", StringComparison.Ordinal) ? key[..^5] : key;
            Assert.NotEqual(token, UiStrings.ColorLabel(key));
        }
    }

    [Fact]
    public void Matches_LabelKeyHexAndGroup()
    {
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#C6D9FF", "波形"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#C6D9FF", "wave"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#C6D9FF", "C6"));
        Assert.False(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#C6D9FF", "playhead"));
        Assert.True(ColorDevCatalog.Matches("波形", "WaveFillBrush", "#C6D9FF", " "));
        Assert.True(ColorDevCatalog.Matches("エリア背景", "WaveformBackBrush", "#262626", "波形", "波形"));
        Assert.False(ColorDevCatalog.Matches("エリア背景", "WaveformBackBrush", "#262626", "波形", "playhead"));
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

    private static string FindUiColorsXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, "Themes", "UiColors.xaml");
            if (File.Exists(path))
            {
                return path;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Themes/UiColors.xaml が見つかりません。");
    }
}
