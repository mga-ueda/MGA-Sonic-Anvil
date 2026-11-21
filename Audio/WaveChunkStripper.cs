using System.Buffers.Binary;
using System.Text;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVE / AIFF から指定 FourCC チャンクを取り除いて書き戻す。
/// PCM は再エンコードせず、チャンク列を組み直す。
/// </summary>
internal static class WaveChunkStripper
{
    public readonly record struct Result(bool Success, string? Error, bool Changed);

    public readonly record struct BatchResult(
        int Succeeded,
        int Changed,
        int Failed,
        string? FirstError,
        IReadOnlyList<string> WrittenPaths);

    /// <summary>
    /// パスごとに取り除く FourCC 集合。
    /// 不可チャンクは無視する（呼び出し側で弾くのが望ましい）。
    /// </summary>
    public static BatchResult StripMany(IReadOnlyDictionary<string, IReadOnlyCollection<string>> plan)
    {
        if (plan.Count == 0)
        {
            return new BatchResult(0, 0, 0, null, Array.Empty<string>());
        }

        var written = new List<string>(plan.Count);
        var succeeded = 0;
        var changed = 0;
        var failed = 0;
        string? firstError = null;

        foreach (var (path, ids) in plan)
        {
            var removable = ids
                .Where(WaveChunkCleanPolicy.CanDelete)
                .ToHashSet(StringComparer.Ordinal);
            if (removable.Count == 0)
            {
                continue;
            }

            var result = StripTo(path, path, removable);
            if (!result.Success)
            {
                failed++;
                firstError ??= result.Error;
                continue;
            }

            succeeded++;
            if (result.Changed)
            {
                changed++;
                written.Add(path);
            }
            else
            {
                written.Add(path);
            }
        }

        return new BatchResult(succeeded, changed, failed, firstError, written);
    }

