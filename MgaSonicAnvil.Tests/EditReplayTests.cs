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

    private static AudioDocument MakeConstant(int frames, float value, int rate)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }
}
