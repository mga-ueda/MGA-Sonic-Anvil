using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class UiColorsTests
{
    [Fact]
    public void TryWrite_MutatesTheSameBrush()
    {
        RunSta(() =>
        {
            var brush = new SolidColorBrush(Colors.Red);
            var dictionary = new ResourceDictionary { ["TestBrush"] = brush };
            Assert.True(UiColors.TryWrite(dictionary, "TestBrush", Colors.Blue));
            Assert.Same(brush, dictionary["TestBrush"]);
            Assert.Equal(Colors.Blue, brush.Color);
        });
    }

    [Fact]
    public void TryWrite_FollowsMergedDictionary()
    {
        RunSta(() =>
        {
            var brush = new SolidColorBrush(Color.FromRgb(0xB6, 0xB6, 0xB6));
            var merged = new ResourceDictionary { ["WaveFillBrush"] = brush };
            var root = new ResourceDictionary();
            root.MergedDictionaries.Add(merged);
            Assert.True(UiColors.TryWrite(root, "WaveFillBrush", Color.FromRgb(0x00, 0xF5, 0xFF)));
            Assert.Equal(Color.FromRgb(0x00, 0xF5, 0xFF), brush.Color);
            Assert.Same(brush, root["WaveFillBrush"]);
        });
    }

    [Fact]
    public void CollectOverrides_KeepsOnlyDifferences()
    {
        var current = new (string Key, Color Value)[]
        {
            ("WaveFillBrush", Color.FromRgb(0x88, 0xBB, 0xFF)),
            ("PlayheadBrush", Color.FromRgb(0xFF, 0x00, 0x00)),
        };
        var defaults = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["WaveFillBrush"] = Color.FromRgb(0x88, 0xBB, 0xFF),
            ["PlayheadBrush"] = Color.FromRgb(0x00, 0xF5, 0xFF),
        };

        var saved = UiColors.CollectOverrides(current, key => defaults[key]);
        Assert.False(saved.ContainsKey("WaveFillBrush"));
        Assert.Equal("#FF0000", saved["PlayheadBrush"]);
    }

    [Fact]
    public void FormatColor_IncludesAlphaWhenTransparent()
    {
        Assert.Equal("#112233", UiColors.FormatColor(Color.FromRgb(0x11, 0x22, 0x33)));
        Assert.Equal(
            "#80112233",
            UiColors.FormatColor(Color.FromArgb(0x80, 0x11, 0x22, 0x33)));
    }

    [Fact]
    public void WithParsedAlpha_KeepsLegacySixDigitAlpha()
    {
        var parsed = Color.FromRgb(0x11, 0x22, 0x33);
        var kept = UiColors.WithParsedAlpha(parsed, "#112233", 0x8C);
        Assert.Equal(0x8C, kept.A);
        Assert.Equal(0x11, kept.R);

        Assert.True(UiColors.TryParseColor("#40112233", out var eight));
        var full = UiColors.WithParsedAlpha(eight, "#40112233", 0x8C);
        Assert.Equal(0x40, full.A);
    }

    [Fact]
    public void CollectOverrides_PersistsAlphaChange()
    {
        var current = new (string Key, Color Value)[]
        {
            ("TileSearchVeilBrush", Color.FromArgb(0x40, 0x00, 0x00, 0x00)),
        };
        var defaults = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["TileSearchVeilBrush"] = Color.FromArgb(0x8C, 0x00, 0x00, 0x00),
        };

        var saved = UiColors.CollectOverrides(current, key => defaults[key]);
        Assert.Equal("#40000000", saved["TileSearchVeilBrush"]);
    }

    [Fact]
    public void MigrateLegacyColors_KeepsSharedAccentsOnly()
    {
        var settings = new AppSettings
        {
            Colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#112233",
                ["PlayheadBrush"] = "#FF0000",
            },
        };

        UiColors.MigrateLegacyColors(settings);
        Assert.Null(settings.Colors);
        Assert.Null(settings.ColorsLight);
        Assert.False(settings.ColorsDark!.ContainsKey("WaveFillBrush"));
        Assert.Equal("#FF0000", settings.ColorsDark["PlayheadBrush"]);
    }

    [Fact]
    public void MigrateLegacyColors_ThemeableOnly_WritesEmptyDarkBag()
    {
        var settings = new AppSettings
        {
            Colors = new Dictionary<string, string> { ["WaveFillBrush"] = "#112233" },
        };

        UiColors.MigrateLegacyColors(settings);
        Assert.Null(settings.Colors);
        Assert.Null(settings.ColorsLight);
        Assert.Empty(settings.ColorsDark!);
    }

    [Fact]
    public void MigrateLightColors_FoldsLightIntoEmptyDark()
    {
        var settings = new AppSettings
        {
            ColorsLight = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#AABBCC",
            },
        };

        UiColors.MigrateLightColors(settings);
        Assert.Null(settings.ColorsLight);
        Assert.Equal("#AABBCC", settings.ColorsDark!["WaveFillBrush"]);
    }

    [Fact]
    public void MigrateLightColors_KeepsDarkAndClearsLight()
    {
        var settings = new AppSettings
        {
            ColorsLight = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#AABBCC",
            },
            ColorsDark = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["WaveFillBrush"] = "#112233",
            },
        };

        UiColors.MigrateLightColors(settings);
        Assert.Null(settings.ColorsLight);
        Assert.Equal("#112233", settings.ColorsDark!["WaveFillBrush"]);
    }

    [Fact]
    public void MigrateLegacyColors_SkipsWhenThemeBagsExist()
    {
        var settings = new AppSettings
        {
            Colors = new Dictionary<string, string> { ["PlayheadBrush"] = "#FF0000" },
            ColorsDark = new Dictionary<string, string> { ["PlayheadBrush"] = "#00FF00" },
        };

        UiColors.MigrateLegacyColors(settings);
        Assert.Equal("#00FF00", settings.ColorsDark["PlayheadBrush"]);
        Assert.Null(settings.ColorsLight);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
