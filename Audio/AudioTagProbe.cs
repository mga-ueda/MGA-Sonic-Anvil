using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>PCM を展開せず、タグとフォーマット情報だけ読む。</summary>
internal static class AudioTagProbe
{
    public static void Ensure(AudioDocument document)
    {
        if (document.Tags.Probed)
        {
            return;
        }

        if (document.SourcePath is { Length: > 0 } path
            && File.Exists(path)
            && TryRead(path, out var tags))
        {
            document.ApplyTags(tags);
            return;
        }

        document.ApplyTags(AudioFileTags.Empty);
    }

    public static bool TryRead(string path, out AudioFileTags tags)
    {
        tags = AudioFileTags.Empty;
        try
        {
            var kind = AudioCodec.DetectKind(path);
            if (kind == AudioFileKind.M4a)
            {
                return TryReadM4a(path, out tags);
            }

            using var stream = File.OpenRead(path);
            var builder = new AudioFileTagsBuilder();
            if (kind == AudioFileKind.Mp3)
            {
                ReadMp3(stream, builder);
            }
            else if (kind == AudioFileKind.Aiff)
            {
                ReadAiff(stream, builder);
            }
            else
            {
                ReadWave(stream, builder);
            }

            tags = builder.ToTags();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
    }

    private static bool TryReadM4a(string path, out AudioFileTags tags)
    {
        tags = AudioFileTags.Empty;
        if (!AudioCodec.TryProbeStreamFormat(path, out var rate, out var channels, out var bits, out var frames))
        {
            return false;
        }

        var builder = new AudioFileTagsBuilder
        {
            SampleRate = rate,
            Channels = channels,
            BitsPerSample = bits,
            DurationSeconds = rate > 0 ? frames / (double)rate : 0,
        };
        if (builder.DurationSeconds > 0)
        {
            builder.BitRateKbps = (int)Math.Round(new FileInfo(path).Length * 8d / builder.DurationSeconds / 1000d);
        }

        tags = builder.ToTags();
        return true;
    }

    internal static int ParseLeadingInt(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var start = i;
        while (i < text.Length && char.IsDigit(text[i]))
        {
            i++;
        }

        if (i == start)
        {
            return 0;
        }

        return int.TryParse(text.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;
    }

    private static void ReadMp3(Stream stream, AudioFileTagsBuilder builder)
    {
        if (Id3Artwork.TryReadTag(stream, out var version, out var frames, includePictureData: false))
        {
            Id3Artwork.FillTags(version, frames, builder);
        }

        ReadMpegAudio(stream, builder);
        if (builder.Title.Length == 0
            || builder.Artist.Length == 0
            || builder.Album.Length == 0
            || builder.Year.Length == 0)
        {
            ReadId3V1(stream, builder);
        }
    }

    private static void ReadMpegAudio(Stream stream, AudioFileTagsBuilder builder)
    {
        var start = stream.Position;
        var header = FindMpegHeader(stream);
        if (header is null)
        {
            stream.Position = Math.Min(start, stream.Length);
            return;
        }

        var (sampleRate, channels, bitRate, frameBytes, samplesPerFrame) = header.Value;
        builder.SampleRate = sampleRate;
        builder.Channels = channels;
        builder.BitsPerSample = 16;
        if (builder.BitRateKbps <= 0)
        {
            builder.BitRateKbps = bitRate;
        }

        if (TryReadXing(stream, channels, sampleRate >= 32000, out var frames, out var bytes))
        {
            if (frames > 0 && sampleRate > 0)
            {
                builder.DurationSeconds = frames * (double)samplesPerFrame / sampleRate;
            }

            if (bytes > 0 && builder.DurationSeconds > 0)
            {
                builder.BitRateKbps = (int)Math.Round(bytes * 8d / builder.DurationSeconds / 1000d);
            }
        }
        else if (builder.DurationSeconds <= 0 && bitRate > 0)
        {
            var audioBytes = Math.Max(0, stream.Length - start);
            builder.DurationSeconds = audioBytes * 8d / (bitRate * 1000d);
        }

        _ = frameBytes;
        stream.Position = start;
    }

    private static (int SampleRate, int Channels, int BitRate, int FrameBytes, int SamplesPerFrame)? FindMpegHeader(
        Stream stream)
    {
        var limit = Math.Min(stream.Length, stream.Position + 8192);
        var previous = -1;
        while (stream.Position < limit)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return null;
            }

            if (previous == 0xFF && (b & 0xE0) == 0xE0)
            {
                var rest = new byte[2];
                if (stream.Read(rest, 0, 2) != 2)
                {
                    return null;
                }

                var header = (uint)((0xFF << 24) | (b << 16) | (rest[0] << 8) | rest[1]);
                if (TryParseMpegHeader(header, out var parsed))
                {
                    stream.Position -= 4;
                    return parsed;
                }

                stream.Position -= 2;
            }

            previous = b;
        }

        return null;
    }

