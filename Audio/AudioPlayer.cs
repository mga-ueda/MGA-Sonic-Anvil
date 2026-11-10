using System.IO;
using MgaSonicAnvil.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

internal sealed class AudioPlayer : IDisposable
{
    private readonly PlaybackSampleProvider _provider = new();
    private readonly VstInsertEngine _vstInserts = new();
    private IWavePlayer? _output;
    private AudioOutputSettings _settings = AudioOutputSettings.Default;
    private int[] _outputMap = [];
    private int[] _fileChannelMap = [];
    private int _speakerChannels;
    private int _deviceRate;
    private int _deviceChannels;
    private int _lockedDeviceRate;
    private string? _clockLockKey;
    private bool _disposed;
    private bool _playing;
    private bool _scrubbing;
    private bool _suppressPlaybackEnded;
    private bool _discardQueuedOutput;
    private bool _outputStarted;
    private bool _asioEndArmed;
    private bool _playExitLayer;
    private bool _shutdownFlushStarted;
    private int _shutdownFlushMs;
    private long _shutdownFlushStartedTicks;
    private int _generation;
    private long _smoothRawFrame = -1;
    private long _smoothShownFrame = -1;
    private long _smoothStampTicks;
    private double _smoothSpeed = 1;

    public AudioPlayer()
    {
        _provider.SetVstInsertEngine(_vstInserts);
    }

    public event EventHandler<int>? PlaybackEnded;

    public event EventHandler<string>? Diagnostic;

    public VstInsertEngine VstInserts => _vstInserts;

    public bool IsPlaying => _playing;

    public bool IsScrubbing => _scrubbing;

    /// <summary>
    /// 再生中スクラブの Stop→Play や一時停止の Stop は、遅延した
    /// PlaybackStopped を EOF にしない。
    /// </summary>
    internal static bool ShouldIgnorePlaybackStopped(bool suppress, bool playing, bool scrubbing) =>
        suppress || !playing || scrubbing;

    /// <summary>
    /// Pause/Stop 後にデバイスを作り直した場合、旧デバイスから遅れて届く
    /// PlaybackStopped は現行出力と一致しないので EOF にしない。
    /// </summary>
    internal static bool ShouldIgnoreStalePlaybackStopped(object? sender, object? currentOutput) =>
        !ReferenceEquals(sender, currentOutput);

    /// <summary>
    /// WaveOut / WASAPI は一度でも再生したデバイスを、次の再生前に作り直して捨てる。
    /// Pause / Stop / 曲末の自己停止でデバイスは実際に止まっており（<see cref="StopNonAsioOutput"/>）、
    /// 停止後や discardQueued 時、一度でも鳴らした後（outputStarted）は確実に清潔な
    /// デバイスを作り直してから鳴らす。これで前の曲の尾が次の曲の頭に混じらない。
    /// 初期化直後で未再生のデバイスはそのまま使う。
    /// ASIO は止めずにプロバイダの無音ゲートで洗い流すので作り直さない。
    /// </summary>
    internal static bool ShouldRecreateOutputBeforePlay(
        bool isAsio,
        bool discardQueued,
        bool stopped,
        bool outputStarted) =>
        !isAsio && (discardQueued || !stopped || outputStarted);

    public bool HasOutputDevice => _output is not null;

    public long CursorFrame => _provider.CursorFrame;

    /// <summary>スキップ連打中の到達位置。フェードアウト中のみ。</summary>
    public long? PendingSeekFrame => _provider.PendingSeekFrame;

    /// <summary>
    /// 描画専用の補間付き再生位置。CursorFrame はオーディオバッファ単位
    /// （ASIO で 10〜20ms 刻み）でしか進まず、60fps 描画と干渉してジャダーに
    /// 見えるため、最後の更新からの経過時間で外挿して滑らかにする。
    /// ロジック判定には CursorFrame を使うこと。
    /// </summary>
    public long SmoothCursorFrame
    {
        get
        {
            _provider.ReadPlaybackVisuals(
                out var raw, out _, out _, out var speed, out var sourceRate, out var playEnd);
            return OffsetPlayheadForPluginLatency(SmoothFrom(raw, speed, sourceRate, playEnd), sourceRate);
        }
    }

    public void ReadPlayheadVisuals(out long playhead, out long exitFrame)
    {
        _provider.ReadPlaybackVisuals(
            out var raw, out var fading, out exitFrame, out var speed, out var sourceRate, out var playEnd);
        playhead = fading
            ? WaveSelection.ClampVisualPlayhead(raw, playEnd)
            : SmoothFrom(raw, speed, sourceRate, playEnd);
        playhead = OffsetPlayheadForPluginLatency(playhead, sourceRate);
    }

    /// <summary>挿入プラグインの遅延分、描画カーソルを聞こえている位置へ戻す。</summary>
    private long OffsetPlayheadForPluginLatency(long frame, int sourceRate) =>
        VstTransport.AudibleSourceFrame(
            frame,
            _vstInserts.ActiveLatencyFrames,
            sourceRate,
            _provider.DeviceSampleRate,
            _playing && !_scrubbing);

    private long SmoothFrom(long raw, double speed, int sourceRate, long playEndFrame)
    {
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_playing || _scrubbing)
        {
            var shown = WaveSelection.ClampVisualPlayhead(raw, playEndFrame);
            _smoothRawFrame = raw;
            _smoothShownFrame = shown;
            _smoothStampTicks = now;
            _smoothSpeed = speed;
            return shown;
        }

