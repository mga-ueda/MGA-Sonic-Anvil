using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DocumentTabLayoutTests
{
    [Fact]
    public void MinVisibleText_TakesSixCharacters()
    {
        Assert.Equal("abcdef", DocumentTabLayout.MinVisibleText("abcdefghij"));
        Assert.Equal("abc", DocumentTabLayout.MinVisibleText("abc"));
        Assert.Equal("", DocumentTabLayout.MinVisibleText(""));
    }

    [Fact]
    public void FitsWithoutArrows_WhenInactiveMinsStillOverflow_IsFalse()
    {
        DocumentTabSlot[] tabs =
        [
            new(220, 80, KeepWide: true),
            new(180, 80, KeepWide: false),
            new(180, 80, KeepWide: false),
        ];
        Assert.False(DocumentTabLayout.FitsWithoutArrows(tabs, hostWidth: 300));
    }

    [Fact]
    public void FitsWithoutArrows_WhenInactiveMinsFit_IsTrue()
    {
        DocumentTabSlot[] tabs =
        [
            new(220, 80, KeepWide: true),
            new(180, 80, KeepWide: false),
            new(180, 80, KeepWide: false),
        ];
        Assert.True(DocumentTabLayout.FitsWithoutArrows(tabs, hostWidth: 380));
    }

    [Fact]
    public void Allocate_KeepsActivePreferredAndShrinksOthersEqually()
    {
        DocumentTabSlot[] tabs =
        [
            new(200, 80, KeepWide: true),
            new(200, 80, KeepWide: false),
            new(200, 80, KeepWide: false),
        ];
        var widths = new double[3];
        DocumentTabLayout.Allocate(tabs, available: 480, widths);
        Assert.Equal(200, widths[0], 3);
        Assert.Equal(140, widths[1], 3);
        Assert.Equal(140, widths[2], 3);
    }

    [Fact]
    public void Allocate_StopsAtSixCharMin()
    {
        DocumentTabSlot[] tabs =
        [
            new(200, 80, KeepWide: true),
            new(200, 80, KeepWide: false),
            new(200, 80, KeepWide: false),
        ];
        var widths = new double[3];
        DocumentTabLayout.Allocate(tabs, available: 300, widths);
        Assert.Equal(200, widths[0], 3);
        Assert.Equal(80, widths[1], 3);
        Assert.Equal(80, widths[2], 3);
    }

    [Fact]
    public void Allocate_HitsNearMinFirstThenKeepsShrinkingTheRest()
    {
        DocumentTabSlot[] tabs =
        [
            new(200, 80, KeepWide: true),
            new(100, 90, KeepWide: false),
            new(200, 80, KeepWide: false),
        ];
        var widths = new double[3];
        DocumentTabLayout.Allocate(tabs, available: 400, widths);
        Assert.Equal(200, widths[0], 3);
        Assert.Equal(90, widths[1], 3);
        Assert.Equal(110, widths[2], 3);
    }
}
