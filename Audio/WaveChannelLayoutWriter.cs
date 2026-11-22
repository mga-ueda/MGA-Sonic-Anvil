using System.Buffers.Binary;
using System.Text;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVE fmt の dwChannelMask / AIFF CHAN を差し替えて書き戻す。
/// PCM 本体は触らない。
/// </summary>
internal static class WaveChannelLayoutWriter
{
    public readonly record struct Result(bool Success, string? Error, bool Changed);

    public readonly record struct BatchResult(
        int Succeeded,
        int Changed,
        int Failed,
        string? FirstError,
        IReadOnlyList<string> WrittenPaths);

    public readonly record struct TargetInfo(string Path, int Channels, int CurrentMask, string Kind);

    public static bool IsSupportedPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var kind = AudioCodec.DetectKind(path);
        return kind is AudioFileKind.Wave or AudioFileKind.Aiff;
    }

    /// <summary>
    /// マスク 0 は全対象。非 0 はビット数＝チャンネル数のファイルだけ。
    /// </summary>
    public static IReadOnlyList<TargetInfo> FilterTargetsForMask(
        IEnumerable<TargetInfo> targets,
        int channelMask)
    {
        var list = targets as IList<TargetInfo> ?? targets.ToList();
        if (channelMask == 0)
        {
            return list is IReadOnlyList<TargetInfo> ro ? ro : list.ToArray();
        }

        var bits = ChannelMaskBits.PopCount(channelMask);
        return list.Where(t => t.Channels == bits).ToArray();
    }

    public static bool TryDescribe(string path, out TargetInfo info)
    {
        info = default;
        if (!IsSupportedPath(path))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (!TryReadChannelsAndMask(bytes, out var channels, out var mask, out var kind))
            {
                return false;
            }

            info = new TargetInfo(path, channels, mask, kind);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static BatchResult ApplyMany(IReadOnlyList<string> paths, int channelMask)
    {
        if (paths.Count == 0)
        {
            return new BatchResult(0, 0, 0, null, Array.Empty<string>());
        }

        var written = new List<string>(paths.Count);
        var succeeded = 0;
        var changed = 0;
        var failed = 0;
        string? firstError = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            if (!IsSupportedPath(path))
            {
                continue;
            }

            var result = ApplyTo(path, path, channelMask);
            if (!result.Success)
            {
                failed++;
                firstError ??= result.Error;
                continue;
            }

            succeeded++;
            written.Add(path);
            if (result.Changed)
            {
                changed++;
            }
        }

        return new BatchResult(succeeded, changed, failed, firstError, written);
    }

    public static Result ApplyTo(string sourcePath, string destinationPath, int channelMask)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new Result(false, UiStrings.WaveStructureFileMissing, false);
        }

        try
        {
            var source = File.ReadAllBytes(sourcePath);
            if (!TryApplyBytes(source, channelMask, out var rewritten, out var error, out var changed))
            {
                return new Result(false, error ?? UiStrings.WaveChunkStripFailed("unknown"), false);
            }

            if (!changed)
            {
                if (!PathsEqual(sourcePath, destinationPath))
                {
                    var dir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrWhiteSpace(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.WriteAllBytes(destinationPath, source);
                }

                return new Result(true, null, false);
            }

            if (PathsEqual(sourcePath, destinationPath))
            {
                WriteOverwrite(destinationPath, rewritten);
            }
            else
            {
                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(destinationPath, rewritten);
            }

            return new Result(true, null, true);
        }
        catch (Exception ex)
        {
            return new Result(false, UiStrings.WaveChunkStripFailed(ex.Message), false);
        }
    }

    internal static bool TryApplyBytes(
        ReadOnlySpan<byte> source,
        int channelMask,
        out byte[] rewritten,
        out string? error,
        out bool changed)
    {
        rewritten = Array.Empty<byte>();
        error = null;
        changed = false;
        if (source.Length < 12)
        {
            error = UiStrings.WaveStructureTooShort;
            return false;
        }

        var container = ReadFourCc(source, 0);
        var form = ReadFourCc(source, 8);
        if (container == "RIFF" && form == "WAVE")
        {
            return TryApplyWave(source, channelMask, out rewritten, out error, out changed);
        }

        if (container == "FORM" && form is "AIFF" or "AIFC")
        {
            return TryApplyAiff(source, form, channelMask, out rewritten, out error, out changed);
        }

        error = UiStrings.WaveChunkStripUnsupported;
        return false;
    }

    private static bool TryApplyWave(
        ReadOnlySpan<byte> source,
        int channelMask,
        out byte[] rewritten,
        out string? error,
        out bool changed)
    {
        rewritten = Array.Empty<byte>();
        error = null;
        changed = false;
        if (!TryReadWaveFmt(source, out var sampleRate, out var channels, out var bits, out var currentMask))
        {
            error = UiStrings.WaveStructureFmtTooSmall;
            return false;
        }

        var extensible = IsWaveExtensibleFmt(source);
        var needsRewrite = currentMask != channelMask
            || (channelMask != 0 && !extensible)
            || (channelMask == 0 && bits <= 16 && extensible);
        if (!needsRewrite)
        {
            rewritten = source.ToArray();
            return true;
        }

        using var output = new MemoryStream(source.Length + 64);
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        var pos = 12;
        var end = source.Length;
        var wroteFmt = false;
        var wroteData = false;
        while (pos + 8 <= end)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            if (dataStart + declared > end)
            {
                error = UiStrings.WaveChunkStripTruncated;
                return false;
            }

            var payload = source.Slice(dataStart, (int)declared);
            pos = dataStart + (int)declared + ((int)declared & 1);

            if (id == "fmt ")
            {
                AudioCodec.WriteFmtChunkForLayout(writer, sampleRate, channels, bits, channelMask);
                wroteFmt = true;
                continue;
            }

            WriteChunkLe(output, id, payload.ToArray());
            if (id is "data" or "wavl")
            {
                wroteData = true;
            }
        }

        if (!wroteFmt || !wroteData)
        {
            error = UiStrings.WaveChunkStripMissingEssential;
            return false;
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        rewritten = bytes;
        changed = true;
        return true;
    }

    private static bool IsWaveExtensibleFmt(ReadOnlySpan<byte> source)
    {
        var pos = 12;
        while (pos + 8 <= source.Length)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            if (dataStart + declared > source.Length)
            {
                return false;
            }

            if (id == "fmt " && declared >= 40)
            {
                var tag = BinaryPrimitives.ReadUInt16LittleEndian(source[dataStart..]);
                return tag == 0xFFFE;
            }

            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        return false;
    }

    private static bool TryApplyAiff(
        ReadOnlySpan<byte> source,
        string form,
        int channelMask,
        out byte[] rewritten,
        out string? error,
        out bool changed)
    {
        rewritten = Array.Empty<byte>();
        error = null;
        changed = false;

        using var output = new MemoryStream(source.Length + 32);
        WriteFourCc(output, "FORM");
        WriteUInt32Be(output, 0);
        WriteFourCc(output, form);

        var pos = 12;
        var end = source.Length;
        var wroteComm = false;
        var wroteSsnd = false;
        var wroteFver = form != "AIFC";
        var wroteChan = false;
        var currentMask = 0;
        var hadChan = false;

        while (pos + 8 <= end)
        {
            var id = ReadFourCc(source, pos);
            var declared = ReadUInt32Be(source, pos + 4);
            var dataStart = pos + 8;
            if (dataStart + declared > end)
            {
                error = UiStrings.WaveChunkStripTruncated;
                return false;
            }

            var payload = source.Slice(dataStart, (int)declared);
            pos = dataStart + (int)declared + ((int)declared & 1);

            if (id == "CHAN")
            {
                hadChan = true;
                if (declared >= 12)
                {
                    var tag = ReadUInt32Be(payload, 0);
                    if (tag == ChannelMaskBits.UseChannelBitmapTag)
                    {
                        currentMask = (int)ReadUInt32Be(payload, 4);
                    }
                }

                // 旧 CHAN は落とす。新値は COMM の直後へ1つだけ書く。
                continue;
            }

            WriteChunkBe(output, id, payload.ToArray());
            if (id == "COMM")
            {
                wroteComm = true;
                if (channelMask != 0 && !wroteChan)
                {
                    WriteChunkBe(output, "CHAN", BuildAiffChanPayload(channelMask));
                    wroteChan = true;
                }
            }
            else if (id == "SSND")
            {
                wroteSsnd = true;
            }
            else if (id == "FVER")
            {
                wroteFver = true;
            }
        }

        if (!wroteComm || !wroteSsnd || !wroteFver)
        {
            error = UiStrings.WaveChunkStripMissingEssential;
            return false;
        }

        if (channelMask != 0 && !wroteChan)
        {
            WriteChunkBe(output, "CHAN", BuildAiffChanPayload(channelMask));
            wroteChan = true;
        }

        changed = channelMask == 0
            ? hadChan
            : !hadChan || currentMask != channelMask;

        var bytes = output.ToArray();
        WriteUInt32Be(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        rewritten = changed ? bytes : source.ToArray();
        return true;
    }

    private static byte[] BuildAiffChanPayload(int channelMask)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(0), ChannelMaskBits.UseChannelBitmapTag);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(4), (uint)channelMask);
        BinaryPrimitives.WriteUInt32BigEndian(payload.AsSpan(8), 0);
        return payload;
    }

    private static bool TryReadChannelsAndMask(
        ReadOnlySpan<byte> source,
        out int channels,
        out int mask,
        out string kind)
    {
        channels = 0;
        mask = 0;
        kind = string.Empty;
        if (source.Length < 12)
        {
            return false;
        }

        var container = ReadFourCc(source, 0);
        var form = ReadFourCc(source, 8);
        if (container == "RIFF" && form == "WAVE")
        {
            if (!TryReadWaveFmt(source, out _, out channels, out _, out mask))
            {
                return false;
            }

            kind = "WAVE";
            return true;
        }

        if (container == "FORM" && form is "AIFF" or "AIFC")
        {
            if (!TryReadAiffChannelsAndMask(source, out channels, out mask))
            {
                return false;
            }

            kind = form;
            return true;
        }

        return false;
    }

    private static bool TryReadWaveFmt(
        ReadOnlySpan<byte> source,
        out int sampleRate,
        out int channels,
        out int bits,
        out int channelMask)
    {
        sampleRate = 0;
        channels = 0;
        bits = 0;
        channelMask = 0;
        var pos = 12;
        while (pos + 8 <= source.Length)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            if (dataStart + declared > source.Length)
            {
                return false;
            }

            if (id == "fmt " && declared >= 16)
            {
                channels = BinaryPrimitives.ReadUInt16LittleEndian(source[(dataStart + 2)..]);
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(source[(dataStart + 4)..]);
                bits = BinaryPrimitives.ReadUInt16LittleEndian(source[(dataStart + 14)..]);
                if (declared >= 40)
                {
                    var tag = BinaryPrimitives.ReadUInt16LittleEndian(source[dataStart..]);
                    if (tag == 0xFFFE)
                    {
                        channelMask = BinaryPrimitives.ReadInt32LittleEndian(source[(dataStart + 20)..]);
                    }
                }

                return channels > 0 && sampleRate > 0 && bits > 0;
            }

            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        return false;
    }

    private static bool TryReadAiffChannelsAndMask(
        ReadOnlySpan<byte> source,
        out int channels,
        out int mask)
    {
        channels = 0;
        mask = 0;
        var pos = 12;
        var foundComm = false;
        while (pos + 8 <= source.Length)
        {
            var id = ReadFourCc(source, pos);
            var declared = ReadUInt32Be(source, pos + 4);
            var dataStart = pos + 8;
            if (dataStart + declared > source.Length)
            {
                return false;
            }

            var payload = source.Slice(dataStart, (int)declared);
            if (id == "COMM" && declared >= 2)
            {
                channels = BinaryPrimitives.ReadInt16BigEndian(payload);
                foundComm = channels > 0;
            }
            else if (id == "CHAN" && declared >= 12)
            {
                var tag = ReadUInt32Be(payload, 0);
                if (tag == ChannelMaskBits.UseChannelBitmapTag)
                {
                    mask = (int)ReadUInt32Be(payload, 4);
                }
            }

            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        return foundComm;
    }

    private static void WriteChunkLe(Stream output, string id, byte[] payload)
    {
        WriteFourCc(output, id);
        output.Write(BitConverter.GetBytes((uint)payload.Length));
        output.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteChunkBe(Stream output, string id, byte[] payload)
    {
        WriteFourCc(output, id);
        WriteUInt32Be(output, (uint)payload.Length);
        output.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static string ReadFourCc(ReadOnlySpan<byte> source, int offset)
    {
        if (offset + 4 > source.Length)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(source.Slice(offset, 4));
    }

    private static void WriteFourCc(Stream output, string id)
    {
        var bytes = Encoding.ASCII.GetBytes(id.Length >= 4 ? id[..4] : id.PadRight(4));
        output.Write(bytes);
    }

    private static uint ReadUInt32Be(ReadOnlySpan<byte> source, int offset) =>
        BinaryPrimitives.ReadUInt32BigEndian(source[offset..]);

    private static void WriteUInt32Be(Stream output, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        output.Write(buf);
    }

    private static void WriteUInt32Be(Span<byte> dest, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(dest, value);

    private static bool PathsEqual(string a, string b) =>
        string.Equals(
            Path.GetFullPath(a),
            Path.GetFullPath(b),
            StringComparison.OrdinalIgnoreCase);

    private static void WriteOverwrite(string path, byte[] bytes)
    {
        using var fs = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Write,
            FileShare.Read);
        fs.SetLength(0);
        fs.Write(bytes);
        fs.Flush(true);
    }
}
