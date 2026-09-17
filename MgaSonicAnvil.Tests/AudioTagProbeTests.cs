using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AudioTagProbeTests
{
    [Fact]
    public void TryRead_WaveListInfo_FillsTagsAndDurationWithoutPcm()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-tags-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteWaveWithInfo(path, frames: 48000, title: "Cue Title", artist: "Cue Artist", album: "Cue Album", genre: "SFX");
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.True(tags.Probed);
            Assert.Equal("Cue Title", tags.Title);
            Assert.Equal("Cue Artist", tags.Artist);
            Assert.Equal("Cue Album", tags.Album);
            Assert.Equal("SFX", tags.Genre);
            Assert.Equal(48000, tags.SampleRate);
            Assert.Equal(16, tags.BitsPerSample);
            Assert.Equal(1, tags.Channels);
            Assert.Equal(1, tags.DurationSeconds, 3);
            Assert.Equal(768, tags.BitRateKbps);
            Assert.False(tags.HasArtwork);

            var document = AudioDocument.CreateDeferred(path);
            AudioTagProbe.Ensure(document);
            Assert.True(document.IsDeferredLoad);
            Assert.Equal(0, document.FrameCount);
            Assert.Equal("Cue Title", document.Tags.Title);
            var row = LibraryBrowserView.CreateRow(new DocumentSession(document));
            Assert.Equal("Cue Title", row.Title);
            Assert.Equal("Cue Artist", row.Artist);
            Assert.Equal(1, row.DurationSeconds, 3);
            Assert.Equal("48kHz", row.SampleRateText);
            Assert.Equal("16bit", row.BitDepthText);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TryRead_AiffNameAuth_FillsTitleArtistAndDuration()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-tags-" + Guid.NewGuid().ToString("N") + ".aiff");
        try
        {
            WriteAiff(path, frames: 24000, title: "Aiff Name", artist: "Aiff Auth");
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal("Aiff Name", tags.Title);
            Assert.Equal("Aiff Auth", tags.Artist);
            Assert.Equal(48000, tags.SampleRate);
            Assert.Equal(0.5, tags.DurationSeconds, 3);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void TryRead_Id3v23TextAndApic_FillsTagsAndHasArtwork()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-tags-" + Guid.NewGuid().ToString("N") + ".mp3");
        try
        {
            File.WriteAllBytes(path, BuildId3v23(
                title: "Song",
                artist: "Band",
                album: "Record",
                track: "3/10",
                year: "2019",
                genre: "(13)Pop",
                comment: "Hello",
                composer: "Writer",
                hasPicture: true));
            Assert.True(AudioTagProbe.TryRead(path, out var tags));
            Assert.Equal("Song", tags.Title);
            Assert.Equal("Band", tags.Artist);
            Assert.Equal("Record", tags.Album);
            Assert.Equal("3/10", tags.Track);
            Assert.Equal(3, tags.TrackNumber);
            Assert.Equal("2019", tags.Year);
            Assert.Equal(2019, tags.YearNumber);
            Assert.Equal("Pop", tags.Genre);
            Assert.Equal("Hello", tags.Comment);
            Assert.Equal("Writer", tags.Composer);
            Assert.True(tags.HasArtwork);
            Assert.Equal(44100, tags.SampleRate);
            Assert.Equal(2, tags.Channels);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Ensure_DoesNotReplaceAlreadyProbedTags()
    {
        var document = AudioDocument.CreateDeferred("missing.wav");
        document.ApplyTags(new AudioFileTags { Probed = true, Title = "Kept" });
        AudioTagProbe.Ensure(document);
        Assert.Equal("Kept", document.Tags.Title);
    }

    private static byte[] BuildId3v23(
        string title,
        string artist,
        string album,
        string track,
        string year,
        string genre,
        string comment,
        string composer,
        bool hasPicture)
    {
        var body = new MemoryStream();
        WriteTextFrame(body, "TIT2", title);
        WriteTextFrame(body, "TPE1", artist);
        WriteTextFrame(body, "TALB", album);
        WriteTextFrame(body, "TRCK", track);
        WriteTextFrame(body, "TYER", year);
        WriteTextFrame(body, "TCON", genre);
        WriteTextFrame(body, "TCOM", composer);
        WriteCommentFrame(body, comment);
        if (hasPicture)
        {
            var png = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            };
            var mime = Encoding.ASCII.GetBytes("image/png");
            var pic = new byte[1 + mime.Length + 1 + 1 + 1 + png.Length];
            pic[0] = 0;
            mime.CopyTo(pic, 1);
            pic[1 + mime.Length] = 0;
            pic[2 + mime.Length] = 3;
            pic[3 + mime.Length] = 0;
            png.CopyTo(pic, 4 + mime.Length);
            WriteFrame(body, "APIC", pic);
        }

        var frames = body.ToArray();
        var tag = new byte[10 + frames.Length];
        tag[0] = (byte)'I';
        tag[1] = (byte)'D';
        tag[2] = (byte)'3';
        tag[3] = 3;
        WriteSyncSafe(tag.AsSpan(6, 4), frames.Length);
        frames.CopyTo(tag, 10);

        var mpeg = new byte[] { 0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var file = new byte[tag.Length + mpeg.Length];
        tag.CopyTo(file, 0);
        mpeg.CopyTo(file, tag.Length);
        return file;
    }

    private static void WriteTextFrame(Stream stream, string id, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var data = new byte[1 + payload.Length];
        data[0] = 3;
        payload.CopyTo(data, 1);
        WriteFrame(stream, id, data);
    }

    private static void WriteCommentFrame(Stream stream, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var data = new byte[1 + 3 + 1 + payload.Length];
        data[0] = 3;
        data[1] = (byte)'e';
        data[2] = (byte)'n';
        data[3] = (byte)'g';
        payload.CopyTo(data, 5);
        WriteFrame(stream, "COMM", data);
    }

    private static void WriteFrame(Stream stream, string id, byte[] data)
    {
        stream.Write(Encoding.ASCII.GetBytes(id));
        stream.WriteByte((byte)(data.Length >> 24));
        stream.WriteByte((byte)(data.Length >> 16));
        stream.WriteByte((byte)(data.Length >> 8));
        stream.WriteByte((byte)data.Length);
        stream.WriteByte(0);
        stream.WriteByte(0);
        stream.Write(data);
    }

    private static void WriteSyncSafe(Span<byte> dest, int size)
    {
        dest[0] = (byte)((size >> 21) & 0x7F);
        dest[1] = (byte)((size >> 14) & 0x7F);
        dest[2] = (byte)((size >> 7) & 0x7F);
        dest[3] = (byte)(size & 0x7F);
    }

    private static void WriteWaveWithInfo(
        string path,
        int frames,
        string title,
        string artist,
        string album,
        string genre)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        var sizePos = stream.Position;
        writer.Write(0);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write(48000);
        writer.Write(48000 * 2);
        writer.Write((ushort)2);
        writer.Write((ushort)16);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(frames * 2);
        writer.Write(new byte[frames * 2]);

        writer.Write(Encoding.ASCII.GetBytes("LIST"));
        var listSizePos = stream.Position;
        writer.Write(0);
        var listStart = stream.Position;
        writer.Write(Encoding.ASCII.GetBytes("INFO"));
        WriteInfo(writer, "INAM", title);
        WriteInfo(writer, "IART", artist);
        WriteInfo(writer, "IPRD", album);
        WriteInfo(writer, "IGNR", genre);
        var listEnd = stream.Position;
        stream.Position = listSizePos;
        writer.Write((int)(listEnd - listStart));
        stream.Position = listEnd;

        var end = stream.Position;
        stream.Position = sizePos;
        writer.Write((int)(end - 8));
    }

    private static void WriteInfo(BinaryWriter writer, string id, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var size = bytes.Length + 1;
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(size);
        writer.Write(bytes);
        writer.Write((byte)0);
        if ((size & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteAiff(string path, int frames, string title, string artist)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("FORM"));
        var sizePos = stream.Position;
        WriteU32Be(writer, 0);
        writer.Write(Encoding.ASCII.GetBytes("AIFF"));

        writer.Write(Encoding.ASCII.GetBytes("COMM"));
        WriteU32Be(writer, 18);
        WriteU16Be(writer, 1);
        WriteU32Be(writer, (uint)frames);
        WriteU16Be(writer, 16);
        writer.Write(new byte[] { 0x40, 0x0E, 0xBB, 0x80, 0, 0, 0, 0, 0, 0 });

        WritePstringChunk(writer, "NAME", title);
        WritePstringChunk(writer, "AUTH", artist);

        writer.Write(Encoding.ASCII.GetBytes("SSND"));
        WriteU32Be(writer, (uint)(8 + frames * 2));
        WriteU32Be(writer, 0);
        WriteU32Be(writer, 0);
        writer.Write(new byte[frames * 2]);

        var end = stream.Position;
        stream.Position = sizePos;
        WriteU32Be(writer, (uint)(end - 8));
    }

    private static void WritePstringChunk(BinaryWriter writer, string id, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        var size = bytes.Length;
        writer.Write(Encoding.ASCII.GetBytes(id));
        WriteU32Be(writer, (uint)size);
        writer.Write(bytes);
        if ((size & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteU16Be(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU32Be(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
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
}
