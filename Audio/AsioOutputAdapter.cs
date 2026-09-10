using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// ASIO コールバックへ常に IEEE float を満杯で渡す。
/// 終端は無音で埋め、Read が 0 を返してドライバが落ちるのを防ぐ。
/// </summary>
internal sealed class AsioOutputAdapter : IWaveProvider
{
    private readonly PlaybackSampleProvider _source;
    private float[] _scratch = new float[16384];

    public AsioOutputAdapter(PlaybackSampleProvider source, int outputChannels)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        var channels = Math.Clamp(outputChannels, 1, ChannelLayout.MaxChannels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
            Math.Max(1, source.WaveFormat.SampleRate),
            channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var outChannels = WaveFormat.Channels;
        var frameBytes = 4 * outChannels;
        var frames = count / frameBytes;
        if (frames <= 0 || buffer.Length < offset + frames * frameBytes)
        {
            return 0;
        }

        var sourceChannels = Math.Max(1, _source.WaveFormat.Channels);
        var needed = frames * sourceChannels;
        if (_scratch.Length < needed)
        {
            _scratch = new float[needed];
        }

        var got = _source.Read(_scratch, 0, needed);
        var gotFrames = Math.Max(0, got / sourceChannels);
        var dest = buffer.AsSpan(offset, frames * frameBytes);
        dest.Clear();
        var copy = Math.Min(sourceChannels, outChannels);
        for (var frame = 0; frame < gotFrames; frame++)
        {
            var src = frame * sourceChannels;
            var at = frame * frameBytes;
            for (var channel = 0; channel < copy; channel++)
            {
                BitConverter.TryWriteBytes(dest.Slice(at + channel * 4, 4), _scratch[src + channel]);
            }
        }

        return frames * frameBytes;
    }
}
