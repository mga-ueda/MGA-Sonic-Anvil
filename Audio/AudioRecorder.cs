using System.Runtime.InteropServices;
using MgaSonicAnvil.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

internal sealed class AudioRecorder : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _capture;
    private float[] _samples = [];
    private int _count;
    private int[] _map = [];
    private int[] _fileMap = [];
    private int _sourceChannels = 1;
    private int _speakerChannels = 2;
    private int _destChannels = 2;
    private int _sampleRate = 48000;
    private bool _recording;
    private float[] _routeScratch = [];

    public bool IsRecording
    {
        get
        {
            lock (_gate)
            {
                return _recording;
            }
        }
    }

    public int SampleRate
    {
        get
        {
            lock (_gate)
            {
                return _sampleRate;
            }
        }
    }

    public int Channels
    {
        get
        {
            lock (_gate)
            {
                return _destChannels;
            }
        }
    }

    public int FrameCount
    {
        get
        {
            lock (_gate)
            {
                var channels = Math.Max(1, _destChannels);
                return _count / channels;
            }
        }
    }

    public void Start(
        AudioOutputSettings output,
        string? recordDeviceId,
        ChannelLayout layout,
        int[]? inputMap,
        int[]? fileChannelMap = null)
    {
        Stop();
        _speakerChannels = Math.Clamp(layout.Channels, 1, ChannelLayout.MaxChannels);
        _destChannels = ChannelRouter.DestLaneCount(_speakerChannels, fileChannelMap);
        _fileMap = ChannelRouter.Normalize(fileChannelMap, _speakerChannels, _destChannels);
        lock (_gate)
        {
            _samples = new float[_destChannels * _sampleRate];
            _count = 0;
            _recording = true;
        }

        try
        {
            switch (output.Api)
            {
                case AudioOutputApi.Wasapi:
                    StartWasapi(recordDeviceId, layout, inputMap, fileChannelMap);
                    break;
                case AudioOutputApi.Asio:
                    StartAsio(output.DeviceId, layout, inputMap, fileChannelMap);
                    break;
                default:
                    StartWaveIn(recordDeviceId, layout, inputMap, fileChannelMap);
                    break;
            }
        }
        catch
        {
            lock (_gate)
            {
                _recording = false;
            }

            DisposeCapture();
            throw;
        }
    }

    public float[] Snapshot()
    {
        lock (_gate)
        {
            var copy = new float[_count];
            Array.Copy(_samples, copy, _count);
            return copy;
        }
    }

    public float[] StopAndTake()
    {
        Stop();
        lock (_gate)
        {
            var copy = new float[_count];
            Array.Copy(_samples, copy, _count);
            return copy;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _recording = false;
        }

        DisposeCapture();
    }

    public void Dispose() => Stop();

    private void StartWaveIn(string? deviceId, ChannelLayout layout, int[]? inputMap, int[]? fileChannelMap)
    {
        var index = AudioCaptureFactory.ParseIndex(deviceId);
        if (WaveIn.DeviceCount < 1)
        {
            throw new InvalidOperationException(UiStrings.ErrorNoCaptureDevice);
        }

        if (index < 0 || index >= WaveIn.DeviceCount)
        {
            index = 0;
        }

        var caps = WaveIn.GetCapabilities(index);
        var channels = Math.Clamp(caps.Channels, 1, ChannelLayout.MaxChannels);
        var rate = 48000;
        var waveIn = new WaveInEvent
        {
            DeviceNumber = index,
            WaveFormat = new WaveFormat(rate, 16, channels),
            BufferMilliseconds = 50,
        };
        PrepareRoute(waveIn.WaveFormat.SampleRate, channels, layout, inputMap, fileChannelMap);
        waveIn.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, waveIn.WaveFormat);
        waveIn.StartRecording();
        _capture = waveIn;
    }

    private void StartWasapi(string? deviceId, ChannelLayout layout, int[]? inputMap, int[]? fileChannelMap)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        var capture = new WasapiCapture(device);
        PrepareRoute(capture.WaveFormat.SampleRate, capture.WaveFormat.Channels, layout, inputMap, fileChannelMap);
        capture.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, capture.WaveFormat);
        capture.StartRecording();
        _capture = new CaptureLease(capture, device);
    }

    private void StartAsio(string? driverName, ChannelLayout layout, int[]? inputMap, int[]? fileChannelMap)
    {
        var names = AsioDriver.GetAsioDriverNames();
        if (names.Length == 0)
        {
            throw new InvalidOperationException(UiStrings.ErrAsioNoDrivers);
        }

        var selected = string.IsNullOrWhiteSpace(driverName)
            ? names[0]
            : names.FirstOrDefault(name => name.Equals(driverName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(UiStrings.ErrAsioDriverNotFound(driverName));

        var asio = new AsioOut(selected) { AutoStop = false };
        var inputCount = Math.Max(1, asio.DriverInputChannelCount);
        var rate = 48000;
        try
        {
            var live = AudioOutputFactory.ReadLiveSampleRate(asio);
            if (live >= 1000)
            {
                rate = live;
            }
        }
        catch
        {
            // ドライバ既定のまま。
        }

        PrepareRoute(rate, inputCount, layout, inputMap, fileChannelMap);
        var silence = new SilenceSampleProvider(WaveFormat.CreateIeeeFloatWaveFormat(rate, Math.Min(2, Math.Max(1, asio.DriverOutputChannelCount))));
        asio.InitRecordAndPlayback(new SampleToWaveProvider(silence), inputCount, 0);
        asio.AudioAvailable += OnAsioAudio;
        asio.Play();
        _capture = asio;
    }

    private void OnAsioAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        var needed = e.SamplesPerBuffer * _sourceChannels;
        if (_routeScratch.Length < needed)
        {
            _routeScratch = new float[needed];
        }

        e.GetAsInterleavedSamples(_routeScratch);
        AppendRouted(_routeScratch.AsSpan(0, needed));
    }

    private void PrepareRoute(
        int sampleRate,
        int sourceChannels,
        ChannelLayout layout,
        int[]? inputMap,
        int[]? fileChannelMap)
    {
        _sampleRate = Math.Clamp(sampleRate, 1000, 384000);
        _sourceChannels = Math.Max(1, sourceChannels);
        _speakerChannels = Math.Clamp(layout.Channels, 1, ChannelLayout.MaxChannels);
        _destChannels = ChannelRouter.DestLaneCount(_speakerChannels, fileChannelMap);
        _map = ChannelRouter.Normalize(inputMap, _speakerChannels, _sourceChannels);
        _fileMap = ChannelRouter.Normalize(fileChannelMap, _speakerChannels, _destChannels);
        lock (_gate)
        {
            _samples = new float[Math.Max(_destChannels * _sampleRate, _destChannels)];
            _count = 0;
        }
    }

    private void AppendPcm(byte[] buffer, int bytes, WaveFormat format)
    {
        if (bytes <= 0 || format.BlockAlign <= 0)
        {
            return;
        }

        var frames = bytes / format.BlockAlign;
        var floats = frames * format.Channels;
        if (_routeScratch.Length < floats)
        {
            _routeScratch = new float[floats];
        }

        CapturePcm.ToFloat(buffer.AsSpan(0, bytes), format, _routeScratch.AsSpan(0, floats));
        AppendRouted(_routeScratch.AsSpan(0, floats));
    }

    private void AppendRouted(ReadOnlySpan<float> interleaved)
    {
        var srcCh = Math.Max(1, _sourceChannels);
        var destCh = Math.Max(1, _destChannels);
        var frames = interleaved.Length / srcCh;
        if (frames <= 0)
        {
            return;
        }

        Span<float> speakers = stackalloc float[_speakerChannels];
        Span<float> dest = stackalloc float[destCh];
        lock (_gate)
        {
            if (!_recording)
            {
                return;
            }

            EnsureCapacity(frames * destCh);
            for (var frame = 0; frame < frames; frame++)
            {
                ChannelRouter.Gather(interleaved.Slice(frame * srcCh, srcCh), speakers, _map);
                ChannelRouter.Scatter(speakers, dest, _fileMap);
                dest.CopyTo(_samples.AsSpan(_count, destCh));
                _count += destCh;
            }
        }
    }

    private void EnsureCapacity(int add)
    {
        var need = _count + add;
        if (need <= _samples.Length)
        {
            return;
        }

        var next = Math.Max(_samples.Length * 2, need);
        Array.Resize(ref _samples, next);
    }

    private void DisposeCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null)
        {
            return;
        }

        try
        {
            switch (capture)
            {
                case WaveInEvent wave:
                    wave.StopRecording();
                    wave.Dispose();
                    break;
                case CaptureLease lease:
                    lease.Dispose();
                    break;
                case AsioOut asio:
                    asio.AudioAvailable -= OnAsioAudio;
                    asio.Stop();
                    asio.Dispose();
                    break;
                default:
                    capture.Dispose();
                    break;
            }
        }
        catch
        {
            // デバイス切断後の停止失敗は無視。
        }
    }

    private sealed class CaptureLease(WasapiCapture capture, MMDevice device) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                capture.StopRecording();
            }
            catch
            {
                // 無視。
            }

            capture.Dispose();
            device.Dispose();
        }
    }

    private sealed class SilenceSampleProvider(WaveFormat format) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = format;

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}

