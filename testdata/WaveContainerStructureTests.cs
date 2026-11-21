using System.IO;
using System.Text;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveContainerStructureTests
{
    [Fact]
    public void TryRead_Wave_BuildsFmtAndTruncatesData()
    {
        var pcm = new byte[2048];
        for (var i = 0; i < pcm.Length; i++)
        {
            pcm[i] = (byte)(i & 0xFF);
        }

        var path = WriteTemp("struct-ok.wav", BuildPcmWave(pcm, sampleRate: 44100, channels: 1, bits: 16));
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            Assert.Equal("WAVE", doc.ContainerKind);
            Assert.False(doc.HasErrors);
            Assert.False(doc.IsUnsupported);
            Assert.Single(doc.Roots);
            var root = doc.Roots[0];
            Assert.Contains(root.Children, c => c.Id == "fmt ");
            var data = Assert.Single(root.Children, c => c.Id == "data");
            Assert.Equal(WaveStructureContentKind.Binary, data.Kind);
            Assert.True(data.ContentPending);
            Assert.Equal(2048, data.Size);
            var loaded = WaveContainerStructure.EnsureContent(path, data);
            Assert.False(loaded.ContentPending);
            Assert.True(loaded.ContentTruncated);
            Assert.Contains(UiStrings.WaveStructureDataTruncated(2048, WaveContainerStructure.DataPreviewPageBytes), loaded.Content);
            Assert.Contains("00000000", loaded.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Wave_LeavesChunkPayloadPendingUntilEnsureContent()
    {
        var path = WriteTemp("struct-pending.wav", BuildPcmWave(new byte[32], sampleRate: 44100, channels: 1, bits: 16));
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(doc.Roots);
            Assert.False(root.ContentPending);
            Assert.All(
                root.Children,
                static c => Assert.True(c.ContentPending || c.IsInvalid));
            Assert.All(root.Children, static c => Assert.True(string.IsNullOrEmpty(c.Content) || c.IsInvalid));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Wave_ReportsTruncatedChunk()
    {
        // RIFF size claims more than file; last chunk header size overruns.
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        bytes.AddRange(BitConverter.GetBytes(100u));
        bytes.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        bytes.AddRange(Encoding.ASCII.GetBytes("fmt "));
        bytes.AddRange(BitConverter.GetBytes(16u));
        bytes.AddRange(new byte[16]);
        bytes.AddRange(Encoding.ASCII.GetBytes("data"));
        bytes.AddRange(BitConverter.GetBytes(5000u)); // larger than remaining
        bytes.AddRange(new byte[8]);

        var path = WriteTemp("struct-bad.wav", bytes.ToArray());
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            Assert.True(doc.HasErrors);
            Assert.Contains(doc.Errors, e => e.Contains("data", StringComparison.Ordinal));
            var data = doc.Roots[0].Children.FirstOrDefault(c => c.Id == "data" && c.IsInvalid);
            Assert.NotNull(data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Wave_ParsesInfoListAndXml()
    {
        var infoBody = Concat(
            Encoding.ASCII.GetBytes("INFO"),
            RiffChunk("INAM", Encoding.Latin1.GetBytes("Title\0")),
            RiffChunk("IART", Encoding.Latin1.GetBytes("Artist\0")));
        var xml = Encoding.UTF8.GetBytes("<SPEED><SPEED_AUDIO>48</SPEED_AUDIO></SPEED>");
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("LIST", infoBody),
            RiffChunk("iXML", xml),
            RiffChunk("data", new byte[64]));
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        var path = WriteTemp("struct-meta.wav", file);
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            Assert.False(doc.HasErrors);
            var root = Assert.Single(doc.Roots);
            var list = Assert.Single(root.Children, c => c.Id == "LIST");
            Assert.True(list.ContentPending);
            var inam = Assert.Single(list.Children, c => c.Id == "INAM");
            Assert.True(inam.ContentPending);
            var inamLoaded = WaveContainerStructure.EnsureContent(path, inam);
            Assert.Contains("Title", inamLoaded.Content);
            var ixml = Assert.Single(root.Children, c => c.Id == "iXML");
            Assert.Equal(WaveStructureContentKind.Xml, ixml.Kind);
            Assert.True(ixml.ContentPending);
            var ixmlLoaded = WaveContainerStructure.EnsureContent(path, ixml);
            Assert.Contains("SPEED_AUDIO", ixmlLoaded.Content);
            Assert.Contains("    <SPEED_AUDIO>", ixmlLoaded.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Mp3_IsUnsupported()
    {
        var path = WriteTemp("struct.bin.mp3", Encoding.ASCII.GetBytes("ID3notreally"));
        try
        {
            // 拡張子で Mp3 と判定される。
            var doc = WaveContainerStructure.TryRead(path);
            Assert.True(doc.IsUnsupported);
            Assert.Empty(doc.Roots);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BuildChunkUnion_MergesDistinctFourCcs_SkipsUnsupported()
    {
        var pcm = new byte[64];
        var fmt = FmtBody();
        var withBext = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes(0u), // placeholder
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", fmt),
            RiffChunk("bext", new byte[10]),
            RiffChunk("data", pcm));
        BitConverter.GetBytes((uint)(withBext.Length - 8)).CopyTo(withBext, 4);

        var waveA = WriteTemp("union-a.wav", BuildPcmWave(pcm, sampleRate: 44100, channels: 1, bits: 16));
        var waveB = WriteTemp("union-b.wav", withBext);
        var mp3 = WriteTemp("union-skip.mp3", Encoding.ASCII.GetBytes("ID3x"));
        try
        {
            var doc = WaveContainerStructure.BuildChunkUnion([waveA, waveB, mp3]);
            Assert.True(doc.IsChunkUnion);
            Assert.Empty(doc.Errors);
            var byId = doc.Roots.ToDictionary(static n => n.Id, static n => n.Size);
            Assert.Equal(2, byId["RIFF"]);
            Assert.Equal(2, byId["fmt "]);
            Assert.Equal(2, byId["data"]);
            Assert.Equal(1, byId["bext"]);
            Assert.Equal(
                byId.Keys.OrderBy(static x => x, StringComparer.Ordinal).ToArray(),
                doc.Roots.Select(static n => n.Id).ToArray());
            Assert.All(doc.Roots, static n => Assert.Empty(n.Children));
            var bext = doc.Roots.First(static n => n.Id == "bext");
            Assert.Equal("bext - 1", bext.FormatUnionTreeCaption(4, 1));
            Assert.Equal([waveB], bext.MemberPaths);
            Assert.Equal(2, doc.Roots.First(static n => n.Id == "fmt ").MemberPaths.Count);
        }
        finally
        {
            File.Delete(waveA);
            File.Delete(waveB);
            File.Delete(mp3);
        }
    }

    [Fact]
    public void BuildChunkUnion_CollapsesDistinctBlankFourCcs()
    {
        // NUL×4 と空白×4 は表示が同じ **** なので和集合では1行にまとめる。
        static byte[] WithBlank(byte[] blankId)
        {
            var body = Concat(
                Encoding.ASCII.GetBytes("WAVE"),
                RiffChunk("fmt ", FmtBody()),
                RiffChunk("data", new byte[16]),
                blankId,
                BitConverter.GetBytes(0));
            return Concat(
                Encoding.ASCII.GetBytes("RIFF"),
                BitConverter.GetBytes((uint)body.Length),
                body);
        }

        var waveNul = WriteTemp("union-blank-nul.wav", WithBlank([0, 0, 0, 0]));
        var waveSpc = WriteTemp("union-blank-spc.wav", WithBlank([(byte)' ', (byte)' ', (byte)' ', (byte)' ']));
        try
        {
            var doc = WaveContainerStructure.BuildChunkUnion([waveNul, waveSpc]);
            var blanks = doc.Roots.Where(static n => WaveStructureNode.IsBlankFourCc(n.Id)).ToArray();
            var blank = Assert.Single(blanks);
            Assert.Equal(WaveStructureNode.BlankFourCcCanonicalId, blank.Id);
            Assert.Equal(2, blank.Size);
            Assert.True(blank.IsInvalid);
            Assert.Equal(
                WaveStructureNode.BlankFourCcListLabel + " - 2",
                blank.FormatUnionTreeCaption(4, 1));
            Assert.Equal(2, blank.MemberPaths.Count);
            Assert.Equal(
                WaveStructureNode.BlankFourCcCanonicalId,
                WaveStructureNode.UnionChunkKey("    "));
            Assert.Equal(
                WaveStructureNode.BlankFourCcCanonicalId,
                WaveStructureNode.UnionChunkKey("\0\0\0\0"));
        }
        finally
        {
            File.Delete(waveNul);
            File.Delete(waveSpc);
        }
    }

    [Fact]
    public void BuildChunkUnion_PutsTruncatedRepairTargetsUnderBlankRow()
    {
        // 切り詰め data は FourCC が "data" のままなので、修復対象として先頭 **** にも載せる。
        var truncated = new List<byte>();
        truncated.AddRange(Encoding.ASCII.GetBytes("RIFF"));
        truncated.AddRange(BitConverter.GetBytes(100u));
        truncated.AddRange(Encoding.ASCII.GetBytes("WAVE"));
        truncated.AddRange(Encoding.ASCII.GetBytes("fmt "));
        truncated.AddRange(BitConverter.GetBytes(16u));
        truncated.AddRange(FmtBody());
        truncated.AddRange(Encoding.ASCII.GetBytes("data"));
        truncated.AddRange(BitConverter.GetBytes(5000u));
        truncated.AddRange(new byte[8]);

        var bad = WriteTemp("union-trunc.wav", truncated.ToArray());
        var good = WriteTemp("union-ok.wav", BuildPcmWave(new byte[64], sampleRate: 44100, channels: 1, bits: 16));
        try
        {
            Assert.True(WaveHeaderRepair.NeedsRepair(WaveContainerStructure.TryRead(bad)));
            Assert.False(WaveHeaderRepair.NeedsRepair(WaveContainerStructure.TryRead(good)));

            var doc = WaveContainerStructure.BuildChunkUnion([bad, good]);
            Assert.Equal(WaveStructureNode.BlankFourCcListLabel, "****");
            Assert.True(WaveStructureNode.IsBlankFourCc(doc.Roots[0].Id));
            var blank = Assert.Single(doc.Roots, static n => WaveStructureNode.IsBlankFourCc(n.Id));
            Assert.Same(doc.Roots[0], blank);
            Assert.Equal(1, blank.Size);
            Assert.True(blank.IsInvalid);
            Assert.Equal(
                WaveStructureNode.BlankFourCcListLabel + " - 1",
                blank.FormatUnionTreeCaption(4, 1));
            Assert.Contains(
                Path.GetFullPath(bad),
                blank.MemberPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                Path.GetFullPath(good),
                blank.MemberPaths.Select(Path.GetFullPath),
                StringComparer.OrdinalIgnoreCase);

            // 通常の data 行には両方載る。
            var data = Assert.Single(doc.Roots, static n => n.Id == "data");
            Assert.Equal(2, data.Size);
        }
        finally
        {
            File.Delete(bad);
            File.Delete(good);
        }
    }

    [Fact]
    public void FormatHexDump_GroupsSixteenBytes()
    {
        var text = WaveContainerStructure.FormatHexDump(new byte[] { 0x52, 0x49, 0x46, 0x46 }, 0);
        Assert.StartsWith("00000000", text);
        Assert.Contains("52 49 46 46", text);
        Assert.Contains("RIFF", text);
    }

    [Fact]
    public void FormatXml_UsesFourSpaceIndent()
    {
        var doc = System.Xml.Linq.XDocument.Parse("<ROOT><CHILD>x</CHILD></ROOT>");
        var text = WaveContainerStructure.FormatXml(doc);
        Assert.Contains("\n    <CHILD>x</CHILD>", text.Replace("\r\n", "\n"));
        Assert.DoesNotContain("\n  <CHILD>", text.Replace("\r\n", "\n"));
    }

    [Fact]
    public void FormatFourCc_LeftAlignsWithinFourChars()
    {
        Assert.Equal("fmt ", WaveStructureNode.FormatFourCc("fmt "));
        Assert.Equal("fmt ", WaveStructureNode.FormatFourCc("fmt"));
        Assert.Equal("cue ", WaveStructureNode.FormatFourCc("cue "));
        Assert.Equal("data", WaveStructureNode.FormatFourCc("data"));
        Assert.Equal("iXML", WaveStructureNode.FormatFourCc("iXML"));
        Assert.Equal("x   ", WaveStructureNode.FormatFourCc("x"));
        Assert.Equal("????", WaveStructureNode.FormatFourCc("\0\0\0\0"));
        Assert.Equal("a?c ", WaveStructureNode.FormatFourCc("a\0c"));
        Assert.Equal("    ", WaveStructureNode.FormatFourCc(string.Empty));
    }

    [Fact]
    public void TryRead_Wave_NullFourCcChunk_IsMarkedInvalidEmpty()
    {
        // data のあとに 00 00 00 00 / size 0（実ファイル 12_15_05.WAV と同型）。
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("data", new byte[32]),
            new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 });
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
        var path = WriteTemp("struct-nul-fourcc.wav", file);
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            Assert.DoesNotContain(
                doc.Errors,
                e => e.Contains("FourCC", StringComparison.OrdinalIgnoreCase)
                    || e.Contains("チャンク名", StringComparison.Ordinal));
            var root = Assert.Single(doc.Roots);
            var nul = Assert.Single(root.Children, c => c.Id == "\0\0\0\0");
            Assert.Equal(0, nul.Size);
            Assert.True(nul.IsInvalid);
            Assert.False(nul.ContentPending);
            Assert.Null(nul.Error);
            Assert.Equal(UiStrings.WaveStructureEmptyPayload, nul.Content);
            Assert.True(WaveStructureNode.IsBlankFourCc(nul.Id));
            Assert.Equal(WaveStructureNode.BlankFourCcListLabel, nul.FormatDisplayFourCc());
            Assert.Contains(
                WaveStructureNode.BlankFourCcListLabel,
                nul.FormatTreeCaption(0, 4, 4),
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WellKnownAndCustomChunkFlags()
    {
        Assert.True(WaveStructureNode.IsWellKnownChunkId("fmt "));
        Assert.True(WaveStructureNode.IsCoreChunkId("fmt "));
        Assert.True(WaveStructureNode.IsWellKnownChunkId("iXML"));
        Assert.True(WaveStructureNode.IsSpecializedChunkId("iXML"));
        Assert.True(WaveStructureNode.IsWellKnownChunkId("COMM"));
        Assert.False(WaveStructureNode.IsWellKnownChunkId("xyz "));
        Assert.False(WaveStructureNode.IsWellKnownChunkId("resu"));

        var known = new WaveStructureNode(
            "fmt ", "fmt ", 0, 16, WaveStructureContentKind.Text, "x", false, false, null);
        Assert.False(known.IsCustom);
        Assert.False(known.IsSpecialized);
        Assert.False(known.IsInvalid);

        var specialized = new WaveStructureNode(
            "iXML", "iXML", 0, 8, WaveStructureContentKind.Xml, "<a/>", false, false, null);
        Assert.False(specialized.IsCustom);
        Assert.True(specialized.IsSpecialized);

        var custom = new WaveStructureNode(
            "xyz ", "xyz ", 0, 8, WaveStructureContentKind.Binary, "x", false, false, null);
        Assert.True(custom.IsCustom);
        Assert.False(custom.IsSpecialized);
        Assert.False(custom.IsInvalid);

        var invalid = new WaveStructureNode(
            "data", "data", 0, 8, WaveStructureContentKind.Text, "bad", false, true, "bad");
        Assert.True(invalid.IsInvalid);
        Assert.False(invalid.IsCustom);
    }

    [Fact]
    public void FormatVisibleNewlines_ShowsCrLfSymbols()
    {
        var shown = WaveStructureNode.FormatVisibleNewlines("a\r\nb\nc\rd");
        Assert.Contains("␍␊", shown);
        Assert.Contains("␊", shown);
        Assert.Contains("␍", shown);
        Assert.Contains("a", shown);
        Assert.Contains("b", shown);
    }

    [Fact]
    public void FormatKeyValueTable_RightAlignsKeys_LeftAlignsValues()
    {
        var text = WaveStructureNode.FormatKeyValueTable(
        [
            ("A", "1"),
            ("LongKey", "2048"),
        ]);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal(lines[0].IndexOf('│'), lines[1].IndexOf('│'));
        Assert.StartsWith("      A  │", lines[0]);
        Assert.StartsWith("LongKey  │", lines[1]);
        Assert.EndsWith("│ 1", lines[0]);
        Assert.EndsWith("│ 2048", lines[1]);
    }

    [Fact]
    public void FormatNodeDetail_IncludesOffsetSizeAndBody()
    {
        var node = new WaveStructureNode(
            "fmt ",
            "Format",
            offset: 12,
            size: 16,
            WaveStructureContentKind.Text,
            content: "Audio format  │ 1",
            contentTruncated: false,
            isInvalid: false,
            error: null);
        var text = WaveStructureNode.FormatNodeDetail(node);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        Assert.Contains(lines, static l => l.Contains("Offset", StringComparison.Ordinal));
        Assert.Contains(lines, static l => l.Contains("Size", StringComparison.Ordinal));
        Assert.Contains(lines, static l => l.Contains("Audio format", StringComparison.Ordinal));
        Assert.Contains("0xC (12)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatDetailWithMeta_AlignsPipeAcrossSections()
    {
        var body = WaveStructureNode.FormatKeyValueTable(
        [
            ("Container", "RIFF"),
            ("Declared size", "26993346"),
        ]);
        var text = WaveStructureNode.FormatDetailWithMeta(
        [
            ("Offset", "0x0 (0)"),
            ("Size", "1 B"),
        ],
        body);
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Where(static l => l.Contains('│', StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(4, lines.Length);
        var pipe = lines[0].IndexOf('│');
        Assert.All(lines, line => Assert.Equal(pipe, line.IndexOf('│')));
        Assert.StartsWith("Declared size  │", lines[3]);
        Assert.StartsWith("       Offset  │", lines[0]);
    }

    [Fact]
    public void FormatGrid_RightAlignsNumericColumns()
    {
        var text = WaveStructureNode.FormatGrid(
            ["Name", "Size"],
            [
                ["fmt", "16"],
                ["data", "2048"],
            ]);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        Assert.True(lines.Length >= 3);
        Assert.EndsWith("  16", lines[2]);
        Assert.EndsWith("2048", lines[3]);
        Assert.True(WaveStructureNode.LooksNumericCell("1,234"));
        Assert.True(WaveStructureNode.LooksNumericCell("0xFF"));
        Assert.False(WaveStructureNode.LooksNumericCell("data"));
    }

    [Fact]
    public void TryRead_Wave_CueAndLabelShowMarkerFields()
    {
        var cueBody = new List<byte>();
        cueBody.AddRange(BitConverter.GetBytes(1u));
        cueBody.AddRange(BitConverter.GetBytes(7u)); // cue id
        cueBody.AddRange(BitConverter.GetBytes(100u)); // position
        cueBody.AddRange(Encoding.ASCII.GetBytes("data"));
        cueBody.AddRange(BitConverter.GetBytes(0u));
        cueBody.AddRange(BitConverter.GetBytes(0u));
        cueBody.AddRange(BitConverter.GetBytes(100u));

        var labelPayload = new List<byte>();
        labelPayload.AddRange(BitConverter.GetBytes(7u));
        labelPayload.AddRange(Encoding.Latin1.GetBytes("Mark\r\nTwo\0"));

        var adtl = Concat(
            Encoding.ASCII.GetBytes("adtl"),
            RiffChunk("labl", labelPayload.ToArray()));
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("cue ", cueBody.ToArray()),
            RiffChunk("LIST", adtl),
            RiffChunk("data", new byte[32]));
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        var path = WriteTemp("struct-cue.wav", file);
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(doc.Roots);
            var cue = WaveContainerStructure.EnsureContent(
                path,
                Assert.Single(root.Children, c => c.Id == "cue "));
            Assert.Contains("CueId", cue.Content);
            Assert.Contains("7", cue.Content);
            var list = Assert.Single(root.Children, c => c.Id == "LIST");
            var labl = WaveContainerStructure.EnsureContent(
                path,
                Assert.Single(list.Children, c => c.Id == "labl"));
            Assert.Contains("Cue ID", labl.Content);
            Assert.Contains("7", labl.Content);
            Assert.Contains("Mark", labl.Content);
            Assert.Contains("␍␊", labl.Content);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Wave_MarksProprietaryChunkAsCustom()
    {
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("resu", new byte[12]),
            RiffChunk("data", new byte[32]));
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        var path = WriteTemp("struct-custom.wav", file);
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(doc.Roots);
            var custom = Assert.Single(root.Children, c => c.Id == "resu");
            Assert.True(custom.IsCustom);
            Assert.False(custom.IsInvalid);
            Assert.False(Assert.Single(root.Children, c => c.Id == "fmt ").IsCustom);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryRead_Wave_NonDataBinaryShowsFullWithoutTruncationMessage()
    {
        // data より大きい独自チャンクでも、続きメッセージは付けない。
        var payload = new byte[600];
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)(i & 0xFF);
        }

        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", FmtBody()),
            RiffChunk("resu", payload),
            RiffChunk("data", new byte[32]));
        var file = Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);

        var path = WriteTemp("struct-full-custom.wav", file);
        try
        {
            var doc = WaveContainerStructure.TryRead(path);
            var root = Assert.Single(doc.Roots);
            var custom = WaveContainerStructure.EnsureContent(
                path,
                Assert.Single(root.Children, c => c.Id == "resu"));
            Assert.False(custom.ContentTruncated);
            Assert.DoesNotContain(
                UiStrings.WaveStructureDataTruncated(600, WaveContainerStructure.DataPreviewPageBytes),
                custom.Content);
            Assert.Contains("00000250", custom.Content); // offset past the data-preview page size
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ForTreeDisplay_ContainerRootBecomesSingleLine()
    {
        var fmt = new WaveStructureNode(
            "fmt ",
            "fmt",
            12,
            16,
            WaveStructureContentKind.Text,
            "x",
            false,
            false,
            null);
        var listChild = new WaveStructureNode(
            "INFO",
            "INFO",
            100,
            8,
            WaveStructureContentKind.Text,
            "i",
            false,
            false,
            null);
        var list = new WaveStructureNode(
            "LIST",
            "LIST / INFO",
            80,
            32,
            WaveStructureContentKind.Text,
            "l",
            false,
            false,
            null,
            [listChild]);
        var root = new WaveStructureNode(
            "RIFF",
            "RIFF / WAVE",
            0,
            1_234_567,
            WaveStructureContentKind.Text,
            "y",
            false,
            false,
            null,
            [fmt, list]);

        var display = WaveStructureNode.ForTreeDisplay([root]);
        Assert.Equal(3, display.Count);
        Assert.Equal("RIFF", display[0].Id);
        Assert.Empty(display[0].Children);
        Assert.Equal("fmt ", display[1].Id);
        Assert.Same(list, display[2]);
        Assert.Single(display[2].Children);

        var flat = new WaveStructureNode(
            "bext",
            "bext",
            0,
            10,
            WaveStructureContentKind.Text,
            "b",
            false,
            false,
            null);
        Assert.Same(flat, Assert.Single(WaveStructureNode.ForTreeDisplay([flat])));
    }

    [Fact]
    public void FormatTreeCaption_AlignsColumnsIncludingIndent()
    {
        var child = new WaveStructureNode(
            "fmt",
            "fmt",
            12,
            16,
            WaveStructureContentKind.Text,
            "x",
            false,
            false,
            null);
        var root = new WaveStructureNode(
            "RIFF",
            "RIFF / WAVE",
            0,
            1_234_567,
            WaveStructureContentKind.Text,
            "y",
            false,
            false,
            null,
            [child]);
        var sizeWidth = WaveStructureNode.MaxSizeFieldWidth([root]);
        var nameWidth = WaveStructureNode.NameFieldWidth(WaveStructureNode.MaxDepth([root]));
        var a = root.FormatTreeCaption(0, nameWidth, sizeWidth);
        var b = child.FormatTreeCaption(1, nameWidth, sizeWidth);
        // "name - size" なので - は nameFieldWidth+1 桁目で縦に揃う。
        Assert.Equal(nameWidth + 1, a.IndexOf('-'));
        Assert.Equal(a.IndexOf('-'), b.IndexOf('-'));
        Assert.Equal(a.Length, b.Length);
        Assert.StartsWith("RIFF", a, StringComparison.Ordinal);
        Assert.StartsWith("  fmt ", b, StringComparison.Ordinal);
        Assert.EndsWith(WaveStructureNode.FormatSize(1_234_567), a);
        Assert.EndsWith(WaveStructureNode.FormatSize(16), b);
    }

    private static string WriteTemp(string name, byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-anvil-" + Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllBytes(path, bytes);
        return path!;
    }

    private static byte[] BuildPcmWave(byte[] pcm, int sampleRate, int channels, int bits)
    {
        var fmt = FmtBody(sampleRate, channels, bits);
        var body = Concat(
            Encoding.ASCII.GetBytes("WAVE"),
            RiffChunk("fmt ", fmt),
            RiffChunk("data", pcm));
        return Concat(
            Encoding.ASCII.GetBytes("RIFF"),
            BitConverter.GetBytes((uint)body.Length),
            body);
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
