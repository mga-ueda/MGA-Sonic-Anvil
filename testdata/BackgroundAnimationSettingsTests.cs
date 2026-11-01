using System.Text.Json;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class BackgroundAnimationSettingsTests
{
    [Fact]
    public void Settings_DefaultOffAndRoundTrips()
    {
        Assert.False(new AppSettings().EnableBackgroundAnimation);
        Assert.False(AppSettings.CreateDefault().EnableBackgroundAnimation);
        Assert.Equal(
            AppSettings.BackgroundAnimationSpeedPercentDefault,
            new AppSettings().BackgroundAnimationSpeedPercent);
        Assert.Equal(
            AppSettings.BackgroundAnimationSpeedPercentDefault,
            AppSettings.CreateDefault().BackgroundAnimationSpeedPercent);

        var json = JsonSerializer.Serialize(
            new AppSettings
            {
                EnableBackgroundAnimation = true,
                BackgroundAnimationSpeedPercent = 200,
            },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.True(back!.EnableBackgroundAnimation);
        Assert.Equal(200, back.BackgroundAnimationSpeedPercent);

        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.False(missing!.EnableBackgroundAnimation);
        Assert.Equal(
            AppSettings.BackgroundAnimationSpeedPercentDefault,
            missing.BackgroundAnimationSpeedPercent);
    }

    [Fact]
    public void Speed_ClampParseAndScale()
    {
        Assert.Equal(
            AppSettings.BackgroundAnimationSpeedPercentDefault,
            AppSettings.BackgroundAnimationSpeedPercentMin);
        Assert.Equal(100, AppSettings.ClampBackgroundAnimationSpeedPercent(100));
        Assert.Equal(100, AppSettings.ClampBackgroundAnimationSpeedPercent(1));
        Assert.Equal(400, AppSettings.ClampBackgroundAnimationSpeedPercent(999));

        var settings = new AppSettings { BackgroundAnimationSpeedPercent = 200 };
        Assert.Equal(2.0, settings.ResolvedBackgroundAnimationSpeed(), 3);
        Assert.Equal(5.5, settings.ScaleBackgroundAnimationSeconds(11), 3);

        settings.BackgroundAnimationSpeedPercent = 50;
        Assert.Equal(1.0, settings.ResolvedBackgroundAnimationSpeed(), 3);
        Assert.Equal(11, settings.ScaleBackgroundAnimationSeconds(11), 3);

        Assert.Equal(105, AppSettings.NudgeBackgroundAnimationSpeedPercent(100, 1));
        Assert.Equal(100, AppSettings.NudgeBackgroundAnimationSpeedPercent(100, -1));
        Assert.Equal(400, AppSettings.NudgeBackgroundAnimationSpeedPercent(400, 1));
    }
}

