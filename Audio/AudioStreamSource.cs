using System.Threading;
using MgaSonicAnvil.Domain;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// プレイヤーモード用。MediaFoundation のデコードは専用スレッドで行い、
/// 音声コールバックはリングバッファだけ読む（MF を WaveOut スレッドで叩かない）。
/// </summary>
internal sealed class AudioStreamSource : IDisposable
{
    private const int ChunkFrames = 4096;
    private const int RingFrames = ChunkFrames * 8;
    private const int PrebufferFrames = ChunkFrames * 2;

    private WaveStream? _reader;
    private ISampleProvider? _samples;
    private readonly float[] _ring;
    private readonly float[] _pumpScratch;
    private readonly object _ringGate = new();
    private readonly AutoResetEvent _hasData = new(false);
    private readonly AutoResetEvent _hasSpace = new(true);
    private int _readPos;
    private int _writePos;
    private int _availableFrames;
    private long _frame;
    private long _pumpFrame;
    private long _pendingSeekFrame;
    private bool _hasPendingSeek;
    private int _seekVersion;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private bool _disposed;
    private bool _readerEof;

    private AudioStreamSource(WaveStream reader, long frameCount, int prebufferTimeoutMs)
    {
        _reader = reader;
        SampleRate = Math.Max(1, reader.WaveFormat.SampleRate);
        Channels = Math.Max(1, reader.WaveFormat.Channels);
        FrameCount = Math.Max(0, frameCount);
        BitsPerSample = reader.WaveFormat.BitsPerSample > 0 ? reader.WaveFormat.BitsPerSample : 16;
        _ring = new float[RingFrames * Channels];
        _pumpScratch = new float[ChunkFrames * Channels];
        ResetProvider();
        StartPump();
        if (prebufferTimeoutMs > 0)
        {
            WaitForPrebuffer(PrebufferFrames, prebufferTimeoutMs);
        }
    }

    public int SampleRate { get; }

    public int Channels { get; }

    public int BitsPerSample { get; }

    public long FrameCount { get; }

    public long Frame => _frame;

    public static AudioStreamSource Open(string path, int prebufferTimeoutMs = 3000)
    {
        var reader = AudioCodec.OpenPlaybackStream(path);
        try
        {
            var format = reader.WaveFormat;
            var block = Math.Max(1, format.BlockAlign);
            var frames = reader.Length > 0 ? reader.Length / block : 0;
            if (frames <= 0 && reader.TotalTime.TotalSeconds > 0)
            {
                frames = (long)Math.Round(reader.TotalTime.TotalSeconds * format.SampleRate);
            }

            if (frames <= 0)
            {
                reader.Dispose();
                throw new InvalidDataException(UiStrings.ErrEmptyAudioFile);
            }

            return new AudioStreamSource(reader, frames, prebufferTimeoutMs);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    public void SeekFrame(long frame, int prebufferTimeoutMs = 3000)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_reader is null)
        {
            return;
        }

        var next = Math.Clamp(frame, 0, FrameCount);
        lock (_ringGate)
        {
            _frame = next;
            _pumpFrame = next;
            _readerEof = false;
            _pendingSeekFrame = next;
            _hasPendingSeek = true;
            _readPos = 0;
            _writePos = 0;
            _availableFrames = 0;
        }

        Interlocked.Increment(ref _seekVersion);
        _hasSpace.Set();
        _hasData.Set();
        if (prebufferTimeoutMs > 0)
        {
            WaitForPrebuffer(PrebufferFrames, prebufferTimeoutMs);
        }
    }

    /// <summary>リングにある分だけ先読みを取り出す。シーク直前のフェードアウト用。</summary>
    public int DrainFrames(float[] dest, int frames) =>
        ReadFrames(dest, 0, frames, timeoutMs: 0);

    /// <summary>1 フレーム分を読む。EOF／欠落なら false。timeoutMs=0 は待たない（音声スレッド用）。</summary>
    public bool TryReadFrame(Span<float> dest, int timeoutMs = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (dest.Length < Channels)
        {
            return false;
        }

        if (_frame >= FrameCount)
        {
            return false;
        }

        if (!TryPopFrame(dest, timeoutMs))
        {
            return false;
        }

        _frame++;
        return true;
    }

