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
            Assert.Equal(44100, tags.SampleRate);
            Assert.Equal(2, tags.Channels);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
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
        double? taggedDurationSeconds = null)
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
            DurationSeconds = taggedDurationSeconds ?? (1000 / 48000d),
        });
        return document;
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
