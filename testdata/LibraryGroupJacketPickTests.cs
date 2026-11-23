using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryGroupJacketPickTests
{
    [Fact]
    public void Resolve_UsesFirstArtworkInGroup()
    {
        var first = Row("a.mp3", art: false);
        var second = Row("b.mp3", art: true);
        var third = Row("c.mp3", art: true);
        Assert.Same(second, LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { first, second, third }));
    }

    [Fact]
    public void Resolve_IgnoresActive_KeepsFirstArtwork()
    {
        var first = Row("a.mp3", art: true);
        var second = Row("b.mp3", art: false);
        Assert.Same(first, LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { first, second }, second));
    }

    [Fact]
    public void Resolve_ActiveInOtherGroup_KeepsFirstArtwork()
    {
        var first = Row("a.mp3", art: true);
        var second = Row("b.mp3", art: true);
        var other = Row("z.mp3", art: true);
        var group = new LibraryFileRow[] { first, second };
        Assert.Same(first, LibraryGroupJacketPick.Resolve(group, other));
        Assert.False(LibraryGroupJacketPick.ContainsActive(group, other));
        Assert.True(LibraryGroupJacketPick.ContainsActive(group, second));
    }

    [Fact]
    public void Resolve_NoArtwork_ReturnsNull()
    {
        var listed = Row("a.mp3", art: false);
        Assert.Null(LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { listed }, listed));
    }

    private static LibraryFileRow Row(string name, bool art, object? tag = null) =>
        new()
        {
            Name = name,
            Kind = "MP3",
            HasArtwork = art,
            JacketText = art ? UiStrings.LibraryJacketMark : string.Empty,
            Tag = tag,
        };
}
