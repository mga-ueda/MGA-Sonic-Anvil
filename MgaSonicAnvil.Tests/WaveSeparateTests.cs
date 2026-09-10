using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveSeparateTests
{
    [Theory]
    [InlineData(1, 9, "_1")]
    [InlineData(9, 9, "_9")]
    [InlineData(1, 10, "_01")]
    [InlineData(10, 10, "_10")]
    [InlineData(3, 100, "_003")]
    public void IndexedBaseName_PadsToCountWidth(int index, int count, string suffix)
    {
        Assert.Equal("tone" + suffix, WaveSeparate.IndexedBaseName("tone", index, count));
    }

    [Fact]
    public void IndexedBaseName_SanitizesBase()
    {
        Assert.Equal("a_b_1", WaveSeparate.IndexedBaseName("a:b", 1, 1));
    }

    [Fact]
    public void FromMarkers_WithoutMarkers_CannotSeparate()
    {
        var document = MakeDocument(100);
        Assert.False(WaveSeparate.TrySegmentsByMarkers(document, out var segments));
        Assert.Equal([new WaveSelection(0, 100)], WaveSeparate.FromMarkers(document));
        Assert.Empty(segments);
    }

    [Fact]
    public void FromMarkers_OnlyAtEdge_CannotSeparate()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(0);
        document.TryAddMarker(100);
        Assert.False(WaveSeparate.TrySegmentsByMarkers(document, out _));
    }

    [Fact]
    public void FromMarkers_OneInterior_SplitsInTwo()
    {
        var document = MakeDocument(100);
        document.TryAddMarker(40);
        Assert.True(WaveSeparate.TrySegmentsByMarkers(document, out var segments));
        Assert.Equal(
            [new WaveSelection(0, 40), new WaveSelection(40, 100)],
            segments);
    }

    [Fact]
    public void FromMarkers_UsesAllMarkersIncludingEnds()
    {
        var document = MakeDocument(80);
        document.TryAddMarker(0);
        document.TryAddMarker(20);
        document.TryAddMarker(60);
        document.TryAddMarker(80);
        Assert.True(WaveSeparate.TrySegmentsByMarkers(document, out var segments));
        Assert.Equal(
            [new WaveSelection(0, 20), new WaveSelection(20, 60), new WaveSelection(60, 80)],
            segments);
    }

    [Fact]
    public void FromRegions_Empty_CannotSeparate()
    {
        var document = MakeDocument(100);
        Assert.False(WaveSeparate.TrySegmentsByRegions(document, out var segments));
        Assert.Empty(segments);
    }

    [Fact]
    public void FromRegions_ExportsEachRegion()
    {
        var document = MakeDocument(100);
        document.SetRegions([new WaveSelection(10, 30), new WaveSelection(50, 80)]);
        Assert.True(WaveSeparate.TrySegmentsByRegions(document, out var segments));
        Assert.Equal(
            [new WaveSelection(10, 30), new WaveSelection(50, 80)],
            segments);
    }

    [Fact]
    public void FromRegions_SingleRegion_IsEnough()
    {
        var document = MakeDocument(100);
        document.SetRegions([new WaveSelection(20, 70)]);
        Assert.True(WaveSeparate.TrySegmentsByRegions(document, out var segments));
        Assert.Equal([new WaveSelection(20, 70)], segments);
    }

    private static AudioDocument MakeDocument(int frames) =>
        new(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);
}
