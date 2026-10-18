using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaylistCacheTests : IDisposable
{
    private readonly string _dir;
    private readonly string[]? _prevWave;
    private readonly string[]? _prevMp3;
    private readonly string[]? _prevVideo;
    private readonly string[]? _prevMixed;

    public LibraryPlaylistCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-playlist-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LibraryCatalog.UseDirectoryForTests(_dir);
        LibraryMediaCache.UseDirectoryForTests(_dir);
        _prevWave = AppStorage.Settings.LibraryListColumnsWave;
        _prevMp3 = AppStorage.Settings.LibraryListColumnsMp3;
        _prevVideo = AppStorage.Settings.LibraryListColumnsVideo;
        _prevMixed = AppStorage.Settings.LibraryListColumnsMixed;
    }

    public void Dispose()
    {
        AppStorage.Settings.LibraryListColumnsWave = _prevWave ?? [];
        AppStorage.Settings.LibraryListColumnsMp3 = _prevMp3 ?? [];
        AppStorage.Settings.LibraryListColumnsVideo = _prevVideo ?? [];
        AppStorage.Settings.LibraryListColumnsMixed = _prevMixed ?? [];
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
    public void IsWarm_Mp3Defaults_NeedsTagsOnly()
    {
        AppStorage.Settings.ApplyLibraryListColumnsMp3(LibraryColumnFilter.Mp3Defaults);
        var path = Path.Combine(_dir, "song.mp3");
        File.WriteAllBytes(path, MinimalWav());
        Assert.False(LibraryPlaylistCache.RequiresWaveformForPath(path));
        Assert.False(LibraryPlaylistCache.RequiresJacketForPath(path));

        LibraryCatalog.UpsertTags(path, new AudioFileTags { Probed = true, Title = "A", HasArtwork = true });
        Assert.True(LibraryPlaylistCache.IsWarm(path));
    }

    [Fact]
    public void IsWarm_Mp3WithJacketColumn_RequiresArtworkProbe()
    {
        AppStorage.Settings.ApplyLibraryListColumnsMp3(
        [
            .. LibraryColumnFilter.Mp3Defaults,
            LibraryFileColumn.Jacket,
        ]);
        var path = Path.Combine(_dir, "cover.mp3");
        File.WriteAllBytes(path, MinimalWav());
        Assert.True(LibraryPlaylistCache.RequiresJacketForPath(path));

        LibraryCatalog.UpsertTags(path, new AudioFileTags { Probed = true, HasArtwork = true });
        Assert.False(LibraryPlaylistCache.IsWarm(path));

        LibraryMediaCache.PutArtwork(path, [1, 2, 3, 4]);
        Assert.True(LibraryPlaylistCache.IsWarm(path));
    }

    [Fact]
    public void IsWarm_Mp3WithJacketColumn_NoArtworkTag_IsWarmWithoutImage()
    {
        AppStorage.Settings.ApplyLibraryListColumnsMp3(
        [
            .. LibraryColumnFilter.Mp3Defaults,
            LibraryFileColumn.Jacket,
        ]);
        var path = Path.Combine(_dir, "plain.mp3");
        File.WriteAllBytes(path, MinimalWav());
        LibraryCatalog.UpsertTags(path, new AudioFileTags { Probed = true, HasArtwork = false });
        Assert.True(LibraryPlaylistCache.IsWarm(path));
    }

    [Fact]
    public void IsWarm_WaveWithWaveformColumn_RequiresBars()
    {
        AppStorage.Settings.ApplyLibraryListColumnsWave(LibraryColumnFilter.WaveDefaults);
        var path = Path.Combine(_dir, "tone.wav");
        File.WriteAllBytes(path, MinimalWav());
        Assert.True(LibraryPlaylistCache.RequiresWaveformForPath(path));

        LibraryCatalog.UpsertTags(path, new AudioFileTags { Probed = true, SampleRate = 48000 });
        Assert.False(LibraryPlaylistCache.IsWarm(path));

        var bars = LibraryPlaylistWaveformSizes.BarCount(
            AppStorage.Settings.ResolvedLibraryPlaylistWaveformSizeWave());
        LibraryMediaCache.PutWaveform(path, bars, new float[bars]);
        Assert.True(LibraryPlaylistCache.IsWarm(path));
    }

    [Fact]
    public void PutNoArtwork_CountsAsProbe()
    {
        var path = Path.Combine(_dir, "empty.mp3");
        File.WriteAllBytes(path, MinimalWav());
        LibraryMediaCache.PutNoArtwork(path);
        Assert.True(LibraryMediaCache.TryGetArtworkProbe(path, out var has));
        Assert.False(has);
        Assert.False(LibraryMediaCache.TryGetArtwork(path, out _));
    }

    private static byte[] MinimalWav()
    {
        var data = new byte[44];
        data[0] = (byte)'R';
        data[1] = (byte)'I';
        data[2] = (byte)'F';
        data[3] = (byte)'F';
        data[8] = (byte)'W';
        data[9] = (byte)'A';
        data[10] = (byte)'V';
        data[11] = (byte)'E';
        return data;
    }
}