    private static bool TryParseMpegHeader(
        uint header,
        out (int SampleRate, int Channels, int BitRate, int FrameBytes, int SamplesPerFrame) parsed)
    {
        parsed = default;
        var versionBits = (int)((header >> 19) & 3);
        var layerBits = (int)((header >> 17) & 3);
        var bitrateIndex = (int)((header >> 12) & 0xF);
        var rateIndex = (int)((header >> 10) & 3);
        var padding = (int)((header >> 9) & 1);
        var channelMode = (int)((header >> 6) & 3);
        if (versionBits == 1 || layerBits == 0 || bitrateIndex is 0 or 15 || rateIndex == 3)
        {
            return false;
        }

        var mpeg1 = versionBits == 3;
        var mpeg25 = versionBits == 0;
        var layer3 = layerBits == 1;
        var bitRate = MpegBitRate(mpeg1, layerBits, bitrateIndex);
        var sampleRate = MpegSampleRate(mpeg1, mpeg25, rateIndex);
        if (bitRate <= 0 || sampleRate <= 0)
        {
            return false;
        }

        var channels = channelMode == 3 ? 1 : 2;
        var samplesPerFrame = layer3 ? (mpeg1 ? 1152 : 576) : (layerBits == 3 ? 384 : 1152);
        var frameBytes = layerBits == 3
            ? (12 * bitRate * 1000 / sampleRate + padding) * 4
            : 144 * bitRate * 1000 / sampleRate + padding;
        if (frameBytes < 24)
        {
            return false;
        }

        parsed = (sampleRate, channels, bitRate, frameBytes, samplesPerFrame);
        return true;
    }

    private static int MpegBitRate(bool mpeg1, int layerBits, int index)
    {
        ReadOnlySpan<int> mpeg1l3 = [0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0];
        ReadOnlySpan<int> mpeg2l3 = [0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0];
        ReadOnlySpan<int> mpeg1l2 = [0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, 0];
        ReadOnlySpan<int> mpeg1l1 = [0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, 0];
        if (mpeg1)
        {
            return layerBits switch
            {
                1 => mpeg1l3[index],
                2 => mpeg1l2[index],
                _ => mpeg1l1[index],
            };
        }

        return layerBits == 3 ? mpeg1l1[index] / 2 : mpeg2l3[index];
    }

    private static int MpegSampleRate(bool mpeg1, bool mpeg25, int index)
    {
        ReadOnlySpan<int> rates1 = [44100, 48000, 32000];
        ReadOnlySpan<int> rates2 = [22050, 24000, 16000];
        ReadOnlySpan<int> rates25 = [11025, 12000, 8000];
        if (mpeg1)
        {
            return rates1[index];
        }

        return mpeg25 ? rates25[index] : rates2[index];
    }

    private static bool TryReadXing(Stream stream, int channels, bool mpeg1, out int frames, out int bytes)
    {
        frames = 0;
        bytes = 0;
        var side = mpeg1 ? (channels == 1 ? 17 : 32) : (channels == 1 ? 9 : 17);
        var origin = stream.Position;
        stream.Position = origin + 4 + side;
        var tag = new byte[4];
        if (stream.Read(tag, 0, 4) != 4)
        {
            stream.Position = origin;
            return false;
        }

        var id = Encoding.ASCII.GetString(tag);
        if (id is not ("Xing" or "Info"))
        {
            stream.Position = origin;
            return false;
        }

        var flagsBytes = new byte[4];
        if (stream.Read(flagsBytes, 0, 4) != 4)
        {
            stream.Position = origin;
            return false;
        }

        var flags = BinaryPrimitives.ReadInt32BigEndian(flagsBytes);
        if ((flags & 1) != 0)
        {
            var count = new byte[4];
            if (stream.Read(count, 0, 4) != 4)
            {
                stream.Position = origin;
                return false;
            }

            frames = BinaryPrimitives.ReadInt32BigEndian(count);
        }

        if ((flags & 2) != 0)
        {
            var size = new byte[4];
            if (stream.Read(size, 0, 4) != 4)
            {
                stream.Position = origin;
                return false;
            }

            bytes = BinaryPrimitives.ReadInt32BigEndian(size);
        }

        stream.Position = origin;
        return frames > 0 || bytes > 0;
    }

