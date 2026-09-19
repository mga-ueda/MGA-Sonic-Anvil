using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlaybackSeekCrossfadeTests
{
    [Fact]
    public void SeekFrameCrossfade_OverlapsOldAndNew()
    {
        var rate = 48000;
        var fadeMs = 1;
        var fadeFrames = PlaybackSampleProvider.SeekCrossfadeFrames(rate, fadeMs);
        var frames = 400;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = i < 150 ? 0.9f : 0.2f;
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, rate, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(rate);
        provider.Bind(document, 0, playRange: null, loop: false);

        var prime = new float[10 * 2];
        Assert.Equal(prime.Length, provider.Read(prime, 0, prime.Length));
        Assert.Equal(10, provider.CursorFrame);

        provider.SeekFrameCrossfade(200, fadeMs);
        Assert.Equal(200, provider.PendingSeekFrame);
        Assert.Equal(200, provider.CursorFrame);

        var mixed = new float[fadeFrames * 2];
        Assert.Equal(mixed.Length, provider.Read(mixed, 0, mixed.Length));
        Assert.True(Math.Abs(mixed[0]) > 0.8);
        Assert.True(Math.Abs(mixed[^2]) < 0.35);
        Assert.True(Math.Abs(mixed[0]) > Math.Abs(mixed[^2]));
        Assert.Null(provider.PendingSeekFrame);
        Assert.Equal(200 + fadeFrames, provider.CursorFrame);
    }

    [Fact]
    public void SeekNudgeCrossfade_Is750Milliseconds()
    {
        Assert.Equal(750, LibraryPlayerMode.SeekNudgeFadeMilliseconds);
        Assert.Equal(36000, PlaybackSampleProvider.SeekCrossfadeFrames(48000, 750));
    }

    [Fact]
    public void SeekFrameCrossfade_MashDuringOverlap_RetargetsIncoming()
    {
        var rate = 48000;
        var fadeMs = LibraryPlayerMode.SeekNudgeFadeMilliseconds;
        var fadeFrames = PlaybackSampleProvider.SeekCrossfadeFrames(rate, fadeMs);
        var frames = fadeFrames * 8;
        var samples = new float[frames * 2];
        Array.Fill(samples, 0.8f);
        var document = new AudioDocument(samples, rate, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(rate);
        provider.Bind(document, 0, playRange: null, loop: false);

        var prime = new float[10 * 2];
        Assert.Equal(prime.Length, provider.Read(prime, 0, prime.Length));
        Assert.Equal(10, provider.CursorFrame);

        provider.SeekFrameCrossfade(200, fadeMs);
        Assert.Equal(200, provider.PendingSeekFrame);
        Assert.Equal(200, provider.CursorFrame);
        provider.SeekFrameCrossfade(400, fadeMs);
        Assert.Equal(400, provider.PendingSeekFrame);
        Assert.Equal(400, provider.CursorFrame);

        var mixed = new float[fadeFrames * 2];
        Assert.Equal(mixed.Length, provider.Read(mixed, 0, mixed.Length));
        Assert.True(Math.Abs(mixed[0]) > 0.5);
        Assert.True(Math.Abs(mixed[^2]) > 0.5);
        Assert.Null(provider.PendingSeekFrame);
        Assert.Equal(400 + fadeFrames, provider.CursorFrame);
    }

    [Fact]
    public void SeekFrameCrossfade_WhenPaused_JumpsImmediately()
    {
        var provider = MakeProvider(300);
        provider.SetPaused(true);
        provider.SeekFrameCrossfade(80, LibraryPlayerMode.SeekNudgeFadeMilliseconds);
        Assert.Equal(80, provider.CursorFrame);

        var buffer = new float[8];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, sample => Assert.Equal(0, sample));
        Assert.Equal(80, provider.CursorFrame);
    }

    [Fact]
    public void SeekFrameCrossfade_PcmRateMismatch_KeepsOutgoingPitch()
    {
        var srcRate = 44100;
        var deviceRate = 48000;
        var hz = 440d;
        var frames = srcRate * 3;
        var samples = Tone(frames, srcRate, hz);
        var document = new AudioDocument(samples, srcRate, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(deviceRate);
        provider.Bind(document, 0, playRange: null, loop: false);

        var prime = new float[deviceRate / 10 * 2];
        Assert.Equal(prime.Length, provider.Read(prime, 0, prime.Length));
        provider.SeekFrameCrossfade(srcRate, fadeMilliseconds: 80);

        var mixed = new float[deviceRate / 25 * 2];
        Assert.Equal(mixed.Length, provider.Read(mixed, 0, mixed.Length));
        var measured = EstimateRisingZeroHz(mixed, channels: 2, deviceRate);
        Assert.InRange(measured, hz - 25, hz + 25);
        Assert.True(measured < hz * deviceRate / srcRate - 15);
    }

    [Fact]
    public void SeekFrameCrossfade_StreamRateMismatch_KeepsOutgoingPitch()
    {
        var srcRate = 44100;
        var deviceRate = 48000;
        var hz = 440d;
        var path = Path.Combine(Path.GetTempPath(), "mga-seek-pitch-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteToneWave(path, srcRate, srcRate * 3, hz);
            var document = AudioDocument.CreateDeferred(path);
            Assert.True(AudioCodec.TryActivateStreamPlayback(document));
            var source = AudioStreamSource.Open(path, prebufferTimeoutMs: 1000);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(deviceRate);
            provider.BindStream(source, document, startFrame: 0, playRange: null, loop: false);
            provider.SetSilentSkip(false, -60);

            var prime = new float[deviceRate / 10 * 2];
            Assert.True(provider.Read(prime, 0, prime.Length) > 0);
            provider.SeekFrameCrossfade(srcRate, fadeMilliseconds: 80);

            var mixed = new float[deviceRate / 25 * 2];
            Assert.True(provider.Read(mixed, 0, mixed.Length) >= mixed.Length / 2);
            var measured = EstimateRisingZeroHz(mixed, channels: 2, deviceRate);
            Assert.InRange(measured, hz - 35, hz + 35);
            Assert.True(measured < hz * deviceRate / srcRate - 15);

            provider.Bind(new AudioDocument([0f, 0f], 48000, 1, 16, AudioFileKind.Wave, null), 0, null, false);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static PlaybackSampleProvider MakeProvider(int frames)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, 0.2f);
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        return provider;
    }

    private static float[] Tone(int frames, int sampleRate, double frequency)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * frequency * i / sampleRate);
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return samples;
    }

    private static void WriteToneWave(string path, int sampleRate, int frames, double frequency)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * i / sampleRate) * 0.5 * short.MaxValue);
            var at = i * 4;
            buffer[at] = (byte)sample;
            buffer[at + 1] = (byte)(sample >> 8);
            buffer[at + 2] = (byte)sample;
            buffer[at + 3] = (byte)(sample >> 8);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static double EstimateRisingZeroHz(float[] interleaved, int channels, int sampleRate)
    {
        var frames = interleaved.Length / channels;
        var crossings = 0;
        var prev = interleaved[0];
        for (var i = 1; i < frames; i++)
        {
            var sample = interleaved[i * channels];
            if (prev <= 0 && sample > 0)
            {
                crossings++;
            }

            prev = sample;
        }

        return crossings * sampleRate / (double)Math.Max(1, frames - 1);
    }
}
