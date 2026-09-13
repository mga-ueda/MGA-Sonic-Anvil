using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;
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
        Assert.False(settings.ColorsLight!.ContainsKey("WaveFillBrush"));
        Assert.Equal("#FF0000", settings.ColorsLight["PlayheadBrush"]);
        Assert.Equal("#FF0000", settings.ColorsDark!["PlayheadBrush"]);
    }

    [Fact]
    public void MigrateLegacyColors_ThemeableOnly_WritesEmptyBags()
    {
        var settings = new AppSettings
        {
            Colors = new Dictionary<string, string> { ["WaveFillBrush"] = "#112233" },
        };

        UiColors.MigrateLegacyColors(settings);
        Assert.Null(settings.Colors);
        Assert.Empty(settings.ColorsLight!);
        Assert.Empty(settings.ColorsDark!);
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
