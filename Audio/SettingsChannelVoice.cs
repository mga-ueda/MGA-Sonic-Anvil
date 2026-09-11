using System.Speech.Synthesis;
using MgaSonicAnvil.Domain;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 設定の Voice。英語のチャンネル名を SAPI で読み、−3 dBFS に揃えてループする。
/// 正弦波よりピークを上げる。発話は実効値が低いので −20 dBFS だと小さく聞こえる。
/// 既定デバイスへは出さず、呼び出し側が対象ポートへ載せる。LFE は使わない。
/// </summary>
internal static class SettingsChannelVoice
{
    public const double GapSeconds = 0.65;
    public const double LevelDb = -3;

    public static float Amplitude { get; } = (float)Math.Pow(10, LevelDb / 20d);

    private static readonly object Gate = new();
    private static readonly Dictionary<(string Label, int Rate), float[]> Cache = [];

    public static string SpokenText(string? label)
    {
        var key = (label ?? string.Empty).Trim();
        if (key.Length == 0 || SettingsTone.IsLfe(key))
        {
            return string.Empty;
        }

        return key.ToUpperInvariant() switch
        {
            "M" => "Mono",
            "L" => "Left",
            "R" => "Right",
            "C" => "Center",
            "LS" => "Left surround",
            "RS" => "Right surround",
            "LB" => "Left back",
            "RB" => "Right back",
            "S" => "Surround",
            "SL" => "Side left",
            "SR" => "Side right",
            "CS" => "Center surround",
            "LC" => "Left center",
            "RC" => "Right center",
            "TFL" => "Top front left",
            "TFR" => "Top front right",
            "TSL" => "Top side left",
            "TSR" => "Top side right",
            "TBL" => "Top back left",
            "TBR" => "Top back right",
            "LW" => "Left wide",
            "RW" => "Right wide",
            _ => key,
        };
    }

    public static float[] NormalizeLoop(ReadOnlySpan<float> pcm, int sampleRate, double gapSeconds = GapSeconds)
    {
        sampleRate = Math.Clamp(sampleRate, 1000, 384000);
        var peak = 0f;
        foreach (var sample in pcm)
        {
            var abs = Math.Abs(sample);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        var scale = peak > 1e-8f ? Amplitude / peak : 0f;
        var gap = Math.Max(0, (int)Math.Round(sampleRate * Math.Max(0, gapSeconds)));
        var result = new float[pcm.Length + gap];
        for (var i = 0; i < pcm.Length; i++)
        {
            result[i] = pcm[i] * scale;
        }

        return result;
    }

    public static float[] Resample(ReadOnlySpan<float> source, int sourceRate, int destRate)
    {
        sourceRate = Math.Clamp(sourceRate, 1, 384000);
        destRate = Math.Clamp(destRate, 1, 384000);
        if (source.Length == 0 || sourceRate == destRate)
        {
            return source.ToArray();
        }

        var destLength = Math.Max(1, (int)((long)source.Length * destRate / sourceRate));
        var dest = new float[destLength];
        var step = sourceRate / (double)destRate;
        var cursor = 0d;
        for (var i = 0; i < dest.Length; i++)
        {
            var index = (int)cursor;
            var frac = (float)(cursor - index);
            var a = source[Math.Clamp(index, 0, source.Length - 1)];
            var b = source[Math.Clamp(index + 1, 0, source.Length - 1)];
            dest[i] = a + ((b - a) * frac);
            cursor += step;
        }

        return dest;
    }

    public static bool TryRender(string? label, int outputRate, out float[] samples, out string? error)
    {
        samples = [];
        error = null;
        outputRate = Math.Clamp(outputRate < 1000 ? 48000 : outputRate, 1000, 384000);
        var text = SpokenText(label);
        if (text.Length == 0)
        {
            error = UiStrings.ErrorChannelVoiceFailed;
            return false;
        }

        var key = (label ?? string.Empty, outputRate);
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached))
            {
                samples = cached;
                return true;
            }

            try
            {
                var rendered = Speak(text);
                if (rendered.Samples.Length == 0)
                {
                    error = UiStrings.ErrorChannelVoiceFailed;
                    return false;
                }

                var loop = NormalizeLoop(rendered.Samples, rendered.SampleRate);
                samples = Resample(loop, rendered.SampleRate, outputRate);
                Cache[key] = samples;
                return true;
            }
            catch (Exception ex)
            {
                error = string.IsNullOrWhiteSpace(ex.Message)
                    ? UiStrings.ErrorChannelVoiceFailed
                    : ex.Message;
                samples = [];
                return false;
            }
        }
    }

    private static (float[] Samples, int SampleRate) Speak(string text)
    {
        using var synth = new SpeechSynthesizer();
        SelectEnglishVoice(synth);
        synth.Rate = -1;
        synth.Volume = 100;
        using var stream = new MemoryStream();
        synth.SetOutputToWaveStream(stream);
        synth.Speak(text);
        synth.SetOutputToNull();
        if (stream.Length < 44)
        {
            return ([], 22050);
        }

        stream.Position = 0;
        using var reader = new WaveFileReader(stream);
        var rate = reader.WaveFormat.SampleRate;
        var provider = reader.ToSampleProvider();
        var channels = Math.Max(1, provider.WaveFormat.Channels);
        var buffer = new float[Math.Max(channels, rate / 2)];
        var list = new List<float>();
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            var frames = read / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                list.Add(buffer[frame * channels]);
            }
        }

        return (list.ToArray(), rate > 0 ? rate : 22050);
    }

    private static void SelectEnglishVoice(SpeechSynthesizer synth)
    {
        foreach (var voice in synth.GetInstalledVoices())
        {
            if (!voice.Enabled)
            {
                continue;
            }

            var culture = voice.VoiceInfo.Culture;
            if (culture is not null
                && culture.TwoLetterISOLanguageName.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                synth.SelectVoice(voice.VoiceInfo.Name);
                return;
            }
        }
    }
}
