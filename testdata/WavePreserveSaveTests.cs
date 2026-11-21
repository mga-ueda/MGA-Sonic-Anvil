using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WavePreserveSaveTests
{
    [Fact]
    public void SaveWave_Overwrite_PreservesNonAudioChunks()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-preserve-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var bext = BuildMinimalBext(timeReference: 48000UL);
            File.WriteAllBytes(path, BuildWaveWithExtras(pcmFrames: 8, bext, junk: [9, 8, 7, 6]));

            var document = AudioCodec.Load(path);
            Assert.Contains("bext", ChunkIds(path));
            Assert.Contains("JUNK", ChunkIds(path));

            var history = new EditHistory();
            history.Do(document, ProcessEdits.ConvertBitDepth(document, 24)!);
            AudioCodec.SaveWave(document, path);

            var ids = ChunkIds(path);
            Assert.Contains("bext", ids);
            Assert.Contains("JUNK", ids);
            Assert.Contains("fmt ", ids);
            Assert.Contains("data", ids);

            var loaded = AudioCodec.Load(path);
            Assert.Equal(24, loaded.BitsPerSample);
            Assert.Equal(8, loaded.FrameCount);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void SaveWave_SampleRateConvert_ScalesBextTimeReference()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-preserve-rate-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(path, BuildWaveWithExtras(pcmFrames: 16, BuildMinimalBext(48000UL), junk: null));
            var document = AudioCodec.Load(path);
            var history = new EditHistory();
            history.Do(document, ProcessEdits.ConvertSampleRate(document, 24000)!);
            AudioCodec.SaveWave(document, path);

            var bext = ReadChunkPayload(path, "bext");
            Assert.NotNull(bext);
            // 48000 samples @ 48 kHz → 24000 samples @ 24 kHz
            Assert.Equal(24000u, BinaryPrimitives.ReadUInt32LittleEndian(bext.AsSpan(338)));
            Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(bext.AsSpan(342)));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void SaveWave_SaveAs_PreservesChunksFromSourcePath()
    {
        var source = Path.Combine(Path.GetTempPath(), "mga-preserve-src-" + Guid.NewGuid().ToString("N") + ".wav");
        var dest = Path.Combine(Path.GetTempPath(), "mga-preserve-dst-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(source, BuildWaveWithExtras(pcmFrames: 4, BuildMinimalBext(100UL), junk: [1, 2]));
            var document = AudioCodec.Load(source);
            var history = new EditHistory();
            var command = ProcessEdits.ConvertBitDepth(document, 8);
            Assert.NotNull(command);
            history.Do(document, command);
            AudioCodec.SaveWave(document, dest);

            Assert.Contains("bext", ChunkIds(dest));
            Assert.Contains("JUNK", ChunkIds(dest));
            Assert.Equal(8, AudioCodec.Load(dest).BitsPerSample);
        }
        finally
        {
            TryDelete(source);
            TryDelete(dest);
        }
    }

    private static byte[] BuildWaveWithExtras(int pcmFrames, byte[] bext, byte[]? junk)
    {
        var pcm = new byte[pcmFrames * 2]; // 16-bit mono
        var fmt = new byte[]
        {
            1, 0, 1, 0, 0x80, 0xBB, 0, 0, 0x00, 0x77, 0x01, 0, 2, 0, 16, 0,
        };
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("RIFF"));
        var sizePos = ms.Position;
        w.Write(0);
        w.Write(Encoding.ASCII.GetBytes("WAVE"));
        WriteChunk(w, "fmt ", fmt);
        WriteChunk(w, "bext", bext);
        if (junk is { Length: > 0 })
        {
            WriteChunk(w, "JUNK", junk);
        }

        WriteChunk(w, "data", pcm);
        var end = ms.Position;
        ms.Position = sizePos;
        w.Write((int)(end - 8));
        return ms.ToArray();
    }

    private static byte[] BuildMinimalBext(ulong timeReference)
    {
        var bext = new byte[602];
        Encoding.ASCII.GetBytes("desc").CopyTo(bext, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bext.AsSpan(338), (uint)(timeReference & 0xFFFFFFFFu));
        BinaryPrimitives.WriteUInt32LittleEndian(bext.AsSpan(342), (uint)(timeReference >> 32));
        BinaryPrimitives.WriteUInt16LittleEndian(bext.AsSpan(346), 1); // version
        return bext;
    }

    private static void WriteChunk(BinaryWriter w, string id, byte[] payload)
    {
        w.Write(Encoding.ASCII.GetBytes(id));
        w.Write(payload.Length);
        w.Write(payload);
        if ((payload.Length & 1) != 0)
        {
            w.Write((byte)0);
        }
    }

    private static List<string> ChunkIds(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var ids = new List<string>();
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4));
            ids.Add(id);
            pos += 8 + (int)size + ((int)size & 1);
        }

        return ids;
    }

    private static byte[]? ReadChunkPayload(string path, string fourCc)
    {
        var bytes = File.ReadAllBytes(path);
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 4));
            var start = pos + 8;
            if (id == fourCc)
            {
                return bytes.AsSpan(start, (int)size).ToArray();
            }

            pos = start + (int)size + ((int)size & 1);
        }

        return null;
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
