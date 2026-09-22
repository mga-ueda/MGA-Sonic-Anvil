using System.Threading;
using MgaSonicAnvil.Domain;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal sealed class Pcm16WaveProvider : IWaveProvider, IDisposable
{
    private readonly float[] _samples;
    private readonly int _count;
    private readonly IProgress<double>? _progress;
    private int _index;
    private int _lastBucket = -1;

    public Pcm16WaveProvider(AudioDocument document, IProgress<double>? progress = null)
    {
        _samples = document.Interleaved;
        _count = Math.Clamp(document.SampleCount, 0, _samples.Length);
        _progress = progress;
        WaveFormat = new WaveFormat(document.SampleRate, 16, document.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var frames = count / 2;
        var n = Math.Min(frames, _count - _index);
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
        if (_progress is not null && _count > 0)
        {
            var bucket = _index * 50 / _count;
            if (bucket != _lastBucket || _index >= _count)
            {
                _lastBucket = bucket;
                _progress.Report(Math.Clamp(_index / (double)_count, 0, 1));
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
    private int _usedSamples;
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
    private bool _silentSkip;
    private float _silentSkipLinear = SilentSkip.LinearFromDb(SilentSkip.DefaultThresholdDb);
    /// <summary>再生ヘッドの進行倍率。1 / <see cref="FastSpeed"/> / -<see cref="FastSpeed"/>。</summary>
    private double _playbackSpeed = 1;
    private float _shuttleOutputGain = 1f;
    private bool _shuttlePrimed;
    private bool _shuttleFadeIn;
    private double _shuttleOrigin;
    private double _shuttleRead;
    private int _flushFadeRemaining;
    private int _flushFadeTotal;
    /// <summary>1＝推移元フェードアウトと推移先フェードインを同時。0 はなし。</summary>
    private int _seekFadePhase;
    private int _seekFadePos;
    private int _seekFadeFrames;
    private long _seekFadeTarget;
    private double _seekFadeOutFrame;
    private AudioStreamSource? _seekFadeOutStream;
    private float[] _seekFadeOutSrc = [];
    private float[] _seekFadeOutCached = [];
    private long _seekFadeOutCachedAt = -1;
    private float[] _seekFadeMix = [];
    private float[] _seekFadeOutPcm = [];
    private int _seekFadeOutPcmFrames;
    private double _seekFadeOutPcmPos;
    private readonly RangeClickMixer _rangeClicks = new();
    private AudioStreamSource? _stream;
    private AudioStreamSource? _gaplessStream;
    private AudioDocument? _gaplessDocument;
    private AudioDocument? _gaplessAdvanced;
    private volatile int _gaplessAdvancePending;
    private volatile bool _gaplessArmed;
    private AudioDocument? _boundDocument;
    private float[] _streamFrame = [];
    private float[] _streamCached = [];
    private long _streamCachedAt = -1;
    /// <summary>ストリーム SRC 用。sinc が参照するソースフレームの滑り窓。</summary>
    private float[] _streamSrcWin = [];
    private long _streamSrcWinStart = -1;
    private int _streamSrcWinFrames;
    private const int StreamSrcWinSlack = 32;
    /// <summary>ストリーム早戻し。デコード済みを逆方向へ再生し、次チャンクは重ならせてつなぐ。</summary>
    private float[] _streamReverseBuf = [];
    private long _streamReverseBufStart = -1;
    private int _streamReverseBufFrames;
    private float[] _streamReverseNext = [];
    private long _streamReverseNextStart = -1;
    private int _streamReverseNextFrames;
    private float[] _streamReverseLast = [];
    /// <summary>1 回の Read でリングから捨てる無音の上限。超えたら次のコールバックへ回す。</summary>
    private int _streamSkipBudget;
    private bool _streamSkipYield;
    private const int StreamSilentSkipMaxFrames = 8192;

    /// <summary>早送りはピッチ据え置きグレイン、巻き戻しは逆再生。</summary>
    internal const double FastSpeed = 3;
    /// <summary>1／3（および ←→ 早送り）の推移中に下げる音量。</summary>
    internal const double ShuttleGainDb = -4;
    internal static readonly float ShuttleGainLinear = (float)Math.Pow(10, ShuttleGainDb / 20.0);
    private const double ShuttleGrainSeconds = 0.03;
    private const double ShuttleTaperSeconds = 0.004;
    private const double StreamReverseChunkSeconds = 0.16;
    private const double StreamReverseOverlapSeconds = 0.02;

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
            _rangeClicks.SetDeviceRate(_deviceRate);
        }
    }

    /// <summary>テスト用。本番は Low / High.wav を <see cref="EnsureRangeClickSamples"/> で読む。</summary>
    public void SetRangeClickSample(float[] mono, int sampleRate)
    {
        lock (_gate)
        {
            _rangeClicks.SetSample(mono, sampleRate);
            _rangeClicks.SetDeviceRate(_deviceRate);
        }
    }

    public void SetRangeClickSamples(float[] low, float[] high, int sampleRate)
    {
        lock (_gate)
        {
            _rangeClicks.SetSamples(low, high, sampleRate);
            _rangeClicks.SetDeviceRate(_deviceRate);
        }
    }

    public void EnsureRangeClickSamples()
    {
        var needLow = !_rangeClicks.HasSample;
        var needHigh = !_rangeClicks.HasHighSample;
        if (!needLow && !needHigh)
        {
            return;
        }

        var low = needLow ? RangeClickSample.LoadLow() : default;
        var high = needHigh ? RangeClickSample.LoadHigh() : default;
        lock (_gate)
        {
            if (needLow && !_rangeClicks.HasSample)
            {
                _rangeClicks.SetLowSample(low.Samples, low.SampleRate);
            }

            if (needHigh && !_rangeClicks.HasHighSample)
            {
                _rangeClicks.SetHighSample(high.Samples, high.SampleRate);
            }

            _rangeClicks.SetDeviceRate(_deviceRate);
        }
    }

    public void SetRangeClickFrames(long[] frames, int groupSize = 0)
    {
        if (frames.Length > 0)
        {
            EnsureRangeClickSamples();
        }

        lock (_gate)
        {
            _rangeClicks.SetTriggers(frames, groupSize);
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
            if (_stream is not null)
            {
                return ReferenceEquals(_boundDocument, document);
            }

            return ReferenceEquals(_samples, document.Interleaved);
        }
    }

    public bool IsStreamBound
    {
        get
        {
            lock (_gate)
            {
                return _stream is not null;
            }
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

    /// <summary>クロスフェード中のスキップ先。連打はここへ積む。ヘッドは目標から進める。</summary>
    public long? PendingSeekFrame
    {
        get
        {
            lock (_gate)
            {
                return _seekFadePhase == 1 ? (long)Math.Floor(_sourceFrame) : null;
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

    public void Bind(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null)
    {
        lock (_gate)
        {
            DisposeStreamNoLock();
            ClearGaplessNoLock();
            NoteGaplessAdvancedNoLock(null);
            _boundDocument = document;
            _samples = document.Interleaved;
            _usedSamples = Math.Clamp(document.SampleCount, 0, _samples.Length);
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
            ClearSeekFadeNoLock();
            _exitPlaying = false;
            _shuttlePrimed = false;
            _exitSpanStartFrame = -1;
            _exitSpanEndFrame = -1;
            ApplyPlayWindowNoLock(playRange, loop);
            _rangeClicks.ResetVoice();

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

    /// <summary>プレイヤー用。ファイルをストリーム再生する。呼び出し側が開いた source の所有権を移す。</summary>
    public void BindStream(
        AudioStreamSource source,
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            DisposeStreamNoLock();
            _stream = source;
            _boundDocument = document;
            _samples = [];
            _channels = Math.Max(1, source.Channels);
            _sourceRate = Math.Max(1, source.SampleRate);
            var frames = Math.Max(0, source.FrameCount);
            var sampleCount = frames * (long)_channels;
            _usedSamples = sampleCount > int.MaxValue ? int.MaxValue : (int)sampleCount;
            if (_streamFrame.Length < _channels)
            {
                _streamFrame = new float[_channels];
            }

            if (_streamCached.Length < _channels)
            {
                _streamCached = new float[_channels];
            }

            InvalidateStreamCursorCacheNoLock();
            ClearStreamReverseBuf();
            ClearGaplessNoLock();
            NoteGaplessAdvancedNoLock(null);
            ApplyOutputConfig();
            var start = Math.Clamp(startFrame, 0, frames);
            source.SeekFrame(start, prebufferTimeoutMs: 200);
            _sourceFrame = start;
            _cursor = checked((int)start * _channels);
            _frameGain = frameGain;
            _silenceOnly = false;
            _flushFadeRemaining = 0;
            _flushFadeTotal = 0;
            ClearSeekFadeNoLock();
            _exitPlaying = false;
            _shuttlePrimed = false;
            _exitSpanStartFrame = -1;
            _exitSpanEndFrame = -1;
            // ストリームでは Exit／ピッチ据え置きグレインは使わない（可変速）。
            _playExitLayer = false;
            ApplyPlayWindowNoLock(playRange, loop);
            _rangeClicks.ResetVoice();

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

    private void DisposeStreamNoLock(bool synchronous = true)
    {
        var stream = _stream;
        _stream = null;
        _boundDocument = null;
        InvalidateStreamCursorCacheNoLock();
        if (stream is null)
        {
            return;
        }

        if (synchronous)
        {
            stream.Dispose();
            return;
        }

        // StopPump は Dispose 時だけ待つ。音声コールバックではキューへ逃がす。
        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                try
                {
                    ((AudioStreamSource)state!).Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            },
            stream);
    }

    public bool HasGaplessArmed => _gaplessArmed;

    public bool HasGaplessAdvancePending => _gaplessAdvancePending != 0;

    /// <summary>描画用。カーソル・フェード・Exit・速度を 1 ロックで取る。</summary>
    internal void ReadPlaybackVisuals(
        out long cursor,
        out bool seekFading,
        out long exitFrame,
        out double speed,
        out int sourceRate)
    {
        lock (_gate)
        {
            cursor = _channels <= 0 ? 0 : (long)Math.Floor(_sourceFrame);
            seekFading = _seekFadePhase == 1;
            exitFrame = _exitPlaying ? (long)Math.Floor(_exitFrame) : -1;
            speed = _playbackSpeed;
            sourceRate = _sourceRate;
        }
    }

    /// <summary>
    /// 次の曲を音声スレッドでつなぐ。所有権は成功時に移る。通常速・非ループのストリーム再生だけ。
    /// </summary>
    public bool TryArmGapless(AudioStreamSource source, AudioDocument document)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(document);
        lock (_gate)
        {
            if (_stream is null)
            {
                return false;
            }

            ClearGaplessNoLock();
            _gaplessStream = source;
            _gaplessDocument = document;
            _gaplessArmed = true;
            return true;
        }
    }

    public void ClearGaplessNext()
    {
        lock (_gate)
        {
            ClearGaplessNoLock();
        }
    }

    public bool TryTakeGaplessAdvance(out AudioDocument? document)
    {
        if (_gaplessAdvancePending == 0)
        {
            document = null;
            return false;
        }

        lock (_gate)
        {
            document = _gaplessAdvanced;
            NoteGaplessAdvancedNoLock(null);
            return document is not null;
        }
    }

    private void NoteGaplessAdvancedNoLock(AudioDocument? document)
    {
        _gaplessAdvanced = document;
        _gaplessAdvancePending = document is null ? 0 : 1;
    }

    private void ClearGaplessNoLock()
    {
        _gaplessStream?.Dispose();
        _gaplessStream = null;
        _gaplessDocument = null;
        _gaplessArmed = false;
    }

    /// <summary>現在のストリーム終端で、先読みした次の曲へデバイスを止めずに切り替える。</summary>
    private bool TryConsumeGaplessNoLock()
    {
        if (_gaplessStream is null || _gaplessDocument is null || _loop || _scrubbing)
        {
            return false;
        }

        if (_seekFadePhase != 0)
        {
            return false;
        }

        if (Math.Abs(_playbackSpeed - 1d) > 1e-6)
        {
            return false;
        }

        var next = _gaplessStream;
        var document = _gaplessDocument;
        _gaplessStream = null;
        _gaplessDocument = null;
        _gaplessArmed = false;
        var outChannels = _outputChannels;
        var waveChannels = WaveFormat.Channels;
        var waveRate = WaveFormat.SampleRate;

        DisposeStreamNoLock(synchronous: false);
        _stream = next;
        _boundDocument = document;
        _samples = [];
        _channels = Math.Max(1, next.Channels);
        _sourceRate = Math.Max(1, next.SampleRate);
        var frames = Math.Max(0, next.FrameCount);
        var sampleCount = frames * (long)_channels;
        _usedSamples = sampleCount > int.MaxValue ? int.MaxValue : (int)sampleCount;
        if (_streamFrame.Length < _channels)
        {
            _streamFrame = new float[_channels];
        }

        if (_streamCached.Length < _channels)
        {
            _streamCached = new float[_channels];
        }

        InvalidateStreamCursorCacheNoLock();
        ClearStreamReverseBuf();
        _sourceFrame = 0;
        _cursor = 0;
        _frameGain = null;
        _silenceOnly = false;
        _flushFadeRemaining = 0;
        _flushFadeTotal = 0;
        ClearSeekFadeNoLock();
        _exitPlaying = false;
        _shuttlePrimed = false;
        _playExitLayer = false;
        ApplyOutputConfig();
        if (_outputChannels != outChannels
            || WaveFormat.Channels != waveChannels
            || WaveFormat.SampleRate != waveRate)
        {
            Ended = true;
            return false;
        }

        ApplyPlayWindowNoLock(null, loop: false);
        Ended = false;
        ResetMeterBuffers();
        lock (_monitorGate)
        {
            _meterSourceChannels = Math.Max(1, _channels);
            ResetMeterIntervalNoLock();
            ResetLoudnessNoLock();
        }

        NoteGaplessAdvancedNoLock(document);
        return true;
    }

    /// <summary>実音が尽きたときだけ次曲へ進む。見積もり長さまでの無音は挟まない。</summary>
    private bool TryHandoffGapless(double playEndFrame) =>
        StreamExhausted(playEndFrame) && TryConsumeGaplessNoLock();

    private bool StreamExhausted(double playEndFrame)
    {
        if (_playbackSpeed < 0)
        {
            return false;
        }

        if (_stream is null)
        {
            return _sourceFrame + 1 >= playEndFrame;
        }

        if (_stream.IsDrained)
        {
            return true;
        }

        var srcCh = Math.Max(1, _channels);
        return _stream.Frame >= FrameCountOrStreamEnd(srcCh) || _sourceFrame + 1 >= playEndFrame;
    }

    private void ResumeStreamAfterReverseNoLock()
    {
        if (_stream is null)
        {
            return;
        }

        var end = Math.Max(1, FrameCountOrStreamEnd(Math.Max(1, _channels)));
        var frame = (long)Math.Clamp(Math.Floor(_sourceFrame), 0, end - 1);
        _stream.SeekFrame(frame, prebufferTimeoutMs: 0);
        InvalidateStreamCursorCacheNoLock();
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
            ClearSeekFadeNoLock();
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
            ClearSeekFadeNoLock();
            SeekFrameNoLock(frame);
        }
    }

    /// <summary>
    /// クロスフェードでシークする。再生中のみ。停止・一時停止は即ジャンプ。
    /// 推移元をフェードアウトしながら、同時に推移先をフェードインする。
    /// </summary>
    public void SeekFrameCrossfade(long frame, int fadeMilliseconds)
    {
        lock (_gate)
        {
            var max = UsedFrameCountNoLock(_channels);
            var next = Math.Clamp(frame, 0, max);
            var current = _channels <= 0 ? 0L : (long)Math.Floor(_sourceFrame);
            if (_paused || _silenceOnly || fadeMilliseconds <= 0 || next == current)
            {
                ClearSeekFadeNoLock();
                SeekFrameNoLock(next);
                return;
            }

            var fadeFrames = SeekCrossfadeFrames(_deviceRate, fadeMilliseconds);
            if (_seekFadePhase == 1)
            {
                RetargetSeekFadeIncomingNoLock(next);
                return;
            }

            BeginSeekFadeNoLock(current, next, fadeFrames);
        }
    }

    internal static int SeekCrossfadeFrames(int sampleRate, int milliseconds)
    {
        var ms = Math.Clamp(milliseconds, 1, 2000);
        return Math.Max(1, Math.Max(1, sampleRate) * ms / 1000);
    }

    private void SeekFrameNoLock(long frame)
    {
        var max = UsedFrameCountNoLock(_channels);
        var next = Math.Clamp(frame, 0, max);
        _sourceFrame = next;
        _cursor = checked((int)next * _channels);
        // 音声スレッドも UI も _gate を握ったまま来る。先読み待ちで数秒止めない。
        _stream?.SeekFrame(next, prebufferTimeoutMs: 0);
        InvalidateStreamCursorCacheNoLock();
        ClearStreamReverseBuf();
        // シークでジャンプしたら進行中の Exit 二重再生は直ちに止める（IM Importer と同じ）。
        _exitPlaying = false;
        _shuttlePrimed = false;
        _rangeClicks.ResetVoice();
        Ended = false;
    }

    private void ClearSeekFadeNoLock()
    {
        DisposeSeekFadeOutNoLock();
        _seekFadePhase = 0;
        _seekFadePos = 0;
        _seekFadeFrames = 0;
        _seekFadeOutFrame = 0;
        _seekFadeOutCachedAt = -1;
        _seekFadeOutPcmFrames = 0;
        _seekFadeOutPcmPos = 0;
    }

    private void CompleteSeekFadeNoLock()
    {
        DisposeSeekFadeOutNoLock();
        ClearSeekFadeNoLock();
    }

    private void DisposeSeekFadeOutNoLock()
    {
        var outgoing = _seekFadeOutStream;
        _seekFadeOutStream = null;
        if (outgoing is null)
        {
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(
            static state =>
            {
                try
                {
                    ((AudioStreamSource)state!).Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            },
            outgoing);
    }

    private void BeginSeekFadeNoLock(long from, long next, int fadeFrames)
    {
        _seekFadeOutFrame = from;
        _seekFadeTarget = next;
        _seekFadeFrames = Math.Max(1, fadeFrames);
        _seekFadePos = 0;
        _seekFadePhase = 1;
        Ended = false;
        if (_stream is not null)
        {
            CaptureSeekFadeOutPcmNoLock(fadeFrames);
            SeekFrameNoLock(next);
            return;
        }

        _sourceFrame = next;
        _cursor = checked((int)next * Math.Max(1, _channels));
    }

    private void CaptureSeekFadeOutPcmNoLock(int fadeFrames)
    {
        _seekFadeOutPcmFrames = 0;
        _seekFadeOutPcmPos = 0;
        if (_stream is null || fadeFrames <= 0)
        {
            return;
        }

        var srcCh = Math.Max(1, _channels);
        var sourceNeed = Math.Max(1, (int)Math.Ceiling(
            fadeFrames * (_sourceRate / (double)Math.Max(1, _deviceRate))));
        if (UsesStreamRateConvert())
        {
            sourceNeed += FormatConvert.ResampleEdgePad;
        }
        var need = sourceNeed * srcCh;
        if (_seekFadeOutPcm.Length < need)
        {
            _seekFadeOutPcm = new float[need];
        }

        _seekFadeOutPcmFrames = _stream.DrainFrames(_seekFadeOutPcm, sourceNeed);
    }

    private void RetargetSeekFadeIncomingNoLock(long next)
    {
        _seekFadeTarget = next;
        Ended = false;
        if (_stream is not null)
        {
            _stream.SeekFrame(next, prebufferTimeoutMs: 0);
            InvalidateStreamCursorCacheNoLock();
            ClearStreamReverseBuf();
        }

        _sourceFrame = next;
        _cursor = checked((int)next * Math.Max(1, _channels));
    }

    internal bool WaitSeekFadeIncoming(int timeoutMs)
    {
        _ = timeoutMs;
        return true;
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
            if (paused)
            {
                CompleteSeekFadeNoLock();
            }

            _paused = paused;
        }
    }

    public void SetSilentSkip(bool enabled, double thresholdDb)
    {
        lock (_gate)
        {
            _silentSkip = enabled;
            _silentSkipLinear = SilentSkip.LinearFromDb(thresholdDb);
        }
    }

    public double PlaybackSpeed
    {
        get
        {
            lock (_gate)
            {
                return _playbackSpeed;
            }
        }
    }

    /// <summary>
    /// 再生速度。出力を止めずに切り替える。1 / <see cref="FastSpeed"/> / -<see cref="FastSpeed"/>。
    /// </summary>
    /// <returns>値が変わったとき true。</returns>
    public bool SetPlaybackSpeed(double speed)
    {
        var next = 1d;
        if (speed <= -1.5)
        {
            next = -FastSpeed;
        }
        else if (speed > 1.5)
        {
            next = FastSpeed;
        }

        lock (_gate)
        {
            if (Math.Abs(_playbackSpeed - next) < 1e-12)
            {
                return false;
            }

            var leaveReverse = _playbackSpeed < 0 && next > 0;
            _playbackSpeed = next;
            _shuttleOutputGain = Math.Abs(next) > 1.5 ? ShuttleGainLinear : 1f;
            _shuttlePrimed = false;
            Ended = false;
            CompleteSeekFadeNoLock();
            InvalidateStreamSrcWinNoLock();
            ClearStreamReverseBuf();
            if (leaveReverse)
            {
                ResumeStreamAfterReverseNoLock();
            }

            return true;
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
            ApplyPlayWindowNoLock(playRange, loop);

            // 再生ウィンドウが変わったら進行中の Exit は止める（-E 区間は呼び出し側が再設定する）。
            _exitPlaying = false;
            Ended = false;
        }
    }

    private int UsedSampleCountNoLock() =>
        _stream is not null
            ? Math.Max(0, _usedSamples)
            : Math.Clamp(_usedSamples, 0, _samples.Length);

    private int UsedFrameCountNoLock(int srcCh) =>
        srcCh <= 0 ? 0 : UsedSampleCountNoLock() / srcCh;

    private void ApplyPlayWindowNoLock(WaveSelection? playRange, bool loop)
    {
        var used = UsedSampleCountNoLock();
        if (playRange is { IsEmpty: false } range)
        {
            _loop = loop;
            _playEnd = Math.Min(checked((int)range.EndFrame * _channels), used);
            _loopStart = Math.Clamp(checked((int)range.StartFrame * _channels), 0, Math.Max(0, _playEnd));
        }
        else
        {
            _loop = false;
            _loopStart = 0;
            _playEnd = used;
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
            if (_flushFadeTotal > 0)
            {
                ClearSeekFadeNoLock();
            }
            else
            {
                ApplySeekCrossfade(buffer, offset, ref written);
            }
            pushedSource = _sourceMeterThisRead > 0;
        }

        // メーターへの書き込みは _gate の外で行う。UI スレッドは毎描画フレームで
        // CursorFrame（_gate）を読むため、コールバック内の長い保持はスクロールの
        // ジッターになる。スクラッチはこのオーディオスレッドしか触らない。
        if (pushedSource)
        {
            FlushSourceMeterScratch();
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

        if (_usedSamples <= 0 || Ended)
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
        var writtenFrames = _stream is not null
            ? ReadCoreStream(buffer, offset, framesWanted, srcCh, outCh)
            : UsesNativeRead()
                ? ReadCoreNative(buffer, offset, framesWanted, srcCh, outCh)
                : UsesShuttleRead()
                    ? ReadCoreShuttle(buffer, offset, framesWanted, srcCh, outCh)
                    : ReadCoreResampled(buffer, offset, framesWanted, srcCh, outCh);
        var written = writtenFrames * outCh;
        if (_flushFadeTotal > 0 && written < count)
        {
            Array.Clear(buffer, offset + written, count - written);
            return count;
        }

        return written;
    }

    private bool UsesNativeRead() =>
        _sourceRate == _deviceRate && Math.Abs(_playbackSpeed - 1d) < 1e-9;

    // メモリ再生の早送り／早戻し。ストリームは Read 側で先に分岐するのでここには来ない。
    private bool UsesShuttleRead() =>
        Math.Abs(_playbackSpeed) > 1.5;

    private double PlaybackStep(bool resampled) =>
        (resampled ? _sourceRate / (double)_deviceRate : 1d) * _playbackSpeed;

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

    private void ApplySeekCrossfade(float[] buffer, int offset, ref int written)
    {
        if (_seekFadePhase == 0 || written <= 0)
        {
            return;
        }

        var srcCh = Math.Max(1, _channels);
        var outCh = Math.Max(1, _outputChannels);
        var frames = written / outCh;
        if (_seekFadeOutSrc.Length < srcCh)
        {
            _seekFadeOutSrc = new float[srcCh];
        }

        if (_seekFadeMix.Length < outCh)
        {
            _seekFadeMix = new float[outCh];
        }

        var outStep = PlaybackStep(_sourceRate != _deviceRate || Math.Abs(_playbackSpeed - 1d) > 1e-9);
        if (Math.Abs(outStep) < 1e-6)
        {
            outStep = outStep < 0 ? -1e-6 : 1e-6;
        }

        var i = 0;
        while (i < frames && _seekFadePhase == 1)
        {
            var t = _seekFadeFrames <= 0 ? 1f : _seekFadePos / (float)_seekFadeFrames;
            var gOut = MathF.Cos(0.5f * MathF.PI * Math.Clamp(t, 0f, 1f));
            var gIn = MathF.Sin(0.5f * MathF.PI * Math.Clamp(t, 0f, 1f));
            ReadSeekFadeOutSource(srcCh, outStep, _seekFadeOutSrc);
            Array.Clear(_seekFadeMix, 0, outCh);
            EmitFrame(_seekFadeMix, 0, 0, outCh, _seekFadeOutSrc, 1f, mixClicks: false);
            var dest = offset + i * outCh;
            for (var channel = 0; channel < outCh; channel++)
            {
                buffer[dest + channel] = _seekFadeMix[channel] * gOut + buffer[dest + channel] * gIn;
            }

            _seekFadePos++;
            i++;
            if (_seekFadePos >= _seekFadeFrames)
            {
                ClearSeekFadeNoLock();
            }
        }
    }

    private void ReadSeekFadeOutSource(int srcCh, double outStep, Span<float> dest)
    {
        if (_seekFadeOutPcmFrames > 0)
        {
            ReadSeekFadeOutPcm(srcCh, dest);
            _seekFadeOutPcmPos += outStep;
            return;
        }

        if (_seekFadeOutStream is not null)
        {
            ReadSeekFadeOutStream(srcCh, dest);
            _seekFadeOutFrame += outStep;
            return;
        }

        var frameCount = UsedFrameCountNoLock(srcCh);
        ReadShuttleSource(_seekFadeOutFrame, srcCh, frameCount, dest);
        _seekFadeOutFrame += outStep;
    }

    private void ReadSeekFadeOutPcm(int srcCh, Span<float> dest)
    {
        if (_seekFadeOutPcmFrames <= 0 || _seekFadeOutPcm.Length < srcCh)
        {
            dest.Clear();
            return;
        }

        if (UsesStreamRateConvert())
        {
            FormatConvert.ResampleFrameBandlimited(
                _seekFadeOutPcm,
                srcCh,
                _seekFadeOutPcmPos,
                _seekFadeOutPcmFrames,
                _sourceRate,
                _deviceRate,
                dest);
            return;
        }

        var index = (int)Math.Floor(_seekFadeOutPcmPos);
        if (index < 0 || index >= _seekFadeOutPcmFrames || _seekFadeOutPcm.Length < (index + 1) * srcCh)
        {
            dest.Clear();
            return;
        }

        _seekFadeOutPcm.AsSpan(index * srcCh, srcCh).CopyTo(dest);
    }

    /// <summary>
    /// 推移元ストリームもフェードインと同じソース歩幅で進める。
    /// 出力レートと違うと、1 出力フレーム＝1 ソースフレームではピッチがずれる。
    /// </summary>
    private void ReadSeekFadeOutStream(int srcCh, Span<float> dest)
    {
        var stream = _seekFadeOutStream;
        if (stream is null)
        {
            dest.Clear();
            return;
        }

        if (_seekFadeOutCached.Length < srcCh)
        {
            _seekFadeOutCached = new float[srcCh];
        }

        var want = (long)Math.Floor(_seekFadeOutFrame);
        if (want < 0)
        {
            dest.Clear();
            return;
        }

        if (_seekFadeOutCachedAt == want)
        {
            _seekFadeOutCached.AsSpan(0, srcCh).CopyTo(dest);
            return;
        }

        var skipped = 0;
        while (stream.Frame < want && skipped < 4096)
        {
            if (!stream.TryReadFrame(_seekFadeOutCached.AsSpan(0, srcCh), timeoutMs: 0))
            {
                dest.Clear();
                _seekFadeOutCachedAt = -1;
                return;
            }

            skipped++;
        }

        if (stream.Frame != want
            || !stream.TryReadFrame(_seekFadeOutCached.AsSpan(0, srcCh), timeoutMs: 0))
        {
            dest.Clear();
            _seekFadeOutCachedAt = -1;
            return;
        }

        _seekFadeOutCachedAt = want;
        _seekFadeOutCached.AsSpan(0, srcCh).CopyTo(dest);
    }

    /// <summary>
    /// ストリーム再生。プレイヤー専用。速度変更は可変速（ピッチも変わる）で、
    /// メモリ再生のピッチ据え置きグレインシャトルには触れない。
    /// </summary>
    private int ReadCoreStream(float[] buffer, int offset, int framesWanted, int srcCh, int outCh)
    {
        if (_stream is null)
        {
            return 0;
        }

        var playEndFrame = srcCh <= 0 ? 0 : _playEnd / (double)srcCh;
        var loopStartFrame = srcCh <= 0 ? 0 : _loopStart / (double)srcCh;
        var step = PlaybackStep(resampled: _sourceRate != _deviceRate || Math.Abs(_playbackSpeed - 1d) > 1e-9);
        if (Math.Abs(step) < 1e-6)
        {
            step = step < 0 ? -1e-6 : 1e-6;
        }

        if (step < 0)
        {
            return ReadCoreStreamReverse(buffer, offset, framesWanted, srcCh, outCh, step, playEndFrame, loopStartFrame);
        }

        var writtenFrames = 0;
        var source = _streamFrame.AsSpan(0, srcCh);
        _streamSkipBudget = StreamSilentSkipMaxFrames;
        _streamSkipYield = false;
        while (writtenFrames < framesWanted)
        {
            if (_streamSkipYield)
            {
                source.Clear();
                while (writtenFrames < framesWanted)
                {
                    EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                    writtenFrames++;
                }

                break;
            }

            if (_sourceFrame >= playEndFrame)
            {
                if (_loop && playEndFrame > loopStartFrame)
                {
                    _sourceFrame = loopStartFrame;
                    _stream.SeekFrame((long)Math.Floor(loopStartFrame), prebufferTimeoutMs: 0);
                    InvalidateStreamCursorCacheNoLock();
                    continue;
                }

                if (TryHandoffGapless(playEndFrame))
                {
                    srcCh = Math.Max(1, _channels);
                    outCh = Math.Max(1, _outputChannels);
                    playEndFrame = srcCh <= 0 ? 0 : _playEnd / (double)srcCh;
                    loopStartFrame = srcCh <= 0 ? 0 : _loopStart / (double)srcCh;
                    step = PlaybackStep(resampled: _sourceRate != _deviceRate || Math.Abs(_playbackSpeed - 1d) > 1e-9);
                    source = _streamFrame.AsSpan(0, srcCh);
                    continue;
                }

                Ended = true;
                break;
            }

            if (TrySkipStreamSilenceNoLock(playEndFrame))
            {
                continue;
            }

            if (!TryReadStreamPlayFrameNoLock(source, srcCh))
            {
                if (TryHandoffGapless(playEndFrame))
                {
                    srcCh = Math.Max(1, _channels);
                    outCh = Math.Max(1, _outputChannels);
                    playEndFrame = srcCh <= 0 ? 0 : _playEnd / (double)srcCh;
                    loopStartFrame = srcCh <= 0 ? 0 : _loopStart / (double)srcCh;
                    step = PlaybackStep(resampled: _sourceRate != _deviceRate || Math.Abs(_playbackSpeed - 1d) > 1e-9);
                    source = _streamFrame.AsSpan(0, srcCh);
                    continue;
                }

                if (StreamExhausted(playEndFrame))
                {
                    Ended = true;
                    break;
                }

                // 先読み不足でヘッドを進めると、次の読みがリングより先へ離れて
                // SeekFrame が _gate を握ったままポンプ再起動を待つ。無音のまま位置を保つ。
                // ジャンプ直後のフェード中だけはヘッドを止めるとシークバーが硬直する。
                source.Clear();
                while (writtenFrames < framesWanted)
                {
                    EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                    if (_seekFadePhase == 1)
                    {
                        _sourceFrame += step;
                    }

                    writtenFrames++;
                }

                break;
            }

            var gain = _frameGain is { } gainAt ? gainAt((long)Math.Floor(_sourceFrame)) : 1f;
            EmitFrame(buffer, offset, writtenFrames, outCh, source, gain);
            _sourceFrame += step;
            writtenFrames++;
        }

        _cursor = checked((int)Math.Clamp(_sourceFrame, 0, UsedFrameCountNoLock(srcCh)) * srcCh);
        return writtenFrames;
    }

    /// <summary>ストリーム早戻し。まとめて読んでから逆方向へ可変速再生する（シーク連打を避ける）。</summary>
    private int ReadCoreStreamReverse(
        float[] buffer,
        int offset,
        int framesWanted,
        int srcCh,
        int outCh,
        double step,
        double playEndFrame,
        double loopStartFrame)
    {
        var frameCount = FrameCountOrStreamEnd(srcCh);
        var writtenFrames = 0;
        var source = _streamFrame.AsSpan(0, srcCh);
        var chunkFrames = Math.Max(1024, (int)Math.Round(_sourceRate * StreamReverseChunkSeconds));
        var overlapFrames = Math.Max(256, (int)Math.Round(_sourceRate * StreamReverseOverlapSeconds));
        EnsureReverseLast(srcCh);
        while (writtenFrames < framesWanted)
        {
            var canLoop = _loop && playEndFrame > loopStartFrame;
            if (_sourceFrame < loopStartFrame && canLoop)
            {
                _sourceFrame = playEndFrame - (loopStartFrame - _sourceFrame);
                if (_sourceFrame >= playEndFrame)
                {
                    _sourceFrame = Math.Max(loopStartFrame, playEndFrame - 1);
                }

                ClearStreamReverseBuf();
                continue;
            }

            if (_sourceFrame <= loopStartFrame && !canLoop)
            {
                _sourceFrame = loopStartFrame;
                source.Clear();
                EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                writtenFrames++;
                continue;
            }

            var frame = (long)Math.Floor(_sourceFrame);
            var got = TryReadStreamReverseFrame(
                frame, srcCh, frameCount, loopStartFrame, chunkFrames, overlapFrames, source);

            if (got && UsesStreamRateConvert())
            {
                TryResampleReversePlayFrame(srcCh, source);
            }

            if (!got)
            {
                // 届くまで音声スレッドでは待たない（Seek 完了待ちは 7/9・1/3 を重くする）。
                // このコールバックの残りは無音。次のデバイス周期でチャンクを取り直す。
                source.Clear();
                while (writtenFrames < framesWanted)
                {
                    EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                    writtenFrames++;
                }

                break;
            }

            source.CopyTo(_streamReverseLast.AsSpan(0, srcCh));
            var gain = _frameGain is { } gainAt ? gainAt(Math.Clamp(frame, 0, Math.Max(0, frameCount - 1))) : 1f;
            EmitFrame(buffer, offset, writtenFrames, outCh, source, gain);
            _sourceFrame += step;
            writtenFrames++;
        }

        _cursor = checked((int)Math.Clamp(_sourceFrame, 0, frameCount) * srcCh);
        return writtenFrames;
    }

    /// <summary>
    /// 逆方向チャンクの中を sinc する。窓の先頭を毎フレームずらさない。
    /// 端に足りなければ整数コピーのまま（早戻しは止めない）。
    /// </summary>
    private void TryResampleReversePlayFrame(int srcCh, Span<float> dest)
    {
        if (TryResampleReverseBuf(
                _streamReverseBuf, _streamReverseBufStart, _streamReverseBufFrames, srcCh, dest))
        {
            return;
        }

        TryResampleReverseBuf(
            _streamReverseNext, _streamReverseNextStart, _streamReverseNextFrames, srcCh, dest);
    }

    private bool TryResampleReverseBuf(
        float[] buf,
        long start,
        int frames,
        int srcCh,
        Span<float> dest)
    {
        if (start < 0 || frames <= 0 || buf.Length < srcCh)
        {
            return false;
        }

        var local = _sourceFrame - start;
        if (local < 0 || local >= frames)
        {
            return false;
        }

        FormatConvert.ResampleFrameBandlimited(
            buf,
            srcCh,
            local,
            frames,
            _sourceRate,
            _deviceRate,
            dest);
        return true;
    }

    private bool TryReadStreamReverseFrame(
        long frame,
        int srcCh,
        long frameCount,
        double loopStartFrame,
        int chunkFrames,
        int overlapFrames,
        Span<float> dest)
    {
        if (frame < 0 || frame >= frameCount)
        {
            dest.Clear();
            return false;
        }

        var fromCurrent = TryCopyReverseBuf(frame, _streamReverseBufStart, _streamReverseBufFrames, _streamReverseBuf, srcCh, dest);
        if (fromCurrent)
        {
            // 今のチャンクがメモリにあるときだけ次を先読みする。先にシークするとポンプを奪う。
            PrefetchStreamReverseNext(frame, srcCh, frameCount, loopStartFrame, chunkFrames, overlapFrames);
        }

        var nextIndex = (int)(frame - _streamReverseNextStart);
        var fromNext = _streamReverseNextStart >= 0
            && _streamReverseNextFrames > 0
            && _streamReverseNext.Length >= srcCh
            && (uint)nextIndex < (uint)_streamReverseNextFrames;
        if (fromCurrent && fromNext)
        {
            var t = overlapFrames <= 0
                ? 0f
                : Math.Clamp((frame - _streamReverseBufStart) / (float)overlapFrames, 0f, 1f);
            var gCurrent = MathF.Sin(0.5f * MathF.PI * t);
            var gNext = MathF.Cos(0.5f * MathF.PI * t);
            var nextAt = nextIndex * srcCh;
            for (var i = 0; i < srcCh; i++)
            {
                dest[i] = dest[i] * gCurrent + _streamReverseNext[nextAt + i] * gNext;
            }

            return true;
        }

        if (fromCurrent)
        {
            return true;
        }

        if (fromNext)
        {
            _streamReverseNext.AsSpan(nextIndex * srcCh, srcCh).CopyTo(dest);
            if (frame < _streamReverseBufStart)
            {
                PromoteReverseNext();
            }

            return true;
        }

        return FillStreamReverseCurrent(frame, srcCh, frameCount, loopStartFrame, chunkFrames, dest);
    }

    private static bool TryCopyReverseBuf(
        long frame,
        long start,
        int filled,
        float[] buf,
        int srcCh,
        Span<float> dest)
    {
        var index = (int)(frame - start);
        if (start < 0 || filled <= 0 || buf.Length < srcCh || (uint)index >= (uint)filled)
        {
            return false;
        }

        buf.AsSpan(index * srcCh, srcCh).CopyTo(dest);
        return true;
    }

    private bool FillStreamReverseCurrent(
        long frame,
        int srcCh,
        long frameCount,
        double loopStartFrame,
        int chunkFrames,
        Span<float> dest)
    {
        var end = frame;
        if (UsesStreamRateConvert())
        {
            end = Math.Min(frameCount - 1, frame + FormatConvert.ResampleEdgePad);
        }

        var start = Math.Max((long)Math.Floor(loopStartFrame), frame - chunkFrames + 1);
        start = Math.Clamp(start, 0, Math.Max(0, frameCount - 1));
        end = Math.Clamp(end, start, Math.Max(0, frameCount - 1));
        FillReverseWindow(
            ref _streamReverseBuf,
            ref _streamReverseBufStart,
            ref _streamReverseBufFrames,
            start,
            end,
            srcCh);
        var index = (int)(frame - _streamReverseBufStart);
        if (_streamReverseBufFrames <= 0 || (uint)index >= (uint)_streamReverseBufFrames)
        {
            dest.Clear();
            return false;
        }

        _streamReverseBuf.AsSpan(index * srcCh, srcCh).CopyTo(dest);
        return true;
    }

    private void PrefetchStreamReverseNext(
        long frame,
        int srcCh,
        long frameCount,
        double loopStartFrame,
        int chunkFrames,
        int overlapFrames)
    {
        if (_streamReverseBufFrames <= 0 || _streamReverseBufStart < 0)
        {
            return;
        }

        if (frame - _streamReverseBufStart > overlapFrames * 2)
        {
            return;
        }

        var loopStart = (long)Math.Floor(loopStartFrame);
        var nextEnd = Math.Min(frameCount - 1, _streamReverseBufStart + overlapFrames - 1);
        var nextStart = Math.Max(loopStart, nextEnd - chunkFrames + 1);
        nextStart = Math.Clamp(nextStart, 0, Math.Max(0, frameCount - 1));
        if (nextStart >= _streamReverseBufStart)
        {
            return;
        }

        FillReverseWindow(
            ref _streamReverseNext,
            ref _streamReverseNextStart,
            ref _streamReverseNextFrames,
            nextStart,
            nextEnd,
            srcCh);
    }

    private void FillReverseWindow(
        ref float[] buf,
        ref long bufStart,
        ref int bufFrames,
        long start,
        long end,
        int srcCh)
    {
        var len = (int)(end - start + 1);
        if (len <= 0)
        {
            return;
        }

        if (bufStart == start && bufFrames >= len)
        {
            return;
        }

        var need = len * srcCh;
        if (buf.Length < need)
        {
            buf = new float[need];
        }

        long filledFrom;
        if (bufStart == start)
        {
            filledFrom = start + bufFrames;
        }
        else
        {
            Array.Clear(buf, 0, need);
            bufStart = start;
            bufFrames = 0;
            filledFrom = start;
            if (_stream is not null && _stream.Frame != filledFrom)
            {
                _stream.SeekFrame(filledFrom, prebufferTimeoutMs: 0);
                InvalidateStreamCursorCacheNoLock();
            }
        }

        bufFrames += FillStreamReverseRange(buf, start, filledFrom, end, srcCh);
    }

    private void PromoteReverseNext()
    {
        _streamReverseBuf = _streamReverseNext;
        _streamReverseBufStart = _streamReverseNextStart;
        _streamReverseBufFrames = _streamReverseNextFrames;
        _streamReverseNext = [];
        _streamReverseNextStart = -1;
        _streamReverseNextFrames = 0;
    }

    private int FillStreamReverseRange(float[] buf, long start, long from, long end, int srcCh)
    {
        if (_stream is null || from > end)
        {
            return 0;
        }

        var want = (int)(end - from + 1);
        if (want <= 0)
        {
            return 0;
        }

        if (_stream.Frame != from)
        {
            _stream.SeekFrame(from, prebufferTimeoutMs: 0);
            InvalidateStreamCursorCacheNoLock();
        }

        var got = _stream.ReadFrames(buf, (int)(from - start) * srcCh, want, timeoutMs: 0);
        if (got > 0)
        {
            InvalidateStreamCursorCacheNoLock();
        }

        return got;
    }

    private void EnsureReverseLast(int srcCh)
    {
        if (_streamReverseLast.Length < srcCh)
        {
            _streamReverseLast = new float[srcCh];
        }
    }

    private void ClearStreamReverseBuf()
    {
        _streamReverseBufStart = -1;
        _streamReverseBufFrames = 0;
        _streamReverseNextStart = -1;
        _streamReverseNextFrames = 0;
    }

    private long FrameCountOrStreamEnd(int srcCh) =>
        _stream?.FrameCount ?? UsedFrameCountNoLock(srcCh);

    private bool UsesStreamRateConvert() =>
        _sourceRate != _deviceRate;

    private void InvalidateStreamSrcWinNoLock()
    {
        _streamSrcWinStart = -1;
        _streamSrcWinFrames = 0;
    }

    private void InvalidateStreamCursorCacheNoLock()
    {
        _streamCachedAt = -1;
        InvalidateStreamSrcWinNoLock();
    }

    private bool StreamSrcWinContainsFrame(long frame) =>
        _streamSrcWinStart >= 0
        && frame >= _streamSrcWinStart
        && frame < _streamSrcWinStart + _streamSrcWinFrames;

    private bool TryCopyStreamSrcWin(long frame, Span<float> dest)
    {
        var srcCh = Math.Max(1, _channels);
        var index = (int)(frame - _streamSrcWinStart);
        if (!StreamSrcWinContainsFrame(frame)
            || dest.Length < srcCh
            || _streamSrcWin.Length < (index + 1) * srcCh)
        {
            return false;
        }

        _streamSrcWin.AsSpan(index * srcCh, srcCh).CopyTo(dest);
        return true;
    }

    private void EnsureStreamSrcWinCapacity(int frames, int srcCh)
    {
        srcCh = Math.Max(1, srcCh);
        var capFrames = _streamSrcWin.Length / srcCh;
        if (capFrames >= frames)
        {
            return;
        }

        var nextFrames = Math.Max(frames, Math.Max(64, capFrames * 2));
        var next = new float[nextFrames * srcCh];
        if (_streamSrcWinFrames > 0 && _streamSrcWin.Length >= _streamSrcWinFrames * srcCh)
        {
            Array.Copy(_streamSrcWin, next, _streamSrcWinFrames * srcCh);
        }

        _streamSrcWin = next;
    }

    private void DropStreamSrcWinBefore(long from, int srcCh)
    {
        var drop = (int)(from - _streamSrcWinStart);
        if (drop <= 0)
        {
            return;
        }

        var remain = _streamSrcWinFrames - drop;
        if (remain <= 0)
        {
            _streamSrcWinStart = from;
            _streamSrcWinFrames = 0;
            return;
        }

        Array.Copy(_streamSrcWin, drop * srcCh, _streamSrcWin, 0, remain * srcCh);
        _streamSrcWinStart = from;
        _streamSrcWinFrames = remain;
    }

    private bool FillStreamSrcWinToNoLock(long toInclusive, int srcCh)
    {
        while (_streamSrcWinStart + _streamSrcWinFrames <= toInclusive)
        {
            var frame = _streamSrcWinStart + _streamSrcWinFrames;
            EnsureStreamSrcWinCapacity(_streamSrcWinFrames + 1, srcCh);
            var dest = _streamSrcWin.AsSpan(_streamSrcWinFrames * srcCh, srcCh);
            if (!ReadStreamFrameRawNoLock(frame, dest))
            {
                return _streamSrcWinFrames > 0;
            }

            if (_streamCached.Length < srcCh)
            {
                _streamCached = new float[srcCh];
            }

            dest.CopyTo(_streamCached.AsSpan(0, srcCh));
            _streamCachedAt = frame;
            _streamSrcWinFrames++;
        }

        return true;
    }

    private bool EnsureStreamSrcWinNoLock(long from, long to, int srcCh)
    {
        if (_stream is null || srcCh <= 0)
        {
            return false;
        }

        var maxFrame = Math.Max(0, UsedFrameCountNoLock(srcCh) - 1);
        from = Math.Clamp(from, 0, maxFrame);
        to = Math.Clamp(to, from, maxFrame);
        var streamPos = _stream.Frame;
        if (_streamSrcWinStart >= 0 && _streamSrcWinFrames > 0)
        {
            var winEnd = _streamSrcWinStart + _streamSrcWinFrames;
            if (from < _streamSrcWinStart)
            {
                // 左側が足りなくても巻き戻しシークしない。sinc が端を繰り返す。
                from = _streamSrcWinStart;
                if (to < from)
                {
                    to = from;
                }
            }

            if (from >= _streamSrcWinStart && from < winEnd)
            {
                DropStreamSrcWinBefore(from, srcCh);
            }
            else
            {
                InvalidateStreamSrcWinNoLock();
                _streamSrcWinStart = Math.Clamp(Math.Max(from, streamPos), 0, maxFrame);
                _streamSrcWinFrames = 0;
            }
        }
        else
        {
            _streamSrcWinStart = Math.Clamp(Math.Max(from, streamPos), 0, maxFrame);
            _streamSrcWinFrames = 0;
        }

        var slackTo = Math.Min(maxFrame, Math.Max(to, _streamSrcWinStart) + StreamSrcWinSlack);
        FillStreamSrcWinToNoLock(slackTo, srcCh);
        return _streamSrcWinFrames > 0;
    }

    private bool TryReadStreamPlayFrameNoLock(Span<float> dest, int srcCh)
    {
        if (!UsesStreamRateConvert())
        {
            return EnsureStreamFrameNoLock((long)Math.Floor(_sourceFrame), dest);
        }

        var pad = FormatConvert.ResampleEdgePad;
        var center = (long)Math.Floor(_sourceFrame);
        EnsureStreamSrcWinNoLock(center - pad, center + pad, srcCh);
        if (_streamSrcWinFrames <= 0 || _streamSrcWinStart < 0)
        {
            dest.Clear();
            return false;
        }

        FormatConvert.ResampleFrameBandlimited(
            _streamSrcWin,
            srcCh,
            _sourceFrame - _streamSrcWinStart,
            _streamSrcWinFrames,
            _sourceRate,
            _deviceRate,
            dest);
        return true;
    }

    /// <summary>
    /// リングから 1 フレーム読む。音声スレッドでは巻き戻しシークしない。
    /// </summary>
    private bool ReadStreamFrameRawNoLock(long frame, Span<float> dest)
    {
        if (_stream is null)
        {
            dest.Clear();
            return false;
        }

        var current = _stream.Frame;
        if (frame < current)
        {
            dest.Clear();
            return false;
        }

        if (frame > current)
        {
            if (frame - current > 4096)
            {
                dest.Clear();
                return false;
            }

            while (_stream.Frame < frame)
            {
                if (!_stream.TryReadFrame(_streamCached.AsSpan(0, _channels), timeoutMs: 0))
                {
                    dest.Clear();
                    return false;
                }
            }
        }

        if (!_stream.TryReadFrame(dest, timeoutMs: 0))
        {
            dest.Clear();
            return false;
        }

        return true;
    }

    private bool EnsureStreamFrameNoLock(long frame, Span<float> dest)
    {
        if (_stream is null)
        {
            dest.Clear();
            return false;
        }

        if (frame < 0 || frame >= UsedFrameCountNoLock(_channels))
        {
            dest.Clear();
            return false;
        }

        if (TryCopyStreamSrcWin(frame, dest))
        {
            return true;
        }

        if (_streamCachedAt == frame)
        {
            _streamCached.AsSpan(0, _channels).CopyTo(dest);
            return true;
        }

        EnsureStreamSrcWinNoLock(frame, frame, Math.Max(1, _channels));
        if (!TryCopyStreamSrcWin(frame, dest))
        {
            dest.Clear();
            _streamCachedAt = -1;
            return false;
        }

        if (_streamCached.Length < dest.Length)
        {
            _streamCached = new float[Math.Max(_channels, dest.Length)];
        }

        dest.CopyTo(_streamCached.AsSpan(0, dest.Length));
        _streamCachedAt = frame;
        return true;
    }

    private bool TrySkipStreamSilenceNoLock(double playEndFrame)
    {
        if (_stream is null || !_silentSkip || Math.Abs(_playbackSpeed - 1d) > 1e-9)
        {
            return false;
        }

        var start = (long)Math.Floor(_sourceFrame);
        var end = (long)Math.Floor(playEndFrame);
        if (start < 0 || start >= end)
        {
            return false;
        }

        var hold = SilentSkip.PeakWindowRadiusFrames(_sourceRate);
        var peaks = _boundDocument?.Peaks;
        if (peaks is not null && peaks.TryIsSilent(start, _silentSkipLinear, hold, out var silent))
        {
            if (!silent)
            {
                return TrySkipStreamSilenceFromRingNoLock(start, end);
            }

            var next = peaks.FindNextAudibleFrame(start, end, _silentSkipLinear);
            if (next < end)
            {
                next = Math.Max(start, next - hold);
                if (next <= start)
                {
                    return TrySkipStreamSilenceFromRingNoLock(start, end);
                }

                ApplyStreamSilentSkipJump(next);
                return true;
            }

            if (_loop && playEndFrame > _loopStart / (double)Math.Max(1, _channels))
            {
                var loopStart = (long)Math.Floor(_loopStart / (double)Math.Max(1, _channels));
                var wrap = peaks.FindNextAudibleFrame(loopStart, end, _silentSkipLinear);
                if (wrap < end)
                {
                    ApplyStreamSilentSkipJump(Math.Max(loopStart, wrap - hold));
                    return true;
                }

                return false;
            }

            ApplyStreamSilentSkipJump(end);
            return true;
        }

        return TrySkipStreamSilenceFromRingNoLock(start, end);
    }

    private bool TrySkipStreamSilenceFromRingNoLock(long start, long end)
    {
        if (_stream is null)
        {
            return false;
        }

        if (_streamSkipBudget <= 0)
        {
            _streamSkipYield = true;
            return true;
        }

        var dest = _streamFrame.AsSpan(0, _channels);
        if (!EnsureStreamFrameNoLock(start, dest) || !IsScratchFrameSilent())
        {
            return false;
        }

        // 瞬間無音だけではトーンのゼロ交差を落とす。先の 50ms も谷なら本格的な無音。
        var hold = SilentSkip.PeakHoldFrames(_sourceRate);
        if (!_stream.TryPeekAbsPeak(0, hold, out var ahead) || ahead >= _silentSkipLinear)
        {
            return false;
        }

        var frame = start;
        var skipped = 0;
        var maxSkip = _streamSkipBudget;
        while (frame < end && skipped < maxSkip)
        {
            if (!EnsureStreamFrameNoLock(frame, dest))
            {
                break;
            }

            if (!IsScratchFrameSilent())
            {
                _sourceFrame = frame;
                return skipped > 0;
            }

            frame++;
            skipped++;
        }

        if (skipped <= 0)
        {
            return false;
        }

        _streamSkipBudget -= skipped;
        _sourceFrame = frame;
        _cursor = CursorSampleFromFrame(frame);
        if (frame < end && _streamSkipBudget <= 0)
        {
            _streamSkipYield = true;
        }

        return true;
    }

    private void ApplyStreamSilentSkipJump(long frame)
    {
        _sourceFrame = frame;
        _cursor = CursorSampleFromFrame(frame);
        InvalidateStreamCursorCacheNoLock();
        if (_stream is null)
        {
            return;
        }

        var current = _stream.Frame;
        if (frame == current)
        {
            return;
        }

        if (frame < current || frame - current > 64)
        {
            _stream.SeekFrame(frame, prebufferTimeoutMs: 0);
            return;
        }

        var scratch = _streamCached.AsSpan(0, _channels);
        while (_stream.Frame < frame)
        {
            if (!_stream.TryReadFrame(scratch, timeoutMs: 0))
            {
                return;
            }
        }
    }

    private int CursorSampleFromFrame(long frame)
    {
        var srcCh = Math.Max(1, _channels);
        var maxFrame = UsedFrameCountNoLock(srcCh);
        return checked((int)Math.Clamp(frame, 0, maxFrame) * srcCh);
    }

    private bool IsScratchFrameSilent()
    {
        var floor = Math.Max(0f, _silentSkipLinear);
        for (var i = 0; i < _channels; i++)
        {
            if (!ChannelSolo.Contains(_soloMask, i))
            {
                continue;
            }

            if (Math.Abs(_streamFrame[i]) >= floor)
            {
                return false;
            }
        }

        return true;
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

            if (TrySkipSilenceNoLock(srcCh, _cursor / srcCh))
            {
                continue;
            }

            var frames = Math.Min(framesWanted - writtenFrames, (_playEnd - _cursor) / srcCh);
            if (frames <= 0)
            {
                Ended = true;
                break;
            }

            for (var i = 0; i < frames; i++)
            {
                if (i > 0
                    && _silentSkip
                    && SilentSkip.IsFrameSilent(
                        _samples,
                        srcCh,
                        _cursor / srcCh,
                        _silentSkipLinear,
                        _soloMask,
                        SilentSkip.PeakWindowRadiusFrames(_sourceRate)))
                {
                    break;
                }

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
        var frameCount = UsedFrameCountNoLock(srcCh);
        var step = PlaybackStep(resampled: true);
        var writtenFrames = 0;
        var exitMixedFrames = 0;
        Span<float> frame = stackalloc float[ChannelLayout.MaxChannels];
        var source = frame[..Math.Min(srcCh, ChannelLayout.MaxChannels)];
        while (writtenFrames < framesWanted)
        {
            if (step < 0)
            {
                var canLoop = _loop && playEndFrame > loopStartFrame;
                if (_sourceFrame < loopStartFrame && canLoop)
                {
                    _sourceFrame = playEndFrame - (loopStartFrame - _sourceFrame);
                    if (_sourceFrame >= playEndFrame)
                    {
                        _sourceFrame = Math.Max(loopStartFrame, playEndFrame - 1);
                    }

                    continue;
                }

                if (_sourceFrame <= loopStartFrame && !canLoop)
                {
                    _sourceFrame = loopStartFrame;
                    source.Clear();
                    EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                    writtenFrames++;
                    continue;
                }
            }
            else if (_sourceFrame >= playEndFrame)
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

            if (TrySkipSilenceNoLock(srcCh, (long)Math.Floor(_sourceFrame)))
            {
                continue;
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
    /// ピッチ据え置きの早送り／巻き戻し。グレインを原速で出し、ヘッドだけ倍速で動かす。
    /// </summary>
    private int ReadCoreShuttle(float[] buffer, int offset, int framesWanted, int srcCh, int outCh)
    {
        var playEndFrame = srcCh <= 0 ? 0 : _playEnd / (double)srcCh;
        var loopStartFrame = srcCh <= 0 ? 0 : _loopStart / (double)srcCh;
        var frameCount = UsedFrameCountNoLock(srcCh);
        var step = PlaybackStep(resampled: true);
        var grainStep = _sourceRate / (double)Math.Max(1, _deviceRate);
        var grainFrames = Math.Max(64, (int)Math.Round(ShuttleGrainSeconds * _sourceRate));
        var taper = Math.Clamp((int)Math.Round(ShuttleTaperSeconds * _sourceRate), 1, grainFrames / 4);
        var rewind = _playbackSpeed < 0;
        var dir = rewind ? -1 : 1;
        var writtenFrames = 0;
        Span<float> frame = stackalloc float[ChannelLayout.MaxChannels];
        var source = frame[..Math.Min(srcCh, ChannelLayout.MaxChannels)];
        while (writtenFrames < framesWanted)
        {
            if (rewind)
            {
                var canLoop = _loop && playEndFrame > loopStartFrame;
                if (_sourceFrame < loopStartFrame && canLoop)
                {
                    _sourceFrame = playEndFrame - (loopStartFrame - _sourceFrame);
                    if (_sourceFrame >= playEndFrame)
                    {
                        _sourceFrame = Math.Max(loopStartFrame, playEndFrame - grainStep);
                    }

                    _shuttlePrimed = false;
                    continue;
                }

                if (_sourceFrame <= loopStartFrame && !canLoop)
                {
                    _sourceFrame = loopStartFrame;
                    source.Clear();
                    EmitFrame(buffer, offset, writtenFrames, outCh, source, 0f);
                    writtenFrames++;
                    continue;
                }
            }
            else if (_sourceFrame >= playEndFrame)
            {
                if (_loop && playEndFrame > loopStartFrame)
                {
                    _sourceFrame = loopStartFrame + (_sourceFrame - playEndFrame);
                    _shuttlePrimed = false;
                    BeginExitOnLoopWrapNoLock();
                    continue;
                }

                Ended = true;
                break;
            }

            if (!_shuttlePrimed || _shuttleRead >= grainFrames)
            {
                _shuttleFadeIn = _shuttlePrimed;
                _shuttleOrigin = _sourceFrame;
                _shuttleRead = 0;
                _shuttlePrimed = true;
            }

            var audioFrame = _shuttleOrigin + dir * _shuttleRead;
            ReadShuttleSource(audioFrame, srcCh, frameCount, source);
            var window = ShuttleWindow(_shuttleRead, grainFrames, taper, _shuttleFadeIn);
            var gain = _frameGain is { } gainAt
                ? gainAt((long)Math.Floor(Math.Clamp(audioFrame, 0, Math.Max(0, frameCount - 1)))) * window
                : window;
            EmitFrame(buffer, offset, writtenFrames, outCh, source, gain);
            _shuttleRead += grainStep;
            _sourceFrame += step;
            writtenFrames++;
        }

        _cursor = checked((int)Math.Clamp(_sourceFrame, 0, frameCount) * srcCh);
        return writtenFrames;
    }

    private void ReadShuttleSource(double sourceFrame, int srcCh, int frameCount, Span<float> dest)
    {
        var used = UsedSampleCountNoLock();
        if (sourceFrame < 0 || sourceFrame >= frameCount || used < srcCh)
        {
            dest.Clear();
            return;
        }

        if (_sourceRate == _deviceRate && Math.Abs(sourceFrame - Math.Round(sourceFrame)) < 1e-6)
        {
            var at = checked((int)Math.Round(sourceFrame) * srcCh);
            if ((uint)at > (uint)(used - srcCh))
            {
                dest.Clear();
                return;
            }

            _samples.AsSpan(at, srcCh).CopyTo(dest);
            return;
        }

        FormatConvert.ResampleFrameBandlimited(
            _samples,
            srcCh,
            sourceFrame,
            frameCount,
            _sourceRate,
            _deviceRate,
            dest);
    }

    private static float ShuttleWindow(double read, int grainFrames, int taper, bool fadeIn)
    {
        if (fadeIn && read < taper)
        {
            return 0.5f * (1f - MathF.Cos((float)(Math.PI * read / taper)));
        }

        if (read > grainFrames - taper)
        {
            return 0.5f * (1f - MathF.Cos((float)(Math.PI * (grainFrames - read) / taper)));
        }

        return 1f;
    }

    /// <returns>カーソルを動かした（続きの判定が必要）。</returns>
    private bool TrySkipSilenceNoLock(int srcCh, long start)
    {
        if (!_silentSkip || _usedSamples <= 0 || UsesShuttleRead() || _playbackSpeed < 0)
        {
            return false;
        }

        var end = srcCh <= 0 ? 0 : _playEnd / srcCh;
        if (start < 0 || start >= end)
        {
            return false;
        }

        var hold = SilentSkip.PeakWindowRadiusFrames(_sourceRate);
        if (!SilentSkip.IsFrameSilent(_samples, srcCh, start, _silentSkipLinear, _soloMask, hold))
        {
            return false;
        }

        var next = SilentSkip.FindNextAudible(
            _samples,
            srcCh,
            start,
            end,
            _silentSkipLinear,
            _soloMask,
            hold);
        if (next < end)
        {
            ApplySilentSkipJump(next, srcCh, wrapped: false);
            return true;
        }

        if (_loop && _playEnd > _loopStart)
        {
            var loopStart = _loopStart / (double)srcCh;
            var wrap = SilentSkip.FindNextAudible(
                _samples,
                srcCh,
                (long)loopStart,
                end,
                _silentSkipLinear,
                _soloMask,
                hold);
            if (wrap < end)
            {
                ApplySilentSkipJump(wrap, srcCh, wrapped: true);
                return true;
            }

            return false;
        }

        ApplySilentSkipJump(end, srcCh, wrapped: false);
        Ended = true;
        return true;
    }

    private void ApplySilentSkipJump(long frame, int srcCh, bool wrapped)
    {
        _sourceFrame = frame;
        _cursor = checked((int)frame * srcCh);
        _exitPlaying = false;
        if (wrapped)
        {
            BeginExitOnLoopWrapNoLock();
        }
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

        var frameCount = UsedFrameCountNoLock(srcCh);
        var endFrame = Math.Min(_exitSpanEndFrame, frameCount);
        var step = PlaybackStep(resampled);
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
        EmitFrame(buffer, offset, writtenFrames, outCh, source, gain, sourceFrame);
    }

    /// <summary>1 フレーム分のソース信号をメーターへ流し、ルーティングして出力へ書く。</summary>
    private void EmitFrame(
        float[] buffer,
        int offset,
        int writtenFrames,
        int outCh,
        ReadOnlySpan<float> source,
        float gain,
        long? clickSourceFrame = null,
        bool mixClicks = true)
    {
        gain *= _shuttleOutputGain;
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
                MixRangeClick(buffer, offset, writtenFrames, outCh, clickSourceFrame, mixClicks);
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

            MixRangeClick(buffer, offset, writtenFrames, outCh, clickSourceFrame, mixClicks);
            return;
        }

        ChannelMix.Downmix(ToSpeakerFrame(source), out var left, out var right);
        if (gain != 1f)
        {
            left *= gain;
            right *= gain;
        }

        WriteFrame(buffer, offset, writtenFrames, outCh, left, right);
        MixRangeClick(buffer, offset, writtenFrames, outCh, clickSourceFrame, mixClicks);
    }

    private void MixRangeClick(
        float[] buffer,
        int offset,
        int writtenFrames,
        int outCh,
        long? clickSourceFrame,
        bool mixClicks)
    {
        if (!mixClicks || !_rangeClicks.Enabled)
        {
            return;
        }

        var sample = _rangeClicks.Advance(clickSourceFrame ?? (long)Math.Floor(_sourceFrame));
        if (sample == 0f || outCh <= 0)
        {
            return;
        }

        var dest = offset + writtenFrames * outCh;
        for (var channel = 0; channel < outCh; channel++)
        {
            buffer[dest + channel] += sample;
        }
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

        // スクラブは L/R しか書かないため、サラウンド出力では ch2 以降に
        // 再利用バッファへ残った直前の再生音がそのまま鳴り続けてしまう。
        // 通常再生の EmitFrame と同じく、先にクリアしてから書く。
        if (outCh > 2)
        {
            Array.Clear(buffer, offset, framesWanted * outCh);
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

    private void FlushSourceMeterScratch()
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
