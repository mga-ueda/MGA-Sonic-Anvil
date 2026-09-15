using System.Reflection;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>履歴に残る操作が、コピー＆ペースト用の Replay を必ず持つこと。</summary>
public sealed class EditReplayCoverageTests
{
    [Fact]
    public void TryCreate_EveryKnownKind_AttachesReplay()
    {
        var failures = new List<string>();
        foreach (var kind in KnownKinds())
        {
            var (document, recipe) = Fixture(kind);
            var command = HistoryRecipes.TryCreate(document, recipe);
            if (command is null)
            {
                failures.Add($"{kind}: TryCreate が null");
                continue;
            }

            if (command.Replay is null)
            {
                failures.Add($"{kind}: Replay がない");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TryCreate_ReplayResult_StaysCopyable()
    {
        var failures = new List<string>();
        foreach (var kind in KnownKinds())
        {
            var (document, recipe) = Fixture(kind);
            var command = HistoryRecipes.TryCreate(document, recipe);
            if (command?.Replay is null)
            {
                failures.Add($"{kind}: 元がコピーできない");
                continue;
            }

            var (target, _) = Fixture(kind);
            var replayed = command.Replay(target) ?? command.Replay(MakeConstant(100, 1f));
            if (replayed is not null && replayed.Replay is null)
            {
                failures.Add($"{kind}: 再適用後に Replay が落ちた");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void LiveCommands_AttachReplay()
    {
        var failures = new List<string>();
        foreach (var (name, command) in LiveCommands())
        {
            if (command is null)
            {
                failures.Add($"{name}: コマンドが null");
            }
            else if (command.Replay is null)
            {
                failures.Add($"{name}: Replay がない");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TryImport_RestoredEntriesRemainCopyable()
    {
        var document = Stocked();
        var history = new EditHistory();
        history.Do(document, ProcessEdits.SetSampleLoop(document, new WaveSelection(8, 30))!);
        history.Do(document, ProcessEdits.SetRegion(document, new WaveSelection(4, 12))!);
        history.Do(document, ProcessEdits.AddMarker(document, 25));
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;
        Assert.True(document.TryMoveMarkers([25], 4, out _));
        history.Do(document, ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore)!);

        var exported = history.TryExport();
        Assert.NotNull(exported);

        var restored = MakeConstant(100, 1f);
        Assert.True(EditHistory.TryImport(restored, exported!, out var imported));
        foreach (var entry in imported.Snapshot().Skip(1))
        {
            Assert.True(entry.CanReplay, entry.Title);
        }
    }

    private static IEnumerable<(string Name, IEditCommand? Command)> LiveCommands()
    {
        var document = Stocked();
        yield return ("FadeIn", ProcessEdits.FadeIn(document, new WaveSelection(0, 40)));
        yield return ("FadeOut", ProcessEdits.FadeOut(document, new WaveSelection(0, 40)));
        yield return ("FadeAround", ProcessEdits.FadeAroundPlayhead(document, new WaveSelection(0, 80), 40));
        yield return ("Normalize", ProcessEdits.Normalize(document, new WaveSelection(0, 40)));
        yield return ("Gain", ProcessEdits.Gain(document, new WaveSelection(0, 40), -3));
        yield return ("Reverse", ProcessEdits.Reverse(document, new WaveSelection(0, 40)));
        yield return ("Delete", ProcessEdits.Delete(document, new WaveSelection(10, 20)));
        yield return ("AddMarker", ProcessEdits.AddMarker(document, 25));
        yield return ("RemoveMarkers", ProcessEdits.RemoveMarkers(document, [10]));
        yield return ("ClearAllMarkers", ProcessEdits.ClearAllMarkers(document));
        yield return ("SetMarkerComment", ProcessEdits.SetMarkerComment(document, 10, "L"));
        yield return ("SetRegion", ProcessEdits.SetRegion(document, new WaveSelection(4, 12)));
        yield return ("SetRegionClear", ProcessEdits.SetRegion(document, WaveSelection.Empty));
        yield return ("RemoveRegions", ProcessEdits.RemoveRegions(document, [new WaveSelection(20, 40)]));
        yield return ("ClearAllRegions", ProcessEdits.ClearAllRegions(document));
        yield return ("SetRegionName", ProcessEdits.SetRegionName(document, new WaveSelection(20, 40), "verse"));
        yield return ("SetSampleLoop", ProcessEdits.SetSampleLoop(document, new WaveSelection(8, 30)));
        yield return ("ApplySampleLoop", ProcessEdits.ApplySampleLoop(document, new WaveSelection(12, 36)));
        yield return ("MoveMarkers", ProcessEdits.MoveMarkers(document, [10], 5, out _));
        yield return ("ApplyMarkers", ProcessEdits.ApplyMarkers(document, [new MarkerSnapshot(8, "")]));
        yield return ("ApplyRegions", ProcessEdits.ApplyRegions(document, [new WaveRegion(new WaveSelection(6, 18), "")]));
        yield return ("ApplyTimeline", ProcessEdits.ApplyTimeline(
            document,
            [new MarkerSnapshot(15, "")],
            [],
            WaveSelection.Empty));
        yield return ("NormalizePerRegion", ProcessEdits.NormalizePerRegion(document));
        yield return ("ConvertRate", ProcessEdits.ConvertSampleRate(document, 24000));
        yield return ("ConvertBits", ProcessEdits.ConvertBitDepth(document, 16));
        yield return ("ConvertChannels", ProcessEdits.ConvertChannels(document, 1));
        yield return ("Paste", ProcessEdits.Paste(
            document,
            new AudioClip([0.1f, 0.1f, 0.2f, 0.2f], 2, 48000),
            0));
        yield return ("Record", ProcessEdits.RecordOverwrite(document, 10, [0.3f, 0.3f, 0.4f, 0.4f]));

        var silent = WithSilence();
        yield return ("DeleteSilence", ProcessEdits.DeleteSilence(silent, new WaveSelection(0, silent.FrameCount), -60));

        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;
        document.TryMoveMarkers([10], 3, out _);
        yield return ("MoveTimelineItems", ProcessEdits.MoveTimelineItems(document, markersBefore, regionsBefore, loopBefore));
    }

    private static IEnumerable<string> KnownKinds() =>
        typeof(HistoryRecipes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .OrderBy(kind => kind, StringComparer.Ordinal);

    private static (AudioDocument Document, HistoryRecipe Recipe) Fixture(string kind)
    {
        if (kind == HistoryRecipes.DeleteSilence)
        {
            var silent = WithSilence();
            return (silent, new HistoryRecipe
            {
                Kind = kind,
                SourceRate = silent.SampleRate,
                Start = 0,
                End = silent.FrameCount,
                Amount = -60,
            });
        }

        if (kind is HistoryRecipes.PitchShift or HistoryRecipes.TimeStretch)
        {
            var longDoc = MakeConstant(2048, 1f);
            return (longDoc, new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 0,
                End = 2048,
                Value = kind == HistoryRecipes.PitchShift ? 12 : 0,
                Amount = kind == HistoryRecipes.TimeStretch ? 0.5 : 0,
            });
        }

        var document = Stocked();
        var recipe = kind switch
        {
            HistoryRecipes.FadeIn => Range(kind, 0, 40),
            HistoryRecipes.FadeOut => Range(kind, 0, 40),
            HistoryRecipes.FadeAround => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 0,
                End = 80,
                Playhead = 40,
            },
            HistoryRecipes.Normalize => Range(kind, 0, 40),
            HistoryRecipes.Gain => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 0,
                End = 40,
                Amount = -3,
            },
            HistoryRecipes.Reverse => Range(kind, 0, 40),
            HistoryRecipes.NormalizePerRegion => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Starts = [20],
                Ends = [40],
            },
            HistoryRecipes.Delete => Range(kind, 10, 20),
            HistoryRecipes.Paste => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frame = 0,
                ClipSamples = [0.1f, 0.1f, 0.2f, 0.2f],
                ClipChannels = 2,
                ClipRate = 48000,
            },
            HistoryRecipes.Record => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frame = 10,
                ClipSamples = [0.3f, 0.3f, 0.4f, 0.4f],
                ClipChannels = 2,
                ClipRate = 48000,
            },
            HistoryRecipes.SetSampleLoop => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 8,
                End = 30,
            },
            HistoryRecipes.SetRegion => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 4,
                End = 12,
            },
            HistoryRecipes.RemoveRegions => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Starts = [20],
                Ends = [40],
            },
            HistoryRecipes.ClearAllRegions => new HistoryRecipe { Kind = kind },
            HistoryRecipes.AddMarker => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frame = 25,
            },
            HistoryRecipes.ReplaceMarkers => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frames = [8, 24],
                Comments = ["", ""],
            },
            HistoryRecipes.ReplaceRegions => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Starts = [6],
                Ends = [18],
                Names = [""],
            },
            HistoryRecipes.MarkerComment => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frame = 10,
                Text = "L",
            },
            HistoryRecipes.RegionName => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Start = 20,
                End = 40,
                Text = "verse",
            },
            HistoryRecipes.RemoveMarkers => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frames = [10],
            },
            HistoryRecipes.ClearAllMarkers => new HistoryRecipe { Kind = kind },
            HistoryRecipes.MoveMarkers => new HistoryRecipe
            {
                Kind = kind,
                SourceRate = 48000,
                Frames = [10],
                Delta = 5,
            },
            HistoryRecipes.MoveTimeline => HistoryRecipes.FromTimeline(
                48000,
                [new MarkerSnapshot(15, "")],
                [],
                WaveSelection.Empty),
            HistoryRecipes.ConvertRate => new HistoryRecipe { Kind = kind, Value = 24000 },
            HistoryRecipes.ConvertBits => new HistoryRecipe { Kind = kind, Value = 16 },
            HistoryRecipes.ConvertChannels => new HistoryRecipe { Kind = kind, Value = 1 },
            _ => throw new InvalidOperationException($"fixture missing: {kind}"),
        };
        return (document, recipe);
    }

    private static HistoryRecipe Range(string kind, long start, long end) =>
        new()
        {
            Kind = kind,
            SourceRate = 48000,
            Start = start,
            End = end,
        };

    private static AudioDocument Stocked()
    {
        var document = MakeConstant(100, 1f);
        document.TryAddMarker(10);
        document.TryAddMarker(40);
        document.SetRegions([new WaveSelection(20, 40)]);
        document.SetSampleLoop(new WaveSelection(50, 70));
        return document;
    }

    private static AudioDocument WithSilence()
    {
        const int rate = 1000;
        const int audible = 20;
        var silent = SilentSkip.PeakWindowRadiusFrames(rate) * 2 + 20;
        var frames = audible + silent + audible;
        var samples = new float[frames * 2];
        Array.Fill(samples, 0.5f);
        for (var i = audible; i < audible + silent; i++)
        {
            samples[i * 2] = 0;
            samples[i * 2 + 1] = 0;
        }

        return new AudioDocument(samples, rate, 2, 24, AudioFileKind.Wave, null);
    }

    private static AudioDocument MakeConstant(int frames, float value)
    {
        var samples = new float[frames * 2];
        Array.Fill(samples, value);
        return new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
