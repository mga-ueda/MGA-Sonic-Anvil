using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryColumnFilterTests
{
    [Fact]
    public void Resolve_Empty_UsesDefaultsIncludingName()
    {
        var resolved = LibraryColumnFilter.Resolve([]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved);
        Assert.Contains(LibraryFileColumn.Name, resolved);
        Assert.Contains(LibraryFileColumn.Title, resolved);
        Assert.Contains(LibraryFileColumn.Artist, resolved);
        Assert.Contains(LibraryFileColumn.Album, resolved);
        Assert.Contains(LibraryFileColumn.Track, resolved);
        Assert.Contains(LibraryFileColumn.Disc, resolved);
        Assert.Contains(LibraryFileColumn.Year, resolved);
        Assert.Contains(LibraryFileColumn.Genre, resolved);
        Assert.Contains(LibraryFileColumn.Composer, resolved);
        Assert.Contains(LibraryFileColumn.Duration, resolved);
        Assert.Contains(LibraryFileColumn.Comment, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Jacket, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Kind, resolved);
        Assert.DoesNotContain(LibraryFileColumn.AlbumArtist, resolved);
    }

    [Fact]
    public void Defaults_MatchStoredLibraryListColumnsOrder()
    {
        Assert.Equal(
            new[]
            {
                LibraryFileColumn.Name,
                LibraryFileColumn.Title,
                LibraryFileColumn.Album,
                LibraryFileColumn.Artist,
                LibraryFileColumn.Composer,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Track,
                LibraryFileColumn.Disc,
                LibraryFileColumn.Year,
                LibraryFileColumn.Genre,
                LibraryFileColumn.Comment,
            },
            LibraryColumnFilter.Defaults);
        Assert.Equal(
            LibraryColumnFilter.Defaults,
            LibraryColumnFilter.All[..LibraryColumnFilter.Defaults.Length]);
        Assert.Equal(
            [
                "Name", "Title", "Album", "Artist", "Composer", "Duration", "Track", "Disc", "Year", "Genre",
                "Comment",
            ],
            LibraryColumnFilter.Serialize(LibraryColumnFilter.Defaults));
    }

    [Fact]
    public void Resolve_StoredNames_KeepsNameEvenIfOmitted()
    {
        var resolved = LibraryColumnFilter.Resolve(["Title", "BitRate", "Unknown"]);
        Assert.Equal(
            [LibraryFileColumn.Name, LibraryFileColumn.Title, LibraryFileColumn.BitRate],
            resolved);
    }

    [Fact]
    public void Serialize_PreservesGivenOrder()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Duration, LibraryFileColumn.Name, LibraryFileColumn.Title]);
        Assert.Equal(["Duration", "Name", "Title"], stored);
        Assert.Equal(
            [LibraryFileColumn.Duration, LibraryFileColumn.Name, LibraryFileColumn.Title],
            LibraryColumnFilter.Resolve(stored));
    }

    [Fact]
    public void Serialize_ThenResolve_RoundtripsVisibleOrder()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name]);
        var resolved = LibraryColumnFilter.Resolve(stored);
        Assert.Equal(["Title", "Folder", "Name"], stored);
        Assert.Equal(stored, LibraryColumnFilter.Serialize(resolved));
        Assert.Equal(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name],
            resolved);
    }

    [Fact]
    public void Merge_KeepsPreviousOrderAndAppendsNew()
    {
        var merged = LibraryColumnFilter.Merge(
            [LibraryFileColumn.Title, LibraryFileColumn.Name, LibraryFileColumn.Duration],
            [
                LibraryFileColumn.Name,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Folder,
                LibraryFileColumn.Title,
            ]);
        Assert.Equal(
            [
                LibraryFileColumn.Title,
                LibraryFileColumn.Name,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Folder,
            ],
            merged);
    }

    [Fact]
    public void Merge_DropsUncheckedAndKeepsName()
    {
        var merged = LibraryColumnFilter.Merge(
            LibraryColumnFilter.Defaults,
            [LibraryFileColumn.Name, LibraryFileColumn.Album]);
        Assert.Equal([LibraryFileColumn.Name, LibraryFileColumn.Album], merged);
    }
}
