using System.Buffers.Binary;
using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveChannelLayoutWriterTests
{
    [Fact]
    public void ApplyWave_SixteenBitMaskPromotesToExtensible()
    {
        var path = TempPath("16mask", ".wav");
        try
        {
            WritePlainPcmWave(path, channels: 6, bits: 16, frames: 8);
            Assert.Equal(0, DevicePortNames.ReadWaveFileChannelMask(path));

            var result = WaveChannelLayoutWriter.ApplyTo(path, path, 0x3F);
            Assert.True(result.Success);
            Assert.True(result.Changed);
            Assert.Equal(0x3F, DevicePortNames.ReadWaveFileChannelMask(path));

            var fmt = ReadWaveFmt(path);
            Assert.Equal(0xFFFE, fmt.FormatTag);
            Assert.Equal(40, fmt.ChunkSize);
            Assert.Equal(16, fmt.Bits);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ApplyWave_ClearMaskDemotesSixteenBitToPlain()
    {
        var path = TempPath("16clear", ".wav");
        try
        {
            WritePlainPcmWave(path, channels: 6, bits: 16, frames: 8);
            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x3F).Changed);
            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0).Changed);
            Assert.Equal(0, DevicePortNames.ReadWaveFileChannelMask(path));

            var fmt = ReadWaveFmt(path);
            Assert.Equal(1, fmt.FormatTag);
            Assert.Equal(16, fmt.ChunkSize);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ApplyWave_TwentyFourBitRoundTrip()
    {
        var path = TempPath("24mask", ".wav");
        try
        {
            var document = new AudioDocument(new float[48], 48000, 6, 24, AudioFileKind.Wave, null);
            AudioCodec.SaveWave(document, path);
            Assert.Equal(0, DevicePortNames.ReadWaveFileChannelMask(path));

            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x3B).Changed);
            Assert.Equal(0x3B, DevicePortNames.ReadWaveFileChannelMask(path));

            var loaded = AudioCodec.Load(path);
            Assert.Equal(0x3B, loaded.ChannelMask);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ApplyWave_OddMaskAllowed()
    {
        var path = TempPath("oddmask", ".wav");
        try
        {
            WritePlainPcmWave(path, channels: 5, bits: 16, frames: 4);
            // 4 bits for 5ch — 変則でも書ける。
            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0xF).Changed);
            Assert.Equal(0xF, DevicePortNames.ReadWaveFileChannelMask(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ApplyAiff_InsertReplaceAndClearChan()
    {
        var path = TempPath("aiff-chan", ".aiff");
        try
        {
            WriteMinimalAiff(path, channels: 6, frames: 4);
            Assert.Equal(0, DevicePortNames.ReadAiffFileChannelMask(path));

            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x3F).Changed);
            Assert.Equal(0x3F, DevicePortNames.ReadAiffFileChannelMask(path));
            Assert.Contains("CHAN", ChunkIdsBe(path));

            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x60F).Changed);
            Assert.Equal(0x60F, DevicePortNames.ReadAiffFileChannelMask(path));

            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0).Changed);
            Assert.Equal(0, DevicePortNames.ReadAiffFileChannelMask(path));
            Assert.DoesNotContain("CHAN", ChunkIdsBe(path));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Load_ReadsAiffChanMask()
    {
        var path = TempPath("aiff-load", ".aiff");
        try
        {
            WriteMinimalAiff(path, channels: 6, frames: 8);
            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x3F).Changed);
            var loaded = AudioCodec.Load(path);
            Assert.Equal(0x3F, loaded.ChannelMask);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ApplyMany_MixedWaveAndAiff()
    {
        var wave = TempPath("batch-w", ".wav");
        var aiff = TempPath("batch-a", ".aiff");
        try
        {
            WritePlainPcmWave(wave, channels: 6, bits: 16, frames: 4);
            WriteMinimalAiff(aiff, channels: 6, frames: 4);
            var batch = WaveChannelLayoutWriter.ApplyMany([wave, aiff], 0x3F);
            Assert.Equal(2, batch.Succeeded);
            Assert.Equal(2, batch.Changed);
            Assert.Equal(0, batch.Failed);
            Assert.Equal(0x3F, DevicePortNames.ReadWaveFileChannelMask(wave));
            Assert.Equal(0x3F, DevicePortNames.ReadAiffFileChannelMask(aiff));
        }
        finally
        {
            TryDelete(wave);
            TryDelete(aiff);
        }
    }

    [Fact]
    public void ChannelLayoutMasks_FiveOneAndFourOne()
    {
        Assert.True(ChannelLayoutMasks.TryGetMask("5.1", out var fiveOne));
        Assert.Equal(0x3F, fiveOne);
        Assert.True(ChannelLayoutMasks.TryGetMask("4.1", out var fourOne));
        Assert.Equal(0x3B, fourOne);
        Assert.True(ChannelLayoutMasks.TryGetMask("5.1-Side", out var side));
        Assert.Equal(0x60F, side);
    }

    [Fact]
    public void ChannelMaskBits_BitsFromMask_FollowsAscendingOrder()
    {
        Assert.Equal([0x1, 0x2, 0x4, 0x8, 0x10, 0x20], ChannelMaskBits.BitsFromMask(0x3F, 6));
        Assert.Equal([0x1, 0x2, 0x4, 0x8, 0x200, 0x400], ChannelMaskBits.BitsFromMask(0x60F, 6));
        Assert.True(ChannelMaskBits.IsValidWaveChannelOrder([0x1, 0x2, 0x4]));
        Assert.False(ChannelMaskBits.IsValidWaveChannelOrder([0x4, 0x1]));
        Assert.False(ChannelMaskBits.IsValidWaveChannelOrder([0x1, 0x1]));
    }

    [Fact]
    public void FilterTargetsForMask_KeepsMatchingChannelCount()
    {
        var targets = new[]
        {
            new WaveChannelLayoutWriter.TargetInfo("a.wav", 6, 0, "WAVE"),
            new WaveChannelLayoutWriter.TargetInfo("b.wav", 5, 0, "WAVE"),
            new WaveChannelLayoutWriter.TargetInfo("c.wav", 6, 0x3F, "WAVE"),
        };
        var filtered = WaveChannelLayoutWriter.FilterTargetsForMask(targets, 0x3F);
        Assert.Equal(2, filtered.Count);
        Assert.All(filtered, t => Assert.Equal(6, t.Channels));
        Assert.Equal(3, WaveChannelLayoutWriter.FilterTargetsForMask(targets, 0).Count);
    }

    [Fact]
    public void SaveWave_SixteenBitWithMaskWritesExtensible()
    {
        var document = new AudioDocument(new float[48], 48000, 6, 16, AudioFileKind.Wave, null);
        document.SetChannelMask(0x3F);
        var path = TempPath("save16mask", ".wav");
        try
        {
            AudioCodec.SaveWave(document, path);
            Assert.Equal(0x3F, DevicePortNames.ReadWaveFileChannelMask(path));
            var fmt = ReadWaveFmt(path);
            Assert.Equal(0xFFFE, fmt.FormatTag);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void DecodeAiffChan_ShowsBitmap()
    {
        var path = TempPath("chan-decode", ".aiff");
        try
        {
            WriteMinimalAiff(path, channels: 2, frames: 2);
            Assert.True(WaveChannelLayoutWriter.ApplyTo(path, path, 0x3).Changed);
            var doc = WaveContainerStructure.TryRead(path);
            Assert.NotNull(doc);
            var chan = FindChunk(doc!.Roots, "CHAN");
            Assert.NotNull(chan);
            var text = WaveContainerStructure.EnsureContent(path, chan!).Content;
            Assert.Contains("0x00000003", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("UseChannelBitmap", text, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    private static void WritePlainPcmWave(string path, int channels, int bits, int frames)
    {
        var blockAlign = channels * (bits / 8);
        var dataSize = frames * blockAlign;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write((ushort)channels);
        writer.Write(48000);
        writer.Write(48000 * blockAlign);
        writer.Write((ushort)blockAlign);
        writer.Write((ushort)bits);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    private static void WriteMinimalAiff(string path, int channels, int frames)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        writer.Write(Encoding.ASCII.GetBytes("FORM"));
        var sizePos = stream.Position;
        WriteU32Be(writer, 0);
        writer.Write(Encoding.ASCII.GetBytes("AIFF"));

        writer.Write(Encoding.ASCII.GetBytes("COMM"));
        WriteU32Be(writer, 18);
        WriteU16Be(writer, (ushort)channels);
        WriteU32Be(writer, (uint)frames);
        WriteU16Be(writer, 16);
        writer.Write(new byte[] { 0x40, 0x0E, 0xBB, 0x80, 0, 0, 0, 0, 0, 0 });

        var ssndPayload = 8 + frames * channels * 2;
        writer.Write(Encoding.ASCII.GetBytes("SSND"));
        WriteU32Be(writer, (uint)ssndPayload);
        WriteU32Be(writer, 0);
        WriteU32Be(writer, 0);
        writer.Write(new byte[frames * channels * 2]);

        var end = stream.Position;
        stream.Position = sizePos;
        WriteU32Be(writer, (uint)(end - 8));
    }

    private static (int ChunkSize, int FormatTag, int Bits) ReadWaveFmt(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        _ = reader.ReadUInt32();
        Assert.Equal("WAVE", Encoding.ASCII.GetString(reader.ReadBytes(4)));
        while (stream.Position + 8 <= stream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var tag = reader.ReadUInt16();
                _ = reader.ReadUInt16();
                _ = reader.ReadInt32();
                _ = reader.ReadInt32();
                _ = reader.ReadUInt16();
                var bits = reader.ReadUInt16();
                return (size, tag, bits);
            }

            stream.Position += size + (size & 1);
        }

        throw new InvalidDataException("fmt missing");
    }

    private static List<string> ChunkIdsBe(string path)
    {
        var ids = new List<string>();
        var bytes = File.ReadAllBytes(path);
        var pos = 12;
        while (pos + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, pos, 4);
            var size = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(pos + 4));
            ids.Add(id);
            pos = pos + 8 + (int)size + ((int)size & 1);
        }

        return ids;
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

    private static WaveStructureNode? FindChunk(IReadOnlyList<WaveStructureNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id)
            {
                return node;
            }

            var nested = FindChunk(node.Children, id);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string TempPath(string tag, string ext) =>
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
