using MgaSonicAnvil.Domain;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal sealed class Pcm16WaveProvider : IWaveProvider, IDisposable
{
    private readonly float[] _samples;
    private readonly IProgress<double>? _progress;
    private int _index;
    private int _lastBucket = -1;

    public Pcm16WaveProvider(AudioDocument document, IProgress<double>? progress = null)
    {
        _samples = document.Interleaved;
        _progress = progress;
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
        if (_progress is not null && _samples.Length > 0)
        {
            var bucket = _index * 50 / _samples.Length;
            if (bucket != _lastBucket || _index >= _samples.Length)
            {
                _lastBucket = bucket;
                _progress.Report(Math.Clamp(_index / (double)_samples.Length, 0, 1));
            }
        }

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
    private int _deviceOutputChannels = 2;
    private int[] _outputMap = [];
    private int[] _fileChannelMap = [];
    private int[] _fileMap = [];
    private int[] _routeMap = [];
    private int _speakerChannels;
    private int _soloMask;
    private float[] _soloScratch = [];
    private int _monoLeftPort;
    private int _monoRightPort = 1;
    private bool _directRoute;
    private int _deviceRate = 48000;
    private int _sourceRate = 48000;
    private double _sourceFrame;
    private int _cursor;
    private int _loopStart;
    private int _playEnd;
    private bool _loop;
    private Func<long, float>? _frameGain;
    /// <summary>Play -E：ループ折り返しで -E 区間を二重再生するか。</summary>
    private bool _playExitLayer;
    /// <summary>再生ウィンドウ終端に続く -E 区間（ソースフレーム）。負値で未設定。</summary>
    private long _exitSpanStartFrame = -1;
    private long _exitSpanEndFrame = -1;
    private bool _exitPlaying;
    /// <summary>Exit レイヤーの読み出し位置（ソースフレーム）。</summary>
    private double _exitFrame;
    private readonly float[] _meterL = new float[LevelMeterEngine.WindowFrames];
    private readonly float[] _meterR = new float[LevelMeterEngine.WindowFrames];
    private readonly float[] _meterPlanar = new float[LevelMeterEngine.WindowFrames * ChannelLayout.MaxChannels];
    private readonly float[] _intervalPeak = new float[ChannelLayout.MaxChannels];
    private readonly double[] _intervalSumSq = new double[ChannelLayout.MaxChannels];
    private int _meterWrite;
    private int _meterCount;
    private int _meterSourceChannels = 2;
    private int _sourceMeterThisRead;
    private float[] _sourceMeterScratch = [];
    private float[] _speakerScratch = [];
    private int _sourceMeterScratchFrames;
    private int _sourceMeterScratchChannels = 1;
    private readonly float[] _monitorRing = new float[8192];
    private long _monitorWriteCount;
    private readonly object _monitorGate = new();
    private readonly MemoryScrubVoice _scrub = new();
    private bool _scrubbing;
    private float[] _scrubScratch = [];
    private int _intervalFrames;
    private readonly float[] _loudL = new float[8192];
    private readonly float[] _loudR = new float[8192];
    private int _loudWrite;
    private int _loudCount;
    private bool _silenceOnly;
    private bool _paused;
    private int _flushFadeRemaining;
    private int _flushFadeTotal;

    public WaveFormat WaveFormat { get; private set; } =
        WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

    public int DeviceSampleRate
    {
        get
        {
            lock (_gate)
            {
                return _deviceRate;
            }
        }
    }

    public bool Ended { get; private set; }

    public void SetDeviceSampleRate(int sampleRate)
    {
        lock (_gate)
        {
            _deviceRate = Math.Clamp(sampleRate, 1000, 384000);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_deviceRate, Math.Max(1, _outputChannels));
        }
    }

    public void SetSoloChannel(int channel) =>
        SetSoloMask(ChannelSolo.MaskOf(channel));

    public void SetSoloMask(int mask)
    {
        lock (_gate)
        {
            _soloMask = mask;
            _scrub.SetSoloMask(mask);
            if (_scrubbing)
            {
                _scrub.Capture((long)Math.Floor(_sourceFrame));
            }
        }
    }

    public void ConfigureOutput(int deviceChannels, int[]? map, int[]? fileChannelMap = null, int speakerChannels = 0)
    {
        lock (_gate)
        {
            _deviceOutputChannels = Math.Max(1, deviceChannels);
            _outputMap = map ?? [];
            _fileChannelMap = fileChannelMap ?? [];
            _speakerChannels = speakerChannels < 1 ? 0 : speakerChannels;
            ApplyOutputConfig();
        }
    }

    private void ApplyOutputConfig()
    {
        var dest = Math.Max(1, _deviceOutputChannels);
        var logical = _speakerChannels > 0 ? _speakerChannels : _channels;
        _fileMap = _speakerChannels > 0
            ? ChannelRouter.Normalize(_fileChannelMap, logical, _channels)
            : [];
        if (ChannelRouter.ShouldDownmix(logical, dest, _outputMap))
        {
            _directRoute = false;
            _outputChannels = 2;
        }
        else
        {
            _directRoute = dest > 2 || !ChannelRouter.IsEmpty(_outputMap);
            // ダウンミックス経路は常に L/R。1ch の WaveFormat だと左だけ鳴る。
            _outputChannels = _directRoute ? dest : dest <= 1 ? 1 : 2;
        }

        _routeMap = ChannelRouter.Normalize(_outputMap, logical, _outputChannels);
        (_monoLeftPort, _monoRightPort) = ChannelRouter.MonoPorts(_outputChannels, _outputMap);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_deviceRate, Math.Max(1, _outputChannels));
    }

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
                return _channels <= 0 ? 0 : (long)Math.Floor(_sourceFrame);
            }
        }
    }

    public int SourceSampleRate
    {
        get
        {
            lock (_gate)
            {
                return _sourceRate;
            }
        }
    }

    public int SourceChannels
    {
        get
        {
            lock (_gate)
            {
                return Math.Max(1, _channels);
            }
        }
    }

    public int MeterChannels
    {
        get
        {
            lock (_monitorGate)
            {
                return Math.Max(1, _meterSourceChannels);
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
            _sourceRate = Math.Max(1, document.SampleRate);
            ApplyOutputConfig();
            var start = Math.Clamp(startFrame, 0, document.FrameCount);
            _sourceFrame = start;
            _cursor = checked((int)start * _channels);
            _frameGain = frameGain;
            _silenceOnly = false;
            _flushFadeRemaining = 0;
            _flushFadeTotal = 0;
            _exitPlaying = false;
            _exitSpanStartFrame = -1;
            _exitSpanEndFrame = -1;
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
            ResetMeterBuffers();
            lock (_monitorGate)
            {
                _meterSourceChannels = Math.Max(1, _channels);
                Array.Clear(_monitorRing);
                _monitorWriteCount = 0;
                ResetMeterIntervalNoLock();
                ResetLoudnessNoLock();
            }
        }
    }

    /// <summary>
    /// 終了時にドライバ先読みを洗い流す。実音は足さず、無音だけを返す。
    /// </summary>
    public void BeginSilenceFlush()
    {
        lock (_gate)
        {
            _scrubbing = false;
            _scrub.Stop();
            _exitPlaying = false;
            _frameGain = null;
            Ended = false;
            // フェードで実音を足すと、終了時に先読みへまた音が入る。
            _flushFadeTotal = 0;
            _flushFadeRemaining = 0;
            _silenceOnly = true;
            _paused = false;
            ResetMeterBuffers();
            lock (_monitorGate)
            {
                Array.Clear(_monitorRing);
                _monitorWriteCount = 0;
                ResetMeterIntervalNoLock();
                ResetLoudnessNoLock();
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
        Span<float> peaks = stackalloc float[ChannelLayout.MaxChannels];
        Span<float> rms = stackalloc float[ChannelLayout.MaxChannels];
        var ok = TakeMeterInterval(peaks, rms, out _);
        peakLeft = peaks[0];
        rmsLeft = rms[0];
        peakRight = peaks[1];
        rmsRight = rms[1];
        return ok;
    }

    public bool TakeMeterInterval(Span<float> peaks, Span<float> rms, out int channels)
    {
        lock (_monitorGate)
        {
            channels = Math.Max(1, _meterSourceChannels);
            var n = Math.Min(Math.Min(peaks.Length, rms.Length), channels);
            if (_intervalFrames <= 0)
            {
                peaks[..n].Clear();
                rms[..n].Clear();
                return false;
            }

            for (var ch = 0; ch < n; ch++)
            {
                peaks[ch] = _intervalPeak[ch];
                rms[ch] = (float)Math.Sqrt(_intervalSumSq[ch] / _intervalFrames);
            }

            ResetMeterIntervalNoLock();
            return true;
        }
    }

    public void CopyMeterPlanar(float[] dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        var frames = LevelMeterEngine.WindowFrames;
        var stride = ChannelLayout.MaxChannels;
        if (dest.Length < frames * stride)
        {
            throw new ArgumentException("Planar meter buffer must hold 1024 frames.");
        }

        lock (_monitorGate)
        {
            var count = _meterCount;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += frames;
            }

            for (var i = 0; i < frames; i++)
            {
                var destAt = i * stride;
                if (i < frames - count)
                {
                    dest.AsSpan(destAt, stride).Clear();
                    continue;
                }

                var src = (start + i - (frames - count)) % frames;
                _meterPlanar.AsSpan(src * stride, stride).CopyTo(dest.AsSpan(destAt, stride));
            }
        }
    }

    public void CopyMeterPeaks(Span<float> peaks)
    {
        lock (_monitorGate)
        {
            var n = Math.Min(peaks.Length, Math.Max(1, _meterSourceChannels));
            var count = _meterCount;
            var window = LevelMeterEngine.WindowFrames;
            var start = _meterWrite - count;
            if (start < 0)
            {
                start += window;
            }

            for (var ch = 0; ch < n; ch++)
            {
                var peak = 0f;
                for (var i = 0; i < count; i++)
                {
                    var src = (start + i) % window;
                    var sample = Math.Abs(_meterPlanar[src * ChannelLayout.MaxChannels + ch]);
                    if (sample > peak)
                    {
                        peak = sample;
                    }
                }

                peaks[ch] = peak;
            }

            if (peaks.Length > n)
            {
                peaks[n..].Clear();
            }
        }
    }

    private void ResetMeterBuffers()
    {
        Array.Clear(_meterL);
        Array.Clear(_meterR);
        Array.Clear(_meterPlanar);
        _meterWrite = 0;
        _meterCount = 0;
    }

    private void ResetMeterIntervalNoLock()
    {
        Array.Clear(_intervalPeak);
        Array.Clear(_intervalSumSq);
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
            var next = Math.Clamp(frame, 0, max);
            _sourceFrame = next;
            _cursor = checked((int)next * _channels);
            // シークでジャンプしたら進行中の Exit 二重再生は直ちに止める（IM Importer と同じ）。
            _exitPlaying = false;
            Ended = false;
        }
    }

    public void SetScrubbing(bool scrubbing)
    {
        lock (_gate)
        {
            if (_scrubbing == scrubbing)
            {
                return;
            }

            _scrubbing = scrubbing;
            _scrub.Stop();
            if (scrubbing)
            {
                _exitPlaying = false;
            }

            Ended = false;
        }
    }

    /// <summary>
    /// 一時停止ゲート。true の間は Read が無音を返し、カーソルは進まない。
    /// 出力デバイスは動かしたままにして先読みを無音で置き換える。
    /// </summary>
    public void SetPaused(bool paused)
    {
        lock (_gate)
        {
            _paused = paused;
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

            // 再生ウィンドウが変わったら進行中の Exit は止める（-E 区間は呼び出し側が再設定する）。
            _exitPlaying = false;
            Ended = false;
        }
    }

    /// <summary>
    /// ループ折り返し時に -E 区間を二重再生するか（Play -E）。
    /// false にすると進行中の Exit も直ちに止める。
    /// </summary>
    public void SetPlayExitLayer(bool enabled)
    {
        lock (_gate)
        {
            _playExitLayer = enabled;
            if (!enabled)
            {
                _exitPlaying = false;
            }
        }
    }

    /// <summary>
    /// 再生ウィンドウ終端に続く -E 区間（ソースフレーム）を登録する。null で解除。
    /// </summary>
    public void SetExitSpan(WaveSelection? span)
    {
        lock (_gate)
        {
            if (span is { IsEmpty: false } range)
            {
                _exitSpanStartFrame = range.StartFrame;
                _exitSpanEndFrame = range.EndFrame;
            }
            else
            {
                _exitSpanStartFrame = -1;
                _exitSpanEndFrame = -1;
                _exitPlaying = false;
            }
        }
    }

    /// <summary>Exit レイヤー再生中の現在フレーム。停止中は -1。</summary>
    public long ExitCursorFrame
    {
        get
        {
            lock (_gate)
            {
                return _exitPlaying ? (long)Math.Floor(_exitFrame) : -1;
            }
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int written;
        var pushedSource = false;
        lock (_gate)
        {
            _sourceMeterThisRead = 0;
            _sourceMeterScratchFrames = 0;
            written = ReadCore(buffer, offset, count);
            ApplyFlushFade(buffer, offset, written);
            pushedSource = _sourceMeterThisRead > 0;
            if (pushedSource)
            {
                FlushSourceMeterScratchNoLock();
            }
        }

        if (written > 0)
        {
            PushOutputLoudness(buffer, offset, written);
            if (!pushedSource)
            {
                PushMeterFromOutput(buffer, offset, written);
            }
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

        // 一時停止中はカーソルを進めず無音を返す。デバイスを止めないことで、
        // ドライバ／仮想ミキサの先読みが常に無音で上書きされ、次の再生で
        // 古い音が出ない（停止時に洗い流す方式）。
        if (_paused)
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
            if (_flushFadeTotal > 0)
            {
                Array.Clear(buffer, offset, count);
                return count;
            }

            return 0;
        }

        var srcCh = Math.Max(1, _channels);
        var outCh = Math.Max(1, _outputChannels);
        var framesWanted = count / outCh;
        var writtenFrames = _sourceRate == _deviceRate
            ? ReadCoreNative(buffer, offset, framesWanted, srcCh, outCh)
            : ReadCoreResampled(buffer, offset, framesWanted, srcCh, outCh);
        var written = writtenFrames * outCh;
        if (_flushFadeTotal > 0 && written < count)
        {
            Array.Clear(buffer, offset + written, count - written);
            return count;
        }

        return written;
    }

    private void ApplyFlushFade(float[] buffer, int offset, int count)
    {
        if (_flushFadeTotal <= 0 || count <= 0)
        {
            return;
        }

        var outCh = Math.Max(1, _outputChannels);
        var frames = count / outCh;
        for (var i = 0; i < frames; i++)
        {
            if (_flushFadeRemaining <= 0)
            {
                Array.Clear(buffer, offset + i * outCh, count - i * outCh);
                _silenceOnly = true;
                return;
            }

            var gain = _flushFadeRemaining / (float)_flushFadeTotal;
            var at = offset + i * outCh;
            for (var channel = 0; channel < outCh; channel++)
            {
                buffer[at + channel] *= gain;
            }

            _flushFadeRemaining--;
        }

        if (_flushFadeRemaining <= 0)
        {
            _silenceOnly = true;
        }
    }

    private int ReadCoreNative(float[] buffer, int offset, int framesWanted, int srcCh, int outCh)
    {
        var writtenFrames = 0;
        var exitMixedFrames = 0;
        _cursor = checked((int)_sourceFrame * srcCh);
        while (writtenFrames < framesWanted)
        {
            if (_cursor >= _playEnd)
            {
                if (_loop && _playEnd > _loopStart)
                {
                    // 折り返し前までの Exit を先に乗せてから、Exit を -E 先頭から（再）開始する。
                    MixExitLayer(buffer, offset, exitMixedFrames, writtenFrames, srcCh, outCh, resampled: false);
                    exitMixedFrames = writtenFrames;
                    _cursor = _loopStart;
                    _sourceFrame = _loopStart / (double)srcCh;
                    BeginExitOnLoopWrapNoLock();
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
                EmitSourceFrame(buffer, offset, writtenFrames, srcCh, outCh, _cursor, _cursor / srcCh);
                _cursor += srcCh;
                writtenFrames++;
            }
        }

        _sourceFrame = srcCh <= 0 ? 0 : _cursor / (double)srcCh;
        MixExitLayer(buffer, offset, exitMixedFrames, writtenFrames, srcCh, outCh, resampled: false);
        return writtenFrames;
    }

    private int ReadCoreResampled(float[] buffer, int offset, int framesWanted, int srcCh, int outCh)
    {
        var playEndFrame = srcCh <= 0 ? 0 : _playEnd / (double)srcCh;
        var loopStartFrame = srcCh <= 0 ? 0 : _loopStart / (double)srcCh;
        var frameCount = srcCh <= 0 ? 0 : _samples.Length / srcCh;
        var step = _sourceRate / (double)_deviceRate;
        var writtenFrames = 0;
        var exitMixedFrames = 0;
        Span<float> frame = stackalloc float[ChannelLayout.MaxChannels];
        var source = frame[..Math.Min(srcCh, ChannelLayout.MaxChannels)];
        while (writtenFrames < framesWanted)
        {
            if (_sourceFrame >= playEndFrame)
            {
                if (_loop && playEndFrame > loopStartFrame)
                {
                    // 折り返し前までの Exit を先に乗せてから、Exit を -E 先頭から（再）開始する。
                    MixExitLayer(buffer, offset, exitMixedFrames, writtenFrames, srcCh, outCh, resampled: true);
                    exitMixedFrames = writtenFrames;
                    _sourceFrame = loopStartFrame;
                    BeginExitOnLoopWrapNoLock();
                    continue;
                }

                Ended = true;
                break;
            }

            // ホールドだと折り返しイメージ（ジャリつく偽の高域）が乗る。帯域制限補間で再構成する。
            FormatConvert.ResampleFrameBandlimited(
                _samples,
                srcCh,
                _sourceFrame,
                frameCount,
                _sourceRate,
                _deviceRate,
                source);
            var gain = _frameGain is { } gainAt ? gainAt((long)Math.Floor(_sourceFrame)) : 1f;
            EmitFrame(buffer, offset, writtenFrames, outCh, source, gain);

            _sourceFrame += step;
            writtenFrames++;
        }

        _cursor = checked((int)Math.Clamp(_sourceFrame, 0, frameCount) * srcCh);
        MixExitLayer(buffer, offset, exitMixedFrames, writtenFrames, srcCh, outCh, resampled: true);
        return writtenFrames;
    }

    /// <summary>
    /// ループ末端→頭の折り返しと同時に Exit 二重再生を開始／頭から再開する（Wwise 相当）。
    /// </summary>
    private void BeginExitOnLoopWrapNoLock()
    {
        if (!_playExitLayer || _exitSpanStartFrame < 0 || _exitSpanEndFrame <= _exitSpanStartFrame)
        {
            return;
        }

        _exitFrame = _exitSpanStartFrame;
        _exitPlaying = true;
    }

    /// <summary>
    /// buffer の [fromFrame, toFrame) へ Exit レイヤーを加算ミックスする。
    /// -E 終端へ達したら Exit を止める。
    /// </summary>
    private void MixExitLayer(
        float[] buffer,
        int offset,
        int fromFrame,
        int toFrame,
        int srcCh,
        int outCh,
        bool resampled)
    {
        if (!_exitPlaying || toFrame <= fromFrame)
        {
            return;
        }

        var frameCount = srcCh <= 0 ? 0 : _samples.Length / srcCh;
        var endFrame = Math.Min(_exitSpanEndFrame, frameCount);
        var step = resampled ? _sourceRate / (double)_deviceRate : 1d;
        for (var i = fromFrame; i < toFrame; i++)
        {
            if (_exitFrame >= endFrame)
            {
                _exitPlaying = false;
                return;
            }

            float left;
            float right;
            if (_soloMask != 0)
            {
                ChannelMix.Downmix(
                    ApplySolo(_samples.AsSpan(checked((int)_exitFrame * srcCh), srcCh)),
                    out left,
                    out right);
            }
            else if (resampled)
            {
                FormatConvert.DownmixBandlimited(
                    _samples,
                    srcCh,
                    _exitFrame,
                    frameCount,
                    _sourceRate,
                    _deviceRate,
                    out left,
                    out right);
            }
            else
            {
                ChannelMix.Downmix(_samples, checked((int)_exitFrame * srcCh), srcCh, out left, out right);
            }

            if (_frameGain is { } gainAt)
            {
                var gain = gainAt((long)Math.Floor(_exitFrame));
                left *= gain;
                right *= gain;
            }

            AddFrame(buffer, offset, i, outCh, left, right);
            _exitFrame += step;
        }
    }

    private void EmitSourceFrame(
        float[] buffer,
        int offset,
        int writtenFrames,
        int srcCh,
        int outCh,
        int sourceIndex,
        long sourceFrame)
    {
        var source = _samples.AsSpan(sourceIndex, srcCh);
        var gain = _frameGain is { } gainAt ? gainAt(sourceFrame) : 1f;
        EmitFrame(buffer, offset, writtenFrames, outCh, source, gain);
    }

    /// <summary>1 フレーム分のソース信号をメーターへ流し、ルーティングして出力へ書く。</summary>
    private void EmitFrame(
        float[] buffer,
        int offset,
        int writtenFrames,
        int outCh,
        ReadOnlySpan<float> source,
        float gain)
    {
        source = ApplySolo(source);
        PushSourceFrame(source, gain);
        if (_directRoute)
        {
            var dest = buffer.AsSpan(offset + writtenFrames * outCh, outCh);
            if (ChannelRouter.ShouldMirrorMono(source.Length, outCh))
            {
                dest.Clear();
                var sample = source[0] * gain;
                dest[_monoLeftPort] = sample;
                dest[_monoRightPort] = sample;
                return;
            }

            var logical = ToSpeakerFrame(source);
            ChannelRouter.Scatter(logical, dest, _routeMap);
            if (gain != 1f)
            {
                for (var i = 0; i < dest.Length; i++)
                {
                    dest[i] *= gain;
                }
            }

            return;
        }

        ChannelMix.Downmix(ToSpeakerFrame(source), out var left, out var right);
        if (gain != 1f)
        {
            left *= gain;
            right *= gain;
        }

        WriteFrame(buffer, offset, writtenFrames, outCh, left, right);
    }

    private ReadOnlySpan<float> ApplySolo(ReadOnlySpan<float> source)
    {
        if (_soloMask == 0)
        {
            return source;
        }

        if (_soloScratch.Length < source.Length)
        {
            _soloScratch = new float[source.Length];
        }

        var dest = _soloScratch.AsSpan(0, source.Length);
        dest.Clear();
        for (var i = 0; i < source.Length; i++)
        {
            if (ChannelSolo.Contains(_soloMask, i))
            {
                dest[i] = source[i];
            }
        }

        return dest;
    }

    private ReadOnlySpan<float> ToSpeakerFrame(ReadOnlySpan<float> source)
    {
        if (_fileMap.Length == 0)
        {
            return source;
        }

        if (_speakerScratch.Length < _fileMap.Length)
        {
            _speakerScratch = new float[_fileMap.Length];
        }

        var dest = _speakerScratch.AsSpan(0, _fileMap.Length);
        ChannelRouter.Gather(source, dest, _fileMap);
        return dest;
    }

    private static void WriteFrame(float[] buffer, int offset, int frame, int outCh, float left, float right)
    {
        var dest = offset + frame * outCh;
        buffer[dest] = left;
        if (outCh > 1)
        {
            buffer[dest + 1] = right;
        }
    }

    private static void AddFrame(float[] buffer, int offset, int frame, int outCh, float left, float right)
    {
        var dest = offset + frame * outCh;
        buffer[dest] += left;
        if (outCh > 1)
        {
            buffer[dest + 1] += right;
        }
    }

    private int ReadScrub(float[] buffer, int offset, int count)
    {
        var outCh = Math.Max(1, _outputChannels);
        var framesWanted = Math.Max(0, count / outCh);
        var sourceFrames = _sourceRate == _deviceRate
            ? framesWanted
            : Math.Max(1, (int)Math.Round(framesWanted * (_sourceRate / (double)_deviceRate)));
        var needed = Math.Max(framesWanted, sourceFrames) * 2;
        if (_scrubScratch.Length < needed)
        {
            _scrubScratch = new float[needed];
        }

        _scrub.Read(_scrubScratch, 0, sourceFrames, 1f);
        if (_sourceRate == _deviceRate)
        {
            for (var i = 0; i < framesWanted; i++)
            {
                WriteFrame(buffer, offset, i, outCh, _scrubScratch[i * 2], _scrubScratch[i * 2 + 1]);
            }
        }
        else
        {
            var step = sourceFrames / (double)Math.Max(1, framesWanted);
            for (var i = 0; i < framesWanted; i++)
            {
                FormatConvert.DownmixBandlimited(
                    _scrubScratch,
                    2,
                    i * step,
                    sourceFrames,
                    _sourceRate,
                    _deviceRate,
                    out var left,
                    out var right);
                WriteFrame(buffer, offset, i, outCh, left, right);
            }
        }

        MemoryScrubVoice.SoftClip(buffer, offset, framesWanted * outCh);
        Ended = false;
        return framesWanted * outCh;
    }

    private void PushSourceFrame(ReadOnlySpan<float> frame, float gain)
    {
        var n = Math.Min(Math.Max(1, frame.Length), ChannelLayout.MaxChannels);
        if (_sourceMeterScratchFrames == 0)
        {
            _sourceMeterScratchChannels = n;
        }

        var channels = _sourceMeterScratchChannels;
        var dest = _sourceMeterScratchFrames * channels;
        var need = dest + channels;
        if (_sourceMeterScratch.Length < need)
        {
            Array.Resize(ref _sourceMeterScratch, Math.Max(need, _sourceMeterScratch.Length * 2 + 64));
        }

        var copy = Math.Min(n, channels);
        for (var ch = 0; ch < copy; ch++)
        {
            _sourceMeterScratch[dest + ch] = frame[ch] * gain;
        }

        for (var ch = copy; ch < channels; ch++)
        {
            _sourceMeterScratch[dest + ch] = 0;
        }

        _sourceMeterScratchFrames++;
        _sourceMeterThisRead++;
    }

    private void FlushSourceMeterScratchNoLock()
    {
        if (_sourceMeterScratchFrames <= 0)
        {
            return;
        }

        var n = Math.Max(1, _sourceMeterScratchChannels);
        lock (_monitorGate)
        {
            for (var i = 0; i < _sourceMeterScratchFrames; i++)
            {
                WriteSourceMeterNoLock(_sourceMeterScratch.AsSpan(i * n, n));
            }
        }

        _sourceMeterScratchFrames = 0;
    }

    private void WriteSourceMeterNoLock(ReadOnlySpan<float> frame)
    {
        var n = Math.Min(Math.Max(1, frame.Length), ChannelLayout.MaxChannels);
        _meterSourceChannels = Math.Max(_meterSourceChannels, n);
        var dest = _meterWrite * ChannelLayout.MaxChannels;
        for (var ch = 0; ch < n; ch++)
        {
            var sample = frame[ch];
            var abs = Math.Abs(sample);
            if (abs > _intervalPeak[ch])
            {
                _intervalPeak[ch] = abs;
            }

            _intervalSumSq[ch] += abs * (double)abs;
            _meterPlanar[dest + ch] = sample;
        }

        for (var ch = n; ch < ChannelLayout.MaxChannels; ch++)
        {
            _meterPlanar[dest + ch] = 0;
        }

        _intervalFrames++;
        _meterL[_meterWrite] = frame[0];
        _meterR[_meterWrite] = n > 1 ? frame[1] : frame[0];
        _monitorRing[(int)(_monitorWriteCount % _monitorRing.Length)] = ChannelMix.Mid(frame);
        _monitorWriteCount++;
        AdvanceMeterWriteNoLock();
    }

    private void PushMeterFromOutput(float[] source, int offset, int count)
    {
        var channels = Math.Max(1, _outputChannels);
        var frames = count / channels;
        Span<float> frame = stackalloc float[2];
        lock (_monitorGate)
        {
            for (var i = 0; i < frames; i++)
            {
                var src = offset + i * channels;
                frame[0] = source[src];
                frame[1] = channels > 1 ? source[src + 1] : frame[0];
                WriteSourceMeterNoLock(channels > 1 ? frame : frame[..1]);
            }
        }
    }

    private void PushOutputLoudness(float[] source, int offset, int count)
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
                _loudL[_loudWrite] = left;
                _loudR[_loudWrite] = right;
                _loudWrite++;
                if (_loudWrite >= _loudL.Length)
                {
                    _loudWrite = 0;
                }

                if (_loudCount < _loudL.Length)
                {
                    _loudCount++;
                }
            }
        }
    }

    private void AdvanceMeterWriteNoLock()
    {
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

    /// <summary>前回取得以降のステレオ出力。新規がなければ 0。</summary>
    public int TakeLoudnessFrames(float[] left, float[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        lock (_monitorGate)
        {
            var n = Math.Min(_loudCount, Math.Min(left.Length, right.Length));
            if (n <= 0)
            {
                return 0;
            }

            var start = _loudWrite - _loudCount;
            if (start < 0)
            {
                start += _loudL.Length;
            }

            for (var i = 0; i < n; i++)
            {
                var src = (start + i) % _loudL.Length;
                left[i] = _loudL[src];
                right[i] = _loudR[src];
            }

            _loudCount -= n;
            return n;
        }
    }

    private void ResetLoudnessNoLock()
    {
        Array.Clear(_loudL);
        Array.Clear(_loudR);
        _loudWrite = 0;
        _loudCount = 0;
    }
}
