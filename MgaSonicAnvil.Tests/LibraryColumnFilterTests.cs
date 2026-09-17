using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryColumnFilterTests
{
    [Fact]
    public void Resolve_Empty_UsesDefaultsIncludingName()
    {
        var resolved = LibraryColumnFilter.Resolve([]);
        Assert.Equal(LibraryColumnFilter.Defaults, resolved.OrderBy(c => Array.IndexOf(LibraryColumnFilter.All, c)));
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
                LibraryFileColumn.Artist,
                LibraryFileColumn.Album,
                LibraryFileColumn.Track,
                LibraryFileColumn.Disc,
                LibraryFileColumn.Year,
                LibraryFileColumn.Genre,
                LibraryFileColumn.Composer,
                LibraryFileColumn.Duration,
                LibraryFileColumn.Comment,
            },
            LibraryColumnFilter.Defaults);
        Assert.Equal(
            LibraryColumnFilter.Defaults,
            LibraryColumnFilter.All[..LibraryColumnFilter.Defaults.Length]);
        Assert.Equal(
            [
                "Name", "Title", "Artist", "Album", "Track", "Disc", "Year", "Genre", "Composer", "Duration",
                "Comment",
            ],
            LibraryColumnFilter.Serialize(LibraryColumnFilter.Defaults));
    }

    [Fact]
    public void Resolve_StoredNames_KeepsNameEvenIfOmitted()
    {
        var resolved = LibraryColumnFilter.Resolve(["Title", "BitRate", "Unknown"]);
        Assert.Contains(LibraryFileColumn.Name, resolved);
        Assert.Contains(LibraryFileColumn.Title, resolved);
        Assert.Contains(LibraryFileColumn.BitRate, resolved);
        Assert.DoesNotContain(LibraryFileColumn.Artist, resolved);
        Assert.Equal(3, resolved.Count);
    }

    [Fact]
    public void Serialize_ThenResolve_RoundtripsVisibleSet()
    {
        var stored = LibraryColumnFilter.Serialize(
            [LibraryFileColumn.Title, LibraryFileColumn.Folder, LibraryFileColumn.Name]);
        var resolved = LibraryColumnFilter.Resolve(stored);
        Assert.Equal(LibraryColumnFilter.Serialize(resolved), stored);
        Assert.Equal(["Name", "Title", "Folder"], stored);
    }
}
