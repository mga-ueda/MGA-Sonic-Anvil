using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class EmbeddedMetaTests
{
    [Fact]
    public void Load_ReadsCueMarkersAndSkipsRegions()
    {
        var path = TempPath("cue");
        try
        {
            WriteWave(
                path,
                frames: 80,
                cues:
                [
                    new Cue(1, 10, "Intro", 0),
                    new Cue(2, 40, "Region", 16),
                    new Cue(3, 60, "-L", 0),
                ]);

            var document = AudioCodec.Load(path);
            Assert.Equal(2, document.Markers.Count);
            Assert.Equal(10, document.Markers[0].Frame);
            Assert.Equal("Intro", document.Markers[0].Comment);
            Assert.Equal(60, document.Markers[1].Frame);
            Assert.Equal("-L", document.Markers[1].Comment);
            Assert.True(document.SampleLoop.IsEmpty);
            Assert.False(document.IsDirty);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_ReadsSmplLoopAsSampleLoopNotMarkers()
    {
        var path = TempPath("smpl");
        try
        {
            WriteWave(path, frames: 80, smpl: (12, 31));

            var document = AudioCodec.Load(path);
            Assert.Empty(document.Markers);
            Assert.Equal(new WaveSelection(12, 32), document.SampleLoop);
            Assert.False(document.IsDirty);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_ReadsCueAndSmplTogether()
    {
        var path = TempPath("both");
        try
        {
            WriteWave(
                path,
                frames: 100,
                cues: [new Cue(1, 8, "Hit", 0)],
                smpl: (20, 49));

            var document = AudioCodec.Load(path);
            Assert.Single(document.Markers);
            Assert.Equal(8, document.Markers[0].Frame);
            Assert.Equal("Hit", document.Markers[0].Comment);
            Assert.Equal(new WaveSelection(20, 50), document.SampleLoop);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_ReadsAiffMarkersAndSustainLoop()
    {
        var path = TempPath("aiff", ".aiff");
        try
        {
            WriteAiff(path, frames: 40, markStart: 5, markEnd: 25);

            var document = AudioCodec.Load(path);
            Assert.Equal(2, document.Markers.Count);
            Assert.Equal(5, document.Markers[0].Frame);
            Assert.Equal("start", document.Markers[0].Comment);
            Assert.Equal(25, document.Markers[1].Frame);
            Assert.Equal("end", document.Markers[1].Comment);
            Assert.Equal(new WaveSelection(5, 25), document.SampleLoop);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private readonly record struct Cue(uint Id, uint Frame, string Label, uint RegionLength);

    private static void WriteWave(
        string path,
        int frames,
        Cue[]? cues = null,
        (uint Start, uint InclusiveEnd)? smpl = null)
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

        if (cues is { Length: > 0 })
        {
            writer.Write(Encoding.ASCII.GetBytes("cue "));
            writer.Write(4 + cues.Length * 24);
            writer.Write(cues.Length);
            foreach (var cue in cues)
            {
                writer.Write(cue.Id);
                writer.Write(cue.Frame);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(0);
                writer.Write(0);
                writer.Write(cue.Frame);
            }

            writer.Write(Encoding.ASCII.GetBytes("LIST"));
            var listSizePos = stream.Position;
            writer.Write(0);
            var listStart = stream.Position;
            writer.Write(Encoding.ASCII.GetBytes("adtl"));
            foreach (var cue in cues)
            {
                var labelBytes = Encoding.ASCII.GetBytes(cue.Label);
                var lablSize = 4 + labelBytes.Length + 1;
                writer.Write(Encoding.ASCII.GetBytes("labl"));
                writer.Write(lablSize);
                writer.Write(cue.Id);
                writer.Write(labelBytes);
                writer.Write((byte)0);
                if ((lablSize & 1) != 0)
                {
                    writer.Write((byte)0);
                }

                if (cue.RegionLength <= 0)
                {
                    continue;
                }

                writer.Write(Encoding.ASCII.GetBytes("ltxt"));
                writer.Write(20);
                writer.Write(cue.Id);
                writer.Write(cue.RegionLength);
                writer.Write(0);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write(0);
            }

            var listSize = (int)(stream.Position - listStart);
            var here = stream.Position;
            stream.Position = listSizePos;
            writer.Write(listSize);
            stream.Position = here;
        }

        if (smpl is { } loop)
        {
            writer.Write(Encoding.ASCII.GetBytes("smpl"));
            writer.Write(60);
            writer.Write(new byte[28]);
            writer.Write(1);
            writer.Write(0);
            writer.Write(1u);
            writer.Write(0u);
            writer.Write(loop.Start);
            writer.Write(loop.InclusiveEnd);
            writer.Write(0);
            writer.Write(0);
        }

        var end = stream.Position;
        stream.Position = sizePos;
        writer.Write((int)(end - 8));
    }

    private static void WriteAiff(string path, int frames, uint markStart, uint markEnd)
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

        writer.Write(Encoding.ASCII.GetBytes("SSND"));
        WriteU32Be(writer, (uint)(8 + frames * 2));
        WriteU32Be(writer, 0);
        WriteU32Be(writer, 0);
        writer.Write(new byte[frames * 2]);

        writer.Write(Encoding.ASCII.GetBytes("MARK"));
        var markSizePos = stream.Position;
        WriteU32Be(writer, 0);
        var markStartPos = stream.Position;
        WriteU16Be(writer, 2);
        WriteMarker(writer, 1, markStart, "start");
        WriteMarker(writer, 2, markEnd, "end");
        var markSize = (uint)(stream.Position - markStartPos);
        var afterMark = stream.Position;
        stream.Position = markSizePos;
        WriteU32Be(writer, markSize);
        stream.Position = afterMark;
        if ((markSize & 1) != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write(Encoding.ASCII.GetBytes("INST"));
        WriteU32Be(writer, 20);
        writer.Write(new byte[] { 60, 0, 0, 127, 0, 127, 0, 0 });
        WriteU16Be(writer, 1);
        WriteU16Be(writer, 1);
        WriteU16Be(writer, 2);
        WriteU16Be(writer, 0);
        WriteU16Be(writer, 0);
        WriteU16Be(writer, 0);

        var end = stream.Position;
        stream.Position = sizePos;
        WriteU32Be(writer, (uint)(end - 8));
    }

    private static void WriteMarker(BinaryWriter writer, short id, uint frame, string name)
    {
        WriteU16Be(writer, (ushort)id);
        WriteU32Be(writer, frame);
        var bytes = Encoding.ASCII.GetBytes(name);
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
        if (((1 + bytes.Length) & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteU16Be(BinaryWriter writer, ushort value)
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

    private static string TempPath(string tag, string ext = ".wav") =>
        Path.Combine(Path.GetTempPath(), $"sonic-anvil-{tag}-{Guid.NewGuid():N}{ext}");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
