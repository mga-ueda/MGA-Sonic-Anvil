using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryGroupJacketPickTests
{
    [Fact]
    public void Resolve_WithoutActive_UsesFirstArtworkInGroup()
    {
        var first = Row("a.mp3", art: false);
        var second = Row("b.mp3", art: true);
        var third = Row("c.mp3", art: true);
        Assert.Same(second, LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { first, second, third }, active: null));
    }

    [Fact]
    public void Resolve_ActiveInGroup_PrefersActiveEvenWithoutArtwork()
    {
        var first = Row("a.mp3", art: true);
        var second = Row("b.mp3", art: false);
        Assert.Same(second, LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { first, second }, second));
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
    public void Resolve_MatchesActiveByTag()
    {
        var tag = new object();
        var listed = Row("a.mp3", art: false, tag: tag);
        var selected = Row("a-copy.mp3", art: false, tag: tag);
        Assert.Same(listed, LibraryGroupJacketPick.Resolve(new LibraryFileRow[] { listed }, selected));
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
