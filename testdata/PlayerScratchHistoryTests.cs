using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PlayerScratchHistoryTests
{
    [Fact]
    public void UndoRedo_RestoresMarkersAndAb()
    {
        var cues = new PlayerScratchCues();
        var history = new PlayerScratchHistory();

        Assert.True(cues.TryAddMarker(100, 1000));

        var beforeSecond = PlayerScratchSnapshot.Capture(cues);
        Assert.True(cues.TryAddMarker(200, 1000));
        history.Push(beforeSecond, cues);

        var beforeAb = PlayerScratchSnapshot.Capture(cues);
        Assert.True(cues.TrySetAbIn(10));
        Assert.True(cues.TrySetAbOut(50));
        history.Push(beforeAb, cues);

        Assert.Equal(2, cues.Markers.Count);
        Assert.Equal(10, cues.AbIn);
        Assert.Equal(50, cues.AbOut);
        Assert.True(history.CanUndo);

        Assert.True(history.TryUndo(cues));
        Assert.Equal(2, cues.Markers.Count);
        Assert.Null(cues.AbIn);
        Assert.Null(cues.AbOut);

        Assert.True(history.TryUndo(cues));
        Assert.Single(cues.Markers);
        Assert.Equal(100, cues.Markers[0].Frame);

        Assert.True(history.TryRedo(cues));
        Assert.Equal(2, cues.Markers.Count);

        Assert.True(history.TryRedo(cues));
        Assert.Equal(10, cues.AbIn);
        Assert.Equal(50, cues.AbOut);

        history.Clear();
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_IgnoresUnchangedSnapshot()
    {
        var cues = new PlayerScratchCues();
        var history = new PlayerScratchHistory();
        Assert.True(cues.TryAddMarker(10, 100));
        var snap = PlayerScratchSnapshot.Capture(cues);
        history.Push(snap, cues);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void PushBefore_ThenMutate_UndoesToPrior()
    {
        var cues = new PlayerScratchCues();
        var history = new PlayerScratchHistory();
        Assert.True(cues.TrySetAbIn(5));
        history.PushBefore(cues);
        cues.ClearAb();
        Assert.True(history.TryUndo(cues));
        Assert.Equal(5, cues.AbIn);
    }
}
