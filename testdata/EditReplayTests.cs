using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>履歴レシピ（コピー＆ペースト）の別ドキュメント再適用。</summary>
public sealed class EditReplayTests
{
    [Fact]
    public void FadeIn_Replay_MapsRangeAcrossSampleRates()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var command = ProcessEdits.FadeIn(source, new WaveSelection(0, 100), FadeShape.Linear);
        Assert.NotNull(command.Replay);

        // 半分のレートなら同じ時間 = 半分のフレーム数に写像される。
        var target = MakeConstant(frames: 100, value: 1f, rate: 24000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        var history = new EditHistory();
        history.Do(target, replayed!);

        Assert.Equal(0f, target.Interleaved[0], 5);
        Assert.True(target.Interleaved[25 * 2] is > 0.2f and < 0.8f);
        Assert.Equal(1f, target.Interleaved[80 * 2], 5);
    }

    [Fact]
    public void Normalize_Replay_RecomputesGainFromTargetPeak()
    {
        var source = MakeConstant(frames: 50, value: 0.5f, rate: 48000);
        var command = ProcessEdits.Normalize(source, new WaveSelection(0, 50));

        var target = MakeConstant(frames: 50, value: 0.25f, rate: 48000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);

        // -0.1 dB ターゲットへ適用先のピークから再計算される。
        var expected = (float)Math.Pow(10d, -0.1d / 20d);
        Assert.Equal(expected, target.Interleaved[0], 4);
    }

    [Fact]
    public void Gain_Replay_AppliesSameDbToTarget()
    {
        var source = MakeConstant(frames: 50, value: 0.5f, rate: 48000);
        var command = ProcessEdits.Gain(source, new WaveSelection(0, 50), -6);
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var target = MakeConstant(frames: 50, value: 0.8f, rate: 48000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);

        var expected = 0.8f * (float)Math.Pow(10d, -6d / 20d);
        Assert.Equal(expected, target.Interleaved[0], 4);
    }

    [Fact]
    public void PitchShift_Replay_AppliesSameSemitonesToTarget()
    {
        var source = MakeSine(frames: 2048, rate: 48000);
        var command = ProcessEdits.PitchShift(source, new WaveSelection(0, 2048), 12);
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var target = MakeSine(frames: 2048, rate: 48000);
        var before = (float[])target.Interleaved.Clone();
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.NotEqual(before, target.Interleaved);
    }

    [Fact]
    public void Reverse_Replay_FlipsMappedRangeOnTarget()
    {
        var source = MakeRamp(frames: 8, rate: 48000);
        var command = ProcessEdits.Reverse(source, new WaveSelection(2, 6));
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var target = MakeRamp(frames: 8, rate: 48000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);

        Assert.Equal(0f, target.Interleaved[0]);
        Assert.Equal(5f, target.Interleaved[4]);
        Assert.Equal(4f, target.Interleaved[6]);
        Assert.Equal(3f, target.Interleaved[8]);
        Assert.Equal(2f, target.Interleaved[10]);
        Assert.Equal(6f, target.Interleaved[12]);
    }

    [Fact]
    public void TimeStretch_Replay_AppliesSameRatioToTarget()
    {
        var source = MakeSine(frames: 48000, rate: 48000);
        var command = ProcessEdits.TimeStretch(source, new WaveSelection(0, 48000), 24000);
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var target = MakeSine(frames: 48000, rate: 48000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Equal(24000, target.FrameCount);
    }

    [Fact]
    public void Delete_Replay_ClampsRangeToTargetLength()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var command = ProcessEdits.Delete(source, new WaveSelection(50, 100));

        var target = MakeConstant(frames: 60, value: 1f, rate: 48000);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);

