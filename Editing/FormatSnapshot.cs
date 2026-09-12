using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal readonly record struct FormatSnapshot(
    float[] Samples,
    int SampleRate,
    int Channels,
    int BitsPerSample,
    float[] OriginSamples,
    int OriginSampleRate,
    int OriginChannels,
    int OriginBitsPerSample,
    WaveSelection Selection,
    WaveSelection SampleLoop,
    WaveRegion[] Regions,
    long Cursor,
    MarkerSnapshot[] Markers)
{
    public static FormatSnapshot Capture(AudioDocument document) =>
        new(
            document.Interleaved,
            document.SampleRate,
            document.Channels,
            document.BitsPerSample,
            document.FormatOriginSamples,
            document.FormatOriginSampleRate,
            document.FormatOriginChannels,
            document.FormatOriginBitsPerSample,
            document.Selection,
            document.SampleLoop,
            document.SnapshotRegions(),
            document.CursorFrame,
            document.SnapshotMarkers());

    public void Restore(AudioDocument document)
    {
        document.ReplaceAudio(Samples, SampleRate, Channels, BitsPerSample);
        document.SetFormatOrigin(OriginSamples, OriginSampleRate, OriginChannels, OriginBitsPerSample);
        document.ReplaceMarkers(Markers, markDirty: false);
        document.Selection = Selection;
        document.SetSampleLoop(SampleLoop, markDirty: false);
        document.SetRegions(Regions, markDirty: false);
        document.CursorFrame = Cursor;
    }
}
