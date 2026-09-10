using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class RangeDivideTests
{
    [Fact]
    public void MarkerFrames_FirstPressIsStartAndEnd()
    {
        var range = new WaveSelection(10, 90);
        Assert.Equal(new long[] { 10, 90 }, RangeDivide.MarkerFrames(range, 1));
        Assert.Empty(RangeDivide.InteriorFrames(range, 1));
    }

    [Fact]
    public void MarkerFrames_SecondPressAddsCenter()
    {
        var range = new WaveSelection(0, 100);
        Assert.Equal(new long[] { 0, 50, 100 }, RangeDivide.MarkerFrames(range, 2));
        Assert.Equal(new long[] { 50 }, RangeDivide.InteriorFrames(range, 2));
    }

    [Fact]
    public void MarkerFrames_ThreeAndBeyondEqualParts()
    {
        var range = new WaveSelection(0, 80);
        Assert.Equal(new long[] { 0, 80 / 3, 160 / 3, 80 }, RangeDivide.MarkerFrames(range, 3));
        Assert.Equal(new long[] { 0, 20, 40, 60, 80 }, RangeDivide.MarkerFrames(range, 4));
        Assert.Equal(9, RangeDivide.MarkerFrames(range, 8).Length);
        Assert.Equal(10, RangeDivide.MarkerFrames(range, 9).Length);
        Assert.Equal(17, RangeDivide.MarkerFrames(range, 16).Length);
    }

    [Fact]
    public void MarkerFrames_DedupsShortRange()
    {
        var range = new WaveSelection(0, 3);
        var frames = RangeDivide.MarkerFrames(range, 8);
        Assert.Equal(new long[] { 0, 1, 2, 3 }, frames);
        for (var i = 1; i < frames.Length; i++)
        {
            Assert.True(frames[i] > frames[i - 1]);
        }
    }

    [Fact]
    public void NextParts_IncrementsWithoutCap()
    {
        Assert.Equal(1, RangeDivide.NextParts(0));
        Assert.Equal(2, RangeDivide.NextParts(1));
        Assert.Equal(8, RangeDivide.NextParts(7));
        Assert.Equal(9, RangeDivide.NextParts(8));
        Assert.Equal(100, RangeDivide.NextParts(99));
        Assert.Equal(int.MaxValue, RangeDivide.NextParts(int.MaxValue));
    }

    [Fact]
    public void MarkerFrames_PartsBeyondLengthCollapsesToEveryFrame()
    {
        var range = new WaveSelection(0, 10);
        Assert.Equal(11, RangeDivide.MarkerFrames(range, 10).Length);
        Assert.Equal(RangeDivide.MarkerFrames(range, 10), RangeDivide.MarkerFrames(range, 1_000_000));
        Assert.Equal(10, RangeDivide.ResolvePartCount(1_000_000, 10));
    }

    [Fact]
    public void ApplyMarkers_FirstAddsEnds_ThenCenter_ThenReplacesWithThirds()
    {
        var range = new WaveSelection(0, 90);
        var first = RangeDivide.ApplyMarkers([], range, 0, 1);
        Assert.Equal(new long[] { 0, 90 }, first.Select(item => item.Frame).ToArray());

        var second = RangeDivide.ApplyMarkers(first, range, 1, 2);
        Assert.Equal(new long[] { 0, 45, 90 }, second.Select(item => item.Frame).ToArray());

        var third = RangeDivide.ApplyMarkers(second, range, 2, 3);
        Assert.Equal(new long[] { 0, 30, 60, 90 }, third.Select(item => item.Frame).ToArray());
        Assert.DoesNotContain(45, third.Select(item => item.Frame));

        var ninth = RangeDivide.ApplyMarkers(
            RangeDivide.ApplyMarkers(third, range, 3, 8),
            range,
            8,
            9);
        Assert.Equal(10, ninth.Length);
        Assert.Equal(0, ninth[0].Frame);
        Assert.Equal(90, ninth[^1].Frame);
        Assert.Equal(new long[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90 }, ninth.Select(item => item.Frame).ToArray());
    }

    [Fact]
    public void ApplyMarkers_DoesNotStackOnExistingFrame()
    {
        var range = new WaveSelection(10, 40);
        var before = new MarkerSnapshot[] { new(10, "keep") };
        var after = RangeDivide.ApplyMarkers(before, range, 0, 1);
        Assert.Equal(2, after.Length);
        Assert.Equal(10, after[0].Frame);
        Assert.Equal("keep", after[0].Comment);
        Assert.Equal(40, after[1].Frame);
    }

    [Fact]
    public void ApplyMarkers_KeepsUnrelatedMarkers()
    {
        var range = new WaveSelection(20, 80);
        var before = new MarkerSnapshot[] { new(5, "head"), new(90, "tail") };
        var after = RangeDivide.ApplyMarkers(before, range, 0, 2);
        Assert.Equal(new long[] { 5, 20, 50, 80, 90 }, after.Select(item => item.Frame).ToArray());
        Assert.Equal("head", after[0].Comment);
        Assert.Equal("tail", after[4].Comment);
    }

    [Fact]
    public void ApplyRegions_FirstAddsSpan_ThenSplits()
    {
        var range = new WaveSelection(0, 90);
        var first = RangeDivide.ApplyRegions([], range, 0, 1);
        Assert.Equal([new WaveSelection(0, 90)], first.Select(item => item.Range).ToArray());

        var second = RangeDivide.ApplyRegions(first, range, 1, 2);
        Assert.Equal(
            [new WaveSelection(0, 45), new WaveSelection(45, 90)],
            second.Select(item => item.Range).ToArray());

        var third = RangeDivide.ApplyRegions(second, range, 2, 3);
        Assert.Equal(
            [new WaveSelection(0, 30), new WaveSelection(30, 60), new WaveSelection(60, 90)],
            third.Select(item => item.Range).ToArray());
    }

    [Fact]
    public void ApplyRegions_KeepsUnrelatedRegion()
    {
        var range = new WaveSelection(40, 80);
        var before = new WaveRegion[] { new(new WaveSelection(0, 10), "intro") };
        var after = RangeDivide.ApplyRegions(before, range, 0, 1);
        Assert.Equal(2, after.Length);
        Assert.Equal("intro", after[0].Name);
        Assert.Equal(new WaveSelection(40, 80), after[1].Range);
    }

    [Fact]
    public void DivideMarkers_UndoRestoresPrevious()
    {
        var document = MakeDocument(100);
        var history = new EditHistory();
        var range = new WaveSelection(0, 80);
        history.Do(document, ProcessEdits.DivideMarkers(document, range, 0, 1)!);
        history.Do(document, ProcessEdits.DivideMarkers(document, range, 1, 2)!);
        Assert.Equal(new long[] { 0, 40, 80 }, document.Markers.Select(item => item.Frame).ToArray());

        Assert.True(history.Undo(document));
        Assert.Equal(new long[] { 0, 80 }, document.Markers.Select(item => item.Frame).ToArray());
        Assert.True(history.Undo(document));
        Assert.Empty(document.Markers);
    }

    [Fact]
    public void DivideRegions_UndoRestoresPrevious()
    {
        var document = MakeDocument(100);
        var history = new EditHistory();
        var range = new WaveSelection(10, 90);
        history.Do(document, ProcessEdits.DivideRegions(document, range, 0, 1)!);
        history.Do(document, ProcessEdits.DivideRegions(document, range, 1, 2)!);
        Assert.Equal(
            [new WaveSelection(10, 50), new WaveSelection(50, 90)],
            document.Regions);

        Assert.True(history.Undo(document));
        Assert.Equal([new WaveSelection(10, 90)], document.Regions);
    }

    [Fact]
    public void ApplyMarkers_FromBeforeAfter_CoalescesHoldIntoOneUndo()
    {
        var document = MakeDocument(100);
        var history = new EditHistory();
        var range = new WaveSelection(0, 80);
        var before = document.SnapshotMarkers();
        ProcessEdits.DivideMarkers(document, range, 0, 1)!.Apply(document);
        ProcessEdits.DivideMarkers(document, range, 1, 2)!.Apply(document);
        ProcessEdits.DivideMarkers(document, range, 2, 3)!.Apply(document);
        Assert.Equal(new long[] { 0, 80 / 3, 160 / 3, 80 }, document.Markers.Select(item => item.Frame).ToArray());

        var command = ProcessEdits.ApplyMarkers(document, before, document.SnapshotMarkers());
        Assert.NotNull(command);
        history.Do(document, command);
        Assert.Equal(1, history.UndoCount);
        Assert.True(history.Undo(document));
        Assert.Empty(document.Markers);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void ApplyRegions_FromBeforeAfter_CoalescesHoldIntoOneUndo()
    {
        var document = MakeDocument(100);
        var history = new EditHistory();
        var range = new WaveSelection(10, 90);
        var before = document.SnapshotRegions();
        ProcessEdits.DivideRegions(document, range, 0, 1)!.Apply(document);
        ProcessEdits.DivideRegions(document, range, 1, 2)!.Apply(document);
        var command = ProcessEdits.ApplyRegions(
            document,
            before,
            document.SnapshotRegions(),
            range);
        Assert.NotNull(command);
        history.Do(document, command);
        Assert.Equal(1, history.UndoCount);
        Assert.True(history.Undo(document));
        Assert.Empty(document.Regions);
    }

    [Fact]
    public void DivideMarkers_NoOpWhenAllFramesAlreadyPresent()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(0);
        document.TryAddMarker(80);
        Assert.Null(ProcessEdits.DivideMarkers(document, new WaveSelection(0, 80), 0, 1));
    }

    [Fact]
    public void HasMarkersAtEnds_IgnoresRegions()
    {
        var document = MakeDocument(100);
        var range = new WaveSelection(10, 90);
        Assert.False(RangeDivide.HasMarkersAtEnds(document, range));

        document.TryAddMarker(10);
        Assert.False(RangeDivide.HasMarkersAtEnds(document, range));

        document.TryAddMarker(90);
        Assert.True(RangeDivide.HasMarkersAtEnds(document, range));
        Assert.False(RangeDivide.HasMarkersAtEnds(document, WaveSelection.Empty));

        document.SetRegion(new WaveSelection(0, 80));
        Assert.False(RangeDivide.HasMarkersAtEnds(document, new WaveSelection(0, 80)));
    }

    [Fact]
    public void HasRegionsAtEnds_IgnoresMarkers()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(0);
        document.TryAddMarker(80);
        Assert.False(RangeDivide.HasRegionsAtEnds(document, new WaveSelection(0, 80)));

        document.SetRegion(new WaveSelection(0, 80));
        Assert.True(RangeDivide.HasRegionsAtEnds(document, new WaveSelection(0, 80)));
        Assert.False(RangeDivide.HasRegionsAtEnds(document, new WaveSelection(0, 40)));
        Assert.False(RangeDivide.HasRegionsAtEnds(document, WaveSelection.Empty));
    }

    [Fact]
    public void ResolvePreviousParts_SeedsBySameKindOnly()
    {
        var document = MakeDocument(100);
        var range = new WaveSelection(0, 80);
        Assert.Equal(0, RangeDivide.ResolvePreviousParts(null, document, range, regions: false));
        Assert.Equal(0, RangeDivide.ResolvePreviousParts(null, document, range, regions: true));

        document.TryAddMarker(0);
        document.TryAddMarker(80);
        Assert.Equal(1, RangeDivide.ResolvePreviousParts(null, document, range, regions: false));
        Assert.Equal(0, RangeDivide.ResolvePreviousParts(null, document, range, regions: true));

        var state = new RangeDivideState(document, range.StartFrame, range.EndFrame, 3);
        Assert.Equal(3, RangeDivide.ResolvePreviousParts(state, document, range, regions: true));
        Assert.Equal(0, RangeDivide.ResolvePreviousParts(state, document, new WaveSelection(10, 90), regions: false));

        document.SetRegion(new WaveSelection(10, 90));
        Assert.Equal(1, RangeDivide.ResolvePreviousParts(state, document, new WaveSelection(10, 90), regions: true));
        Assert.Equal(0, RangeDivide.ResolvePreviousParts(state, document, new WaveSelection(10, 90), regions: false));
    }

    [Fact]
    public void DivideMarkers_SkipsBothEndsWhenTheyAlreadyExist()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(0);
        document.TryAddMarker(80);
        var range = new WaveSelection(0, 80);
        var previous = RangeDivide.ResolvePreviousParts(null, document, range, regions: false);
        var next = RangeDivide.NextParts(previous);
        ProcessEdits.DivideMarkers(document, range, previous, next)!.Apply(document);
        Assert.Equal(new long[] { 0, 40, 80 }, document.Markers.Select(item => item.Frame).ToArray());
    }

    [Fact]
    public void DivideRegions_PlacesSpanWhenOnlyMarkersBoundTheRange()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(10);
        document.TryAddMarker(90);
        var range = new WaveSelection(10, 90);
        var previous = RangeDivide.ResolvePreviousParts(null, document, range, regions: true);
        var next = RangeDivide.NextParts(previous);
        ProcessEdits.DivideRegions(document, range, previous, next)!.Apply(document);
        Assert.Equal([new WaveSelection(10, 90)], document.Regions);
    }

    [Fact]
    public void DivideRegions_SkipsWholeSpanWhenRegionEndsAlreadyExist()
    {
        var document = MakeDocument(100);
        document.SetRegion(new WaveSelection(10, 90));
        var range = new WaveSelection(10, 90);
        var previous = RangeDivide.ResolvePreviousParts(null, document, range, regions: true);
        var next = RangeDivide.NextParts(previous);
        ProcessEdits.DivideRegions(document, range, previous, next)!.Apply(document);
        Assert.Equal(
            [new WaveSelection(10, 50), new WaveSelection(50, 90)],
            document.Regions);
    }

    private static AudioDocument MakeDocument(int frames) =>
        new(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);
}
