using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class M4aArtworkTests
{
    private static readonly byte[] OnePixelPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41, 0x54,
        0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00, 0x05, 0x00, 0x01,
        0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00,
        0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    [Fact]
    public void TryRead_IsoMetaCovrPng_ReturnsImage()
    {
        var path = WriteTemp(BuildM4a(cover: OnePixelPng, isoMeta: true, prependMdat: true));
        try
        {
            Assert.True(M4aArtwork.TryRead(path, out var art));
            Assert.Equal(OnePixelPng, art);
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.True(tags.HasArtwork);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_QuickTimeMetaCovr_ReturnsImage()
    {
        var path = WriteTemp(BuildM4a(cover: OnePixelPng, isoMeta: false, prependMdat: false));
        try
        {
            Assert.True(M4aArtwork.TryRead(path, out var art));
            Assert.Equal(OnePixelPng, art);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryFillTags_IlstTextAndNumbers()
    {
        var path = WriteTemp(BuildM4a(
            cover: OnePixelPng,
            isoMeta: true,
            prependMdat: true,
            title: "Song Title",
            artist: "Band Name",
            album: "Record",
            albumArtist: "Various",
            composer: "Writer",
            comment: "Hello",
            genre: "Pop",
            day: "2019-05-01",
            track: 3,
            trackTotal: 10,
            disc: 1,
            discTotal: 2));
        try
        {
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal("Song Title", tags.Title);
            Assert.Equal("Band Name", tags.Artist);
            Assert.Equal("Record", tags.Album);
            Assert.Equal("Various", tags.AlbumArtist);
            Assert.Equal("Writer", tags.Composer);
            Assert.Equal("Hello", tags.Comment);
            Assert.Equal("Pop", tags.Genre);
            Assert.Equal("2019", tags.Year);
            Assert.Equal(2019, tags.YearNumber);
            Assert.Equal("3/10", tags.Track);
            Assert.Equal(3, tags.TrackNumber);
            Assert.Equal("1/2", tags.Disc);
            Assert.Equal(1, tags.DiscNumber);
            Assert.True(tags.HasArtwork);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryFillTags_ItunesSmpbFreeform_FillsDelay()
    {
        var path = WriteTemp(BuildM4a(
            cover: null,
            isoMeta: true,
            prependMdat: false,
            itunesDelay: 0x210,
            itunesPadding: 0x400,
            itunesOriginal: 0x10000));
        try
        {
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(0x210, tags.EncoderDelayFrames);
            Assert.Equal(0x400, tags.EncoderPaddingFrames);
            Assert.Equal(0x10000, tags.EncoderOriginalFrames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryFillTags_EditList_FillsDelayAndPadding()
    {
        var path = WriteTemp(BuildM4aElst(delay: 2112, play: 100000, frames: 100, frameSamples: 1024));
        try
        {
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(2112, tags.EncoderDelayFrames);
            Assert.Equal(102400 - 2112 - 100000, tags.EncoderPaddingFrames);
            Assert.Equal(100000, tags.EncoderOriginalFrames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryFillTags_ItunesSmpbWinsOverEditList()
    {
        var path = WriteTemp(BuildM4a(
            cover: null,
            isoMeta: true,
            prependMdat: false,
            itunesDelay: 528,
            itunesPadding: 64,
            itunesOriginal: 44100,
            includeEditList: true));
        try
        {
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal(528, tags.EncoderDelayFrames);
            Assert.Equal(64, tags.EncoderPaddingFrames);
            Assert.Equal(44100, tags.EncoderOriginalFrames);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_WithoutCovr_IsFalse()
    {
        var path = WriteTemp(BuildM4a(cover: null, isoMeta: true, prependMdat: false));
        try
        {
            Assert.False(M4aArtwork.TryRead(path, out _));
            Assert.False(AudioTagProbe.TryRead(path, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTemp(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-m4a-" + Guid.NewGuid().ToString("N") + ".m4a");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildM4a(
        byte[]? cover,
        bool isoMeta,
        bool prependMdat,
        string? title = null,
        string? artist = null,
        string? album = null,
        string? albumArtist = null,
        string? composer = null,
        string? comment = null,
        string? genre = null,
        string? day = null,
        int track = 0,
        int trackTotal = 0,
        int disc = 0,
        int discTotal = 0,
        int itunesDelay = 0,
        int itunesPadding = 0,
        long itunesOriginal = 0,
        bool includeEditList = false)
    {
        var items = new List<byte[]>();
        AddText(items, 0xA96E616D, title);
        AddText(items, 0xA9415254, artist);
        AddText(items, 0xA9616C62, album);
        AddText(items, 0x61415254, albumArtist);
        AddText(items, 0xA9777274, composer);
        AddText(items, 0xA9636D74, comment);
        AddText(items, 0xA967656E, genre);
        AddText(items, 0xA9646179, day);
        AddPair(items, 0x74726B6E, track, trackTotal);
        AddPair(items, 0x6469736B, disc, discTotal);
        if (cover is { Length: > 0 })
        {
            var dataPayload = new byte[8 + cover.Length];
            dataPayload[3] = 14;
            cover.CopyTo(dataPayload, 8);
            items.Add(TypedBox(0x636F7672, Box("data", dataPayload)));
        }

        if (itunesDelay > 0 || itunesPadding > 0)
        {
            items.Add(ItunesSmpbItem(itunesDelay, itunesPadding, itunesOriginal));
        }

        var ilst = TypedBox(0x696C7374, Concat(items.ToArray()));
        var metaBody = isoMeta ? Concat(new byte[4], ilst) : ilst;
        var udta = Box("udta", Box("meta", metaBody));
        var moovBody = includeEditList
            ? Concat(MovieHeader(44100), SoundTrackElst(2112, 100000, 100, 1024), udta)
            : udta;
        var moov = Box("moov", moovBody);
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("M4A " + "\0\0\0\0" + "M4A mp42"));
        return prependMdat
            ? Concat(ftyp, Box("mdat", new byte[64]), moov)
            : Concat(ftyp, moov);
    }

    private static byte[] BuildM4aElst(int delay, int play, int frames, int frameSamples)
    {
        var ftyp = Box("ftyp", Encoding.ASCII.GetBytes("M4A " + "\0\0\0\0" + "M4A mp42"));
        var moov = Box("moov", Concat(MovieHeader(44100), SoundTrackElst(delay, play, frames, frameSamples)));
        return Concat(ftyp, moov);
    }

    private static byte[] ItunesSmpbItem(int delay, int padding, long original)
    {
        var text = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $" 00000000 {delay:X8} {padding:X8} {original:X16}");
        var utf8 = Encoding.UTF8.GetBytes(text);
        var dataPayload = new byte[8 + utf8.Length];
        dataPayload[3] = 1;
        utf8.CopyTo(dataPayload, 8);
        return TypedBox(0x2D2D2D2D, Concat(
            FullBoxLabel("mean", "com.apple.iTunes"),
            FullBoxLabel("name", "iTunSMPB"),
            Box("data", dataPayload)));
    }

    private static byte[] FullBoxLabel(string type, string text)
    {
        var utf8 = Encoding.UTF8.GetBytes(text);
        var payload = new byte[4 + utf8.Length];
        utf8.CopyTo(payload, 4);
        return Box(type, payload);
    }

    private static byte[] MovieHeader(int timescale)
    {
        var body = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(12), (uint)timescale);
        return Box("mvhd", body);
    }

    private static byte[] SoundTrackElst(int delay, int play, int frames, int frameSamples)
    {
        var mdhd = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(mdhd.AsSpan(12), 44100);
        var hdlr = new byte[12];
        Encoding.ASCII.GetBytes("soun").CopyTo(hdlr, 8);
        var stts = new byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(stts.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(stts.AsSpan(8), (uint)frames);
        BinaryPrimitives.WriteUInt32BigEndian(stts.AsSpan(12), (uint)frameSamples);
        var elst = new byte[20];
        BinaryPrimitives.WriteUInt32BigEndian(elst.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(elst.AsSpan(8), (uint)play);
        BinaryPrimitives.WriteInt32BigEndian(elst.AsSpan(12), delay);
        BinaryPrimitives.WriteUInt32BigEndian(elst.AsSpan(16), 0x00010000);
        var mdia = Box("mdia", Concat(
            Box("mdhd", mdhd),
            Box("hdlr", hdlr),
            Box("minf", Box("stbl", Box("stts", stts)))));
        var edts = Box("edts", Box("elst", elst));
        return Box("trak", Concat(edts, mdia));
    }

    private static void AddText(List<byte[]> items, uint type, string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var utf8 = Encoding.UTF8.GetBytes(text);
        var payload = new byte[8 + utf8.Length];
        payload[3] = 1;
        utf8.CopyTo(payload, 8);
        items.Add(TypedBox(type, Box("data", payload)));
    }

    private static void AddPair(List<byte[]> items, uint type, int number, int total)
    {
        if (number <= 0 && total <= 0)
        {
            return;
        }

        var payload = new byte[16];
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(10), (ushort)number);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(12), (ushort)total);
        items.Add(TypedBox(type, Box("data", payload)));
    }

    private static byte[] TypedBox(uint type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        BinaryPrimitives.WriteUInt32BigEndian(box.AsSpan(4), type);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Box(string type, byte[] payload)
    {
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var length = 0;
        foreach (var part in parts)
        {
            length += part.Length;
        }

        var all = new byte[length];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(all, offset);
            offset += part.Length;
        }

        return all;
    }
}