        if (Math.Abs(speed - _smoothSpeed) > 1e-9)
        {
            // 早戻しへ切り替えた瞬間に、直前の外挿を逆方向へ伸ばさない。
            _smoothSpeed = speed;
            var shown = WaveSelection.ClampVisualPlayhead(raw, playEndFrame);
            _smoothRawFrame = raw;
            _smoothShownFrame = shown;
            _smoothStampTicks = now;
            return shown;
        }

        if (raw != _smoothRawFrame)
        {
            var delta = raw - _smoothRawFrame;
            var discontinuity = speed < 0 ? delta > 0 : delta < 0;
            var maxStep = (long)(sourceRate * 0.25 * Math.Max(1, Math.Abs(speed)));
            if (Math.Abs(delta) > maxStep)
            {
                discontinuity = true;
            }

            _smoothRawFrame = raw;
            _smoothStampTicks = now;
            if (discontinuity)
            {
                // ループ折り返し・大きなシークは追いかけず即座に反映する。
                var shown = WaveSelection.ClampVisualPlayhead(raw, playEndFrame);
                _smoothShownFrame = shown;
                return shown;
            }
        }

        // 外挿は曲の時間で 80ms まで。3 倍の早戻しで壁時計 80ms のまま伸ばすと、
        // シーク待ちのあいだバーだけ先に進んで音とずれたままになる。
        var maxWallSec = 0.08 / Math.Max(1d, Math.Abs(speed));
        var elapsedSec = Math.Min(
            maxWallSec,
            (now - _smoothStampTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
        var value = _smoothRawFrame
            + (long)(elapsedSec * sourceRate * speed);
        // 生カーソル更新の直後に外挿分だけ戻って見えないよう、進行方向の単調性を保つ。
        if (_smoothShownFrame >= 0)
        {
            if (speed < 0)
            {
                if (value > _smoothShownFrame)
                {
                    value = _smoothShownFrame;
                }
            }
            else if (value < _smoothShownFrame)
            {
                value = _smoothShownFrame;
            }
        }

        // 再生窓終端への外挿はみ出しを止め、次フレームの単調クランプにも残さない。
        value = WaveSelection.ClampVisualPlayhead(value, playEndFrame);
        _smoothShownFrame = value;
        return value;
    }

    public bool ProviderEnded
    {
        get
        {
            if (!_playing || _scrubbing)
            {
                return false;
            }

            if (_provider.Ended)
            {
                return true;
            }

            // ASIO は Stop 後も HasReachedEnd が残る。再生が一度動いてからだけ見る。
            if (_output is not AsioOut asio)
            {
                return false;
            }

            if (!asio.HasReachedEnd)
            {
                _asioEndArmed = true;
                return false;
            }

            return _asioEndArmed;
        }
    }

    public int Generation => _generation;

    public int OutputSampleRate => _provider.WaveFormat.SampleRate;

    public int SourceChannels => _provider.SourceChannels;

    public void CopyMeterWindow(float[] left, float[] right) =>
        _provider.CopyMeterWindow(left, right);

    public void CopyMeterPeaks(Span<float> peaks) =>
        _provider.CopyMeterPeaks(peaks);

    public void CopyMeterPlanar(float[] dest) =>
        _provider.CopyMeterPlanar(dest);

    public int ReadRecentOutputSamples(float[] destination) =>
        _provider.CopyRecentOutputSamples(destination);

    public void SetOutputMap(int[]? map) => _outputMap = map ?? [];

    public void SetFileChannelMap(int[]? map, int speakerChannels)
    {
        _fileChannelMap = map ?? [];
        _speakerChannels = speakerChannels < 1 ? 0 : speakerChannels;
    }

    public void SetSoloMask(int mask) => _provider.SetSoloMask(mask);

    /// <summary>再生中のフレームゲイン（音量カーブ試聴など）。null で解除。</summary>
    public void SetFrameGain(Func<long, float>? frameGain) => _provider.SetFrameGain(frameGain);

    public bool MonitorMono => _provider.MonitorMono;

    public void SetMonitorMono(bool enabled) => _provider.SetMonitorMono(enabled);

    public bool MonitorDim => _provider.MonitorDim;

    public void SetMonitorDim(bool enabled) => _provider.SetMonitorDim(enabled);

    public double MonitorDimDb => _provider.MonitorDimDbAmount;

    public void SetMonitorDimDb(double db) => _provider.SetMonitorDimDb(db);

    public bool MonitorLowCut => _provider.MonitorLowCut;

    public void SetMonitorLowCut(bool enabled) => _provider.SetMonitorLowCut(enabled);

    public double MonitorLowCutHz => _provider.MonitorLowCutCutoffHz;

    public void SetMonitorLowCutHz(double hz) => _provider.SetMonitorLowCutHz(hz);

    public BpmMonitorMode BpmMonitorMode => _provider.BpmMonitorMode;

    public void SetBpmMonitorMode(BpmMonitorMode mode) => _provider.SetBpmMonitorMode(mode);

    public bool TryGetBpmMonitorBpm(out float bpm) => _provider.TryGetBpmMonitorBpm(out bpm);

    public void ResetBpmMonitor() => _provider.ResetBpmMonitor();

    public void SetSilentSkip(bool enabled, double thresholdDb) =>
        _provider.SetSilentSkip(enabled, thresholdDb);

    public void SetRangeClickFrames(long[] frames, int groupSize = 0) =>
        _provider.SetRangeClickFrames(frames, groupSize);

    public double PlaybackSpeed => _provider.PlaybackSpeed;

    public int PlaybackSemitones => _provider.PlaybackSemitones;

    /// <summary>再生中のピッチを半音単位で切り替える。出力は止めない。</summary>
    /// <returns>値が変わったとき true。</returns>
    public bool SetPlaybackSemitones(int semitones) => _provider.SetPlaybackSemitones(semitones);

    public bool IsStreamBound => _provider.IsStreamBound;

    public bool IsBoundTo(AudioDocument document) => _provider.IsBoundTo(document);

    /// <summary>
    /// ストリーム再生とギャップレス先読みのファイルハンドルを閉じる。
    /// Pause だけでは掴んだままになるので、リストから外す／プレイヤー退出時に呼ぶ。
    /// </summary>
    public void ReleaseStreamSource()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.ReleaseStreamSource();
    }

