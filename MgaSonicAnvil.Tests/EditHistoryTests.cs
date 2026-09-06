using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class EditHistoryTests
{
    [Fact]
    public void Snapshot_StartsWithOriginAndFollowsUndoRedo()
    {
        var document = MakeConstant(8, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 8)));
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 8)));

        var items = history.Snapshot();
        Assert.Equal(3, items.Count);
        Assert.Equal(UiStrings.EditHistoryOrigin, items[0].Name);
        Assert.Equal("Normalize", items[1].Name);
        Assert.Equal("Fade In", items[2].Name);
        Assert.Equal(2, history.CurrentIndex);

        history.Undo(document);
        Assert.Equal(1, history.CurrentIndex);
        Assert.Equal(3, history.Snapshot().Count);
        Assert.Equal("Fade In", history.Snapshot()[2].Name);
    }

    [Fact]
    public void JumpTo_RestoresEarlierAndLaterStates()
    {
        var document = MakeConstant(8, 1f);
        var original = document.Interleaved[0];
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 8)));
        var faded = document.Interleaved[^1];
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 8)));

        Assert.True(history.JumpTo(document, 0));
        Assert.Equal(original, document.Interleaved[0], 5);
        Assert.Equal(0, history.CurrentIndex);

        Assert.True(history.JumpTo(document, 1));
        Assert.Equal(faded, document.Interleaved[^1], 5);
        Assert.Equal(1, history.CurrentIndex);

        Assert.True(history.JumpTo(document, 2));
        Assert.Equal(2, history.CurrentIndex);
        Assert.False(history.JumpTo(document, 2));
    }

    [Fact]
    public void MoveTimelineItems_SummaryIncludesMarkerLoopAndRegionShifts()
    {
        var document = MakeConstant(48000, 1f);
        document.TryAddMarker(0);
        document.TrySetMarkerComment(0, "-L");
        document.SetRegions([new WaveSelection(4800, 9600)]);
        document.SetSampleLoop(new WaveSelection(24000, 36000));
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;

        Assert.True(document.TryMoveMarkers([0], 4800, out _));
        Assert.True(document.TryMoveRegionEdges([RangeEdgeMove.FromStart(regionsBefore[0].Range)], 2400, out _));
        Assert.True(document.TryMoveSampleLoopEdges(start: false, end: true, 4800, out _));

        var command = ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);
        Assert.Equal(
            "マーカー移動  00:00.000→00:00.100  -L  ループ終了  00:00.750→00:00.850  リージョン開始  00:00.100→00:00.150",
            command.Summary);
    }

    [Fact]
    public void MoveTimelineItems_SummaryDescribesSingleLoopStart()
    {
        var document = MakeConstant(48000, 1f);
        document.SetSampleLoop(new WaveSelection(4800, 24000));
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;
        Assert.True(document.TryMoveSampleLoopEdges(start: true, end: false, 4800, out _));

        var command = ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);
        Assert.Equal("ループ開始  00:00.100→00:00.200", command.Summary);
    }

    [Fact]
    public void MoveTimelineItems_SummaryDescribesWholeLoopMove()
    {
        var document = MakeConstant(48000, 1f);
        document.SetSampleLoop(new WaveSelection(4800, 14400));
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;
        Assert.True(document.TryMoveSampleLoop(4800, out _));

        var command = ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);
        Assert.Equal("ループ移動  00:00.100–00:00.300→00:00.200–00:00.400", command.Summary);
    }

    [Fact]
    public void Snapshot_TitleIncludesActionAndTimeRange()
    {
        var document = MakeConstant(48000, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 48000)));
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 4800), FadeShape.Linear));
        history.Do(document, ProcessEdits.Delete(document, new WaveSelection(24000, 36000)));

        var items = history.Snapshot();
        Assert.Equal("Normalize", items[1].Name);
        Assert.Equal("フェードイン  00:00.000–00:00.100  直線", items[2].Title);
        Assert.Equal("削除  00:00.500–00:00.750", items[3].Title);
        Assert.Contains("ノーマライズ", items[1].Title);
        Assert.Contains("00:00.000", items[1].Title);
        Assert.Contains("00:01.000", items[1].Title);
    }

    [Fact]
    public void UndoToOrigin_ClearsDirty()
    {
        var document = MakeConstant(8, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 8)));
        Assert.True(document.IsDirty);

        Assert.True(history.Undo(document));
        Assert.False(document.IsDirty);
        Assert.True(history.IsClean);
    }

    [Fact]
    public void JumpToOrigin_ClearsDirty()
    {
        var document = MakeConstant(8, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 8)));
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 8)));
        Assert.True(document.IsDirty);

        Assert.True(history.JumpTo(document, 0));
        Assert.False(document.IsDirty);
    }

    [Fact]
    public void UndoPastSave_MarksDirtyAgain()
    {
        var document = MakeConstant(8, 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 8)));
        document.MarkSaved("saved.wav", AudioFileKind.Wave);
        history.MarkClean();
        Assert.False(document.IsDirty);

        Assert.True(history.Undo(document));
        Assert.True(document.IsDirty);

        Assert.True(history.Redo(document));
        Assert.False(document.IsDirty);
    }

    private static AudioDocument MakeConstant(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
