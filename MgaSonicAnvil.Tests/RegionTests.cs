using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class RegionTests
{
    [Fact]
    public void SetRegion_DoesNotAddMarkersOrClearSampleLoop()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(10, 40));
        document.SetRegion(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.Region);
        Assert.Equal(new WaveSelection(10, 40), document.SampleLoop);
        Assert.Empty(document.Markers);
    }

    [Fact]
    public void SetRegion_SameRangeClears()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegion(new WaveSelection(10, 40));
        var command = ProcessEdits.SetRegion(document, new WaveSelection(10, 40));
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.True(document.Region.IsEmpty);
        Assert.Null(ProcessEdits.SetRegion(document, WaveSelection.Empty));
    }

    [Fact]
    public void SetRegion_AllowsOverlapWithSampleLoop()
    {
        var document = MakeDocument(frames: 100);
        var loop = ProcessEdits.SetSampleLoop(document, new WaveSelection(20, 80));
        var region = ProcessEdits.SetRegion(document, new WaveSelection(40, 90));
        Assert.NotNull(loop);
        Assert.NotNull(region);
        new EditHistory().Do(document, loop);
        new EditHistory().Do(document, region);

        Assert.Equal(new WaveSelection(20, 80), document.SampleLoop);
        Assert.Equal(new WaveSelection(40, 90), document.Region);
    }

    [Fact]
    public void DoubleClickSpanAt_InsideRegionSelectsRegion()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegion(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(40));
        Assert.Equal(new WaveSelection(0, 20), document.DoubleClickSpanAt(10));
    }

    [Fact]
    public void DoubleClickSpanAt_SampleLoopWinsWhenOverlapped()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegion(new WaveSelection(10, 90));
        document.SetSampleLoop(new WaveSelection(20, 80));

        Assert.Equal(new WaveSelection(20, 80), document.DoubleClickSpanAt(40));
        Assert.Equal(new WaveSelection(10, 90), document.DoubleClickSpanAt(15));
    }

    [Fact]
    public void AdjacentMarker_IncludesRegionBounds()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegion(new WaveSelection(20, 80));

        Assert.Equal(20, document.AdjacentMarkerFrame(40, -1));
        Assert.Equal(80, document.AdjacentMarkerFrame(40, 1));
    }

    [Fact]
    public void ApplyDelete_ShiftsRegion()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegion(new WaveSelection(20, 60));
        document.ApplyDeleteToRegion(30, 10);

        Assert.Equal(new WaveSelection(20, 50), document.Region);
    }

    [Fact]
    public void Regions_AreNumberedAfterMarkers()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(50, 80), new WaveSelection(10, 30)]);

        Assert.Equal(1, document.RegionNumber(document.Regions[0]));
        Assert.Equal(2, document.RegionNumber(document.Regions[1]));

        document.TryAddMarker(5);
        document.TryAddMarker(40);
        Assert.Equal(1, document.Markers[0].Id);
        Assert.Equal(2, document.Markers[1].Id);
        Assert.Equal(3, document.RegionNumber(document.Regions[0]));
        Assert.Equal(4, document.RegionNumber(document.Regions[1]));
        Assert.Equal(0, document.RegionNumber(new WaveSelection(0, 5)));
    }

    [Fact]
    public void SetRegion_AddsSecondRangeWithoutReplacing()
    {
        var document = MakeDocument(frames: 100);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(10, 30))!);
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(50, 80))!);

        Assert.Equal(
            [new WaveSelection(10, 30), new WaveSelection(50, 80)],
            document.Regions);
    }

    [Fact]
    public void SetRegion_SameRangeClearsOnlyThatRange()
    {
        var document = MakeDocument(frames: 100);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(10, 30))!);
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(50, 80))!);
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(10, 30))!);

        Assert.Equal([new WaveSelection(50, 80)], document.Regions);
    }

    [Fact]
    public void DoubleClickSpanAt_PicksInnermostRegion()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 90), new WaveSelection(20, 40)]);

        Assert.Equal(new WaveSelection(20, 40), document.DoubleClickSpanAt(30));
        Assert.Equal(new WaveSelection(10, 90), document.DoubleClickSpanAt(50));
    }

    [Fact]
    public void ApplyDelete_ShiftsAllRegions()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 25), new WaveSelection(50, 80)]);
        document.ApplyDeleteToRegion(5, 10);

        Assert.Equal(
            [new WaveSelection(5, 15), new WaveSelection(40, 70)],
            document.Regions);
    }

    [Fact]
    public void RemoveRegions_DeletesListedRangesOnly()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70)]);
        var command = ProcessEdits.RemoveRegions(document, [new WaveSelection(10, 30)]);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal([new WaveSelection(50, 70)], document.Regions);
    }

    [Fact]
    public void TryMoveRegions_MovesSelectedAndKeepsOthers()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70)]);

        Assert.True(document.TryMoveRegions([new WaveSelection(10, 30)], 5, out var applied));
        Assert.Equal(5, applied);
        Assert.Equal(
            [new WaveSelection(15, 35), new WaveSelection(50, 70)],
            document.Regions);
    }

    [Fact]
    public void TryMoveRegions_ClampsToDocument()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 30), new WaveSelection(80, 100)]);

        Assert.False(document.TryMoveRegions(
            [new WaveSelection(10, 30), new WaveSelection(80, 100)],
            40,
            out _));
        Assert.False(document.TryMoveRegions([new WaveSelection(80, 100)], 10, out _));
        Assert.True(document.TryMoveRegions([new WaveSelection(10, 30)], -20, out var left));
        Assert.Equal(-10, left);
        Assert.Equal(new WaveSelection(0, 20), document.Regions[0]);
    }

    [Fact]
    public void TryMoveSampleLoop_ClampsToDocument()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(10, 40));

        Assert.True(document.TryMoveSampleLoop(-20, out var left));
        Assert.Equal(-10, left);
        Assert.Equal(new WaveSelection(0, 30), document.SampleLoop);
        Assert.False(document.TryMoveSampleLoop(-5, out _));
        Assert.True(document.TryMoveSampleLoop(20, out var right));
        Assert.Equal(20, right);
        Assert.Equal(new WaveSelection(20, 50), document.SampleLoop);
    }

    [Fact]
    public void TryMoveRegionEdges_MovesStartOnly()
    {
        var document = MakeDocument(frames: 100);
        var region = new WaveSelection(20, 50);
        document.SetRegions([region]);

        Assert.True(document.TryMoveRegionEdges([RangeEdgeMove.FromStart(region)], 10, out var applied));
        Assert.Equal(10, applied);
        Assert.Equal(new WaveSelection(30, 50), document.Regions[0]);
    }

    [Fact]
    public void TryMoveRegionEdges_MovesEndOnly()
    {
        var document = MakeDocument(frames: 100);
        var region = new WaveSelection(20, 50);
        document.SetRegions([region]);

        Assert.True(document.TryMoveRegionEdges([RangeEdgeMove.FromEnd(region)], -10, out var applied));
        Assert.Equal(-10, applied);
        Assert.Equal(new WaveSelection(20, 40), document.Regions[0]);
    }

    [Fact]
    public void TryMoveRegionEdges_KeepsAtLeastOneFrame()
    {
        var document = MakeDocument(frames: 100);
        var region = new WaveSelection(20, 25);
        document.SetRegions([region]);

        Assert.True(document.TryMoveRegionEdges([RangeEdgeMove.FromStart(region)], 20, out var applied));
        Assert.Equal(4, applied);
        Assert.Equal(new WaveSelection(24, 25), document.Regions[0]);
        Assert.False(document.TryMoveRegionEdges([RangeEdgeMove.FromEnd(document.Regions[0])], -10, out _));
    }

    [Fact]
    public void TryMoveSampleLoopEdges_MovesOneEnd()
    {
        var document = MakeDocument(frames: 100);
        document.SetSampleLoop(new WaveSelection(10, 40));

        Assert.True(document.TryMoveSampleLoopEdges(start: true, end: false, 5, out var applied));
        Assert.Equal(5, applied);
        Assert.Equal(new WaveSelection(15, 40), document.SampleLoop);
    }

    [Fact]
    public void TimelineMoves_Resolve_UsesTightestLimit()
    {
        var markers = new long[] { 10 };
        var occupied = new HashSet<long> { 25 };
        var ranges = new[] { new WaveSelection(5, 40) };

        Assert.Equal(20, TimelineMoves.Resolve(markers, occupied, ranges, 20, 100));
        Assert.Equal(14, TimelineMoves.Resolve(markers, occupied, ranges, 15, 100));
        Assert.Equal(10, TimelineMoves.Resolve([], occupied, [new WaveSelection(80, 90)], 40, 100));
        Assert.Equal(15, TimelineMoves.Resolve(markers, [], Array.Empty<WaveSelection>(), 15, 100));
        Assert.Equal(0, TimelineMoves.Resolve(markers, [], [new WaveSelection(90, 100)], 20, 100));
    }

    [Fact]
    public void TrySetRegionName_StoresAndPreservesAcrossMove()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 40)]);
        Assert.True(document.TrySetRegionName(new WaveSelection(10, 40), "verse"));
        Assert.Equal("verse", document.RegionName(new WaveSelection(10, 40)));

        Assert.True(document.TryMoveRegions([new WaveSelection(10, 40)], 5, out _));
        Assert.Equal("verse", document.RegionName(new WaveSelection(15, 45)));
        Assert.Equal(string.Empty, document.RegionName(new WaveSelection(10, 40)));
    }

    [Fact]
    public void SetRegionName_UndoRestoresPrevious()
    {
        var document = MakeDocument(frames: 100);
        document.SetRegions([new WaveSelection(10, 40)]);
        var history = new EditHistory();
        var command = ProcessEdits.SetRegionName(document, new WaveSelection(10, 40), "chorus");
        Assert.NotNull(command);
        history.Do(document, command);
        Assert.Equal("chorus", document.RegionName(new WaveSelection(10, 40)));

        Assert.True(history.Undo(document));
        Assert.Equal(string.Empty, document.RegionName(new WaveSelection(10, 40)));
    }

    [Fact]
    public void FlagsOverlapX_DetectsHorizontalOverlap()
    {
        Assert.True(WaveformView.FlagsOverlapX(10, 20, 20, 10));
        Assert.True(WaveformView.FlagsOverlapX(10, 20, 10, 20));
        Assert.False(WaveformView.FlagsOverlapX(10, 20, 30, 10));
        Assert.False(WaveformView.FlagsOverlapX(40, 10, 10, 20));
        Assert.True(WaveformView.FlagsOverlapX(10, 20, 30, 10, pad: 1));
        Assert.True(WaveformView.FlagsOverlapX(10, 20, 32, 8, pad: WaveformView.FlagProximityPad));
    }

    [Fact]
    public void PackFlagRow_ShortRegionKeepsBothStemsVisible()
    {
        var packed = WaveformView.PackFlagRow(
        [
            new WaveformView.PackedTimelineFlag(10, 20, GrowLeft: false),
            new WaveformView.PackedTimelineFlag(18, 20, GrowLeft: true),
        ]);
        Assert.Equal(2, packed.Length);
        Assert.True(packed[0].Right <= packed[1].Left + 0.001);
        Assert.True(packed[0].Width >= 1);
        Assert.True(packed[1].Width >= 1);
        Assert.Equal(10, packed[0].StemX);
        Assert.Equal(18, packed[1].StemX);
    }

    [Fact]
    public void PackFlagRow_CloseRightGrowingFlagsDoNotCoverNextStem()
    {
        var packed = WaveformView.PackFlagRow(
        [
            new WaveformView.PackedTimelineFlag(10, 20, GrowLeft: false),
            new WaveformView.PackedTimelineFlag(16, 20, GrowLeft: false),
        ]);
        Assert.True(packed[0].Right <= packed[1].StemX);
        Assert.Equal(20, packed[1].Width);
    }

    [Fact]
    public void ChainFlagRow_CloseMarkersKeepFullWidthAndLineUp()
    {
        var packed = WaveformView.ChainFlagRow(
        [
            new WaveformView.PackedTimelineFlag(10, 20, GrowLeft: false),
            new WaveformView.PackedTimelineFlag(16, 20, GrowLeft: false),
            new WaveformView.PackedTimelineFlag(16, 18, GrowLeft: false),
        ]);
        Assert.Equal(20, packed[0].Width);
        Assert.Equal(20, packed[1].Width);
        Assert.Equal(18, packed[2].Width);
        Assert.Equal(10, packed[0].Left);
        Assert.Equal(packed[0].Right, packed[1].Left);
        Assert.Equal(packed[1].Right, packed[2].Left);
        Assert.Equal(10, packed[0].StemX);
        Assert.Equal(16, packed[1].StemX);
        Assert.Equal(16, packed[2].StemX);
    }

    [Fact]
    public void ChainFlagRow_FarMarkersStayOnStem()
    {
        var packed = WaveformView.ChainFlagRow(
        [
            new WaveformView.PackedTimelineFlag(10, 20, GrowLeft: false),
            new WaveformView.PackedTimelineFlag(80, 16, GrowLeft: false),
        ]);
        Assert.Equal(0, packed[0].Offset);
        Assert.Equal(0, packed[1].Offset);
        Assert.Equal(10, packed[0].Left);
        Assert.Equal(80, packed[1].Left);
    }

    [Fact]
    public void PackFlagRow_AdjacentEndAndStartGrowAway()
    {
        var packed = WaveformView.PackFlagRow(
        [
            new WaveformView.PackedTimelineFlag(50, 20, GrowLeft: true),
            new WaveformView.PackedTimelineFlag(50, 20, GrowLeft: false),
        ]);
        Assert.True(packed[0].Right <= packed[1].Left);
        Assert.Equal(20, packed[0].Width);
        Assert.Equal(20, packed[1].Width);
    }

    [Fact]
    public void SplitFlagRect_PutsRegionAboveMarker()
    {
        var region = WaveformView.SplitFlagRect(8, 16, laneHeight: 32, top: true);
        var marker = WaveformView.SplitFlagRect(8, 16, laneHeight: 32, top: false);
        Assert.True(region.Bottom <= marker.Top);
        Assert.True(region.Y < marker.Y);
        Assert.Equal(8, region.X);
        Assert.Equal(8, marker.X);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 2)]
    public void CountFlagLaneRows_MatchesContent(bool hasMarkers, bool hasRegions, int rows)
    {
        Assert.Equal(rows, WaveformView.CountFlagLaneRows(hasMarkers, hasRegions));
    }

    [Fact]
    public void MarkerLaneHeightForRows_SingleLaneIsHalfOfTwo()
    {
        Assert.Equal(0, WaveformView.MarkerLaneHeightForRows(0));
        Assert.Equal(DesignMetrics.MarkerLaneRowHeight, WaveformView.MarkerLaneHeightForRows(1));
        Assert.Equal(DesignMetrics.MarkerLaneHeight, WaveformView.MarkerLaneHeightForRows(2));
        Assert.Equal(DesignMetrics.MarkerLaneHeight, WaveformView.MarkerLaneHeightForRows(1) * 2);
    }

    [Fact]
    public void LaneFlagRect_UsesFullLaneWhenNotSplit()
    {
        var full = WaveformView.LaneFlagRect(8, 16, laneHeight: 32, split: false, top: true);
        var marker = WaveformView.LaneFlagRect(8, 16, laneHeight: 32, split: false, top: false);
        Assert.Equal(full, marker);
        Assert.True(full.Height > 20);
        Assert.Equal(1, full.Y);
    }

    [Fact]
    public void LaneFlagRect_SplitsWhenBothKindsPresent()
    {
        var region = WaveformView.LaneFlagRect(8, 16, laneHeight: 32, split: true, top: true);
        var marker = WaveformView.LaneFlagRect(8, 16, laneHeight: 32, split: true, top: false);
        Assert.True(region.Bottom <= marker.Top);
        Assert.True(region.Height < 20);
        Assert.True(marker.Height < 20);
    }

    [Fact]
    public void MoveTimelineItems_UndoRestoresMarkersRegionsAndLoop()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(10);
        document.SetRegions([new WaveSelection(20, 40)]);
        document.SetSampleLoop(new WaveSelection(50, 70));
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;

        Assert.True(document.TryMoveMarkers([10], 5, out _));
        Assert.True(document.TryMoveRegions([new WaveSelection(20, 40)], 5, out _));
        Assert.True(document.TryMoveSampleLoop(5, out _));

        var command = ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);
        var history = new EditHistory();
        history.Do(document, command);
        Assert.Equal(15, document.Markers[0].Frame);
        Assert.Equal(new WaveSelection(25, 45), document.Regions[0]);
        Assert.Equal(new WaveSelection(55, 75), document.SampleLoop);

        history.Undo(document);
        Assert.Equal(10, document.Markers[0].Frame);
        Assert.Equal(new WaveSelection(20, 40), document.Regions[0]);
        Assert.Equal(new WaveSelection(50, 70), document.SampleLoop);
    }

    private static AudioDocument MakeDocument(int frames)
    {
        return new AudioDocument(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