internal static class CapturePcm
{
    public static void ToFloat(ReadOnlySpan<byte> source, WaveFormat format, Span<float> dest)
    {
        var channels = Math.Max(1, format.Channels);
        var frames = dest.Length / channels;
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var bytes = MemoryMarshal.AsBytes(dest);
            source[..Math.Min(source.Length, dest.Length * 4)].CopyTo(bytes);
            return;
        }

        if (format.BitsPerSample == 16)
        {
            var shorts = MemoryMarshal.Cast<byte, short>(source);
            var n = Math.Min(dest.Length, shorts.Length);
            for (var i = 0; i < n; i++)
            {
                dest[i] = shorts[i] / 32768f;
            }

            return;
        }

        if (format.BitsPerSample == 24)
        {
            for (var i = 0; i < frames * channels && (i * 3) + 2 < source.Length; i++)
            {
                var at = i * 3;
                var value = source[at] | (source[at + 1] << 8) | (source[at + 2] << 16);
                if ((value & 0x800000) != 0)
                {
                    value |= unchecked((int)0xFF000000);
                }

                dest[i] = value / 8388608f;
            }

            return;
        }

        if (format.BitsPerSample == 32)
        {
            var ints = MemoryMarshal.Cast<byte, int>(source);
            var n = Math.Min(dest.Length, ints.Length);
            for (var i = 0; i < n; i++)
            {
                dest[i] = ints[i] / 2147483648f;
            }
        }
    }
}
