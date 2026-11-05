using MgaSonicAnvil.Audio;
using NAudio.Wave.Asio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AsioBufferSwitchTimeInfoTests
{
    [Fact]
    public void TimeInfo_ForwardsToBufferSwitchAndReturnsTheTimePointer()
    {
        var calls = 0;
        var callbacks = new AsioCallbacks
        {
            pbufferSwitch = (index, direct) =>
            {
                calls++;
                Assert.Equal(1, index);
                Assert.True(direct);
            },
        };

        callbacks = AsioOutFactory.ForwardBufferSwitchTimeInfo(callbacks);
        var time = new IntPtr(0x1234);
        var returned = callbacks.pbufferSwitchTimeInfo(time, 1, true);

        Assert.Equal(1, calls);
        Assert.Equal(time, returned);
    }

    [Fact]
    public void TimeInfo_LeavesCallbacksUnchangedWhenBufferSwitchIsMissing()
    {
        var callbacks = AsioOutFactory.ForwardBufferSwitchTimeInfo(default);

        Assert.Null(callbacks.pbufferSwitch);
        Assert.Null(callbacks.pbufferSwitchTimeInfo);
    }
}
