using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text;

namespace MgaSonicAnvil.Audio;

/// <summary>M4A / MP4 の ilst（タグと covr）を読む。書き込みはしない。</summary>
internal static class M4aArtwork
{
    private const uint TypeMoov = 0x6D6F6F76;
    private const uint TypeUdta = 0x75647461;
    private const uint TypeMeta = 0x6D657461;
    private const uint TypeIlst = 0x696C7374;
    private const uint TypeCovr = 0x636F7672;
    private const uint TypeData = 0x64617461;
    private const uint TypeHdlr = 0x68646C72;
    private const uint TypeKeys = 0x6B657973;
    private const uint TypeName = 0xA96E616D;
    private const uint TypeArtist = 0xA9415254;
    private const uint TypeArtistLower = 0xA9617274;
    private const uint TypeAlbum = 0xA9616C62;
    private const uint TypeAlbumArtist = 0x61415254;
    private const uint TypeComposer = 0xA9777274;
    private const uint TypeComment = 0xA9636D74;
    private const uint TypeGenre = 0xA967656E;
    private const uint TypeGenreId = 0x676E7265;
    private const uint TypeDay = 0xA9646179;
    private const uint TypeTrack = 0x74726B6E;
    private const uint TypeDisk = 0x6469736B;
    private const int MaxArtworkBytes = 32 * 1024 * 1024;
    private const int MaxTextBytes = 64 * 1024;
    private const int TypeUtf16 = 2;

