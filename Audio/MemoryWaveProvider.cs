using System.Globalization;
using System.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
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

internal sealed partial class PlaybackSampleProvider : ISampleProvider
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
    /// <summary>オンなら出力を L+R 合算のモノラル監視にする（メーターは元のまま）。</summary>
    private bool _monitorMono;
    /// <summary>オンなら出力だけ DIM（メーターは元のまま）。</summary>
    private bool _monitorDim;
    private double _monitorDimDb = DefaultMonitorDimDb;
    private float _monitorDimLinear = DefaultMonitorDimLinear;
    /// <summary>オンなら出力にローカット（メーターは元のまま）。</summary>
    private bool _monitorLowCut;
    private double _monitorLowCutHz = DefaultMonitorLowCutHz;
    private BpmMonitorMode _bpmMonitorMode;
    private EffeTuneBpmEstimator? _bpmMonitor;
    private readonly BpmMonitorClickMixer _bpmClicks = new();
    private float[] _bpmScratch = [];
    private readonly List<int> _bpmOnsets = [];
    /// <summary>モニター LC: RBJ ローシェルフ（ゲイン固定 −20 dB、Q≈0.707）。</summary>
    private bool _lowCutCoeffReady;
    private float _lowCutB0;
    private float _lowCutB1;
    private float _lowCutB2;
    private float _lowCutA1;
    private float _lowCutA2;
    private float _lowCutS1L;
    private float _lowCutS2L;
    private float _lowCutS1R;
    private float _lowCutS2R;
    private bool _directRoute;
    private int _deviceRate = 48000;
    private int _sourceRate = 48000;
    private double _sourceFrame;
    private int _cursor;
    private int _loopStart;
    private int _playEnd;
    private bool _loop;
    private Func<long, float>? _frameGain;
    private LoopFadePreview? _loopFadePreview;
    private float[] _fadeMixScratch = [];
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
    private readonly double[] _intervalSum = new double[ChannelLayout.MaxChannels];
    private readonly double[] _intervalSumSq = new double[ChannelLayout.MaxChannels];
    private int _meterWrite;
    private int _meterCount;
    private int _meterSourceChannels = 2;
    private int _sourceMeterThisRead;
    private float[] _sourceMeterScratch = [];
    private float[] _speakerScratch = [];
    private int _sourceMeterScratchFrames;
    private int _sourceMeterScratchChannels = 1;
    private readonly float[] _monitorRing = new float[SpectrumAnalyzer.FftSize];
    private long _monitorWriteCount;
    private readonly object _monitorGate = new();
    private readonly MemoryScrubVoice _scrub = new();
    private bool _scrubbing;
    private float[] _scrubScratch = [];
    private float[] _streamScrubBuf = [];
    private long _lastStreamScrubCaptureMs;
    /// <summary>ストリームスクラブのグレイン採取間隔。UI スレッドを塞ぎすぎない。</summary>
    internal const int StreamScrubCaptureMinIntervalMs = 50;
    private int _intervalFrames;
    private readonly float[] _loudL = new float[8192];
    private readonly float[] _loudR = new float[8192];
    private int _loudWrite;
    private int _loudCount;
    private bool _silenceOnly;
    private bool _paused;
    private bool _silentSkip;
    private float _silentSkipLinear = SilentSkip.LinearFromDb(SilentSkip.DefaultThresholdDb);
    /// <summary>
    /// ループ区間に可聴が無く飛ばす先が無いとき true。
    /// UI の Silent Skip は触らず、この Bind 中だけ走査を止める（全文再走査で固まらないようにする）。
    /// </summary>
    private bool _silentSkipIdleLoop;
    /// <summary>再生ヘッドの進行倍率。1 / <see cref="FastSpeed"/> / -<see cref="FastSpeed"/>。</summary>
    private double _playbackSpeed = 1;
    private int _playbackSemitones;
    private readonly PlaybackPitchShift _pitch = new();
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
    private float[] _streamReversePrerollScratch = [];
    /// <summary>順再生で読んだ直近フレーム。早戻しシークの着地をここに合わせる。</summary>
    private float[] _streamRecentPcm = [];
    private int _streamRecentWrite;
    private int _streamRecentCount;
    private int _streamRecentChannels;
    private long _streamRecentNext = -1;
    private bool _streamRecentValid;
    private const int StreamRecentMaxFrames = 4096;
    private const int StreamReverseAlignFrames = 256;
    /// <summary>1 回の Read でリングから捨てる無音の上限。超えたら次のコールバックへ回す。</summary>
    private int _streamSkipBudget;
    private bool _streamSkipYield;
    /// <summary>
    /// リングに溜まっている分まで。時刻シークは使わない。
    /// MP3 の CurrentTime はフレーム境界で、飛んだあとの音とシークバーがずれる。
    /// </summary>
    private const int StreamSilentSkipMaxFrames = AudioStreamSource.RingFrames;

    /// <summary>早送りはピッチ据え置きグレイン、巻き戻しは逆再生。</summary>
    internal const double FastSpeed = 3;
    /// <summary>1／3（および ←→ 早送り）の推移中に下げる音量。</summary>
    internal const double ShuttleGainDb = -4;
    internal static readonly float ShuttleGainLinear = (float)Math.Pow(10, ShuttleGainDb / 20.0);
    /// <summary>モニター DIM の規定減衰（dB）。</summary>
    internal const double DefaultMonitorDimDb = -15;
    internal const double MinMonitorDimDb = -60;
    internal const double MaxMonitorDimDb = 0;
    internal static readonly float DefaultMonitorDimLinear =
        (float)Math.Pow(10, DefaultMonitorDimDb / 20.0);
    /// <summary>互換用。規定 DIM の線形ゲイン。</summary>
    internal static readonly float MonitorDimLinear = DefaultMonitorDimLinear;
    /// <summary>モニターローカット（ローシェルフ）の規定カットオフ（Hz）。RME Lo Cut と同様 20〜500。</summary>
    internal const double DefaultMonitorLowCutHz = 120;
    internal const double MinMonitorLowCutHz = 20;
    internal const double MaxMonitorLowCutHz = 500;
    /// <summary>ローシェルフゲイン（dB）。設定には出さず内部固定。RME の −20 dB シェルフ相当。</summary>
    internal const double FixedMonitorLowCutGainDb = -20;
    private const double ShuttleGrainSeconds = 0.03;
    private const double ShuttleTaperSeconds = 0.004;
    /// <summary>圧縮ストリーム早戻しの窓。Seek 回数を抑えつつ継ぎ目を滑らかにする。</summary>
    private const double StreamReverseChunkSeconds = 0.24;
    private const double StreamReverseOverlapSeconds = 0.04;
    /// <summary>Seek 直後のプライミング汚れを捨てるための手前デコード量。</summary>
    private const double StreamReversePrerollSeconds = 0.08;

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
            _bpmClicks.SetDeviceRate(_deviceRate);
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

    public bool MonitorMono
    {
        get
        {
            lock (_gate)
            {
                return _monitorMono;
            }
        }
    }

    public void SetMonitorMono(bool enabled)
    {
        lock (_gate)
        {
            _monitorMono = enabled;
        }
    }

    public bool MonitorDim
    {
        get
        {
            lock (_gate)
            {
                return _monitorDim;
            }
        }
    }

    public void SetMonitorDim(bool enabled)
    {
        lock (_gate)
        {
            _monitorDim = enabled;
        }
    }

    public double MonitorDimDbAmount
    {
        get
        {
            lock (_gate)
            {
                return _monitorDimDb;
            }
        }
    }

    public void SetMonitorDimDb(double db)
    {
        lock (_gate)
        {
            var next = ClampMonitorDimDb(db);
            if (Math.Abs(_monitorDimDb - next) < 1e-9)
            {
                return;
            }

            _monitorDimDb = next;
            _monitorDimLinear = DbToLinear(next);
        }
    }

    public bool MonitorLowCut
    {
        get
        {
            lock (_gate)
            {
                return _monitorLowCut;
            }
        }
    }

    public void SetMonitorLowCut(bool enabled)
    {
        lock (_gate)
        {
            if (_monitorLowCut == enabled)
            {
                return;
            }

            _monitorLowCut = enabled;
            ResetLowCutStateNoLock();
        }
    }

    public BpmMonitorMode BpmMonitorMode
    {
        get
        {
            lock (_gate)
            {
                return _bpmMonitorMode;
            }
        }
    }

    public void SetBpmMonitorMode(BpmMonitorMode mode)
    {
        lock (_gate)
        {
            if (_bpmMonitorMode == mode)
            {
                return;
            }

            _bpmMonitorMode = mode;
            if (mode == BpmMonitorMode.Off)
            {
                _bpmMonitor?.Reset();
                _bpmClicks.Reset();
                return;
            }

            _bpmMonitor ??= new EffeTuneBpmEstimator();
            _bpmClicks.Reset();
            _bpmClicks.EnsureSample();
            _bpmClicks.SetDeviceRate(_deviceRate);
        }
    }

    public bool TryGetBpmMonitorBpm(out float bpm)
    {
        lock (_gate)
        {
            if (_bpmMonitorMode == BpmMonitorMode.Off
                || _bpmMonitor?.Bpm is not { } value)
            {
                bpm = 0;
                return false;
            }

            bpm = value;
            return true;
        }
    }

    public void ResetBpmMonitor()
    {
        lock (_gate)
        {
            if (_bpmMonitor is null)
            {
                return;
            }

            _bpmMonitor.Reset();
            _bpmClicks.Reset();
        }
    }

    public double MonitorLowCutCutoffHz
    {
        get
        {
            lock (_gate)
            {
                return _monitorLowCutHz;
            }
        }
    }

    public void SetMonitorLowCutHz(double hz)
    {
        lock (_gate)
        {
            var next = ClampMonitorLowCutHz(hz);
            if (Math.Abs(_monitorLowCutHz - next) < 1e-9)
            {
                return;
            }

            _monitorLowCutHz = next;
            ResetLowCutStateNoLock();
        }
    }

    internal static double ClampMonitorDimDb(double db) =>
        Math.Clamp(db, MinMonitorDimDb, MaxMonitorDimDb);

    internal static bool TryParseMonitorDimDb(string? text, out double db)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0
            || (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            || parsed < MinMonitorDimDb
            || parsed > MaxMonitorDimDb
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed))
        {
            db = DefaultMonitorDimDb;
            return false;
        }

        db = parsed;
        return true;
    }

    internal static double ClampMonitorLowCutHz(double hz) =>
        Math.Clamp(hz, MinMonitorLowCutHz, MaxMonitorLowCutHz);

    internal static bool TryParseMonitorLowCutHz(string? text, out double hz)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0
            || (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out parsed))
            || parsed < MinMonitorLowCutHz
            || parsed > MaxMonitorLowCutHz
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed))
        {
            hz = DefaultMonitorLowCutHz;
            return false;
        }

        hz = parsed;
        return true;
    }

    private static float DbToLinear(double db) =>
        (float)Math.Pow(10, db / 20.0);

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
        ResetLowCutStateNoLock();
    }

    public bool IsBoundTo(AudioDocument document)
    {
        lock (_gate)
        {
            // ストリーム文書の Interleaved は Array.Empty なので、参照比較だけでは解放後も一致する。
            if (_boundDocument is not null || _stream is not null)
            {
                return ReferenceEquals(_boundDocument, document);
            }

            var samples = document.Interleaved;
            return samples.Length > 0 && ReferenceEquals(_samples, samples);
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

    /// <summary>無音ストリームの尺を映像の NaturalDuration に合わせる。</summary>
    public void GrowSilenceFrames(long frameCount)
    {
        lock (_gate)
        {
            if (_stream is not { IsSilence: true } || frameCount <= 0)
            {
                return;
            }

            _stream.GrowFrameCount(frameCount);
            var sampleCount = Math.Max(0, _stream.FrameCount) * (long)Math.Max(1, _channels);
            _usedSamples = sampleCount > int.MaxValue ? int.MaxValue : (int)sampleCount;
            if (!_loop)
            {
                _playEnd = _usedSamples;
            }

            var cursorSamples = _sourceFrame * Math.Max(1, _channels);
            if (cursorSamples < _playEnd)
            {
                Ended = false;
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
        Func<long, float>? frameGain = null,
        bool allowBeforePlayWindowStart = false)
    {
        lock (_gate)
        {
            var previous = _boundDocument;
            DisposeStreamNoLock();
            ClearGaplessNoLock();
            NoteGaplessAdvancedNoLock(null);
            _boundDocument = document;
            NotePlaybackDocumentNoLock(previous, document);
            _samples = document.Interleaved;
            _usedSamples = Math.Clamp(document.SampleCount, 0, _samples.Length);
            _channels = Math.Max(1, document.Channels);
            _sourceRate = Math.Max(1, document.SampleRate);
            ApplyOutputConfig();
            var window = Mp3Gapless.ResolveWindow(document, document.FrameCount, playRange);
            playRange = window;
            var start = Mp3Gapless.ClampStart(
                startFrame,
                window,
                document.FrameCount,
                allowBeforePlayWindowStart);
            _sourceFrame = start;
            _cursor = checked((int)start * _channels);
            _frameGain = frameGain;
            _loopFadePreview = null;
            _silenceOnly = false;
            _silentSkipIdleLoop = false;
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
        Func<long, float>? frameGain = null,
        bool allowBeforePlayWindowStart = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            var previous = _boundDocument;
            DisposeStreamNoLock();
            _stream = source;
            _boundDocument = document;
            NotePlaybackDocumentNoLock(previous, document);
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
            var window = Mp3Gapless.ResolveWindow(document, frames, playRange);
            playRange = window;
            var start = Mp3Gapless.ClampStart(startFrame, window, frames, allowBeforePlayWindowStart);
            source.SeekFrame(start, prebufferTimeoutMs: 200);
            _sourceFrame = start;
            _cursor = checked((int)start * _channels);
            InvalidateStreamRecentNoLock();
            _frameGain = frameGain;
            _loopFadePreview = null;
            _silenceOnly = false;
            _silentSkipIdleLoop = false;
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
            _loopFadePreview = null;
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
        InvalidateStreamRecentNoLock();
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
            sourceNeed += FormatConvert.ResampleMargin(_sourceRate, _deviceRate);
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
                _lastStreamScrubCaptureMs = 0;
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
            if (paused)
            {
                _bpmClicks.Reset();
            }
        }
    }

    public void SetSilentSkip(bool enabled, double thresholdDb)
    {
        lock (_gate)
        {
            _silentSkip = enabled;
            _silentSkipLinear = SilentSkip.LinearFromDb(thresholdDb);
            // しきい値やオン／オフが変わったら、全域無音判定をやり直す。
            _silentSkipIdleLoop = false;
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
            var enterReverse = _playbackSpeed > 0 && next < 0;
            _playbackSpeed = next;
            _shuttleOutputGain = Math.Abs(next) > 1.5 ? ShuttleGainLinear : 1f;
            _shuttlePrimed = false;
            Ended = false;
            CompleteSeekFadeNoLock();
            // 正方向の 1↔3 で窓を捨てると、リングは先読み分だけヘッドより前にいる。
            // 音声スレッドは巻き戻さないので、早送りの読みが失敗したままヘッドが止まる。
            if (leaveReverse || enterReverse)
            {
                InvalidateStreamSrcWinNoLock();
            }

            ClearStreamReverseBuf();
            if (leaveReverse)
            {
                ResumeStreamAfterReverseNoLock();
            }

            return true;
        }
    }

    public int PlaybackSemitones
    {
        get
        {
            lock (_gate)
            {
                return _playbackSemitones;
            }
        }
    }

    /// <summary>
    /// 再生中のピッチ（半音）。長さは変えず、次の出力バッファから聞こえる。
    /// ±<see cref="PitchShift.MaxSemitones"/>。0 でバイパス。
    /// </summary>
    /// <returns>値が変わったとき true。</returns>
    public bool SetPlaybackSemitones(int semitones)
    {
        semitones = PitchShift.Snap(semitones);
        lock (_gate)
        {
            if (_playbackSemitones == semitones)
            {
                return false;
            }

            _playbackSemitones = semitones;
            _pitch.SetSemitones(semitones);
            if (semitones == 0)
            {
                _pitch.ResetEngine();
            }

            return true;
        }
    }

    public void DisposePlaybackPitch()
    {
        lock (_gate)
        {
            _playbackSemitones = 0;
            _pitch.Dispose();
        }
    }

    /// <summary>別トラックへ切り替わったときだけ半音を忘れる。同じ文書の再バインドは残す。</summary>
    private void NotePlaybackDocumentNoLock(AudioDocument? previous, AudioDocument document)
    {
        if (ReferenceEquals(previous, document))
        {
            _pitch.ResetEngine();
            return;
        }

        ForgetPlaybackPitchNoLock();
    }

    private void ForgetPlaybackPitchNoLock()
    {
        _playbackSemitones = 0;
        _pitch.ClearSemitones();
    }

    public void CaptureScrub(AudioDocument document, long frame)
    {
        var kind = BeginStreamScrubCapture(
            frame,
            out var stream,
            out var buf,
            out var rate,
            out var channels,
            out var grainFrames,
            out var start);
        if (kind == StreamScrubCaptureBegin.MemoryDocument)
        {
            _scrub.Bind(document);
            _scrub.Capture(frame);
            return;
        }

        if (kind == StreamScrubCaptureBegin.Throttled)
        {
            return;
        }

        // _gate 外で待つ。音声スレッドの Read と食い合わないようにする。
        var got = stream!.ReadFrames(buf!, 0, grainFrames, timeoutMs: 40);
        if (got <= 0)
        {
            return;
        }

        if (got < grainFrames)
        {
            Array.Clear(buf!, got * channels, (grainFrames - got) * channels);
        }

        _scrub.CaptureInterleaved(buf!, channels, rate, grainFrames, start);
    }

    private enum StreamScrubCaptureBegin
    {
        MemoryDocument,
        Throttled,
        Ready,
    }

    /// <summary>ストリームスクラブ用に Seek まで済ませる。</summary>
    private StreamScrubCaptureBegin BeginStreamScrubCapture(
        long frame,
        out AudioStreamSource? stream,
        out float[]? buf,
        out int rate,
        out int channels,
        out int grainFrames,
        out long start)
    {
        lock (_gate)
        {
            stream = _stream;
            buf = null;
            rate = 0;
            channels = 0;
            grainFrames = 0;
            start = 0;
            if (stream is null)
            {
                return StreamScrubCaptureBegin.MemoryDocument;
            }

            rate = Math.Max(1, _sourceRate);
            channels = Math.Max(1, _channels);
            grainFrames = Math.Max(64, (int)Math.Round(MemoryScrubVoice.GrainSeconds * rate));
            var max = UsedFrameCountNoLock(channels);
            start = Math.Clamp(frame, 0, max);
            _sourceFrame = start;
            _cursor = checked((int)start * channels);
            var now = Environment.TickCount64;
            if (now - _lastStreamScrubCaptureMs < StreamScrubCaptureMinIntervalMs)
            {
                // ヘッドは合わせつつ、デコード待ちの連打は間引く。
                return StreamScrubCaptureBegin.Throttled;
            }

            InvalidateStreamCursorCacheNoLock();
            ClearStreamReverseBuf();
            Ended = false;
            var need = grainFrames * channels;
            if (_streamScrubBuf.Length < need)
            {
                _streamScrubBuf = new float[need];
            }

            buf = _streamScrubBuf;
            stream.SeekFrame(start, prebufferTimeoutMs: 0);
            _lastStreamScrubCaptureMs = now;
            return StreamScrubCaptureBegin.Ready;
        }
    }

    public void SetPlayWindow(WaveSelection? playRange, bool loop)
    {
        lock (_gate)
        {
            var frames = UsedFrameCountNoLock(Math.Max(1, _channels));
            ApplyPlayWindowNoLock(Mp3Gapless.ResolveWindow(_boundDocument, frames, playRange), loop);

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

    /// <summary>フェード調整ツールの非破壊プレビュー（ゲイン＋クロスフェード合成）。null で解除。</summary>
    public void SetLoopFadePreview(LoopFadePreview? preview)
    {
        lock (_gate)
        {
            _loopFadePreview = preview;
        }
    }

    public void SetFrameGain(Func<long, float>? frameGain)
    {
        lock (_gate)
        {
            _frameGain = frameGain;
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

}