    public static Result StripTo(string sourcePath, string destinationPath, IReadOnlySet<string> removeIds)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new Result(false, UiStrings.WaveStructureFileMissing, false);
        }

        if (removeIds.Count == 0)
        {
            return new Result(true, null, false);
        }

        try
        {
            var source = File.ReadAllBytes(sourcePath);
            if (!TryStripBytes(source, removeIds, out var rewritten, out var error, out var changed))
            {
                return new Result(false, error ?? UiStrings.WaveChunkStripFailed("unknown"), false);
            }

            if (!changed)
            {
                return new Result(true, null, false);
            }

            var overwrite = PathsEqual(sourcePath, destinationPath);
            if (overwrite)
            {
                WriteOverwrite(sourcePath, rewritten);
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

    internal static bool TryStripBytes(
        ReadOnlySpan<byte> source,
        IReadOnlySet<string> removeIds,
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
        if (container is "RIFF" or "RF64" or "BW64" or "RIFX")
        {
            if (form != "WAVE")
            {
                error = UiStrings.WaveChunkStripUnsupported;
                return false;
            }

            return TryStripRiffWave(source, container, removeIds, out rewritten, out error, out changed);
        }

        if (container == "FORM" && form is "AIFF" or "AIFC")
        {
            return TryStripAiff(source, form, removeIds, out rewritten, out error, out changed);
        }

        error = UiStrings.WaveChunkStripUnsupported;
        return false;
    }

    private static bool TryStripRiffWave(
        ReadOnlySpan<byte> source,
        string container,
        IReadOnlySet<string> removeIds,
        out byte[] rewritten,
        out string? error,
        out bool changed)
    {
        rewritten = Array.Empty<byte>();
        error = null;
        changed = false;
        using var output = new MemoryStream(source.Length);
        WriteFourCc(output, container);
        output.Write(BitConverter.GetBytes(0u));
        WriteFourCc(output, "WAVE");

        var pos = 12;
        var end = source.Length;
        var wroteEssential = false;
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

            if (ShouldRemoveTopLevel(id, removeIds))
            {
                changed = true;
                continue;
            }

            if (id is "LIST" or "list")
            {
                if (!TryFilterListPayload(payload, removeIds, littleEndian: true, out var filtered, out var listChanged))
                {
                    // 子が全部消えて中身がフォーム種別だけ → LIST 自体を落とす。
                    changed = true;
                    continue;
                }

                if (listChanged)
                {
                    changed = true;
                }

                WriteChunkLe(output, id, filtered);
            }
            else
            {
                WriteChunkLe(output, id, payload.ToArray());
            }

            if (id is "fmt " or "data" or "wavl")
            {
                wroteEssential = true;
            }
        }

        if (!wroteEssential)
        {
            error = UiStrings.WaveChunkStripMissingEssential;
            return false;
        }

        var bytes = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        rewritten = bytes;
        return true;
    }

    private static bool TryStripAiff(
        ReadOnlySpan<byte> source,
        string form,
        IReadOnlySet<string> removeIds,
        out byte[] rewritten,
        out string? error,
        out bool changed)
    {
        rewritten = Array.Empty<byte>();
        error = null;
        changed = false;
        using var output = new MemoryStream(source.Length);
        WriteFourCc(output, "FORM");
        WriteUInt32Be(output, 0);
        WriteFourCc(output, form);

        var pos = 12;
        var end = source.Length;
        var wroteComm = false;
        var wroteSsnd = false;
        var wroteFver = form != "AIFC";
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

            if (ShouldRemoveTopLevel(id, removeIds))
            {
                changed = true;
                continue;
            }

            WriteChunkBe(output, id, payload.ToArray());
            if (id == "COMM")
            {
                wroteComm = true;
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

        var bytes = output.ToArray();
        WriteUInt32Be(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        rewritten = bytes;
        return true;
    }

    /// <summary>
    /// LIST をフィルタ。子が残れば true（payload は type+children）。
    /// 子が無くなったら false（LIST ごと削除）。
    /// </summary>
    private static bool TryFilterListPayload(
        ReadOnlySpan<byte> payload,
        IReadOnlySet<string> removeIds,
        bool littleEndian,
        out byte[] filtered,
        out bool changed)
    {
        filtered = Array.Empty<byte>();
        changed = false;
        if (payload.Length < 4)
        {
            filtered = payload.ToArray();
            return true;
        }

        using var body = new MemoryStream(payload.Length);
        body.Write(payload[..4]); // INFO / adtl / wavl 等
        var pos = 4;
        var wroteChild = false;
        while (pos + 8 <= payload.Length)
        {
            var id = ReadFourCc(payload, pos);
            var size = littleEndian
                ? BinaryPrimitives.ReadUInt32LittleEndian(payload[(pos + 4)..])
                : ReadUInt32Be(payload, pos + 4);
            var dataStart = pos + 8;
            if (dataStart + size > payload.Length)
            {
                break;
            }

            var childPayload = payload.Slice(dataStart, (int)size);
            pos = dataStart + (int)size + ((int)size & 1);

            if (removeIds.Contains(id) && WaveChunkCleanPolicy.CanDelete(id))
            {
                changed = true;
                continue;
            }

            if (id is "LIST" or "list")
            {
                if (!TryFilterListPayload(childPayload, removeIds, littleEndian, out var nested, out var nestedChanged))
                {
                    changed = true;
                    continue;
                }

                if (nestedChanged)
                {
                    changed = true;
                }

                if (littleEndian)
                {
                    WriteChunkLe(body, id, nested);
                }
                else
                {
                    WriteChunkBe(body, id, nested);
                }
            }
            else if (littleEndian)
            {
                WriteChunkLe(body, id, childPayload.ToArray());
            }
            else
            {
                WriteChunkBe(body, id, childPayload.ToArray());
            }

            wroteChild = true;
        }

        if (!wroteChild)
        {
            return false;
        }

        filtered = body.ToArray();
        return true;
    }

    private static bool ShouldRemoveTopLevel(string id, IReadOnlySet<string> removeIds) =>
        removeIds.Contains(id) && WaveChunkCleanPolicy.CanDelete(id);

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
