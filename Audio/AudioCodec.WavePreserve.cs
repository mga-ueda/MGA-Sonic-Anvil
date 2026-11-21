using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// WAVE 保存時に元ファイルのチャンクを引き継ぐ。
/// fmt / data / fact と、アプリが書き戻す cue / smpl / LIST-adtl 以外は残す。
/// </summary>
internal static partial class AudioCodec
{
    /// <summary>
    /// SourcePath の RIFF WAVE を雛形に、PCM だけ差し替えて <paramref name="destinationPath"/> へ書く。
    /// 読めない／非対応コンテナなら false（呼び出し側がクリーン書き込みへ）。
    /// </summary>
    private static bool TryWriteWavePreservingChunks(
        AudioDocument document,
        string destinationPath,
        int bits,
        IProgress<double>? progress)
    {
        // 未保存のヘッダ修復／チャンク削除があれば、その作業コピーを雛形にする。
        var sourcePath = document.ResolveWaveContainerPath();
        if (string.IsNullOrWhiteSpace(sourcePath)
            || !File.Exists(sourcePath)
            || DetectKind(sourcePath) != AudioFileKind.Wave)
        {
            return false;
        }

        byte[] source;
        try
        {
            source = File.ReadAllBytes(sourcePath);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        if (source.Length < 12
            || !ReadFourCc(source, 0).Equals("RIFF", StringComparison.Ordinal)
            || !ReadFourCc(source, 8).Equals("WAVE", StringComparison.Ordinal))
        {
            // RF64 / BW64 は別経路。ここでは掃除せずクリーン書き込みへ落とす。
            return false;
        }

        if (!TryReadWaveSampleRate(source, out var sourceRate))
        {
            sourceRate = Math.Max(1, document.SampleRate);
        }

        var channels = Math.Max(1, document.Channels);
        var sampleRate = Math.Max(1, document.SampleRate);
        var blockAlign = channels * (bits / 8);
        var byteRate = sampleRate * blockAlign;
        var frames = document.FrameCount;
        var needFact = bits > 16;

        using var output = new MemoryStream(Math.Max(source.Length, 64 * 1024));
        using var writer = new BinaryWriter(output, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        var pos = 12;
        var end = source.Length;
        var wroteFmt = false;
        var wroteData = false;
        var factPending = needFact;

        while (pos + 8 <= end)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(pos + 4));
            var dataStart = pos + 8;
            if (dataStart + (long)declared > end)
            {
                return false;
            }

            var payloadLen = (int)declared;
            var payload = source.AsSpan(dataStart, payloadLen);
            pos = dataStart + payloadLen + (payloadLen & 1);

            if (id == "fmt ")
            {
                WriteFmtChunk(
                    writer,
                    sampleRate,
                    channels,
                    bits,
                    byteRate,
                    blockAlign,
                    document.ChannelMask);
                wroteFmt = true;
                if (factPending)
                {
                    WriteFactChunk(writer, frames);
                    factPending = false;
                }

                continue;
            }

            if (id == "fact")
            {
                // 新しい fmt に合わせて書き直す（または不要なら落とす）。
                continue;
            }

            if (id == "data")
            {
                WriteDataChunk(document, writer, bits, progress);
                wroteData = true;
                continue;
            }

            // マーカー／ループは WavEmbeddedMeta.Write が末尾へ書き戻す。
            if (id is "cue " or "smpl")
            {
                continue;
            }

            if (id is "LIST" or "list")
            {
                if (payloadLen >= 4
                    && ReadFourCc(payload, 0).Equals("adtl", StringComparison.Ordinal))
                {
                    continue;
                }

                WriteChunk(writer, id, payload);
                continue;
            }

            if (id == "bext")
            {
                WriteChunk(
                    writer,
                    id,
                    AdjustBextTimeReference(payload, sourceRate, sampleRate));
                continue;
            }

            WriteChunk(writer, id, payload);
        }

        if (!wroteFmt)
        {
            WriteFmtChunk(
                writer,
                sampleRate,
                channels,
                bits,
                byteRate,
                blockAlign,
                document.ChannelMask);
            if (factPending)
            {
                WriteFactChunk(writer, frames);
            }
        }

        if (!wroteData)
        {
            WriteDataChunk(document, writer, bits, progress);
        }

        var bytes = output.ToArray();
        if (bytes.Length < 12)
        {
            return false;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), (uint)(bytes.Length - 8));
        WriteWaveBytes(destinationPath, sourcePath, bytes);
        return true;
    }

    private static void WriteFactChunk(BinaryWriter writer, long frames)
    {
        writer.Write(Encoding.ASCII.GetBytes("fact"));
        writer.Write(4);
        writer.Write((int)Math.Clamp(frames, 0, int.MaxValue));
    }

    private static void WriteDataChunk(
        AudioDocument document,
        BinaryWriter writer,
        int bits,
        IProgress<double>? progress)
    {
        writer.Write(Encoding.ASCII.GetBytes("data"));
        var sizePos = writer.BaseStream.Position;
        writer.Write(0);
        WritePcm(document, writer, bits, progress, progressScale: 0.92);
        var dataSize = (int)(writer.BaseStream.Position - sizePos - 4);
        if ((dataSize & 1) != 0)
        {
            writer.Write((byte)0);
        }

        var end = writer.BaseStream.Position;
        writer.BaseStream.Position = sizePos;
        writer.Write(dataSize);
        writer.BaseStream.Position = end;
    }

    private static void WriteChunk(BinaryWriter writer, string id, ReadOnlySpan<byte> payload)
    {
        writer.Write(Encoding.ASCII.GetBytes(id.Length >= 4 ? id[..4] : id.PadRight(4)));
        writer.Write(payload.Length);
        writer.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteWaveBytes(string destinationPath, string sourcePath, byte[] bytes)
    {
        var overwrite = string.Equals(
            Path.GetFullPath(destinationPath),
            Path.GetFullPath(sourcePath),
            StringComparison.OrdinalIgnoreCase);
        if (overwrite)
        {
            using var fs = new FileStream(
                destinationPath,
                FileMode.Open,
                FileAccess.Write,
                FileShare.Read);
            fs.SetLength(0);
            fs.Write(bytes);
            fs.Flush(true);
            return;
        }

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(destinationPath, bytes);
    }

    private static bool TryReadWaveSampleRate(ReadOnlySpan<byte> source, out int sampleRate)
    {
        sampleRate = 0;
        var pos = 12;
        while (pos + 8 <= source.Length)
        {
            var id = ReadFourCc(source, pos);
            var declared = BinaryPrimitives.ReadUInt32LittleEndian(source[(pos + 4)..]);
            var dataStart = pos + 8;
            if (dataStart + (long)declared > source.Length)
            {
                return false;
            }

            if (id == "fmt " && declared >= 8)
            {
                // PCM/extensible: sample rate は offset 4（after wFormatTag+nChannels）
                sampleRate = BinaryPrimitives.ReadInt32LittleEndian(source[(dataStart + 4)..]);
                return sampleRate > 0;
            }

            pos = dataStart + (int)declared + ((int)declared & 1);
        }

        return false;
    }

    /// <summary>
    /// BWF bext の TimeReference（サンプル数）をレート変換に合わせて更新する。
    /// レイアウトが足りない／壊れているときはそのまま返す。
    /// </summary>
    internal static byte[] AdjustBextTimeReference(
        ReadOnlySpan<byte> payload,
        int sourceRate,
        int destRate)
    {
        if (sourceRate < 1
            || destRate < 1
            || sourceRate == destRate
            || payload.Length < 346)
        {
            return payload.ToArray();
        }

        // Description256 + Originator32 + OriginatorReference32 + Date10 + Time8 = 338
        const int timeRefOffset = 338;
        var low = BinaryPrimitives.ReadUInt32LittleEndian(payload[timeRefOffset..]);
        var high = BinaryPrimitives.ReadUInt32LittleEndian(payload[(timeRefOffset + 4)..]);
        var samples = ((ulong)high << 32) | low;
        var scaled = (ulong)Math.Round(samples * (double)destRate / sourceRate);
        var copy = payload.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(timeRefOffset), (uint)(scaled & 0xFFFFFFFFu));
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(timeRefOffset + 4), (uint)(scaled >> 32));
        return copy;
    }

    private static string ReadFourCc(ReadOnlySpan<byte> source, int offset)
    {
        if (offset + 4 > source.Length)
        {
            return string.Empty;
        }

        return Encoding.ASCII.GetString(source.Slice(offset, 4));
    }
}
