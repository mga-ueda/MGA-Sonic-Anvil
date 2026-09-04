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
    public void Delete_RemovesRangeAndUndoRestores()
    {
        var document = MakeSine(frames: 50);
        var original = (float[])document.Interleaved.Clone();
        var history = new EditHistory();
        document.Selection = new WaveSelection(10, 20);
        history.Do(document, ProcessEdits.Delete(document, document.Selection));

        Assert.Equal(40, document.FrameCount);
        Assert.True(history.Undo(document));
        Assert.Equal(50, document.FrameCount);
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
