using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstTransportClockTests
{
    [Fact]
    public void Stopped_HoldsProjectTimeAcrossBlocks()
    {
        var transport = VstTransport.Live(playing: false, projectTimeSamples: 24000);
        var first = VstTransportClock.Advance(transport, 0, 100, 0, 512);
        var second = VstTransportClock.Advance(
            transport,
            first.NextFreeProjectTime,
            first.NextContinuousTime,
            0,
            512);

        Assert.False(first.Playing);
        Assert.Equal(24000, first.ProjectTimeSamples);
        Assert.Equal(100, first.ContinuousTimeSamples);
        Assert.Equal(24000, second.ProjectTimeSamples);
        Assert.Equal(100, second.ContinuousTimeSamples);
        Assert.Equal(100, second.NextContinuousTime);

        var ctx = VstTransportClock.Context(second, 48000, systemTimeNs: 5);
        Assert.Equal(0u, ctx.State & (uint)Vst3HostNative.ProcessContextPlaying);
        Assert.NotEqual(0u, ctx.State & (uint)Vst3HostNative.ProcessContextTempoValid);
        Assert.NotEqual(0u, ctx.State & (uint)Vst3HostNative.ProcessContextTimeSigValid);
        Assert.Equal(120, ctx.Tempo);
        Assert.Equal(4, ctx.TimeSigNumerator);
        Assert.Equal(24000, ctx.ProjectTimeSamples);
        Assert.Equal(1.0, ctx.ProjectTimeMusic, 6);
    }

    [Fact]
    public void Playing_AdvancesProjectTimeAndSetsPlaying()
    {
        var transport = VstTransport.Live(playing: true, projectTimeSamples: 1000);
        var step = VstTransportClock.Advance(transport, 0, 50, 0, 128);
        var ctx = VstTransportClock.Context(step, 48000, systemTimeNs: 1);

        Assert.True(step.Playing);
        Assert.Equal(1000, step.ProjectTimeSamples);
        Assert.Equal(178, step.NextContinuousTime);
        Assert.NotEqual(0u, ctx.State & (uint)Vst3HostNative.ProcessContextPlaying);
        Assert.Equal(1000, ctx.ProjectTimeSamples);
    }

    [Fact]
    public void AudibleTime_SubtractsLatencyOnlyWhilePlaying()
    {
        Assert.Equal(0, VstTransport.AudibleProjectSamples(1000, latencySamples: 48000, playing: true));
        Assert.Equal(2000, VstTransport.AudibleProjectSamples(50000, latencySamples: 48000, playing: true));
        Assert.Equal(50000, VstTransport.AudibleProjectSamples(50000, latencySamples: 48000, playing: false));
        Assert.Equal(1200, VstTransport.AudibleSourceFrame(1200, 48000, sourceRate: 48000, deviceRate: 48000, playing: false));
        Assert.Equal(0, VstTransport.AudibleSourceFrame(24000, 48000, sourceRate: 48000, deviceRate: 48000, playing: true));
        Assert.Equal(48000, VstTransport.AudibleSourceFrame(96000, 48000, sourceRate: 48000, deviceRate: 48000, playing: true));
    }

    [Fact]
    public void FreeRunning_KeepsPlayingClockForOffline()
    {
        var step = VstTransportClock.Advance(VstTransport.FreeRunning, 10, 10, 0, 32);
        Assert.True(step.Playing);
        Assert.Equal(10, step.ProjectTimeSamples);
        Assert.Equal(42, step.NextFreeProjectTime);
        Assert.Equal(42, step.NextContinuousTime);
    }
}
