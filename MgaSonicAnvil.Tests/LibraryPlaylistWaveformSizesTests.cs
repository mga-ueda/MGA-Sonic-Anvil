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
    public void Parse_DefaultsToL()
    {
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse(null));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse(""));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse("x"));
        Assert.Equal(LibraryPlaylistWaveformSize.S, LibraryPlaylistWaveformSizes.Parse("s"));
        Assert.Equal(LibraryPlaylistWaveformSize.M, LibraryPlaylistWaveformSizes.Parse("m"));
        Assert.Equal(LibraryPlaylistWaveformSize.L, LibraryPlaylistWaveformSizes.Parse("L"));
    }

    [Fact]
    public void IsWaveOnly_RequiresAllWave()
    {
        Assert.False(LibraryPlaylistWaveformSizes.IsWaveOnly([]));
        Assert.True(LibraryPlaylistWaveformSizes.IsWaveOnly(
            [new LibraryFileRow { Name = "a.wav", Kind = "WAVE" }]));
        Assert.False(LibraryPlaylistWaveformSizes.IsWaveOnly(
            [
                new LibraryFileRow { Name = "a.wav", Kind = "WAVE" },
                new LibraryFileRow { Name = "b.mp3", Kind = "MP3" },
            ]));
        Assert.False(LibraryPlaylistWaveformSizes.IsWaveOnly(
            [new LibraryFileRow { Name = "a.aiff", Kind = "AIFF" }]));
    }

    [Fact]
    public void Resolve_AutoLargeUsesLForWaveOnly()
    {
        var wave = new LibraryFileRow[] { new() { Name = "a.wav", Kind = "WAVE" } };
        Assert.Equal(
            LibraryPlaylistWaveformSize.L,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.S, true, wave));
        Assert.Equal(
            LibraryPlaylistWaveformSize.S,
            LibraryPlaylistWaveformSizes.Resolve(LibraryPlaylistWaveformSize.S, false, wave));
        Assert.Equal(
            LibraryPlaylistWaveformSize.M,
            LibraryPlaylistWaveformSizes.Resolve(
                LibraryPlaylistWaveformSize.M,
                true,
                [new LibraryFileRow { Name = "a.mp3", Kind = "MP3" }]));
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
}
