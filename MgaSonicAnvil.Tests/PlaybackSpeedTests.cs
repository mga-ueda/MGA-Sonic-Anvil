using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlaybackSpeedTests
{
    [Fact]
    public void DefaultSpeed_IsUnity()
    {
        var provider = MakeProvider(300);
        Assert.Equal(1, provider.PlaybackSpeed);
        Assert.False(provider.SetPlaybackSpeed(1));
    }

    [Fact]
    public void SetPlaybackSpeed_SnapsToFastOrUnityOrRewind()
    {
        var provider = MakeProvider(300);
        Assert.True(provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed));
        Assert.Equal(PlaybackSampleProvider.FastSpeed, provider.PlaybackSpeed);
        Assert.False(provider.SetPlaybackSpeed(4));
        Assert.True(provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed));
        Assert.Equal(-PlaybackSampleProvider.FastSpeed, provider.PlaybackSpeed);
        Assert.False(provider.SetPlaybackSpeed(-4));
        Assert.True(provider.SetPlaybackSpeed(0));
        Assert.Equal(1, provider.PlaybackSpeed);
    }

    [Fact]
    public void FastSpeed_AdvancesCursorThreefold()
    {
        var provider = MakeProvider(300);
        provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(60, provider.CursorFrame);
    }

    [Fact]
    public void FastSpeed_KeepsOriginalPitchOnFirstGrain()
    {
        var rate = 48000;
        var samples = new float[rate * 2];
        for (var i = 0; i < rate; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, rate, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(rate);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

        var frames = 1000;
        var buffer = new float[frames * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        for (var i = 0; i < frames; i++)
        {
            Assert.Equal(samples[i * 2], buffer[i * 2], 3);
        }

        Assert.Equal(frames * 3, provider.CursorFrame);
    }

    [Fact]
    public void UnitySpeed_AdvancesCursorOneToOne()
    {
        var provider = MakeProvider(300);
        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(20, provider.CursorFrame);
    }

    [Fact]
    public void SetPlaybackSpeed_AppliesOnNextReadWithoutRebind()
    {
        var provider = MakeProvider(300);
        var first = new float[10 * 2];
        Assert.Equal(first.Length, provider.Read(first, 0, first.Length));
        Assert.Equal(10, provider.CursorFrame);

        Assert.True(provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed));
        var second = new float[10 * 2];
        Assert.Equal(second.Length, provider.Read(second, 0, second.Length));
        Assert.Equal(40, provider.CursorFrame);
    }

    [Fact]
    public void RewindSpeed_RetreatsCursorThreefold()
    {
        var provider = MakeProvider(300);
        provider.SeekFrame(200);
        provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(140, provider.CursorFrame);
        Assert.Equal(0.2f, buffer[0], 3);
        Assert.Equal(0.2f, buffer[2], 3);
    }

    [Fact]
    public void RewindSpeed_PlaysOriginalPitchBackward()
    {
        var samples = new float[80 * 2];
        for (var i = 0; i < 80; i++)
        {
            samples[i * 2] = i;
            samples[i * 2 + 1] = i;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 40, playRange: null, loop: false);
        provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed);

        var buffer = new float[8 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(40f, buffer[0], 3);
        Assert.Equal(39f, buffer[2], 3);
        Assert.Equal(38f, buffer[4], 3);
        Assert.Equal(16, provider.CursorFrame);
    }

    [Fact]
    public void RewindSpeed_StopsAtStartWithoutEnding()
    {
        var provider = MakeProvider(30);
        provider.SeekFrame(6);
        provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0, provider.CursorFrame);
        Assert.False(provider.Ended);
    }

    [Fact]
    public void FastSpeed_StillWorksWhenDeviceRateDiffers()
    {
        var samples = new float[300 * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.2f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(96000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        // 出力 20 フレーム × (48k/96k) × 3 = 30 ソースフレーム。
        Assert.Equal(30, provider.CursorFrame);
    }

    private static PlaybackSampleProvider MakeProvider(int frames)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.2f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        return provider;
    }
}
