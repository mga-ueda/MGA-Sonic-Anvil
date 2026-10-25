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
    [InlineData("PlayerFallbackWashNavyBrush", "AmbientWash")]
    [InlineData("PlayerGlowVeilBrush", "AmbientWash")]
    [InlineData("OverviewOutsideFillBrush", "Overview")]
    [InlineData("WaveFillBrush", "Waveform")]
    [InlineData("PlayheadBrush", "Guides")]
    [InlineData("WaveSelectionFillBrush", "Selection")]
    [InlineData("LoopRangeFillBrush", "Selection")]
    [InlineData("SampleLoopTimelineBrush", "SampleLoop")]
    [InlineData("CrossfadePreBrush", "Crossfade")]
    [InlineData("CrossfadePostBrush", "Crossfade")]
    [InlineData("RegionTimelineBrush", "Region")]
    [InlineData("MarkerBrush", "Marker")]
    [InlineData("PlayerFormatMismatchForeBrush", "Player")]
    [InlineData("PlayerJacketReflectionBrush", "Player")]
    [InlineData("LoudnessSafeBrush", "Loudness")]
    [InlineData("LevelMeterClipOnBrush", "Meter")]
    [InlineData("LevelGradFloorBrush", "Meter")]
    [InlineData("LevelGradCeilBrush", "Meter")]
    [InlineData("VectorScopeBackBrush", "VectorScope")]
    [InlineData("PlayerLevelMeterTickBrush", "Player")]
    [InlineData("TransportBackBrush", "Transport")]
    [InlineData("ProjectBarBackBrush", "Transport")]
    [InlineData("HistoryStripBackBrush", "Transport")]
    [InlineData("ActionCopyrightForeBrush", "Transport")]
    [InlineData("WaapiToggleOnBackBrush", "Transport")]
    [InlineData("StatusBarBackBrush", "StatusBar")]
    [InlineData("WaapiBarBackBrush", "StatusBar")]
    [InlineData("WindowBackBrush", "Dialog")]
    [InlineData("ColorPanelBackBrush", "Dialog")]
    [InlineData("DialogInputBackBrush", "Dialog")]
    [InlineData("UnknownBrush", "Other")]
    public void GroupOf_UsesKeyNotLabelPrefix(string key, string group)
    {
        Assert.Equal(group, ColorDevCatalog.GroupOf(key).ToString());
    }

    [Fact]
    public void Order_ImportanceThenAreaThenScreenDetail()
    {
        Assert.True(ColorDevCatalog.Rank("SurfaceBackBrush") < ColorDevCatalog.Rank("PlayerFallbackWashNavyBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayerFallbackWashNavyBrush") < ColorDevCatalog.Rank("PlayerGlowVeilBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayerGlowVeilBrush") < ColorDevCatalog.Rank("WaveFillBrush"));
        Assert.True(ColorDevCatalog.Rank("WaveFillBrush") < ColorDevCatalog.Rank("OverviewOutsideFillBrush"));
        Assert.True(ColorDevCatalog.Rank("OverviewOutsideFillBrush") < ColorDevCatalog.Rank("PlayheadBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayheadBrush") < ColorDevCatalog.Rank("WaveSelectionFillBrush"));
        Assert.True(ColorDevCatalog.Rank("WaveSelectionFillBrush") < ColorDevCatalog.Rank("LoopRangeFillBrush"));
        Assert.True(ColorDevCatalog.Rank("LoopRangeFillBrush") < ColorDevCatalog.Rank("SampleLoopTimelineBrush"));
        Assert.True(ColorDevCatalog.Rank("SampleLoopTimelineBrush") < ColorDevCatalog.Rank("CrossfadePreBrush"));
        Assert.True(ColorDevCatalog.Rank("CrossfadePreBrush") < ColorDevCatalog.Rank("CrossfadePostBrush"));
        Assert.True(ColorDevCatalog.Rank("CrossfadePostBrush") < ColorDevCatalog.Rank("RegionTimelineBrush"));
        Assert.True(ColorDevCatalog.Rank("RegionTimelineBrush") < ColorDevCatalog.Rank("MarkerBrush"));
        Assert.True(ColorDevCatalog.Rank("MarkerBrush") < ColorDevCatalog.Rank("LoudnessWaveFillBrush"));
        Assert.True(ColorDevCatalog.Rank("LoudnessWaveFillBrush") < ColorDevCatalog.Rank("LevelGradFloorBrush"));
        Assert.True(ColorDevCatalog.Rank("LevelGradFloorBrush") < ColorDevCatalog.Rank("LevelMeterTrackBackBrush"));
        Assert.True(ColorDevCatalog.Rank("LevelMeterTrackBackBrush") < ColorDevCatalog.Rank("VectorScopeBackBrush"));
        Assert.True(ColorDevCatalog.Rank("VectorScopeBackBrush") < ColorDevCatalog.Rank("PlayerJacketReflectionBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayerJacketReflectionBrush") < ColorDevCatalog.Rank("PlayerLevelMeterTickBrush"));
        Assert.True(ColorDevCatalog.Rank("PlayerLevelMeterTickBrush") < ColorDevCatalog.Rank("TransportBackBrush"));
        Assert.True(ColorDevCatalog.Rank("TransportBackBrush") < ColorDevCatalog.Rank("ProjectBarBackBrush"));
        Assert.True(ColorDevCatalog.Rank("ProjectBarBackBrush") < ColorDevCatalog.Rank("StatusBarBackBrush"));
        Assert.True(ColorDevCatalog.Rank("StatusBarBackBrush") < ColorDevCatalog.Rank("WindowBackBrush"));
        Assert.True(ColorDevCatalog.Rank("SampleLoopTimelineBrush") < ColorDevCatalog.Rank("SampleLoopGripBrush"));
        Assert.True(ColorDevCatalog.Rank("SampleLoopGripBrush") < ColorDevCatalog.Rank("SampleLoopWaveFillBrush"));
        Assert.True(ColorDevCatalog.Rank("SpectrogramGridBrush") < ColorDevCatalog.Rank("SpectrogramScaleForeBrush"));
        Assert.True(ColorDevCatalog.Rank("SpectrogramScaleForeBrush") < ColorDevCatalog.Rank("SpectrogramScaleEdgeBrush"));
        Assert.True(ColorDevCatalog.Rank("TransportPressedBackBrush") < ColorDevCatalog.Rank("RecordLatchForeBrush"));
    }

    [Fact]
    public void IsPlayerShared_IncludesAmbientWashAndPlayerUi()
    {
        Assert.True(ColorDevCatalog.IsPlayerShared("PlayerFallbackWashNavyBrush"));
        Assert.True(ColorDevCatalog.IsPlayerShared("PlayerGlowVeilBrush"));
        Assert.True(ColorDevCatalog.IsPlayerShared("PlayerWaveFillBrush"));
        Assert.False(ColorDevCatalog.IsPlayerShared("SurfaceBackBrush"));
        Assert.False(ColorDevCatalog.IsPlayerShared("WaveFillBrush"));
    }

    [Fact]
    public void GroupTitle_FollowsLanguage()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("共通", ColorDevCatalog.GroupTitle(ColorDevGroup.Shared));
            Assert.Equal("環境ウォッシュ", ColorDevCatalog.GroupTitle(ColorDevGroup.AmbientWash));
            Assert.Equal("波形", ColorDevCatalog.GroupTitle(ColorDevGroup.Waveform));
            Assert.Equal("ガイド", ColorDevCatalog.GroupTitle(ColorDevGroup.Guides));
            Assert.Equal("レベル（メーター／スペアナ）", ColorDevCatalog.GroupTitle(ColorDevGroup.Meter));
            Assert.Equal("トランスポート", ColorDevCatalog.GroupTitle(ColorDevGroup.Transport));
            Assert.Equal("プレイヤー UI", ColorDevCatalog.GroupTitle(ColorDevGroup.Player));

            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Shared", ColorDevCatalog.GroupTitle(ColorDevGroup.Shared));
            Assert.Equal("Ambient wash", ColorDevCatalog.GroupTitle(ColorDevGroup.AmbientWash));
            Assert.Equal("Waveform", ColorDevCatalog.GroupTitle(ColorDevGroup.Waveform));
            Assert.Equal("Guides", ColorDevCatalog.GroupTitle(ColorDevGroup.Guides));
            Assert.Equal("Level (meter · spectrum)", ColorDevCatalog.GroupTitle(ColorDevGroup.Meter));
            Assert.Equal("Transport", ColorDevCatalog.GroupTitle(ColorDevGroup.Transport));
            Assert.Equal("Player UI", ColorDevCatalog.GroupTitle(ColorDevGroup.Player));
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
        Assert.Contains("x:Key=\"PlayerWaveFillBrush\" Color=\"#94C6D9FF\"", xaml);
        Assert.Contains("x:Key=\"PlayerFormatMismatchForeBrush\" Color=\"#FFFF8A1A\"", xaml);
        Assert.Contains("x:Key=\"PlayerFallbackWashNavyBrush\" Color=\"#FF0B1B49\"", xaml);
        Assert.Contains("x:Key=\"PlayerLevelMeterTrackBorderBrush\" Color=\"#FF5A605A\"", xaml);
        Assert.Contains("x:Key=\"PlayerLevelMeterTickBrush\" Color=\"#60FFFFFF\"", xaml);
        Assert.Contains("x:Key=\"PlayerVectorScopeGridBrush\" Color=\"#FF717174\"", xaml);
        Assert.Contains("x:Key=\"LevelMeterClipOnBrush\" Color=\"#FFFF0000\"", xaml);
        Assert.Contains("x:Key=\"LoudnessSafeBrush\" Color=\"#FF00FFFF\"", xaml);
        Assert.Contains("x:Key=\"RecordLatchForeBrush\" Color=\"#FFE24B4A\"", xaml);
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
    public void Labels_DescribeActualUse()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("アプリ基本背景", UiStrings.ColorLabel("SurfaceBackBrush"));
            Assert.Equal("ネイビー（ジャケットなし）", UiStrings.ColorLabel("PlayerFallbackWashNavyBrush"));
            Assert.Equal("ベール（暗幕）", UiStrings.ColorLabel("PlayerGlowVeilBrush"));
            Assert.Equal("エリア背景", UiStrings.ColorLabel("WaveformBackBrush"));
            Assert.Equal("控えめクロームのボタン枠", UiStrings.ColorLabel("ProjectBarBackBrush"));
            Assert.Equal("バー色・暗い端", UiStrings.ColorLabel("LevelGradFloorBrush"));
            Assert.Equal("パネル面（見出しなど）", UiStrings.ColorLabel("ColorPanelBackBrush"));
            Assert.Equal("ジャケット下の映り込み", UiStrings.ColorLabel("PlayerJacketReflectionBrush"));
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void Matches_LabelKeyHexAndGroup()
    {
        Assert.True(ColorDevCatalog.Matches("波形塗り", "WaveFillBrush", "#C6D9FF", "波形"));
        Assert.True(ColorDevCatalog.Matches("波形塗り", "WaveFillBrush", "#C6D9FF", "wave"));
        Assert.True(ColorDevCatalog.Matches("波形塗り", "WaveFillBrush", "#C6D9FF", "C6"));
        Assert.False(ColorDevCatalog.Matches("波形塗り", "WaveFillBrush", "#C6D9FF", "playhead"));
        Assert.True(ColorDevCatalog.Matches("波形塗り", "WaveFillBrush", "#C6D9FF", " "));
        Assert.True(ColorDevCatalog.Matches("エリア背景", "WaveformBackBrush", "#262626", "波形", "波形"));
        Assert.False(ColorDevCatalog.Matches("エリア背景", "WaveformBackBrush", "#262626", "波形", "playhead"));
        Assert.True(ColorDevCatalog.Matches(
            "マーカー編集欄",
            "TimelineWellBackBrush",
            "#17171A",
            "波形",
            "編集"));
        Assert.True(ColorDevCatalog.Matches(
            "タイル仕切り",
            "WaveformTileDividerBrush",
            "#00000000",
            "波形",
            "仕切り"));
        Assert.True(ColorDevCatalog.Matches(
            "ネイビー（ジャケットなし）",
            "PlayerFallbackWashNavyBrush",
            "#0B1B49",
            "環境ウォッシュ",
            "ウォッシュ"));
    }

    [Fact]
    public void SearchFilter_DoesNotStealFocusOrReselectDuringIme()
    {
        Assert.False(ColorDevPanelWindow.ShouldFocusListAfterSelect(searchBoxFocused: true));
        Assert.True(ColorDevPanelWindow.ShouldFocusListAfterSelect(searchBoxFocused: false));
        Assert.False(ColorDevPanelWindow.ShouldReselectWhileFiltering(imeComposing: true));
        Assert.True(ColorDevPanelWindow.ShouldReselectWhileFiltering(imeComposing: false));
    }

    [Fact]
    public void Title_UsesPlainColorSettingsLabel()
    {
        var dark = UiStrings.ColorDevTitleFor();
        Assert.DoesNotContain("開発者", dark);
        Assert.DoesNotContain("developer", dark, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("モード", dark);
        Assert.DoesNotContain("mode", dark, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            dark.Contains("色設定", StringComparison.Ordinal)
            || dark.Contains("Color settings", StringComparison.Ordinal));
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
