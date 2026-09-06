using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>WAV の cue + adtl（単発マーカー / ltxt リージョン）と smpl（サンプルループ）。</summary>
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
            var regionLengths = new Dictionary<uint, uint>();
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
                        ReadAdtlList(reader, dataStart + 4, chunkSize - 4, labels, notes, regionIds, regionLengths);
                    }
                }

                stream.Position = dataStart + chunkSize + (chunkSize & 1);
            }

            meta = new EmbeddedAudioMeta(
                BuildMarkers(cuePositions, labels, notes, regionIds),
                ChooseSampleLoop(smplLoops),
                CollectRegions(cuePositions, regionLengths));
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

    private static IReadOnlyList<EmbeddedRegion> CollectRegions(
        Dictionary<uint, long> cuePositions,
        Dictionary<uint, uint> regionLengths)
    {
        var regions = new List<EmbeddedRegion>();
        foreach (var (cueId, length) in regionLengths)
        {
            if (length == 0 || !cuePositions.TryGetValue(cueId, out var start) || start < 0)
            {
                continue;
            }

            var end = start + length;
            if (end <= start)
            {
                continue;
            }

            var region = new EmbeddedRegion(start, end);
            var exists = false;
            foreach (var item in regions)
            {
                if (item == region)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                regions.Add(region);
            }
        }

        regions.Sort(static (a, b) =>
        {
            var byStart = a.StartFrame.CompareTo(b.StartFrame);
            return byStart != 0 ? byStart : a.EndFrame.CompareTo(b.EndFrame);
        });
        return regions;
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
        HashSet<uint> regionIds,
        Dictionary<uint, uint> regionLengths)
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
                    regionLengths[cueId] = length;
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

    public static void Write(string path, AudioDocument document)
    {
        var points = CollectCuePoints(document.Markers);
        var loop = NormalizeLoop(document.SampleLoop);
        var regionSpans = new List<(uint Id, uint Length)>();
        foreach (var range in document.Regions)
        {
            var span = NormalizeLoop(range);
            if (span is not { } region)
            {
                continue;
            }

            var id = NextCueId(points);
            regionSpans.Add((id, region.InclusiveEnd - region.Start + 1));
            points.Add((id, region.Start, string.Empty));
        }

        if (points.Count == 0 && loop is null)
        {
            return;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (stream.Length < 12)
        {
            throw new InvalidDataException("Wave file is too short to append metadata.");
        }

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = 0;
        if (ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException("Not a RIFF wave file.");
        }

        stream.Position = 8;
        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException("Not a WAVE file.");
        }

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        stream.Position = stream.Length;
        if ((stream.Position & 1) != 0)
        {
            writer.Write((byte)0);
        }

        if (points.Count > 0)
        {
            WriteCueChunk(writer, points);
            WriteAdtlList(writer, points, regionSpans);
        }

        if (loop is { } value)
        {
            WriteSmplChunk(writer, value, document.SampleRate);
        }

        var riffSize = stream.Length - 8;
        if (riffSize > uint.MaxValue)
        {
            throw new InvalidDataException("Wave file exceeds RIFF size limit.");
        }

        stream.Position = 4;
        writer.Write((uint)riffSize);
    }

    private static List<(uint Id, uint Frame, string Comment)> CollectCuePoints(
        IReadOnlyList<WaveMarker> markers)
    {
        var points = new List<(uint Id, uint Frame, string Comment)>();
        if (markers is null || markers.Count == 0)
        {
            return points;
        }

        for (var i = 0; i < markers.Count; i++)
        {
            var marker = markers[i];
            if (marker.Frame < 0 || marker.Frame > uint.MaxValue)
            {
                continue;
            }

            var id = marker.Id > 0 ? (uint)marker.Id : (uint)(i + 1);
            points.Add((id, (uint)marker.Frame, marker.Comment ?? string.Empty));
        }

        return points;
    }

    private static uint NextCueId(List<(uint Id, uint Frame, string Comment)> points)
    {
        uint max = 0;
        foreach (var point in points)
        {
            if (point.Id > max)
            {
                max = point.Id;
            }
        }

        return max + 1;
    }

    private static (uint Start, uint InclusiveEnd)? NormalizeLoop(WaveSelection loop)
    {
        if (loop.IsEmpty || loop.StartFrame < 0 || loop.StartFrame > uint.MaxValue)
        {
            return null;
        }

        var inclusiveEnd = loop.EndFrame - 1;
        if (inclusiveEnd < loop.StartFrame)
        {
            return null;
        }

        if (inclusiveEnd > uint.MaxValue)
        {
            inclusiveEnd = uint.MaxValue;
        }

        return ((uint)loop.StartFrame, (uint)inclusiveEnd);
    }

    private static void WriteCueChunk(
        BinaryWriter writer,
        List<(uint Id, uint Frame, string Comment)> points)
    {
        writer.Write(Encoding.ASCII.GetBytes("cue "));
        writer.Write(4 + points.Count * 24);
        writer.Write(points.Count);
        foreach (var point in points)
        {
            writer.Write(point.Id);
            writer.Write(point.Frame);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(0);
            writer.Write(0);
            writer.Write(point.Frame);
        }
    }

    private static void WriteAdtlList(
        BinaryWriter writer,
        List<(uint Id, uint Frame, string Comment)> points,
        List<(uint Id, uint Length)> regionSpans)
    {
        writer.Write(Encoding.ASCII.GetBytes("LIST"));
        var sizePos = writer.BaseStream.Position;
        writer.Write(0);
        var listStart = writer.BaseStream.Position;
        writer.Write(Encoding.ASCII.GetBytes("adtl"));
        var textEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        foreach (var point in points)
        {
            var text = textEncoding.GetBytes(point.Comment);
            var payload = 4 + text.Length + 1;
            writer.Write(Encoding.ASCII.GetBytes("labl"));
            writer.Write(payload);
            writer.Write(point.Id);
            writer.Write(text);
            writer.Write((byte)0);
            if ((payload & 1) != 0)
            {
                writer.Write((byte)0);
            }
        }

        foreach (var (id, length) in regionSpans)
        {
            if (length == 0)
            {
                continue;
            }

            writer.Write(Encoding.ASCII.GetBytes("ltxt"));
            writer.Write(20);
            writer.Write(id);
            writer.Write(length);
            writer.Write(Encoding.ASCII.GetBytes("rgn "));
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(0);
        }

        var listSize = (int)(writer.BaseStream.Position - listStart);
        var here = writer.BaseStream.Position;
        writer.BaseStream.Position = sizePos;
        writer.Write(listSize);
        writer.BaseStream.Position = here;
        if ((listSize & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteSmplChunk(
        BinaryWriter writer,
        (uint Start, uint InclusiveEnd) loop,
        int sampleRate)
    {
        writer.Write(Encoding.ASCII.GetBytes("smpl"));
        writer.Write(60);
        writer.Write(0);
        writer.Write(0);
        writer.Write(sampleRate > 0 ? (uint)Math.Round(1_000_000_000d / sampleRate) : 0u);
        writer.Write(60);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        writer.Write(0);
        writer.Write(1u);
        writer.Write(0u);
        writer.Write(loop.Start);
        writer.Write(loop.InclusiveEnd);
        writer.Write(0);
        writer.Write(0);
    }
}
