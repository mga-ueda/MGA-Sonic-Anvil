using MgaSonicAnvil.Audio;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DeviceClockTests
{
    [Fact]
    public void QueryCurrentSampleRate_AsioDoesNotOpenADriver()
    {
        var rate = AudioOutputFactory.QueryCurrentSampleRate(
            new AudioOutputSettings(AudioOutputApi.Asio, "NoSuchDriver"));
        Assert.Equal(0, rate);
    }

    [Fact]
    public void QueryCurrentSampleRate_SharedOutputUsesMixFormatWhenAvailable()
    {
        var rate = AudioOutputFactory.QueryCurrentSampleRate(AudioOutputSettings.Default);
        if (rate == 0)
        {
            return;
        }

        Assert.InRange(rate, 1000, 384000);
    }

    [Fact]
    public void ReadLiveSampleRate_WaveOutHasNoAsioClock()
    {
        using var output = new WaveOutEvent();
        Assert.Equal(0, AudioOutputFactory.ReadLiveSampleRate(output));
    }

    [Fact]
    public void Playback_KeepsArbitraryCapturedDeviceClock()
    {
        var document = new AudioDocument(new float[8820], 44100, 2, 16, AudioFileKind.Wave, null);
        for (var i = 0; i < document.Interleaved.Length; i++)
        {
            document.Interleaved[i] = 0.25f;
        }

        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(88200);
        provider.Bind(document, 0, null, loop: false);

        Assert.Equal(88200, provider.WaveFormat.SampleRate);
        Assert.Equal(88200, provider.DeviceSampleRate);

        var buffer = new float[882];
        Assert.Equal(882, provider.Read(buffer, 0, buffer.Length));
        Assert.True(provider.CursorFrame > 0);
        Assert.True(provider.CursorFrame < 441);
    }
}
