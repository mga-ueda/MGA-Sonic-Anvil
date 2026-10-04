using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// FileStream が BlockAlign 未満の端数を返すと、24-bit（3/6 バイト）は以降ずっとずれる。
/// 端数は内部に残し、フレーム単位でだけ上位へ渡す。
/// </summary>
internal sealed class BlockAlignedWaveStream : WaveStream
{
    private readonly WaveStream _inner;
    private byte[] _leftover = [];
    private int _leftoverLength;
    private byte[] _scratch = [];

    public BlockAlignedWaveStream(WaveStream inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override WaveFormat WaveFormat => _inner.WaveFormat;

    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position - _leftoverLength;
        set
        {
            _leftoverLength = 0;
            _inner.Position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var block = Math.Max(1, WaveFormat.BlockAlign);
        if (count < block || offset < 0 || offset > buffer.Length)
        {
            return 0;
        }

        count = Math.Min(count, buffer.Length - offset);
        count -= count % block;
        if (count < block)
        {
            return 0;
        }

        var written = 0;
        while (written < count)
        {
            var want = count - written;
            EnsureScratch(_leftoverLength + want);
            if (_leftoverLength > 0)
            {
                Array.Copy(_leftover, 0, _scratch, 0, _leftoverLength);
            }

            var n = _inner.Read(_scratch, _leftoverLength, want);
            if (n <= 0)
            {
                break;
            }

            var have = _leftoverLength + n;
            var aligned = have - (have % block);
            if (aligned > 0)
            {
                var copy = Math.Min(aligned, count - written);
                copy -= copy % block;
                Array.Copy(_scratch, 0, buffer, offset + written, copy);
                written += copy;
                var rest = have - copy;
                EnsureLeftover(rest);
                if (rest > 0)
                {
                    Array.Copy(_scratch, copy, _leftover, 0, rest);
                }

                _leftoverLength = rest;
            }
            else
            {
                EnsureLeftover(have);
                Array.Copy(_scratch, 0, _leftover, 0, have);
                _leftoverLength = have;
            }
        }

        return written;
    }

    public override TimeSpan TotalTime => _inner.TotalTime;

    public override TimeSpan CurrentTime
    {
        get => _inner.CurrentTime;
        set => _inner.CurrentTime = value;
    }

    private void EnsureScratch(int size)
    {
        if (_scratch.Length < size)
        {
            _scratch = new byte[size];
        }
    }

    private void EnsureLeftover(int size)
    {
        if (_leftover.Length < size)
        {
            _leftover = new byte[Math.Max(size, Math.Max(1, WaveFormat.BlockAlign))];
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
