using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ReloadEmbeddedMetaTests
{
    [Fact]
    public void ReloadEmbeddedMeta_AfterCueStrip_ClearsInMemoryMarkers()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-reload-cue-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, BuildWaveWithCueAndJunk(markerFrame: 10));
            var document = AudioCodec.Load(path);
            Assert.Single(document.Markers);
            Assert.Equal(10, document.Markers[0].Frame);

            var stripped = WaveChunkStripper.StripTo(
                path,
                path,
                new HashSet<string>(StringComparer.Ordinal) { "cue ", "JUNK" });
            Assert.True(stripped.Success);
            Assert.True(stripped.Changed);

            // 削除直後はメモリに古いマーカーが残る。
            Assert.Single(document.Markers);

            AudioCodec.ReloadEmbeddedMeta(document);
            Assert.Empty(document.Markers);
            Assert.False(document.IsDirty);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ReloadEmbeddedMeta_AfterJunkOnlyStrip_KeepsMarkersFromDisk()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-reload-junk-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, BuildWaveWithCueAndJunk(markerFrame: 12));
            var document = AudioCodec.Load(path);
            Assert.Single(document.Markers);

            var stripped = WaveChunkStripper.StripTo(
                path,
                path,
                new HashSet<string>(StringComparer.Ordinal) { "JUNK" });
            Assert.True(stripped.Success);
            Assert.True(stripped.Changed);

            AudioCodec.ReloadEmbeddedMeta(document);
            Assert.Single(document.Markers);
            Assert.Equal(12, document.Markers[0].Frame);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static byte[] BuildWaveWithCueAndJunk(uint markerFrame)
    {
        var cueBody = new List<byte>();
        cueBody.AddRange(BitConverter.GetBytes(1u));
        cueBody.AddRange(BitConverter.GetBytes(1u)); // cue id
        cueBody.AddRange(BitConverter.GetBytes(markerFrame));
        cueBody.AddRange(Encoding.ASCII.GetBytes("data"));
        cueBody.AddRange(BitConverter.GetBytes(0u));
        cueBody.AddRange(BitConverter.GetBytes(0u));
        cueBody.AddRange(BitConverter.GetBytes(markerFrame));

        var pcm = new byte[64];
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("JUNK", new byte[16]),
            RiffChunk("cue ", cueBody.ToArray()),
            RiffChunk("data", pcm));
        return Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
    }

    private static byte[] FmtBody() =>
    [
        1, 0, // PCM
        1, 0, // mono
        0x44, 0xAC, 0, 0, // 44100
        0x88, 0x58, 0x01, 0, // byte rate
        2, 0, // block align
        16, 0, // bits
    ];

    private static byte[] RiffChunk(string id, byte[] payload)
    {
        var padded = payload.Length + (payload.Length & 1);
        var chunk = new byte[8 + padded];
        Encoding.ASCII.GetBytes(id).CopyTo(chunk, 0);
        BitConverter.GetBytes((uint)payload.Length).CopyTo(chunk, 4);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var len = 0;
        foreach (var part in parts)
        {
            len += part.Length;
        }

        var result = new byte[len];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
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
        catch
        {
            // ignore
        }
    }
}
