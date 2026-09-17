using System.IO;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>MP3 の ID3v2 APIC（ジャケット）の読み書き。再エンコードしない。</summary>
internal static class Id3Artwork
{
    public static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp"];

    public static bool IsImagePath(string? path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return true;
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return true;
        }

        if (bytes.Length >= 3 && bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F')
        {
            return true;
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return true;
        }

        return bytes.Length >= 12
            && bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P';
    }

    public static string MimeFromImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50)
        {
            return "image/png";
        }

        if (bytes.Length >= 3 && bytes[0] == (byte)'G')
        {
            return "image/gif";
        }

        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return "image/bmp";
        }

        return "image/webp";
    }

    public static bool TryRead(string path, out byte[] artwork)
    {
        artwork = [];
        try
        {
            using var stream = File.OpenRead(path);
            return TryRead(stream, out artwork);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryRead(Stream stream, out byte[] artwork)
    {
        artwork = [];
        if (!TryReadTag(stream, out var version, out var frames) || frames.Count == 0)
        {
            return false;
        }

        foreach (var frame in frames)
        {
            if (TryDecodePicture(version, frame.Id, frame.Data, out artwork))
            {
                return artwork.Length > 0;
            }
        }

        return false;
    }

    public static bool TryWrite(string path, byte[]? artwork)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        var temp = path + ".jacket.tmp";
        try
        {
            using (var source = File.OpenRead(path))
            {
                TryReadTag(source, out _, out var frames);
                var audioStart = source.Position;
                frames.RemoveAll(frame => IsPictureFrame(frame.Id));
                if (artwork is { Length: > 0 } && LooksLikeImage(artwork))
                {
                    frames.Add(new Id3Frame("APIC", BuildApicBody(artwork)));
                }

                using var dest = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None);
                if (frames.Count > 0)
                {
                    WriteTag(dest, frames);
                }

                source.Position = audioStart;
                source.CopyTo(dest);
            }

            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(temp);
            return false;
        }
    }

    internal static void FillTags(int version, List<Id3Frame> frames, AudioFileTagsBuilder builder)
    {
        foreach (var frame in frames)
        {
            if (IsPictureFrame(frame.Id))
            {
                builder.HasArtwork = true;
                continue;
            }

            var text = DecodeTextFrame(version, frame.Id, frame.Data);
            if (text.Length == 0)
            {
                continue;
            }

            switch (frame.Id)
            {
                case "TIT2" or "TT2":
                    builder.Title = Prefer(builder.Title, text);
                    break;
                case "TPE1" or "TP1":
                    builder.Artist = Prefer(builder.Artist, text);
                    break;
                case "TALB" or "TAL":
                    builder.Album = Prefer(builder.Album, text);
                    break;
                case "TPE2" or "TP2":
                    builder.AlbumArtist = Prefer(builder.AlbumArtist, text);
                    break;
                case "TRCK" or "TRK":
                    builder.Track = Prefer(builder.Track, text);
                    break;
                case "TPOS" or "TPA":
                    builder.Disc = Prefer(builder.Disc, text);
                    break;
                case "TYER" or "TYE" or "TDRC" or "TDRL":
                    builder.Year = Prefer(builder.Year, text.Length >= 4 ? text[..4] : text);
                    break;
                case "TCON" or "TCO":
                    builder.Genre = Prefer(builder.Genre, StripGenreNumber(text));
                    break;
                case "COMM" or "COM" or "USLT" or "ULT":
                    builder.Comment = Prefer(builder.Comment, text);
                    break;
                case "TCOM" or "TCM":
                    builder.Composer = Prefer(builder.Composer, text);
                    break;
                case "TLEN" or "TLE":
                    if (builder.LengthMillis <= 0 && int.TryParse(text, out var millis) && millis > 0)
                    {
                        builder.LengthMillis = millis;
                    }

                    break;
            }
        }
    }

    internal static bool TryReadTag(Stream stream, out int version, out List<Id3Frame> frames) =>
        TryReadTag(stream, out version, out frames, includePictureData: true);

    /// <summary>
    /// ID3 を読む。includePictureData=false なら APIC/PIC の本体はシークだけし、
    /// 空のフレームを残す（HasArtwork 判定用）。フォルダ一覧のタグ探査向け。
    /// </summary>
    internal static bool TryReadTag(
        Stream stream,
        out int version,
        out List<Id3Frame> frames,
        bool includePictureData)
    {
        version = 0;
        frames = [];
        if (stream.Length < 10)
        {
            return false;
        }

        var header = new byte[10];
        if (stream.Read(header, 0, 10) != 10
            || header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
        {
            stream.Position = 0;
            return false;
        }

        version = header[3];
        if (version is < 2 or > 4)
        {
            stream.Position = 0;
            return false;
        }

        var flags = header[5];
        var tagSize = ReadSyncSafe(header.AsSpan(6, 4));
        var tagEnd = 10L + tagSize;
        if (tagEnd > stream.Length)
        {
            stream.Position = 0;
            return false;
        }

        var bodyStart = 10L;
        if ((flags & 0x40) != 0 && version >= 3)
        {
            var extSizeBytes = new byte[4];
            if (stream.Read(extSizeBytes, 0, 4) != 4)
            {
                stream.Position = 0;
                return false;
            }

            var extSize = version >= 4
                ? ReadSyncSafe(extSizeBytes)
                : ReadInt32(extSizeBytes);
            bodyStart = stream.Position + Math.Max(0, extSize - 4);
            if (bodyStart > tagEnd)
            {
                stream.Position = tagEnd;
                return false;
            }
        }

        stream.Position = bodyStart;
        var idLength = version == 2 ? 3 : 4;
        var headerLength = version == 2 ? 6 : 10;
        while (stream.Position + headerLength <= tagEnd)
        {
            var frameHeader = new byte[headerLength];
            if (stream.Read(frameHeader, 0, headerLength) != headerLength)
            {
                break;
            }

            if (frameHeader[0] == 0)
            {
                break;
            }

            var id = Encoding.ASCII.GetString(frameHeader, 0, idLength);
            if (!IsFrameId(id))
            {
                break;
            }

            int size;
            if (version == 2)
            {
                size = (frameHeader[3] << 16) | (frameHeader[4] << 8) | frameHeader[5];
            }
            else if (version >= 4)
            {
                size = ReadSyncSafe(frameHeader.AsSpan(4, 4));
            }
            else
            {
                size = ReadInt32(frameHeader.AsSpan(4, 4));
            }

            if (size < 0 || stream.Position + size > tagEnd)
            {
                break;
            }

            if (!includePictureData && IsPictureFrame(id))
            {
                // ジャケット本体（数MB もあり得る）は読まず進める。
                stream.Position += size;
                frames.Add(new Id3Frame(id, []));
                continue;
            }

            var data = size == 0 ? [] : new byte[size];
            if (size > 0 && stream.Read(data, 0, size) != size)
            {
                break;
            }

            frames.Add(new Id3Frame(id, data));
        }

        stream.Position = tagEnd;
        return true;
    }

    private static void WriteTag(Stream stream, List<Id3Frame> frames)
    {
        var body = new MemoryStream();
        foreach (var frame in frames)
        {
            var id = frame.Id.Length >= 4 ? frame.Id[..4] : frame.Id.PadRight(4, '\0');
            if (id == "PIC\0")
            {
                id = "APIC";
            }

            body.Write(Encoding.ASCII.GetBytes(id));
            WriteInt32(body, frame.Data.Length);
            body.WriteByte(0);
            body.WriteByte(0);
            body.Write(frame.Data);
        }

        var bodyBytes = body.ToArray();
        var header = new byte[10];
        header[0] = (byte)'I';
        header[1] = (byte)'D';
        header[2] = (byte)'3';
        header[3] = 3;
        header[4] = 0;
        header[5] = 0;
        WriteSyncSafe(header.AsSpan(6, 4), bodyBytes.Length);
        stream.Write(header);
        stream.Write(bodyBytes);
    }

    private static byte[] BuildApicBody(byte[] image)
    {
        var mime = MimeFromImage(image);
        var mimeBytes = Encoding.ASCII.GetBytes(mime);
        var body = new byte[1 + mimeBytes.Length + 1 + 1 + 1 + image.Length];
        body[0] = 0;
        mimeBytes.CopyTo(body, 1);
        body[1 + mimeBytes.Length] = 0;
        body[2 + mimeBytes.Length] = 3;
        body[3 + mimeBytes.Length] = 0;
        image.CopyTo(body, 4 + mimeBytes.Length);
        return body;
    }

    private static bool TryDecodePicture(int version, string id, byte[] data, out byte[] artwork)
    {
        artwork = [];
        if (version == 2 && id == "PIC")
        {
            return TryDecodePicV2(data, out artwork);
        }

        if (id is not ("APIC" or "PIC"))
        {
            return false;
        }

        if (data.Length < 4)
        {
            return false;
        }

        var encoding = data[0];
        var offset = 1;
        if (!TryReadTerminated(data, ref offset, latin1: true, out _))
        {
            return false;
        }

        if (offset >= data.Length)
        {
            return false;
        }

        offset++;
        if (!TryReadTerminated(data, ref offset, latin1: encoding is 0 or 3, out _))
        {
            return false;
        }

        if (offset >= data.Length)
        {
            return false;
        }

        artwork = data[offset..];
        return artwork.Length > 0 && LooksLikeImage(artwork);
    }

    private static bool TryDecodePicV2(byte[] data, out byte[] artwork)
    {
        artwork = [];
        if (data.Length < 6)
        {
            return false;
        }

        var encoding = data[0];
        var offset = 4;
        offset++;
        if (!TryReadTerminated(data, ref offset, latin1: encoding is 0 or 3, out _))
        {
            return false;
        }

        if (offset >= data.Length)
        {
            return false;
        }

        artwork = data[offset..];
        return artwork.Length > 0 && LooksLikeImage(artwork);
    }

    private static string DecodeTextFrame(int version, string id, byte[] data)
    {
        if (data.Length < 2)
        {
            return string.Empty;
        }

        if (id is "COMM" or "COM" or "USLT" or "ULT")
        {
            return DecodeComment(data);
        }

        if (id.StartsWith('T') || (version == 2 && id.Length == 3 && id[0] == 'T'))
        {
            return NormalizeText(DecodeEncoded(data[0], data.AsSpan(1)));
        }

        return string.Empty;
    }

    private static string DecodeComment(byte[] data)
    {
        if (data.Length < 5)
        {
            return string.Empty;
        }

        var encoding = data[0];
        var offset = 4;
        if (!TryReadTerminated(data, ref offset, latin1: encoding is 0 or 3, out _))
        {
            return string.Empty;
        }

        if (offset >= data.Length)
        {
            return string.Empty;
        }

        return NormalizeText(DecodeEncoded(encoding, data.AsSpan(offset)));
    }

    private static string DecodeEncoded(byte encoding, ReadOnlySpan<byte> payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        return encoding switch
        {
            1 => DecodeUtf16(payload),
            2 => Encoding.BigEndianUnicode.GetString(payload),
            3 => Encoding.UTF8.GetString(payload),
            _ => Encoding.Latin1.GetString(payload),
        };
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> payload)
    {
        if (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(payload[2..]);
        }

        if (payload.Length >= 2 && payload[0] == 0xFE && payload[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(payload[2..]);
        }

        return Encoding.Unicode.GetString(payload);
    }

    private static string NormalizeText(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        text = text.Replace('\0', ';').Trim().Trim(';').Trim();
        while (text.Contains(";;", StringComparison.Ordinal))
        {
            text = text.Replace(";;", ";", StringComparison.Ordinal);
        }

        return text.Replace("; ", ";", StringComparison.Ordinal).Replace(";", "; ", StringComparison.Ordinal);
    }

    private static string StripGenreNumber(string text)
    {
        if (text.Length >= 3 && text[0] == '(')
        {
            var close = text.IndexOf(')');
            if (close > 1)
            {
                var rest = text[(close + 1)..].Trim();
                return rest.Length > 0 ? rest : text[1..close];
            }
        }

        return text;
    }

    private static string Prefer(string current, string next) =>
        current.Length > 0 ? current : next;

    private static bool TryReadTerminated(byte[] data, ref int offset, bool latin1, out string text)
    {
        text = string.Empty;
        if (latin1)
        {
            var start = offset;
            while (offset < data.Length && data[offset] != 0)
            {
                offset++;
            }

            if (offset >= data.Length)
            {
                return false;
            }

            text = Encoding.Latin1.GetString(data, start, offset - start);
            offset++;
            return true;
        }

        if (offset + 1 >= data.Length)
        {
            return false;
        }

        var start16 = offset;
        while (offset + 1 < data.Length && (data[offset] != 0 || data[offset + 1] != 0))
        {
            offset += 2;
        }

        if (offset + 1 >= data.Length)
        {
            return false;
        }

        var length = offset - start16;
        if (length >= 2 && data[start16] == 0xFF && data[start16 + 1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(data, start16 + 2, length - 2);
        }
        else if (length >= 2 && data[start16] == 0xFE && data[start16 + 1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(data, start16 + 2, length - 2);
        }

        offset += 2;
        return true;
    }

    private static bool IsPictureFrame(string id) =>
        id is "APIC" or "PIC" or "PIC\0";

    private static bool IsFrameId(string id)
    {
        foreach (var c in id)
        {
            if (c is not (>= 'A' and <= 'Z' or >= '0' and <= '9'))
            {
                return false;
            }
        }

        return id.Length > 0;
    }

    private static int ReadSyncSafe(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 21) | (bytes[1] << 14) | (bytes[2] << 7) | bytes[3];

    private static int ReadInt32(ReadOnlySpan<byte> bytes) =>
        (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

    private static void WriteInt32(Stream stream, int value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteSyncSafe(Span<byte> dest, int size)
    {
        dest[0] = (byte)((size >> 21) & 0x7F);
        dest[1] = (byte)((size >> 14) & 0x7F);
        dest[2] = (byte)((size >> 7) & 0x7F);
        dest[3] = (byte)(size & 0x7F);
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
        catch (IOException)
        {
        }
    }

    internal readonly record struct Id3Frame(string Id, byte[] Data);
}
