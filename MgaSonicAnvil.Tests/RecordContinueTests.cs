using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class RecordContinueTests
{
    [Fact]
    public void EmptyDocument_CanStartAsRecording()
    {
        var document = new AudioDocument([], 48000, 2, 24, AudioFileKind.Wave, null)
        {
            CanContinueRecording = true,
        };
        document.SetDirty(true);

        Assert.Equal(0, document.FrameCount);
        Assert.True(RecordContinue.Matches(document, 48000, 2));
    }

    [Fact]
    public void Matches_OnlyUnsavedRecordingWithSameFormat()
    {
        var document = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null)
        {
            CanContinueRecording = true,
        };

        Assert.True(RecordContinue.Matches(document, 48000, 2));
        Assert.False(RecordContinue.Matches(document, 44100, 2));
        Assert.False(RecordContinue.Matches(document, 48000, 1));

        document.CanContinueRecording = false;
        Assert.False(RecordContinue.Matches(document, 48000, 2));
    }

    [Fact]
    public void MarkSaved_ClearsContinueFlag()
    {
        var document = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null)
        {
            CanContinueRecording = true,
        };
        document.MarkSaved(@"C:\tmp\rec.wav", AudioFileKind.Wave);

        Assert.False(document.CanContinueRecording);
        Assert.False(document.IsDirty);
        Assert.False(RecordContinue.Matches(document, 48000, 2));
    }

    [Fact]
    public void ReplaceAudio_CanSkipPeakRebuild()
    {
        var document = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null);
        var oldPeaks = document.Peaks.FrameCount;
        var next = new float[200];

        document.ReplaceAudio(next, 48000, 2, 24, rebuildPeaks: false);
        Assert.Equal(100, document.FrameCount);
        Assert.Equal(oldPeaks, document.Peaks.FrameCount);

        document.ReplaceAudio(next, 48000, 2, 24, rebuildPeaks: true);
        Assert.Equal(100, document.Peaks.FrameCount);
    }

    [Fact]
    public void DrawSettleFrames_IsAboutHalfSecond()
    {
        Assert.Equal(24000, RecordContinue.DrawSettleFrames(48000));
        Assert.Equal(1, RecordContinue.DrawSettleFrames(1));
    }

    [Fact]
    public void AppendLiveSamples_GrowsFrameCountWithoutReplacingBuffer()
    {
        var document = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null);
        var peakFrames = document.Peaks.FrameCount;

        document.AppendLiveSamples([0.2f, -0.3f, 0.4f, -0.5f]);

        Assert.Equal(4, document.FrameCount);
        Assert.True(document.Interleaved.Length >= 8);
        Assert.Equal(peakFrames, document.Peaks.FrameCount);
        Assert.Equal(0.2f, document.Interleaved[4]);
        Assert.Equal(-0.5f, document.Interleaved[7]);

        document.RefreshPeaks();
        Assert.Equal(4, document.Peaks.FrameCount);

        document.CommitLiveSamples(rebuildPeaks: false);
        Assert.Equal(8, document.Interleaved.Length);
        Assert.Equal(4, document.FrameCount);
    }

    [Fact]
    public void CountPrefixColumns_StopsAtSettledFrames()
    {
        Assert.Equal(0, RecordContinue.CountPrefixColumns(0, 100, 10, 0));
        Assert.Equal(5, RecordContinue.CountPrefixColumns(0, 100, 10, 50));
        Assert.Equal(10, RecordContinue.CountPrefixColumns(0, 100, 10, 100));
        Assert.Equal(0, RecordContinue.CountPrefixColumns(80, 40, 8, 80));
        Assert.Equal(2, RecordContinue.CountPrefixColumns(80, 40, 8, 90));
    }

    [Fact]
    public void SessionCapture_RestoresContinueFlag()
    {
        var source = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null)
        {
            CanContinueRecording = true,
        };
        source.MarkUnsaved(null);
        var snap = DocumentSessionStore.Capture(source, new SessionViewState(0, 1, 1, 0, true, []), 0);
        Assert.True(snap.CanContinueRecording);

        var target = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null);
        DocumentSessionStore.ApplyMeta(target, snap);
        Assert.True(target.CanContinueRecording);

        source.MarkSaved(@"C:\tmp\rec.wav", AudioFileKind.Wave);
        snap = DocumentSessionStore.Capture(source, new SessionViewState(0, 1, 1, 0, true, []), 0);
        target = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null)
        {
            CanContinueRecording = true,
        };
        DocumentSessionStore.ApplyMeta(target, snap);
        Assert.False(target.CanContinueRecording);
    }

    [Fact]
    public void ApplyMeta_UntitledDirty_IsContinuableRecording()
    {
        var target = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null);
        DocumentSessionStore.ApplyMeta(target, new OpenDocumentSnapshot { Dirty = true });
        Assert.True(target.CanContinueRecording);
    }

    [Fact]
    public void SessionCapture_RestoresMp3Kind()
    {
        var source = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Mp3, null)
        {
            CanContinueRecording = true,
        };
        var snap = DocumentSessionStore.Capture(source, new SessionViewState(0, 1, 1, 0, true, []), 0);
        Assert.Equal(nameof(AudioFileKind.Mp3), snap.SourceKind);

        var target = new AudioDocument(new float[4], 48000, 2, 24, AudioFileKind.Wave, null);
        DocumentSessionStore.ApplyMeta(target, snap);
        Assert.Equal(AudioFileKind.Mp3, target.SourceKind);
        Assert.False(target.AllowsRegionsAndLoops);
    }
}