        Assert.Equal(50, target.FrameCount);
    }

    [Fact]
    public void Delete_Replay_ReturnsNullWhenRangeFallsOutside()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var command = ProcessEdits.Delete(source, new WaveSelection(50, 100));

        // 対象が短すぎて範囲が空になる場合は適用しない。
        var target = MakeConstant(frames: 40, value: 1f, rate: 48000);
        Assert.Null(command.Replay!(target));
    }

    [Fact]
    public void Snapshot_MarksReplayableEntries()
    {
        var document = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 100)));

        var snapshot = history.Snapshot();
        Assert.False(snapshot[0].CanReplay);
        Assert.True(snapshot[1].CanReplay);
        Assert.NotNull(history.ReplayAt(1));
        Assert.Null(history.ReplayAt(0));
        Assert.Null(history.ReplayAt(2));
    }

    [Fact]
    public void MoveTimelineItems_Replay_AppliesAfterStateToTarget()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.TryAddMarker(10);
        source.SetRegions([new WaveSelection(20, 40)]);
        source.SetSampleLoop(new WaveSelection(50, 70));
        var markersBefore = source.SnapshotMarkers();
        var regionsBefore = source.SnapshotRegions();
        var loopBefore = source.SampleLoop;
        Assert.True(source.TryMoveMarkers([10], 5, out _));
        Assert.True(source.TryMoveRegions([new WaveSelection(20, 40)], 5, out _));
        Assert.True(source.TryMoveSampleLoop(5, out _));
        var command = ProcessEdits.MoveTimelineItems(source, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var history = new EditHistory();
        history.Do(source, command);
        var snapshot = history.Snapshot();
        Assert.True(snapshot[1].CanReplay);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        target.TryAddMarker(10);
        target.SetRegions([new WaveSelection(20, 40)]);
        target.SetSampleLoop(new WaveSelection(50, 70));
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Equal(15, target.Markers[0].Frame);
        Assert.Equal(new WaveSelection(25, 45), target.Regions[0]);
        Assert.Equal(new WaveSelection(55, 75), target.SampleLoop);
    }

    [Fact]
    public void MoveTimelineItems_Replay_MapsFramesAcrossSampleRates()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.TryAddMarker(10);
        var markersBefore = source.SnapshotMarkers();
        var regionsBefore = source.SnapshotRegions();
        var loopBefore = source.SampleLoop;
        Assert.True(source.TryMoveMarkers([10], 10, out _));
        var command = ProcessEdits.MoveTimelineItems(source, markersBefore, regionsBefore, loopBefore);
        Assert.NotNull(command);

        var target = MakeConstant(frames: 50, value: 1f, rate: 24000);
        var replayed = command!.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Equal(10, target.Markers[0].Frame);
    }

    [Fact]
    public void AddMarker_Replay_SkipsWhenMarkerExists()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var command = ProcessEdits.AddMarker(source, 10);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var first = command.Replay!(target);
        Assert.NotNull(first);
        new EditHistory().Do(target, first!);
        Assert.True(target.HasMarkerAt(10));

        Assert.Null(command.Replay!(target));
    }

    [Fact]
    public void ClearAllMarkers_Replay_RemovesEveryMarkerOnTarget()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.TryAddMarker(10);
        source.TryAddMarker(40);
        var command = ProcessEdits.ClearAllMarkers(source);
        Assert.NotNull(command);
        Assert.Equal(HistoryRecipes.ClearAllMarkers, command!.Persist?.Kind);

        var target = MakeConstant(frames: 200, value: 1f, rate: 48000);
        target.TryAddMarker(5);
        target.TryAddMarker(80);
        target.TryAddMarker(150);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Empty(target.Markers);
    }

    [Fact]
    public void RemoveMarkers_Replay_KeepsUnlistedMarkersOnTarget()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.TryAddMarker(10);
        source.TryAddMarker(40);
        source.TryAddMarker(70);
        var command = ProcessEdits.RemoveMarkers(source, [10, 40]);
        Assert.NotNull(command);
        Assert.Equal(HistoryRecipes.RemoveMarkers, command!.Persist?.Kind);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        target.TryAddMarker(10);
        target.TryAddMarker(40);
        target.TryAddMarker(70);
        target.TryAddMarker(90);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Equal(new long[] { 70, 90 }, target.Markers.Select(marker => marker.Frame).ToArray());
    }

    [Fact]
    public void ClearAllRegions_Replay_RemovesEveryRegionOnTarget()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70)]);
        var command = ProcessEdits.ClearAllRegions(source);
        Assert.NotNull(command);
        Assert.Equal(HistoryRecipes.ClearAllRegions, command!.Persist?.Kind);

        var target = MakeConstant(frames: 200, value: 1f, rate: 48000);
        target.SetRegions(
        [
            new WaveSelection(0, 20),
            new WaveSelection(40, 60),
            new WaveSelection(120, 180),
        ]);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Empty(target.Regions);
    }

    [Fact]
    public void RemoveRegions_Replay_KeepsUnlistedRegionsOnTarget()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70)]);
        var command = ProcessEdits.RemoveRegions(source, [new WaveSelection(10, 30)]);
        Assert.NotNull(command);
        Assert.Equal(HistoryRecipes.RemoveRegions, command!.Persist?.Kind);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        target.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70), new WaveSelection(80, 95)]);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        new EditHistory().Do(target, replayed!);
        Assert.Equal(
            [new WaveSelection(50, 70), new WaveSelection(80, 95)],
            target.Regions);
    }

    [Fact]
    public void SetSampleLoop_Recipe_RestoresWithReplay()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var command = ProcessEdits.SetSampleLoop(source, new WaveSelection(10, 40));
        Assert.NotNull(command);
        Assert.NotNull(command!.Persist);

        var restored = HistoryRecipes.TryCreate(
            MakeConstant(frames: 100, value: 1f, rate: 48000),
            command.Persist!);
        Assert.NotNull(restored);
        Assert.NotNull(restored!.Replay);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        var replayed = restored.Replay!(target);
        Assert.NotNull(replayed);
        Assert.NotNull(replayed!.Replay);
        new EditHistory().Do(target, replayed);
        Assert.Equal(new WaveSelection(10, 40), target.SampleLoop);
    }

    [Fact]
    public void SetRegion_ClearAll_ReplayResultStaysCopyable()
    {
        var source = MakeConstant(frames: 100, value: 1f, rate: 48000);
        source.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 70)]);
        var command = ProcessEdits.SetRegion(source, WaveSelection.Empty);
        Assert.NotNull(command);
        Assert.NotNull(command!.Replay);

        var target = MakeConstant(frames: 100, value: 1f, rate: 48000);
        target.SetRegions([new WaveSelection(5, 20)]);
        var replayed = command.Replay!(target);
        Assert.NotNull(replayed);
        Assert.NotNull(replayed!.Replay);

        var imported = MakeConstant(frames: 100, value: 1f, rate: 48000);
        imported.SetRegions([new WaveSelection(8, 24)]);
        var restored = HistoryRecipes.TryCreate(imported, command.Persist!);
        Assert.NotNull(restored);
        Assert.NotNull(restored!.Replay);
    }

    [Fact]
    public void ClearAll_Recipe_RebuildsAsClearAllOnTarget()
    {
        var target = MakeConstant(frames: 80, value: 1f, rate: 48000);
        target.TryAddMarker(12);
        target.TryAddMarker(36);
        target.SetRegions([new WaveSelection(4, 16), new WaveSelection(40, 60)]);

        var markers = HistoryRecipes.TryCreate(target, new HistoryRecipe { Kind = HistoryRecipes.ClearAllMarkers });
        Assert.NotNull(markers);
        new EditHistory().Do(target, markers!);
        Assert.Empty(target.Markers);

        var regions = HistoryRecipes.TryCreate(target, new HistoryRecipe { Kind = HistoryRecipes.ClearAllRegions });
        Assert.NotNull(regions);
        new EditHistory().Do(target, regions!);
        Assert.Empty(target.Regions);
    }

    private static AudioDocument MakeConstant(int frames, float value, int rate)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }

    private static AudioDocument MakeSine(int frames, int rate)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5f;
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }

    private static AudioDocument MakeRamp(int frames, int rate)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = i;
            samples[i * 2 + 1] = i;
        }

        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }
}
