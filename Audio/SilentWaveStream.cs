using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>音声トラックの無い動画用。指定尺の無音 PCM を返す。</summary>
internal sealed class SilentWaveStream : WaveStream
{
    private readonly WaveFormat _format;
    private long _length;
    private long _position;

    public SilentWaveStream(int sampleRate, int channels, int bitsPerSample, long frameCount)
    {
        var bits = bitsPerSample is 8 or 16 or 24 or 32 ? bitsPerSample : 16;
        _format = new WaveFormat(Math.Max(1, sampleRate), bits, Math.Max(1, channels));
        _length = Math.Max(1, frameCount) * _format.BlockAlign;
    }

    public override WaveFormat WaveFormat => _format;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, _length);
    }

    public void GrowToFrames(long frameCount)
    {
        var next = Math.Max(1, frameCount) * (long)_format.BlockAlign;
        if (next > _length)
        {
            _length = next;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (count <= 0 || _position >= _length)
        {
            return 0;
        }

        var remain = (int)Math.Min(count, _length - _position);
        var aligned = remain - (remain % Math.Max(1, _format.BlockAlign));
        if (aligned <= 0)
        {
            return 0;
        }

        Array.Clear(buffer, offset, aligned);
        _position += aligned;
        return aligned;
    }
}
