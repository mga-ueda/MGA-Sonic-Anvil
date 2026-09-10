using System.IO;
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
}
