using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TileSearchQueryTests
{
    [Fact]
    public void Parse_SplitsAndOrGroups()
    {
        var groups = TileSearchQuery.Parse("bgm loop|se");
        Assert.Equal(2, groups.Count);
        Assert.Equal(["bgm", "loop"], groups[0]);
        Assert.Equal(["se"], groups[1]);
    }

    [Fact]
    public void Matches_EmptyQuery_MatchesAll()
    {
        Assert.True(TileSearchQuery.Matches("song.wav", []));
        Assert.True(TileSearchQuery.Matches("song.wav", TileSearchQuery.Parse("   ")));
    }

    [Fact]
    public void Matches_AndOr_IsCaseInsensitive()
    {
        var query = TileSearchQuery.Parse("BGM loop|SE");
        Assert.True(TileSearchQuery.Matches("bgm_loop_01.wav", query));
        Assert.True(TileSearchQuery.Matches("se_hit.wav", query));
        Assert.False(TileSearchQuery.Matches("voice_loop.wav", query));
    }

    [Fact]
    public void ShouldKeep_UnmatchedDirty()
    {
        Assert.True(TileSearchQuery.ShouldKeep(matches: true, isDirty: false));
        Assert.True(TileSearchQuery.ShouldKeep(matches: false, isDirty: true));
        Assert.False(TileSearchQuery.ShouldKeep(matches: false, isDirty: false));
    }

    [Fact]
    public void SessionsToDrop_KeepsHitsAndDirty()
    {
        var hit = Session("bgm_loop.wav", dirty: false);
        var dirtyMiss = Session("voice.wav", dirty: true);
        var cleanMiss = Session("se_hit.wav", dirty: false);
        var drop = TileSearchQuery.SessionsToDrop(
            [hit, dirtyMiss, cleanMiss],
            TileSearchQuery.Parse("bgm"));
        Assert.Same(cleanMiss, Assert.Single(drop));
    }

    [Fact]
    public void SessionsToDrop_EmptyQuery_DropsNone()
    {
        var miss = Session("se_hit.wav", dirty: false);
        var drop = TileSearchQuery.SessionsToDrop([miss], []);
        Assert.Empty(drop);
        Assert.Empty(TileSearchQuery.SessionsToDrop([miss], TileSearchQuery.Parse("   ")));
    }

    [Fact]
    public void SessionsToDrop_UsesSnapshot_NotLaterClearedGroups()
    {
        var miss = Session("se_hit.wav", dirty: false);
        var snapshot = TileSearchQuery.Parse("bgm");
        var drop = TileSearchQuery.SessionsToDrop([miss], snapshot);
        Assert.Same(miss, Assert.Single(drop));
        Assert.Empty(TileSearchQuery.SessionsToDrop([miss], []));
    }

    private static DocumentSession Session(string name, bool dirty)
    {
        var document = new AudioDocument(
            new float[48],
            48000,
            1,
            16,
            AudioFileKind.Wave,
            @"C:\tmp\" + name);
        document.SetDirty(dirty);
        return new DocumentSession(document);
    }
}
