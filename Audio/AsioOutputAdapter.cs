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
        var channels = Math.Clamp(outputChannels, 1, 2);
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
        for (var frame = 0; frame < gotFrames; frame++)
        {
            var src = frame * sourceChannels;
            var left = _scratch[src];
            var right = sourceChannels > 1 ? _scratch[src + 1] : left;
            var at = frame * frameBytes;
            BitConverter.TryWriteBytes(dest.Slice(at, 4), left);
            if (outChannels > 1)
            {
                BitConverter.TryWriteBytes(dest.Slice(at + 4, 4), right);
            }
        }

        return frames * frameBytes;
    }
}