    /// <summary>最大 frames 分を連続で読む。戻り値は読めたフレーム数。</summary>
    public int ReadFrames(float[] buffer, int offset, int frames, int timeoutMs = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (frames <= 0 || offset < 0 || buffer.Length < offset + frames * Channels)
        {
            return 0;
        }

        var dest = buffer.AsSpan(offset);
        var read = 0;
        var waitUntil = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (read < frames && _frame < FrameCount)
        {
            var got = TryPopFrames(dest.Slice(read * Channels), frames - read);
            if (got > 0)
            {
                _frame += got;
                read += got;
                continue;
            }

            if (IsDrained || timeoutMs <= 0)
            {
                break;
            }

            var remaining = waitUntil - Environment.TickCount64;
            if (remaining <= 0)
            {
                break;
            }

            _hasData.WaitOne((int)Math.Min(remaining, 50));
        }

        return read;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopPump();
        _samples = null;
        _reader?.Dispose();
        _reader = null;
        _hasData.Dispose();
        _hasSpace.Dispose();
    }

    private void StartPump()
    {
        _pumpCts = new CancellationTokenSource();
        var token = _pumpCts.Token;
        _pumpTask = Task.Run(() => PumpLoop(token), token);
    }

    private void StopPump()
    {
        var cts = _pumpCts;
        var task = _pumpTask;
        _pumpCts = null;
        _pumpTask = null;
        if (cts is null && task is null)
        {
            return;
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _hasSpace.Set();
        _hasData.Set();
        // Dispose 以外では止めない。Seek はポンプへ依頼するだけ。
        // 呼び出し元（UI／音声）がデコード終了を待つと、7／9 と 1／3 がワンテンポ遅れる。
        if (task is not null && task.Id != Task.CurrentId)
        {
            try
            {
                task.Wait();
            }
            catch (Exception ex) when (ex is AggregateException or OperationCanceledException)
            {
            }
        }

        cts?.Dispose();
    }

    private void PumpLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && !_disposed)
        {
            if (TryApplyPendingSeek())
            {
                continue;
            }

            int free;
            bool eof;
            lock (_ringGate)
            {
                free = RingFrames - _availableFrames;
                eof = _readerEof;
            }

            if (free <= 0)
            {
                _hasSpace.WaitOne(50);
                continue;
            }

            if (_samples is null || eof || _pumpFrame >= FrameCount)
            {
                MarkReaderEof();
                _hasSpace.WaitOne(50);
                continue;
            }

            var wantFrames = (int)Math.Min(Math.Min(ChunkFrames, free), FrameCount - _pumpFrame);
            if (wantFrames <= 0)
            {
                MarkReaderEof();
                _hasSpace.WaitOne(50);
                continue;
            }

            var version = Volatile.Read(ref _seekVersion);
            int got;
            try
            {
                got = _samples.Read(_pumpScratch, 0, wantFrames * Channels);
            }
            catch
            {
                MarkReaderEof();
                _hasSpace.WaitOne(50);
                continue;
            }

            if (Volatile.Read(ref _seekVersion) != version || HasPendingSeek())
            {
                continue;
            }

            if (got < Channels)
            {
                MarkReaderEof();
                _hasSpace.WaitOne(50);
                continue;
            }

            var gotFrames = got / Channels;
            PushFrames(_pumpScratch, gotFrames);
            _pumpFrame += gotFrames;
            _hasData.Set();
        }
    }

    private bool HasPendingSeek()
    {
        lock (_ringGate)
        {
            return _hasPendingSeek;
        }
    }

    private bool TryApplyPendingSeek()
    {
        long next;
        lock (_ringGate)
        {
            if (!_hasPendingSeek)
            {
                return false;
            }

            next = _pendingSeekFrame;
        }

        ApplyReaderSeek(next);
        lock (_ringGate)
        {
            if (_hasPendingSeek && _pendingSeekFrame == next)
            {
                _hasPendingSeek = false;
            }

            _pumpFrame = _hasPendingSeek ? _pendingSeekFrame : next;
            _readerEof = false;
        }

        return true;
    }

    private void ApplyReaderSeek(long next)
    {
        var reader = _reader;
        if (reader is null)
        {
            return;
        }

        try
        {
            var seconds = SampleRate > 0 ? next / (double)SampleRate : 0;
            reader.CurrentTime = TimeSpan.FromSeconds(
                Math.Clamp(seconds, 0, Math.Max(0, reader.TotalTime.TotalSeconds)));
        }
        catch
        {
            var block = Math.Max(1, reader.WaveFormat.BlockAlign);
            reader.Position = Math.Min(next * block, Math.Max(0, reader.Length));
        }

        ResetProvider();
    }

    private void PushFrames(float[] source, int frames)
    {
        lock (_ringGate)
        {
            for (var i = 0; i < frames; i++)
            {
                if (_availableFrames >= RingFrames)
                {
                    break;
                }

                var src = i * Channels;
                var dst = _writePos * Channels;
                Array.Copy(source, src, _ring, dst, Channels);
                _writePos++;
                if (_writePos >= RingFrames)
                {
                    _writePos = 0;
                }

                _availableFrames++;
            }
        }

        _hasSpace.Set();
    }

    /// <summary>デコードが終わり、リングにも残っていない。</summary>
    public bool IsDrained
    {
        get
        {
            lock (_ringGate)
            {
                return _readerEof && _availableFrames <= 0;
            }
        }
    }

    /// <summary>リング先頭から offset 先の abs ピーク。足りなければ false。</summary>
    public bool TryPeekAbsPeak(int offset, int frames, out float peak)
    {
        peak = 0f;
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (offset < 0 || frames <= 0)
        {
            return false;
        }

        lock (_ringGate)
        {
            if (offset + frames > _availableFrames)
            {
                return false;
            }

            var pos = _readPos + offset;
            if (pos >= RingFrames)
            {
                pos -= RingFrames;
            }

            for (var i = 0; i < frames; i++)
            {
                var src = pos * Channels;
                for (var ch = 0; ch < Channels; ch++)
                {
                    var a = Math.Abs(_ring[src + ch]);
                    if (a > peak)
                    {
                        peak = a;
                    }
                }

                pos++;
                if (pos >= RingFrames)
                {
                    pos = 0;
                }
            }
        }

        return true;
    }

    private void MarkReaderEof()
    {
        lock (_ringGate)
        {
            if (_hasPendingSeek)
            {
                return;
            }

            _readerEof = true;
        }

        _hasData.Set();
    }

    private bool TryPopFrame(Span<float> dest, int timeoutMs)
    {
        var waitUntil = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (true)
        {
            if (TryPopFrames(dest, 1) == 1)
            {
                return true;
            }

            if (IsDrained)
            {
                return false;
            }

            var remaining = waitUntil - Environment.TickCount64;
            if (remaining <= 0)
            {
                return false;
            }

            _hasData.WaitOne((int)Math.Min(remaining, 50));
        }
    }

    /// <summary>リングからまとめて出す。1 回のロックで複数フレームをコピーし、空き通知も 1 回。</summary>
    private int TryPopFrames(Span<float> dest, int frames)
    {
        var ch = Channels;
        if (frames <= 0 || ch <= 0 || dest.Length < ch)
        {
            return 0;
        }

        frames = Math.Min(frames, dest.Length / ch);
        int take;
        lock (_ringGate)
        {
            take = Math.Min(frames, _availableFrames);
            if (take <= 0)
            {
                return 0;
            }

            var copied = 0;
            while (copied < take)
            {
                var run = Math.Min(take - copied, RingFrames - _readPos);
                var samples = run * ch;
                _ring.AsSpan(_readPos * ch, samples).CopyTo(dest.Slice(copied * ch, samples));
                _readPos += run;
                if (_readPos >= RingFrames)
                {
                    _readPos = 0;
                }

                copied += run;
            }

            _availableFrames -= take;
        }

        _hasSpace.Set();
        return take;
    }

    private void WaitForPrebuffer(int frames, int timeoutMs)
    {
        var need = Math.Min(frames, (int)Math.Min(RingFrames, Math.Max(1, FrameCount - _frame)));
        var waitUntil = Environment.TickCount64 + Math.Max(0, timeoutMs);
        while (Environment.TickCount64 < waitUntil)
        {
            int available;
            bool eof;
            bool pending;
            lock (_ringGate)
            {
                available = _availableFrames;
                eof = _readerEof;
                pending = _hasPendingSeek;
            }

            if (pending)
            {
                _hasData.WaitOne(20);
                continue;
            }

            if (available >= need || (eof && available > 0))
            {
                return;
            }

            if (eof && available == 0)
            {
                return;
            }

            _hasData.WaitOne(20);
        }
    }

    private void ResetProvider()
    {
        if (_reader is null)
        {
            _samples = null;
            return;
        }

        _samples = AudioCodec.AsSampleProvider(_reader);
    }
}
