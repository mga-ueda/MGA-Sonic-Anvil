using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SampleLoopTests
{
    [Fact]
    public void SetSampleLoop_DoesNotAddMarkers()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.SampleLoop);
        Assert.Empty(document.Markers);
    }

    [Fact]
    public void SetSampleLoop_MarksDocumentDirty()
    {
        var document = MakeDocument(frames: 100);
        Assert.False(document.IsDirty);

        document.SetSampleLoop(new WaveSelection(20, 80));
        Assert.True(document.IsDirty);

        document.MarkSaved("loop.wav", AudioFileKind.Wave);
        document.SetSampleLoop(new WaveSelection(20, 80));
        Assert.False(document.IsDirty);

        document.SetSampleLoop(WaveSelection.Empty, markDirty: false);
        Assert.False(document.IsDirty);
        Assert.True(document.SampleLoop.IsEmpty);
    }

    [Fact]
    public void AdjacentMarker_IncludesSampleLoopBounds()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(20, document.AdjacentMarkerFrame(40, -1));
        Assert.Equal(80, document.AdjacentMarkerFrame(40, 1));
        Assert.Equal(0, document.AdjacentMarkerFrame(20, -1));
        Assert.Equal(80, document.AdjacentMarkerFrame(20, 1));
        Assert.Equal(20, document.AdjacentMarkerFrame(80, -1));
        Assert.Equal(100, document.AdjacentMarkerFrame(80, 1));
    }

    [Fact]
    public void AdjacentMarker_MixesMarkersAndSampleLoop()
    {
        var document = MakeDocument(frames: 200);
        document.TryAddMarker(40);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(20, document.AdjacentMarkerFrame(0, 1));
        Assert.Equal(40, document.AdjacentMarkerFrame(20, 1));
        Assert.Equal(80, document.AdjacentMarkerFrame(40, 1));
        Assert.Equal(40, document.AdjacentMarkerFrame(80, -1));
        Assert.Equal(20, document.AdjacentMarkerFrame(40, -1));
    }

    [Fact]
    public void AdjacentMarker_IgnoresDuplicateLoopOnMarker()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(20);
        document.TryAddMarker(60);
        document.SetSampleLoop(new WaveSelection(20, 60));

        Assert.Equal(20, document.AdjacentMarkerFrame(40, -1));
        Assert.Equal(60, document.AdjacentMarkerFrame(40, 1));
        Assert.Equal(0, document.AdjacentMarkerFrame(20, -1));
        Assert.Equal(100, document.AdjacentMarkerFrame(60, 1));
    }

    [Fact]
    public void ApplyDelete_ShiftsAndTrimsSampleLoop()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 60));
        document.ApplyDeleteToSampleLoop(30, 10);

        Assert.Equal(new WaveSelection(20, 50), document.SampleLoop);
    }

    [Fact]
    public void ApplyInsert_ShiftsSampleLoop()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 60));
        document.ApplyInsertToSampleLoop(20, 10);

        Assert.Equal(new WaveSelection(30, 70), document.SampleLoop);
    }

    [Fact]
    public void ApplyDelete_ClearsSampleLoopWhenFullyRemoved()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 40));
        document.ApplyDeleteToSampleLoop(10, 40);

        Assert.True(document.SampleLoop.IsEmpty);
    }

    [Fact]
    public void DeleteUndo_RestoresSampleLoop()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 80));
        document.Selection = new WaveSelection(30, 40);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Delete(document, document.Selection));

        Assert.Equal(new WaveSelection(20, 70), document.SampleLoop);
        Assert.True(history.Undo(document));
        Assert.Equal(new WaveSelection(20, 80), document.SampleLoop);
        Assert.Empty(document.Markers);
    }

    [Fact]
    public void SetSampleLoop_UndoRestoresPrevious()
    {
        var document = MakeDocument(frames: 100);
        var history = new EditHistory();
        var command = ProcessEdits.SetSampleLoop(document, new WaveSelection(10, 50));
        Assert.NotNull(command);
        history.Do(document, command);
        Assert.Equal(new WaveSelection(10, 50), document.SampleLoop);

        var again = ProcessEdits.SetSampleLoop(document, new WaveSelection(40, 90));
        Assert.NotNull(again);
        history.Do(document, again);
        Assert.Equal(new WaveSelection(40, 90), document.SampleLoop);

        Assert.True(history.Undo(document));
        Assert.Equal(new WaveSelection(10, 50), document.SampleLoop);
        Assert.True(history.Undo(document));
        Assert.True(document.SampleLoop.IsEmpty);
    }

    [Fact]
    public void SetSampleLoop_SameRangeClears()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(10, 40));
        var command = ProcessEdits.SetSampleLoop(document, new WaveSelection(10, 40));
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.True(document.SampleLoop.IsEmpty);
        Assert.Null(ProcessEdits.SetSampleLoop(document, WaveSelection.Empty));
    }

    [Fact]
    public void DoubleClickSpanAt_InsideSampleLoopSelectsLoop()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(10);
        document.TryAddMarker(90);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(40));
        Assert.Equal(new WaveSelection(10, 20), document.DoubleClickSpanAt(15));
        Assert.Equal(new WaveSelection(80, 90), document.DoubleClickSpanAt(85));
        Assert.Equal(new WaveSelection(90, 100), document.DoubleClickSpanAt(95));
    }

    [Fact]
    public void DoubleClickSpanAt_MarkerSpanInsideLoopExpandsToLoop()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(30);
        document.TryAddMarker(50);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(40));
        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(30));
    }

    [Fact]
    public void DoubleClickSpanAt_WithoutMarkersSelectsLoopWhenInside()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(40));
        Assert.Equal(new WaveSelection(0, 20), document.DoubleClickSpanAt(10));
        Assert.Equal(new WaveSelection(80, 100), document.DoubleClickSpanAt(90));
    }

    [Fact]
    public void SampleLoopBar_HitTestUsesVisibleRect()
    {
        var lane = new System.Windows.Rect(40, 10, 200, 16);
        Assert.True(MarkerRolePaint.TryGetVisibleRangeRect(
            new WaveSelection(20, 80),
            lane,
            viewStart: 0,
            viewSpan: 100,
            out var bar));
        Assert.True(bar.Contains(new System.Windows.Point(bar.X + 2, lane.Y + 4)));
        Assert.False(bar.Contains(new System.Windows.Point(lane.X + 2, lane.Y + 4)));
        Assert.False(MarkerRolePaint.TryGetVisibleRangeRect(
            WaveSelection.Empty,
            lane,
            viewStart: 0,
            viewSpan: 100,
            out _));
    }

    private static AudioDocument MakeDocument(int frames)
    {
        return new AudioDocument(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
