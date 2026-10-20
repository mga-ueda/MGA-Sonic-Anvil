using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ProcessEditsTests
{
    [Fact]
    public void FadeIn_StartsSilentAndEndsAtOriginal()
    {
        var document = MakeSine(frames: 100);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 100)));

        Assert.Equal(0f, document.Interleaved[0], 5);
        Assert.Equal(0f, document.Interleaved[1], 5);
        Assert.True(Math.Abs(document.Interleaved[^2]) > 0.1f);
    }

    [Fact]
    public void FadeOut_EndsSilent()
    {
        var document = MakeSine(frames: 80);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 80)));

        Assert.Equal(0f, document.Interleaved[^1], 5);
        Assert.Equal(0f, document.Interleaved[^2], 5);
    }

    [Fact]
    public void FadeIn_UsesSelectedCurveGain()
    {
        var document = MakeConstant(frames: 5, value: 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 5), FadeShape.Exp3));

        Assert.Equal(0f, document.Interleaved[0], 5);
        Assert.Equal(0.125f, document.Interleaved[4], 5);
        Assert.Equal(1f, document.Interleaved[8], 5);
    }

    [Fact]
    public void PlaybackProvider_AppliesFadePreviewGainWithoutChangingDocument()
    {
        var document = MakeConstant(frames: 5, value: 1f);
        var original = (float[])document.Interleaved.Clone();
        var provider = new PlaybackSampleProvider();
        provider.Bind(
            document,
            0,
            new WaveSelection(0, 5),
            loop: false,
            frame => FadeCurves.GainAtFrame(FadeShape.Linear, fadeIn: true, frame, 0, 5));

        var buffer = new float[10];
        Assert.Equal(10, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0f, buffer[0], 5);
        Assert.Equal(0.5f, buffer[4], 5);
        Assert.Equal(1f, buffer[8], 5);
        Assert.Equal(original, document.Interleaved);
    }

    [Fact]
    public void FadeOut_IncludesSampleAtExclusiveEndBoundary()
    {
        var document = MakeConstant(frames: 20, value: 1f);
        var history = new EditHistory();
        // 選択 [0,10) の終端線は frame 10。ここも無音にする。
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 10), FadeShape.Linear));

        Assert.Equal(1f, document.Interleaved[0], 5);
        Assert.Equal(0f, document.Interleaved[10 * 2], 5);
        Assert.Equal(0f, document.Interleaved[10 * 2 + 1], 5);
        Assert.Equal(1f, document.Interleaved[11 * 2], 5);
    }

    [Fact]
    public void FadeIn_StartsSilentAtSelectionStart()
    {
        var document = MakeConstant(frames: 20, value: 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(5, 15), FadeShape.Linear));

        Assert.Equal(1f, document.Interleaved[4 * 2], 5);
        Assert.Equal(0f, document.Interleaved[5 * 2], 5);
        Assert.Equal(0f, document.Interleaved[5 * 2 + 1], 5);
    }

    [Fact]
    public void FadeOut_UsesComplementOfRisingCurve()
    {
        var document = MakeConstant(frames: 5, value: 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeOut(document, new WaveSelection(0, 5), FadeShape.Linear));

        Assert.Equal(1f, document.Interleaved[0], 5);
        Assert.Equal(0.5f, document.Interleaved[4], 5);
        Assert.Equal(0f, document.Interleaved[8], 5);
    }

    [Fact]
    public void FadeAroundPlayhead_AppliesLinearOutThenIn()
    {
        var document = MakeConstant(frames: 10, value: 1f);
        document.Selection = new WaveSelection(2, 8);
        document.CursorFrame = 5;
        var history = new EditHistory();
        var command = ProcessEdits.FadeAroundPlayhead(document, new WaveSelection(0, 10), playhead: 5);
        Assert.NotNull(command);
        history.Do(document, command);

        Assert.Equal(1f, document.Interleaved[0], 5);
        Assert.Equal(0.5f, document.Interleaved[4], 5);
        Assert.Equal(0f, document.Interleaved[8], 5);
        Assert.Equal(0f, document.Interleaved[10], 5);
        Assert.Equal(0.5f, document.Interleaved[14], 5);
        Assert.Equal(1f, document.Interleaved[18], 5);
        Assert.Equal(new WaveSelection(2, 8), document.Selection);
        Assert.Equal(5, document.CursorFrame);
        Assert.True(history.Undo(document));
        Assert.Equal(1f, document.Interleaved[8], 5);
        Assert.Equal(1f, document.Interleaved[10], 5);
    }

    [Fact]
    public void FadeAroundPlayhead_OnlyFadesVisibleAfterWhenPlayheadIsAtStart()
    {
        var document = MakeConstant(frames: 8, value: 1f);
        var command = ProcessEdits.FadeAroundPlayhead(document, new WaveSelection(2, 6), playhead: 2);
        Assert.NotNull(command);
        command.Apply(document);

        Assert.Equal(1f, document.Interleaved[2], 5);
        Assert.Equal(0f, document.Interleaved[4], 5);
        Assert.Equal(1f, document.Interleaved[10], 5);
        Assert.Equal(1f, document.Interleaved[12], 5);
    }

    [Fact]
    public void FadeAroundPlayhead_OnlyFadesVisibleBeforeWhenPlayheadIsAtEnd()
    {
        var document = MakeConstant(frames: 8, value: 1f);
        var command = ProcessEdits.FadeAroundPlayhead(document, new WaveSelection(2, 6), playhead: 6);
        Assert.NotNull(command);
        command.Apply(document);

        Assert.Equal(1f, document.Interleaved[2], 5);
        Assert.Equal(1f, document.Interleaved[4], 5);
        Assert.Equal(0f, document.Interleaved[10], 5);
        Assert.Equal(1f, document.Interleaved[12], 5);
    }

    [Fact]
    public void SplitVisibleAroundPlayhead_UsesVisibleSidesOnly()
    {
        ProcessEdits.SplitVisibleAroundPlayhead(new WaveSelection(10, 40), 25, 100, out var midOut, out var midIn);
        Assert.Equal(new WaveSelection(10, 25), midOut);
        Assert.Equal(new WaveSelection(25, 40), midIn);

        ProcessEdits.SplitVisibleAroundPlayhead(new WaveSelection(10, 40), 0, 100, out var beforeOut, out var beforeIn);
        Assert.True(beforeOut.IsEmpty);
        Assert.Equal(new WaveSelection(10, 40), beforeIn);

        ProcessEdits.SplitVisibleAroundPlayhead(new WaveSelection(10, 40), 80, 100, out var afterOut, out var afterIn);
        Assert.Equal(new WaveSelection(10, 40), afterOut);
        Assert.True(afterIn.IsEmpty);
    }

    [Fact]
    public void NormalizePerRegion_FitsEachRegionPeakAndFadesEdges()
    {
        var samples = new float[800];
        Array.Fill(samples, 0.05f);
        for (var i = 40; i < 400; i++)
        {
            samples[i] = 0.25f;
        }

        for (var i = 450; i < 750; i++)
        {
            samples[i] = 0.1f;
        }

        var document = new AudioDocument(samples, 48000, 1, 24, AudioFileKind.Wave, null);
        document.SetRegions([new WaveSelection(40, 400), new WaveSelection(450, 750)]);
        var command = ProcessEdits.NormalizePerRegion(document, fadeMs: 2);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);

        Assert.Equal("Normalize Per Region", command!.Name);
        Assert.Equal(0f, document.Interleaved[40], 5);
        Assert.InRange(document.Interleaved[220], 0.97f, 1.0f);
        Assert.Equal(0f, document.Interleaved[399], 5);
        Assert.InRange(document.Interleaved[600], 0.97f, 1.0f);
        Assert.Equal(0.05f, document.Interleaved[420], 5);
    }

    [Fact]
    public void NormalizePerRegion_ReturnsNullWhenNoRegions()
    {
        var document = MakeConstant(frames: 8, value: 0.5f);
        Assert.Null(ProcessEdits.NormalizePerRegion(document));
    }

    [Fact]
    public void Normalize_RaisesPeakNearMinusPointOneDb()
    {
        var samples = new float[200];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = i % 2 == 0 ? 0.25f : -0.1f;
        }

        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 100)));

        var peak = document.Interleaved.Max(Math.Abs);
        Assert.InRange(peak, 0.97f, 1.0f);
    }

    [Fact]
    public void Gain_ScalesSelectionByDbAndUndoRestores()
    {
        var document = MakeConstant(frames: 8, value: 0.25f);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        var command = ProcessEdits.Gain(document, new WaveSelection(2, 6), 6);
        Assert.NotNull(command);
        history.Do(document, command!);

        var expected = 0.25f * (float)Math.Pow(10d, 6d / 20d);
        Assert.Equal(0.25f, document.Interleaved[0], 5);
        Assert.Equal(expected, document.Interleaved[4], 5);
        Assert.Equal(0.25f, document.Interleaved[12], 5);
        Assert.Equal("Volume", command!.Name);
        Assert.True(history.Undo(document));
        Assert.Equal(original, document.Interleaved);
    }

    [Fact]
    public void SelectionRangeEdits_SeekCursorToRangeStart()
    {
        var range = new WaveSelection(10, 40);
        var document = MakeConstant(frames: 80, value: 0.5f);
        document.Selection = range;
        document.CursorFrame = range.EndFrame;
        var history = new EditHistory();

        history.Do(document, ProcessEdits.FadeIn(document, range, FadeShape.Linear));
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));
        Assert.Equal(range.EndFrame, document.CursorFrame);

        history.Do(document, ProcessEdits.FadeOut(document, range, FadeShape.Linear));
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));

        history.Do(document, ProcessEdits.Gain(document, range, -3)!);
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));

        history.Do(document, ProcessEdits.Normalize(document, range));
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));

        history.Do(document, ProcessEdits.Reverse(document, range)!);
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));

        history.Do(document, ProcessEdits.PitchShift(document, range, 1)!);
        Assert.Equal(range.StartFrame, document.CursorFrame);
        Assert.True(history.Undo(document));

        history.Do(document, ProcessEdits.TimeStretch(document, range, (int)range.Length / 2)!);
        Assert.Equal(range.StartFrame, document.CursorFrame);
    }

    [Fact]
    public void PitchShift_RaisesSineAndUndoRestores()
    {
        var document = MakeSine(frames: 48000);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        var command = ProcessEdits.PitchShift(document, new WaveSelection(0, 48000), 12);
        Assert.NotNull(command);
        history.Do(document, command!);
        Assert.Equal("Pitch Shift", command!.Name);
        Assert.NotEqual(original[8000], document.Interleaved[8000]);
        Assert.True(history.Undo(document));
        Assert.Equal(original, document.Interleaved);
    }

    [Fact]
    public void PitchShift_WithoutStretchChangesLengthAndUndoRestores()
    {
        var document = MakeSine(frames: 48000);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        document.Selection = new WaveSelection(0, 48000);
        var command = ProcessEdits.PitchShift(document, document.Selection, 12, timeStretch: false);
        Assert.NotNull(command);
        history.Do(document, command!);
        Assert.Equal(24000, document.FrameCount);
        Assert.True(document.Selection.IsEmpty);
        Assert.True(history.Undo(document));
        Assert.Equal(original, document.Interleaved);
        Assert.Equal(48000, document.FrameCount);
    }

    [Fact]
    public void PitchShift_ReturnsNullWhenZeroSemitones()
    {
        var document = MakeSine(frames: 64);
        Assert.Null(ProcessEdits.PitchShift(document, new WaveSelection(0, 64), 0));
        Assert.Null(ProcessEdits.PitchShift(document, WaveSelection.Empty, 3));
    }

    [Fact]
    public void TimeStretch_ChangesLengthAndUndoRestores()
    {
        var document = MakeSine(frames: 48000);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        document.Selection = new WaveSelection(0, 48000);
        var command = ProcessEdits.TimeStretch(document, document.Selection, 24000);
        Assert.NotNull(command);
        history.Do(document, command!);
        Assert.Equal("Time Stretch", command!.Name);
        Assert.Equal(24000, document.FrameCount);
        Assert.True(document.Selection.IsEmpty);
        Assert.True(history.Undo(document));
        Assert.Equal(original, document.Interleaved);
        Assert.Equal(48000, document.FrameCount);
    }

    [Fact]
    public void TimeStretch_ReturnsNullWhenLengthUnchanged()
    {
        var document = MakeSine(frames: 64);
        Assert.Null(ProcessEdits.TimeStretch(document, new WaveSelection(0, 64), 64));
        Assert.Null(ProcessEdits.TimeStretch(document, WaveSelection.Empty, 32));
    }

    [Fact]
    public void Reverse_FlipsRangeAndUndoRestores()
    {
        var samples = new float[] { 0.1f, -0.1f, 0.2f, -0.2f, 0.3f, -0.3f, 0.4f, -0.4f };
        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        document.Selection = new WaveSelection(1, 4);
        var command = ProcessEdits.Reverse(document, document.Selection);
        Assert.NotNull(command);
        history.Do(document, command!);

        Assert.Equal("Reverse", command!.Name);
        Assert.Equal(0.1f, document.Interleaved[0]);
        Assert.Equal(-0.1f, document.Interleaved[1]);
        Assert.Equal(0.4f, document.Interleaved[2]);
        Assert.Equal(-0.4f, document.Interleaved[3]);
        Assert.Equal(0.3f, document.Interleaved[4]);
        Assert.Equal(-0.3f, document.Interleaved[5]);
        Assert.Equal(0.2f, document.Interleaved[6]);
        Assert.Equal(-0.2f, document.Interleaved[7]);
        Assert.True(document.Selection.IsEmpty);
        Assert.True(history.Undo(document));
        Assert.Equal(original, document.Interleaved);
    }

    [Fact]
    public void Reverse_LeavesMarkersRegionsAndLoopInPlace()
    {
        var document = MakeConstant(frames: 20, value: 0.4f);
        document.TryAddMarker(2);
        document.TryAddMarker(5);
        document.TryAddMarker(15);
        document.SetRegions([new WaveSelection(0, 5), new WaveSelection(3, 7), new WaveSelection(12, 18)]);
        document.SetSampleLoop(new WaveSelection(4, 7));
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Reverse(document, new WaveSelection(2, 10))!);

        Assert.Equal(new long[] { 2, 5, 15 }, document.Markers.Select(item => item.Frame).ToArray());
        Assert.Equal(
            [new WaveSelection(0, 5), new WaveSelection(3, 7), new WaveSelection(12, 18)],
            document.Regions);
        Assert.Equal(new WaveSelection(4, 7), document.SampleLoop);
    }

    [Fact]
    public void Reverse_ReturnsNullWhenTooShort()
    {
        var document = MakeConstant(frames: 4, value: 0.5f);
        Assert.Null(ProcessEdits.Reverse(document, new WaveSelection(0, 1)));
        Assert.Null(ProcessEdits.Reverse(document, WaveSelection.Empty));
    }

    [Fact]
    public void RangeTransforms_ClearSelection()
    {
        var document = MakeSine(frames: 64);
        document.Selection = new WaveSelection(0, 32);
        new EditHistory().Do(document, ProcessEdits.FadeIn(document, document.Selection));
        Assert.True(document.Selection.IsEmpty);

        document.Selection = new WaveSelection(8, 24);
        new EditHistory().Do(document, ProcessEdits.Normalize(document, document.Selection));
        Assert.True(document.Selection.IsEmpty);

        document.Selection = new WaveSelection(0, 16);
        new EditHistory().Do(document, ProcessEdits.Gain(document, document.Selection, -3)!);
        Assert.True(document.Selection.IsEmpty);

        document = MakeSine(frames: 4096);
        document.Selection = new WaveSelection(0, 4096);
        new EditHistory().Do(document, ProcessEdits.PitchShift(document, document.Selection, 1)!);
        Assert.True(document.Selection.IsEmpty);

        document = MakeSine(frames: 4096);
        document.Selection = new WaveSelection(0, 4096);
        new EditHistory().Do(document, ProcessEdits.TimeStretch(document, document.Selection, 2048)!);
        Assert.True(document.Selection.IsEmpty);

        document = MakeSine(frames: 64);
        document.Selection = new WaveSelection(8, 24);
        new EditHistory().Do(document, ProcessEdits.Reverse(document, document.Selection)!);
        Assert.True(document.Selection.IsEmpty);
    }

    [Fact]
    public void Gain_ReturnsNullWhenZeroDb()
    {
        var document = MakeConstant(frames: 4, value: 0.5f);
        Assert.Null(ProcessEdits.Gain(document, new WaveSelection(0, 4), 0));
        Assert.Null(ProcessEdits.Gain(document, WaveSelection.Empty, 3));
    }

    [Fact]
    public void DeleteSilence_RemovesSilentSpansAndShiftsMarkers()
    {
        const int rate = 1000;
        const int audible = 20;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        const int tail = 20;
        var document = MakeToneWithSilence(audible, silent, tail, rate);
        var hole = audible + radius + 10;
        var afterHole = audible + silent + 2;
        document.TryAddMarker(10);
        document.TryAddMarker(hole);
        document.TryAddMarker(afterHole);
        document.SetRegion(new WaveSelection(0, document.FrameCount));
        document.SetSampleLoop(new WaveSelection(15, afterHole));
        document.CursorFrame = afterHole;
        var history = new EditHistory();
        var command = ProcessEdits.DeleteSilence(document, new WaveSelection(0, document.FrameCount), -60);
        Assert.NotNull(command);
        var beforeCount = audible + silent + tail;
        history.Do(document, command);

        Assert.Equal(beforeCount - 20, document.FrameCount);
        Assert.Equal(0.5f, document.Interleaved[0], 5);
        Assert.Equal(new long[] { 10, afterHole - 20 }, document.Markers.Select(marker => marker.Frame));
        Assert.Equal(new WaveSelection(0, document.FrameCount), document.Region);
        Assert.Equal(new WaveSelection(15, afterHole - 20), document.SampleLoop);
        Assert.Equal(0, document.CursorFrame);
        Assert.True(history.Undo(document));
        Assert.Equal(beforeCount, document.FrameCount);
        Assert.Equal(new long[] { 10, hole, afterHole }, document.Markers.Select(marker => marker.Frame));
    }

    [Fact]
    public void DeleteSilence_FadesSpliceEdges()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        const int tail = 20;
        const int audible = 20;
        var document = MakeToneWithSilence(audible, silent, tail, rate);
        var range = new WaveSelection(0, document.FrameCount);
        var runs = SilentSkip.CollectAudibleSpans(
            document.Interleaved,
            document.Channels,
            range.StartFrame,
            range.EndFrame,
            SilentSkip.LinearFromDb(-60),
            0,
            radius);
        Assert.Equal(2, runs.Count);
        var command = ProcessEdits.DeleteSilence(document, range, -60);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);

        Assert.Equal(0.5f, document.Interleaved[0], 5);
        var firstEnd = (int)runs[0].Length - 1;
        Assert.True(Math.Abs(document.Interleaved[firstEnd * 2]) < 0.5f);
        Assert.True(Math.Abs(document.Interleaved[(firstEnd + 1) * 2]) < 0.5f);
    }

    [Fact]
    public void DeleteSilence_KeepsNearThresholdTone()
    {
        var document = MakeNearThresholdSine(cycles: 8);
        var threshold = SilentSkip.LinearFromDb(-60);
        var frames = document.FrameCount;
        long start = 0;
        while (start < frames && Math.Abs(document.Interleaved[start * 2]) < threshold)
        {
            start++;
        }

        long end = frames;
        while (end > start && Math.Abs(document.Interleaved[(end - 1) * 2]) < threshold)
        {
            end--;
        }

        // 端の谷だけを外し、内側の周期は残す（フレーム数は外縁のしきい値交差まで）。
        Assert.True(start > 0 && end < frames);
        var command = ProcessEdits.DeleteSilence(document, new WaveSelection(0, frames), -60);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(end - start, document.FrameCount);
        Assert.True(document.FrameCount > frames / 2);
    }

    [Fact]
    public void DeleteSilence_ReturnsNullWhenNoSilence()
    {
        var document = MakeConstant(frames: 8, value: 0.5f);
        Assert.Null(ProcessEdits.DeleteSilence(document, new WaveSelection(0, 8), -60));
    }

    [Fact]
    public void DeleteSilence_ReturnsNullWhenWholeFileIsSilent()
    {
        var document = MakeConstant(frames: 8, value: 0f);
        Assert.Null(ProcessEdits.DeleteSilence(document, new WaveSelection(0, 8), -60));
    }

    [Fact]
    public void DeleteSilence_DetectsSilenceOnSoloedChannelOnly()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var frames = 20 + silent + 10;
        var samples = new float[frames * 2];
        Array.Fill(samples, 0.5f);
        for (var i = 20; i < 20 + silent; i++)
        {
            samples[i * 2] = 0f;
        }

        var document = new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
        Assert.Null(ProcessEdits.DeleteSilence(document, new WaveSelection(0, frames), -60));

        var command = ProcessEdits.DeleteSilence(
            document,
            new WaveSelection(0, frames),
            -60,
            channelMask: ChannelSolo.MaskOf(0));
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(frames - 20, document.FrameCount);
        Assert.Equal(0.5f, document.Interleaved[0], 5);
        Assert.Equal(0.5f, document.Interleaved[1], 5);
    }

    [Fact]
    public void DeleteSilence_RemovesPartialRangeWhenItIsAllSilent()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(30, silent, 30, rate);
        var core = new WaveSelection(30 + radius, 30 + silent - radius);
        var command = ProcessEdits.DeleteSilence(document, core, -60);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(110, document.FrameCount);
    }

    [Fact]
    public void DeleteSilence_EdgesOnly_KeepsMiddleSilence()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var middleSilent = radius * 2 + 20;
        const int lead = 40;
        const int audible = 20;
        const int tail = 40;
        var frames = lead + audible + middleSilent + audible + tail;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            if (i < lead || (i >= lead + audible && i < lead + audible + middleSilent) || i >= frames - tail)
            {
                continue;
            }

            samples[i * 2] = 0.5f;
            samples[i * 2 + 1] = 0.5f;
        }

        var document = new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
        var entire = ProcessEdits.DeleteSilence(document, new WaveSelection(0, frames), -60);
        Assert.NotNull(entire);

        var edges = ProcessEdits.DeleteSilence(
            document,
            new WaveSelection(0, frames),
            -60,
            edgesOnly: true);
        Assert.NotNull(edges);
        new EditHistory().Do(document, edges);

        // 先頭・末尾のデジタルゼロはすべて削る。中央の無音は残す。
        var edgeTrim = lead + tail;
        Assert.Equal(frames - edgeTrim, document.FrameCount);
        Assert.True(document.FrameCount > audible * 2);
        var mid = SilentSkip.CollectSilentSpans(
            document.Interleaved,
            document.Channels,
            0,
            document.FrameCount,
            SilentSkip.LinearFromDb(-60),
            0,
            radius);
        Assert.NotEmpty(mid);
    }

    [Fact]
    public void DeleteSilence_EdgesOnly_ReturnsNullWhenNoEdgeSilence()
    {
        var document = MakeConstant(frames: 8, value: 0.5f);
        Assert.Null(ProcessEdits.DeleteSilence(document, new WaveSelection(0, 8), -60, edgesOnly: true));
    }

    [Fact]
    public void SilencePreview_DisplayGainFillsPeak()
    {
        Assert.Equal(1f, MgaSonicAnvil.UI.SilenceDetectWavePreview.DisplayGain([], []));
        Assert.Equal(4f, MgaSonicAnvil.UI.SilenceDetectWavePreview.DisplayGain([-0.25f, 0.1f], [0.2f, 0.05f]));
    }

    [Fact]
    public void SilencePreview_ZoomAroundKeepsAnchorInsideRange()
    {
        var (start, span) = MgaSonicAnvil.UI.SilenceDetectWavePreview.ZoomAround(
            0, 1000, 0, 1000, 0.25, 2, 10);
        Assert.Equal(500, span, 6);
        Assert.Equal(250, start + 0.25 * span, 6);

        var full = MgaSonicAnvil.UI.SilenceDetectWavePreview.ZoomAround(
            start, span, 0, 1000, 0, 0.1, 10);
        Assert.Equal(0, full.Start, 6);
        Assert.Equal(1000, full.Span, 6);

        var tight = MgaSonicAnvil.UI.SilenceDetectWavePreview.ZoomAround(
            0, 1000, 0, 1000, 1, 1000, 40);
        Assert.Equal(40, tight.Span, 6);
        Assert.Equal(960, tight.Start, 6);
    }

    [Fact]
    public void SilencePreview_UsesSameSpansAsApplyIncludingMinimumTimes()
    {
        RunSta(() =>
        {
            const int rate = 1000;
            var radius = SilentSkip.PeakWindowRadiusFrames(rate);
            var document = MakeToneWithSilence(40, radius * 2 + 80, 40, rate);
            var range = new WaveSelection(0, document.FrameCount);
            var options = SilenceDetectOptions.FromThreshold(-60) with
            {
                MinGateMs = 0,
                MinCloseMs = 30,
                PreRollMs = 15,
                PostRollMs = 25,
            };
            var expected = SilenceDetect.CollectKeepSpans(
                document.Interleaved,
                document.Channels,
                range.StartFrame,
                range.EndFrame,
                document.SampleRate,
                soloMask: 0,
                options);
            Assert.Equal(2, expected.Count);

            var preview = new MgaSonicAnvil.UI.SilenceDetectWavePreview
            {
                Document = document,
                Range = range,
                Options = options,
            };
            Assert.Equal(expected, preview.Keeps);

            var bridged = options with { MinGateMs = radius * 2 + 80 };
            preview.Options = bridged;
            Assert.Equal(
                SilenceDetect.CollectKeepSpans(
                    document.Interleaved,
                    document.Channels,
                    range.StartFrame,
                    range.EndFrame,
                    document.SampleRate,
                    soloMask: 0,
                    bridged),
                preview.Keeps);
            Assert.Single(preview.Keeps);
        });
    }

    [Fact]
    public void SilencePreview_AdjacentCueFrame_JumpsKeepBounds()
    {
        RunSta(() =>
        {
            const int rate = 1000;
            var radius = SilentSkip.PeakWindowRadiusFrames(rate);
            var document = MakeToneWithSilence(40, radius * 2 + 80, 40, rate);
            var range = new WaveSelection(0, document.FrameCount);
            var options = SilenceDetectOptions.FromThreshold(-60) with
            {
                MinGateMs = 0,
                MinCloseMs = 30,
                PreRollMs = 0,
                PostRollMs = 0,
            };
            var preview = new MgaSonicAnvil.UI.SilenceDetectWavePreview
            {
                Document = document,
                Range = range,
                Options = options,
            };
            Assert.Equal(2, preview.Keeps.Count);
            var first = preview.Keeps[0];
            var second = preview.Keeps[1];
            var firstEnd = Math.Max(first.StartFrame, first.EndFrame - 1);
            var secondEnd = Math.Max(second.StartFrame, second.EndFrame - 1);

            // キュー上では次／前へ。先頭キューより前からは最初のキューへ。
            if (first.StartFrame > 0)
            {
                Assert.Equal(first.StartFrame, preview.AdjacentCueFrame(0, 1));
            }
            else
            {
                Assert.Equal(firstEnd, preview.AdjacentCueFrame(0, 1));
            }

            Assert.Equal(firstEnd, preview.AdjacentCueFrame(first.StartFrame, 1));
            Assert.Equal(second.StartFrame, preview.AdjacentCueFrame(firstEnd, 1));
            Assert.Equal(secondEnd, preview.AdjacentCueFrame(second.StartFrame, 1));
            Assert.Equal(range.EndFrame - 1, preview.AdjacentCueFrame(secondEnd, 1));
            Assert.Equal(second.StartFrame, preview.AdjacentCueFrame(secondEnd, -1));
            Assert.Equal(first.StartFrame > 0 ? first.StartFrame : 0,
                preview.AdjacentCueFrame(firstEnd, -1));
            Assert.Equal(0, preview.AdjacentCueFrame(first.StartFrame, -1));
        });
    }

    [Fact]
    public void CollectDeleteSilenceKeepSpans_EdgesOnly_MergesAudible()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(20, silent, 20, rate);
        var keeps = ProcessEdits.CollectDeleteSilenceKeepSpans(
            document,
            new WaveSelection(0, document.FrameCount),
            soloMask: 0,
            SilenceDetectOptions.FromThreshold(-60, edgesOnly: true));
        Assert.Single(keeps);
        Assert.True(keeps[0].Length > 20);
    }

    [Fact]
    public void CreateRegionsFromSilenceDetection_AddsAudibleRegions()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(20, silent, 20, rate);
        document.SetRegion(new WaveSelection(0, 5));
        var command = ProcessEdits.CreateRegionsFromSilenceDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            -60);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(2, document.Regions.Count);
        Assert.DoesNotContain(document.Regions, region => region == new WaveSelection(0, 5));
    }

    [Fact]
    public void CreateRegionsFromSilenceDetection_EdgesOnly_CreatesSingleRegion()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(20, silent, 20, rate);
        var command = ProcessEdits.CreateRegionsFromSilenceDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            -60,
            edgesOnly: true);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Single(document.Regions);
        Assert.True(document.Regions[0].Length > 20);
    }

    [Fact]
    public void CreateMarkersFromSilenceDetection_AddsMarkersAtAudibleStarts()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(20, silent, 20, rate);
        document.TryAddMarker(5);
        var command = ProcessEdits.CreateMarkersFromSilenceDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            -60);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(2, document.Markers.Count);
        Assert.DoesNotContain(document.Markers, marker => marker.Frame == 5);
    }

    [Fact]
    public void CreateMarkersFromSilenceDetection_EdgesOnly_AddsSingleMarker()
    {
        const int rate = 1000;
        var radius = SilentSkip.PeakWindowRadiusFrames(rate);
        var silent = radius * 2 + 20;
        var document = MakeToneWithSilence(20, silent, 20, rate);
        var command = ProcessEdits.CreateMarkersFromSilenceDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            -60,
            edgesOnly: true);
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Single(document.Markers);
    }

    [Fact]
    public void CreateMarkersFromHitPointDetection_MarksRisesInsideASustainedTone()
    {
        const int rate = 8000;
        var frames = rate;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.4f;
            samples[i * 2 + 1] = 0.4f;
        }

        StampHit(samples, 2, 1000, 0.95f);
        StampHit(samples, 2, 4000, 0.95f);
        StampHit(samples, 2, 6500, 0.95f);
        var document = new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
        document.TryAddMarker(10);
        var options = SilenceDetectOptions.FromThreshold(-20) with { MinGateMs = 50 };
        var silence = ProcessEdits.CreateMarkersFromSilenceDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            options);
        Assert.NotNull(silence);
        var hit = ProcessEdits.CreateMarkersFromHitPointDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            options);
        Assert.NotNull(hit);
        new EditHistory().Do(document, hit);
        Assert.Equal(3, document.Markers.Count);
        Assert.DoesNotContain(document.Markers, marker => marker.Frame == 10);
        Assert.Contains(document.Markers, marker => Math.Abs(marker.Frame - 1000) <= 8);
        Assert.Contains(document.Markers, marker => Math.Abs(marker.Frame - 4000) <= 8);
        Assert.Contains(document.Markers, marker => Math.Abs(marker.Frame - 6500) <= 8);
    }

    [Fact]
    public void CreateMarkersFromHitPointDetection_IgnoresFlatAudio()
    {
        const int rate = 8000;
        var samples = new float[rate * 2];
        Array.Fill(samples, 0.4f);
        var document = new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
        var command = ProcessEdits.CreateMarkersFromHitPointDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            SilenceDetectOptions.FromThreshold(-20) with { MinGateMs = 50 });
        Assert.Null(command);
    }

    [Fact]
    public void CreateMarkersFromHitPointDetection_HonorsMinimumGap()
    {
        const int rate = 8000;
        var frames = rate;
        var samples = new float[frames * 2];
        StampHit(samples, 2, 1000, 0.95f);
        StampHit(samples, 2, 1200, 0.95f);
        StampHit(samples, 2, 4000, 0.95f);
        var document = new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
        var command = ProcessEdits.CreateMarkersFromHitPointDetection(
            document,
            new WaveSelection(0, document.FrameCount),
            SilenceDetectOptions.FromThreshold(-20) with { MinGateMs = 200 });
        Assert.NotNull(command);
        new EditHistory().Do(document, command);
        Assert.Equal(2, document.Markers.Count);
    }

    [Fact]
    public void Delete_RemovesRangeAndUndoRestores()
    {
        var document = MakeSine(frames: 50);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        document.Selection = new WaveSelection(10, 20);
        var committedBytes = document.CommittedFileBytes;
        history.Do(document, ProcessEdits.Delete(document, document.Selection));

        Assert.Equal(40, document.FrameCount);
        Assert.True(document.FileSizeEdited);
        Assert.Equal(
            AudioDocument.EstimateFileBytes(committedBytes, 50, document.Channels, document.BitsPerSample, 40, document.Channels, document.BitsPerSample),
            document.EstimatedFileBytes);
        Assert.True(history.Undo(document));
        Assert.Equal(50, document.FrameCount);
        Assert.False(document.FileSizeEdited);
        Assert.Equal(original, document.Interleaved);
    }

    [Fact]
    public void History_IsUnlimitedWithinMemory()
    {
        var document = MakeSine(frames: 32);
        var history = new EditHistory();
        for (var i = 0; i < 40; i++)
        {
            history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 32)));
        }

        var undone = 0;
        while (history.Undo(document))
        {
            undone++;
        }

        Assert.Equal(40, undone);
    }

    [Fact]
    public void WaveRoundTrip_PreservesSampleRateAndLength()
    {
        var document = MakeSine(frames: 480);
        var path = Path.Combine(Path.GetTempPath(), $"sonic-anvil-{Guid.NewGuid():N}.wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            var loaded = AudioCodec.Load(path);
            Assert.Equal(48000, loaded.SampleRate);
            Assert.Equal(2, loaded.Channels);
            Assert.Equal(480, loaded.FrameCount);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void WaveOut_AcceptsConvertedPcm16Provider()
    {
        var document = MakeSine(frames: 480);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        using var output = new WaveOutEvent { DeviceNumber = -1 };
        output.Init(new SampleToWaveProvider16(provider));
        output.Play();
        Thread.Sleep(50);
        output.Stop();
    }

    [Fact]
    public void PlaybackProvider_SeekOutsideSelectionKeepsReading()
    {
        var document = MakeConstant(frames: 40, value: 0.5f);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, new WaveSelection(0, 8), loop: false);
        provider.SetPlayWindow(null, loop: false);
        provider.SeekFrame(20);

        var buffer = new float[4];
        Assert.Equal(4, provider.Read(buffer, 0, buffer.Length));
        Assert.False(provider.Ended);
        Assert.All(buffer, sample => Assert.Equal(0.5f, sample, 5));
    }

    [Fact]
    public void PlaybackProvider_ReadsNonZeroSamples()
    {
        var document = MakeSine(frames: 480);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var buffer = new float[128];
        var read = provider.Read(buffer, 0, buffer.Length);
        Assert.Equal(128, read);
        Assert.Contains(buffer, sample => Math.Abs(sample) > 0.01f);
        Assert.False(provider.Ended);
    }

    [Fact]
    public void AsioOutputAdapter_KeepsFillingAfterSourceEnds()
    {
        var document = MakeSine(frames: 8);
        var provider = new PlaybackSampleProvider();
        provider.Bind(document, 0, null, loop: false);
        var adapter = new AsioOutputAdapter(provider, 2);
        var buffer = new byte[1024];
        Assert.Equal(1024, adapter.Read(buffer, 0, buffer.Length));
        Assert.True(provider.Ended);
        Assert.Equal(1024, adapter.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0, value));
    }

    [Fact]
    public void CopyPaste_InsertsAtCursorAndUndoRestores()
    {
        var document = MakeConstant(frames: 10, value: 0.5f);
        document.Selection = new WaveSelection(0, 3);
        var clip = ProcessEdits.Copy(document, document.Selection);
        Assert.NotNull(clip);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = 10;
        var history = new EditHistory();
        var command = ProcessEdits.Paste(document, clip, 10);
        Assert.NotNull(command);
        history.Do(document, command);

        Assert.Equal(13, document.FrameCount);
        Assert.True(document.Selection.IsEmpty);
        Assert.Equal(0.5f, document.Interleaved[20]);
        Assert.True(history.Undo(document));
        Assert.Equal(10, document.FrameCount);
        Assert.True(document.Selection.IsEmpty);
    }

    [Fact]
    public void Paste_ReplacesSelection()
    {
        var document = MakeConstant(frames: 8, value: 0.2f);
        document.ReplaceRange(0, [0.9f, 0.9f, 0.9f, 0.9f]);
        document.Selection = new WaveSelection(0, 2);
        var clip = ProcessEdits.Copy(document, document.Selection);
        Assert.NotNull(clip);
        document.Selection = new WaveSelection(4, 6);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Paste(document, clip, 0)!);

        Assert.Equal(8, document.FrameCount);
        Assert.Equal(0.9f, document.Interleaved[8]);
        Assert.Equal(0.9f, document.Interleaved[10]);
        Assert.Equal(0.2f, document.Interleaved[12]);
        Assert.True(document.Selection.IsEmpty);
    }

    [Fact]
    public void CopyPaste_IncludesMarkersAndComments()
    {
        var document = MakeConstant(frames: 20, value: 0.4f);
        document.TryAddMarker(2);
        document.TrySetMarkerComment(2, "-L");
        document.TryAddMarker(5);
        document.TryAddMarker(12);
        document.Selection = new WaveSelection(0, 10);
        var clip = ProcessEdits.Copy(document, document.Selection);
        Assert.NotNull(clip);
        Assert.Equal(2, clip.Markers.Count);

        document.Selection = WaveSelection.Empty;
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Paste(document, clip, 20)!);

        Assert.Equal(30, document.FrameCount);
        Assert.Equal(new long[] { 2, 5, 12, 22, 25 }, document.Markers.Select(m => m.Frame).ToArray());
        Assert.Equal("-L", document.MarkerCommentAt(22));
        Assert.True(history.Undo(document));
        Assert.Equal(new long[] { 2, 5, 12 }, document.Markers.Select(m => m.Frame).ToArray());
    }

    [Fact]
    public void CopyPaste_IncludesExactRegionAndName()
    {
        var document = MakeConstant(frames: 20, value: 0.3f);
        document.SetRegions([new WaveSelection(4, 10), new WaveSelection(12, 18)]);
        Assert.True(document.TrySetRegionName(new WaveSelection(4, 10), "verse"));
        document.Selection = new WaveSelection(4, 10);
        var clip = ProcessEdits.Copy(document, document.Selection);
        Assert.NotNull(clip);
        var copied = Assert.Single(clip.Regions);
        Assert.Equal(new WaveSelection(0, 6), copied.Range);
        Assert.Equal("verse", copied.Name);

        document.Selection = new WaveSelection(5, 12);
        var wider = ProcessEdits.Copy(document, document.Selection);
        Assert.NotNull(wider);
        Assert.Empty(wider.Regions);

        document.Selection = WaveSelection.Empty;
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Paste(document, clip, 20)!);

        Assert.Equal(26, document.FrameCount);
        Assert.Equal(
            [new WaveSelection(4, 10), new WaveSelection(12, 18), new WaveSelection(20, 26)],
            document.Regions);
        Assert.Equal("verse", document.RegionName(new WaveSelection(20, 26)));
        Assert.True(history.Undo(document));
        Assert.Equal(
            [new WaveSelection(4, 10), new WaveSelection(12, 18)],
            document.Regions);
        Assert.Equal("verse", document.RegionName(new WaveSelection(4, 10)));
    }

    [Fact]
    public void Paste_AdaptsMonoClipToStereo()
    {
        var document = MakeConstant(frames: 4, value: 0.1f);
        var clip = new AudioClip([0.8f, 0.8f], channels: 1, sampleRate: 48000);
        document.Selection = WaveSelection.Empty;
        document.CursorFrame = 2;
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Paste(document, clip, 2)!);

        Assert.Equal(6, document.FrameCount);
        Assert.Equal(0.8f, document.Interleaved[4]);
        Assert.Equal(0.8f, document.Interleaved[5]);
    }

    [Fact]
    public void FadeIn_OnlyTouchesSoloChannel()
    {
        var document = MakeConstant(frames: 5, value: 1f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.FadeIn(document, new WaveSelection(0, 5), FadeShape.Linear, channel: 0));

        Assert.Equal(0f, document.Interleaved[0], 5);
        Assert.Equal(1f, document.Interleaved[1], 5);
        Assert.Equal(1f, document.Interleaved[8], 5);
        Assert.Equal(1f, document.Interleaved[9], 5);
    }

    [Fact]
    public void Normalize_UsesSoloChannelPeak()
    {
        var samples = new float[] { 0.5f, 1f, 0.5f, 1f };
        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Normalize(document, new WaveSelection(0, 2), channel: 0));

        var target = (float)Math.Pow(10d, -0.1d / 20d);
        Assert.Equal(target, document.Interleaved[0], 4);
        Assert.Equal(1f, document.Interleaved[1], 5);
    }

    [Fact]
    public void Delete_SilencesSoloChannel()
    {
        var document = MakeConstant(frames: 4, value: 0.4f);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Delete(document, new WaveSelection(1, 3), channel: 1));

        Assert.Equal(4, document.FrameCount);
        Assert.Equal(0.4f, document.Interleaved[0], 5);
        Assert.Equal(0.4f, document.Interleaved[1], 5);
        Assert.Equal(0.4f, document.Interleaved[2], 5);
        Assert.Equal(0f, document.Interleaved[3], 5);
        Assert.Equal(0.4f, document.Interleaved[4], 5);
        Assert.Equal(0f, document.Interleaved[5], 5);
    }

    [Fact]
    public void Reverse_OnlySoloChannel()
    {
        var samples = new float[] { 1f, 10f, 2f, 20f, 3f, 30f, 4f, 40f };
        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        new EditHistory().Do(document, ProcessEdits.Reverse(document, new WaveSelection(0, 4), channel: 0)!);

        Assert.Equal(4f, document.Interleaved[0], 5);
        Assert.Equal(10f, document.Interleaved[1], 5);
        Assert.Equal(3f, document.Interleaved[2], 5);
        Assert.Equal(20f, document.Interleaved[3], 5);
    }

    [Fact]
    public void Copy_ExtractsSoloChannel()
    {
        var samples = new float[] { 0.1f, 0.9f, 0.2f, 0.8f };
        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var clip = ProcessEdits.Copy(document, new WaveSelection(0, 2), channel: 1);
        Assert.NotNull(clip);
        Assert.Equal(1, clip.Channels);
        Assert.Equal(new[] { 0.9f, 0.8f }, clip.Interleaved);
    }

    [Fact]
    public void Paste_WritesSoloChannelWithoutChangingLength()
    {
        var document = MakeConstant(frames: 4, value: 0.1f);
        var clip = new AudioClip([0.8f, 0.8f], channels: 1, sampleRate: 48000);
        document.Selection = new WaveSelection(1, 3);
        new EditHistory().Do(document, ProcessEdits.Paste(document, clip, 1, channel: 0)!);

        Assert.Equal(4, document.FrameCount);
        Assert.Equal(0.1f, document.Interleaved[0], 5);
        Assert.Equal(0.8f, document.Interleaved[2], 5);
        Assert.Equal(0.1f, document.Interleaved[3], 5);
    }

    [Fact]
    public void FadeIn_TouchesSelectedMaskOnly()
    {
        var samples = new float[5 * 3];
        Array.Fill(samples, 1f);
        var document = new AudioDocument(samples, 48000, 3, 24, AudioFileKind.Wave, null);
        new EditHistory().Do(
            document,
            ProcessEdits.FadeIn(document, new WaveSelection(0, 5), FadeShape.Linear, channelMask: 1 | 2));

        Assert.Equal(0f, document.Interleaved[0], 5);
        Assert.Equal(0f, document.Interleaved[1], 5);
        Assert.Equal(1f, document.Interleaved[2], 5);
        Assert.Equal(1f, document.Interleaved[12], 5);
        Assert.Equal(1f, document.Interleaved[14], 5);
    }

    [Fact]
    public void SilenceSelection_ZerosRangeKeepsLengthAndRestores()
    {
        var document = MakeConstant(frames: 40, value: 0.5f);
        document.TryAddMarker(10);
        document.TryAddMarker(30);
        document.Selection = new WaveSelection(10, 20);
        document.CursorFrame = 15;
        var history = new EditHistory();
        history.Do(document, ProcessEdits.SilenceSelection(document, document.Selection)!);

        Assert.Equal(40, document.FrameCount);
        Assert.Equal(0.5f, document.Interleaved[9 * 2], 5);
        Assert.Equal(0f, document.Interleaved[10 * 2], 5);
        Assert.Equal(0f, document.Interleaved[19 * 2 + 1], 5);
        Assert.Equal(0.5f, document.Interleaved[20 * 2], 5);
        Assert.Equal(10, document.SnapshotMarkers()[0].Frame);
        Assert.Equal(30, document.SnapshotMarkers()[1].Frame);
        Assert.Equal(new WaveSelection(10, 20), document.Selection);
        Assert.Equal(10, document.CursorFrame);
        Assert.Null(ProcessEdits.SilenceSelection(document, document.Selection));

        Assert.True(history.Undo(document));
        Assert.Equal(0.5f, document.Interleaved[10 * 2], 5);
        Assert.Equal(0.5f, document.Interleaved[19 * 2 + 1], 5);
        Assert.Equal(30, document.SnapshotMarkers()[1].Frame);
    }

    [Fact]
    public void SilenceSelection_EdgeFadeRampsOutsideTheRangeAndRestores()
    {
        var document = MakeConstant(frames: 200, value: 1f);
        document.Selection = new WaveSelection(100, 110);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.SilenceSelection(document, document.Selection, edgeFade: true, fadeMs: 1)!);

        var fade = ClickGuard.FadeFrames(document.SampleRate, 1);
        Assert.Equal(48, fade);
        Assert.Equal(200, document.FrameCount);
        Assert.Equal(1f, document.Interleaved[(100 - fade - 1) * 2], 5);
        Assert.Equal(0f, document.Interleaved[(100 - 1) * 2], 5);
        Assert.Equal(0f, document.Interleaved[100 * 2], 5);
        Assert.Equal(0f, document.Interleaved[109 * 2], 5);
        Assert.Equal(0f, document.Interleaved[110 * 2], 5);
        Assert.Equal(1f, document.Interleaved[(110 + fade - 1) * 2], 5);
        Assert.Equal(1f, document.Interleaved[(110 + fade) * 2], 5);
        Assert.Equal(new WaveSelection(100, 110), document.Selection);

        Assert.True(history.Undo(document));
        Assert.Equal(1f, document.Interleaved[(100 - 1) * 2], 5);
        Assert.Equal(1f, document.Interleaved[100 * 2], 5);
        Assert.Equal(1f, document.Interleaved[110 * 2], 5);
    }

    [Fact]
    public void InsertSilence_InsertsZerosAndShiftsLaterAudioAndMarkers()
    {
        var document = MakeConstant(frames: 20, value: 1f);
        document.TryAddMarker(5);
        document.TryAddMarker(12);
        var history = new EditHistory();
        var frames = 48;
        var seconds = frames / (double)document.SampleRate;
        history.Do(document, ProcessEdits.InsertSilence(document, 8, seconds)!);

        Assert.Equal(20 + frames, document.FrameCount);
        Assert.Equal(0f, document.Interleaved[8 * 2], 5);
        Assert.Equal(0f, document.Interleaved[(8 + frames - 1) * 2 + 1], 5);
        Assert.Equal(1f, document.Interleaved[(8 + frames) * 2], 5);
        Assert.Equal(5, document.SnapshotMarkers()[0].Frame);
        Assert.Equal(12 + frames, document.SnapshotMarkers()[1].Frame);
        Assert.Equal(new WaveSelection(8, 8 + frames), document.Selection);

        Assert.True(history.Undo(document));
        Assert.Equal(20, document.FrameCount);
        Assert.Equal(1f, document.Interleaved[8 * 2], 5);
        Assert.Equal(12, document.SnapshotMarkers()[1].Frame);
        Assert.True(document.Selection.IsEmpty);
    }

    [Fact]
    public void InsertSilence_EdgeFadeRampsToSilenceAndRestores()
    {
        var document = MakeConstant(frames: 200, value: 1f);
        var history = new EditHistory();
        var silence = 10;
        var seconds = silence / (double)document.SampleRate;
        history.Do(document, ProcessEdits.InsertSilence(document, 100, seconds, edgeFade: true, fadeMs: 1)!);

        var fade = ClickGuard.FadeFrames(document.SampleRate, 1);
        Assert.Equal(48, fade);
        Assert.Equal(1f, document.Interleaved[(100 - fade - 1) * 2], 5);
        Assert.Equal(0f, document.Interleaved[(100 - 1) * 2], 5);
        Assert.Equal(0f, document.Interleaved[100 * 2], 5);
        Assert.Equal(0f, document.Interleaved[(100 + silence) * 2], 5);
        Assert.Equal(1f, document.Interleaved[(100 + silence + fade - 1) * 2], 5);
        Assert.Equal(1f, document.Interleaved[(100 + silence + fade) * 2], 5);

        Assert.True(history.Undo(document));
        Assert.Equal(200, document.FrameCount);
        Assert.Equal(1f, document.Interleaved[(100 - 1) * 2], 5);
        Assert.Equal(1f, document.Interleaved[100 * 2], 5);
    }

    [Fact]
    public void InsertSilence_RejectsNonPositiveDuration()
    {
        var document = MakeConstant(frames: 8, value: 0.5f);
        Assert.Null(ProcessEdits.InsertSilence(document, 0, 0));
        Assert.Null(ProcessEdits.InsertSilence(document, 0, double.NaN));
        Assert.Equal(8, document.FrameCount);
    }

    private static AudioDocument MakeConstant(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
    }

    private static AudioDocument MakeSine(int frames)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * 440 * i / 48000d) * 0.5f;
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
    }

    private static AudioDocument MakeNearThresholdSine(int cycles)
    {
        const int rate = 48000;
        const double hertz = 200;
        var frames = (int)Math.Round(rate / hertz * cycles);
        var peak = SilentSkip.LinearFromDb(-60) * 1.5f;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var value = (float)Math.Sin(2 * Math.PI * hertz * i / rate) * peak;
            samples[i * 2] = value;
            samples[i * 2 + 1] = value;
        }

        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static AudioDocument MakeToneWithSilence(int audible, int silent, int tail, int sampleRate = 1000)
    {
        var frames = audible + silent + tail;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            if (i >= audible && i < audible + silent)
            {
                continue;
            }

            samples[i * 2] = 0.5f;
            samples[i * 2 + 1] = 0.5f;
        }

        return new AudioDocument(samples, sampleRate, 2, 24, AudioFileKind.Wave, null);
    }

    private static void StampHit(float[] interleaved, int channels, int frame, float level)
    {
        for (var i = 0; i < 8; i++)
        {
            var index = (frame + i) * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                interleaved[index + ch] = level;
            }
        }
    }
}
