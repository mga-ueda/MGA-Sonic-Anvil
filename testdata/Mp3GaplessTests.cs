using System.IO;
using System.Text;
using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Mp3GaplessTests
{
    [Fact]
    public void Settings_DefaultOnAndRoundTrips()
    {
        Assert.True(new AppSettings().GaplessPlayback);
        Assert.True(AppSettings.CreateDefault().GaplessPlayback);
        var json = JsonSerializer.Serialize(
            new AppSettings { GaplessPlayback = false },
            AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.False(back!.GaplessPlayback);
        var missing = JsonSerializer.Deserialize(
            """{"SettingsGeneration":1}""",
            AppSettingsJsonContext.Default.AppSettings);
        Assert.True(missing!.GaplessPlayback);
    }

    [Fact]
    public void TryRead_XingLame_FillsDelayAndPadding()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-lame-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(path, BuildXingLame(delay: 576, padding: 288, frames: 100));
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(576, tags.EncoderDelayFrames);
            Assert.Equal(288, tags.EncoderPaddingFrames);
            Assert.Equal(100 * 1152L - 576 - 288, tags.EncoderOriginalFrames);
            Assert.Equal(44100, tags.SampleRate);
            Assert.Equal(2, tags.Channels);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TryRead_ItunesSmpbComment_FillsDelayAndDoesNotPolluteComment()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-smpb-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(
                path,
                Concat(
                    BuildId3(
                        CommentFrame("iTunSMPB", " 00000000 00000210 00000400 0000000000010000"),
                        CommentFrame("", "Hello")),
                    MpegStub));
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(0x210, tags.EncoderDelayFrames);
            Assert.Equal(0x400, tags.EncoderPaddingFrames);
            Assert.Equal(0x10000, tags.EncoderOriginalFrames);
            Assert.Equal("Hello", tags.Comment);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TryRead_ItunesSmpbTxxx_FillsDelay()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-smpb-txxx-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(path, Concat(BuildId3(TxxxFrame("iTunSMPB", "00000000 00000210 00000080 0000000000001000")), MpegStub));
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(0x210, tags.EncoderDelayFrames);
            Assert.Equal(0x80, tags.EncoderPaddingFrames);
            Assert.Equal(0x1000, tags.EncoderOriginalFrames);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TryRead_LameWinsOverItunesSmpb()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-lame-smpb-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(
                path,
                Concat(
                    BuildId3(CommentFrame("iTunSMPB", " 00000000 00000210 00000400 0000000000010000")),
                    BuildXingLame(delay: 576, padding: 288, frames: 100)));
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(576, tags.EncoderDelayFrames);
            Assert.Equal(288, tags.EncoderPaddingFrames);
            Assert.Equal(100 * 1152L - 576 - 288, tags.EncoderOriginalFrames);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Parse_ItunesSmpbHex()
    {
        Assert.True(ItunesGapless.TryParse(
            " 00000000 00000210 00000AD4 00000000000A2C0C 00000000 00000000",
            out var delay,
            out var padding,
            out var original));
        Assert.Equal(0x210, delay);
        Assert.Equal(0xAD4, padding);
        Assert.Equal(0xA2C0C, original);
        Assert.False(ItunesGapless.TryParse("hello", out _, out _, out _));
    }

    [Fact]
    public void ResolveWindow_SkipsDelayAndPaddingWhenEnabled()
    {
        var previous = Mp3Gapless.Enabled;
        try
        {
            Mp3Gapless.Enabled = true;
            var document = ToneDocument(1000, delay: 50, padding: 30);
            var window = Mp3Gapless.ResolveWindow(document, 1000, playRange: null);
            Assert.Equal(new WaveSelection(50, 970), window);

            Mp3Gapless.Enabled = false;
            Assert.Null(Mp3Gapless.ResolveWindow(document, 1000, playRange: null));
        }
        finally
        {
            Mp3Gapless.Enabled = previous;
        }
    }

    [Fact]
    public void ResolveWindow_DoesNotDoubleSkipWhenDecoderAlreadyStripped()
    {
        var previous = Mp3Gapless.Enabled;
        try
        {
            Mp3Gapless.Enabled = true;
            var document = ToneDocument(
                1000 - 50 - 30,
                delay: 50,
                padding: 30,
                taggedDurationSeconds: 1000 / 48000d);
            Assert.Null(Mp3Gapless.ResolveWindow(document, document.FrameCount, playRange: null));
        }
        finally
        {
            Mp3Gapless.Enabled = previous;
        }
    }

    [Fact]
    public void ResolveWindow_DoesNotTrimWhenOriginalMatchesDecoded()
    {
        var previous = Mp3Gapless.Enabled;
        try
        {
            Mp3Gapless.Enabled = true;
            var document = ToneDocument(
                920,
                delay: 50,
                padding: 30,
                taggedDurationSeconds: 920 / 48000d,
                original: 920);
            Assert.Null(Mp3Gapless.ResolveWindow(document, document.FrameCount, playRange: null));
        }
        finally
        {
            Mp3Gapless.Enabled = previous;
        }
    }

    [Fact]
    public void ResolveWindow_TrimsWhenDecodedStillIncludesDelay()
    {
        var previous = Mp3Gapless.Enabled;
        try
        {
            Mp3Gapless.Enabled = true;
            var document = ToneDocument(
                1000,
                delay: 50,
                padding: 30,
                taggedDurationSeconds: 920 / 48000d,
                original: 920);
            Assert.Equal(new WaveSelection(50, 970), Mp3Gapless.ResolveWindow(document, 1000, playRange: null));
        }
        finally
        {
            Mp3Gapless.Enabled = previous;
        }
    }

    [Fact]
    public void Bind_StartsAfterEncoderDelay()
    {
        var previous = Mp3Gapless.Enabled;
        try
        {
            Mp3Gapless.Enabled = true;
            const int delay = 40;
            const int padding = 20;
            const int frames = 200;
            var samples = new float[frames * 2];
            for (var i = delay; i < frames - padding; i++)
            {
                samples[i * 2] = 0.5f;
                samples[(i * 2) + 1] = 0.5f;
            }

            var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Mp3, null, buildPeaks: false);
            document.ApplyTags(new AudioFileTags
            {
                Probed = true,
                SampleRate = 48000,
                Channels = 2,
                EncoderDelayFrames = delay,
                EncoderPaddingFrames = padding,
                DurationSeconds = frames / 48000d,
            });

            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(48000);
            provider.SetSilentSkip(false, -60);
            provider.Bind(document, startFrame: 0, playRange: null, loop: false);
            var buffer = new float[8];
            Assert.Equal(8, provider.Read(buffer, 0, buffer.Length));
            Assert.True(buffer[0] > 0.4f);
            Assert.True(buffer[1] > 0.4f);
        }
        finally
        {
            Mp3Gapless.Enabled = previous;
        }
    }

    private static AudioDocument ToneDocument(
        int frames,
        int delay,
        int padding,
        double? taggedDurationSeconds = null,
        long original = 0)
    {
        var samples = new float[Math.Max(1, frames) * 2];
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Mp3, null, buildPeaks: false);
        document.ApplyTags(new AudioFileTags
        {
            Probed = true,
            SampleRate = 48000,
            Channels = 2,
            EncoderDelayFrames = delay,
            EncoderPaddingFrames = padding,
            EncoderOriginalFrames = original,
            DurationSeconds = taggedDurationSeconds ?? (1000 / 48000d),
        });
        return document;
    }

    private static readonly byte[] MpegStub = [0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00];

    private static byte[] BuildId3(params byte[][] frames)
    {
        var body = Concat(frames);
        var tag = new byte[10 + body.Length];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 3;
        var size = body.Length;
        tag[6] = (byte)((size >> 21) & 0x7F);
        tag[7] = (byte)((size >> 14) & 0x7F);
        tag[8] = (byte)((size >> 7) & 0x7F);
        tag[9] = (byte)(size & 0x7F);
        body.CopyTo(tag, 10);
        return tag;
    }

    private static byte[] CommentFrame(string description, string text)
    {
        var desc = Encoding.UTF8.GetBytes(description);
        var payload = Encoding.UTF8.GetBytes(text);
        var data = new byte[1 + 3 + desc.Length + 1 + payload.Length];
        data[0] = 3;
        data[1] = (byte)'e';
        data[2] = (byte)'n';
        data[3] = (byte)'g';
        desc.CopyTo(data, 4);
        payload.CopyTo(data, 5 + desc.Length);
        return Id3Frame("COMM", data);
    }

    private static byte[] TxxxFrame(string description, string text)
    {
        var desc = Encoding.UTF8.GetBytes(description);
        var payload = Encoding.UTF8.GetBytes(text);
        var data = new byte[1 + desc.Length + 1 + payload.Length];
        data[0] = 3;
        desc.CopyTo(data, 1);
        payload.CopyTo(data, 2 + desc.Length);
        return Id3Frame("TXXX", data);
    }

    private static byte[] Id3Frame(string id, byte[] data)
    {
        var frame = new byte[10 + data.Length];
        Encoding.ASCII.GetBytes(id).CopyTo(frame, 0);
        frame[4] = (byte)(data.Length >> 24);
        frame[5] = (byte)(data.Length >> 16);
        frame[6] = (byte)(data.Length >> 8);
        frame[7] = (byte)data.Length;
        data.CopyTo(frame, 10);
        return frame;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var all = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(all, offset);
            offset += part.Length;
        }

        return all;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static byte[] BuildXingLame(int delay, int padding, int frames)
    {
        var frame = new byte[417];
        frame[0] = 0xFF;
        frame[1] = 0xFB;
        frame[2] = 0x90;
        frame[3] = 0x00;
        var o = 4 + 32;
        Encoding.ASCII.GetBytes("Xing").CopyTo(frame, o);
        o += 4;
        WriteBe32(frame, o, 3);
        o += 4;
        WriteBe32(frame, o, frames);
        o += 4;
        WriteBe32(frame, o, 417 * frames);
        o += 4;
        Encoding.ASCII.GetBytes("LAME3.100").CopyTo(frame, o);
        frame[o + 21] = (byte)((delay >> 4) & 0xFF);
        frame[o + 22] = (byte)(((delay & 0x0F) << 4) | ((padding >> 8) & 0x0F));
        frame[o + 23] = (byte)(padding & 0xFF);
        return frame;
    }

    private static void WriteBe32(byte[] dest, int offset, int value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }
}