    private static void ReadId3V1(Stream stream, AudioFileTagsBuilder builder)
    {
        if (stream.Length < 128)
        {
            return;
        }

        var restore = stream.Position;
        stream.Position = stream.Length - 128;
        var block = new byte[128];
        if (stream.Read(block, 0, 128) != 128
            || block[0] != (byte)'T'
            || block[1] != (byte)'A'
            || block[2] != (byte)'G')
        {
            stream.Position = restore;
            return;
        }

        SetIfEmpty(builder, nameof(AudioFileTagsBuilder.Title), DecodeFixed(block, 3, 30));
        SetIfEmpty(builder, nameof(AudioFileTagsBuilder.Artist), DecodeFixed(block, 33, 30));
        SetIfEmpty(builder, nameof(AudioFileTagsBuilder.Album), DecodeFixed(block, 63, 30));
        SetIfEmpty(builder, nameof(AudioFileTagsBuilder.Year), DecodeFixed(block, 93, 4));
        if (builder.Comment.Length == 0)
        {
            if (block[125] == 0 && block[126] != 0)
            {
                builder.Comment = DecodeFixed(block, 97, 28);
                if (builder.Track.Length == 0)
                {
                    builder.Track = block[126].ToString(CultureInfo.InvariantCulture);
                }
            }
            else
            {
                builder.Comment = DecodeFixed(block, 97, 30);
            }
        }

        stream.Position = restore;
    }

    private static void SetIfEmpty(AudioFileTagsBuilder builder, string field, string value)
    {
        if (value.Length == 0)
        {
            return;
        }

        switch (field)
        {
            case nameof(AudioFileTagsBuilder.Title) when builder.Title.Length == 0:
                builder.Title = value;
                break;
            case nameof(AudioFileTagsBuilder.Artist) when builder.Artist.Length == 0:
                builder.Artist = value;
                break;
            case nameof(AudioFileTagsBuilder.Album) when builder.Album.Length == 0:
                builder.Album = value;
                break;
            case nameof(AudioFileTagsBuilder.Year) when builder.Year.Length == 0:
                builder.Year = value;
                break;
        }
    }

    private static string DecodeFixed(byte[] data, int offset, int length)
    {
        var end = offset + length;
        while (end > offset && data[end - 1] is 0 or 0x20)
        {
            end--;
        }

        return end <= offset ? string.Empty : Encoding.Latin1.GetString(data, offset, end - offset).Trim();
    }

