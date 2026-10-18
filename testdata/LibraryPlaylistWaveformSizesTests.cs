using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaylistWaveformSizesTests
{
    [Fact]
    public void BarCount_ScalesFromS()
    {
        Assert.Equal(PeakPyramid.PlaylistBarCount, LibraryPlaylistWaveformSizes.BaseBarCount);
        Assert.Equal(48, LibraryPlaylistWaveformSizes.BarCount(LibraryPlaylistWaveformSize.S));
        Assert.Equal(96, LibraryPlaylistWaveformSizes.BarCount(LibraryPlaylistWaveformSize.M));
        Assert.Equal(144, LibraryPlaylistWaveformSizes.BarCount(LibraryPlaylistWaveformSize.L));
    }

    [Fact]
    public void Parse_UnknownUsesFallback()
    {
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse(null));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse(""));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse("x"));
        Assert.Equal(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSizes.Parse("s"));
        Assert.Equal(LibraryPlaylistWaveformSize.M, LibraryPlaylistWaveformSizes.Parse("m"));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse("L"));
        Assert.Equal(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSizes.Parse(null, LibraryPlaylistWaveformSize.S));
        Assert.Equal(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSizes.Parse("", LibraryPlaylistWaveformSize.S));
        Assert.Equal(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSizes.Parse("x", LibraryPlaylistWaveformSize.S));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse("L", LibraryPlaylistWaveformSize.S));
    }

    [Fact]
    public void Resolve_UsesWaveAndMp3SizesByPlaylistKind()
    {
        var wave = new LibraryFileRow[] { new() { Name = "a.wav" } };
        var aiff = new LibraryFileRow[] { new() { Name = "a.aiff" } };
        var mp3 = new LibraryFileRow[] { new() { Name = "a.mp3" } };
        var mixed = new LibraryFileRow[]
        {
            new() { Name = "a.wav" },
            new() { Name = "b.mp3" },
        };

        Assert.Equal(
            LibraryPlaylistWaveformSize.L,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSize.S, wave));
        Assert.Equal(
            LibraryPlaylistWaveformSize.M,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.M, LibraryPlaylistWaveformSize.S, aiff));
        Assert.Equal(
            LibraryPlaylistWaveformSize.S,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSize.S, mp3));
        Assert.Equal(
            LibraryPlaylistWaveformSize.L,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSize.S, mixed));
        Assert.Equal(
            LibraryPlaylistWaveformSize.M,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSize.M, mixed));
        Assert.Equal(
            LibraryPlaylistWaveformSize.L,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSize.S, []));
    }

    [Fact]
    public void BuildPlaylistBars_HonorsBarCount()
    {
        var frames = 256;
        var samples = new float[frames];
        samples[frames / 2] = 1f;
        var peaks = PeakPyramid.BuildPlayerDisplay(samples, 1, samples.Length);
        var bars = PeakPyramid.BuildPlaylistBarsFromPeaks(peaks, 96);
        Assert.Equal(96, bars.Length);
        Assert.Contains(bars, static v => v >= 0.99f);
    }

    [Fact]
    public void BuildBars_SkipsInProgressPeaksSoTailStaysAudible()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-pl-wave-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            const int frames = 8000;
            using (var writer = new NAudio.Wave.WaveFileWriter(path, new NAudio.Wave.WaveFormat(8000, 16, 1)))
            {
                var samples = new float[frames];
                for (var i = 0; i < frames; i++)
                {
                    samples[i] = i >= frames / 2 ? 0.8f : 0.05f;
                }

                writer.WriteSamples(samples, 0, samples.Length);
            }

            const int buckets = 8;
            const int baseBucket = frames / buckets;
            var mins = new float[buckets];
            var maxs = new float[buckets];
            Array.Fill(mins, -0.05f);
            Array.Fill(maxs, 0.05f);
            for (var i = buckets / 2; i < buckets; i++)
            {
                mins[i] = 0;
                maxs[i] = 0;
            }

            var building = PeakPyramid.CreateMonoForTests(
                mins,
                maxs,
                frames,
                baseBucket,
                filledFrames: frames / 2);
            var fromBuilding = PeakPyramid.BuildPlaylistBarsFromPeaks(building, buckets);
            Assert.Equal(0f, fromBuilding[^1]);

            var bars = LibraryPlaylistWaveform.BuildBars(path, building, buckets, CancellationToken.None);
            Assert.True(bars[^1] > 0.9f, $"tail={bars[^1]}");
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void TrySet_DoesNotReplaceExistingBars()
    {
        var path = @"C:\playlist-wave-first-wins\" + Guid.NewGuid().ToString("N") + ".mp3";
        var barCount = LibraryPlaylistWaveform.BarCount;
        var first = new float[barCount];
        first[0] = 1f;
        var second = new float[barCount];
        second[^1] = 1f;

        Assert.True(LibraryPlaylistWaveform.TrySet(path, barCount, first));
        Assert.False(LibraryPlaylistWaveform.TrySet(path, barCount, second));
        Assert.True(LibraryPlaylistWaveform.TryGet(path, barCount, out var stored));
        Assert.Equal(1f, stored[0]);
        Assert.Equal(0f, stored[^1]);

        var peaks = PeakPyramid.BuildPlayerDisplay(second, 1, second.Length);
        LibraryPlaylistWaveform.SetFromCompletedPeaks(path, peaks);
        Assert.True(LibraryPlaylistWaveform.TryGet(path, barCount, out stored));
        Assert.Equal(1f, stored[0]);
    }
}
