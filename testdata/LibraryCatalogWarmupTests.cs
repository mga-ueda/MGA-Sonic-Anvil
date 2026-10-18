using System.IO;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryCatalogWarmupTests : IDisposable
{
    private readonly string _dir;

    public LibraryCatalogWarmupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-library-warmup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LibraryCatalog.UseDirectoryForTests(_dir);
        LibraryMediaCache.UseDirectoryForTests(_dir);
    }

    public void Dispose()
    {
        LibraryCatalog.ResetForTests();
        LibraryMediaCache.ResetForTests();
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Run_CachesTagsAndWaveformBars()
    {
        var root = Path.Combine(_dir, "music");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "tone.wav");
        WriteToneWav(path, sampleRate: 8000, frames: 8000);

        var last = -1d;
        var progress = new Progress<double>(value => last = value);
        LibraryCatalogWarmup.Run([root], progress);

        Assert.True(LibraryCatalog.TryGetFreshTags(path, out var tags));
        Assert.True(tags.Probed);
        Assert.True(tags.SampleRate > 0 || tags.DurationSeconds >= 0);

        var barCount = LibraryPlaylistWaveformSizes.BarCount(LibraryPlaylistWaveformSize.L);
        Assert.True(LibraryMediaCache.TryGetWaveform(path, barCount, out var bars));
        Assert.Equal(barCount, bars.Length);
        Assert.Equal(1d, last);
    }

    private static void WriteToneWav(string path, int sampleRate, int frames)
    {
        // 16-bit mono PCM WAV
        var dataBytes = frames * 2;
        var bytes = new byte[44 + dataBytes];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        BitConverter.TryWriteBytes(bytes.AsSpan(4), 36 + dataBytes);
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'A';
        bytes[10] = (byte)'V';
        bytes[11] = (byte)'E';
        bytes[12] = (byte)'f';
        bytes[13] = (byte)'m';
        bytes[14] = (byte)'t';
        bytes[15] = (byte)' ';
        BitConverter.TryWriteBytes(bytes.AsSpan(16), 16);
        BitConverter.TryWriteBytes(bytes.AsSpan(20), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(22), (short)1);
        BitConverter.TryWriteBytes(bytes.AsSpan(24), sampleRate);
        BitConverter.TryWriteBytes(bytes.AsSpan(28), sampleRate * 2);
        BitConverter.TryWriteBytes(bytes.AsSpan(32), (short)2);
        BitConverter.TryWriteBytes(bytes.AsSpan(34), (short)16);
        bytes[36] = (byte)'d';
        bytes[37] = (byte)'a';
        bytes[38] = (byte)'t';
        bytes[39] = (byte)'a';
        BitConverter.TryWriteBytes(bytes.AsSpan(40), dataBytes);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)(Math.Sin(i * 0.1) * 10000);
            BitConverter.TryWriteBytes(bytes.AsSpan(44 + (i * 2)), sample);
        }

        File.WriteAllBytes(path, bytes);
    }
}