    private static void ReadWave(Stream stream, AudioFileTagsBuilder builder)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadFourCc(reader) != "RIFF")
        {
            return;
        }

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            return;
        }

        while (stream.Position + 8 <= stream.Length)
        {
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var start = stream.Position;
            if (start + size > stream.Length)
            {
                break;
            }

            if (id == "fmt " && size >= 16)
            {
                _ = reader.ReadUInt16();
                builder.Channels = reader.ReadUInt16();
                builder.SampleRate = reader.ReadInt32();
                var byteRate = reader.ReadInt32();
                _ = reader.ReadUInt16();
                builder.BitsPerSample = reader.ReadUInt16();
                if (byteRate > 0 && builder.BitRateKbps <= 0)
                {
                    builder.BitRateKbps = (int)Math.Round(byteRate * 8d / 1000d);
                }
            }
            else if (id == "data")
            {
                if (builder.SampleRate > 0 && builder.Channels > 0 && builder.BitsPerSample > 0)
                {
                    var bytesPerFrame = builder.Channels * Math.Max(1, (builder.BitsPerSample + 7) / 8);
                    if (bytesPerFrame > 0)
                    {
                        builder.DurationSeconds = size / (double)bytesPerFrame / builder.SampleRate;
                    }
                }
            }
            else if (id == "LIST" && size >= 4 && ReadFourCc(reader) == "INFO")
            {
                ReadWaveInfo(reader, start + 4, size - 4, builder);
            }

            stream.Position = start + size + (size & 1);
        }
    }

    private static void ReadWaveInfo(BinaryReader reader, long start, uint size, AudioFileTagsBuilder builder)
    {
        var stream = reader.BaseStream;
        var end = start + size;
        while (stream.Position + 8 <= end)
        {
            var id = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var dataStart = stream.Position;
            if (dataStart + chunkSize > end)
            {
                break;
            }

            var text = ReadLatin1(reader, (int)chunkSize);
            switch (id)
            {
                case "INAM":
                    builder.Title = Prefer(builder.Title, text);
                    break;
                case "IART":
                    builder.Artist = Prefer(builder.Artist, text);
                    break;
                case "IPRD":
                    builder.Album = Prefer(builder.Album, text);
                    break;
                case "IGNR":
                    builder.Genre = Prefer(builder.Genre, text);
                    break;
                case "ICMT":
                    builder.Comment = Prefer(builder.Comment, text);
                    break;
                case "ICRD":
                    builder.Year = Prefer(builder.Year, text.Length >= 4 ? text[..4] : text);
                    break;
                case "ITRK" or "IPRT":
                    builder.Track = Prefer(builder.Track, text);
                    break;
                case "IMUS" or "IENG":
                    builder.Composer = Prefer(builder.Composer, text);
                    break;
            }

            stream.Position = dataStart + chunkSize + (chunkSize & 1);
        }
    }

    private static void ReadAiff(Stream stream, AudioFileTagsBuilder builder)
    {
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        if (ReadFourCc(reader) != "FORM")
        {
            return;
        }

        var formSize = BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
        var form = ReadFourCc(reader);
        if (form is not ("AIFF" or "AIFC"))
        {
            return;
        }

        var formEnd = Math.Min(stream.Length, 8L + formSize);
        while (stream.Position + 8 <= formEnd)
        {
            var id = ReadFourCc(reader);
            var size = (uint)BinaryPrimitives.ReadInt32BigEndian(reader.ReadBytes(4));
            var start = stream.Position;
            if (start + size > stream.Length)
            {
                break;
            }

            if (id == "COMM" && size >= 18)
            {
                builder.Channels = BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));
                var frames = BinaryPrimitives.ReadUInt32BigEndian(reader.ReadBytes(4));
                builder.BitsPerSample = BinaryPrimitives.ReadInt16BigEndian(reader.ReadBytes(2));
                builder.SampleRate = ReadIeee80Int(reader.ReadBytes(10));
                if (builder.SampleRate > 0)
                {
                    builder.DurationSeconds = frames / (double)builder.SampleRate;
                }
            }
            else if (id is "NAME" or "AUTH" or "ANNO" or "(c) ")
            {
                var text = ReadLatin1(reader, (int)size);
                switch (id)
                {
                    case "NAME":
                        builder.Title = Prefer(builder.Title, text);
                        break;
                    case "AUTH":
                        builder.Artist = Prefer(builder.Artist, text);
                        break;
                    default:
                        builder.Comment = Prefer(builder.Comment, text);
                        break;
                }
            }

            stream.Position = start + size + (size & 1);
        }
    }

    private static int ReadIeee80Int(byte[] bytes)
    {
        if (bytes.Length < 10)
        {
            return 0;
        }

        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        var hi = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(2, 4));
        var lo = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(6, 4));
        var fraction = ((ulong)hi << 32) | lo;
        if (exponent == 0 && fraction == 0)
        {
            return 0;
        }

        var value = fraction * Math.Pow(2, exponent - 16383 - 63);
        if ((bytes[0] & 0x80) != 0)
        {
            value = -value;
        }

        return (int)Math.Round(value);
    }

    private static string Prefer(string current, string next) =>
        current.Length > 0 ? current : next;

    private static string ReadLatin1(BinaryReader reader, int size)
    {
        if (size <= 0)
        {
            return string.Empty;
        }

        var bytes = reader.ReadBytes(size);
        var length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
        {
            length--;
        }

        return length <= 0 ? string.Empty : Encoding.Latin1.GetString(bytes, 0, length).Trim();
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));
}
