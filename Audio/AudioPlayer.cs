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

    public bool ProviderEnded =>
        _provider.Ended || _output is AsioOut { HasReachedEnd: true };

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

        var wasPlaying = _playing;
        _provider.Bind(document, startFrame, playRange, loop, frameGain);
        EnsureDeviceMatchesProvider();
        if (wasPlaying)
        {
            Play();
        }
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
            throw new InvalidOperationException("Audio output device is not available.");
        }

        if (!_playing)
        {
            _generation++;
            _output.Play();
            _playing = true;
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
            throw new InvalidOperationException("Audio output device is not available.");
        }

        _generation++;
        _output.Play();
        _playing = true;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_scrubbing)
        {
            EndScrub();
        }

        // 停止済みの出力に Pause すると、続く Play が無音のまま終わることがある。
        if (_playing)
        {
            _output?.Pause();
        }

        _playing = false;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _playing = false;
        _suppressPlaybackEnded = true;
        try
        {
            _output?.Stop();
        }
        catch
        {
            // ASIO Stop 失敗は破棄で回収する。
        }
        finally
        {
            _suppressPlaybackEnded = false;
        }
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
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            FlushOutputWithSilence();
        }
        finally
        {
            DisposeOutputOnly();
        }
    }

    /// <summary>
    /// 停止だけではドライバ先読みが残ることがあるため、無音を流してから破棄する。
    /// 洗い流し時間は出力レイテンシ／バッファ長に合わせる（短い固定値だと足りない）。
    /// </summary>
    private void FlushOutputWithSilence()
    {
        if (_output is null)
        {
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _scrubbing = false;
            _provider.BeginSilenceFlush();
            try
            {
                if (_output.PlaybackState != PlaybackState.Playing)
                {
                    _output.Play();
                }

                _playing = true;
            }
            catch
            {
                // 既に止まっている等は無視して破棄へ進む。
            }

            // 先読みに残った音が無音に置き換わるまで待つ（秒数は AudioOutputFlush）。
            var until = Environment.TickCount64 + EstimateFlushMilliseconds(_output);
            while (Environment.TickCount64 < until)
            {
                Thread.Sleep(15);
            }

            try
            {
                _output.Stop();
            }
            catch
            {
            }

            _playing = false;
        }
        catch
        {
            // 終了処理は失敗しても破棄を優先する。
        }
        finally
        {
            _suppressPlaybackEnded = false;
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
        throw new InvalidOperationException(
            "Could not read the ASIO driver sample rate before Init.");
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
            throw new InvalidOperationException("ASIO driver has no output channels.");
        }

        var rate = _provider.WaveFormat.SampleRate;
        if (!asio.IsSampleRateSupported(rate))
        {
            throw new InvalidOperationException(
                $"ASIO '{asio.DriverName}' does not support {rate} Hz.");
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
        if (_suppressPlaybackEnded)
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