    public static bool TryRead(string path, out byte[] artwork)
    {
        artwork = [];
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var state = new ScanState { Builder = new AudioFileTagsBuilder(), IncludePicture = true };
            TryFill(stream, state);
            artwork = state.Artwork;
            return artwork.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>ilst の文字タグとジャケット有無を埋める。画像本体は読まない。</summary>
    public static bool TryFillTags(string path, AudioFileTagsBuilder builder)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var state = new ScanState { Builder = builder, IncludePicture = false };
            TryFill(stream, state);
            return state.Applied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryFill(Stream stream, ScanState state)
    {
        var limit = stream.Length;
        stream.Position = 0;
        while (TryReadBoxHeader(stream, limit, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeMoov)
            {
                WalkMoov(stream, dataStart, boxEnd, state);
            }

            stream.Position = boxEnd;
        }
    }

    private static void WalkMoov(Stream stream, long start, long end, ScanState state)
    {
        stream.Position = start;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeUdta)
            {
                WalkUdta(stream, dataStart, boxEnd, state);
            }
            else if (type == TypeMeta)
            {
                WalkMeta(stream, dataStart, boxEnd, state);
            }

            stream.Position = boxEnd;
        }
    }

    private static void WalkUdta(Stream stream, long start, long end, ScanState state)
    {
        stream.Position = start;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeMeta)
            {
                WalkMeta(stream, dataStart, boxEnd, state);
            }

            stream.Position = boxEnd;
        }
    }

    private static void WalkMeta(Stream stream, long start, long end, ScanState state)
    {
        var body = start;
        if (start + 8 <= end)
        {
            stream.Position = start;
            if (TryReadBoxHeader(stream, end, out var type, out _, out _)
                && type is TypeIlst or TypeHdlr or TypeKeys)
            {
                body = start;
            }
            else if (start + 4 <= end)
            {
                body = start + 4;
            }
        }

        stream.Position = body;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeIlst)
            {
                WalkIlst(stream, dataStart, boxEnd, state);
            }

            stream.Position = boxEnd;
        }
    }

    private static void WalkIlst(Stream stream, long start, long end, ScanState state)
    {
        stream.Position = start;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            ApplyItem(stream, type, dataStart, boxEnd, state);
            stream.Position = boxEnd;
        }
    }

    private static void ApplyItem(Stream stream, uint type, long start, long end, ScanState state)
    {
        if (type == TypeCovr)
        {
            ApplyCover(stream, start, end, state);
            return;
        }

        if (!TryReadFirstData(stream, start, end, out var dataType, out var payload) || payload.Length == 0)
        {
            return;
        }

        switch (type)
        {
            case TypeName:
                Assign(state, () => state.Builder.Title, v => state.Builder.Title = v, DecodeText(dataType, payload));
                break;
            case TypeArtist:
            case TypeArtistLower:
                Assign(state, () => state.Builder.Artist, v => state.Builder.Artist = v, DecodeText(dataType, payload));
                break;
            case TypeAlbum:
                Assign(state, () => state.Builder.Album, v => state.Builder.Album = v, DecodeText(dataType, payload));
                break;
            case TypeAlbumArtist:
                Assign(state, () => state.Builder.AlbumArtist, v => state.Builder.AlbumArtist = v, DecodeText(dataType, payload));
                break;
            case TypeComposer:
                Assign(state, () => state.Builder.Composer, v => state.Builder.Composer = v, DecodeText(dataType, payload));
                break;
            case TypeComment:
                Assign(state, () => state.Builder.Comment, v => state.Builder.Comment = v, DecodeText(dataType, payload));
                break;
            case TypeGenre:
                Assign(state, () => state.Builder.Genre, v => state.Builder.Genre = v, DecodeText(dataType, payload));
                break;
            case TypeDay:
                Assign(state, () => state.Builder.Year, v => state.Builder.Year = v, YearFromDay(DecodeText(dataType, payload)));
                break;
            case TypeTrack:
                Assign(state, () => state.Builder.Track, v => state.Builder.Track = v, FormatIndexPair(payload));
                break;
            case TypeDisk:
                Assign(state, () => state.Builder.Disc, v => state.Builder.Disc = v, FormatIndexPair(payload));
                break;
            case TypeGenreId:
                Assign(state, () => state.Builder.Genre, v => state.Builder.Genre = v, GenreFromId(payload));
                break;
        }
    }

    private static void ApplyCover(Stream stream, long start, long end, ScanState state)
    {
        stream.Position = start;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeData && TryTakeCover(stream, dataStart, boxEnd, state))
            {
                return;
            }

            stream.Position = boxEnd;
        }
    }

    private static bool TryTakeCover(Stream stream, long dataStart, long boxEnd, ScanState state)
    {
        var payloadStart = dataStart + 8;
        if (payloadStart > boxEnd)
        {
            return false;
        }

        var length = boxEnd - payloadStart;
        if (length <= 0 || length > MaxArtworkBytes)
        {
            return false;
        }

        state.Builder.HasArtwork = true;
        state.Applied = true;
        if (!state.IncludePicture || state.Artwork.Length > 0)
        {
            return true;
        }

        stream.Position = payloadStart;
        var payload = new byte[length];
        if (stream.Read(payload, 0, payload.Length) != payload.Length
            || !Id3Artwork.LooksLikeImage(payload))
        {
            return false;
        }

        state.Artwork = payload;
        return true;
    }

    private static bool TryReadFirstData(
        Stream stream,
        long start,
        long end,
        out int dataType,
        out byte[] payload)
    {
        dataType = 0;
        payload = [];
        stream.Position = start;
        while (TryReadBoxHeader(stream, end, out var type, out var dataStart, out var boxEnd))
        {
            if (type == TypeData && TryReadDataPayload(stream, dataStart, boxEnd, out dataType, out payload))
            {
                return payload.Length > 0;
            }

            stream.Position = boxEnd;
        }

        return false;
    }

    private static bool TryReadDataPayload(
        Stream stream,
        long dataStart,
        long boxEnd,
        out int dataType,
        out byte[] payload)
    {
        dataType = 0;
        payload = [];
        var payloadStart = dataStart + 8;
        if (payloadStart > boxEnd)
        {
            return false;
        }

        var length = boxEnd - payloadStart;
        if (length <= 0 || length > MaxTextBytes)
        {
            return false;
        }

        stream.Position = dataStart;
        Span<byte> head = stackalloc byte[8];
        if (stream.Read(head) != 8)
        {
            return false;
        }

        dataType = (head[1] << 16) | (head[2] << 8) | head[3];
        payload = new byte[length];
        return stream.Read(payload, 0, payload.Length) == payload.Length;
    }

    private static void Assign(ScanState state, Func<string> get, Action<string> set, string value)
    {
        if (get().Length > 0 || value.Length == 0)
        {
            return;
        }

        set(value);
        state.Applied = true;
    }

    private static string DecodeText(int dataType, byte[] payload)
    {
        if (payload.Length == 0)
        {
            return string.Empty;
        }

        var utf16 = dataType == TypeUtf16
            || (payload.Length >= 2 && payload[0] == 0xFF && payload[1] == 0xFE)
            || (payload.Length >= 2 && payload[0] == 0xFE && payload[1] == 0xFF);
        if (utf16)
        {
            var little = payload[0] == 0xFF && payload[1] == 0xFE;
            var bom = payload[0] is 0xFF or 0xFE && payload[1] is 0xFE or 0xFF;
            var enc = little ? Encoding.Unicode : Encoding.BigEndianUnicode;
            return enc.GetString(payload, bom ? 2 : 0, payload.Length - (bom ? 2 : 0)).Trim('\0').Trim();
        }

        return Encoding.UTF8.GetString(payload).Trim('\0').Trim();
    }

    private static string YearFromDay(string text)
    {
        if (text.Length >= 4
            && char.IsDigit(text[0])
            && char.IsDigit(text[1])
            && char.IsDigit(text[2])
            && char.IsDigit(text[3]))
        {
            return text[..4];
        }

        return text;
    }

    private static string FormatIndexPair(byte[] payload)
    {
        if (payload.Length < 4)
        {
            return string.Empty;
        }

        var number = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(payload.Length >= 6 ? 2 : 0));
        var total = payload.Length >= 6
            ? BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4))
            : (ushort)0;
        if (number == 0 && total == 0)
        {
            return string.Empty;
        }

        return total > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{number}/{total}")
            : number.ToString(CultureInfo.InvariantCulture);
    }

    private static string GenreFromId(byte[] payload)
    {
        if (payload.Length < 2)
        {
            return string.Empty;
        }

        var id = BinaryPrimitives.ReadUInt16BigEndian(payload);
        if (id == 0)
        {
            return string.Empty;
        }

        // iTunes は ID3 ジャンル番号 + 1。
        var index = id - 1;
        return index.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryReadBoxHeader(
        Stream stream,
        long limit,
        out uint type,
        out long dataStart,
        out long boxEnd)
    {
        type = 0;
        dataStart = 0;
        boxEnd = 0;
        var pos = stream.Position;
        if (pos < 0 || pos + 8 > limit || pos + 8 > stream.Length)
        {
            return false;
        }

        Span<byte> header = stackalloc byte[8];
        if (stream.Read(header) != 8)
        {
            return false;
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(header);
        type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        long headerLen = 8;
        long total;
        if (size == 1)
        {
            Span<byte> large = stackalloc byte[8];
            if (pos + 16 > limit || stream.Read(large) != 8)
            {
                return false;
            }

            var wide = BinaryPrimitives.ReadUInt64BigEndian(large);
            if (wide > (ulong)long.MaxValue)
            {
                return false;
            }

            total = (long)wide;
            headerLen = 16;
        }
        else if (size == 0)
        {
            total = limit - pos;
        }
        else
        {
            total = size;
        }

        if (total < headerLen)
        {
            return false;
        }

        dataStart = pos + headerLen;
        boxEnd = pos + total;
        return boxEnd <= limit && boxEnd <= stream.Length && dataStart <= boxEnd;
    }

    private sealed class ScanState
    {
        public required AudioFileTagsBuilder Builder { get; init; }

        public bool IncludePicture { get; init; }

        public byte[] Artwork { get; set; } = [];

        public bool Applied { get; set; }
    }
}
