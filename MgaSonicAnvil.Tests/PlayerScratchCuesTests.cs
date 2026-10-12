using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlayerScratchCuesTests
{
    [Fact]
    public void TrySetAbIn_RejectsAtOrPastOut()
    {
        Assert.True(PlayerScratchCues.TryResolveAbIn(10, null, null, out var first));
        Assert.Equal(10, first);

        Assert.True(PlayerScratchCues.TryResolveAbIn(20, 10, 50, out var updated));
        Assert.Equal(20, updated);

        Assert.False(PlayerScratchCues.TryResolveAbIn(50, 10, 50, out var sameAsOut));
        Assert.Equal(10, sameAsOut);

        Assert.False(PlayerScratchCues.TryResolveAbIn(60, 10, 50, out var pastOut));
        Assert.Equal(10, pastOut);
    }

    [Fact]
    public void TrySetAbOut_RejectsAtOrBeforeIn()
    {
        Assert.True(PlayerScratchCues.TryResolveAbOut(40, null, null, out var first));
        Assert.Equal(40, first);

        Assert.True(PlayerScratchCues.TryResolveAbOut(30, 10, 40, out var updated));
        Assert.Equal(30, updated);

        Assert.False(PlayerScratchCues.TryResolveAbOut(10, 10, 40, out var sameAsIn));
        Assert.Equal(40, sameAsIn);

        Assert.False(PlayerScratchCues.TryResolveAbOut(5, 10, 40, out var beforeIn));
        Assert.Equal(40, beforeIn);
    }

    [Fact]
    public void LoopRange_RequiresOrderedPair()
    {
        Assert.True(PlayerScratchCues.LoopRange(null, 20).IsEmpty);
        Assert.True(PlayerScratchCues.LoopRange(10, null).IsEmpty);
        Assert.True(PlayerScratchCues.LoopRange(20, 10).IsEmpty);
        Assert.True(PlayerScratchCues.LoopRange(10, 10).IsEmpty);

        var range = PlayerScratchCues.LoopRange(10, 40);
        Assert.False(range.IsEmpty);
        Assert.Equal(10, range.StartFrame);
        Assert.Equal(40, range.EndFrame);
    }

    [Fact]
    public void Markers_AddMoveRemove_StaySortedAndUnique()
    {
        var cues = new PlayerScratchCues();
        Assert.True(cues.TryAddMarker(100, 1000));
        Assert.True(cues.TryAddMarker(50, 1000));
        Assert.False(cues.TryAddMarker(50, 1000));
        Assert.Equal(2, cues.Markers.Count);
        Assert.Equal(50, cues.Markers[0].Frame);
        Assert.Equal(100, cues.Markers[1].Frame);

        Assert.True(cues.TryMoveMarkers([50], 10, 1000, out var applied));
        Assert.Equal(10, applied);
        Assert.Equal(60, cues.Markers[0].Frame);
        Assert.Equal(100, cues.Markers[1].Frame);

        Assert.True(cues.TryRemoveMarkers([100]));
        Assert.Single(cues.Markers);
        Assert.Equal(60, cues.Markers[0].Frame);
    }

    [Fact]
    public void ClearAb_KeepsMarkers()
    {
        var cues = new PlayerScratchCues();
        Assert.True(cues.TrySetAbIn(1));
        Assert.True(cues.TrySetAbOut(9));
        Assert.True(cues.TryAddMarker(3, 100));
        cues.ClearAb();
        Assert.Null(cues.AbIn);
        Assert.Null(cues.AbOut);
        Assert.True(cues.HasMarkers);
    }

    [Fact]
    public void ReplaceAb_RestoresPartialOrCompletePair()
    {
        var cues = new PlayerScratchCues();
        cues.ReplaceAb(12, null);
        Assert.Equal(12, cues.AbIn);
        Assert.Null(cues.AbOut);
        Assert.True(cues.LoopRange().IsEmpty);

        cues.ReplaceAb(12, 40);
        Assert.Equal(12, cues.AbIn);
        Assert.Equal(40, cues.AbOut);
        Assert.Equal(new WaveSelection(12, 40), cues.LoopRange());
    }

    [Fact]
    public void MergeMarkers_UnionsFrames_PrefersExistingComment()
    {
        var existing = new MarkerSnapshot[]
        {
            new(10, "keep"),
            new(40, string.Empty),
        };
        var scratch = new MarkerSnapshot[]
        {
            new(10, "scratch"),
            new(40, "from-scratch"),
            new(25, "new"),
        };

        var merged = PlayerScratchCues.MergeMarkers(existing, scratch);
        Assert.Equal(3, merged.Length);
        Assert.Equal(10, merged[0].Frame);
        Assert.Equal("keep", merged[0].Comment);
        Assert.Equal(25, merged[1].Frame);
        Assert.Equal("new", merged[1].Comment);
        Assert.Equal(40, merged[2].Frame);
        Assert.Equal("from-scratch", merged[2].Comment);
    }

    [Fact]
    public void DoubleClickSpanAt_SelectsBetweenMarkers_AndAbLoopWinsInside()
    {
        var cues = new PlayerScratchCues();
        Assert.Equal(new WaveSelection(0, 100), cues.DoubleClickSpanAt(40, 100));

        Assert.True(cues.TryAddMarker(20, 100));
        Assert.True(cues.TryAddMarker(60, 100));
        Assert.Equal(new WaveSelection(0, 20), cues.DoubleClickSpanAt(10, 100));
        Assert.Equal(new WaveSelection(20, 60), cues.DoubleClickSpanAt(40, 100));
        Assert.Equal(new WaveSelection(60, 100), cues.DoubleClickSpanAt(80, 100));

        Assert.True(cues.TrySetAbIn(10));
        Assert.True(cues.TrySetAbOut(90));
        Assert.Equal(new WaveSelection(10, 90), cues.DoubleClickSpanAt(40, 100));
        Assert.Equal(new WaveSelection(0, 10), cues.DoubleClickSpanAt(5, 100));
    }
}
