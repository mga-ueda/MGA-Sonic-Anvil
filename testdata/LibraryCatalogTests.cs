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
    public void LoadProbedTagTexts_ReturnsBuiltTagsUnderRoot()
    {
        var root = Path.Combine(_dir, "search");
        var nested = Path.Combine(root, "nested");
        var outside = Path.Combine(_dir, "outside");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(outside);
        var hit = Path.Combine(nested, "hit.wav");
        var bare = Path.Combine(nested, "bare.wav");
        var other = Path.Combine(outside, "other.wav");
        File.WriteAllBytes(hit, MinimalWav());
        File.WriteAllBytes(bare, MinimalWav());
        File.WriteAllBytes(other, MinimalWav());
        LibraryCatalog.ReconcileRoots([root, outside]);
        LibraryCatalog.UpsertTags(
            hit,
            new AudioFileTags
            {
                Probed = true,
                Title = "Yesterday",
                Artist = "The Beatles",
                Album = "Help!",
                Genre = "Rock",
            });
        LibraryCatalog.UpsertTags(
            other,
            new AudioFileTags { Probed = true, Artist = "Elsewhere" });

        var texts = LibraryCatalog.LoadProbedTagTexts([root]);
        Assert.True(texts.TryGetValue(hit, out var hitTexts));
        Assert.Contains("The Beatles", hitTexts);
        Assert.Contains("Yesterday", hitTexts);
        Assert.Contains("Help!", hitTexts);
        Assert.Contains("Rock", hitTexts);
        Assert.False(texts.ContainsKey(bare));
        Assert.False(texts.ContainsKey(other));
    }

    [Fact]
    public void LoadProbedTagTexts_IncludesPlaylistColumns()
    {
        var root = Path.Combine(_dir, "columns");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "song.mp3");
        File.WriteAllBytes(path, MinimalWav());
        LibraryCatalog.UpsertTags(
            path,
            new AudioFileTags
            {
                Probed = true,
                Comment = "take two",
                SampleRate = 44100,
                BitRateKbps = 192,
                Channels = 2,
                DurationSeconds = 65.5,
            });

        var texts = LibraryCatalog.LoadProbedTagTexts([root]);
        Assert.True(texts.TryGetValue(path, out var row));
        Assert.Contains("take two", row);
        Assert.Contains("44.1kHz", row);
        Assert.Contains("192 kbps", row);
        Assert.Contains("2ch", row);
        Assert.Contains("01:05.500", row);
    }

    [Fact]
    public void Clear_DropsProbedTags()
    {
        var root = Path.Combine(_dir, "clear");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "song.wav");
        File.WriteAllBytes(path, MinimalWav());
        LibraryCatalog.UpsertTags(path, new AudioFileTags { Probed = true, Artist = "Tester" });
        Assert.NotEmpty(LibraryCatalog.LoadProbedTagTexts([root]));
        Assert.True(LibraryCatalog.GetUsageBytes() > 0);

        LibraryCatalog.Clear();
        Assert.Equal(0, LibraryCatalog.GetUsageBytes());

        Assert.Empty(LibraryCatalog.LoadProbedTagTexts([root]));
    }

    [Fact]
    public void PruneUnreferenced_DropsRemovedRootAndMissingFile()
    {
        var keepRoot = Path.Combine(_dir, "keep");
        var dropRoot = Path.Combine(_dir, "drop");
        Directory.CreateDirectory(keepRoot);
        Directory.CreateDirectory(dropRoot);
        var keep = Path.Combine(keepRoot, "keep.wav");
        var gone = Path.Combine(keepRoot, "gone.wav");
        var outside = Path.Combine(dropRoot, "outside.wav");
        File.WriteAllBytes(keep, MinimalWav());
        File.WriteAllBytes(gone, MinimalWav());
        File.WriteAllBytes(outside, MinimalWav());
        LibraryCatalog.UpsertTags(keep, new AudioFileTags { Probed = true, Artist = "Keep" });
        LibraryCatalog.UpsertTags(gone, new AudioFileTags { Probed = true, Artist = "Gone" });
        LibraryCatalog.UpsertTags(outside, new AudioFileTags { Probed = true, Artist = "Out" });
        File.Delete(gone);

        LibraryCatalog.PruneUnreferenced([keepRoot]);

        var texts = LibraryCatalog.LoadProbedTagTexts([keepRoot, dropRoot]);
        Assert.Contains(keep, texts.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(gone, texts.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(outside, texts.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void PruneUnreferenced_KeepsRowsWhenRootDirectoryIsAbsent()
    {
        var root = Path.Combine(_dir, "volume");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "song.wav");
        File.WriteAllBytes(file, MinimalWav());
        LibraryCatalog.UpsertTags(file, new AudioFileTags { Probed = true, Title = "Stay" });
        Directory.Move(root, root + "-away");

        LibraryCatalog.PruneUnreferenced([root]);

        var texts = LibraryCatalog.LoadProbedTagTexts([root]);
        Assert.Contains(file, texts.Keys, StringComparer.OrdinalIgnoreCase);
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
