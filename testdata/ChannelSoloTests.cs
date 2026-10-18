using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelSoloTests
{
    [Fact]
    public void Next_CyclesThenClears()
    {
        Assert.Equal(0, ChannelSolo.Next(ChannelSolo.Off, 2));
        Assert.Equal(1, ChannelSolo.Next(0, 2));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Next(1, 2));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Next(0, 1));
    }

    [Fact]
    public void Previous_CyclesThenClears()
    {
        Assert.Equal(1, ChannelSolo.Previous(ChannelSolo.Off, 2));
        Assert.Equal(0, ChannelSolo.Previous(1, 2));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Previous(0, 2));
        Assert.Equal(0, ChannelSolo.Previous(ChannelSolo.Off, 1));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Previous(0, 1));
    }

    [Fact]
    public void Clamp_DropsOutOfRange()
    {
        Assert.Equal(1, ChannelSolo.Clamp(1, 6));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Clamp(5, 2));
        Assert.Equal(ChannelSolo.Off, ChannelSolo.Clamp(ChannelSolo.Off, 6));
    }

    [Fact]
    public void Playback_MutesOtherFileLanes()
    {
        var samples = new float[] { 0.4f, -0.3f, 0.4f, -0.3f };
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(2, [0, 1]);
        provider.SetSoloChannel(0);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[4];
        Assert.Equal(4, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.4f, buffer[0], 3);
        Assert.Equal(0f, buffer[1], 3);
        Assert.Equal(0.4f, buffer[2], 3);
        Assert.Equal(0f, buffer[3], 3);
    }

    [Fact]
    public void Toggle_ClickSolosOrClears()
    {
        Assert.Equal(1, ChannelSolo.Toggle(0, 0, 4, add: false));
        Assert.Equal(0, ChannelSolo.Toggle(1, 0, 4, add: false));
        Assert.Equal(2, ChannelSolo.Toggle(1 | 2, 1, 4, add: false));
        Assert.Equal(0, ChannelSolo.Toggle(0, 0, 1, add: false));
    }

    [Fact]
    public void Toggle_CtrlAddsAndRemoves()
    {
        Assert.Equal(1, ChannelSolo.Toggle(0, 0, 4, add: true));
        Assert.Equal(1 | 2, ChannelSolo.Toggle(1, 1, 4, add: true));
        Assert.Equal(1, ChannelSolo.Toggle(1 | 2, 1, 4, add: true));
        Assert.Equal(0, ChannelSolo.Toggle(4, 2, 4, add: true));
        Assert.Equal(0, ChannelSolo.Toggle(2 | 4 | 8, 0, 4, add: true));
    }

    [Fact]
    public void Mute_RemovesLaneFromAudibleSet()
    {
        Assert.Equal(14, ChannelSolo.Mute(0, 0, 4));
        Assert.Equal(0, ChannelSolo.Mute(14, 0, 4));
        Assert.Equal(12, ChannelSolo.Mute(14, 1, 4));
        Assert.Equal(0, ChannelSolo.Mute(1, 0, 4));
        Assert.Equal(0, ChannelSolo.Mute(0, 0, 1));
        Assert.Equal(0, ChannelSolo.Mute(0, 4, 4));
    }

    [Fact]
    public void ClampMask_AllOrEmptyIsOff()
    {
        Assert.Equal(0, ChannelSolo.ClampMask(0, 4));
        Assert.Equal(0, ChannelSolo.ClampMask(15, 4));
        Assert.Equal(5, ChannelSolo.ClampMask(5 | 16, 4));
        Assert.Equal(0, ChannelSolo.ClampMask(1, 1));
    }

    [Fact]
    public void StepMask_FollowsPrimaryThenSingle()
    {
        Assert.Equal(1, ChannelSolo.StepMask(0, 3, 1));
        Assert.Equal(2, ChannelSolo.StepMask(1 | 4, 3, 1));
        Assert.Equal(0, ChannelSolo.StepMask(4, 3, 1));
        Assert.Equal(4, ChannelSolo.StepMask(0, 3, -1));
    }

    [Fact]
    public void ResolveMask_PrefersExplicitMask()
    {
        Assert.Equal(1 | 4, ChannelSolo.ResolveMask(0, 1 | 4, 4));
        Assert.Equal(1, ChannelSolo.ResolveMask(0, 0, 4));
        Assert.Equal(0, ChannelSolo.ResolveMask(ChannelSolo.Off, 0, 4));
    }

    [Fact]
    public void Playback_KeepsEverySelectedLane()
    {
        var samples = new float[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.1f, 0.2f, 0.3f, 0.4f };
        var document = new AudioDocument(samples, 48000, 4, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(4, [0, 1, 2, 3]);
        provider.SetSoloMask(1 | 4);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[8];
        Assert.Equal(8, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.1f, buffer[0], 3);
        Assert.Equal(0f, buffer[1], 3);
        Assert.Equal(0.3f, buffer[2], 3);
        Assert.Equal(0f, buffer[3], 3);
    }
}
