using System.Buffers.Binary;
using System.Text;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVE ヘッダの構造修復。切り詰めチャンクや壊れた LIST/adtl ラベルを直して書き出す。
/// PCM 本体は再エンコードせず、チャンク境界を組み直す。
/// </summary>
internal static class WaveHeaderRepair
{
    public readonly record struct Result(bool Success, string? Error, bool Changed);

    public readonly record struct BatchResult(
        int Succeeded,
        int Changed,
        int Failed,
        string? FirstError,
        IReadOnlyList<string> WrittenPaths);

    /// <summary>ヘッダツリーに修復 UI を出す条件。</summary>
    public static bool NeedsRepair(WaveStructureDocument? document)
    {
        if (document is null
            || document.IsUnsupported
            || document.IsChunkUnion
            || !string.Equals(document.ContainerKind, "WAVE", StringComparison.Ordinal))
        {
            return false;
        }

        return document.HasErrors || HasInvalidNode(document.Roots);
    }

    /// <summary>選択パスのうち修復が必要な WAVE だけ返す（順序は入力順）。</summary>
    public static IReadOnlyList<string> CollectRepairTargets(IEnumerable<string> paths)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path) || !File.Exists(path))
            {
                continue;
            }

            try
            {
                if (NeedsRepair(WaveContainerStructure.TryRead(path)))
                {
                    list.Add(path);
                }
            }
            catch
            {
                // 読めないファイルは対象外。
            }
        }

        return list;
    }

    /// <summary>同フォルダに <c>原名_repaired.wav</c>（重複時は連番）を提案する。</summary>
    public static string SuggestRepairedPath(
        string sourcePath,
        ISet<string>? reserved = null)
    {
        var dir = Path.GetDirectoryName(sourcePath);
        if (string.IsNullOrWhiteSpace(dir))
        {
            dir = ".";
        }

        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "untitled";
        }

        var baseName = stem + UiStrings.WaveHeaderRepairFileSuffix;
        return AudioExport.UniqueInDirectory(
            dir,
            baseName,
            ".wav",
            reserved ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            skipExistingFiles: true);
    }

    /// <summary>複数ファイルを順に修復。別名時は出力名の衝突を避ける。</summary>
    public static BatchResult RepairMany(IReadOnlyList<string> sourcePaths, bool overwrite)
    {
        if (sourcePaths.Count == 0)
        {
            return new BatchResult(0, 0, 0, null, Array.Empty<string>());
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var written = new List<string>(sourcePaths.Count);
        var succeeded = 0;
        var changed = 0;
        var failed = 0;
        string? firstError = null;

        foreach (var sourcePath in sourcePaths)
        {
            string destinationPath;
            try
            {
                destinationPath = overwrite
                    ? sourcePath
                    : SuggestRepairedPath(sourcePath, reserved);
            }
            catch (Exception ex)
            {
                failed++;
                firstError ??= UiStrings.WaveHeaderRepairFailed(ex.Message);
                continue;
            }

            var result = RepairTo(sourcePath, destinationPath);
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
            }

            written.Add(destinationPath);
        }

        return new BatchResult(succeeded, changed, failed, firstError, written);
    }

    public static Result RepairTo(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return new Result(false, UiStrings.WaveStructureFileMissing, false);
        }

        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return new Result(false, UiStrings.WaveHeaderRepairFailed("empty destination"), false);
        }

        try
        {
            var source = File.ReadAllBytes(sourcePath);
            if (!TryRepairBytes(source, out var repaired, out var error, out var changed))
            {
                return new Result(false, error ?? UiStrings.WaveHeaderRepairFailed("unknown"), false);
            }

            var overwrite = PathsEqual(sourcePath, destinationPath);
            if (overwrite)
            {
                WriteOverwrite(sourcePath, repaired);
            }
            else
            {
                var dir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(destinationPath, repaired);
            }

            return new Result(true, null, changed);
        }
        catch (Exception ex)
        {
            return new Result(false, UiStrings.WaveHeaderRepairFailed(ex.Message), false);
        }
    }

    internal static bool TryRepairBytes(
        ReadOnlySpan<byte> source,
        out byte[] repaired,
        out string? error,
        out bool changed)
    {
        repaired = Array.Empty<byte>();
        error = null;
        changed = false;
        if (source.Length < 12)
        {
            error = UiStrings.WaveStructureTooShort;
            return false;
        }

        var container = ReadFourCc(source, 0);
        var form = ReadFourCc(source, 8);
        if (container != "RIFF" || form != "WAVE")
        {
            error = UiStrings.WaveHeaderRepairWaveOnly;
            return false;
        }

        using var output = new MemoryStream(source.Length + 64);
        output.Write(Encoding.ASCII.GetBytes("RIFF"));
        output.Write(BitConverter.GetBytes(0u)); // placeholder
        output.Write(Encoding.ASCII.GetBytes("WAVE"));

        var pos = 12;
        var end = source.Length;
        var wroteAny = false;
        while (pos + 8 <= end)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            var remaining = end - dataStart;

            if (WaveStructureNode.IsBlankFourCc(id))
            {
                // NUL×4 等は落とす。
                changed = true;
                if (declared == 0)
                {
                    pos = dataStart;
                    continue;
                }

                if (declared <= remaining)
                {
                    pos = dataStart + (int)declared + ((int)declared & 1);
                    continue;
                }

                // 以降は再同期を試みる。
                if (!TryResyncTopLevel(source, pos, end, output, ref changed, ref wroteAny))
                {
                    break;
                }

                break;
            }

            if (declared > remaining)
            {
                changed = true;
                if (id is "LIST" or "list")
                {
                    // 切り詰め LIST は残バイトから可能な範囲で再構築。
                    var listBytes = RepairListPayload(source, dataStart, remaining, ref changed);
                    WriteChunk(output, id, listBytes);
                    wroteAny = true;
                    break;
                }

                if (id is "data" or "wavl" or "fmt " or "fact")
                {
                    // 重要チャンクは残バイトへクランプ。
                    var payload = source.Slice(dataStart, remaining).ToArray();
                    WriteChunk(output, id, payload);
                    wroteAny = true;
                    break;
                }

                if (!TryResyncTopLevel(source, pos, end, output, ref changed, ref wroteAny))
                {
                    break;
                }

                break;
            }

            if (id is "LIST" or "list")
            {
                var listPayload = source.Slice(dataStart, (int)declared);
                var repairedList = RepairListPayload(listPayload, 0, listPayload.Length, ref changed);
                if (repairedList.Length != listPayload.Length
                    || !listPayload.SequenceEqual(repairedList))
                {
                    changed = true;
                }

                WriteChunk(output, id, repairedList);
            }
            else
            {
                var payload = source.Slice(dataStart, (int)declared).ToArray();
                WriteChunk(output, id, payload);
            }

            wroteAny = true;
            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        if (!wroteAny)
        {
            error = UiStrings.WaveHeaderRepairFailed("no chunks");
            return false;
        }

        var bytes = output.ToArray();
        var riffSize = (uint)(bytes.Length - 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4, 4), riffSize);

        // 元の RIFF サイズ欄が違えば変更扱い。
        var originalSize = BinaryPrimitives.ReadUInt32LittleEndian(source[4..]);
        if (originalSize != riffSize || bytes.Length != source.Length)
        {
            changed = true;
        }
        else if (!changed && !source.SequenceEqual(bytes))
        {
            changed = true;
        }

        repaired = bytes;
        return true;
    }

    private static byte[] RepairListPayload(
        ReadOnlySpan<byte> source,
        int listDataStart,
        int listSize,
        ref bool changed)
    {
        if (listSize < 4)
        {
            changed = true;
            return Array.Empty<byte>();
        }

        var listType = ReadFourCc(source, listDataStart);
        var listEnd = listDataStart + listSize;
        using var body = new MemoryStream(listSize);
        WriteFourCc(body, listType);

        if (listType is "adtl" or "ADTL")
        {
            RepairAdtlChildren(source, listDataStart + 4, listEnd, body, ref changed);
        }
        else
        {
            RepairGenericListChildren(source, listDataStart + 4, listEnd, body, ref changed);
        }

        return body.ToArray();
    }

    private static void RepairAdtlChildren(
        ReadOnlySpan<byte> source,
        int start,
        int listEnd,
        MemoryStream body,
        ref bool changed)
    {
        var headers = FindAdtlHeaders(source, start, listEnd);
        if (headers.Count == 0)
        {
            // マジックが無ければ通常走査へ。
            RepairGenericListChildren(source, start, listEnd, body, ref changed);
            return;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var offset = headers[i];
            var id = ReadFourCc(source, offset);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(offset + 4)..]);
            var dataStart = offset + 8;
            var nextHeader = i + 1 < headers.Count ? headers[i + 1] : listEnd;
            var maxPayload = Math.Max(0, nextHeader - dataStart);
            var useSize = (int)Math.Min(declared, (uint)maxPayload);
            if (useSize < 0)
            {
                useSize = 0;
            }

            if (useSize != declared || dataStart + useSize > listEnd)
            {
                changed = true;
            }

            // 次ヘッダ直前のパディング 0 をペイロードに含めない。
            if (i + 1 < headers.Count
                && useSize > 0
                && (useSize & 1) == 1
                && dataStart + useSize < nextHeader
                && source[dataStart + useSize] == 0
                && dataStart + useSize + 1 == nextHeader)
            {
                // odd size + pad before next — OK, WriteChunk will pad.
            }
            else if (i + 1 < headers.Count
                     && useSize == maxPayload
                     && useSize > 0
                     && nextHeader < listEnd)
            {
                // 次マジックまでの距離をペイロードにする（非整列の壊れ方を吸収）。
                changed = true;
            }

            var payload = source.Slice(dataStart, useSize).ToArray();
            WriteChunk(body, id, payload);
        }

        // 元と並びが違う／サイズが違う場合 changed は上で立つ。末尾ゴミは落とす。
        if (headers[^1] + 8 < listEnd)
        {
            var lastDeclared = BinaryPrimitives.ReadUInt32LittleEndian(source[(headers[^1] + 4)..]);
            var lastEnd = headers[^1] + 8 + (int)Math.Min(lastDeclared, (uint)(listEnd - headers[^1] - 8));
            if (lastEnd + (lastEnd & 1) < listEnd)
            {
                changed = true;
            }
        }
    }

    private static List<int> FindAdtlHeaders(ReadOnlySpan<byte> source, int start, int listEnd)
    {
        var list = new List<int>();
        for (var i = start; i + 8 <= listEnd; i++)
        {
            var id = ReadFourCc(source, i);
            if (id is "labl" or "note" or "ltxt")
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static void RepairGenericListChildren(
        ReadOnlySpan<byte> source,
        int pos,
        int listEnd,
        MemoryStream body,
        ref bool changed)
    {
        while (pos + 8 <= listEnd)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            if (dataStart + declared > listEnd)
            {
                changed = true;
                break;
            }

            if (id is "LIST" or "list")
            {
                var nested = RepairListPayload(source, dataStart, (int)declared, ref changed);
                WriteChunk(body, id, nested);
            }
            else if (WaveStructureNode.IsBlankFourCc(id))
            {
                changed = true;
            }
            else
            {
                var payload = source.Slice(dataStart, (int)declared).ToArray();
                WriteChunk(body, id, payload);
            }

            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        if (pos < listEnd)
        {
            changed = true;
        }
    }

    private static bool TryResyncTopLevel(
        ReadOnlySpan<byte> source,
        int from,
        int end,
        MemoryStream output,
        ref bool changed,
        ref bool wroteAny)
    {
        for (var i = from + 1; i + 8 <= end; i++)
        {
            var id = ReadFourCc(source, i);
            if (!WaveStructureNode.IsWellKnownChunkId(id) && id is not ("DSpQ" or "FLLR"))
            {
                continue;
            }

            if (WaveStructureNode.IsBlankFourCc(id))
            {
                continue;
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(source[(i + 4)..]);
            var dataStart = i + 8;
            if (dataStart + size > end)
            {
                continue;
            }

            changed = true;
            // ここから通常走査に戻す。
            var pos = i;
            while (pos + 8 <= end)
            {
                var cid = ReadFourCc(source, pos);
                var csize = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
                var cdata = pos + 8;
                if (cdata + csize > end)
                {
                    break;
                }

                if (cid is "LIST" or "list")
                {
                    var listBytes = RepairListPayload(source, cdata, (int)csize, ref changed);
                    WriteChunk(output, cid, listBytes);
                }
                else if (!WaveStructureNode.IsBlankFourCc(cid))
                {
                    WriteChunk(output, cid, source.Slice(cdata, (int)csize).ToArray());
                }
                else
                {
                    changed = true;
                }

                wroteAny = true;
                pos = cdata + (int)csize + ((int)csize & 1);
            }

            return wroteAny;
        }

        return false;
    }

    private static void WriteChunk(Stream output, string id, byte[] payload)
    {
        WriteFourCc(output, id);
        output.Write(BitConverter.GetBytes((uint)payload.Length));
        output.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteFourCc(Stream output, string id)
    {
        var padded = PadFourCc(id);
        Span<byte> buf = stackalloc byte[4];
        for (var i = 0; i < 4; i++)
        {
            buf[i] = (byte)padded[i];
        }

        output.Write(buf);
    }

    private static void WriteOverwrite(string path, byte[] bytes)
    {
        var temp = path + ".anvil-repair.tmp";
        try
        {
            File.WriteAllBytes(temp, bytes);
            try
            {
                File.Replace(temp, path, destinationBackupFileName: null);
            }
            catch (IOException)
            {
                // ロック中などは直接上書きを試す。
                File.Copy(temp, path, overwrite: true);
                TryDelete(temp);
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a),
                Path.GetFullPath(b),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool HasInvalidNode(IReadOnlyList<WaveStructureNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsInvalid)
            {
                return true;
            }

            if (node.Children.Count > 0 && HasInvalidNode(node.Children))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadFourCc(ReadOnlySpan<byte> data, int offset)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            return "????";
        }

        return new string(
        [
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3],
        ]);
    }

    private static string PadFourCc(string id)
    {
        if (id.Length == 4)
        {
            return id;
        }

        if (id.Length > 4)
        {
            return id[..4];
        }

        return id.PadRight(4, ' ');
    }
}
