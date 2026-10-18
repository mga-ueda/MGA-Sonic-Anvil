using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SharedProcessIdTests
{
    [Fact]
    public void PublishThenRead_ReturnsSameProcessId()
    {
        var name = @"Local\MGA.SonicAnvil.TestOwnerPid." + Guid.NewGuid().ToString("N");
        using var map = SharedProcessId.Publish(name, 4242);
        Assert.NotNull(map);
        Assert.True(SharedProcessId.TryRead(name, out var pid));
        Assert.Equal(4242, pid);
    }

    [Fact]
    public void TryRead_MissingMap_ReturnsFalse()
    {
        var name = @"Local\MGA.SonicAnvil.TestOwnerPid." + Guid.NewGuid().ToString("N");
        Assert.False(SharedProcessId.TryRead(name, out var pid));
        Assert.Equal(0, pid);
    }

    [Fact]
    public void TryAllowProcess_CurrentProcess_DoesNotThrow()
    {
        ForegroundActivation.TryAllowProcess(Environment.ProcessId);
        ForegroundActivation.TryAllowAny();
    }
}
