using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ChannelLayoutTests
{
    [Fact]
    public void Labels_FitThreeCharacterSlot()
    {
        Assert.Equal(3, ChannelLabels.MaxChars);
        Assert.Equal(ChannelLabels.MaxChars, ChannelLabels.LongestCharCount());
    }

    [Theory]
    [InlineData(1, 0, "1")]
    [InlineData(2, 0, "1")]
    [InlineData(2, 1, "2")]
    [InlineData(6, 2, "3")]
    [InlineData(6, 3, "4")]
    [InlineData(6, 4, "5")]
    [InlineData(6, 5, "6")]
    [InlineData(10, 8, "9")]
    public void Name_UsesOrdinalNumbers(int channels, int index, string expected)
    {
        Assert.Equal(expected, ChannelLabels.Name(index, channels));
    }

    [Fact]
    public void Parse_FiveOne_KeepsSpeakerNames()
    {
        var layout = ChannelLayout.Parse("5.1");
        Assert.Equal(["L", "R", "C", "LFE", "Ls", "Rs"], layout.Labels);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], ChannelLayout.Guess(6).Labels);
    }

    [Fact]
    public void ForFile_UsesIdentityMapAsStartingNames()
    {
        var named = ChannelLayout.ForFile(6, ChannelLayout.Parse("5.1"));
        Assert.Equal(["L", "R", "C", "LFE", "Ls", "Rs"], named.Labels);
        Assert.Equal("C", named.LabelAt(2));
        Assert.Equal(
            ["L", "R", "C", "LFE", "Ls", "Rs", "7", "8"],
            ChannelLayout.ForFile(8, ChannelLayout.Parse("5.1")).Labels);
        Assert.Equal(["L", "R"], ChannelLayout.ForFile(2, ChannelLayout.Parse("5.1")).Labels);
    }

    [Fact]
    public void ForFile_NamesOnlyMappedLanes()
    {
        var swapped = ChannelLayout.ForFile(6, ChannelLayout.Parse("5.1"), [2, 0, 1, 3, 4, 5]);
        Assert.Equal(["R", "C", "L", "LFE", "Ls", "Rs"], swapped.Labels);
        var wide = ChannelLayout.ForFile(8, ChannelLayout.Parse("5.1"), [2, ChannelRouter.Off, 0, 3, 4, 5]);
        Assert.Equal(["C", "2", "L", "LFE", "Ls", "Rs", "7", "8"], wide.Labels);
        var unused = ChannelLayout.ForFile(
            6,
            ChannelLayout.Parse("5.1"),
            [ChannelRouter.Off, ChannelRouter.Off, ChannelRouter.Off, ChannelRouter.Off, ChannelRouter.Off, ChannelRouter.Off]);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], unused.Labels);
    }

    [Fact]
    public void MenuLabel_IsEnglishWithChannelCount()
    {
        Assert.Equal("Mono", ChannelLayout.Mono.MenuLabel);
        Assert.Equal("Stereo", ChannelLayout.Stereo.MenuLabel);
        Assert.Equal("5.1 ch", ChannelLayout.Parse("5.1").MenuLabel);
        Assert.Equal("Quad Back 4ch", ChannelLayout.Parse("Quad-Back").MenuLabel);
        Assert.Equal("5.1.2 Side ch", ChannelLayout.Parse("5.1.2-Side").MenuLabel);
        Assert.Equal("9.1.6 ch", ChannelLayout.Parse("9.1.6").MenuLabel);
    }

    [Fact]
    public void SaveWave_TwentyFourBitWritesUnspecifiedMask()
    {
        var document = new AudioDocument(new float[48], 48000, 6, 24, AudioFileKind.Wave, null);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-mask0-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            Assert.Equal(0, ReadChannelMask(path));
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
    public void SaveWave_PreservesExistingChannelMask()
    {
        var document = new AudioDocument(new float[48], 48000, 6, 24, AudioFileKind.Wave, null);
        document.SetChannelMask(0x3F);
        Assert.Equal(24, document.BitsPerSample);
        Assert.Equal(0x3F, document.ChannelMask);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-mask-keep-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            Assert.Equal(0x3F, ReadChannelMask(path));
            var loaded = AudioCodec.Load(path);
            Assert.Equal(0x3F, loaded.ChannelMask);
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
    public void Playback_ReconstructsOnMappedDirectRoute()
    {
        // ASIO 多ポート + マップ有り（直接ルート）でも、8kHz→48kHz は帯域制限補間で再構成する。
        // ホールドだと折り返しイメージが乗り、1 kHz まで下げてもスペアナが 22 kHz まで振れてしまう。
        var frames = 400;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * i / 8);
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, 8000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.ConfigureOutput(18, [0, 1]);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[18 * 960];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        var port0 = Enumerable.Range(0, 960).Select(i => buffer[i * 18]).ToArray();
        Assert.True(port0.Max(Math.Abs) > 0.5f, "信号が出ていない");
        for (var i = 9; i < port0.Length; i++)
        {
            var jump = Math.Abs(port0[i] - port0[i - 1]);
            Assert.True(jump < 0.25f, $"frame {i}: 隣接差 {jump} が大きい（階段＝折り返しイメージ）");
        }

        // マップ外のポートは無音のまま。
        Assert.Equal(0f, buffer[2], 3);
        Assert.Equal(0f, buffer[17], 3);
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
    public void Playback_ReadsFileLanesAssignedToSpeakers()
    {
        var frames = 4;
        var samples = new float[frames * 6];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 6] = 0.4f;
            samples[i * 6 + 2] = -0.3f;
        }

        var document = new AudioDocument(samples, 48000, 6, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.ConfigureOutput(8, [0, 1, 2, 3, 4, 5], [2, 1, 0, 3, 4, 5], speakerChannels: 6);
        provider.Bind(document, 0, null, loop: false);

        var buffer = new float[16];
        Assert.Equal(16, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(-0.3f, buffer[0], 3);
        Assert.Equal(0f, buffer[1], 3);
        Assert.Equal(0.4f, buffer[2], 3);
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

    private static int ReadChannelMask(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                Assert.True(size >= 40, $"fmt size {size}");
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                return reader.ReadInt32();
            }

            stream.Position += size + (size & 1);
        }

        throw new InvalidDataException("fmt chunk missing");
    }
}
