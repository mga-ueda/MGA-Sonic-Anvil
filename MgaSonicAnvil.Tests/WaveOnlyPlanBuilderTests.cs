using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Wwise;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveOnlyPlanBuilderTests
{
    [Fact]
    public void NoMarkers_ExportsWholeFile()
    {
        var document = MakeDocument(frames: 1000);
        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.Single(regions);
        Assert.Equal(0, regions[0].StartFrame);
        Assert.Equal(1000, regions[0].EndFrame);
        Assert.Equal(WaveOnlyRegionKind.Body, regions[0].Kind);
        Assert.True(regions[0].Export);
    }

    [Fact]
    public void SampleLoop_SplitsIntroLoopExit()
    {
        var document = MakeDocument(frames: 1000);
        document.SetSampleLoop(new WaveSelection(200, 800), markDirty: false);

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.Equal(3, regions.Count);
        Assert.Equal(WaveOnlyRegionKind.Body, regions[0].Kind);
        Assert.Equal(0, regions[0].StartFrame);
        Assert.Equal(200, regions[0].EndFrame);
        Assert.Equal(WaveOnlyRegionKind.Loop, regions[1].Kind);
        Assert.True(regions[1].LoopInfinite);
        Assert.Equal(200, regions[1].StartFrame);
        Assert.Equal(800, regions[1].EndFrame);
        Assert.Equal(WaveOnlyRegionKind.Exit, regions[2].Kind);
        Assert.Equal(800, regions[2].StartFrame);
        Assert.Equal(1000, regions[2].EndFrame);

        var segments = WaveOnlyPlanBuilder.Build(document).Segments;
        Assert.Equal(2, segments.Count);
        Assert.Equal("tone_a", segments[0].Name);
        Assert.Equal("tone_b", segments[1].Name);
        Assert.Equal(0, segments[0].StartFrame);
        Assert.Equal(200, segments[0].EndFrame);
        Assert.False(segments[0].LoopInfinite);
        Assert.Equal(200, segments[1].StartFrame);
        Assert.Equal(1000, segments[1].EndFrame);
        Assert.Equal(200, segments[1].EntryCueFrame);
        Assert.Equal(800, segments[1].ExitCueFrame);
        Assert.True(segments[1].LoopInfinite);
    }

    [Fact]
    public void TwoPlainMarkers_BecomeImplicitLoop()
    {
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(100);
        document.TryAddMarker(400);

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.Contains(regions, region => region.Kind == WaveOnlyRegionKind.Loop
            && region.StartFrame == 100
            && region.EndFrame == 400);
    }

    [Fact]
    public void RoleMarkers_BuildAdjacentRegions()
    {
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(0);
        document.TrySetMarkerComment(0, "-A");
        document.TryAddMarker(200);
        document.TrySetMarkerComment(200, "-L");
        document.TryAddMarker(600);
        document.TrySetMarkerComment(600, "-E");

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.Equal(WaveOnlyRegionKind.Anacrusis, regions[0].Kind);
        Assert.Equal(WaveOnlyRegionKind.Loop, regions[1].Kind);
        Assert.Equal(WaveOnlyRegionKind.Exit, regions[2].Kind);

        var segment = Assert.Single(WaveOnlyPlanBuilder.Build(document).Segments);
        Assert.Equal("tone", segment.Name);
        Assert.Equal(0, segment.StartFrame);
        Assert.Equal(1000, segment.EndFrame);
        Assert.Equal(200, segment.EntryCueFrame);
        Assert.Equal(600, segment.ExitCueFrame);
        Assert.True(segment.LoopInfinite);
    }

    [Fact]
    public void ExitRegion_AttachesToPrecedingLoopAsExitCue()
    {
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(100);
        document.TrySetMarkerComment(100, "-L");
        document.TryAddMarker(500);
        document.TrySetMarkerComment(500, "-E");

        var plan = WaveOnlyPlanBuilder.Build(document);
        var loop = Assert.Single(plan.Segments, segment => segment.LoopInfinite);
        Assert.Equal(100, loop.StartFrame);
        Assert.Equal(1000, loop.EndFrame);
        Assert.Equal(100, loop.EntryCueFrame);
        Assert.Equal(500, loop.ExitCueFrame);
        Assert.DoesNotContain(plan.Segments, segment =>
            segment.StartFrame == 500 && !segment.LoopInfinite);
    }

    [Fact]
    public void RemoveMarker_HasNoEffect_AllRegionsExported()
    {
        // このアプリでは -R（除外）を機能させない。通常マーカーと同じ分割点になる。
        var document = MakeDocument(frames: 800);
        document.TryAddMarker(100);
        document.TrySetMarkerComment(100, "-R");
        document.TryAddMarker(300);
        document.TrySetMarkerComment(300, "-L");

        var plan = WaveOnlyPlanBuilder.Build(document);

        Assert.All(plan.Regions, region => Assert.True(region.Export));
        Assert.Equal(plan.Regions.Count, plan.ExportRegions.Count);
        Assert.Contains(plan.ExportRegions, region => region.LoopInfinite);
    }

    [Fact]
    public void RemoveMarkersOnly_ExportWholeFile()
    {
        // -R しか無い場合は特殊リージョン無し扱い（全体 1 リージョン）。
        var document = MakeDocument(frames: 800);
        document.TryAddMarker(100);
        document.TrySetMarkerComment(100, "-R");

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        var whole = Assert.Single(regions);
        Assert.Equal(WaveOnlyRegionKind.Body, whole.Kind);
        Assert.Equal(0, whole.StartFrame);
        Assert.Equal(800, whole.EndFrame);
    }

    [Fact]
    public void TwoMarkers_WithRemoveComment_DoNotBecomeImplicitLoop()
    {
        // -R は 2 点特例（暗黙ループ）の対象外のまま。
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(100);
        document.TrySetMarkerComment(100, "-R");
        document.TryAddMarker(400);

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.DoesNotContain(regions, region => region.Kind == WaveOnlyRegionKind.Loop);
    }

    [Fact]
    public void DocumentRegions_UsedWhenNoSpecialMarkers()
    {
        var document = MakeDocument(frames: 500);
        document.SetRegions([new WaveSelection(10, 80), new WaveSelection(200, 260)], markDirty: false);

        var regions = WaveOnlyPlanBuilder.BuildRegions(document);

        Assert.Equal(2, regions.Count);
        Assert.Equal(10, regions[0].StartFrame);
        Assert.Equal(80, regions[0].EndFrame);
        Assert.Equal(200, regions[1].StartFrame);
        Assert.Equal(260, regions[1].EndFrame);
    }

    [Fact]
    public void MultipleBodySegments_UseLetterSuffixes()
    {
        var document = MakeDocument(frames: 500);
        document.SetRegions([new WaveSelection(10, 80), new WaveSelection(200, 260)], markDirty: false);

        var segments = WaveOnlyPlanBuilder.Build(document).Segments;

        Assert.Equal(2, segments.Count);
        Assert.Equal("tone_a", segments[0].Name);
        Assert.Equal("tone_b", segments[1].Name);
    }

    [Fact]
    public void SanitizeWwiseName_ReplacesIllegalCharacters()
    {
        Assert.Equal("a_b_c", WaveOnlyPlanBuilder.SanitizeWwiseName("a/b:c"));
    }

    private static AudioDocument MakeDocument(int frames)
    {
        var samples = new float[frames * 2];
        return new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, @"C:\tmp\tone.wav");
    }
}
