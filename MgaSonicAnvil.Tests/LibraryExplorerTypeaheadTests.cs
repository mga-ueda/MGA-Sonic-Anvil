using System.Windows.Input;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryExplorerTypeaheadTests
{
    [Fact]
    public void FindMatchIndex_TThenTa_JumpsFromTestToTask()
    {
        string[] names = ["Music", "Test", "Task", "Other"];
        Assert.Equal(1, LibraryExplorerTypeahead.FindMatchIndex(names, "t", currentIndex: 0));
        Assert.Equal(1, LibraryExplorerTypeahead.FindMatchIndex(names, "te", currentIndex: 1));
        Assert.Equal(2, LibraryExplorerTypeahead.FindMatchIndex(names, "ta", currentIndex: 1));
    }

    [Fact]
    public void FindMatchIndex_KeepsCurrentWhenStillMatching()
    {
        string[] names = ["Test", "Tiny"];
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "t", currentIndex: 0));
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "te", currentIndex: 0));
    }

    [Fact]
    public void FindMatchIndex_NoHit_ReturnsMinusOne()
    {
        string[] names = ["Test", "Task"];
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex(names, "z", currentIndex: 0));
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex([], "t", currentIndex: 0));
    }

    [Fact]
    public void Append_IdleGap_StartsNewQuery()
    {
        var t0 = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var t1 = t0 + TimeSpan.FromMilliseconds(200);
        var t2 = t0 + TimeSpan.FromSeconds(2);
        Assert.Equal("te", LibraryExplorerTypeahead.Append("t", "e", t1, t0, LibraryExplorerTypeahead.IdleReset));
        Assert.Equal("e", LibraryExplorerTypeahead.Append("te", "e", t2, t1, LibraryExplorerTypeahead.IdleReset));
    }

    [Fact]
    public void TryMapChar_LettersOnly_NotDigitsNumpadOrStar()
    {
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.T, ModifierKeys.None, out var t));
        Assert.Equal("t", t);
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.A, ModifierKeys.Shift, out var a));
        Assert.Equal("a", a);
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.D2, ModifierKeys.None, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.D0, ModifierKeys.None, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.NumPad2, ModifierKeys.None, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.D8, ModifierKeys.Shift, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.Oem2, ModifierKeys.None, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.Space, ModifierKeys.None, out _));
    }

    [Fact]
    public void IsTypeaheadText_RejectsDigits()
    {
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("t"));
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("a2"));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText("2"));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText("90"));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText("５"));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText(""));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText(" "));
    }
}
