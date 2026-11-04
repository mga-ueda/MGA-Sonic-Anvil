using System.Text.Json;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ShowMainMenuBarSettingsTests
{
    [Fact]
    public void Settings_DefaultOnAndRoundTrips()
    {
        Assert.True(new AppSettings().ShowMainMenuBar);
        Assert.True(AppSettings.CreateDefault().ShowMainMenuBar);
        var json = JsonSerializer.Serialize(
            new AppSettings { ShowMainMenuBar = false },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.ShowMainMenuBar);
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.True(missing!.ShowMainMenuBar);
    }

    [Fact]
    public void MainMenuBarAlign_DefaultsLeftAndRoundTrips()
    {
        Assert.Equal(LayoutMainMenuBarAlign.Left, new AppSettings().ResolvedMainMenuBarAlign());
        Assert.Equal(
            LayoutMainMenuBarAlign.Left,
            AppSettings.CreateDefault().ResolvedMainMenuBarAlign());

        var centered = new AppSettings();
        centered.ApplyMainMenuBarAlign(LayoutMainMenuBarAlign.Center);
        Assert.Equal("Center", centered.MainMenuBarAlign);
        Assert.Equal(LayoutMainMenuBarAlign.Center, centered.ResolvedMainMenuBarAlign());

        var json = JsonSerializer.Serialize(centered, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(LayoutMainMenuBarAlign.Center, back!.ResolvedMainMenuBarAlign());

        // 旧設定で欠落・空文字なら左。
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(LayoutMainMenuBarAlign.Left, missing!.ResolvedMainMenuBarAlign());

        var empty = new AppSettings { MainMenuBarAlign = "" };
        Assert.Equal(LayoutMainMenuBarAlign.Left, empty.ResolvedMainMenuBarAlign());
    }
}
