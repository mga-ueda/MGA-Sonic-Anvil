using System.IO;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryMediaCacheTests : IDisposable
{
    private readonly string _dir;

    public LibraryMediaCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-library-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LibraryMediaCache.UseDirectoryForTests(_dir);
    }

    public void Dispose()
    {
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
    public void Waveform_RoundTrip()
    {
        var path = Path.Combine(_dir, "song.wav");
        File.WriteAllBytes(path, new byte[64]);
        var bars = new float[] { 0.1f, 0.5f, 0.9f, 0.2f };

        LibraryMediaCache.PutWaveform(path, bars.Length, bars);
        Assert.True(LibraryMediaCache.TryGetWaveform(path, bars.Length, out var loaded));
        Assert.Equal(bars, loaded);
    }

    [Fact]
    public void Waveform_MissesWhenFileChanges()
    {
        var path = Path.Combine(_dir, "song.wav");
        File.WriteAllBytes(path, new byte[64]);
        var bars = new float[] { 0.1f, 0.5f, 0.9f };
        LibraryMediaCache.PutWaveform(path, bars.Length, bars);

        File.WriteAllBytes(path, new byte[80]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));
        Assert.False(LibraryMediaCache.TryGetWaveform(path, bars.Length, out _));
    }

    [Fact]
    public void Artwork_RoundTrip()
    {
        var path = Path.Combine(_dir, "cover.mp3");
        File.WriteAllBytes(path, new byte[32]);
        var image = new byte[] { 1, 2, 3, 4, 5 };

        LibraryMediaCache.PutArtwork(path, image);
        Assert.True(LibraryMediaCache.TryGetArtwork(path, out var loaded));
        Assert.Equal(image, loaded);
    }

    [Fact]
    public void PlaylistWaveform_PersistsOnTrySet()
    {
        var path = Path.Combine(_dir, "persist.wav");
        File.WriteAllBytes(path, new byte[48]);
        var bars = new float[LibraryPlaylistWaveform.BarCount];
        for (var i = 0; i < bars.Length; i++)
        {
            bars[i] = i / (float)bars.Length;
        }

        Assert.True(LibraryPlaylistWaveform.TrySet(path, bars.Length, bars));
        // 非同期 Persist を待つ
        for (var i = 0; i < 50; i++)
        {
            if (LibraryMediaCache.TryGetWaveform(path, bars.Length, out _))
            {
                break;
            }

            Thread.Sleep(20);
        }

        Assert.True(LibraryMediaCache.TryGetWaveform(path, bars.Length, out var disk));
        Assert.Equal(bars, disk);
    }
}
