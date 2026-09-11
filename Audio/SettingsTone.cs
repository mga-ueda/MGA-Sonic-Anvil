using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>設定の出力確認用。通常 1 kHz、LFE は 80 Hz。−20 dBFS。</summary>
internal static class SettingsTone
{
    public const double Hertz = 1000;
    public const double LfeHertz = 80;
    public const double LevelDb = -20;

    public static float Amplitude { get; } = (float)Math.Pow(10, LevelDb / 20d);

    public static bool IsLfe(string? label) =>
        string.Equals((label ?? string.Empty).Trim(), "LFE", StringComparison.OrdinalIgnoreCase);

    public static double HertzFor(string? label) => IsLfe(label) ? LfeHertz : Hertz;

    public static void FillInterleaved(
        Span<float> interleaved,
        int channels,
        int sampleRate,
        double phase,
        out double nextPhase)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var step = Math.Tau * Hertz / sampleRate;
        var frames = interleaved.Length / channels;
        var cursor = phase;
        for (var frame = 0; frame < frames; frame++)
        {
            var sample = Amplitude * (float)Math.Sin(cursor);
            cursor += step;
            if (cursor >= Math.Tau)
            {
                cursor -= Math.Tau;
            }

            var at = frame * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                interleaved[at + channel] = sample;
            }
        }

        nextPhase = cursor;
    }
}

internal enum SettingsProbeKind
{
    Sine = 0,
    Voice = 1,
}

/// <summary>選んだ論理チャンネルだけに正弦波またはボイスを載せ、出力マップでポートへ散らす。</summary>
internal sealed class SettingsToneProvider : ISampleProvider
{
    private readonly object _gate = new();
    private readonly int _logicalChannels;
    private readonly int _sampleRate;
    private int[] _map;
    private float[] _voice = [];
    private int _voiceCursor;
    private int _logical = ChannelRouter.Off;
    private SettingsProbeKind _kind;
    private double _step;
    private double _phase;
    private bool _enabled;

    public SettingsToneProvider(int sampleRate, int logicalChannels, int outputPorts, int[]? map)
    {
        _sampleRate = Math.Clamp(sampleRate, 1000, 384000);
        _logicalChannels = Math.Clamp(logicalChannels, 1, ChannelLayout.MaxChannels);
        outputPorts = Math.Clamp(outputPorts, 1, ChannelLayout.MaxChannels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, outputPorts);
        _step = Math.Tau * SettingsTone.Hertz / _sampleRate;
        _map = ChannelRouter.Normalize(map, _logicalChannels, outputPorts);
    }

    public WaveFormat WaveFormat { get; }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public int ActiveChannel
    {
        get
        {
            lock (_gate)
            {
                return _enabled ? _logical : ChannelRouter.Off;
            }
        }
    }

    public void SetMap(int[]? map)
    {
        lock (_gate)
        {
            _map = ChannelRouter.Normalize(map, _logicalChannels, WaveFormat.Channels);
        }
    }

    public SettingsProbeKind Kind
    {
        get
        {
            lock (_gate)
            {
                return _kind;
            }
        }
    }

    public void SetVoiceLoop(float[]? samples)
    {
        lock (_gate)
        {
            _voice = samples is { Length: > 0 } ? [.. samples] : [];
            _voiceCursor = 0;
        }
    }

    public void SetSignal(
        bool enabled,
        int logicalChannel,
        double hertz = SettingsTone.Hertz,
        SettingsProbeKind kind = SettingsProbeKind.Sine)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _logical = logicalChannel;
            _kind = kind;
            _voiceCursor = 0;
            var freq = hertz > 0 ? hertz : SettingsTone.Hertz;
            _step = Math.Tau * freq / _sampleRate;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var outCh = Math.Max(1, WaveFormat.Channels);
        var frames = count / outCh;
        Span<float> logical = stackalloc float[_logicalChannels];
        int[] map;
        float[] voice;
        var on = false;
        var channel = ChannelRouter.Off;
        var kind = SettingsProbeKind.Sine;
        lock (_gate)
        {
            map = _map;
            voice = _voice;
            on = _enabled;
            channel = _logical;
            kind = _kind;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var dest = buffer.AsSpan(offset + (frame * outCh), outCh);
            logical.Clear();
            if (on && (uint)channel < (uint)logical.Length)
            {
                logical[channel] = NextSample(kind, voice);
            }

            if (on)
            {
                ChannelRouter.Scatter(logical, dest, map);
            }
            else
            {
                dest.Clear();
            }

            _phase += _step;
            if (_phase >= Math.Tau)
            {
                _phase -= Math.Tau;
            }
        }

        return frames * outCh;
    }

    private float NextSample(SettingsProbeKind kind, float[] voice)
    {
        if (kind == SettingsProbeKind.Voice)
        {
            if (voice.Length == 0)
            {
                return 0;
            }

            var sample = voice[_voiceCursor];
            _voiceCursor++;
            if (_voiceCursor >= voice.Length)
            {
                _voiceCursor = 0;
            }

            return sample;
        }

        return SettingsTone.Amplitude * (float)Math.Sin(_phase);
    }
}
