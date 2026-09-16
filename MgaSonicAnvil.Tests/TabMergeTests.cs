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
    public void Mix_ResamplesLaterTabsToFirstRate()
    {
        var first = new AudioDocument(new float[8], 48000, 2, 24, AudioFileKind.Wave, null);
        var second = new AudioDocument(new float[4], 24000, 2, 16, AudioFileKind.Wave, null);
        second.ReplaceMarkers([new MarkerSnapshot(1, "join")], markDirty: false);

        var merged = TabMerge.Mix([first, second]);

        Assert.Equal(48000, merged.SampleRate);
        Assert.Equal(24, merged.BitsPerSample);
        Assert.Equal(4, merged.FrameCount);
        Assert.Equal(new[] { 2L }, merged.Markers.Select(marker => marker.Frame).ToArray());
    }

    [Fact]
    public void Mix_RemixesMonoOntoFirstStereo()
    {
        var stereo = Document([1f, 0f, 1f, 0f], channels: 2);
        var mono = new AudioDocument([0.5f, -0.25f], 48000, 1, 24, AudioFileKind.Wave, null);

        var merged = TabMerge.Mix([stereo, mono]);

        Assert.Equal(2, merged.Channels);
        Assert.Equal(2, merged.FrameCount);
        Assert.Equal(1.5f, merged.Interleaved[0], 3);
        Assert.Equal(0.5f, merged.Interleaved[1], 3);
        Assert.Equal(0.75f, merged.Interleaved[2], 3);
        Assert.Equal(-0.25f, merged.Interleaved[3], 3);
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
