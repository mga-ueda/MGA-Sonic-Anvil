namespace MgaSonicAnvil.Audio;

internal readonly record struct EmbeddedCueMarker(long Frame, string Comment);

internal readonly record struct EmbeddedSampleLoop(long StartFrame, long EndFrame);

internal sealed class EmbeddedAudioMeta
{
    public static EmbeddedAudioMeta Empty { get; } = new([], null);

    public EmbeddedAudioMeta(IReadOnlyList<EmbeddedCueMarker> markers, EmbeddedSampleLoop? sampleLoop)
    {
        Markers = markers;
        SampleLoop = sampleLoop;
    }

    public IReadOnlyList<EmbeddedCueMarker> Markers { get; }

    public EmbeddedSampleLoop? SampleLoop { get; }

    public bool IsEmpty => Markers.Count == 0 && SampleLoop is null;

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
            document.SetSampleLoop(new WaveSelection(loop.StartFrame, loop.EndFrame));
        }
    }
}
