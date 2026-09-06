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
    private int _generation;

    public event EventHandler<int>? PlaybackEnded;

    public event EventHandler<string>? Diagnostic;

    public bool IsPlaying => _playing;

    public bool IsScrubbing => _scrubbing;

    public bool HasOutputDevice => _output is not null;

    public long CursorFrame => _provider.CursorFrame;

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

        _output?.Pause();
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
                _output.Play();
                _playing = true;
            }
            catch
            {
                // 既に止まっている等は無視して破棄へ進む。
            }

            Thread.Sleep(EstimateFlushMilliseconds(_output));
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
    }

    private int EstimateFlushMilliseconds(IWavePlayer output)
    {
        // 再生中バッファ＋キュー分を見込む。上限は終了待ちの体感用。
        const int minMs = 200;
        const int maxMs = 1500;
        var sampleRate = Math.Max(1, _provider.WaveFormat.SampleRate);
        var ms = output switch
        {
            WaveOutEvent wave =>
                Math.Max(1, wave.DesiredLatency) * Math.Max(1, wave.NumberOfBuffers),
            WasapiOut =>
                AudioOutputFactory.WasapiLatencyMs * 3,
            AsioOut asio =>
                EstimateAsioFlushMilliseconds(asio, sampleRate),
            _ => 400,
        };

        // 実効遅延より少し長めに洗い流す。
        ms = (int)Math.Ceiling(ms * 1.25);
        return Math.Clamp(ms, minMs, maxMs);
    }

    private static int EstimateAsioFlushMilliseconds(AsioOut asio, int sampleRate)
    {
        try
        {
            var frames = Math.Max(1, asio.FramesPerBuffer);
            // ASIO は典型的にダブルバッファ。
            return (int)Math.Ceiling(frames * 2.0 * 1000.0 / Math.Max(1, sampleRate));
        }
        catch
        {
            return 400;
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
