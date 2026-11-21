using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveChunkStripperTests
{
    [Fact]
    public void TryStripBytes_RemovesJunkAndKeepsAudio()
    {
        var pcm = new byte[64];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (byte)i;
        }

        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("JUNK", new byte[32]),
            RiffChunk("bext", new byte[12]),
            RiffChunk("data", pcm));
        var source = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        Assert.True(
            WaveChunkStripper.TryStripBytes(
                source,
                new HashSet<string>(StringComparer.Ordinal) { "JUNK", "bext" },
                out var rewritten,
                out var error,
                out var changed));
        Assert.Null(error);
        Assert.True(changed);

        var path = WriteTemp("strip-junk.wav", rewritten);
        try
        {
            var read = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(read.Roots);
            Assert.Contains(root.Children, static c => c.Id == "fmt ");
            Assert.Contains(root.Children, static c => c.Id == "data");
            Assert.DoesNotContain(root.Children, static c => c.Id == "JUNK");
            Assert.DoesNotContain(root.Children, static c => c.Id == "bext");
            var data = Assert.Single(root.Children, static c => c.Id == "data");
            Assert.Equal(64, data.Size);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryStripBytes_RemovesFact()
    {
        var pcm = new byte[16];
        var fact = BitConverter.GetBytes(8); // dwSampleLength
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("fact", fact),
            RiffChunk("data", pcm));
        var source = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        Assert.True(
            WaveChunkStripper.TryStripBytes(
                source,
                new HashSet<string>(StringComparer.Ordinal) { "fact" },
                out var rewritten,
                out var error,
                out var changed));
        Assert.Null(error);
        Assert.True(changed);

        var path = WriteTemp("strip-fact.wav", rewritten);
        try
        {
            var read = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(read.Roots);
            Assert.DoesNotContain(root.Children, static c => c.Id == "fact");
            Assert.Contains(root.Children, static c => c.Id == "fmt ");
            Assert.Contains(root.Children, static c => c.Id == "data");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryStripBytes_IgnoresForbiddenFmt()
    {
        var pcm = new byte[16];
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("data", pcm));
        var source = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        Assert.True(
            WaveChunkStripper.TryStripBytes(
                source,
                new HashSet<string>(StringComparer.Ordinal) { "fmt " },
                out var rewritten,
                out var error,
                out var changed));
        Assert.Null(error);
        Assert.False(changed);
        Assert.Equal(source.Length, rewritten.Length);
    }

    [Fact]
    public void TryStripBytes_FiltersInfoListChildren()
    {
        var infoBody = Concat(
            Encoding.ASCII.GetBytes("INFO"),
            RiffChunk("INAM", Encoding.ASCII.GetBytes("Title\0")),
            RiffChunk("IART", Encoding.ASCII.GetBytes("Artist\0")));
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("LIST", infoBody),
            RiffChunk("data", new byte[8]));
        var source = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        Assert.True(
            WaveChunkStripper.TryStripBytes(
                source,
                new HashSet<string>(StringComparer.Ordinal) { "IART" },
                out var rewritten,
                out var error,
                out var changed));
        Assert.Null(error);
        Assert.True(changed);

        var path = WriteTemp("strip-iart.wav", rewritten);
        try
        {
            var read = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(read.Roots);
            var list = Assert.Single(root.Children, static c => c.Id == "LIST");
            Assert.Contains(list.Children, static c => c.Id == "INAM");
            Assert.DoesNotContain(list.Children, static c => c.Id == "IART");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTemp(string name, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] FmtBody(int sampleRate = 44100, int channels = 1, int bits = 16)
    {
        var blockAlign = channels * (bits / 8);
        var byteRate = sampleRate * blockAlign;
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
        {
            w.Write((ushort)1);
            w.Write((ushort)channels);
            w.Write(sampleRate);
            w.Write(byteRate);
            w.Write((ushort)blockAlign);
            w.Write((ushort)bits);
        }

        return ms.ToArray();
    }

    private static byte[] RiffChunk(string fourCc, byte[] payload)
    {
        var pad = (payload.Length & 1) != 0 ? new byte[] { 0 } : Array.Empty<byte>();
        return Concat(
            Encoding.ASCII.GetBytes(fourCc),
            BitConverter.GetBytes((uint)payload.Length),
            payload,
            pad);
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var len = 0;
        foreach (var p in parts)
        {
            len += p.Length;
        }

        var result = new byte[len];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, o, p.Length);
            o += p.Length;
        }

        return result;
    }
}
