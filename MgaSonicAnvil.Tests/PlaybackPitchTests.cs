using System.IO;
using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using NAudio.Wave;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlaybackPitchTests
{
    [Fact]
    public void PitchKeys_AreCtrlPlusAndMinusOnKeyboardAndNumpad()
    {
        Assert.True(LibraryPlayerMode.TryPlaybackPitchStep(Key.Add, ModifierKeys.Control, out var up));
        Assert.Equal(1, up);
        Assert.True(LibraryPlayerMode.TryPlaybackPitchStep(Key.OemPlus, ModifierKeys.Control, out up));
        Assert.Equal(1, up);
        Assert.True(LibraryPlayerMode.TryPlaybackPitchStep(
            Key.OemPlus,
            ModifierKeys.Control | ModifierKeys.Shift,
            out up));
        Assert.Equal(1, up);
        Assert.True(LibraryPlayerMode.TryPlaybackPitchStep(Key.Subtract, ModifierKeys.Control, out var down));
        Assert.Equal(-1, down);
        Assert.True(LibraryPlayerMode.TryPlaybackPitchStep(Key.OemMinus, ModifierKeys.Control, out down));
        Assert.Equal(-1, down);
        Assert.False(LibraryPlayerMode.TryPlaybackPitchStep(Key.Add, ModifierKeys.None, out _));
        Assert.False(LibraryPlayerMode.TryPlaybackPitchStep(Key.OemPlus, ModifierKeys.Shift, out _));
        Assert.False(LibraryPlayerMode.TryPlaybackPitchStep(Key.Add, ModifierKeys.Control | ModifierKeys.Alt, out _));
        Assert.True(LibraryPlayerMode.IsPlaybackPitchReset(Key.D0, ModifierKeys.Control));
        Assert.True(LibraryPlayerMode.IsPlaybackPitchReset(Key.NumPad0, ModifierKeys.Control));
        Assert.False(LibraryPlayerMode.IsPlaybackPitchReset(Key.D0, ModifierKeys.None));
        Assert.False(LibraryPlayerMode.IsPlaybackPitchReset(Key.D0, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.False(LibraryPlayerMode.IsPlaybackPitchReset(Key.D1, ModifierKeys.Control));
    }

    [Fact]
    public void SetPlaybackSemitones_SnapsAndKeepsUnitySamples()
    {
        var provider = MakeProvider(300);
        Assert.Equal(0, provider.PlaybackSemitones);
        Assert.False(provider.SetPlaybackSemitones(0));
        Assert.True(provider.SetPlaybackSemitones(100));
        Assert.Equal(PitchShift.MaxSemitones, provider.PlaybackSemitones);
        Assert.False(provider.SetPlaybackSemitones(PitchShift.MaxSemitones));
        Assert.True(provider.SetPlaybackSemitones(-100));
        Assert.Equal(PitchShift.MinSemitones, provider.PlaybackSemitones);

        var dry = MakeProvider(300);
        var pitched = MakeProvider(300);
        Assert.True(pitched.SetPlaybackSemitones(1));
        Assert.True(pitched.SetPlaybackSemitones(0));
        var left = new float[32];
        var right = new float[32];
        Assert.Equal(left.Length, dry.Read(left, 0, left.Length));
        Assert.Equal(right.Length, pitched.Read(right, 0, right.Length));
        Assert.Equal(left, right);
    }

    [Fact]
    public void RebindSameDocument_KeepsSemitones_OtherDocumentResets()
    {
        var provider = MakeProvider(300);
        var document = Bound(provider);
        Assert.True(provider.SetPlaybackSemitones(5));
        provider.Bind(document, 10, playRange: null, loop: false);
        Assert.Equal(5, provider.PlaybackSemitones);

        var other = new AudioDocument(new float[64], 48000, 2, 16, AudioFileKind.Wave, null);
        provider.Bind(other, 0, playRange: null, loop: false);
        Assert.Equal(0, provider.PlaybackSemitones);
    }

    [Fact]
    public void Seek_KeepsSemitones()
    {
        var provider = MakeProvider(300);
        Assert.True(provider.SetPlaybackSemitones(3));
        provider.SeekFrame(40);
        Assert.Equal(3, provider.PlaybackSemitones);
    }

    [Fact]
    public void GaplessAdvance_ResetsSemitones()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-pitch-gapless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var firstPath = Path.Combine(dir, "a.wav");
        var secondPath = Path.Combine(dir, "b.wav");
        try
        {
            const int rate = 48000;
            WriteDc(firstPath, rate, 64, 0);
            WriteDc(secondPath, rate, 64, 16000);
            var firstDoc = StreamDocument(firstPath, rate, 64);
            var secondDoc = StreamDocument(secondPath, rate, 64);
            var provider = new PlaybackSampleProvider();
            provider.SetDeviceSampleRate(rate);
            provider.BindStream(AudioStreamSource.Open(firstPath), firstDoc, 0, null, loop: false);
            Assert.True(provider.SetPlaybackSemitones(4));
            Assert.True(provider.TryArmGapless(AudioStreamSource.Open(secondPath), secondDoc));

            var buffer = new float[(64 + 24) * 2];
            Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
            Assert.True(provider.TryTakeGaplessAdvance(out var advanced));
            Assert.Same(secondDoc, advanced);
            Assert.Equal(0, provider.PlaybackSemitones);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void PlusTwelveSemitones_RaisesTheToneBeingPlayed()
    {
        const int rate = 48000;
        const int frames = rate;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440 * i / rate);
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, rate, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(rate);
        provider.Bind(document, 0, playRange: null, loop: false);
        Assert.True(provider.SetPlaybackSemitones(12));

        var buffer = new float[frames * 2];
        var filled = 0;
        while (filled < buffer.Length)
        {
            var n = provider.Read(buffer, filled, Math.Min(512, buffer.Length - filled));
            Assert.True(n > 0);
            filled += n;
        }

        var dry = ZeroCrossings(samples, frames / 2, frames / 4);
        var pitched = ZeroCrossings(buffer, frames / 2, frames / 4);
        Assert.InRange(pitched / (double)dry, 1.7, 2.3);
        Assert.Equal(frames, provider.CursorFrame);
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

    private static AudioDocument Bound(PlaybackSampleProvider provider)
    {
        var samples = new float[300 * 2];
        Array.Fill(samples, 0.2f);
        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        provider.Bind(document, 0, playRange: null, loop: false);
        return document;
    }

    private static int ZeroCrossings(float[] interleaved, int startFrame, int frames)
    {
        var count = 0;
        var previous = interleaved[startFrame * 2];
        for (var i = 1; i < frames; i++)
        {
            var value = interleaved[(startFrame + i) * 2];
            if ((previous <= 0 && value > 0) || (previous >= 0 && value < 0))
            {
                count++;
            }

            previous = value;
        }

        return count;
    }

    private static AudioDocument StreamDocument(string path, int rate, int frames)
    {
        var document = new AudioDocument([], rate, 2, 16, AudioFileKind.Wave, path, buildPeaks: false);
        document.ActivateStreamPlayback(rate, 2, 16, frames);
        return document;
    }

    private static void WriteDc(string path, int sampleRate, int frames, short sample)
    {
        var format = new WaveFormat(sampleRate, 16, 2);
        using var writer = new WaveFileWriter(path, format);
        var buffer = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames; i++)
        {
            var at = i * 4;
            buffer[at] = (byte)sample;
            buffer[at + 1] = (byte)(sample >> 8);
            buffer[at + 2] = (byte)sample;
            buffer[at + 3] = (byte)(sample >> 8);
        }

        writer.Write(buffer, 0, buffer.Length);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }
}
