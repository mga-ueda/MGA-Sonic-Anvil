namespace MgaSonicAnvil.Audio;

internal readonly record struct EmbeddedCueMarker(long Frame, string Comment);

internal readonly record struct EmbeddedSampleLoop(long StartFrame, long EndFrame);

internal readonly record struct EmbeddedRegion(long StartFrame, long EndFrame);

internal sealed class EmbeddedAudioMeta
{
    public static EmbeddedAudioMeta Empty { get; } = new([], null, []);

    public EmbeddedAudioMeta(
        IReadOnlyList<EmbeddedCueMarker> markers,
        EmbeddedSampleLoop? sampleLoop,
        IReadOnlyList<EmbeddedRegion>? regions = null)
    {
        Markers = markers;
        SampleLoop = sampleLoop;
        Regions = regions ?? [];
    }

    public IReadOnlyList<EmbeddedCueMarker> Markers { get; }

    public EmbeddedSampleLoop? SampleLoop { get; }

    public IReadOnlyList<EmbeddedRegion> Regions { get; }

    public bool IsEmpty => Markers.Count == 0 && SampleLoop is null && Regions.Count == 0;

    public void Apply(AudioDocument document)
    {
        if (Markers.Count > 0)
        {
            var snapshots = new MarkerSnapshot[Markers.Count];
            for (var i = 0; i < Markers.Count; i++)
            {
                snapshots[i] = new MarkerSnapshot(Markers[i].Frame, Markers[i].Comment);
            }

            document.ReplaceMarkers(snapshots, markDirty: false);
        }

        if (SampleLoop is { } loop)
        {
            document.SetSampleLoop(new WaveSelection(loop.StartFrame, loop.EndFrame), markDirty: false);
        }

        if (Regions.Count > 0)
        {
            var ranges = new WaveSelection[Regions.Count];
            for (var i = 0; i < Regions.Count; i++)
            {
                ranges[i] = new WaveSelection(Regions[i].StartFrame, Regions[i].EndFrame);
            }

            document.SetRegions(ranges, markDirty: false);
        }
    }
}
