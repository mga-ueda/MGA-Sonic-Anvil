using System.IO;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelLayoutTests
{
    [Theory]
    [InlineData(1, 0, "M")]
    [InlineData(2, 0, "L")]
    [InlineData(2, 1, "R")]
    [InlineData(6, 2, "C")]
    [InlineData(6, 3, "LFE")]
    [InlineData(6, 4, "Ls")]
    [InlineData(6, 5, "Rs")]
    [InlineData(10, 8, "Ch9")]
    public void Name_UsesStandardOrder(int channels, int index, string expected)
    {
        Assert.Equal(expected, ChannelLabels.Name(index, channels));
    }

    [Fact]
    public void Load_PreservesSixChannels()
    {
        var frames = 240;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            for (var ch = 0; ch < 6; ch++)
            {
                samples[i * 6 + ch] = (ch + 1) * 0.05f;
            }
        }

        var document = new AudioDocument(samples, 48000, 6, 16, AudioFileKind.Wave, null);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-6ch-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var loaded = AudioCodec.Load(path);
            Assert.Equal(6, loaded.Channels);
            Assert.Equal(frames, loaded.FrameCount);
            Assert.Equal(0.30f, loaded.Interleaved[5], 3);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Playback_DownmixesSurroundToStereo()
    {
        var frames = 32;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 6] = 0.4f;
            samples[i * 6 + 1] = -0.2f;
        }

        var document = new AudioDocument(samples, 48000, 6, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(2, provider.WaveFormat.Channels);

        var buffer = new float[16];
        var read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(16, read);
        Assert.True(buffer[0] > 0.3f);
        Assert.True(buffer[1] < -0.1f);
    }

    [Fact]
    public void Playback_MonoPlaysOnBothStereoChannels()
    {
        var samples = new float[] { 0.4f, 0.4f, 0.4f, 0.4f };
        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(2, provider.WaveFormat.Channels);

        var buffer = new float[8];
        Assert.Equal(8, provider.Read(buffer, 0, buffer.Length));
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(0.4f, buffer[i * 2], 3);
            Assert.Equal(0.4f, buffer[i * 2 + 1], 3);
        }
    }

    [Fact]
    public void Playback_MonoMirrorsToLeftAndRightOnManyPorts()
    {
        var samples = new float[] { 0.5f, 0.5f };
        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, null);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(8, provider.WaveFormat.Channels);

        var buffer = new float[16];
        Assert.Equal(16, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.5f, buffer[0], 3);
        Assert.Equal(0.5f, buffer[1], 3);
        Assert.Equal(0f, buffer[2], 3);
        Assert.Equal(0f, buffer[7], 3);
    }

    [Fact]
    public void Playback_MonoMirrorsToMappedStereoPorts()
    {
        // ASIO Fireface + 保存済み再生ポート [0,1] の構成。マップ有りでもモノラルは両ポートへ。
        var samples = new float[] { 0.5f, 0.5f };
        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(18, [0, 1]);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(18, provider.WaveFormat.Channels);

        var buffer = new float[36];
        Assert.Equal(36, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.5f, buffer[0], 3);
        Assert.Equal(0.5f, buffer[1], 3);
        for (var port = 2; port < 18; port++)
        {
            Assert.Equal(0f, buffer[port], 3);
        }
    }

    [Fact]
    public void Playback_MonoFollowsMapToNonDefaultPorts()
    {
        var samples = new float[] { 0.5f, 0.5f };
        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, [2, 3]);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[16];
        Assert.Equal(16, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0f, buffer[0], 3);
        Assert.Equal(0f, buffer[1], 3);
        Assert.Equal(0.5f, buffer[2], 3);
        Assert.Equal(0.5f, buffer[3], 3);
    }

    [Fact]
    public void Playback_HoldsSamplesOnMappedDirectRoute()
    {
        // 8kHz ソースを 48kHz デバイスへ。直接ルート（マップ有り）でも補間せずホールドする。
        var frames = 64;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = i % 2 == 0 ? 1f : -1f;
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, 8000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.ConfigureOutput(18, [0, 1]);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[18 * 96];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        for (var frame = 0; frame < 96; frame++)
        {
            var sample = buffer[frame * 18];
            Assert.True(sample is 1f or -1f, $"frame {frame}: {sample} は補間されている");
        }
    }

    [Fact]
    public void Playback_RoutesSurroundWhenDeviceHasManyPorts()
    {
        var frames = 8;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 6] = 0.4f;
            samples[i * 6 + 5] = -0.3f;
        }

        var document = new AudioDocument(samples, 48000, 6, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, null);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(8, provider.WaveFormat.Channels);

        var buffer = new float[16];
        Assert.Equal(16, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.4f, buffer[0], 3);
        Assert.Equal(0f, buffer[1], 3);
        Assert.Equal(-0.3f, buffer[5], 3);
        Assert.Equal(0f, buffer[6], 3);
    }

    [Fact]
    public void Playback_KeepsDeviceRateWhenDocumentDiffers()
    {
        var document = new AudioDocument(new float[8820], 44100, 2, 16, AudioFileKind.Wave, null);
        for (var i = 0; i < document.Interleaved.Length; i++)
        {
            document.Interleaved[i] = 0.25f;
        }

        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(48000, provider.WaveFormat.SampleRate);
        Assert.Equal(48000, provider.DeviceSampleRate);

        var buffer = new float[960];
        var read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(960, read);
        Assert.True(buffer.Take(16).All(sample => sample > 0.2f));
        Assert.True(provider.CursorFrame > 0);
        Assert.True(provider.CursorFrame < 44100);
    }

    [Fact]
    public void Playback_UsesCapturedDeviceRateNotHardcoded48k()
    {
        var document = new AudioDocument(new float[9600], 48000, 2, 16, AudioFileKind.Wave, null);
        for (var i = 0; i < document.Interleaved.Length; i++)
        {
            document.Interleaved[i] = 0.2f;
        }

        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(96000);
        provider.Bind(document, 0, null, loop: false);
        Assert.Equal(96000, provider.WaveFormat.SampleRate);

        var buffer = new float[192];
        Assert.Equal(192, provider.Read(buffer, 0, buffer.Length));
        Assert.True(provider.CursorFrame > 0);
        Assert.True(provider.CursorFrame < 200);
    }
}
