using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TabMergeTests
{
    [Fact]
    public void Mix_SumsOverlappingSamples()
    {
        var first = Document([0.1f, -0.2f, 0.3f, -0.4f], channels: 2);
        var second = Document([0.5f, 0.6f], channels: 2);

        var merged = TabMerge.Mix([first, second]);

        Assert.Equal(48000, merged.SampleRate);
        Assert.Equal(2, merged.Channels);
        Assert.Equal(24, merged.BitsPerSample);
        Assert.Equal(AudioFileKind.Wave, merged.SourceKind);
        Assert.Null(merged.SourcePath);
        Assert.True(merged.IsDirty);
        Assert.Equal(2, merged.FrameCount);
        Assert.Equal(0.6f, merged.Interleaved[0], 5);
        Assert.Equal(0.4f, merged.Interleaved[1], 5);
        Assert.Equal(0.3f, merged.Interleaved[2], 5);
        Assert.Equal(-0.4f, merged.Interleaved[3], 5);
    }

    [Fact]
    public void Mix_LengthIsLongestTab()
    {
        var shortDoc = Document([1f, 1f], channels: 2);
        var longDoc = Document(new float[8], channels: 2);

        var merged = TabMerge.Mix([shortDoc, longDoc]);

        Assert.Equal(4, merged.FrameCount);
        Assert.Equal(1f, merged.Interleaved[0]);
        Assert.Equal(0f, merged.Interleaved[2]);
    }

    [Fact]
    public void Mix_MergesMarkersAndDropsExactDuplicates()
    {
        var first = Document(new float[8], channels: 2);
        first.ReplaceMarkers([new MarkerSnapshot(1, "A"), new MarkerSnapshot(3, "keep")], markDirty: false);
        var second = Document(new float[8], channels: 2);
        second.ReplaceMarkers([new MarkerSnapshot(1, "A"), new MarkerSnapshot(2, "B")], markDirty: false);

        var merged = TabMerge.Mix([first, second]);

        Assert.Equal(new[] { 1L, 2L, 3L }, merged.Markers.Select(marker => marker.Frame).ToArray());
        Assert.Equal("A", merged.Markers.First(marker => marker.Frame == 1).Comment);
        Assert.Equal("B", merged.Markers.First(marker => marker.Frame == 2).Comment);
        Assert.Equal("keep", merged.Markers.First(marker => marker.Frame == 3).Comment);
    }

    [Fact]
    public void Mix_KeepsFirstMarkerWhenSameFrameDifferentComment()
    {
        var first = Document(new float[8], channels: 2);
        first.ReplaceMarkers([new MarkerSnapshot(1, "first")], markDirty: false);
        var second = Document(new float[8], channels: 2);
        second.ReplaceMarkers([new MarkerSnapshot(1, "second")], markDirty: false);

        var merged = TabMerge.Mix([first, second]);

        Assert.Single(merged.Markers);
        Assert.Equal("first", merged.Markers[0].Comment);
    }

    [Fact]
    public void Mix_MergesRegionsWithoutOffsetAndDropsSameRange()
    {
        var first = Document(new float[8], channels: 2);
        first.SetRegions([new WaveRegion(0, 2, "head")], markDirty: false);
        var second = Document(new float[8], channels: 2);
        second.SetRegions([new WaveRegion(0, 2, "dupe"), new WaveRegion(1, 3, "tail")], markDirty: false);

        var merged = TabMerge.Mix([first, second]);

        var regions = merged.SnapshotRegions();
        Assert.Equal(2, regions.Length);
        Assert.Equal(new WaveSelection(0, 2), regions[0].Range);
        Assert.Equal("head", regions[0].Name);
        Assert.Equal(new WaveSelection(1, 3), regions[1].Range);
        Assert.Equal("tail", regions[1].Name);
    }

    [Fact]
    public void Mix_RejectsDifferentSampleRateAndNamesTheFile()
    {
        var first = new AudioDocument(new float[8], 48000, 2, 24, AudioFileKind.Wave, null);
        var second = new AudioDocument(new float[4], 24000, 2, 24, AudioFileKind.Wave, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TabMerge.Mix([first, second], out _, names: ["keep.wav", "other.wav"]));

        Assert.Contains("other.wav", ex.Message);
        Assert.Contains("24000", ex.Message);
        Assert.DoesNotContain("keep.wav", ex.Message.Split('\n').Last());
    }

    [Fact]
    public void Mix_RejectsDifferentChannelCount()
    {
        var stereo = Document([1f, 0f], channels: 2);
        var mono = new AudioDocument([0.5f], 48000, 1, 24, AudioFileKind.Wave, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TabMerge.Mix([stereo, mono], out _, names: ["stereo.wav", "mono.wav"]));

        Assert.Contains("mono.wav", ex.Message);
    }

    [Fact]
    public void Mix_RejectsDifferentBitDepth()
    {
        var wide = new AudioDocument(new float[2], 48000, 2, 24, AudioFileKind.Wave, null);
        var narrow = new AudioDocument(new float[2], 48000, 2, 16, AudioFileKind.Wave, null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            TabMerge.Mix([wide, narrow], out _, names: ["wide.wav", "narrow.wav"]));

        Assert.Contains("narrow.wav", ex.Message);
        Assert.Contains("16", ex.Message);
    }

    [Fact]
    public void Mix_ReportsOverageWithoutChangingSamples()
    {
        var first = Document([0.8f, -0.2f], channels: 1);
        var second = new AudioDocument([0.5f, -1.0f], 48000, 1, 24, AudioFileKind.Wave, null);

        var merged = TabMerge.Mix([first, second], out var clip);

        Assert.True(clip.ExceedsFullScale);
        Assert.Equal(1.3f, clip.Peak, 5);
        Assert.Equal(1.3f, merged.Interleaved[0], 5);
        Assert.Equal(-1.2f, merged.Interleaved[1], 5);
        Assert.Equal("2.3", TabMerge.FormatOverDb(clip.OverDb));
    }

    [Fact]
    public void WarnMergeClip_LeavesABlankLineAndUsesYesNoWords()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var japanese = UiStrings.WarnMergeClip("1");
            Assert.Contains("切り詰められます。\n\nYes:", japanese);
            Assert.Contains("\nNo:", japanese);
            Assert.DoesNotContain("イエス", japanese);
            Assert.DoesNotContain("ノー", japanese);
            Assert.Equal("OK", UiStrings.ButtonOk);
            Assert.Equal("Cancel", UiStrings.ButtonCancel);

            UiStrings.SetLanguage(UiLanguage.English);
            var english = UiStrings.WarnMergeClip("1");
            Assert.Contains("excess.\n\nYes:", english);
            Assert.Contains("\nNo:", english);
            Assert.Equal("OK", UiStrings.ButtonOk);
            Assert.Equal("Cancel", UiStrings.ButtonCancel);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FormatOverDb_RoundsOneDecibelToOne()
    {
        var peak = Math.Pow(10d, 1d / 20d);
        Assert.Equal("1", TabMerge.FormatOverDb(LevelMeterEngine.ToDb(peak)));
    }

    [Fact]
    public void LowerByOverage_ScalesTheWholeMixToFullScale()
    {
        var samples = new[] { 1.3f, -0.65f, 0.2f };
        TabMerge.LowerByOverage(samples, 1.3f);

        Assert.InRange(Math.Abs(samples[0]), 0.999f, 1f);
        Assert.Equal(samples[0] * -0.5f, samples[1], 4);
        Assert.Equal(samples[0] * (0.2f / 1.3f), samples[2], 4);
        Assert.False(TabMerge.MeasureClip(samples).ExceedsFullScale);
    }

    [Fact]
    public void Mix_SameFormat32BitSumsWithoutClip()
    {
        var first = new AudioDocument([0.25f, 0.25f], 48000, 2, 32, AudioFileKind.Wave, null);
        var second = new AudioDocument([0.25f, -0.25f], 48000, 2, 32, AudioFileKind.Wave, null);

        var merged = TabMerge.Mix([first, second], out var clip);

        Assert.False(clip.ExceedsFullScale);
        Assert.Equal(32, merged.BitsPerSample);
        Assert.Equal(0.5f, merged.Interleaved[0], 5);
        Assert.Equal(0f, merged.Interleaved[1], 5);
    }

    [Fact]
    public void Mix_SkipsEmptyDocuments()
    {
        var empty = Document([], channels: 2);
        empty.ReplaceMarkers([new MarkerSnapshot(0, "ghost")], markDirty: false);
        var tone = Document([0.2f, -0.2f], channels: 2);

        var merged = TabMerge.Mix([empty, tone]);

        Assert.Equal(1, merged.FrameCount);
        Assert.Equal(new[] { 0.2f, -0.2f }, merged.Interleaved);
        Assert.Empty(merged.Markers);
    }

    [Fact]
    public void AdaptChannels_CopiesDiscreteLanesWhenUpmixing()
    {
        var quad = new[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var dest = TabMerge.AdaptChannels(quad, 4, 6);
        Assert.Equal(6, dest.Length);
        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0f, 0f }, dest);
    }

    [Fact]
    public void MergedBaseName_IsShortFixedName()
    {
        Assert.Equal("Bounce", TabMerge.MergedBaseName);
    }

    [Fact]
    public void InsertIndex_OpensToTheRightOfSelection()
    {
        Assert.Equal(3, TabMerge.InsertIndex(4, [0, 2]));
        Assert.Equal(2, TabMerge.InsertIndex(3, [0, 1]));
        Assert.Equal(4, TabMerge.InsertIndex(4, []));
        Assert.Equal(0, TabMerge.InsertIndex(0, []));
    }

    private static AudioDocument Document(float[] samples, int channels) =>
        new(samples, 48000, channels, 24, AudioFileKind.Wave, null);
}
