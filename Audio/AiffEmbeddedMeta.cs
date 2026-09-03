using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>AIFF/AIFC の MARK（マーカー）と INST サステインループ。</summary>
internal static class AiffEmbeddedMeta
{
    public static bool TryRead(string path, out EmbeddedAudioMeta meta)
    {
        meta = EmbeddedAudioMeta.Empty;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (ReadFourCc(reader) != "FORM")
            {
                return false;
            }

            _ = ReadUInt32Be(reader);
            var formType = ReadFourCc(reader);
            if (formType is not ("AIFF" or "AIFC"))
            {
                return false;
            }

            var markers = new Dictionary<short, (long Frame, string Name)>();
            short sustainBegin = 0;
            short sustainEnd = 0;
            short sustainMode = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = ReadUInt32Be(reader);
                var dataStart = stream.Position;
                if (dataStart + chunkSize > stream.Length)
                {
                    break;
                }

                if (chunkId == "MARK")
                {
                    ReadMarkChunk(reader, dataStart, chunkSize, markers);
                }
                else if (chunkId == "INST" && chunkSize >= 20)
                {
                    stream.Position = dataStart + 8;
                    sustainMode = ReadInt16Be(reader);
                    sustainBegin = ReadInt16Be(reader);
                    sustainEnd = ReadInt16Be(reader);
                }

                var padded = chunkSize + (chunkSize & 1);
                stream.Position = dataStart + padded;
            }

            var points = new List<EmbeddedCueMarker>(markers.Count);
            foreach (var pair in markers.Values.OrderBy(item => item.Frame))
            {
                if (pair.Frame < 0)
                {
                    continue;
                }

                points.Add(new EmbeddedCueMarker(pair.Frame, pair.Name.Trim()));
            }

            EmbeddedSampleLoop? loop = null;
            if (sustainMode != 0
                && markers.TryGetValue(sustainBegin, out var begin)
                && markers.TryGetValue(sustainEnd, out var end)
                && end.Frame > begin.Frame)
            {
                loop = new EmbeddedSampleLoop(begin.Frame, end.Frame);
            }

            meta = new EmbeddedAudioMeta(Dedup(points), loop);
            return !meta.IsEmpty;
        }
        catch
        {
            meta = EmbeddedAudioMeta.Empty;
            return false;
        }
    }

    private static void ReadMarkChunk(
        BinaryReader reader,
        long dataStart,
        uint chunkSize,
        Dictionary<short, (long Frame, string Name)> markers)
    {
        if (chunkSize < 2)
        {
            return;
        }

        var end = dataStart + chunkSize;
        var count = ReadUInt16Be(reader);
        for (var i = 0; i < count && reader.BaseStream.Position + 6 <= end; i++)
        {
            var id = ReadInt16Be(reader);
            var frame = ReadUInt32Be(reader);
            var name = ReadPString(reader, end);
            if (id != 0)
            {
                markers[id] = (frame, name);
            }
        }
    }

    private static List<EmbeddedCueMarker> Dedup(List<EmbeddedCueMarker> points)
    {
        if (points.Count <= 1)
        {
            return points;
        }

        var unique = new List<EmbeddedCueMarker>(points.Count);
        foreach (var point in points)
        {
            if (unique.Count == 0 || unique[^1].Frame != point.Frame)
            {
                unique.Add(point);
                continue;
            }

            if (string.IsNullOrWhiteSpace(unique[^1].Comment) && !string.IsNullOrWhiteSpace(point.Comment))
            {
                unique[^1] = point;
            }
        }

        return unique;
    }

    private static string ReadPString(BinaryReader reader, long endExclusive)
    {
        if (reader.BaseStream.Position >= endExclusive)
        {
            return string.Empty;
        }

        var length = reader.ReadByte();
        var take = Math.Min(length, (int)Math.Max(0, endExclusive - reader.BaseStream.Position));
        var bytes = reader.ReadBytes(take);
        if (((1 + length) & 1) != 0 && reader.BaseStream.Position < endExclusive)
        {
            _ = reader.ReadByte();
        }

        return bytes.Length == 0 ? string.Empty : Encoding.Latin1.GetString(bytes);
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static ushort ReadUInt16Be(BinaryReader reader)
    {
        var high = reader.ReadByte();
        var low = reader.ReadByte();
        return (ushort)((high << 8) | low);
    }

    private static short ReadInt16Be(BinaryReader reader) => (short)ReadUInt16Be(reader);

    private static uint ReadUInt32Be(BinaryReader reader)
    {
        var b0 = reader.ReadByte();
        var b1 = reader.ReadByte();
        var b2 = reader.ReadByte();
        var b3 = reader.ReadByte();
        return (uint)((b0 << 24) | (b1 << 16) | (b2 << 8) | b3);
    }
}
