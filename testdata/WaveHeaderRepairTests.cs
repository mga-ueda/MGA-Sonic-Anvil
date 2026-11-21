using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveHeaderRepairTests
{
    [Fact]
    public void NeedsRepair_True_WhenTruncatedChunk()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(BitConverter.GetBytes(100u));
        bytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        bytes.AddRange(Encoding.ASCII.GetBytes("fmt "));
        bytes.AddRange(BitConverter.GetBytes(16u));
        bytes.AddRange(new byte[16]);
        bytes.AddRange(Encoding.ASCII.GetBytes("data"));
        bytes.AddRange(BitConverter.GetBytes(5000u));
        bytes.AddRange(new byte[8]);

        var path = WriteTemp("repair-need.wav", bytes.ToArray());
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            Assert.True(WaveHeaderRepair.NeedsRepair(doc));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RepairTo_ClampsTruncatedData_AndClearsErrors()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(BitConverter.GetBytes(100u));
        bytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        bytes.AddRange(Encoding.ASCII.GetBytes("fmt "));
        bytes.AddRange(BitConverter.GetBytes(16u));
        bytes.AddRange(FmtBody());
        bytes.AddRange(Encoding.ASCII.GetBytes("data"));
        bytes.AddRange(BitConverter.GetBytes(5000u));
        bytes.AddRange(new byte[8]);

        var source = WriteTemp("repair-trunc-src.wav", bytes.ToArray());
        var dest = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-repair-trunc-out.wav");
        try
        {
            var result = WaveHeaderRepair.RepairTo(source, dest);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Changed);
            Assert.True(File.Exists(dest));

            var doc = WaveContainerStructure.TryRead(dest);
            Assert.False(doc.HasErrors);
            Assert.False(WaveHeaderRepair.NeedsRepair(doc));
            var data = Assert.Single(doc.Roots[0].Children, c => c.Id == "data");
            Assert.Equal(8, data.Size);
            Assert.False(data.IsInvalid);
        }
        finally
        {
            File.Delete(source);
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
    }

    [Fact]
    public void RepairTo_FixesBrokenAdtlLabels()
    {
        // 実ファイルと同型: 4番目 labl の size が次ヘッダを飲み込み、5番目が非整列。
        var labl1 = RiffChunk("labl", Concat(BitConverter.GetBytes(1u), Encoding.UTF8.GetBytes("書き出し\0")));
        var labl2 = RiffChunk("labl", Concat(BitConverter.GetBytes(2u), Encoding.ASCII.GetBytes("A\0")));
        var labl3 = RiffChunk("labl", Concat(BitConverter.GetBytes(3u), Encoding.ASCII.GetBytes("B\0")));

        // 壊れた続き: size=23 の labl の中に次の labl ヘッダが埋まる。
        var brokenTail = Concat(
            Encoding.ASCII.GetBytes("labl"),
            BitConverter.GetBytes(23u),
            BitConverter.GetBytes(4u),
            new byte[] { 0x00, 0x30, 0x33, 0x5F, 0x81, 0x8D, 0xE5, 0x87, 0xBA, 0x00, 0x00 },
            Encoding.ASCII.GetBytes("labl"),
            BitConverter.GetBytes(23u),
            BitConverter.GetBytes(5u),
            new byte[] { 0x00, 0x30, 0x33, 0x5F, 0x81, 0x8D, 0xE5, 0x87, 0xBA, 0x00, 0x00 });

        var adtl = Concat(
            Encoding.ASCII.GetBytes("adtl"),
            labl1,
            labl2,
            labl3,
            brokenTail);

        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("LIST", adtl),
            RiffChunk("data", new byte[64]));
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        var source = WriteTemp("repair-adtl-src.wav", file);
        var dest = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-repair-adtl-out.wav");
        try
        {
            var before = WaveContainerStructure.TryRead(source);
            Assert.True(before.HasErrors);
            Assert.True(WaveHeaderRepair.NeedsRepair(before));

            var result = WaveHeaderRepair.RepairTo(source, dest);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Changed);

            var after = WaveContainerStructure.TryRead(dest);
            Assert.False(after.HasErrors);
            Assert.False(WaveHeaderRepair.NeedsRepair(after));
            var list = Assert.Single(after.Roots[0].Children, c => c.Id == "LIST");
            Assert.Equal(5, list.Children.Count);
            Assert.All(list.Children, c => Assert.False(c.IsInvalid));
            Assert.All(list.Children, c => Assert.Equal("labl", c.Id));
        }
        finally
        {
            File.Delete(source);
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
    }

    [Fact]
    public void RepairTo_Overwrite_DropsBlankFourCc()
    {
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("data", new byte[32]),
            new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
        var path = WriteTemp("repair-blank.wav", file);
        try
        {
            var before = WaveContainerStructure.TryRead(path);
            Assert.True(WaveHeaderRepair.NeedsRepair(before));

            var result = WaveHeaderRepair.RepairTo(path, path);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Changed);

            var after = WaveContainerStructure.TryRead(path);
            Assert.False(WaveHeaderRepair.NeedsRepair(after));
            Assert.DoesNotContain(after.Roots[0].Children, c => WaveStructureNode.IsBlankFourCc(c.Id));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RepairTo_RealSunflowerSample_ClearsAdtlErrors_WhenPresent()
    {
        var src = @"V:\共有ドライブ\Project Sunflower\C-Garden\from C-Garden\2025\20251127\春エリア（大神産み後）.wav";
        if (!File.Exists(src))
        {
            return;
        }

        var dest = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-spring-repaired.wav");
        try
        {
            var before = WaveContainerStructure.TryRead(src);
            Assert.True(before.HasErrors);
            Assert.True(WaveHeaderRepair.NeedsRepair(before));

            var result = WaveHeaderRepair.RepairTo(src, dest);
            Assert.True(result.Success, result.Error);
            Assert.True(result.Changed);

            var after = WaveContainerStructure.TryRead(dest);
            Assert.False(after.HasErrors);
            Assert.False(WaveHeaderRepair.NeedsRepair(after));
            var list = Assert.Single(after.Roots[0].Children, c => c.Id == "LIST");
            Assert.True(list.Children.Count >= 4);
            Assert.All(list.Children, c => Assert.False(c.IsInvalid));
            Assert.Contains(after.Roots[0].Children, c => c.Id == "data" && !c.IsInvalid);
        }
        finally
        {
            if (File.Exists(dest))
            {
                File.Delete(dest);
            }
        }
    }

    [Fact]
    public void CollectRepairTargets_ReturnsOnlyBrokenWaveFiles()
    {
        var brokenBytes = new List<byte>();
        brokenBytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        brokenBytes.AddRange(BitConverter.GetBytes(100u));
        brokenBytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        brokenBytes.AddRange(Encoding.ASCII.GetBytes("fmt "));
        brokenBytes.AddRange(BitConverter.GetBytes(16u));
        brokenBytes.AddRange(FmtBody());
        brokenBytes.AddRange(Encoding.ASCII.GetBytes("data"));
        brokenBytes.AddRange(BitConverter.GetBytes(5000u));
        brokenBytes.AddRange(new byte[8]);

        var ok = WriteTemp("repair-collect-ok.wav", BuildOkWave());
        var bad = WriteTemp("repair-collect-bad.wav", brokenBytes.ToArray());
        var mp3 = WriteTemp("repair-collect-skip.mp3", Encoding.ASCII.GetBytes("ID3x"));
        try
        {
            var targets = WaveHeaderRepair.CollectRepairTargets([ok, bad, mp3, bad]);
            Assert.Equal([bad], targets);
        }
        finally
        {
            File.Delete(ok);
            File.Delete(bad);
            File.Delete(mp3);
        }
    }

    [Fact]
    public void RepairMany_AsCopy_WritesDistinctOutputs()
    {
        var broken = BuildTruncatedWave();
        var a = WriteTemp("repair-many-a.wav", broken);
        var b = WriteTemp("repair-many-b.wav", broken);
        try
        {
            var batch = WaveHeaderRepair.RepairMany([a, b], overwrite: false);
            Assert.Equal(2, batch.Succeeded);
            Assert.Equal(0, batch.Failed);
            Assert.Equal(2, batch.Changed);
            Assert.Equal(2, batch.WrittenPaths.Count);
            Assert.All(batch.WrittenPaths, path => Assert.True(File.Exists(path)));
            Assert.NotEqual(batch.WrittenPaths[0], batch.WrittenPaths[1], StringComparer.OrdinalIgnoreCase);
            Assert.All(
                batch.WrittenPaths,
                path => Assert.False(WaveHeaderRepair.NeedsRepair(WaveContainerStructure.TryRead(path))));
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
            foreach (var leftover in Directory.GetFiles(Path.GetDirectoryName(a)!, "*_repaired*.wav"))
            {
                try { File.Delete(leftover); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void SuggestRepairedPath_UsesSuffixAndAvoidsCollision()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mga-anvil-repair-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "sample.wav");
            File.WriteAllBytes(source, [1]);
            var first = WaveHeaderRepair.SuggestRepairedPath(source);
            Assert.Equal(
                Path.Combine(dir, "sample" + UiStrings.WaveHeaderRepairFileSuffix + ".wav"),
                first);
            File.WriteAllBytes(first, [1]);
            var second = WaveHeaderRepair.SuggestRepairedPath(source);
            Assert.Equal(
                Path.Combine(dir, "sample" + UiStrings.WaveHeaderRepairFileSuffix + " 2.wav"),
                second);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string WriteTemp(string name, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static byte[] BuildOkWave()
    {
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("data", new byte[32]));
        return Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
    }

    private static byte[] BuildTruncatedWave()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(BitConverter.GetBytes(100u));
        bytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        bytes.AddRange(Encoding.ASCII.GetBytes("fmt "));
        bytes.AddRange(BitConverter.GetBytes(16u));
        bytes.AddRange(FmtBody());
        bytes.AddRange(Encoding.ASCII.GetBytes("data"));
        bytes.AddRange(BitConverter.GetBytes(5000u));
        bytes.AddRange(new byte[8]);
        return bytes.ToArray();
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
