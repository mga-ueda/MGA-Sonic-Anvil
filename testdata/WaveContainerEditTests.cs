using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveContainerEditTests
{
    [Fact]
    public void ReplaceWaveContainer_DoesNotTouchSourcePath_AndIsUndoable()
    {
        var source = Path.Combine(Path.GetTempPath(), "mga-wcedit-src-" + Guid.NewGuid().ToString("N") + ".wav");
        var working = Path.Combine(Path.GetTempPath(), "mga-wcedit-work-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            File.WriteAllBytes(source, BuildMinimalWave());
            File.WriteAllBytes(working, BuildMinimalWave());

            var document = AudioCodec.Load(source);
            Assert.Equal(source, document.SourcePath);
            Assert.Null(document.WaveContainerPath);
            Assert.Equal(source, document.ResolveWaveContainerPath());

            var history = new EditHistory();
            var command = ProcessEdits.ReplaceWaveContainer(
                document,
                beforeContainerPath: null,
                afterContainerPath: working,
                "Repair Header");
            Assert.NotNull(command);
            history.Do(document, command);

            Assert.True(document.IsDirty);
            Assert.Equal(source, document.SourcePath);
            Assert.Equal(working, document.WaveContainerPath);
            Assert.Equal(working, document.ResolveWaveContainerPath());
            Assert.True(File.Exists(source));

            history.Undo(document);
            Assert.Null(document.WaveContainerPath);
            Assert.Equal(source, document.ResolveWaveContainerPath());
        }
        finally
        {
            TryDelete(source);
            TryDelete(working);
        }
    }

    private static byte[] BuildMinimalWave()
    {
        var pcm = new byte[8];
        var fmt = new byte[]
        {
            1, 0, 1, 0, 0x44, 0xAC, 0, 0, 0x88, 0x58, 0x01, 0, 2, 0, 16, 0,
        };
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", fmt),
            RiffChunk("data", pcm));
        return Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
    }

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
