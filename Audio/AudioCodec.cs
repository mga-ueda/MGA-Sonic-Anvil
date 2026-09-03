using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace MgaSonicAnvil.Audio;

internal static class AudioCodec
{
    private static bool _mediaFoundationStarted;

    public static IReadOnlyList<string> OpenExtensions { get; } =
        [".wav", ".wave", ".aif", ".aiff", ".mp3"];

    public static IReadOnlyList<string> SaveExtensions { get; } = [".wav", ".wave", ".mp3"];

    public static bool IsOpenable(string path)
    {
        var ext = Path.GetExtension(path);
        return OpenExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static AudioFileKind DetectKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFileKind.Mp3;
        }

        if (ext.Equals(".aif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFileKind.Aiff;
        }

        return AudioFileKind.Wave;
    }

    public static AudioDocument Load(string path)
    {
        EnsureMediaFoundation();
        var bits = PeekBitDepth(path);
        using var stream = OpenReader(path);
        var format = stream.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var sampleRate = format.SampleRate;
        var frames = stream.Length / Math.Max(1, format.BlockAlign);
        if (frames <= 0)
        {
            throw new InvalidDataException("Empty audio file.");
        }

        var interleaved = new float[checked((int)frames * channels)];
        var provider = stream.ToSampleProvider();
        if (provider.WaveFormat.Channels != channels)
        {
            throw new InvalidDataException(
                $"Channel count changed while reading ({channels} → {provider.WaveFormat.Channels}).");
        }

        var read = 0;
        while (read < interleaved.Length)
        {
            var n = provider.Read(interleaved, read, interleaved.Length - read);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        if (read < interleaved.Length)
        {
            Array.Resize(ref interleaved, read - read % channels);
        }

        if (interleaved.Length < channels)
        {
            throw new InvalidDataException("Empty audio file.");
        }

        var document = new AudioDocument(
            interleaved,
            sampleRate,
            channels,
            bits <= 0 ? 16 : bits,
            DetectKind(path),
            path);
        ApplyEmbeddedMeta(document, path);
        return document;
    }

    private static void ApplyEmbeddedMeta(AudioDocument document, string path)
    {
        var kind = DetectKind(path);
        if (kind == AudioFileKind.Wave && WavEmbeddedMeta.TryRead(path, out var wave))
        {
            wave.Apply(document);
            return;
        }

        if (kind == AudioFileKind.Aiff && AiffEmbeddedMeta.TryRead(path, out var aiff))
        {
            aiff.Apply(document);
        }
    }

    private static WaveStream OpenReader(string path)
    {
        var kind = DetectKind(path);
        return kind switch
        {
            AudioFileKind.Aiff => new AiffFileReader(path),
            AudioFileKind.Mp3 => OpenMp3Reader(path),
            _ => new WaveFileReader(path),
        };
    }

    private static WaveStream OpenMp3Reader(string path)
    {
        try
        {
            return new MediaFoundationReader(path);
        }
        catch
        {
            return new Mp3FileReader(path);
        }
    }

    public static void SaveWave(AudioDocument document, string path)
    {
        var format = document.BitsPerSample <= 16
            ? new WaveFormat(document.SampleRate, 16, document.Channels)
            : new WaveFormat(document.SampleRate, 24, document.Channels);
        using var writer = new WaveFileWriter(path, format);
        WritePcm(document, writer, format.BitsPerSample);
    }

    public static void SaveMp3(AudioDocument document, string path, int bitRateKbps)
    {
        EnsureMediaFoundation();
        using var provider = new Pcm16WaveProvider(document);
        MediaFoundationEncoder.EncodeToMp3(provider, path, bitRateKbps * 1000);
    }

    public static void Save(AudioDocument document, string path, int mp3BitRateKbps)
    {
        var kind = DetectKind(path);
        if (kind == AudioFileKind.Aiff)
        {
            throw new NotSupportedException("AIFF export is not supported.");
        }

        if (kind == AudioFileKind.Mp3)
        {
            SaveMp3(document, path, mp3BitRateKbps);
            return;
        }

        SaveWave(document, path);
    }

    private static void WritePcm(AudioDocument document, WaveFileWriter writer, int bits)
    {
        var samples = document.Interleaved;
        if (bits <= 16)
        {
            var buffer = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)Math.Clamp((int)Math.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
                buffer[i * 2] = (byte)value;
                buffer[i * 2 + 1] = (byte)(value >> 8);
            }

            writer.Write(buffer, 0, buffer.Length);
            return;
        }

        var packed = new byte[samples.Length * 3];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (int)Math.Clamp(Math.Round(samples[i] * 8388607d), -8388608, 8388607);
            var offset = i * 3;
            packed[offset] = (byte)value;
            packed[offset + 1] = (byte)(value >> 8);
            packed[offset + 2] = (byte)(value >> 16);
        }

        writer.Write(packed, 0, packed.Length);
    }

    private static int PeekBitDepth(string path)
    {
        var kind = DetectKind(path);
        try
        {
            if (kind == AudioFileKind.Wave)
            {
                using var wave = new WaveFileReader(path);
                return wave.WaveFormat.BitsPerSample;
            }

            if (kind == AudioFileKind.Aiff)
            {
                using var aiff = new AiffFileReader(path);
                return aiff.WaveFormat.BitsPerSample;
            }
        }
        catch
        {
            // ビット深度が取れなければ 16 として扱う。
        }

        return 16;
    }

    private static void EnsureMediaFoundation()
    {
        if (_mediaFoundationStarted)
        {
            return;
        }

        MediaFoundationApi.Startup();
        _mediaFoundationStarted = true;
    }
}
