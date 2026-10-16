using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppLaunchModesTests
{
    [Theory]
    [InlineData(null, "editor")]
    [InlineData("", "editor")]
    [InlineData("auto", "editor")]
    [InlineData("EDITOR", "editor")]
    [InlineData("player", "player")]
    [InlineData("compact", "compact")]
    [InlineData("waveform", "waveform")]
    [InlineData("ANALYZERS", "analyzers")]
    public void Parse_EmptyIsEditor(string? stored, string formatted)
    {
        var mode = AppLaunchModes.Parse(stored);
        Assert.Equal(formatted, AppLaunchModes.Format(mode));
        Assert.Equal(mode, AppLaunchModes.Parse(formatted));
    }
}