    /// <summary>
    /// 再生速度。出力を止めずに切り替える。速度が変わったときだけ外挿の起点を更新する。
    /// </summary>
    public void SetPlaybackSpeed(double speed)
    {
        if (!_provider.SetPlaybackSpeed(speed))
        {
            return;
        }

        // 正→逆など符号が変わると SmoothFrom の単調クランプが古い表示位置に引き戻す。
        // Raw だけ更新して Shown を残すと、動画本再生（映像クロック）後の巻き戻しで
        // シークバーが軌跡の始点へ飛ぶ。
        ResetSmoothPlayhead(_provider.CursorFrame);
    }

    public void ReleaseOutput()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        RecreateOutput();
    }

    public void ApplyOutputSettings(AudioOutputSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _settings = settings;
        _clockLockKey = null;
        _lockedDeviceRate = 0;
        if (settings.Api != AudioOutputApi.Asio)
        {
            CaptureDeviceClock(output: null);
        }
        if (_output is null)
        {
            return;
        }

        var frame = _provider.CursorFrame;
        RecreateOutput();
        _provider.SeekFrame(frame);
    }

    public bool TakeMeterInterval(out float peakLeft, out float rmsLeft, out float peakRight, out float rmsRight) =>
        _provider.TakeMeterInterval(out peakLeft, out rmsLeft, out peakRight, out rmsRight);

    public bool TakeMeterInterval(Span<float> peaks, Span<float> rms, out int channels) =>
        _provider.TakeMeterInterval(peaks, rms, out channels);

    public int TakeLoudnessFrames(float[] left, float[] right) =>
        _provider.TakeLoudnessFrames(left, right);

    /// <summary>ループ再生で頭に戻ったとき true（一度取る）。</summary>
    public bool ConsumeLoopWrap() => _provider.ConsumeLoopWrap();

    public void Prepare(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null,
        bool preferStream = false,
        bool allowBeforePlayWindowStart = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scrubbing)
        {
            EndScrub();
        }

        BindPlaybackSource(
            document,
            startFrame,
            playRange,
            loop,
            frameGain,
            preferStream,
            allowBeforePlayWindowStart);
        ApplyPlaybackRouting();
        EnsureDeviceMatchesProvider();
    }

    public bool HasGaplessArmed => _provider.HasGaplessArmed;

    public bool HasGaplessAdvancePending => _provider.HasGaplessAdvancePending;

    /// <summary>次曲のストリームを再生中のデバイスへ予約する。成功時に所有権を移す。</summary>
    public bool TryArmGapless(AudioStreamSource source, AudioDocument document)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _provider.TryArmGapless(source, document);
    }

    public void ClearGaplessNext()
    {
        if (_disposed)
        {
            return;
        }

        _provider.ClearGaplessNext();
    }

    public bool TryTakeGaplessAdvance(out AudioDocument? document)
    {
        if (_disposed)
        {
            document = null;
            return false;
        }

        return _provider.TryTakeGaplessAdvance(out document);
    }

    /// <summary>デバイスを捨てずに中身だけ差し替える。再生中の ASIO Stop を避ける。</summary>
    public void Rebind(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null,
        bool preferStream = false,
        bool allowBeforePlayWindowStart = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playing)
        {
            Pause();
        }

        BindPlaybackSource(
            document,
            startFrame,
            playRange,
            loop,
            frameGain,
            preferStream,
            allowBeforePlayWindowStart);
    }

    /// <summary>
    /// プレイヤー（preferStream）の圧縮／動画はストリーム可変速。
    /// WAVE / AIFF はメモリ再生のピッチ据え置きグレイン（エディタと同じ早戻し）。
    /// エディタは preferStream を false にする。
    /// </summary>
    internal static bool ShouldBindPlaybackStream(AudioDocument document, bool preferStream)
    {
        if (!preferStream
            || document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path))
        {
            return false;
        }

        if (AudioCodec.IsUncompressedPcm(document.SourceKind) || AudioCodec.IsUncompressedPcm(path))
        {
            return false;
        }

        return document.IsStreamPlayback || AudioCodec.CanStreamPlay(path);
    }

    /// <summary>
    /// 動画で音声ストリームが無い／開けないときは無音で尺だけ進める（警告は出さない）。
    /// </summary>
    internal static AudioStreamSource OpenLibraryPlaybackSource(
        AudioDocument document,
        int prebufferTimeoutMs = 3000)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            return AudioStreamSource.Open(document.SourcePath!, prebufferTimeoutMs);
        }
        catch (Exception) when (LibraryPlaylistDocuments.IsVideo(document))
        {
            return AudioStreamSource.CreateSilence(
                Math.Max(1, document.SampleRate),
                Math.Max(1, document.Channels),
                document.BitsPerSample,
                Math.Max(1, document.FrameCount));
        }
    }

    /// <summary>無音動画の尺が MediaOpened で伸びたとき、再生中のストリームに合わせる。</summary>
    public void SyncSilenceLength(long frameCount)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.GrowSilenceFrames(frameCount);
    }

    private void BindPlaybackSource(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain,
        bool preferStream,
        bool allowBeforePlayWindowStart = false)
    {
        if (!ShouldBindPlaybackStream(document, preferStream))
        {
            _provider.Bind(
                document,
                startFrame,
                playRange,
                loop,
                frameGain,
                allowBeforePlayWindowStart);
            return;
        }

        AudioStreamSource stream;
        try
        {
            stream = OpenLibraryPlaybackSource(document);
        }
        catch
        {
            if (document.IsStreamPlayback)
            {
                throw;
            }

            _provider.Bind(
                document,
                startFrame,
                playRange,
                loop,
                frameGain,
                allowBeforePlayWindowStart);
            return;
        }

        try
        {
            _provider.BindStream(
                stream,
                document,
                startFrame,
                playRange,
                loop,
                frameGain,
                allowBeforePlayWindowStart);
            if (document.IsStreamPlayback || document.IsDeferredLoad)
            {
                // UI の尺は document、再生は stream。低レートなどでずれたら stream に揃える。
                if (document.SampleRate != stream.SampleRate
                    || document.Channels != stream.Channels
                    || document.FrameCount != stream.FrameCount)
                {
                    document.SyncStreamPlaybackMeta(
                        stream.SampleRate,
                        stream.Channels,
                        stream.BitsPerSample,
                        stream.FrameCount);
                }
            }
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Seek(long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SeekFrame(frame);
        var to = _provider.PendingSeekFrame ?? _provider.CursorFrame;
        ResetSmoothPlayhead(to);
        if (!_playing)
        {
            _discardQueuedOutput = true;
        }
    }

    public void SeekWithCrossfade(long frame, int fadeMilliseconds)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_playing || _scrubbing || fadeMilliseconds <= 0)
        {
            Seek(frame);
            return;
        }

        _provider.SeekFrameCrossfade(frame, fadeMilliseconds);
        ResetSmoothPlayhead(_provider.PendingSeekFrame ?? _provider.CursorFrame);
    }

    private void ResetSmoothPlayhead(long frame)
    {
        _smoothRawFrame = frame;
        _smoothShownFrame = frame;
        _smoothStampTicks = System.Diagnostics.Stopwatch.GetTimestamp();
    }

    public void SetPlayWindow(WaveSelection? playRange, bool loop)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SetPlayWindow(playRange, loop);
    }

    /// <summary>
    /// Play -E：ループ折り返しで -E 区間を二重再生するか。
    /// false にすると進行中の Exit も直ちに止める。
    /// </summary>
    public bool PlayExitLayer
    {
        get => _playExitLayer;
        set
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _playExitLayer = value;
            _provider.SetPlayExitLayer(value);
        }
    }

    /// <summary>再生ウィンドウ終端に続く -E 区間（ソースフレーム）を登録する。null で解除。</summary>
    public void SetExitSpan(WaveSelection? span)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SetExitSpan(span);
    }

    /// <summary>フェード調整の非破壊プレビュー。null で解除。</summary>
    public void SetLoopFadePreview(Editing.LoopFadePreview? preview)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SetLoopFadePreview(preview);
    }

    /// <summary>Exit レイヤー再生中の現在フレーム。停止中は -1。</summary>
    public long ExitCursorFrame => _provider.ExitCursorFrame;

    public void BeginScrub(AudioDocument document, long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // ストリーム（動画）もメモリと同じくグレインスクラブする。
        // 再生デコーダを進めたまま Seek 連打すると蓄積するので、出力はスクラブ声に切り替える。
        var refillQueued = _playing && !_scrubbing && _output is not AsioOut;
        EnsureBound(document, frame);
        _provider.SetScrubbing(true);
        _provider.CaptureScrub(document, frame);
        EnsureOutputDevice();
        if (_output is null)
        {
            throw new InvalidOperationException(UiStrings.ErrAudioOutputUnavailable);
        }

        // PlaybackStopped が遅れて来ても EOF 扱いにしない。
        _scrubbing = true;
        if (refillQueued)
        {
            RefillOutputForScrub();
        }
        else if (!_playing)
        {
            StartOutput();
        }
    }

    /// <summary>
    /// 再生中スクラブは、先読みに残った実音を捨ててからグレインを埋める。
    /// デバイスを作り直すと概要ドラッグが途切れ、止めないとトーンが残る。
    /// WaveOut は waveOutReset、WASAPI は AudioClient.Reset でキューを空にする。
    /// ASIO は Stop すると HasReachedEnd が残るので、短い残留は許容する。
    /// </summary>
    private void RefillOutputForScrub()
    {
        if (_output is null or AsioOut)
        {
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _output.Stop();
            _output.Play();
            _playing = true;
        }
        finally
        {
            _suppressPlaybackEnded = false;
        }
    }

    public void CaptureScrub(AudioDocument document, long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.CaptureScrub(document, frame);
    }

    public void EndScrub()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SetScrubbing(false);
        _scrubbing = false;
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scrubbing)
        {
            EndScrub();
        }

        EnsureOutputDevice();
        if (_output is null)
        {
            throw new InvalidOperationException(UiStrings.ErrAudioOutputUnavailable);
        }

        StartOutput();
    }

    /// <summary>
    /// Pause 後の Play は先読みキューを再開し、古い位置の音が先に出る。
    /// ASIO はデバイスを止めず、プロバイダの無音ゲートで先読みを洗い流して
    /// あるので、ゲートを開けるだけ（Stop すると HasReachedEnd が残り
    /// シークバーが即終了扱いになる）。
    /// WaveOut / WASAPI は Pause / Stop / 曲末で実際に止めているので、作り直し時に
    /// 捨てるのは停止済みのデバイス。曲末の自己停止後や discardQueued 時、一度でも
    /// 鳴らした後は作り直して、確実に清潔なデバイスから鳴らす。
    /// </summary>
    private void StartOutput()
    {
        var recreate = ShouldRecreateOutputBeforePlay(
            _output is AsioOut,
            _discardQueuedOutput,
            _output is null || _output.PlaybackState == PlaybackState.Stopped,
            _outputStarted);
        if (recreate)
        {
            RecreateOutputKeepingCursor();
        }

        _discardQueuedOutput = false;
        _generation++;
        _asioEndArmed = false;
        ResetSmoothCursor();
        _provider.SetPaused(false);
        // 終了洗い流し（BeginSilenceFlush）後に同じプロバイダで再生を始めても、
        // Read が無音を返し続けないよう、再生のたびに無音ゲートを必ず解除する。
        _provider.CancelSilenceFlush();
        if (_output is null)
        {
            InitOutputDevice();
        }

        // WaveOut.Volume は Vista+ で自プロセスの Windows セッション音量（永続）に
        // 等価。旧版の洗い流しや外部が 0 のまま残していると、作り直した新しい
        // WaveOut もそれを継承して完全無音になるため、再生直前に必ず 1 へ戻す。
        // WASAPI はストリーム音量（AudioStreamVolume＝ミキサ UI とは別系統）が旧版の
        // 洗い流しで 0 のまま残ると、ミキサは 100% でも完全無音になる。再生直前に 1 へ戻す。
        EnsureOutputFullVolume();

        if (_output!.PlaybackState != PlaybackState.Playing)
        {
            _output.Play();
        }

        _playing = true;
        // 意図的 Stop の抑止をここで解除。真の EOF はこれ以降だけ受け取る。
        _suppressPlaybackEnded = false;
        // 一度でも鳴らした WaveOut / WASAPI は、次の再生で作り直して先読みを捨てる。
        if (_output is not AsioOut)
        {
            _outputStarted = true;
        }

        // 非 ASIO はセッションが生きているこのタイミングで、毎回、旧版や外部が
        // 残したミュート・音量 0（Volume≈0 の壊れた状態）だけを戻す。健全な音量や
        // ユーザーが意図的に下げた音量は ShouldRestore 判定で触らない。
        if (_output is not AsioOut)
        {
            TryRestoreProcessSessions();
        }
    }

    /// <summary>
    /// 再生直前に出力の内部音量を戻す。
    /// WaveOut はインスタンス音量（＝Vista+ では Windows セッション音量）が実質 0 の
    /// ときだけ 1 へ戻す（ユーザーがミキサで意図的に下げた音量は触らない）。
    /// WASAPI はストリーム音量（AudioStreamVolume）を必ず 1 へ戻す。これはミキサ UI
    /// （ISimpleAudioVolume）とは別系統の内部ゲインで、旧版の洗い流しが全チャンネルを
    /// 0 のまま残すと、ミキサが 100% でも完全無音になる（本バグ）。既定が 1 の内部値
    /// なので毎回 1 に揃えてもユーザー設定は壊さない。ASIO は対象外。
    /// </summary>
    private void EnsureOutputFullVolume()
    {
        switch (_output)
        {
            case WaveOutEvent wave:
                EnsureWaveOutFullVolume(wave);
                break;
            case WasapiOut wasapi:
                EnsureWasapiStreamFullVolume(wasapi);
                break;
        }
    }

    private static void EnsureWaveOutFullVolume(WaveOutEvent wave)
    {
        try
        {
            if (!AudioSessionRestore.ShouldRestore(muted: false, volume: wave.Volume))
            {
                return;
            }

            wave.Volume = 1f;
        }
        catch
        {
            // 音量設定に失敗しても再生は続ける。
        }
    }

    private static void EnsureWasapiStreamFullVolume(WasapiOut wasapi)
    {
        try
        {
            var stream = wasapi.AudioStreamVolume;
            var channels = stream.ChannelCount;
            if (channels < 1)
            {
                return;
            }

            var volumes = new float[channels];
            Array.Fill(volumes, 1f);
            stream.SetAllVolumes(volumes);
        }
        catch
        {
            // ストリーム音量にアクセスできなくても再生は続ける。
        }
    }

    private void RecreateOutputKeepingCursor()
    {
        var frame = _provider.CursorFrame;
        RecreateOutput();
        // Init の前にプロバイダをシークしておく。InitOutputDevice → Init や
        // 直後の初回 Play が旧位置のフレームを拾わないようにする。
        _provider.SeekFrame(frame);
        InitOutputDevice();
    }

    private void ResetSmoothCursor()
    {
        _smoothRawFrame = -1;
        _smoothShownFrame = -1;
        _smoothStampTicks = 0;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scrubbing)
        {
            EndScrub();
        }

        if (_playing)
        {
            if (_output is AsioOut)
            {
                // ASIO はデバイスを止めず、無音ゲートで先読みを洗い流す。
                // Stop すると HasReachedEnd が残り、シークバーが即終了扱いになるため。
                _provider.SetPaused(true);
            }
            else
            {
                // WaveOut / WASAPI は即 Stop。Playing のまま Dispose すると提出済みバッファが
                // 止まらず「停止なのに後からもう一個」聞こえる。破棄は次の Play の recreate に任せる。
                // Stop 後は無音ゲートを立て、Halt の Seek(0) で遅延 Read があっても頭を流さない。
                StopNonAsioOutput();
                _provider.BeginSilenceFlush();
            }
        }

        _provider.SetPlaybackSpeed(1);
        _playing = false;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playing = false;
        _provider.SetPlaybackSpeed(1);

        if (_output is AsioOut)
        {
            // ASIO はデバイスを止めず無音ゲートで洗い流す（Pause と同じ）。
            // driver.Stop → Start は旧バッファの再生や HasReachedEnd の残留を招く。
            _provider.SetPaused(true);
            return;
        }

        StopNonAsioOutput();
        _provider.BeginSilenceFlush();
    }

    /// <summary>
    /// WaveOut / WASAPI を止める。遅れて届く PlaybackStopped を EOF 扱いにしないよう
    /// 抑止し、次の Play でデバイスを作り直すため <see cref="_discardQueuedOutput"/> を立てる。
    /// 抑止は <see cref="StartOutput"/> まで維持する（Stop 直後に解除すると、遅延
    /// PlaybackStopped が次の再生開始後に届いて AutoLoop 二重再生になる）。
    /// WaveOut.Volume / AudioStreamVolume / セッション音量には一切触らない。
    /// </summary>
    private void StopNonAsioOutput()
    {
        _suppressPlaybackEnded = true;
        try
        {
            if (_output is not null)
            {
                try
                {
                    _output.Stop();
                }
                catch
                {
                    // 次の Play でデバイスを作り直す。
                }
            }
        }
        catch
        {
            // 次の Play でデバイスを作り直す。
        }

        _discardQueuedOutput = true;
        _provider.NotifyVstTransportStopped();
    }

    public void EnsureOutputDevice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_output is not null)
        {
            return;
        }

        InitOutputDevice();
    }

    /// <summary>
    /// 終了シーケンスの最初に実音を止める。セッション WAV 書き出しより先に呼ぶ。
    /// ASIO は無音で先読みを洗い流し、WaveOut / WASAPI はデバイスごと捨てる。
    /// </summary>
    public void BeginShutdownFlush()
    {
        if (_disposed || _shutdownFlushStarted)
        {
            return;
        }

        _shutdownFlushStarted = true;
        _playing = false;
        _scrubbing = false;
        if (_output is AsioOut)
        {
            _shutdownFlushMs = BeginSilenceFlushWait();
            _shutdownFlushStartedTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            return;
        }

        if (_output is null)
        {
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            // WaveOut / WASAPI は即座に止めて破棄する。デバイスを直後に捨てるので、
            // ここで _provider.BeginSilenceFlush() を呼んでも無音は 1 バッファも流れず
            // 意味がない（旧実装のなごり＝冗長）。むしろ _silenceOnly=true を残す副作用が
            // あったため外した。WaveOut.Volume / AudioStreamVolume による終了ミュートも
            // 使わない（Vista+ で永続化され、0 のまま残すと次回起動から無音になる本バグ）。
            // ASIO の VoiceMeeter 尾洗い流し（上の BeginSilenceFlushWait 経由で
            // デバイスを動かしたまま待つ）はここには来ないので影響しない。
            FlushStopOutput();
        }
        finally
        {
            DisposeOutputOnly();
        }
    }

    public void Dispose()
    {
        if (BeginDispose())
        {
            return;
        }

        try
        {
            FlushOutputWithSilence();
        }
        finally
        {
            EndDispose();
        }
    }

    /// <summary>
    /// 洗い流し待ちを UI に載せない。Stop / Dispose は呼び出し側の同期コンテキスト（UI）へ戻す。
    /// </summary>
    public async Task DisposeAsync()
    {
        if (BeginDispose())
        {
            return;
        }

        try
        {
            await FlushOutputWithSilenceAsync().ConfigureAwait(true);
        }
        finally
        {
            EndDispose();
        }
    }

    private bool BeginDispose()
    {
        if (_disposed)
        {
            return true;
        }

        _disposed = true;
        return false;
    }

    private void EndDispose()
    {
        try
        {
            FlushStopOutput();
            _provider.ClearGaplessNext();
            _provider.DisposePlaybackPitch();
            _provider.SetVstInsertEngine(null);
            _vstInserts.Dispose();
        }
        finally
        {
            DisposeOutputOnly();
        }
    }

    /// <summary>
    /// 停止だけではドライバ先読みが残ることがあるため、無音を流してから破棄する。
    /// WaveOut / WASAPI は停止後の Play が旧キューを再再生するので回さない。
    /// </summary>
    private void FlushOutputWithSilence()
    {
        var waitMs = RemainingOrStartFlushWait();
        if (waitMs > 0)
        {
            WaitFlush(waitMs);
        }
    }

    private async Task FlushOutputWithSilenceAsync()
    {
        var waitMs = RemainingOrStartFlushWait();
        if (waitMs > 0)
        {
            await Task.Delay(waitMs).ConfigureAwait(true);
        }
    }

    private int RemainingOrStartFlushWait()
    {
        if (_shutdownFlushStarted)
        {
            return AudioOutputFlush.RemainingMilliseconds(
                _shutdownFlushMs,
                _shutdownFlushStartedTicks,
                System.Diagnostics.Stopwatch.GetTimestamp());
        }

        return BeginSilenceFlushWait();
    }

    /// <returns>先読みを無音で置き換える待ちミリ秒。0 なら待たない。</returns>
    private int BeginSilenceFlushWait()
    {
        if (_output is null)
        {
            return 0;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _scrubbing = false;
            // インスタンス音量でのミュートは使わない（WaveOut.Volume は Vista+ で
            // 永続化される Windows セッション音量に等価で、0 のまま残すと次回起動から
            // 無音になる＝本バグ）。プロバイダの無音ゲートだけで先読みを置き換える。
            _provider.BeginSilenceFlush();

            var playing = false;
            try
            {
                playing = _output.PlaybackState == PlaybackState.Playing;
            }
            catch
            {
                return 0;
            }

            if (!playing)
            {
                return 0;
            }

            _playing = true;
            // 後段 hop（ドライバ／ミキサ）が無音ゲートの無音に置き換わるまで待つ。
            return EstimateFlushMilliseconds(_output);
        }
        catch
        {
            return 0;
        }
    }

    private void FlushStopOutput()
    {
        try
        {
            _output?.Stop();
        }
        catch
        {
        }

        _playing = false;
        _suppressPlaybackEnded = false;
    }

    private static void WaitFlush(int milliseconds)
    {
        var until = Environment.TickCount64 + Math.Max(0, milliseconds);
        while (Environment.TickCount64 < until)
        {
            Thread.Sleep(15);
        }
    }

    /// <summary>
    /// 旧版の終了洗い流しが自プロセスの Windows セッションを Mute=true / Volume=0 の
    /// まま残すと、次回起動でミキサがミュート・音量 0 のまま音が出なくなる。
    /// 出力デバイスを作って再生し始めるたびに、壊れた状態のときだけ戻す。
    /// ユーザーが意図的に下げた音量は <see cref="AudioSessionRestore.ShouldRestore"/>
    /// で判定して触らない。
    /// </summary>
    private static void TryRestoreProcessSessions()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    TryRestoreDeviceSessions(device);
                }
            }
        }
        catch
        {
            // デバイス列挙に失敗しても再生自体は続ける。
        }
    }

    private static void TryRestoreDeviceSessions(MMDevice device)
    {
        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            var pid = (uint)Environment.ProcessId;
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                try
                {
                    if (session.GetProcessID != pid)
                    {
                        continue;
                    }

                    var volume = session.SimpleAudioVolume;
                    // 旧洗い流しは Mute=true かつ Volume=0。Volume≈0 のときだけ戻す。
                    if (!AudioSessionRestore.ShouldRestore(volume.Mute, volume.Volume))
                    {
                        continue;
                    }

                    volume.Mute = false;
                    volume.Volume = 1f;
                }
                catch
                {
                    // 切れたセッションは飛ばす。
                }
            }
        }
        catch
        {
            // デバイスによってはセッション列挙が失敗する。
        }
    }

    private int EstimateFlushMilliseconds(IWavePlayer output)
    {
        var sampleRate = Math.Max(1, _provider.WaveFormat.SampleRate);
        return output switch
        {
            WaveOutEvent wave => AudioOutputFlush.EstimateMilliseconds(
                AudioOutputApi.WaveOut,
                sampleRate,
                waveDesiredLatencyMs: wave.DesiredLatency,
                waveBufferCount: wave.NumberOfBuffers),
            WasapiOut => AudioOutputFlush.EstimateMilliseconds(AudioOutputApi.Wasapi, sampleRate),
            AsioOut asio => AudioOutputFlush.EstimateMilliseconds(
                AudioOutputApi.Asio,
                sampleRate,
                framesPerBuffer: TryAsioFramesPerBuffer(asio),
                playbackLatencySamples: TryAsioPlaybackLatency(asio)),
            _ => AudioOutputFlush.EstimateMilliseconds(AudioOutputApi.Wasapi, sampleRate),
        };
    }

    private static int TryAsioFramesPerBuffer(AsioOut asio)
    {
        try
        {
            return Math.Max(1, asio.FramesPerBuffer);
        }
        catch
        {
            return 0;
        }
    }

    private static int TryAsioPlaybackLatency(AsioOut asio)
    {
        try
        {
            return Math.Max(0, asio.PlaybackLatency);
        }
        catch
        {
            return 0;
        }
    }

    private void EnsureBound(AudioDocument document, long frame)
    {
        if (_provider.IsBoundTo(document))
        {
            _provider.SeekFrame(frame);
            return;
        }

        if (ShouldBindPlaybackStream(
            document,
            preferStream: document.IsStreamPlayback || _provider.IsStreamBound))
        {
            var stream = OpenLibraryPlaybackSource(document);
            try
            {
                _provider.BindStream(stream, document, frame, null, loop: false);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        else
        {
            _provider.Bind(document, frame, null, loop: false);
        }

        ApplyPlaybackRouting();
        EnsureDeviceMatchesProvider();
    }

    private void ApplyPlaybackRouting()
    {
        if (_settings.Api == AudioOutputApi.Asio)
        {
            return;
        }

        var dest = AudioCaptureFactory.QueryOutputChannelCount(_settings);
        ConfigureProviderOutput(dest < 1 ? 2 : dest);
    }

    private void ConfigureProviderOutput(int deviceChannels) =>
        _provider.ConfigureOutput(deviceChannels, _outputMap, _fileChannelMap, _speakerChannels);

    private void EnsureDeviceMatchesProvider()
    {
        var rate = _provider.WaveFormat.SampleRate;
        var channels = _provider.WaveFormat.Channels;
        if (_output is not null && _deviceRate == rate && _deviceChannels == channels)
        {
            return;
        }

        RecreateOutput();
        InitOutputDevice();
    }

    private void RecreateOutput()
    {
        _playing = false;
        DisposeOutputOnly();
    }

    private void InitOutputDevice()
    {
        if (_settings.Api == AudioOutputApi.Asio && SynchronizationContext.Current is null)
        {
            Diagnostic?.Invoke(this, "ASIO requires the UI thread.");
            return;
        }

        try
        {
            _output = AudioOutputFactory.Create(_settings, out var fallback);
            if (!string.IsNullOrEmpty(fallback))
            {
                Diagnostic?.Invoke(this, fallback);
            }

            CaptureDeviceClock(_output);
            EnsureClockLockedForInit(_output);
            InitWaveProvider(_output);
        }
        catch (Exception ex)
        {
            DisposeOutputOnly();
            Diagnostic?.Invoke(this, $"Output init failed: {ex.Message}; falling back to WaveOut default.");
            try
            {
                _output = AudioOutputFactory.Create(AudioOutputSettings.Default, out _);
                CaptureDeviceClock(_output);
                EnsureClockLockedForInit(_output);
                InitWaveProvider(_output);
            }
            catch (Exception fallbackEx)
            {
                DisposeOutputOnly();
                throw new InvalidOperationException(fallbackEx.Message, fallbackEx);
            }
        }

        _output.PlaybackStopped += OnPlaybackStopped;
        _deviceRate = _provider.WaveFormat.SampleRate;
        _deviceChannels = _provider.WaveFormat.Channels;
    }

    /// <summary>
    /// 起動時（または出力デバイス変更時）のカードクロックを固定する。
    /// 以降の再生はこのレートへリアルタイム変換し、ドライバの SetSampleRate は呼ばない。
    /// </summary>
    private void CaptureDeviceClock(IWavePlayer? output)
    {
        var settings = ClockSettingsFor(output);
        var key = settings.Api + "\u001f" + (settings.DeviceId ?? string.Empty);
        if (_clockLockKey == key && _lockedDeviceRate >= 1000)
        {
            _provider.SetDeviceSampleRate(_lockedDeviceRate);
            return;
        }

        var live = output is null ? 0 : AudioOutputFactory.ReadLiveSampleRate(output);
        var queried = live >= 1000 ? 0 : AudioOutputFactory.QueryCurrentSampleRate(settings);
        var rate = live >= 1000 ? live : queried;
        if (rate < 1000)
        {
            if (_lockedDeviceRate >= 1000)
            {
                _provider.SetDeviceSampleRate(_lockedDeviceRate);
            }

            return;
        }

        _clockLockKey = key;
        _lockedDeviceRate = rate;
        _provider.SetDeviceSampleRate(rate);
    }

    private AudioOutputSettings ClockSettingsFor(IWavePlayer? output) =>
        output switch
        {
            AsioOut => _settings.Api == AudioOutputApi.Asio
                ? _settings
                : new AudioOutputSettings(AudioOutputApi.Asio, string.Empty),
            WasapiOut => _settings.Api == AudioOutputApi.Wasapi
                ? _settings
                : new AudioOutputSettings(AudioOutputApi.Wasapi, string.Empty),
            WaveOutEvent => _settings.Api == AudioOutputApi.WaveOut
                ? _settings
                : AudioOutputSettings.Default,
            _ => _settings,
        };

    private void EnsureClockLockedForInit(IWavePlayer output)
    {
        if (_lockedDeviceRate >= 1000 || output is not AsioOut)
        {
            return;
        }

        // Init 前に現在レートへ合わせないと NAudio が SetSampleRate する。
        throw new InvalidOperationException(UiStrings.ErrAsioSampleRateBeforeInit);
    }

    private void InitWaveProvider(IWavePlayer output)
    {
        if (output is AsioOut asio)
        {
            InitAsio(asio);
            return;
        }

        output.Init(CreateWaveProvider());
    }

    private void InitAsio(AsioOut asio)
    {
        if (asio.DriverOutputChannelCount < 1)
        {
            throw new InvalidOperationException(UiStrings.ErrAsioNoOutputChannels);
        }

        var rate = _provider.WaveFormat.SampleRate;
        if (!asio.IsSampleRateSupported(rate))
        {
            throw new InvalidOperationException(
                UiStrings.ErrAsioSampleRateUnsupported(asio.DriverName, rate));
        }

        var driverCh = Math.Max(1, asio.DriverOutputChannelCount);
        ConfigureProviderOutput(driverCh);
        var channels = Math.Clamp(_provider.WaveFormat.Channels, 1, driverCh);
        asio.Init(new AsioOutputAdapter(_provider, channels));
    }

    private IWaveProvider CreateWaveProvider()
    {
        // WaveOut は IEEE float を黙って無音にすることがある。16-bit PCM に落とす。
        if (_output is WaveOutEvent || _settings.Api == AudioOutputApi.WaveOut)
        {
            return new SampleToWaveProvider16(_provider);
        }

        return new SampleToWaveProvider(_provider);
    }

    private void DisposeOutputOnly()
    {
        _playing = false;
        if (_output is null)
        {
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            // 止めて破棄するだけ。残った先読みは次の Play でデバイスを作り直して捨てる。
            // インスタンス音量（WaveOut.Volume）は Vista+ で永続化される Windows
            // セッション音量に等価なので、破棄前に 0 へ落とすと次回以降も無音のまま
            // 残る（本バグ）。絶対に触らない。
            _output.Stop();
            _output.Dispose();
        }
        catch
        {
            // デバイス破棄失敗は無視。
        }
        finally
        {
            _output = null;
            _deviceRate = 0;
            _deviceChannels = 0;
            _outputStarted = false;
            _suppressPlaybackEnded = false;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 一時停止の Stop（WaveOutEvent は Stop 後に非同期で届く）は
        // 終了イベントにしない。真の EOF は _playing 中にしか来ない。
        // 再生中スクラブの refill（Stop→Play）も同様。
        // Pause/Stop → 作り直し後に旧デバイスから遅延した Stop も捨てる
        // （捨てないと AutoLoop が二重再生＝頭のジャジャーンになる）。
        if (ShouldIgnoreStalePlaybackStopped(sender, _output)
            || ShouldIgnorePlaybackStopped(_suppressPlaybackEnded, _playing, _scrubbing))
        {
            return;
        }

        _playing = false;
        if (e.Exception is not null)
        {
            Diagnostic?.Invoke(this, e.Exception.Message);
        }

        PlaybackEnded?.Invoke(this, _generation);
    }
}
