using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Vst3PluginScannerTests : IDisposable
{
    private readonly string _dir;

    public Vst3PluginScannerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "mga-vst3-scan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Vst3PluginList.ResetForTests();
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Scan_FindsBundleFoldersAndSingleFilesRecursively()
    {
        var root = Path.Combine(_dir, "VST3");
        var bundle = Path.Combine(root, "Vendor", "Alpha.vst3");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "x86_64-win"));
        // バンドル内部の .vst3 は別プラグインとして数えない。
        File.WriteAllBytes(Path.Combine(bundle, "Contents", "x86_64-win", "Alpha.vst3"), [0]);
        File.WriteAllBytes(Path.Combine(root, "Beta.vst3"), [0]);
        File.WriteAllBytes(Path.Combine(root, "readme.txt"), [0]);
        Directory.CreateDirectory(Path.Combine(root, "Empty"));

        var found = Vst3PluginScanner.Scan([root]);

        Assert.Equal(["Alpha", "Beta"], found.Select(p => p.Name).ToArray());
        Assert.Equal(Path.GetFullPath(bundle), found[0].Path);
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "Beta.vst3")), found[1].Path);
        Assert.All(found, p => Assert.NotEqual(DateTimeOffset.MinValue, p.LastWriteUtc));
    }

    [Fact]
    public void Scan_DeduplicatesOverlappingRootsCaseInsensitive()
    {
        var root = Path.Combine(_dir, "Plugins");
        var sub = Path.Combine(root, "Sub");
        Directory.CreateDirectory(sub);
        File.WriteAllBytes(Path.Combine(sub, "Gamma.vst3"), [0]);

        var found = Vst3PluginScanner.Scan([root, sub, root.ToUpperInvariant() + Path.DirectorySeparatorChar]);

        Assert.Single(found);
        Assert.Equal("Gamma", found[0].Name);
    }

    [Fact]
    public void Scan_SkipsMissingRootsAndAcceptsVst3PathAsRoot()
    {
        var bundle = Path.Combine(_dir, "Direct.vst3");
        Directory.CreateDirectory(bundle);

        var found = Vst3PluginScanner.Scan([Path.Combine(_dir, "does-not-exist"), bundle, "  "]);

        Assert.Single(found);
        Assert.Equal("Direct", found[0].Name);
    }

    [Fact]
    public void Scan_ExtensionMatchIsCaseInsensitive()
    {
        File.WriteAllBytes(Path.Combine(_dir, "Upper.VST3"), [0]);

        var found = Vst3PluginScanner.Scan([_dir]);

        Assert.Single(found);
        Assert.Equal("Upper", found[0].Name);
    }

    [Fact]
    public void PluginList_SaveLoadClear_RoundTrips()
    {
        Vst3PluginList.UseDirectoryForTests(_dir);
        Assert.Null(Vst3PluginList.Load().ScannedAtUtc);

        var scannedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);
        var doc = Vst3PluginList.FromScan(
            [
                Scanned("Alpha", @"C:\VST3\Alpha.vst3", vendor: "Acme", category: "Fx|Reverb"),
                Scanned("Broken", @"C:\VST3\Broken.vst3", failure: Vst3PluginMetadata.FailureEmptyBundle),
            ],
            scannedAt);
        Assert.True(Vst3PluginList.Save(doc));

        var loaded = Vst3PluginList.Load();
        Assert.Equal(scannedAt, loaded.ScannedAtUtc);
        Assert.Equal(2, loaded.Plugins.Length);
        Assert.Equal("Alpha", loaded.Plugins[0].Name);
        Assert.Equal(@"C:\VST3\Alpha.vst3", loaded.Plugins[0].Path);
        Assert.Equal("Acme", loaded.Plugins[0].Vendor);
        Assert.Equal("Fx|Reverb", loaded.Plugins[0].Category);
        Assert.Equal(Vst3RecognitionKind.Recognized, loaded.Plugins[0].Recognition);
        Assert.Equal(Vst3RecognitionKind.Failed, loaded.Plugins[1].Recognition);
        Assert.Equal(Vst3PluginMetadata.FailureEmptyBundle, loaded.Plugins[1].FailureReason);

        Assert.True(Vst3PluginList.Clear());
        Assert.False(File.Exists(Vst3PluginList.FilePath));
        Assert.Empty(Vst3PluginList.Load().Plugins);
    }

    [Fact]
    public void PluginList_CorruptFile_LoadsEmpty()
    {
        Vst3PluginList.UseDirectoryForTests(_dir);
        File.WriteAllText(Vst3PluginList.FilePath, "{ not json");

        var loaded = Vst3PluginList.Load();

        Assert.Null(loaded.ScannedAtUtc);
        Assert.Empty(loaded.Plugins);
    }

    [Fact]
    public void PluginList_OldJsonWithoutNewFields_LoadsWithDefaults()
    {
        Vst3PluginList.UseDirectoryForTests(_dir);
        File.WriteAllText(
            Vst3PluginList.FilePath,
            """
            {
              "ScannedAtUtc": "2026-11-01T00:00:00+00:00",
              "Plugins": [
                { "Name": "Alpha", "Path": "C:\\VST3\\Alpha.vst3", "LastWriteUtc": "2026-10-01T00:00:00+00:00" }
              ]
            }
            """);

        var loaded = Vst3PluginList.Load();

        Assert.Single(loaded.Plugins);
        var entry = loaded.Plugins[0];
        Assert.Equal("Alpha", entry.Name);
        Assert.Equal(string.Empty, entry.Vendor);
        Assert.Equal(string.Empty, entry.Category);
        Assert.Equal(Vst3RecognitionKind.Recognized, entry.Recognition);
        Assert.Null(entry.FailureReason);
    }

    [Fact]
    public void MergeScan_PreservesManualExcludedAndPromotesRecoveredFailures()
    {
        var previous = new Vst3PluginListDocument
        {
            Plugins =
            [
                new Vst3PluginEntry { Name = "Manual", Path = @"C:\VST3\Manual.vst3", Recognition = Vst3RecognitionKind.ManualExcluded },
                new Vst3PluginEntry { Name = "WasFailed", Path = @"C:\VST3\WasFailed.vst3", Recognition = Vst3RecognitionKind.Failed, FailureReason = "empty-bundle" },
                new Vst3PluginEntry { Name = "Gone", Path = @"C:\VST3\Gone.vst3", Recognition = Vst3RecognitionKind.ManualExcluded },
            ],
        };
        var scannedAt = new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero);

        var merged = Vst3PluginList.MergeScan(
            previous,
            [
                // パスの大文字小文字が違っても同じプラグイン。
                Scanned("Manual", @"C:\vst3\MANUAL.vst3"),
                Scanned("WasFailed", @"C:\VST3\WasFailed.vst3", vendor: "Acme"),
                Scanned("NewBroken", @"C:\VST3\NewBroken.vst3", failure: Vst3PluginMetadata.FailureEmptyBundle),
                Scanned("NewOk", @"C:\VST3\NewOk.vst3"),
            ],
            scannedAt);

        Assert.Equal(scannedAt, merged.ScannedAtUtc);
        Assert.Equal(4, merged.Plugins.Length);
        var byName = merged.Plugins.ToDictionary(p => p.Name);
        Assert.Equal(Vst3RecognitionKind.ManualExcluded, byName["Manual"].Recognition);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["WasFailed"].Recognition);
        Assert.Null(byName["WasFailed"].FailureReason);
        Assert.Equal(Vst3RecognitionKind.Failed, byName["NewBroken"].Recognition);
        Assert.Equal(Vst3PluginMetadata.FailureEmptyBundle, byName["NewBroken"].FailureReason);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["NewOk"].Recognition);
        Assert.False(byName.ContainsKey("Gone"));
    }

    [Fact]
    public void MergeScan_ExcludesInstrumentCategories()
    {
        var previous = new Vst3PluginListDocument
        {
            Plugins =
            [
                // 強制認識しても次のスキャンで楽器は再び除外する。
                new Vst3PluginEntry
                {
                    Name = "ForcedSynth",
                    Path = @"C:\VST3\ForcedSynth.vst3",
                    Recognition = Vst3RecognitionKind.Recognized,
                    Category = "Instrument|Synth",
                },
                new Vst3PluginEntry
                {
                    Name = "KeepManual",
                    Path = @"C:\VST3\KeepManual.vst3",
                    Recognition = Vst3RecognitionKind.ManualExcluded,
                    Category = "Instrument",
                },
            ],
        };

        var merged = Vst3PluginList.MergeScan(
            previous,
            [
                Scanned("ForcedSynth", @"C:\VST3\ForcedSynth.vst3", category: "Instrument|Synth"),
                Scanned("KeepManual", @"C:\VST3\KeepManual.vst3", category: "Instrument"),
                Scanned("Reverb", @"C:\VST3\Reverb.vst3", category: "Fx|Reverb"),
                Scanned("FxInstrument", @"C:\VST3\FxInstrument.vst3", category: "Fx|Instrument"),
                Scanned("BareInstrument", @"C:\VST3\BareInstrument.vst3", category: "Instrument"),
            ],
            DateTimeOffset.UtcNow);

        var byName = merged.Plugins.ToDictionary(p => p.Name);
        Assert.Equal(Vst3RecognitionKind.InstrumentExcluded, byName["ForcedSynth"].Recognition);
        Assert.Equal(Vst3RecognitionKind.ManualExcluded, byName["KeepManual"].Recognition);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["Reverb"].Recognition);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["FxInstrument"].Recognition);
        Assert.Equal(Vst3RecognitionKind.InstrumentExcluded, byName["BareInstrument"].Recognition);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Fx|Reverb", false)]
    [InlineData("Fx|Instrument", false)]
    [InlineData("Fx · Instrument", false)]
    [InlineData("Instrument", true)]
    [InlineData("Instrument|Synth", true)]
    [InlineData("instrument|drum", true)]
    [InlineData("Instrument|Fx", true)]
    [InlineData("Instrument · Fx", true)]
    [InlineData("Instrument - Fx", true)]
    [InlineData("Instrument / Fx", true)]
    public void IsInstrumentCategory_MatchesSteinbergInstrumentRoot(string? category, bool expected) =>
        Assert.Equal(expected, Vst3PluginMetadata.IsInstrumentCategory(category));

    [Fact]
    public void MergeScan_ExcludesInstrumentFxAsInstrument()
    {
        var merged = Vst3PluginList.MergeScan(
            null,
            [
                Scanned("InstFx", @"C:\VST3\InstFx.vst3", category: "Instrument|Fx"),
                Scanned("FxInst", @"C:\VST3\FxInst.vst3", category: "Fx|Instrument"),
            ],
            DateTimeOffset.UtcNow);

        var byName = merged.Plugins.ToDictionary(p => p.Name);
        Assert.Equal(Vst3RecognitionKind.InstrumentExcluded, byName["InstFx"].Recognition);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["FxInst"].Recognition);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Fx|Reverb", false)]
    [InlineData("OnlyARA", true)]
    [InlineData("onlyara", true)]
    [InlineData("Fx|OnlyARA", true)]
    [InlineData("OnlyARA|Fx", true)]
    public void IsOnlyAraCategory_MatchesSteinbergOnlyAra(string? category, bool expected) =>
        Assert.Equal(expected, Vst3PluginMetadata.IsOnlyAraCategory(category));

    [Fact]
    public void MergeScan_ExcludesOnlyAraCategories()
    {
        var previous = new Vst3PluginListDocument
        {
            Plugins =
            [
                new Vst3PluginEntry
                {
                    Name = "ForcedAra",
                    Path = @"C:\VST3\ForcedAra.vst3",
                    Recognition = Vst3RecognitionKind.Recognized,
                    Category = "OnlyARA",
                },
            ],
        };

        var merged = Vst3PluginList.MergeScan(
            previous,
            [
                Scanned("ForcedAra", @"C:\VST3\ForcedAra.vst3", category: "OnlyARA"),
                Scanned("NestedAra", @"C:\VST3\NestedAra.vst3", category: "Fx|OnlyARA"),
                Scanned("Reverb", @"C:\VST3\Reverb.vst3", category: "Fx|Reverb"),
            ],
            DateTimeOffset.UtcNow);

        var byName = merged.Plugins.ToDictionary(p => p.Name);
        Assert.Equal(Vst3RecognitionKind.AraExcluded, byName["ForcedAra"].Recognition);
        Assert.Equal(Vst3RecognitionKind.AraExcluded, byName["NestedAra"].Recognition);
        Assert.Equal(Vst3RecognitionKind.Recognized, byName["Reverb"].Recognition);
    }

    [Fact]
    public void SetRecognized_DistinguishesManualFromFailedAndClearsReasonOnOverride()
    {
        var doc = Vst3PluginList.FromScan(
            [
                Scanned("Ok", @"C:\VST3\Ok.vst3"),
                Scanned("Broken", @"C:\VST3\Broken.vst3", failure: Vst3PluginMetadata.FailureEmptyBundle),
            ],
            DateTimeOffset.UtcNow);

        Assert.Equal(1, Vst3PluginList.SetRecognized(doc, [@"c:\vst3\ok.VST3"], recognized: false));
        Assert.Equal(Vst3RecognitionKind.ManualExcluded, doc.Plugins[0].Recognition);
        Assert.Equal(Vst3RecognitionKind.Failed, doc.Plugins[1].Recognition);

        // すでに認識しない側のものを除外しても変化なし（失敗は失敗のまま）。
        Assert.Equal(0, Vst3PluginList.SetRecognized(doc, [@"C:\VST3\Broken.vst3"], recognized: false));
        Assert.Equal(Vst3RecognitionKind.Failed, doc.Plugins[1].Recognition);

        Assert.Equal(2, Vst3PluginList.SetRecognized(doc, [@"C:\VST3\Ok.vst3", @"C:\VST3\Broken.vst3"], recognized: true));
        Assert.All(doc.Plugins, p => Assert.Equal(Vst3RecognitionKind.Recognized, p.Recognition));
        Assert.Null(doc.Plugins[1].FailureReason);
    }

    [Fact]
    public void Metadata_ReadsModuleInfoFromBundle()
    {
        var bundle = Path.Combine(_dir, "Demo.vst3");
        var resources = Path.Combine(bundle, "Contents", "Resources");
        Directory.CreateDirectory(resources);
        File.WriteAllText(
            Path.Combine(resources, "moduleinfo.json"),
            """
            {
              // 実物は JSON5 寄り。コメントと末尾カンマを許す。
              "Name": "Demo",
              "Version": "0.9",
              "Factory Info": { "Vendor": "Acme", },
              "Classes": [
                { "Name": "Demo Controller", "Category": "Component Controller Class", "Vendor": "Other" },
                { "Name": "Demo Reverb", "Category": "Audio Module Class", "Sub Categories": ["Fx", "Reverb"], "Version": "1.0.0" },
              ],
            }
            """);

        var meta = Vst3PluginMetadata.Read(bundle);

        Assert.False(meta.IsFailed);
        Assert.Equal("Demo Reverb", meta.Name);
        Assert.Equal("Acme", meta.Vendor);
        Assert.Equal("Fx|Reverb", meta.Category);
        Assert.Equal("1.0.0", meta.Version);
    }

    [Fact]
    public void Metadata_AcceptsSubCategoriesAsStringAndLooseKeys()
    {
        var bundle = Path.Combine(_dir, "Loose.vst3");
        var resources = Path.Combine(bundle, "Contents", "Resources");
        Directory.CreateDirectory(resources);
        File.WriteAllText(
            Path.Combine(resources, "moduleinfo.json"),
            """
            { "classes": [ { "name": "Loose Delay", "category": "Audio Module Class", "subCategories": "Fx|Delay", "vendor": "Beta" } ] }
            """);

        var meta = Vst3PluginMetadata.Read(bundle);

        Assert.False(meta.IsFailed);
        Assert.Equal("Loose Delay", meta.Name);
        Assert.Equal("Beta", meta.Vendor);
        Assert.Equal("Fx|Delay", meta.Category);
    }

    [Fact]
    public void Metadata_EmptyBundleFailsButBinaryOrSingleFileIsRecognized()
    {
        var empty = Path.Combine(_dir, "Empty.vst3");
        Directory.CreateDirectory(empty);
        Assert.Equal(Vst3PluginMetadata.FailureEmptyBundle, Vst3PluginMetadata.Read(empty).FailureReason);

        var corrupt = Path.Combine(_dir, "Corrupt.vst3");
        Directory.CreateDirectory(Path.Combine(corrupt, "Contents", "Resources"));
        File.WriteAllText(Path.Combine(corrupt, "Contents", "Resources", "moduleinfo.json"), "{ not json");
        Assert.Equal(Vst3PluginMetadata.FailureModuleInfoCorrupt, Vst3PluginMetadata.Read(corrupt).FailureReason);

        // moduleinfo が壊れていてもバイナリがあれば認識する（ブランド等は空でよい）。
        Directory.CreateDirectory(Path.Combine(corrupt, "Contents", "x86_64-win"));
        File.WriteAllBytes(Path.Combine(corrupt, "Contents", "x86_64-win", "Corrupt.vst3"), [0]);
        Assert.False(Vst3PluginMetadata.Read(corrupt).IsFailed);

        var single = Path.Combine(_dir, "Single.vst3");
        File.WriteAllBytes(single, [0]);
        Assert.False(Vst3PluginMetadata.Read(single).IsFailed);

        Assert.Equal(
            Vst3PluginMetadata.FailureMissing,
            Vst3PluginMetadata.Read(Path.Combine(_dir, "Nope.vst3")).FailureReason);
    }

    [Fact]
    public void ScanWithMetadata_EnrichesEachFoundPlugin()
    {
        var root = Path.Combine(_dir, "VST3");
        var bundle = Path.Combine(root, "Demo.vst3");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        File.WriteAllText(
            Path.Combine(bundle, "Contents", "Resources", "moduleinfo.json"),
            """{ "Factory Info": { "Vendor": "Acme" }, "Classes": [ { "Name": "Demo", "Category": "Audio Module Class", "Sub Categories": "Fx|Dynamics" } ] }""");
        Directory.CreateDirectory(Path.Combine(root, "Hollow.vst3"));

        var found = Vst3PluginScanner.ScanWithMetadata([root]);
        var doc = Vst3PluginList.FromScan(found, DateTimeOffset.UtcNow);

        Assert.Equal(2, doc.Plugins.Length);
        var demo = doc.Plugins.Single(p => p.Name == "Demo");
        Assert.Equal("Acme", demo.Vendor);
        Assert.Equal("Fx|Dynamics", demo.Category);
        Assert.Equal(Vst3RecognitionKind.Recognized, demo.Recognition);
        var hollow = doc.Plugins.Single(p => p.Name == "Hollow");
        Assert.Equal(Vst3RecognitionKind.Failed, hollow.Recognition);
    }

    [Fact]
    public void Metadata_CategoryFromEmbeddedUtf16StringInBundleBinary()
    {
        var bundle = Path.Combine(_dir, "Plain.vst3");
        var bin = Path.Combine(bundle, "Contents", "x86_64-win");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(
            Path.Combine(bin, "Plain.vst3"),
            [0x4D, 0x00, 0x00, 0x00, .. System.Text.Encoding.Unicode.GetBytes("Fx|Reverb"), 0x00, 0x00, 0x7F]);

        var meta = Vst3PluginMetadata.Read(bundle);

        Assert.False(meta.IsFailed);
        Assert.Equal("Fx|Reverb", meta.Category);
    }

    [Fact]
    public void Metadata_CategoryFromEmbeddedUtf8StringPrefersMostSpecific()
    {
        var single = Path.Combine(_dir, "Plain.vst3");
        File.WriteAllBytes(
            single,
            [
                0x01, 0x00, .. "Fx"u8, 0x00,
                .. "junkFx|Delayjunk"u8, 0x00,
                .. "Fx|Delay"u8, 0x00,
                0x02,
            ]);

        Assert.Equal("Fx|Delay", Vst3PluginMetadata.Read(single).Category);
    }

    [Fact]
    public void Metadata_ModuleInfoCategoryWinsOverBinary()
    {
        var bundle = Path.Combine(_dir, "Both.vst3");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "x86_64-win"));
        File.WriteAllText(
            Path.Combine(bundle, "Contents", "Resources", "moduleinfo.json"),
            """{ "Classes": [ { "Name": "Both", "Category": "Audio Module Class", "Sub Categories": "Fx|EQ" } ] }""");
        File.WriteAllBytes(Path.Combine(bundle, "Contents", "x86_64-win", "Both.vst3"), [0, .. "Fx|Reverb"u8, 0]);

        Assert.Equal("Fx|EQ", Vst3PluginMetadata.Read(bundle).Category);
    }

    [Fact]
    public void Metadata_ModuleInfoInResourcesSubfolderIsFound()
    {
        var bundle = Path.Combine(_dir, "Nested.vst3");
        var nested = Path.Combine(bundle, "Contents", "Resources", "en");
        Directory.CreateDirectory(nested);
        File.WriteAllText(
            Path.Combine(nested, "moduleinfo.json"),
            """{ "Classes": [ { "Name": "Nested Comp", "Category": "Audio Module Class", "Sub Categories": ["Fx", "Dynamics"] } ] }""");

        var meta = Vst3PluginMetadata.Read(bundle);

        Assert.False(meta.IsFailed);
        Assert.Equal("Nested Comp", meta.Name);
        Assert.Equal("Fx|Dynamics", meta.Category);
    }

    [Fact]
    public void Metadata_CategoryFromBundleNameWhenBinaryHasNoStrings()
    {
        var bundle = Path.Combine(_dir, "CoolReverb.vst3");
        var bin = Path.Combine(bundle, "Contents", "x86_64-win");
        Directory.CreateDirectory(bin);
        File.WriteAllBytes(Path.Combine(bin, "CoolReverb.dll"), [0, 1, 2, 3]);

        var meta = Vst3PluginMetadata.Read(bundle);

        Assert.False(meta.IsFailed);
        Assert.Equal("Fx|Reverb", meta.Category);
    }

    [Fact]
    public void Metadata_GenericEmbeddedCategoryOnlyRefinedByMatchingNameGuess()
    {
        var synth = Path.Combine(_dir, "MegaSynth.vst3");
        File.WriteAllBytes(synth, [0, .. "Instrument"u8, 0]);
        Assert.Equal("Instrument|Synth", Vst3PluginMetadata.Read(synth).Category);

        // 名前は Reverb でもバイナリが Instrument と言っているなら Instrument のまま。
        var odd = Path.Combine(_dir, "OddReverb.vst3");
        File.WriteAllBytes(odd, [0, .. "Instrument"u8, 0]);
        Assert.Equal("Instrument", Vst3PluginMetadata.Read(odd).Category);

        // NUL で区切られていない短い語は拾わない。
        var plain = Path.Combine(_dir, "Plain.vst3");
        File.WriteAllBytes(plain, [.. "xxFxyyInstrumental"u8]);
        Assert.Equal(string.Empty, Vst3PluginMetadata.Read(plain).Category);
    }

    [Theory]
    [InlineData("CoolReverb", null, "Fx|Reverb")]
    [InlineData("ValhallaVintageVerb", "Valhalla DSP, LLC", "Fx|Reverb")]
    [InlineData("EchoBoy", null, "Fx|Delay")]
    [InlineData("Pro-L 2 Limiter", null, "Fx|Dynamics")]
    [InlineData("Noise Gate", null, "Fx|Dynamics")]
    [InlineData("DeEsser", null, "Fx|Dynamics")]
    [InlineData("SSL Comp", null, "Fx|Dynamics")]
    [InlineData("EQ8", null, "Fx|EQ")]
    [InlineData("Linear Phase Equalizer", null, "Fx|EQ")]
    [InlineData("TAL-Chorus-LX", null, "Fx|Modulation")]
    [InlineData("Decapitator Saturation", null, "Fx|Distortion")]
    [InlineData("Auto Wah", null, "Fx|Filter")]
    [InlineData("Little AlterBoy Pitch", null, "Fx|Pitch Shift")]
    [InlineData("SPAN Spectrum Analyzer", null, "Fx|Analyzer")]
    [InlineData("Stereo Width", null, "Fx|Spatial")]
    [InlineData("Ozone Imager", null, "Fx|Spatial")]
    [InlineData("Drum Machine", null, "Instrument|Drum")]
    [InlineData("TAL-Sampler", null, "Instrument|Sampler")]
    [InlineData("Vital Synth", null, "Instrument|Synth")]
    [InlineData("FabFilter Pro-Q 3", null, null)]
    [InlineData("Sequencer Equipment", null, null)]
    [InlineData("Parameter Tool", null, null)]
    [InlineData("Supermassive", "Valhalla DSP, LLC", null)]
    [InlineData("", null, null)]
    public void GuessCategoryFromName_UsesWordBoundaries(string text, string? vendor, string? expected) =>
        Assert.Equal(expected, Vst3PluginMetadata.GuessCategoryFromName(text, vendor));

    [Fact]
    public void ScanWithMetadata_ReportsMonotonicProgressEndingAtOne()
    {
        var root = Path.Combine(_dir, "VST3");
        Directory.CreateDirectory(root);
        foreach (var name in new[] { "A", "B", "C" })
        {
            File.WriteAllBytes(Path.Combine(root, name + ".vst3"), [0]);
        }

        var reports = new List<Vst3ScanProgress>();
        var found = Vst3PluginScanner.ScanWithMetadata([root], new SyncProgress(reports.Add));

        Assert.Equal(3, found.Count);
        Assert.NotEmpty(reports);
        for (var i = 1; i < reports.Count; i++)
        {
            Assert.True(reports[i].Fraction >= reports[i - 1].Fraction);
        }

        Assert.Equal(1.0, reports[^1].Fraction, 6);
        Assert.Equal(3, reports[^1].Current);
        Assert.Equal(3, reports[^1].Total);
    }

    private sealed class SyncProgress(Action<Vst3ScanProgress> report) : IProgress<Vst3ScanProgress>
    {
        public void Report(Vst3ScanProgress value) => report(value);
    }

    private static Vst3ScannedPlugin Scanned(
        string name,
        string path,
        string vendor = "",
        string category = "",
        string? failure = null) =>
        new(
            new Vst3PluginInfo(name, path, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            new Vst3PluginMetadata(string.Empty, vendor, category, string.Empty, failure));

    [Fact]
    public void SearchPathList_KeepsMissingFoldersAndDeduplicates()
    {
        var missing = Path.Combine(_dir, "later");
        var resolved = Vst3SearchPathList.Resolve([missing, missing + Path.DirectorySeparatorChar, " ", missing.ToUpperInvariant()]);

        Assert.Equal([Path.GetFullPath(missing)], resolved);
        Assert.Empty(Vst3SearchPathList.Resolve(null));
    }

    [Fact]
    public void SteinbergHostCache_MapsBinaryPathToBundleAndPrefersAudioModuleClass()
    {
        var bundle = Path.Combine(_dir, "Softube", "Acoustic Feedback.vst3");
        Directory.CreateDirectory(bundle);
        var binary = Path.Combine(bundle, "Contents", "x86_64-win", "Acoustic Feedback.vst3")
            .Replace('\\', '/');
        var xml = $$"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plugins>
              <plugin>
                <path>{{binary}}</path>
                <vendor>Softube</vendor>
                <class>
                  <category>Component Controller Class</category>
                  <name>Controller</name>
                  <subCategories>Fx</subCategories>
                  <vendor>Softube</vendor>
                  <version>1.0</version>
                </class>
                <class>
                  <category>Audio Module Class</category>
                  <name>Acoustic Feedback</name>
                  <subCategories>Fx|Distortion</subCategories>
                  <vendor>Softube</vendor>
                  <version>2.6.40</version>
                </class>
              </plugin>
            </plugins>
            """;

        var cache = Vst3SteinbergHostCache.FromXml(xml);

        Assert.True(cache.TryFind(bundle, out var entry));
        Assert.Equal("Acoustic Feedback", entry.Name);
        Assert.Equal("Softube", entry.Vendor);
        Assert.Equal("Fx|Distortion", entry.Category);
        Assert.Equal("2.6.40", entry.Version);
        Assert.True(Vst3SteinbergHostCache.TryGetBundleRoot(binary.Replace('/', Path.DirectorySeparatorChar), out var root));
        Assert.Equal(Path.GetFullPath(bundle), root);
    }

    [Fact]
    public void Metadata_UsesSteinbergHostCacheOnlyWhenProvided()
    {
        var bundle = Path.Combine(_dir, "Cached.vst3");
        Directory.CreateDirectory(Path.Combine(bundle, "Contents", "x86_64-win"));
        File.WriteAllBytes(Path.Combine(bundle, "Contents", "x86_64-win", "Cached.vst3"), [0x4D, 0x5A, 0, 0]);

        var cache = Vst3SteinbergHostCache.FromXml($$"""
            <?xml version="1.0"?>
            <plugins>
              <plugin>
                <path>{{Path.Combine(bundle, "Contents", "x86_64-win", "Cached.vst3").Replace('\\', '/')}}</path>
                <vendor>CacheVendor</vendor>
                <class>
                  <category>Audio Module Class</category>
                  <name>Cached Delay</name>
                  <subCategories>Fx|Delay</subCategories>
                  <vendor>CacheVendor</vendor>
                  <version>3.1</version>
                </class>
              </plugin>
            </plugins>
            """);

        var without = Vst3PluginMetadata.Read(bundle, Vst3SteinbergHostCache.Empty);
        Assert.NotEqual("Fx|Delay", without.Category);

        var with = Vst3PluginMetadata.Read(bundle, cache);
        Assert.False(with.IsFailed);
        Assert.Equal("Cached Delay", with.Name);
        Assert.Equal("CacheVendor", with.Vendor);
        Assert.Equal("Fx|Delay", with.Category);
        Assert.Equal("3.1", with.Version);
    }

    [Fact]
    public void EffectClass_AcceptsAudioMixProcessorAndMergesInstrumentBeforeFx()
    {
        Assert.True(Vst3EffectClass.IsUsableEffectClass("Audio Mix Processor", "Fx|Distortion"));
        Assert.False(Vst3EffectClass.IsUsableEffectClass("Component Controller Class", "Fx"));
        Assert.Equal(
            "Instrument · Fx",
            Vst3EffectClass.MergeSubCategories(["Fx", "Instrument", "Fx"]));
        Assert.True(Vst3FactoryProbe.IsWaveShellPath(@"C:\VST3\WaveShell1-VST3 12.7_x64.vst3"));
        Assert.False(Vst3FactoryProbe.IsWaveShellPath(@"C:\VST3\Tape Multi Track.vst3"));
    }

    [Fact]
    public void GuessVendorFromPath_DetectsMeldaAndSoftubeSpi()
    {
        Assert.Equal(
            "MeldaProduction",
            Vst3PluginMetadata.GuessVendorFromPath(@"C:\VST3\MeldaProduction\Dynamics\MCompressor.vst3"));

        var soft = Path.Combine(_dir, "Tape.vst3");
        Directory.CreateDirectory(Path.Combine(soft, "Contents", "Resources"));
        File.WriteAllBytes(Path.Combine(soft, "Contents", "Resources", "PluginInfo.spi"), [1]);
        Assert.Equal("Softube", Vst3PluginMetadata.GuessVendorFromPath(soft));
        Assert.Equal(string.Empty, Vst3PluginMetadata.GuessVendorFromPath(Path.Combine(_dir, "Other.vst3")));
    }

    [Fact]
    public void SteinbergHostCache_ReadsAudioMixProcessor()
    {
        var bundle = Path.Combine(_dir, "Tape Multi Track.vst3");
        Directory.CreateDirectory(bundle);
        var binary = Path.Combine(bundle, "Contents", "x86_64-win", "Tape Multi Track.vst3")
            .Replace('\\', '/');
        var cache = Vst3SteinbergHostCache.FromXml($$"""
            <?xml version="1.0"?>
            <plugins>
              <plugin>
                <path>{{binary}}</path>
                <vendor>Softube</vendor>
                <class>
                  <category>Audio Mix Processor</category>
                  <name>Tape Multi Track</name>
                  <subCategories>Fx|Distortion</subCategories>
                  <vendor>Softube</vendor>
                  <version>2.6.40</version>
                </class>
              </plugin>
            </plugins>
            """);

        Assert.True(cache.TryFind(bundle, out var entry));
        Assert.Equal("Fx|Distortion", entry.Category);
        Assert.Equal("Softube", entry.Vendor);
    }

    [Fact]
    public void FactoryProbe_IsUnavailableUnderTestHost() =>
        Assert.False(Vst3FactoryProbe.IsOutOfProcessAvailable());

#if DEBUG
    [Fact]
    public void VstFeature_IsEnabledInDebug() => Assert.True(VstFeature.IsEnabled);
#endif
}
