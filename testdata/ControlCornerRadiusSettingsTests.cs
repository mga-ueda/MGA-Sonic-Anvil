using System.Text.Json;
using System.Windows.Controls;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ControlCornerRadiusSettingsTests
{
    [Fact]
    public void Settings_DefaultIsFiveAndRoundTrips()
    {
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, new AppSettings().ControlCornerRadius);
        Assert.Equal(5, AppSettings.CreateDefault().ResolvedControlCornerRadius());
        Assert.Equal(0, AppSettings.ClampControlCornerRadius(-1));
        Assert.Equal(16, AppSettings.ClampControlCornerRadius(40));
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, AppSettings.ClampControlCornerRadius(double.NaN));
        Assert.Equal(6, AppSettings.NudgeControlCornerRadius(5, 1));
        Assert.Equal(4, AppSettings.NudgeControlCornerRadius(5, -1));
        Assert.Equal(0, AppSettings.NudgeControlCornerRadius(0, -1));
        Assert.Equal(16, AppSettings.NudgeControlCornerRadius(16, 1));

        var json = JsonSerializer.Serialize(
            new AppSettings { ControlCornerRadius = 8 },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(8, back!.ResolvedControlCornerRadius());

        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.Equal(AppSettings.ControlCornerRadiusDefault, missing!.ResolvedControlCornerRadius());
    }

    [Fact]
    public void CornerBinding_FollowsTheSingleRadius()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var border = new Border();
                DesignMetrics.BindCorner(border);
                DesignMetrics.ApplyControlCornerRadius(9);
                Assert.Equal(9, border.CornerRadius.TopLeft);
                Assert.Equal(9, border.CornerRadius.BottomRight);
                var button = new RoundedButton();
                Assert.Equal(9, button.CornerRadius);
                DesignMetrics.ApplyControlCornerRadius(AppSettings.ControlCornerRadiusDefault);
                Assert.Equal(AppSettings.ControlCornerRadiusDefault, border.CornerRadius.TopLeft);
                Assert.Equal(AppSettings.ControlCornerRadiusDefault, button.CornerRadius);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error?.ToString());
    }
}
