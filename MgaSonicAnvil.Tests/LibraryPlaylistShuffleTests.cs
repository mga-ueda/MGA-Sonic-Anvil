using Xunit;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaylistShuffleTests
{
    [Fact]
    public void Next_PlaysEachIndexOncePerCycle()
    {
        var shuffle = new LibraryPlaylistShuffle();
        var seen = new HashSet<int> { 0 };
        var current = 0;
        for (var i = 1; i < 5; i++)
        {
            current = shuffle.Next(5, current, max => max - 1);
            Assert.True(seen.Add(current));
        }

        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Next_AfterFullCycle_ReshufflesAndAvoidsImmediateRepeat()
    {
        var shuffle = new LibraryPlaylistShuffle();
        var current = 0;
        for (var i = 1; i < 4; i++)
        {
            current = shuffle.Next(4, current, max => 0);
        }

        var last = current;
        var next = shuffle.Next(4, last, max => 0);
        Assert.NotEqual(last, next);
    }

    [Fact]
    public void Next_AtCycleBoundary_SameCurrentReturnsSameNext()
    {
        // ギャップレス先読みが Next を二度呼んでも、一周境界で次曲が食い違わないこと。
        var shuffle = new LibraryPlaylistShuffle();
        var current = 0;
        for (var i = 1; i < 4; i++)
        {
            current = shuffle.Next(4, current, max => 0);
        }

        var first = shuffle.Next(4, current, max => 0);
        var second = shuffle.Next(4, current, max => 0);
        Assert.Equal(first, second);
        Assert.NotEqual(current, first);
    }

    [Fact]
    public void Next_AcrossTwoCycles_NoImmediateRepeatAtBoundary()
    {
        var shuffle = new LibraryPlaylistShuffle();
        var current = shuffle.First(3, max => 0);
        var played = new List<int> { current };
        for (var i = 0; i < 5; i++)
        {
            current = shuffle.Next(3, current, max => 0);
            played.Add(current);
        }

        Assert.NotEqual(played[2], played[3]);
    }

    [Fact]
    public void Previous_WalksBackWithinCycle()
    {
        var shuffle = new LibraryPlaylistShuffle();
        var first = shuffle.Next(3, 0, max => 0);
        var second = shuffle.Next(3, first, max => 0);
        Assert.Equal(first, shuffle.Previous(3, second, max => 0));
    }

    [Fact]
    public void Next_SingleTrack_StaysZero()
    {
        var shuffle = new LibraryPlaylistShuffle();
        Assert.Equal(0, shuffle.Next(1, 0));
        Assert.Equal(0, shuffle.Next(1, 0));
        Assert.Equal(-1, shuffle.Next(0, 0));
    }

    [Fact]
    public void CountChange_RebuildsOrder()
    {
        var shuffle = new LibraryPlaylistShuffle();
        var a = shuffle.Next(3, 0, max => 0);
        var b = shuffle.Next(5, a, max => 0);
        Assert.InRange(b, 0, 4);
    }

    [Fact]
    public void First_ReturnsOneOfTheTracks()
    {
        var shuffle = new LibraryPlaylistShuffle();
        Assert.InRange(shuffle.First(4, max => 0), 0, 3);
        Assert.Equal(-1, shuffle.First(0));
    }
}
