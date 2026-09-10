using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class HistoryStripLayoutTests
{
    [Fact]
    public void VisibleCount_UsesInnerHeight()
    {
        Assert.Equal(0, HistoryStripLayout.VisibleCount(0, 14, 2));
        Assert.Equal(0, HistoryStripLayout.VisibleCount(4, 14, 2));
        Assert.Equal(7, HistoryStripLayout.VisibleCount(102, 14, 2));
    }

    [Fact]
    public void VisibleStart_KeepsCurrentAndClipsOlder()
    {
        Assert.Equal(0, HistoryStripLayout.VisibleStart(3, 2, 8));
        Assert.Equal(5, HistoryStripLayout.VisibleStart(12, 11, 7));
        Assert.Equal(0, HistoryStripLayout.VisibleStart(12, 0, 7));
        Assert.Equal(2, HistoryStripLayout.VisibleStart(12, 8, 7));
    }

    [Fact]
    public void VisibleStart_EmptyOrNoRows()
    {
        Assert.Equal(0, HistoryStripLayout.VisibleStart(0, 0, 7));
        Assert.Equal(0, HistoryStripLayout.VisibleStart(4, 2, 0));
    }
}
