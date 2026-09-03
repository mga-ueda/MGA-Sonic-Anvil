using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>WAV の cue + adtl（単発マーカー）と smpl（サンプルループ）。リージョン（ltxt）は読まない。</summary>
internal static class WavEmbeddedMeta
{
    public static bool TryRead(string path, out EmbeddedAudioMeta meta)
    {
        meta = EmbeddedAudioMeta.Empty;
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
            if (ReadFourCc(reader) != "RIFF")
            {
                return false;
            }

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
            {
                return false;
            }

            var cuePositions = new Dictionary<uint, long>();
            var labels = new Dictionary<uint, string>();
            var notes = new Dictionary<uint, string>();
            var regionIds = new HashSet<uint>();
            var smplLoops = new List<(uint Type, uint Start, uint End)>();

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = reader.ReadUInt32();
                var dataStart = stream.Position;
                if (dataStart + chunkSize > stream.Length)
                {
                    break;
                }

                if (chunkId == "cue ")
                {
                    ReadCueChunk(reader, dataStart, chunkSize, cuePositions);
                }
                else if (chunkId == "smpl")
                {
                    ReadSmplChunk(reader, dataStart, chunkSize, smplLoops);
                }
                else if (chunkId == "LIST" && chunkSize >= 4)
                {
                    var listType = ReadFourCc(reader);
                    if (listType == "adtl")
                    {
                        ReadAdtlList(reader, dataStart + 4, chunkSize - 4, labels, notes, regionIds);
                    }
                }

                stream.Position = dataStart + chunkSize + (chunkSize & 1);
            }

            meta = new EmbeddedAudioMeta(
                BuildMarkers(cuePositions, labels, notes, regionIds),
                ChooseSampleLoop(smplLoops));
            return !meta.IsEmpty;
        }
        catch
        {
            meta = EmbeddedAudioMeta.Empty;
            return false;
        }
    }

    private static IReadOnlyList<EmbeddedCueMarker> BuildMarkers(
        Dictionary<uint, long> cuePositions,
        Dictionary<uint, string> labels,
        Dictionary<uint, string> notes,
        HashSet<uint> regionIds)
    {
        var points = new List<EmbeddedCueMarker>(cuePositions.Count);
        foreach (var (cueId, frame) in cuePositions)
        {
            if (regionIds.Contains(cueId) || frame < 0)
            {
                continue;
            }

            notes.TryGetValue(cueId, out var note);
            labels.TryGetValue(cueId, out var label);
            var comment = !string.IsNullOrWhiteSpace(note) ? note.Trim() : (label ?? string.Empty).Trim();
            points.Add(new EmbeddedCueMarker(frame, comment));
        }

        points.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return DedupFrames(points);
    }

    private static EmbeddedSampleLoop? ChooseSampleLoop(List<(uint Type, uint Start, uint End)> loops)
    {
        (uint Type, uint Start, uint End)? chosen = null;
        foreach (var loop in loops)
        {
            if (loop.End < loop.Start)
            {
                continue;
            }

            if (chosen is null || (chosen.Value.Type != 0 && loop.Type == 0))
            {
                chosen = loop;
                if (loop.Type == 0)
                {
                    break;
                }
            }
        }

        if (chosen is not { } value)
        {
            return null;
        }

        // RIFF smpl の dwEnd はループ最終サンプル（inclusive）。WaveSelection は終端 exclusive。
        var exclusiveEnd = value.End == uint.MaxValue ? long.MaxValue : value.End + 1L;
        return new EmbeddedSampleLoop(value.Start, exclusiveEnd);
    }

    private static List<EmbeddedCueMarker> DedupFrames(List<EmbeddedCueMarker> points)
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

    private static void ReadCueChunk(
        BinaryReader reader,
        long dataStart,
        uint chunkSize,
        Dictionary<uint, long> cuePositions)
    {
        if (chunkSize < 4)
        {
            return;
        }

        var count = reader.ReadUInt32();
        for (uint i = 0; i < count; i++)
        {
            if (dataStart + chunkSize - reader.BaseStream.Position < 24)
            {
                break;
            }

            var cueId = reader.ReadUInt32();
            var position = reader.ReadUInt32();
            var fccChunk = ReadFourCc(reader);
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            var sampleOffset = reader.ReadUInt32();
            long sample = fccChunk == "data" ? sampleOffset : position;
            if (fccChunk == "data" && sampleOffset == 0 && position != 0)
            {
                sample = position;
            }

            cuePositions[cueId] = sample;
        }
    }

    private static void ReadSmplChunk(
        BinaryReader reader,
        long dataStart,
        uint chunkSize,
        List<(uint Type, uint Start, uint End)> loops)
    {
        if (chunkSize < 44)
        {
            return;
        }

        reader.BaseStream.Position = dataStart + 28;
        var loopCount = reader.ReadUInt32();
        var samplerDataSize = reader.ReadUInt32();
        var loopsEnd = dataStart + chunkSize - samplerDataSize;
        for (uint i = 0; i < loopCount; i++)
        {
            if (reader.BaseStream.Position + 24 > loopsEnd)
            {
                break;
            }

            _ = reader.ReadUInt32();
            var type = reader.ReadUInt32();
            var start = reader.ReadUInt32();
            var end = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            _ = reader.ReadUInt32();
            loops.Add((type, start, end));
        }
    }

    private static void ReadAdtlList(
        BinaryReader reader,
        long listStart,
        uint listSize,
        Dictionary<uint, string> labels,
        Dictionary<uint, string> notes,
        HashSet<uint> regionIds)
    {
        var stream = reader.BaseStream;
        var listEnd = listStart + listSize;
        stream.Position = listStart;
        while (stream.Position + 8 <= listEnd)
        {
            var subId = ReadFourCc(reader);
            var subSize = reader.ReadUInt32();
            var subStart = stream.Position;
            if (subStart + subSize > listEnd)
            {
                break;
            }

            if (subId == "labl" && subSize >= 4)
            {
                labels[reader.ReadUInt32()] = ReadZString(reader, subStart + subSize);
            }
            else if (subId == "note" && subSize >= 4)
            {
                notes[reader.ReadUInt32()] = ReadZString(reader, subStart + subSize);
            }
            else if (subId == "ltxt" && subSize >= 20)
            {
                var cueId = reader.ReadUInt32();
                var length = reader.ReadUInt32();
                if (length > 0)
                {
                    regionIds.Add(cueId);
                }
            }

            stream.Position = subStart + subSize + (subSize & 1);
        }
    }

    private static string ReadZString(BinaryReader reader, long endExclusive)
    {
        var bytes = new List<byte>();
        while (reader.BaseStream.Position < endExclusive)
        {
            var value = reader.ReadByte();
            if (value == 0)
            {
                break;
            }

            bytes.Add(value);
        }

        if (bytes.Count == 0)
        {
            return string.Empty;
        }

        var raw = bytes.ToArray();
        try
        {
            return new UTF8Encoding(false, true).GetString(raw);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Default.GetString(raw);
        }
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));
}
