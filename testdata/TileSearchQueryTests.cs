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
    public void Parse_QuotedPhrase_KeepsSpaces()
    {
        var groups = TileSearchQuery.Parse("\"devil may cry 4\"");
        Assert.Equal(["devil may cry 4"], Assert.Single(groups));
    }

    [Fact]
    public void Matches_QuotedPhrase_RequiresContiguousString()
    {
        var query = TileSearchQuery.Parse("\"devil may cry 4\"");
        Assert.True(TileSearchQuery.Matches("Devil May Cry 4", query));
        Assert.True(TileSearchQuery.Matches("ost devil may cry 4 remix", query));
        Assert.False(TileSearchQuery.MatchesAny(["devil", "may", "cry", "4"], query));
        Assert.False(TileSearchQuery.MatchesAny(["devil may cry", "4"], query));
    }

    [Fact]
    public void Parse_QuotedPhrase_DoesNotSplitOrInsideQuotes()
    {
        var groups = TileSearchQuery.Parse("\"a|b\"|se");
        Assert.Equal(2, groups.Count);
        Assert.Equal(["a|b"], groups[0]);
        Assert.Equal(["se"], groups[1]);
    }

    [Fact]
    public void MatchesAny_AndTerms_CanHitDifferentTexts()
    {
        var query = TileSearchQuery.Parse("beatles yesterday");
        Assert.True(TileSearchQuery.MatchesAny(["The Beatles", "Yesterday"], query));
        Assert.False(TileSearchQuery.MatchesAny(["The Beatles", "Help!"], query));
        Assert.True(TileSearchQuery.MatchesAny(["yesterday beatles"], query));
    }

    [Fact]
    public void Matches_WildcardStar_AnyString()
    {
        Assert.True(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("*.wav")));
        Assert.True(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("bgm*")));
        Assert.True(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("*loop*")));
        Assert.False(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("*.mp3")));
        Assert.False(TileSearchQuery.Matches("voice_loop.wav", TileSearchQuery.Parse("bgm*")));
    }

    [Fact]
    public void Matches_WildcardQuestion_AnyOneChar()
    {
        Assert.True(TileSearchQuery.Matches("bgm.wav", TileSearchQuery.Parse("?gm.wav")));
        Assert.True(TileSearchQuery.Matches("se_hit.wav", TileSearchQuery.Parse("se_???.wav")));
        Assert.False(TileSearchQuery.Matches("bgm.wav", TileSearchQuery.Parse("??gm.wav")));
        Assert.False(TileSearchQuery.Matches("se_hit.wav", TileSearchQuery.Parse("se_??.wav")));
    }

    [Fact]
    public void Matches_WithoutWildcard_KeepsSubstring()
    {
        Assert.True(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("loop")));
        Assert.False(TileSearchQuery.Matches("bgm_loop_01.wav", TileSearchQuery.Parse("loop.wav")));
    }

    [Fact]
    public void Matches_Wildcard_AndOr()
    {
        var query = TileSearchQuery.Parse("bgm*|*.mp3|se_???.*");
        Assert.True(TileSearchQuery.Matches("bgm_loop.wav", query));
        Assert.True(TileSearchQuery.Matches("voice.mp3", query));
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
