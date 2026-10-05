using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>MOV / MP4 の映像 fourcc。MJPEG / Photo JPEG 以外はプロキシが必要。</summary>
internal static class VideoCodecProbe
{
    public static bool CanPlayWithoutProxy(string path)
    {
        if (!TryReadVideoFourCcs(path, out var codecs) || codecs.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < codecs.Count; i++)
        {
            if (!IsDirectPlayFamily(codecs[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsDirectPlayFamily(string? fourcc)
    {
        if (string.IsNullOrWhiteSpace(fourcc) || fourcc.Length < 4)
        {
            return false;
        }

        var tag = fourcc.Length == 4 ? fourcc : fourcc[..4];
        return tag.Equals("jpeg", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpg", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpa", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("mjpb", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("dmb1", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryReadVideoFourCcs(string path, out IReadOnlyList<string> fourccs)
    {
        fourccs = [];
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var found = new List<string>();
            Walk(stream, 0, stream.Length, found, inMoov: false);
            if (found.Count == 0)
            {
                return false;
            }

            fourccs = found;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static void Walk(Stream stream, long start, long end, List<string> found, bool inMoov)
    {
        var offset = start;
        var header = new byte[16];
        while (offset + 8 <= end)
        {
            if (!TryReadBox(stream, offset, end, header, out var size, out var type, out var headerSize))
            {
                return;
            }

            var payloadStart = offset + headerSize;
            var next = offset + size;
            if (next > end || next < payloadStart)
            {
                return;
            }

            if (type == "moov" || type == "trak" || type == "mdia" || type == "minf" || type == "stbl")
            {
                Walk(stream, payloadStart, next, found, inMoov: true);
            }
            else if (inMoov && type == "stsd")
            {
                ReadStsd(stream, payloadStart, next, found);
            }

            if (size < 8)
            {
                return;
            }

            offset = next;
        }
    }

    private static void ReadStsd(Stream stream, long start, long end, List<string> found)
    {
        if (end - start < 8)
        {
            return;
        }

        stream.Position = start + 4;
        var countBuf = new byte[4];
        if (stream.Read(countBuf, 0, 4) != 4)
        {
            return;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(countBuf);
        var offset = start + 8;
        var header = new byte[8];
        for (var i = 0; i < count && offset + 8 <= end; i++)
        {
            stream.Position = offset;
            if (stream.Read(header, 0, 8) != 8)
            {
                return;
            }

            var size = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
            if (size < 8)
            {
                return;
            }

            var fourcc = Encoding.ASCII.GetString(header, 4, 4);
            if (!IsHintOrMetaSample(fourcc))
            {
                found.Add(fourcc);
            }

            offset += size;
        }
    }

    private static bool IsHintOrMetaSample(string fourcc) =>
        fourcc.Equals("tmcd", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("mp4a", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("lpcm", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("sowt", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("twos", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("in24", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("in32", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("fl32", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("fl64", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("raw ", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("ulaw", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("alaw", StringComparison.OrdinalIgnoreCase)
        || fourcc.Equals("aac ", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBox(
        Stream stream,
        long offset,
        long end,
        byte[] header,
        out long size,
        out string type,
        out int headerSize)
    {
        size = 0;
        type = string.Empty;
        headerSize = 8;
        stream.Position = offset;
        if (stream.Read(header, 0, 8) != 8)
        {
            return false;
        }

        var size32 = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(0, 4));
        type = Encoding.ASCII.GetString(header, 4, 4);
        if (size32 == 1)
        {
            if (stream.Read(header, 8, 8) != 8)
            {
                return false;
            }

            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8, 8));
            headerSize = 16;
        }
        else if (size32 == 0)
        {
            size = end - offset;
        }
        else
        {
            size = size32;
        }

        return size >= headerSize && offset + size <= end;
    }
}
