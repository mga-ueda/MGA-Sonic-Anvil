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
}
