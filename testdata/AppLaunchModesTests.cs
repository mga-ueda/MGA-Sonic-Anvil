using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppLaunchModesTests
{
    [Theory]
    [InlineData(null, "f12")]
    [InlineData("", "f12")]
    [InlineData("auto", "f12")]
    [InlineData("editor", "f12")]
    [InlineData("EDITOR", "f12")]
    [InlineData("analyzers", "f12")]
    [InlineData("f12", "f12")]
    [InlineData("player", "f10")]
    [InlineData("f10", "f10")]
    [InlineData("compact", "f9")]
    [InlineData("f9", "f9")]
    [InlineData("waveform", "f11")]
    [InlineData("F11", "f11")]
    public void Parse_DefaultsToF12_AndMapsLegacy(string? stored, string formatted)
    {
        var slot = AppLaunchModes.Parse(stored);
        Assert.Equal(formatted, AppLaunchModes.Format(slot));
        Assert.Equal(slot, AppLaunchModes.Parse(formatted));
    }

    [Fact]
    public void DefaultSlot_IsF12()
    {
        Assert.Equal(WindowLayoutSlot.F12, AppLaunchModes.DefaultSlot);
        Assert.Equal("f12", AppLaunchModes.Format(AppLaunchModes.DefaultSlot));
    }

    [Fact]
    public void TryParse_RejectsEmptyAndUnknown()
    {
        Assert.False(AppLaunchModes.TryParse(null, out _));
        Assert.False(AppLaunchModes.TryParse("", out _));
        Assert.False(AppLaunchModes.TryParse("auto", out _));
        Assert.True(AppLaunchModes.TryParse("f12", out var f12));
        Assert.Equal(WindowLayoutSlot.F12, f12);
        Assert.True(AppLaunchModes.TryParse("analyzers", out var analyzers));
        Assert.Equal(WindowLayoutSlot.F12, analyzers);
    }
}
