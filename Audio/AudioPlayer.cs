using MgaSonicAnvil.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

internal sealed class AudioPlayer : IDisposable
{
    private readonly PlaybackSampleProvider _provider = new();
    private IWavePlayer? _output;
    private AudioOutputSettings _settings = AudioOutputSettings.Default;
    private int _deviceRate;
    private int _deviceChannels;
    private int _lockedDeviceRate;
    private string? _clockLockKey;
    private bool _disposed;
    private bool _playing;
    private bool _scrubbing;
    private bool _suppressPlaybackEnded;
    private bool _discardQueuedOutput;
    private bool _asioEndArmed;
    private bool _playExitLayer;
    private int _generation;
    private long _smoothRawFrame = -1;
    private long _smoothShownFrame = -1;
    private long _smoothStampTicks;

    public event EventHandler<int>? PlaybackEnded;

    public event EventHandler<string>? Diagnostic;

    public bool IsPlaying => _playing;

    public bool IsScrubbing => _scrubbing;

    public bool HasOutputDevice => _output is not null;

    public long CursorFrame => _provider.CursorFrame;

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
            var raw = _provider.CursorFrame;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!_playing || _scrubbing)
            {
                _smoothRawFrame = raw;
                _smoothShownFrame = raw;
                _smoothStampTicks = now;
                return raw;
            }

            if (raw != _smoothRawFrame)
            {
                var jumpedBack = raw < _smoothRawFrame;
                _smoothRawFrame = raw;
                _smoothStampTicks = now;
                if (jumpedBack)
                {
                    // ループ折り返し・巻き戻しシークは追いかけず即座に反映する。
                    _smoothShownFrame = raw;
                    return raw;
                }
            }

            // 外挿は 1 バッファ相当（80ms）まで。出力停止時の暴走を防ぐ。
            var elapsedSec = Math.Min(
                0.08,
                (now - _smoothStampTicks) / (double)System.Diagnostics.Stopwatch.Frequency);
            var value = _smoothRawFrame + (long)(elapsedSec * _provider.SourceSampleRate);
            // 生カーソル更新の直後に外挿分だけ戻って見えないよう単調性を保つ。
            if (_smoothShownFrame >= 0 && value < _smoothShownFrame)
            {
                value = _smoothShownFrame;
            }

            _smoothShownFrame = value;
            return value;
        }
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

    public void CopyMeterWindow(float[] left, float[] right) =>
        _provider.CopyMeterWindow(left, right);

    public int ReadRecentOutputSamples(float[] destination) =>
        _provider.CopyRecentOutputSamples(destination);

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

    public int TakeLoudnessFrames(float[] left, float[] right) =>
        _provider.TakeLoudnessFrames(left, right);

    public void Prepare(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scrubbing)
        {
            EndScrub();
        }

        _provider.Bind(document, startFrame, playRange, loop, frameGain);
        EnsureDeviceMatchesProvider();
    }

    /// <summary>デバイスを捨てずに中身だけ差し替える。再生中の ASIO Stop を避ける。</summary>
    public void Rebind(
        AudioDocument document,
        long startFrame,
        WaveSelection? playRange,
        bool loop,
        Func<long, float>? frameGain = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_playing)
        {
            Pause();
        }

        _provider.Bind(document, startFrame, playRange, loop, frameGain);
    }

    public void Seek(long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider.SeekFrame(frame);
        if (!_playing)
        {
            _discardQueuedOutput = true;
        }
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

    /// <summary>Exit レイヤー再生中の現在フレーム。停止中は -1。</summary>
    public long ExitCursorFrame => _provider.ExitCursorFrame;

    public void BeginScrub(AudioDocument document, long frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureBound(document, frame);
        _provider.SetScrubbing(true);
        EnsureOutputDevice();
        if (_output is null)
        {
            throw new InvalidOperationException(UiStrings.ErrAudioOutputUnavailable);
        }

        if (!_playing)
        {
            StartOutput();
        }

        _scrubbing = true;
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
    /// WaveOut / WASAPI は Stop 後に先読みが残ることがあるので作り直す
    /// （IM Importer と同じ）。
    /// </summary>
    private void StartOutput()
    {
        if (_output is not AsioOut
            && (_discardQueuedOutput || _output!.PlaybackState != PlaybackState.Stopped))
        {
            RecreateOutputKeepingCursor();
        }

        _discardQueuedOutput = false;
        _generation++;
        _asioEndArmed = false;
        ResetSmoothCursor();
        _provider.SetPaused(false);
        if (_output is null)
        {
            InitOutputDevice();
        }

        if (_output!.PlaybackState != PlaybackState.Playing)
        {
            _output.Play();
        }

        _playing = true;
    }

    private void RecreateOutputKeepingCursor()
    {
        var frame = _provider.CursorFrame;
        RecreateOutput();
        InitOutputDevice();
        _provider.SeekFrame(frame);
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
                // デバイスは動かしたまま無音を流し、ドライバ／仮想ミキサの
                // 先読みを無音で置き換える（停止時に洗い流す）。
                _provider.SetPaused(true);
            }
            else
            {
                // WaveOut / WASAPI は即止める。残った先読みは次の Play で
                // デバイスごと作り直して捨てる。
                _suppressPlaybackEnded = true;
                try
                {
                    _output?.Stop();
                }
                catch
                {
                    // 次の Play で作り直す。
                }
                finally
                {
                    _suppressPlaybackEnded = false;
                }

                _discardQueuedOutput = true;
            }
        }

        _playing = false;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playing = false;

        // ASIO はデバイスを止めず無音ゲートで洗い流す（Pause と同じ）。
        // driver.Stop → Start は旧バッファの再生や HasReachedEnd の残留を招く。
        if (_output is AsioOut)
        {
            _provider.SetPaused(true);
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _output?.Stop();
        }
        catch
        {
            // 次の Play でデバイスを作り直す。
        }
        finally
        {
            _suppressPlaybackEnded = false;
        }

        _discardQueuedOutput = true;
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
        var waitMs = BeginSilenceFlushWait();
        if (waitMs > 0)
        {
            WaitFlush(waitMs);
        }
    }

    private async Task FlushOutputWithSilenceAsync()
    {
        var waitMs = BeginSilenceFlushWait();
        if (waitMs > 0)
        {
            await Task.Delay(waitMs).ConfigureAwait(true);
        }
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
            var muted = TryMuteOutput(_output);
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
            // ミュートできた WaveOut / WASAPI は適用待ちだけ。
            // ASIO とミュート失敗時は、後段 hop が無音に置き換わるまで待つ。
            return !muted || _output is AsioOut
                ? EstimateFlushMilliseconds(_output)
                : AudioOutputFlush.MutedSettleMilliseconds;
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
    /// 既にキューへ乗った音を直ちに消す。WasapiOut.Volume は端末マスターなので使わない。
    /// ASIO はセッション音量が効かないので false。
    /// </summary>
    private static bool TryMuteOutput(IWavePlayer output)
    {
        if (output is AsioOut)
        {
            TryMuteProcessSessions();
            return false;
        }

        var muted = false;
        try
        {
            if (output is WaveOutEvent wave)
            {
                wave.Volume = 0;
                muted = true;
            }
            else if (output is WasapiOut wasapi)
            {
                var stream = wasapi.AudioStreamVolume;
                stream.SetAllVolumes(new float[stream.ChannelCount]);
                muted = true;
            }
        }
        catch
        {
            // セッション側のミュートに任せる。
        }

        return TryMuteProcessSessions() || muted;
    }

    private static bool TryMuteProcessSessions()
    {
        var muted = false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    muted |= TryMuteDeviceSessions(device);
                }
            }
        }
        catch
        {
            // 終了時のミュート失敗は洗い流し待ちで回収する。
        }

        return muted;
    }

    private static bool TryMuteDeviceSessions(MMDevice device)
    {
        var muted = false;
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

                    session.SimpleAudioVolume.Mute = true;
                    session.SimpleAudioVolume.Volume = 0f;
                    muted = true;
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

        return muted;
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

        _provider.Bind(document, frame, null, loop: false);
        EnsureDeviceMatchesProvider();
    }

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

        var channels = Math.Min(2, asio.DriverOutputChannelCount);
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
            _suppressPlaybackEnded = false;
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // 一時停止の Stop（WaveOutEvent は Stop 後に非同期で届く）は
        // 終了イベントにしない。真の EOF は _playing 中にしか来ない。
        if (_suppressPlaybackEnded || !_playing)
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
