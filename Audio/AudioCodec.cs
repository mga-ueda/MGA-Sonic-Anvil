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

    /// <summary>F10 プレイヤー専用。編集オープンには含めない。</summary>
    public static IReadOnlyList<string> PlayerOpenExtensions { get; } =
        [".wav", ".wave", ".aif", ".aiff", ".mp3", ".m4a"];

    public static IReadOnlyList<string> SaveExtensions { get; } = [".wav", ".wave", ".mp3"];

    public static bool IsOpenable(string path) => MatchesExtension(path, OpenExtensions);

    public static bool IsPlayerOpenable(string path) => MatchesExtension(path, PlayerOpenExtensions);

    /// <summary>プレイヤーでフル PCM 展開せずストリーム再生できるか。</summary>
    public static bool CanStreamPlay(string path)
    {
        var kind = DetectKind(path);
        return kind is AudioFileKind.Mp3 or AudioFileKind.M4a
            || kind is AudioFileKind.Wave or AudioFileKind.Aiff;
    }

    private static bool MatchesExtension(string path, IReadOnlyList<string> extensions)
    {
        var ext = Path.GetExtension(path);
        for (var i = 0; i < extensions.Count; i++)
        {
            if (extensions[i].Equals(ext, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ドロップ／起動パスを開けるファイルへ展開する。フォルダは再帰。非対応拡張子は無視。
    /// </summary>
    public static string[] CollectOpenable(IEnumerable<string> paths) =>
        CollectOpenable(paths, player: false);

    public static string[] CollectPlayerOpenable(IEnumerable<string> paths) =>
        CollectOpenable(paths, player: true);

    private static string[] CollectOpenable(IEnumerable<string> paths, bool player)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(full))
            {
                AddOpenableFromDirectory(full, result, seen, recursive: true, player);
                continue;
            }

            if (IsAccepted(full, player) && seen.Add(full))
            {
                result.Add(full);
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// 1 フォルダ内の開けるファイル。recursive なら配下も。非対応拡張子は無視。
    /// </summary>
    public static string[] CollectOpenableFromDirectory(string directory, bool recursive) =>
        CollectOpenableFromDirectory(directory, recursive, player: false);

    public static string[] CollectPlayerOpenableFromDirectory(string directory, bool recursive) =>
        CollectOpenableFromDirectory(directory, recursive, player: true);

    /// <summary>
    /// プレイヤー用の再帰走査 1 段。今のフォルダのファイル（名前順）と、続けて見る子フォルダ。
    /// ジャンクションは子に含めない。フォルダ全体の収集を待たずに 1 曲ずつ載せる用。
    /// </summary>
    public static void CollectPlayerOpenableDirectoryLayer(
        string directory,
        out string[] files,
        out string[] children)
    {
        files = [];
        children = [];
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        string full;
        try
        {
            full = Path.GetFullPath(directory.Trim());
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(full))
        {
            return;
        }

        var found = new List<string>();
        CollectFilesInDirectory(full, found, player: true);
        found.Sort(StringComparer.OrdinalIgnoreCase);
        files = found.ToArray();

        var nested = new List<string>();
        try
        {
            foreach (var child in Directory.EnumerateDirectories(full))
            {
                if (IsDirectoryReparsePoint(child))
                {
                    continue;
                }

                nested.Add(child);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        nested.Sort(StringComparer.OrdinalIgnoreCase);
        children = nested.ToArray();
    }

    private static string[] CollectOpenableFromDirectory(string directory, bool recursive, bool player)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return [];
        }

        string full;
        try
        {
            full = Path.GetFullPath(directory.Trim());
        }
        catch
        {
            return [];
        }

        if (!Directory.Exists(full))
        {
            return [];
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddOpenableFromDirectory(full, result, seen, recursive, player);
        return result.ToArray();
    }

    public static bool CanAcceptDrop(IEnumerable<string> paths) =>
        CanAcceptDrop(paths, player: false);

    public static bool CanAcceptPlayerDrop(IEnumerable<string> paths) =>
        CanAcceptDrop(paths, player: true);

    private static bool CanAcceptDrop(IEnumerable<string> paths, bool player)
    {
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim().Trim('"'));
            }
            catch
            {
                continue;
            }

            if (Directory.Exists(full) || IsAccepted(full, player))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccepted(string path, bool player) =>
        player ? IsPlayerOpenable(path) : IsOpenable(path);

    private static void AddOpenableFromDirectory(
        string directory,
        List<string> result,
        HashSet<string> seen,
        bool recursive,
        bool player)
    {
        var found = new List<string>();
        if (recursive)
        {
            var stack = new Stack<string>();
            stack.Push(directory);
            while (stack.Count > 0)
            {
                var current = stack.Pop();
                CollectFilesInDirectory(current, found, player);
                try
                {
                    foreach (var child in Directory.EnumerateDirectories(current))
                    {
                        if (IsDirectoryReparsePoint(child))
                        {
                            continue;
                        }

                        stack.Push(child);
                    }
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        else
        {
            CollectFilesInDirectory(directory, found, player);
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        foreach (var file in found)
        {
            if (seen.Add(file))
            {
                result.Add(file);
            }
        }
    }

    private static void CollectFilesInDirectory(string directory, List<string> found, bool player)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (IsAccepted(file, player))
                {
                    found.Add(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsDirectoryReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    public static AudioFileKind DetectKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFileKind.Mp3;
        }

        if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFileKind.M4a;
        }

        if (ext.Equals(".aif", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
        {
            return AudioFileKind.Aiff;
        }

        return AudioFileKind.Wave;
    }

    /// <summary>プレイヤー用ストリーム。呼び出し側が Dispose する。</summary>
    public static WaveStream OpenPlaybackStream(string path)
    {
        EnsureMediaFoundation();
        return OpenReader(path);
    }

    /// <summary>
    /// WaveStream の長さ。Length/BlockAlign と TotalTime が食い違うときは TotalTime を優先する。
    /// MediaFoundation は低サンプルレートなどで Length がずれることがある。
    /// </summary>
    public static long EstimateStreamFrameCount(WaveStream reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var format = reader.WaveFormat;
        var sampleRate = Math.Max(1, format.SampleRate);
        var block = Math.Max(1, format.BlockAlign);
        var fromLength = reader.Length > 0 ? reader.Length / block : 0L;
        var totalSeconds = reader.TotalTime.TotalSeconds;
        var fromTime = totalSeconds > 0
            ? (long)Math.Round(totalSeconds * sampleRate)
            : 0L;
        if (fromLength <= 0)
        {
            return Math.Max(0, fromTime);
        }

        if (fromTime <= 0)
        {
            return fromLength;
        }

        var ratio = fromLength / (double)fromTime;
        // 5% 超えのずれは Length 側を疑う（低レート MF など）。
        if (ratio < 0.95 || ratio > 1.05)
        {
            return fromTime;
        }

        return fromLength;
    }

    /// <summary>ヘッダだけ読んで長さを取る。PCM は展開しない。</summary>
    public static bool TryProbeStreamFormat(
        string path,
        out int sampleRate,
        out int channels,
        out int bitsPerSample,
        out long frameCount)
    {
        sampleRate = 0;
        channels = 0;
        bitsPerSample = 16;
        frameCount = 0;
        try
        {
            EnsureMediaFoundation();
            using var reader = OpenReader(path);
            var format = reader.WaveFormat;
            sampleRate = Math.Max(1, format.SampleRate);
            channels = Math.Max(1, format.Channels);
            bitsPerSample = format.BitsPerSample > 0 ? format.BitsPerSample : 16;
            frameCount = EstimateStreamFrameCount(reader);

            return frameCount > 0 && sampleRate > 0 && channels > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or ArgumentException or NotSupportedException
                                       or System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    /// <summary>プレイヤー用。フル Load せずストリーム再生メタだけ載せる。</summary>
    public static bool TryActivateStreamPlayback(AudioDocument document)
    {
        if (document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path)
            || !CanStreamPlay(path))
        {
            return false;
        }

        if (TryProbeStreamFormat(path, out var rate, out var channels, out var bits, out var frames))
        {
            document.ActivateStreamPlayback(rate, channels, bits, frames);
            return true;
        }

        // タグに長さがあればそれで足りる（開けるかは再生時に判明）。
        AudioTagProbe.Ensure(document);
        var tags = document.Tags;
        if (tags.SampleRate > 0 && tags.Channels > 0 && tags.DurationSeconds > 0)
        {
            var estimated = (long)Math.Round(tags.DurationSeconds * tags.SampleRate);
            if (estimated > 0)
            {
                document.ActivateStreamPlayback(
                    tags.SampleRate,
                    tags.Channels,
                    tags.BitsPerSample > 0 ? tags.BitsPerSample : 16,
                    estimated);
                return true;
            }
        }

        return false;
    }

    internal static ISampleProvider AsSampleProvider(WaveStream stream) =>
        AsPcmProvider(stream).ToSampleProvider();

    public static AudioDocument Load(string path, bool buildPeaks = true)
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
        var provider = AsSampleProvider(stream);
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
            path,
            buildPeaks);
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
            AudioFileKind.M4a => OpenMediaFoundationReader(path),
            _ => new WaveFileReader(path),
        };
    }

    private static WaveStream OpenMp3Reader(string path)
    {
        try
        {
            return OpenMediaFoundationReader(path);
        }
        catch
        {
            return new Mp3FileReader(path);
        }
    }

    private static WaveStream OpenMediaFoundationReader(string path) =>
        new MediaFoundationReader(path);

    private static readonly byte[] PcmSubFormat =
        new Guid(0x00000001, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71).ToByteArray();

    private static readonly byte[] IeeeFloatSubFormat =
        new Guid(0x00000003, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71).ToByteArray();

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
        32 => 32,
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
        if (bits == 32)
        {
            WriteIeeeFloatFmt(writer, sampleRate, channels, byteRate, blockAlign, channelMask);
            return;
        }

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
        WriteExtensibleFmt(writer, sampleRate, channels, bits, byteRate, blockAlign, channelMask, PcmSubFormat);
    }

    private static void WriteIeeeFloatFmt(
        BinaryWriter writer,
        int sampleRate,
        int channels,
        int byteRate,
        int blockAlign,
        int channelMask)
    {
        if (channelMask == 0)
        {
            writer.Write(16);
            writer.Write((ushort)3);
            writer.Write((ushort)channels);
            writer.Write(sampleRate);
            writer.Write(byteRate);
            writer.Write((ushort)blockAlign);
            writer.Write((ushort)32);
            return;
        }

        WriteExtensibleFmt(writer, sampleRate, channels, 32, byteRate, blockAlign, channelMask, IeeeFloatSubFormat);
    }

    private static void WriteExtensibleFmt(
        BinaryWriter writer,
        int sampleRate,
        int channels,
        int bits,
        int byteRate,
        int blockAlign,
        int channelMask,
        byte[] subFormat)
    {
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
        writer.Write(subFormat);
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

    /// <summary>
    /// スピーカー配置へ寄せてから、再生と同じ係数で 2ch に畳む。
    /// モノラルファイルはそのまま。配置が Stereo なら割り当てた 2 本だけ出す。
    /// </summary>
    internal static AudioDocument ForMp3Encode(AudioDocument document, Mp3SpeakerMix mix = default)
    {
        var fileChannels = Math.Max(1, document.Channels);
        if (fileChannels <= 1)
        {
            return document;
        }

        var source = UsedInterleaved(document, fileChannels);
        var speakers = mix.SpeakerChannels > 0
            ? Math.Clamp(mix.SpeakerChannels, 1, ChannelLayout.MaxChannels)
            : fileChannels;
        var map = ChannelRouter.Normalize(mix.FileChannelMap, speakers, fileChannels);
        var gather = NeedsSpeakerGather(map, speakers, fileChannels);
        if (gather)
        {
            source = ChannelRouter.MapInterleaved(source, fileChannels, speakers, map, gather: true);
        }
        else if (speakers <= 2)
        {
            return document;
        }

        if (speakers <= 2)
        {
            return CopyForMp3(document, source, speakers);
        }

        return CopyForMp3(document, FormatConvert.Remix(source, speakers, destChannels: 2), channels: 2);
    }

    private static float[] UsedInterleaved(AudioDocument document, int channels)
    {
        var used = Math.Clamp(document.SampleCount, 0, document.Interleaved.Length);
        used -= used % channels;
        var source = document.Interleaved;
        if (used == source.Length)
        {
            return source;
        }

        var exact = new float[used];
        if (used > 0)
        {
            Array.Copy(source, exact, used);
        }

        return exact;
    }

    private static bool NeedsSpeakerGather(int[] map, int speakers, int fileChannels)
    {
        if (speakers != fileChannels || map.Length != speakers)
        {
            return true;
        }

        for (var i = 0; i < map.Length; i++)
        {
            if (map[i] != i)
            {
                return true;
            }
        }

        return false;
    }

    private static AudioDocument CopyForMp3(AudioDocument document, float[] interleaved, int channels)
    {
        var copy = new AudioDocument(
            interleaved,
            document.SampleRate,
            channels,
            document.BitsPerSample,
            AudioFileKind.Mp3,
            document.SourcePath);
        copy.SetArtwork(document.Artwork);
        return copy;
    }

    public static Mp3EncoderKind SaveMp3(
        AudioDocument document,
        string path,
        Mp3EncodeOptions options,
        IProgress<double>? progress = null,
        Mp3SpeakerMix mix = default)
    {
        document = ForMp3Encode(document, mix);
        if (Mp3Encode.TryResolveLameExe(options.LameExePath, out var lameExe))
        {
            LameEncoder.Encode(document, path, lameExe, options.LameOptions, progress);
            EmbedMp3Artwork(document, path);
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
            EmbedMp3Artwork(document, path);
            return Mp3EncoderKind.Windows;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{UiStrings.ErrorWindowsMp3Failed}\n{ex.Message}", ex);
        }
    }

    private static void EmbedMp3Artwork(AudioDocument document, string path)
    {
        if (document.Artwork is { Length: > 0 })
        {
            Id3Artwork.TryWrite(path, document.Artwork);
        }
    }

    public static Mp3EncoderKind? Save(
        AudioDocument document,
        string path,
        Mp3EncodeOptions options,
        Mp3SpeakerMix mix = default)
    {
        var kind = DetectKind(path);
        if (kind == AudioFileKind.Aiff)
        {
            throw new NotSupportedException(UiStrings.ErrAiffExportNotSupported);
        }

        if (kind == AudioFileKind.M4a)
        {
            throw new NotSupportedException(UiStrings.ErrM4aExportNotSupported);
        }

        if (kind == AudioFileKind.Mp3)
        {
            return SaveMp3(document, path, options, mix: mix);
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

        if (bits == 32)
        {
            WriteIeeeFloat(samples, writer, Step);
            return;
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

    private static void WriteIeeeFloat(float[] samples, BinaryWriter writer, Action<int> step)
    {
        const int chunk = 4096;
        var buffer = new byte[chunk * sizeof(float)];
        var offset = 0;
        while (offset < samples.Length)
        {
            var take = Math.Min(chunk, samples.Length - offset);
            Buffer.BlockCopy(samples, offset * sizeof(float), buffer, 0, take * sizeof(float));
            writer.Write(buffer, 0, take * sizeof(float));
            offset += take;
            step(offset);
        }
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
