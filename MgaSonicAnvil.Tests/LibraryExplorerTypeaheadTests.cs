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
    public void FindMatchIndex_KeepsCurrentWhenStillMatches()
    {
        string[] names =
        [
            "Devil May Cry 2 Original Soundtrack",
            "Devil May Cry 3 Original Soundtrack",
            "Devil May Cry 4 Original Soundtrack",
        ];
        // 同文字連打でクエリは積むが、ヒットする限り現在行を維持（渡り歩きは ↑↓）。
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "d", currentIndex: 0));
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "de", currentIndex: 0));
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex(names, "dd", currentIndex: 0));
    }

    [Fact]
    public void FindStepMatchIndex_WalksHitsWithWrap()
    {
        string[] names =
        [
            "Devil May Cry 2 Original Soundtrack",
            "Devil May Cry 3 Original Soundtrack",
            "Devil May Cry 4 Original Soundtrack",
            "SEGA",
        ];
        Assert.Equal(1, LibraryExplorerTypeahead.FindStepMatchIndex(names, "d", currentIndex: 0, direction: 1));
        Assert.Equal(2, LibraryExplorerTypeahead.FindStepMatchIndex(names, "d", currentIndex: 1, direction: 1));
        Assert.Equal(0, LibraryExplorerTypeahead.FindStepMatchIndex(names, "d", currentIndex: 2, direction: 1));
        Assert.Equal(2, LibraryExplorerTypeahead.FindStepMatchIndex(names, "d", currentIndex: 0, direction: -1));
        Assert.Equal(0, LibraryExplorerTypeahead.FindStepMatchIndex(names, "d", currentIndex: 1, direction: -1));
    }

    [Fact]
    public void FindMatchIndex_KeepsCurrentWhenRefiningMultiChar()
    {
        string[] names = ["Test", "Tiny"];
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "te", currentIndex: 0));
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "tes", currentIndex: 0));
    }

    [Fact]
    public void FindMatchIndex_BelowFirst_ThenWrapsFromTop()
    {
        // Busy / Gnome は途中に a が無い。
        string[] names = ["Alpha", "Busy", "Gnome"];
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "a", currentIndex: 1));
        Assert.Equal(0, LibraryExplorerTypeahead.FindMatchIndex(names, "a", currentIndex: -1));
        string[] lower = ["Music", "Gnome", "Apple"];
        Assert.Equal(2, LibraryExplorerTypeahead.FindMatchIndex(lower, "a", currentIndex: 0));
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex(lower, "z", currentIndex: 1));
    }

    [Fact]
    public void FindMatchIndex_NoHit_ReturnsMinusOne()
    {
        string[] names = ["Test", "Task"];
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex(names, "z", currentIndex: 0));
        Assert.Equal(-1, LibraryExplorerTypeahead.FindMatchIndex([], "t", currentIndex: 0));
    }

    [Fact]
    public void Append_AlwaysStacksIncludingSameChar()
    {
        Assert.Equal("d", LibraryExplorerTypeahead.Append("", "d"));
        Assert.Equal("dd", LibraryExplorerTypeahead.Append("d", "d"));
        Assert.Equal("ddd", LibraryExplorerTypeahead.Append("dd", "D"));
        Assert.Equal("de", LibraryExplorerTypeahead.Append("d", "e"));
    }

    [Fact]
    public void TryMapChar_LettersAndDigits_NotShiftDigitOrStar()
    {
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.T, ModifierKeys.None, out var t));
        Assert.Equal("t", t);
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.A, ModifierKeys.Shift, out var a));
        Assert.Equal("a", a);
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.D2, ModifierKeys.None, out var d2));
        Assert.Equal("2", d2);
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.D0, ModifierKeys.None, out var d0));
        Assert.Equal("0", d0);
        Assert.True(LibraryExplorerTypeahead.TryMapChar(Key.NumPad2, ModifierKeys.None, out var n2));
        Assert.Equal("2", n2);
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.D8, ModifierKeys.Shift, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.NumPad2, ModifierKeys.Shift, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.Oem2, ModifierKeys.None, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.Oem2, ModifierKeys.Shift, out _));
        Assert.False(LibraryExplorerTypeahead.TryMapChar(Key.Space, ModifierKeys.None, out _));
    }

    [Fact]
    public void IsTypeaheadText_AcceptsDigits()
    {
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("t"));
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("a2"));
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("2"));
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("90"));
        Assert.True(LibraryExplorerTypeahead.IsTypeaheadText("５"));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText(""));
        Assert.False(LibraryExplorerTypeahead.IsTypeaheadText(" "));
    }

    [Fact]
    public void Matches_IgnoresCaseAndFullwidth()
    {
        Assert.True(LibraryExplorerTypeahead.Matches("Devil May Cry", "d"));
        Assert.True(LibraryExplorerTypeahead.Matches("devil may cry", "D"));
        Assert.True(LibraryExplorerTypeahead.Matches("Ｄevil", "d"));
        Assert.True(LibraryExplorerTypeahead.Matches("Devil", "Ｄ"));
        Assert.False(LibraryExplorerTypeahead.Matches("Alpha", "d"));
    }

    [Fact]
    public void Matches_MidNameSubstring()
    {
        Assert.True(LibraryExplorerTypeahead.Matches("Devil May Cry", "may"));
        Assert.True(LibraryExplorerTypeahead.Matches("Prince of Persia Trilogy Soundtrack", "persia"));
        Assert.True(LibraryExplorerTypeahead.Matches("SQUARE ENIX", "enix"));
        Assert.False(LibraryExplorerTypeahead.Matches("SEGA", "enix"));
    }

    [Fact]
    public void Matches_DriveLetterInParentheses()
    {
        Assert.True(LibraryExplorerTypeahead.Matches("SSD (D:)", "d"));
        Assert.True(LibraryExplorerTypeahead.Matches("SSD (D:)", "d:"));
        Assert.True(LibraryExplorerTypeahead.Matches("ローカル ディスク (C:)", "c"));
        Assert.False(LibraryExplorerTypeahead.Matches("SSD (D:)", "c"));
    }

    [Fact]
    public void FindMatchIndex_MidName_FindsBelowCursor()
    {
        string[] names = ["Game", "CAPCOM", "Devil May Cry other BGM", "SEGA"];
        Assert.Equal(2, LibraryExplorerTypeahead.FindMatchIndex(names, "may", currentIndex: 0));
    }

    [Fact]
    public void Append_NormalizesUppercaseAndFullwidth()
    {
        Assert.Equal("d", LibraryExplorerTypeahead.Append("", "D"));
        Assert.Equal("dd", LibraryExplorerTypeahead.Append("d", "Ｄ"));
    }

    [Fact]
    public void TryFindMatchRange_FindsMidNameSpan()
    {
        Assert.True(LibraryExplorerTypeahead.TryFindMatchRange("Devil May Cry", "may", out var start, out var length));
        Assert.Equal(6, start);
        Assert.Equal(3, length);
        Assert.True(LibraryExplorerTypeahead.TryFindMatchRange("SQUARE ENIX", "enix", out start, out length));
        Assert.Equal(7, start);
        Assert.Equal(4, length);
        Assert.False(LibraryExplorerTypeahead.TryFindMatchRange("SEGA", "enix", out _, out _));
    }

    [Fact]
    public void TryFindMatchRange_FullwidthMapsToOriginal()
    {
        Assert.True(LibraryExplorerTypeahead.TryFindMatchRange("Ｄevil", "d", out var start, out var length));
        Assert.Equal(0, start);
        Assert.Equal(1, length);
    }
}
