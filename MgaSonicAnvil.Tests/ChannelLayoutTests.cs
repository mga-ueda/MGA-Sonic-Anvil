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
}
