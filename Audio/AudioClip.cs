using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

internal sealed class AudioClip
{
    public AudioClip(
        float[] interleaved,
        int channels,
        int sampleRate,
        IReadOnlyList<MarkerSnapshot>? markers = null,
        IReadOnlyList<WaveRegion>? regions = null)
    {
        Interleaved = interleaved;
        Channels = Math.Max(1, channels);
        SampleRate = Math.Max(1, sampleRate);
        Markers = markers is { Count: > 0 } ? [.. markers] : [];
        Regions = regions is { Count: > 0 } ? [.. regions] : [];
    }

    public float[] Interleaved { get; }

    public int Channels { get; }

    public int SampleRate { get; }

    public IReadOnlyList<MarkerSnapshot> Markers { get; }

    public IReadOnlyList<WaveRegion> Regions { get; }

    public int FrameCount => Interleaved.Length / Channels;

    public bool IsEmpty => FrameCount <= 0;

    public float[] AdaptTo(int destChannels)
    {
        destChannels = Math.Max(1, destChannels);
        if (destChannels == Channels)
        {
            return Interleaved;
        }

        var frames = FrameCount;
        var dest = new float[frames * destChannels];
        for (var frame = 0; frame < frames; frame++)
        {
            ChannelMix.Downmix(Interleaved, frame * Channels, Channels, out var left, out var right);
            if (destChannels == 1)
            {
                dest[frame] = 0.5f * (left + right);
                continue;
            }

            dest[frame * destChannels] = left;
            dest[frame * destChannels + 1] = right;
        }

        return dest;
    }
}
