using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibrarySearchQueryTests
{
    [Fact]
    public void MatchesFileName_IgnoresFolderPath()
    {
        var query = LibrarySearchQuery.Parse("loop");
        Assert.True(LibrarySearchQuery.MatchesFileName(@"C:\Music\bgm_loop.wav", query));
        Assert.False(LibrarySearchQuery.MatchesFileName(@"C:\loop\se_hit.wav", query));
    }

    [Fact]
    public void MatchesFileName_AndOr()
    {
        var query = LibrarySearchQuery.Parse("bgm loop|se");
        Assert.True(LibrarySearchQuery.MatchesFileName("bgm_loop_01.wav", query));
        Assert.True(LibrarySearchQuery.MatchesFileName("SE_Hit.wav", query));
        Assert.False(LibrarySearchQuery.MatchesFileName("voice_loop.wav", query));
    }

    [Fact]
    public void MatchesRow_AllColumns()
    {
        var row = new LibraryFileRow
        {
            Name = "track01.wav",
            Title = "Yesterday",
            Artist = "The Beatles",
            Album = "Help!",
            Genre = "Rock",
            Folder = @"C:\Music\Beatles",
            DateText = "2026/09/20 23:12",
        };
        Assert.True(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("beatles yesterday")));
        Assert.True(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("Help")));
        Assert.True(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("track01")));
        Assert.True(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("2026")));
        Assert.True(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("beatles|radiohead")));
        Assert.False(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("radiohead")));
        Assert.False(LibrarySearchQuery.MatchesRow(row, LibrarySearchQuery.Parse("beatles abbey")));
    }

    [Fact]
    public void VisibleFolders_KeepsAncestorsOfHitsOnly()
    {
        var root = @"C:\Music";
        var files = new[]
        {
            @"C:\Music\AlbumA\hit_loop.wav",
            @"C:\Music\AlbumA\other.wav",
            @"C:\Music\AlbumB\miss.wav",
            @"C:\Music\AlbumA\CD1\also_loop.wav",
        };
        var visible = LibrarySearchQuery.VisibleFolders(
            [root],
            files,
            LibrarySearchQuery.Parse("loop"));
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(root), visible);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA\CD1"), visible);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), visible);
    }

    [Fact]
    public void VisibleFolders_FolderNameHit_KeepsFolderAndAncestors()
    {
        var root = @"C:\Music";
        var visible = LibrarySearchQuery.VisibleFolders(
            [root],
            [@"C:\Music\AlbumB\miss.wav", @"C:\Music\AlbumA\other.wav"],
            LibrarySearchQuery.Parse("AlbumB"));
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(root), visible);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), visible);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);
    }

    [Fact]
    public void VisibleFolders_And_CanHitFileNameAndFolderName()
    {
        var root = @"C:\Music";
        var visible = LibrarySearchQuery.VisibleFolders(
            [root],
            [
                @"C:\Music\AlbumB\bgm_loop.wav",
                @"C:\Music\AlbumB\miss.wav",
                @"C:\Music\AlbumA\bgm_loop.wav",
            ],
            LibrarySearchQuery.Parse("AlbumB loop"));
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), visible);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);
    }

    [Fact]
    public void VisibleFolders_EmptyFolder_MatchesName()
    {
        var root = @"C:\Music";
        var visible = LibrarySearchQuery.VisibleFolders(
            [root],
            [@"C:\Music\AlbumA\a.wav"],
            [@"C:\Music\AlbumA", @"C:\Music\loops"],
            LibrarySearchQuery.Parse("loops"));
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(root), visible);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\loops"), visible);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);
    }

    [Fact]
    public void VisibleFolders_TagHit_KeepsAncestorsOnly()
    {
        var root = @"C:\Music";
        var tagged = @"C:\Music\AlbumA\song.wav";
        var tags = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [tagged] = ["Yesterday", "The Beatles", "Help!"],
        };
        var visible = LibrarySearchQuery.VisibleFolders(
            [root],
            [tagged, @"C:\Music\AlbumB\other.wav"],
            LibrarySearchQuery.Parse("beatles"));
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);

        visible = LibrarySearchQuery.VisibleFolders(
            [root],
            [tagged, @"C:\Music\AlbumB\other.wav"],
            folderPaths: null,
            LibrarySearchQuery.Parse("beatles"),
            tags);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(root), visible);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), visible);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), visible);
    }

    [Fact]
    public void VisibleFolders_TagAndFolder_AndOr()
    {
        var root = @"C:\Music";
        var beatles = @"C:\Music\AlbumB\song.wav";
        var other = @"C:\Music\AlbumA\bgm_loop.wav";
        var tags = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [beatles] = ["The Beatles"],
            [other] = ["Radiohead"],
        };
        var files = new[] { beatles, other, @"C:\Music\AlbumB\miss.wav" };

        var and = LibrarySearchQuery.VisibleFolders(
            [root],
            files,
            folderPaths: null,
            LibrarySearchQuery.Parse("AlbumB beatles"),
            tags);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), and);
        Assert.DoesNotContain(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), and);

        var or = LibrarySearchQuery.VisibleFolders(
            [root],
            files,
            folderPaths: null,
            LibrarySearchQuery.Parse("beatles|loop"),
            tags);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumB"), or);
        Assert.Contains(LibrarySearchQuery.NormalizeFolder(@"C:\Music\AlbumA"), or);
    }

    [Fact]
    public void CollectPlaylistFiles_TagHit_IncludesOnlyThatFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-lib-tag-" + Guid.NewGuid().ToString("N"));
        var album = Path.Combine(root, "Album");
        Directory.CreateDirectory(album);
        var hit = Path.Combine(album, "song.wav");
        var miss = Path.Combine(album, "other.wav");
        WriteWav(hit);
        WriteWav(miss);
        try
        {
            var tags = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [hit] = ["The Beatles"],
            };
            var groups = LibrarySearchQuery.Parse("beatles|se");
            var files = new[] { hit, miss };
            var visible = LibrarySearchQuery.VisibleFolders(
                [root],
                files,
                folderPaths: null,
                groups,
                tags);
            var names = Names(LibrarySearchQuery.CollectPlaylistFiles(
                [root],
                groups,
                visible,
                [root],
                tags));
            Assert.Contains("song.wav", names);
            Assert.DoesNotContain("other.wav", names);

            WriteWav(Path.Combine(album, "se_hit.wav"));
            names = Names(LibrarySearchQuery.CollectPlaylistFiles(
                [album],
                groups,
                visible,
                [root],
                tags));
            Assert.Contains("song.wav", names);
            Assert.Contains("se_hit.wav", names);
            Assert.DoesNotContain("other.wav", names);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void VisibleFolders_EmptyQuery_IsEmptySet()
    {
        var visible = LibrarySearchQuery.VisibleFolders(
            [@"C:\Music"],
            [@"C:\Music\a.wav"],
            []);
        Assert.Empty(visible);
    }

    [Fact]
    public void CollectPlaylistFiles_ParentEnter_OnlyVisibleHits()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-lib-pl-" + Guid.NewGuid().ToString("N"));
        var battle = Path.Combine(root, "戦闘BGM");
        var album = Path.Combine(root, "Album");
        var other = Path.Combine(root, "Other");
        var nested = Path.Combine(battle, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(album);
        Directory.CreateDirectory(other);
        WriteWav(Path.Combine(battle, "battle.wav"));
        WriteWav(Path.Combine(battle, "other.wav"));
        WriteWav(Path.Combine(nested, "deep.wav"));
        WriteWav(Path.Combine(album, "戦闘_loop.wav"));
        WriteWav(Path.Combine(album, "se_hit.wav"));
        WriteWav(Path.Combine(other, "miss.wav"));
        try
        {
            var files = AudioCodec.CollectPlayerOpenableFromDirectory(root, recursive: true);
            var folders = new[] { root, battle, album, other, nested };
            var groups = LibrarySearchQuery.Parse("戦闘");
            var visible = LibrarySearchQuery.VisibleFolders([root], files, folders, groups);

            var fromRoot = Names(LibrarySearchQuery.CollectPlaylistFiles([root], groups, visible));
            Assert.Contains("battle.wav", fromRoot);
            Assert.Contains("other.wav", fromRoot);
            Assert.Contains("deep.wav", fromRoot);
            Assert.Contains("戦闘_loop.wav", fromRoot);
            Assert.DoesNotContain("se_hit.wav", fromRoot);
            Assert.DoesNotContain("miss.wav", fromRoot);

            var fromAlbum = Names(LibrarySearchQuery.CollectPlaylistFiles([album], groups, visible));
            Assert.Contains("戦闘_loop.wav", fromAlbum);
            Assert.DoesNotContain("se_hit.wav", fromAlbum);
            Assert.Single(fromAlbum);

            var fromBattle = Names(LibrarySearchQuery.CollectPlaylistFiles([battle], groups, visible));
            Assert.Contains("battle.wav", fromBattle);
            Assert.Contains("other.wav", fromBattle);
            Assert.Contains("deep.wav", fromBattle);
            Assert.Equal(3, fromBattle.Count);

            var all = Names(LibrarySearchQuery.CollectPlaylistFiles([root], [], visibleFolders: null));
            Assert.Equal(6, all.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CollectPlaylistFiles_SkipsMatchingFileOutsideVisibleFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "mga-lib-pl-hid-" + Guid.NewGuid().ToString("N"));
        var shown = Path.Combine(root, "Shown");
        var hidden = Path.Combine(root, "Hidden");
        Directory.CreateDirectory(shown);
        Directory.CreateDirectory(hidden);
        WriteWav(Path.Combine(shown, "戦闘.wav"));
        WriteWav(Path.Combine(hidden, "戦闘_secret.wav"));
        try
        {
            var groups = LibrarySearchQuery.Parse("戦闘");
            var visible = new[]
            {
                LibrarySearchQuery.NormalizeFolder(root),
                LibrarySearchQuery.NormalizeFolder(shown),
            };
            var names = Names(LibrarySearchQuery.CollectPlaylistFiles([root], groups, visible));
            Assert.Contains("戦闘.wav", names);
            Assert.DoesNotContain("戦闘_secret.wav", names);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void PlaylistWalk_Inactive_IncludesEveryFileAndChild()
    {
        var walk = LibraryExplorerPlaylistWalk.Inactive;
        Assert.False(walk.Active);
        Assert.True(walk.IncludeFile(@"C:\Music\a.wav", folderHit: false));
        Assert.True(walk.IncludeChild(@"C:\Music\Album", folderHit: false));
    }

    private static void WriteWav(string path) => File.WriteAllBytes(path, [0]);

    private static HashSet<string> Names(IEnumerable<string> paths) =>
        paths.Select(path => Path.GetFileName(path) ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
