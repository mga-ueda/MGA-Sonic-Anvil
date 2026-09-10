using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>設定の出力確認用。1 kHz / −20 dBFS の正弦波。</summary>
internal static class SettingsTone
{
    public const double Hertz = 1000;
    public const double LevelDb = -20;

    public static float Amplitude { get; } = (float)Math.Pow(10, LevelDb / 20d);

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

/// <summary>論理チャンネルに正弦波を載せ、出力マップでポートへ散らす。</summary>
internal sealed class SettingsToneProvider : ISampleProvider
{
    private readonly object _gate = new();
    private readonly int _logicalChannels;
    private readonly double _step;
    private int[] _map;
    private double _phase;
    private bool _enabled;

    public SettingsToneProvider(int sampleRate, int logicalChannels, int outputPorts, int[]? map)
    {
        sampleRate = Math.Clamp(sampleRate, 1000, 384000);
        _logicalChannels = Math.Clamp(logicalChannels, 1, ChannelLayout.MaxChannels);
        outputPorts = Math.Clamp(outputPorts, 1, ChannelLayout.MaxChannels);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, outputPorts);
        _step = Math.Tau * SettingsTone.Hertz / sampleRate;
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
        set
        {
            lock (_gate)
            {
                _enabled = value;
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

    public int Read(float[] buffer, int offset, int count)
    {
        var outCh = Math.Max(1, WaveFormat.Channels);
        var frames = count / outCh;
        Span<float> logical = stackalloc float[_logicalChannels];
        int[] map;
        var on = false;
        lock (_gate)
        {
            map = _map;
            on = _enabled;
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var dest = buffer.AsSpan(offset + (frame * outCh), outCh);
            if (on)
            {
                var sample = SettingsTone.Amplitude * (float)Math.Sin(_phase);
                logical.Fill(sample);
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
}
