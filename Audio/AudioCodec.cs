using System.IO;
using System.Text;
using MgaSonicAnvil.Domain;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

internal static class AudioCodec
{
    private static readonly object MediaFoundationGate = new();
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
            throw new InvalidDataException(UiStrings.ErrEmptyAudioFile);
        }

        var interleaved = new float[checked((int)frames * channels)];
        var provider = AsPcmProvider(stream).ToSampleProvider();
        if (provider.WaveFormat.Channels != channels)
        {
            throw new InvalidDataException(
                UiStrings.ErrChannelCountChanged(channels, provider.WaveFormat.Channels));
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
            throw new InvalidDataException(UiStrings.ErrEmptyAudioFile);
        }

        var document = new AudioDocument(
            interleaved,
            sampleRate,
            channels,
            bits <= 0 ? 16 : bits,
            DetectKind(path),
            path);
        document.SetChannelMask(DevicePortNames.ReadWaveFileChannelMask(path));
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

    private static readonly byte[] PcmSubFormat =
        new Guid(0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71).ToByteArray();

    public static void SaveWaveRange(
        AudioDocument document,
        long startFrame,
        long frameCount,
        string path,
        IProgress<double>? progress = null)
    {
        var start = Math.Clamp(startFrame, 0, document.FrameCount);
        var length = Math.Clamp(frameCount, 0, document.FrameCount - start);
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount));
        }

        var samples = document.CopyRange(start, length);
        var slice = new AudioDocument(
            samples,
            document.SampleRate,
            document.Channels,
            document.BitsPerSample,
            AudioFileKind.Wave,
            path);
        slice.SetChannelMask(document.ChannelMask);
        SaveWave(slice, path, progress);
    }

    public static void SaveWave(AudioDocument document, string path, IProgress<double>? progress = null) =>
        WriteWave(document, path, ResolveWaveBits(document.BitsPerSample), embedMeta: true, progress);

    internal static void SavePcm16Wave(AudioDocument document, string path, IProgress<double>? progress = null) =>
        WriteWave(document, path, bits: 16, embedMeta: false, progress);

    private static int ResolveWaveBits(int bits) => bits switch
    {
        8 => 8,
        24 => 24,
        _ => 16,
    };

    private static void WriteWave(
        AudioDocument document,
        string path,
        int bits,
        bool embedMeta,
        IProgress<double>? progress = null)
    {
        var channels = Math.Max(1, document.Channels);
        var sampleRate = Math.Max(1, document.SampleRate);
        var blockAlign = channels * (bits / 8);
        var byteRate = sampleRate * blockAlign;
        var frames = document.FrameCount;

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            var riffSizePos = stream.Position;
            writer.Write(0);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            WriteFmtChunk(writer, sampleRate, channels, bits, byteRate, blockAlign, document.ChannelMask);
            if (bits > 16)
            {
                writer.Write(Encoding.ASCII.GetBytes("fact"));
                writer.Write(4);
                writer.Write((int)Math.Clamp(frames, 0, int.MaxValue));
            }

            writer.Write(Encoding.ASCII.GetBytes("data"));
            var dataSizePos = stream.Position;
            writer.Write(0);
            WritePcm(document, writer, bits, progress, embedMeta ? 0.92 : 1);
            var dataSize = (int)(stream.Position - dataSizePos - 4);
            if ((dataSize & 1) != 0)
            {
                writer.Write((byte)0);
            }

            var end = stream.Position;
            stream.Position = dataSizePos;
            writer.Write(dataSize);
            stream.Position = riffSizePos;
            writer.Write((int)(end - 8));
        }

        if (!embedMeta)
        {
            progress?.Report(1);
            return;
        }

        WavEmbeddedMeta.Write(path, document);
        DeleteSoundForgeSidecars(path);
        progress?.Report(1);
    }

    private static void WriteFmtChunk(
        BinaryWriter writer,
        int sampleRate,
        int channels,
        int bits,
        int byteRate,
        int blockAlign,
        int channelMask)
    {
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        if (bits <= 16)
        {
            writer.Write(16);
            writer.Write((ushort)1);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)bits);
            return;
        }

        // 24-bit を 16 バイト PCM で書くと Sound Forge が raw 32 kHz として開く。
        // スピーカーマスクは元ファイルの値だけ残す。無ければ 0（未指定）。推測して埋めない。
        writer.Write(40);
        writer.Write((ushort)0xFFFE);
        writer.Write((ushort)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)bits);
        writer.Write((ushort)22);
        writer.Write((ushort)bits);
        writer.Write(channelMask);
        writer.Write(PcmSubFormat);
    }

    private static void DeleteSoundForgeSidecars(string path)
    {
        foreach (var ext in new[] { ".sfk", ".sfl" })
        {
            var sidecar = Path.ChangeExtension(path, ext);
            try
            {
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }
            catch
            {
                // ロック中でも WAV 本体は書けている。
            }
        }
    }

    public static Mp3EncoderKind SaveMp3(
        AudioDocument document,
        string path,
        Mp3EncodeOptions options,
        IProgress<double>? progress = null)
    {
        if (Mp3Encode.TryResolveLameExe(options.LameExePath, out var lameExe))
        {
            LameEncoder.Encode(document, path, lameExe, options.LameOptions, progress);
            return Mp3EncoderKind.Lame;
        }

        try
        {
            lock (MediaFoundationGate)
            {
                EnsureMediaFoundation();
                using var provider = new Pcm16WaveProvider(document, progress);
                MediaFoundationEncoder.EncodeToMp3(
                    provider,
                    path,
                    Mp3Encode.ClampWindowsBitRate(options.WindowsBitRateKbps) * 1000);
            }

            progress?.Report(1);
            return Mp3EncoderKind.Windows;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{UiStrings.ErrorWindowsMp3Failed}\n{ex.Message}", ex);
        }
    }

    public static Mp3EncoderKind? Save(AudioDocument document, string path, Mp3EncodeOptions options)
    {
        var kind = DetectKind(path);
        if (kind == AudioFileKind.Aiff)
        {
            throw new NotSupportedException(UiStrings.ErrAiffExportNotSupported);
        }

        if (kind == AudioFileKind.Mp3)
        {
            return SaveMp3(document, path, options);
        }

        SaveWave(document, path);
        return null;
    }

    private static void WritePcm(
        AudioDocument document,
        BinaryWriter writer,
        int bits,
        IProgress<double>? progress,
        double progressScale)
    {
        var samples = document.Interleaved;
        var lastBucket = -1;
        void Step(int index)
        {
            ReportWriteProgress(progress, index, samples.Length, progressScale, ref lastBucket);
        }

        if (bits <= 8)
        {
            var buffer = new byte[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                buffer[i] = (byte)Math.Clamp((int)Math.Round(samples[i] * 127f + 128f), 0, 255);
                Step(i + 1);
            }

            writer.Write(buffer);
            return;
        }

        if (bits <= 16)
        {
            var buffer = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var value = (short)Math.Clamp((int)Math.Round(samples[i] * 32767f), short.MinValue, short.MaxValue);
                buffer[i * 2] = (byte)value;
                buffer[i * 2 + 1] = (byte)(value >> 8);
                Step(i + 1);
            }

            writer.Write(buffer);
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
            Step(i + 1);
        }

        writer.Write(packed);
    }

    private static void ReportWriteProgress(
        IProgress<double>? progress,
        int index,
        int total,
        double scale,
        ref int lastBucket)
    {
        if (progress is null || total <= 0)
        {
            return;
        }

        var bucket = index * 50 / total;
        if (bucket == lastBucket && index < total)
        {
            return;
        }

        lastBucket = bucket;
        progress.Report(Math.Clamp(index / (double)total * scale, 0, 1));
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

    private static IWaveProvider AsPcmProvider(WaveStream stream)
    {
        var format = stream.WaveFormat;
        if (format.Encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible
            && format.BitsPerSample is 8 or 16 or 24 or 32)
        {
            var pcm = new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
            if (format.Encoding != WaveFormatEncoding.Pcm || format.ExtraSize != 0)
            {
                return new RelabeledWaveProvider(stream, pcm);
            }
        }

        return stream;
    }

    private static void EnsureMediaFoundation()
    {
        if (_mediaFoundationStarted)
        {
            return;
        }

        lock (MediaFoundationGate)
        {
            if (_mediaFoundationStarted)
            {
                return;
            }

            MediaFoundationApi.Startup();
            _mediaFoundationStarted = true;
        }
    }

    private sealed class RelabeledWaveProvider : IWaveProvider
    {
        private readonly IWaveProvider _source;

        public RelabeledWaveProvider(IWaveProvider source, WaveFormat format)
        {
            _source = source;
            WaveFormat = format;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count) =>
            _source.Read(buffer, offset, count);
    }
}
