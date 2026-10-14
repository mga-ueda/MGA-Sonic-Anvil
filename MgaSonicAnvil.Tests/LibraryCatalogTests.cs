using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryCatalogTests : IDisposable
{
    private readonly string _dir;

    public LibraryCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-library-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        LibraryCatalog.UseDirectoryForTests(_dir);
    }

    public void Dispose()
    {
        LibraryCatalog.ResetForTests();
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
    public void UpsertAndGet_ReturnsSameTagsWhenUnchanged()
    {
        var root = Path.Combine(_dir, "music");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "song.wav");
        File.WriteAllBytes(path, MinimalWav());

        var tags = new AudioFileTags
        {
            Probed = true,
            Title = "Hello",
            Artist = "Tester",
            Album = "Demo",
            DurationSeconds = 1.5,
            SampleRate = 48000,
            BitsPerSample = 16,
            Channels = 2,
        };
        LibraryCatalog.UpsertTags(path, tags);

        Assert.True(LibraryCatalog.TryGetFreshTags(path, out var cached));
        Assert.Equal("Hello", cached.Title);
        Assert.Equal("Tester", cached.Artist);
        Assert.Equal("Demo", cached.Album);
        Assert.Equal(1.5, cached.DurationSeconds);
        Assert.Equal(48000, cached.SampleRate);
        Assert.Equal(16, cached.BitsPerSample);
        Assert.Equal(2, cached.Channels);
    }

    [Fact]
    public void TryGetFreshTags_FalseAfterFileRewrite()
    {
        var root = Path.Combine(_dir, "music");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "song.wav");
        File.WriteAllBytes(path, MinimalWav());
        LibraryCatalog.UpsertTags(
            path,
            new AudioFileTags { Probed = true, Title = "Old", SampleRate = 44100 });

        // mtime/size を変える
        File.WriteAllBytes(path, MinimalWav(extraBytes: 16));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.False(LibraryCatalog.TryGetFreshTags(path, out _));
    }

    [Fact]
    public void ReconcileRoots_IndexesFilesAndListsThem()
    {
        var root = Path.Combine(_dir, "lib");
        Directory.CreateDirectory(root);
        var a = Path.Combine(root, "a.wav");
        var b = Path.Combine(root, "nested", "b.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(b)!);
        File.WriteAllBytes(a, MinimalWav());
        File.WriteAllBytes(b, MinimalWav());
        File.WriteAllText(Path.Combine(root, "readme.txt"), "nope");

        LibraryCatalog.ReconcileRoots([root]);

        Assert.True(LibraryCatalog.TryListFilesUnderRoots([root], out var files));
        Assert.Contains(a, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(b, files, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, files.Length);
    }

    [Fact]
    public void ReconcileRoots_RemovesDeletedFiles()
    {
        var root = Path.Combine(_dir, "purge");
        Directory.CreateDirectory(root);
        var keep = Path.Combine(root, "keep.wav");
        var drop = Path.Combine(root, "drop.wav");
        File.WriteAllBytes(keep, MinimalWav());
        File.WriteAllBytes(drop, MinimalWav());
        LibraryCatalog.ReconcileRoots([root]);

        File.Delete(drop);
        LibraryCatalog.ReconcileRoots([root]);

        Assert.True(LibraryCatalog.TryListFilesUnderRoots([root], out var files));
        Assert.Contains(keep, files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(drop, files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryListFilesUnderPath_UsesCoveredRoot()
    {
        var root = Path.Combine(_dir, "under");
        var nested = Path.Combine(root, "nested");
        Directory.CreateDirectory(nested);
        var a = Path.Combine(root, "a.wav");
        var b = Path.Combine(nested, "b.wav");
        File.WriteAllBytes(a, MinimalWav());
        File.WriteAllBytes(b, MinimalWav());
        LibraryCatalog.ReconcileRoots([root]);

        Assert.True(LibraryCatalog.TryListFilesUnderPath(nested, out var files));
        Assert.Contains(b, files, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(a, files, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ensure_UsesCatalogCache()
    {
        var root = Path.Combine(_dir, "probe");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "cached.wav");
        File.WriteAllBytes(path, MinimalWav());
        LibraryCatalog.UpsertTags(
            path,
            new AudioFileTags
            {
                Probed = true,
                Title = "FromCache",
                SampleRate = 48000,
                Channels = 1,
                BitsPerSample = 16,
                DurationSeconds = 0.25,
            });

        var document = AudioDocument.CreateDeferred(path);
        AudioTagProbe.Ensure(document);
        Assert.True(document.Tags.Probed);
        Assert.Equal("FromCache", document.Tags.Title);
    }

    private static byte[] MinimalWav(int extraBytes = 0)
    {
        // 44-byte header + optional padding. Enough for FileInfo / catalog keys.
        var data = new byte[44 + extraBytes];
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
