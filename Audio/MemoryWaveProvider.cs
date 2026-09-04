using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal sealed class Pcm16WaveProvider : IWaveProvider, IDisposable
{
    private readonly float[] _samples;
    private int _index;

    public Pcm16WaveProvider(AudioDocument document)
    {
        _samples = document.Interleaved;
        WaveFormat = new WaveFormat(document.SampleRate, 16, document.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var frames = count / 2;
        var n = Math.Min(frames, _samples.Length - _index);
        if (n <= 0)
        {
            return 0;
        }

        for (var i = 0; i < n; i++)
        {
            var value = (short)Math.Clamp((int)Math.Round(_samples[_index + i] * 32767f), short.MinValue, short.MaxValue);
            var at = offset + i * 2;
            buffer[at] = (byte)value;
            buffer[at + 1] = (byte)(value >> 8);
        }

        _index += n;
        return n * 2;
    }

    public void Dispose()
    {
    }
}

internal sealed class PlaybackSampleProvider : ISampleProvider
{
    private readonly object _gate = new();
    private float[] _samples = [];
    private int _channels = 2;
    private int _outputChannels = 2;
    private int _cursor;
    private int _loopStart;
    private int _playEnd;
    private bool _loop;
    private Func<long, float>? _frameGain;
    private readonly float[] _meterL = new float[LevelMeterEngine.WindowFrames];
    private readonly float[] _meterR = new float[LevelMeterEngine.WindowFrames];
    private int _meterWrite;
    private int _meterCount;
    private readonly float[] _monitorRing = new float[8192];
    private long _monitorWriteCount;
    private readonly object _monitorGate = new();
    private readonly MemoryScrubVoice _scrub = new();
    private bool _scrubbing;
    private float[] _scrubScratch = [];
    private float _intervalPeakL;
    private float _intervalPeakR;
    private double _intervalSumSqL;
    private double _intervalSumSqR;
    private int _intervalFrames;
    private bool _silenceOnly;

    public WaveFormat WaveFormat { get; private set; } =
        WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public bool Ended { get; private set; }

    public bool IsBoundTo(AudioDocument document)
    {
        lock (_gate)
        {
            return ReferenceEquals(_samples, document.Interleaved);
        }
    }

    public long CursorFrame
    {
        get
        {
            lock (_gate)
            {
                return _channels <= 0 ? 0 : _cursor / _channels;
            }
        }
    }

    public void Bind(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null)
    {
        lock (_gate)
        {
            _samples = document.Interleaved;
            _channels = Math.Max(1, document.Channels);
            _outputChannels = Math.Min(2, _channels);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(document.SampleRate, _outputChannels);
            _cursor = checked((int)Math.Clamp(startFrame, 0, document.FrameCount) * _channels);
            _frameGain = frameGain;
            _silenceOnly = false;
            if (playRange is { IsEmpty: false } range)
            {
                _loop = loop;
                _loopStart = checked((int)range.StartFrame * _channels);
                _playEnd = checked((int)range.EndFrame * _channels);
            }
            else
            {
                _loop = false;
                _loopStart = 0;
                _playEnd = _samples.Length;
            }

            Ended = false;
            Array.Clear(_meterL);
            Array.Clear(_meterR);
            _meterWrite = 0;
            _meterCount = 0;
            lock (_monitorGate)
            {
                Array.Clear(_monitorRing);
                _monitorWriteCount = 0;
                ResetMeterIntervalNoLock();
            }
        }
    }

    /// <summary>
    /// 以降の Read は無音のみ。終了時にドライバ先読みを洗い流す用途。
    /// </summary>
    public void BeginSilenceFlush()
    {
        lock (_gate)
        {
            _silenceOnly = true;
            _scrubbing = false;
            _scrub.Stop();
            _samples = [];
            _frameGain = null;
            Ended = false;
            Array.Clear(_meterL);
            Array.Clear(_meterR);
            _meterWrite = 0;
            _meterCount = 0;
            lock (_monitorGate)
            {
                Array.Clear(_monitorRing);
                _monitorWriteCount = 0;
                ResetMeterIntervalNoLock();
            }
        }
    }

    public void CopyMeterWindow(float[] left, float[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var n = LevelMeterEngine.WindowFrames;
        if (left.Length < n || right.Length < n)
        {
            throw new ArgumentException("Meter window buffers must hold 1024 samples.");
        }

        lock (_monitorGate)
        {
            var count = _meterCount;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += n;
            }

            for (var i = 0; i < n; i++)
            {
                if (i < n - count)
                {
                    left[i] = 0;
                    right[i] = 0;
                    continue;
                }

                var src = (start + i - (n - count)) % n;
                left[i] = _meterL[src];
                right[i] = _meterR[src];
            }
        }
    }

    /// <summary>前回取得以降に出力した区間の Peak / RMS。新規サンプルがなければ false。</summary>
    public bool TakeMeterInterval(out float peakLeft, out float rmsLeft, out float peakRight, out float rmsRight)
    {
        lock (_monitorGate)
        {
            if (_intervalFrames <= 0)
            {
                peakLeft = 0;
                rmsLeft = 0;
                peakRight = 0;
                rmsRight = 0;
                return false;
            }

            peakLeft = _intervalPeakL;
            peakRight = _intervalPeakR;
            rmsLeft = (float)Math.Sqrt(_intervalSumSqL / _intervalFrames);
            rmsRight = (float)Math.Sqrt(_intervalSumSqR / _intervalFrames);
            ResetMeterIntervalNoLock();
            return true;
        }
    }

    private void ResetMeterIntervalNoLock()
    {
        _intervalPeakL = 0;
        _intervalPeakR = 0;
        _intervalSumSqL = 0;
        _intervalSumSqR = 0;
        _intervalFrames = 0;
    }

    /// <summary>直近の出力サンプル（モノラルミックス）を destination の末尾詰めでコピーする。</summary>
    public int CopyRecentOutputSamples(float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        lock (_monitorGate)
        {
            var available = (int)Math.Min(
                _monitorWriteCount,
                Math.Min(destination.Length, _monitorRing.Length));
            var start = _monitorWriteCount - available;
            for (var i = 0; i < available; i++)
            {
                destination[destination.Length - available + i] =
                    _monitorRing[(int)((start + i) % _monitorRing.Length)];
            }

            if (available < destination.Length)
            {
                Array.Clear(destination, 0, destination.Length - available);
            }

            return available;
        }
    }

    public void SeekFrame(long frame)
    {
        lock (_gate)
        {
            var max = _channels <= 0 ? 0 : _samples.Length / _channels;
            _cursor = checked((int)Math.Clamp(frame, 0, max) * _channels);
            Ended = false;
        }
    }

    public void SetScrubbing(bool scrubbing)
    {
        lock (_gate)
        {
            _scrubbing = scrubbing;
            _scrub.Stop();
            Ended = false;
        }
    }

    public void CaptureScrub(AudioDocument document, long frame)
    {
        _scrub.Bind(document);
        _scrub.Capture(frame);
    }

    public void SetPlayWindow(WaveSelection? playRange, bool loop)
    {
        lock (_gate)
        {
            if (playRange is { IsEmpty: false } range)
            {
                _loop = loop;
                _loopStart = checked((int)range.StartFrame * _channels);
                _playEnd = checked((int)range.EndFrame * _channels);
            }
            else
            {
                _loop = false;
                _loopStart = 0;
                _playEnd = _samples.Length;
            }

            Ended = false;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int written;
        lock (_gate)
        {
            written = ReadCore(buffer, offset, count);
        }

        if (written > 0)
        {
            PushMeter(buffer, offset, written);
        }

        return written;
    }

    private int ReadCore(float[] buffer, int offset, int count)
    {
        if (_silenceOnly)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        if (_scrubbing)
        {
            return ReadScrub(buffer, offset, count);
        }

        if (_samples.Length == 0 || Ended)
        {
            return 0;
        }

        var srcCh = Math.Max(1, _channels);
        var outCh = Math.Max(1, _outputChannels);
        var framesWanted = count / outCh;
        var writtenFrames = 0;
        while (writtenFrames < framesWanted)
        {
            if (_cursor >= _playEnd)
            {
                if (_loop && _playEnd > _loopStart)
                {
                    _cursor = _loopStart;
                    continue;
                }

                Ended = true;
                break;
            }

            var frames = Math.Min(framesWanted - writtenFrames, (_playEnd - _cursor) / srcCh);
            if (frames <= 0)
            {
                Ended = true;
                break;
            }

            for (var i = 0; i < frames; i++)
            {
                ChannelMix.Downmix(_samples, _cursor, srcCh, out var left, out var right);
                if (_frameGain is { } gainAt)
                {
                    var gain = gainAt(_cursor / srcCh);
                    left *= gain;
                    right *= gain;
                }

                var dest = offset + writtenFrames * outCh;
                buffer[dest] = left;
                if (outCh > 1)
                {
                    buffer[dest + 1] = right;
                }

                _cursor += srcCh;
                writtenFrames++;
            }
        }

        return writtenFrames * outCh;
    }

    private int ReadScrub(float[] buffer, int offset, int count)
    {
        var outCh = Math.Max(1, _outputChannels);
        var framesWanted = Math.Max(0, count / outCh);
        var needed = framesWanted * 2;
        if (_scrubScratch.Length < needed)
        {
            _scrubScratch = new float[needed];
        }

        _scrub.Read(_scrubScratch, 0, framesWanted, 1f);
        for (var i = 0; i < framesWanted; i++)
        {
            var dest = offset + i * outCh;
            buffer[dest] = _scrubScratch[i * 2];
            if (outCh > 1)
            {
                buffer[dest + 1] = _scrubScratch[i * 2 + 1];
            }
        }

        MemoryScrubVoice.SoftClip(buffer, offset, framesWanted * outCh);
        Ended = false;
        return framesWanted * outCh;
    }

    private void PushMeter(float[] source, int offset, int count)
    {
        var channels = Math.Max(1, _outputChannels);
        var frames = count / channels;
        lock (_monitorGate)
        {
            for (var i = 0; i < frames; i++)
            {
                var src = offset + i * channels;
                var left = source[src];
                var right = channels > 1 ? source[src + 1] : left;
                var absL = Math.Abs(left);
                var absR = Math.Abs(right);
                if (absL > _intervalPeakL)
                {
                    _intervalPeakL = absL;
                }

                if (absR > _intervalPeakR)
                {
                    _intervalPeakR = absR;
                }

                _intervalSumSqL += absL * (double)absL;
                _intervalSumSqR += absR * (double)absR;
                _intervalFrames++;
                _meterL[_meterWrite] = left;
                _meterR[_meterWrite] = right;
                _monitorRing[(int)(_monitorWriteCount % _monitorRing.Length)] =
                    (left + right) * 0.5f;
                _monitorWriteCount++;
                _meterWrite++;
                if (_meterWrite >= LevelMeterEngine.WindowFrames)
                {
                    _meterWrite = 0;
                }

                if (_meterCount < LevelMeterEngine.WindowFrames)
                {
                    _meterCount++;
                }
            }
        }
    }
}
