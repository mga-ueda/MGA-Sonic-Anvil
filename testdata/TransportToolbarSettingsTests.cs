using System.Text.Json;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TransportToolbarSettingsTests
{
    [Fact]
    public void Settings_DefaultShowsToolbarAndAllIcons()
    {
        var settings = new AppSettings();
        Assert.True(settings.ShowTransportToolbar);
        Assert.Empty(settings.HiddenTransportToolbarCommands);
        Assert.True(AppSettings.CreateDefault().ShowTransportToolbar);
        foreach (var command in TransportToolbarCatalog.AllCommands)
        {
            Assert.True(TransportToolbarCatalog.IsCommandVisible(settings, command));
        }
    }

    [Fact]
    public void Settings_HiddenCommandsRoundTrip()
    {
        var json = JsonSerializer.Serialize(
            new AppSettings
            {
                ShowTransportToolbar = false,
                HiddenTransportToolbarCommands = ["TogglePlayback", "Record"],
            },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.ShowTransportToolbar);
        Assert.Equal(["TogglePlayback", "Record"], back.HiddenTransportToolbarCommands);
        Assert.False(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.TogglePlayback));
        Assert.True(TransportToolbarCatalog.IsCommandVisible(back, TransportCommand.Open));
    }

    [Fact]
    public void NormalizeHidden_DropsUnknownAndDuplicates()
    {
        var normalized = TransportToolbarCatalog.NormalizeHidden(
            ["record", "Record", "NotACommand", " ", "Open"]);
        Assert.Equal(["Record", "Open"], normalized);
    }

    [Fact]
    public void HiddenFromVisible_HidesUnchecked()
    {
        var hidden = TransportToolbarCatalog.HiddenFromVisible(
            [TransportCommand.TogglePlayback, TransportCommand.Open]);
        Assert.Contains("Record", hidden);
        Assert.DoesNotContain("TogglePlayback", hidden);
        Assert.DoesNotContain("Open", hidden);
    }
}
