using MgaSonicAnvil.Domain;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

/// <summary>設定画面用。入力ピーク監視と、再生ポートごとの Sine / Voice 試聴。</summary>
internal sealed class SettingsIoProbe : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _capture;
    private IWavePlayer? _output;
    private SettingsToneProvider? _tone;
    private float[] _peaks = [];
    private int[] _inputMap = [];
    private int[] _outputMap = [];
    private ChannelLayout _recordLayout = ChannelLayout.Stereo;
    private ChannelLayout _playLayout = ChannelLayout.Stereo;
    private int _sourceChannels = 1;
    private int _destChannels = 2;
    private float[] _scratch = [];
    private bool _toneWanted;
    private SettingsProbeKind _toneKind;
    private int _toneChannel = ChannelRouter.Off;
    private float[] _voiceLoop = [];

    public bool TonePlaying
    {
        get
        {
            lock (_gate)
            {
                return _toneWanted && _tone is { Enabled: true };
            }
        }
    }

    public SettingsProbeKind ToneKind
    {
        get
        {
            lock (_gate)
            {
                return _toneKind;
            }
        }
    }

    public int ToneChannel
    {
        get
        {
            lock (_gate)
            {
                return _toneWanted ? _toneChannel : ChannelRouter.Off;
            }
        }
    }

    public void SetInputMap(int[]? map)
    {
        lock (_gate)
        {
            _inputMap = ChannelRouter.Normalize(map, _destChannels, _sourceChannels);
        }
    }

    public void SetOutputMap(int[]? map)
    {
        _outputMap = map ?? [];
        _tone?.SetMap(_outputMap);
    }

    public bool TryGetPortNames(bool input, out string[] names)
    {
        if (_capture is AsioOut asio)
        {
            names = DevicePortNames.FromAsio(asio, input);
            return names.Length > 0;
        }

        names = [];
        return false;
    }

    public void CopyPeaks(Span<float> dest)
    {
        lock (_gate)
        {
            var n = Math.Min(dest.Length, _peaks.Length);
            for (var i = 0; i < n; i++)
            {
                dest[i] = _peaks[i];
                _peaks[i] = 0;
            }

            if (n < dest.Length)
            {
                dest[n..].Clear();
            }
        }
    }

    public string? StartMonitor(
        AudioOutputSettings output,
        string? recordDeviceId,
        ChannelLayout recordLayout,
        ChannelLayout playLayout,
        int[]? inputMap)
    {
        var keepTone = false;
        lock (_gate)
        {
            keepTone = _toneWanted;
        }

        StopDevices(clearToneWanted: false);
        _recordLayout = recordLayout;
        _playLayout = playLayout;
        _destChannels = Math.Clamp(recordLayout.Channels, 1, ChannelLayout.MaxChannels);
        try
        {
            switch (output.Api)
            {
                case AudioOutputApi.Wasapi:
                    StartWasapiCapture(recordDeviceId, recordLayout, inputMap);
                    break;
                case AudioOutputApi.Asio:
                    StartAsio(output.DeviceId, recordLayout, playLayout, inputMap, _outputMap);
                    break;
                default:
                    StartWaveIn(recordDeviceId, recordLayout, inputMap);
                    break;
            }
        }
        catch (Exception ex)
        {
            StopCaptureOnly();
            return string.IsNullOrWhiteSpace(ex.Message) ? UiStrings.StatusInputMonitorFailed : ex.Message;
        }

        if (keepTone)
        {
            SettingsProbeKind kind;
            var channel = ChannelRouter.Off;
            lock (_gate)
            {
                kind = _toneKind;
                channel = _toneChannel;
            }

            return SetTone(true, kind, channel, output, playLayout, _outputMap);
        }

        return null;
    }

    public string? SetTone(
        bool on,
        SettingsProbeKind kind,
        int logicalChannel,
        AudioOutputSettings output,
        ChannelLayout playLayout,
        int[]? outputMap,
        Func<int, float[]>? voiceFactory = null)
    {
        _playLayout = playLayout;
        if (outputMap is not null)
        {
            _outputMap = outputMap;
        }

        if (on && kind == SettingsProbeKind.Voice && SettingsTone.IsLfe(playLayout.LabelAt(logicalChannel)))
        {
            on = false;
        }

        lock (_gate)
        {
            _toneWanted = on;
            if (on)
            {
                _toneKind = kind;
                _toneChannel = logicalChannel;
            }
        }

        try
        {
            if (output.Api == AudioOutputApi.Asio)
            {
                if (_tone is null)
                {
                    if (!on)
                    {
                        return null;
                    }

                    StopDevices(clearToneWanted: false);
                    StartAsio(output.DeviceId, _recordLayout, playLayout, inputMap: _inputMap, outputMap);
                }
                else
                {
                    _tone.SetMap(_outputMap);
                }

                ApplyVoice(voiceFactory);
                ApplyToneState();
                return null;
            }

            if (!on)
            {
                StopOutputOnly();
                return null;
            }

            if (_tone is null || _output is null)
            {
                StartOutput(output, playLayout, outputMap);
            }
            else
            {
                _tone.SetMap(_outputMap);
            }

            ApplyVoice(voiceFactory);
            ApplyToneState();
            return null;
        }
        catch (Exception ex)
        {
            if (output.Api != AudioOutputApi.Asio)
            {
                StopOutputOnly();
            }

            lock (_gate)
            {
                _toneWanted = false;
            }

            return string.IsNullOrWhiteSpace(ex.Message) ? UiStrings.ErrorToneFailed : ex.Message;
        }
    }

    public void Stop() => StopDevices(clearToneWanted: true);

    private void StopDevices(bool clearToneWanted)
    {
        if (clearToneWanted)
        {
            lock (_gate)
            {
                _toneWanted = false;
            }
        }

        StopOutputOnly();
        StopCaptureOnly();
        lock (_gate)
        {
            _peaks = [];
        }
    }

    public void Dispose() => Stop();

    private void PrepareRoute(int sourceChannels, ChannelLayout layout, int[]? inputMap)
    {
        _sourceChannels = Math.Max(1, sourceChannels);
        _destChannels = Math.Clamp(layout.Channels, 1, ChannelLayout.MaxChannels);
        lock (_gate)
        {
            _inputMap = ChannelRouter.Normalize(inputMap, _destChannels, _sourceChannels);
            _peaks = new float[_destChannels];
        }
    }

    private void StartWaveIn(string? deviceId, ChannelLayout layout, int[]? inputMap)
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
        var waveIn = new WaveInEvent
        {
            DeviceNumber = index,
            WaveFormat = new WaveFormat(48000, 16, channels),
            BufferMilliseconds = 50,
        };
        PrepareRoute(channels, layout, inputMap);
        waveIn.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, waveIn.WaveFormat);
        waveIn.StartRecording();
        _capture = waveIn;
    }

    private void StartWasapiCapture(string? deviceId, ChannelLayout layout, int[]? inputMap)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        var capture = new WasapiCapture(device);
        PrepareRoute(capture.WaveFormat.Channels, layout, inputMap);
        capture.DataAvailable += (_, e) => AppendPcm(e.Buffer, e.BytesRecorded, capture.WaveFormat);
        capture.StartRecording();
        _capture = new CaptureLease(capture, device);
    }

    private void StartAsio(
        string? driverName,
        ChannelLayout recordLayout,
        ChannelLayout playLayout,
        int[]? inputMap,
        int[]? outputMap)
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
        var outputCount = Math.Max(1, asio.DriverOutputChannelCount);
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

        PrepareRoute(inputCount, recordLayout, inputMap);
        var tone = new SettingsToneProvider(rate, playLayout.Channels, outputCount, outputMap);
        asio.InitRecordAndPlayback(new SampleToWaveProvider(tone), inputCount, 0);
        asio.AudioAvailable += OnAsioAudio;
        asio.Play();
        _tone = tone;
        _capture = asio;
        _output = asio;
        ApplyToneState();
    }

    private void StartOutput(AudioOutputSettings output, ChannelLayout layout, int[]? outputMap)
    {
        StopOutputOnly();
        var ports = Math.Max(1, AudioCaptureFactory.QueryOutputChannelCount(output));
        var rate = AudioOutputFactory.QueryCurrentSampleRate(output);
        if (rate < 1000)
        {
            rate = 48000;
        }

        var tone = new SettingsToneProvider(rate, layout.Channels, ports, outputMap);
        var player = AudioOutputFactory.Create(output, out _);
        player.Init(new SampleToWaveProvider(tone));
        player.Play();
        _tone = tone;
        _output = player;
        ApplyToneState();
    }

    private void ApplyVoice(Func<int, float[]>? voiceFactory)
    {
        if (_tone is null || voiceFactory is null)
        {
            return;
        }

        var loop = voiceFactory(_tone.WaveFormat.SampleRate) ?? [];
        lock (_gate)
        {
            _voiceLoop = loop;
        }

        _tone.SetVoiceLoop(loop);
    }

    private void ApplyToneState()
    {
        if (_tone is null)
        {
            return;
        }

        bool on;
        var channel = ChannelRouter.Off;
        var kind = SettingsProbeKind.Sine;
        float[] voice;
        lock (_gate)
        {
            on = _toneWanted;
            channel = _toneChannel;
            kind = _toneKind;
            voice = _voiceLoop;
        }

        var label = on ? _playLayout.LabelAt(channel) : string.Empty;
        _tone.SetVoiceLoop(voice);
        _tone.SetSignal(on, channel, SettingsTone.HertzFor(label), kind);
    }

    private void OnAsioAudio(object? sender, AsioAudioAvailableEventArgs e)
    {
        var needed = e.SamplesPerBuffer * _sourceChannels;
        if (_scratch.Length < needed)
        {
            _scratch = new float[needed];
        }

        e.GetAsInterleavedSamples(_scratch);
        Accumulate(_scratch.AsSpan(0, needed));
    }

    private void AppendPcm(byte[] buffer, int bytes, WaveFormat format)
    {
        if (bytes <= 0 || format.BlockAlign <= 0)
        {
            return;
        }

        var frames = bytes / format.BlockAlign;
        var floats = frames * format.Channels;
        if (_scratch.Length < floats)
        {
            _scratch = new float[floats];
        }

        CapturePcm.ToFloat(buffer.AsSpan(0, bytes), format, _scratch.AsSpan(0, floats));
        Accumulate(_scratch.AsSpan(0, floats));
    }

    private void Accumulate(ReadOnlySpan<float> interleaved)
    {
        var srcCh = Math.Max(1, _sourceChannels);
        var destCh = Math.Max(1, _destChannels);
        var frames = interleaved.Length / srcCh;
        if (frames <= 0)
        {
            return;
        }

        Span<float> dest = stackalloc float[destCh];
        lock (_gate)
        {
            if (_peaks.Length != destCh)
            {
                _peaks = new float[destCh];
            }

            for (var frame = 0; frame < frames; frame++)
            {
                ChannelRouter.Gather(interleaved.Slice(frame * srcCh, srcCh), dest, _inputMap);
                for (var channel = 0; channel < destCh; channel++)
                {
                    var abs = Math.Abs(dest[channel]);
                    if (abs > _peaks[channel])
                    {
                        _peaks[channel] = abs;
                    }
                }
            }
        }
    }

    private void StopOutputOnly()
    {
        if (_output is AsioOut)
        {
            _tone?.SetSignal(false, ChannelRouter.Off);
            return;
        }

        var output = _output;
        _output = null;
        _tone = null;
        if (output is null)
        {
            return;
        }

        try
        {
            output.Stop();
        }
        catch
        {
            // 切断後は無視。
        }

        output.Dispose();
    }

    private void StopCaptureOnly()
    {
        var capture = _capture;
        _capture = null;
        if (_output is AsioOut)
        {
            _output = null;
            _tone = null;
        }

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
}
