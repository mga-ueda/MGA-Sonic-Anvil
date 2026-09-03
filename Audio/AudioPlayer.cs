using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

internal sealed class AudioPlayer : IDisposable
{
    private readonly PlaybackSampleProvider _provider = new();
    private IWavePlayer? _output;
    private AudioOutputSettings _settings = AudioOutputSettings.Default;
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
        RecreateOutput();
        _provider.Bind(document, startFrame, playRange, loop, frameGain);
        InitOutputDevice();
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
        DisposeOutputOnly();
    }

    private void EnsureBound(AudioDocument document, long frame)
    {
        if (_provider.IsBoundTo(document))
        {
            _provider.SeekFrame(frame);
            return;
        }

        if (_output is null)
        {
            RecreateOutput();
            _provider.Bind(document, frame, null, loop: false);
            InitOutputDevice();
            return;
        }

        _provider.Bind(document, frame, null, loop: false);
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

            InitWaveProvider(_output);
        }
        catch (Exception ex)
        {
            DisposeOutputOnly();
            Diagnostic?.Invoke(this, $"Output init failed: {ex.Message}; falling back to WaveOut default.");
            try
            {
                _output = AudioOutputFactory.Create(AudioOutputSettings.Default, out _);
                InitWaveProvider(_output);
            }
            catch (Exception fallbackEx)
            {
                DisposeOutputOnly();
                throw new InvalidOperationException(fallbackEx.Message, fallbackEx);
            }
        }

        _output.PlaybackStopped += OnPlaybackStopped;
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
