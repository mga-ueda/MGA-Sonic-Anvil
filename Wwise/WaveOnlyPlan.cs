namespace MgaSonicAnvil.Wwise;

internal enum WaveOnlyRegionKind
{
    Body,
    Anacrusis,
    Loop,
    Exit,
}

internal sealed class WaveOnlyRegion
{
    public required long StartFrame { get; init; }
    public required long EndFrame { get; init; }
    public required WaveOnlyRegionKind Kind { get; init; }
    public required string Name { get; init; }

    public long FrameCount => Math.Max(0, EndFrame - StartFrame);

    public bool LoopInfinite => Kind == WaveOnlyRegionKind.Loop;

    public bool Export => FrameCount > 0;
}

/// <summary>
/// Music Segment 1 つ分。<c>-A</c> は次と、<c>-E</c> は直前（通常 <c>-L</c>）に束ねる。
/// </summary>
internal sealed class WaveOnlySegment
{
    public required string Name { get; init; }
    public required long StartFrame { get; init; }
    public required long EndFrame { get; init; }
    public required long EntryCueFrame { get; init; }
    public required long ExitCueFrame { get; init; }
    public required bool LoopInfinite { get; init; }

    public long FrameCount => Math.Max(0, EndFrame - StartFrame);

    public double DurationMs(int sampleRate) => FrameCount * 1000d / sampleRate;

    public double EntryCueMs(int sampleRate) =>
        Math.Max(0d, (EntryCueFrame - StartFrame) * 1000d / sampleRate);

    public double ExitCueMs(int sampleRate) =>
        Math.Max(0d, (ExitCueFrame - StartFrame) * 1000d / sampleRate);
}

internal sealed class WaveOnlyPlan
{
    public required string ContainerName { get; init; }

    public required IReadOnlyList<WaveOnlyRegion> Regions { get; init; }

    public required IReadOnlyList<WaveOnlySegment> Segments { get; init; }

    public IReadOnlyList<WaveOnlyRegion> ExportRegions =>
        Regions.Where(region => region.Export).ToArray();
}
