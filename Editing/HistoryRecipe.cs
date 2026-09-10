using System.Text.Json.Serialization;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal sealed class HistoryRecipe
{
    public string Kind { get; set; } = "";

    public int SourceRate { get; set; }

    public long Start { get; set; }

    public long End { get; set; }

    public long Frame { get; set; }

    public long Playhead { get; set; }

    public long Delta { get; set; }

    public int Value { get; set; }

    public double Amount { get; set; }

    public string Text { get; set; } = "";

    public bool Flag { get; set; }

    public long[] Frames { get; set; } = [];

    public long[] Starts { get; set; } = [];

    public long[] Ends { get; set; } = [];

    public string[] Names { get; set; } = [];

    public string[] Comments { get; set; } = [];

    public float[] ClipSamples { get; set; } = [];

    public int ClipChannels { get; set; }

    public int ClipRate { get; set; }

    public long[] ClipMarkerFrames { get; set; } = [];

    public string[] ClipMarkerComments { get; set; } = [];

    public long[] ClipRegionStarts { get; set; } = [];

    public long[] ClipRegionEnds { get; set; } = [];

    public string[] ClipRegionNames { get; set; } = [];
}

internal sealed class HistorySessionSnapshot
{
    public int CurrentIndex { get; set; }

    public int CleanIndex { get; set; }

    public bool CleanValid { get; set; } = true;

    public HistoryRecipe[] Recipes { get; set; } = [];
}

[JsonSerializable(typeof(HistorySessionSnapshot))]
[JsonSerializable(typeof(HistoryRecipe))]
[JsonSourceGenerationOptions(WriteIndented = false)]
internal partial class HistorySessionJsonContext : JsonSerializerContext;

internal static class HistoryRecipes
{
    public const string FadeIn = "FadeIn";
    public const string FadeOut = "FadeOut";
    public const string FadeAround = "FadeAround";
    public const string Normalize = "Normalize";
    public const string Gain = "Gain";
    public const string Delete = "Delete";
    public const string Paste = "Paste";
    public const string SetSampleLoop = "SetSampleLoop";
    public const string SetRegion = "SetRegion";
    public const string RemoveRegions = "RemoveRegions";
    public const string AddMarker = "AddMarker";
    public const string ReplaceMarkers = "ReplaceMarkers";
    public const string ReplaceRegions = "ReplaceRegions";
    public const string MarkerComment = "MarkerComment";
    public const string RegionName = "RegionName";
    public const string RemoveMarkers = "RemoveMarkers";
    public const string MoveMarkers = "MoveMarkers";
    public const string MoveTimeline = "MoveTimeline";
    public const string ConvertRate = "ConvertRate";
    public const string ConvertBits = "ConvertBits";
    public const string ConvertChannels = "ConvertChannels";

    public static HistoryRecipe Range(string kind, int sourceRate, WaveSelection range, int value = 0) =>
        new()
        {
            Kind = kind,
            SourceRate = sourceRate,
            Start = range.StartFrame,
            End = range.EndFrame,
            Value = value,
        };

    public static HistoryRecipe FromGain(int sourceRate, WaveSelection range, double gainDb) =>
        new()
        {
            Kind = Gain,
            SourceRate = sourceRate,
            Start = range.StartFrame,
            End = range.EndFrame,
            Amount = gainDb,
        };

    public static IEditCommand? TryCreate(AudioDocument document, HistoryRecipe recipe)
    {
        return recipe.Kind switch
        {
            FadeIn => ProcessEdits.FadeIn(document, RangeOf(recipe), ShapeOf(recipe)),
            FadeOut => ProcessEdits.FadeOut(document, RangeOf(recipe), ShapeOf(recipe)),
            FadeAround => ProcessEdits.FadeAroundPlayhead(document, RangeOf(recipe), recipe.Playhead),
            Normalize => ProcessEdits.Normalize(document, RangeOf(recipe)),
            Gain => ProcessEdits.Gain(document, RangeOf(recipe), recipe.Amount),
            Delete => ProcessEdits.Delete(document, RangeOf(recipe)),
            Paste => TryPaste(document, recipe),
            SetSampleLoop => TrySetSampleLoop(document, recipe),
            SetRegion => TrySetRegion(document, recipe),
            RemoveRegions => ProcessEdits.RemoveRegions(document, RangesOf(recipe)),
            AddMarker => TryAddMarker(document, recipe),
            ReplaceMarkers => ProcessEdits.ApplyMarkers(document, MarkersOf(recipe.Frames, recipe.Comments)),
            ReplaceRegions => ProcessEdits.ApplyRegions(document, RegionsOf(recipe.Starts, recipe.Ends, recipe.Names)),
            MarkerComment => ProcessEdits.SetMarkerComment(document, recipe.Frame, recipe.Text ?? ""),
            RegionName => ProcessEdits.SetRegionName(document, RangeOf(recipe), recipe.Text ?? ""),
            RemoveMarkers => ProcessEdits.RemoveMarkers(document, recipe.Frames ?? []),
            MoveMarkers => ProcessEdits.MoveMarkers(document, recipe.Frames ?? [], recipe.Delta, out _),
            MoveTimeline => TryMoveTimeline(document, recipe),
            ConvertRate => ProcessEdits.ConvertSampleRate(document, recipe.Value),
            ConvertBits => ProcessEdits.ConvertBitDepth(document, recipe.Value),
            ConvertChannels => ProcessEdits.ConvertChannels(document, recipe.Value),
            _ => null,
        };
    }

    private static WaveSelection RangeOf(HistoryRecipe recipe) =>
        new(recipe.Start, recipe.End);

    private static FadeShape ShapeOf(HistoryRecipe recipe) =>
        Enum.IsDefined(typeof(FadeShape), recipe.Value)
            ? (FadeShape)recipe.Value
            : FadeCurves.Default;

    private static WaveSelection[] RangesOf(HistoryRecipe recipe)
    {
        var starts = recipe.Starts ?? [];
        var ends = recipe.Ends ?? [];
        var count = Math.Min(starts.Length, ends.Length);
        var ranges = new WaveSelection[count];
        for (var i = 0; i < count; i++)
        {
            ranges[i] = new WaveSelection(starts[i], ends[i]);
        }

        return ranges;
    }

    private static IEditCommand? TryPaste(AudioDocument document, HistoryRecipe recipe)
    {
        var samples = recipe.ClipSamples;
        if (samples is not { Length: > 0 } || recipe.ClipChannels < 1 || recipe.ClipRate < 1)
        {
            return null;
        }

        var clip = new AudioClip(
            samples,
            recipe.ClipChannels,
            recipe.ClipRate,
            MarkersOf(recipe.ClipMarkerFrames, recipe.ClipMarkerComments),
            RegionsOf(recipe.ClipRegionStarts, recipe.ClipRegionEnds, recipe.ClipRegionNames));
        return ProcessEdits.Paste(document, clip, recipe.Frame);
    }

    private static IEditCommand? TrySetSampleLoop(AudioDocument document, HistoryRecipe recipe)
    {
        var after = recipe.Start == recipe.End
            ? WaveSelection.Empty
            : new WaveSelection(recipe.Start, recipe.End).Clamp(document.FrameCount);
        if (document.SampleLoop == after)
        {
            return null;
        }

        var command = new SetSampleLoopCommand(
            document.SampleLoop,
            after,
            after.IsEmpty
                ? UiStrings.EditHistoryName("Set Sample Loop") + "  解除"
                : UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Set Sample Loop"),
                    document.SampleRate,
                    after.StartFrame,
                    after.EndFrame));
        command.Persist = recipe;
        return command;
    }

    private static IEditCommand? TrySetRegion(AudioDocument document, HistoryRecipe recipe)
    {
        if (recipe.Start == recipe.End)
        {
            var before = document.SnapshotRegions();
            return before.Length == 0
                ? null
                : new SetRegionCommand(before, [], UiStrings.EditHistoryName("Set Region") + "  解除");
        }

        var range = new WaveSelection(recipe.Start, recipe.End);
        if (recipe.Flag)
        {
            return ProcessEdits.RemoveRegions(document, [range]);
        }

        return document.RegionNumber(range) > 0 ? null : ProcessEdits.SetRegion(document, range);
    }

    private static IEditCommand? TryAddMarker(AudioDocument document, HistoryRecipe recipe)
    {
        return document.HasMarkerAt(recipe.Frame)
            ? null
            : ProcessEdits.AddMarker(document, recipe.Frame);
    }

    private static IEditCommand? TryMoveTimeline(AudioDocument document, HistoryRecipe recipe)
    {
        var markersAfter = MarkersOf(recipe.Frames, recipe.Comments);
        var regionsAfter = RegionsOf(recipe.Starts, recipe.Ends, recipe.Names);
        var loopAfter = recipe.Start == recipe.End
            ? WaveSelection.Empty
            : new WaveSelection(recipe.Start, recipe.End);
        var markersBefore = document.SnapshotMarkers();
        var regionsBefore = document.SnapshotRegions();
        var loopBefore = document.SampleLoop;
        if (markersAfter.AsSpan().SequenceEqual(markersBefore)
            && RegionsMatch(regionsAfter, regionsBefore)
            && loopAfter == loopBefore)
        {
            return null;
        }

        var command = new MoveTimelineItemsCommand(
            markersBefore,
            markersAfter,
            regionsBefore,
            regionsAfter,
            loopBefore,
            loopAfter,
            UiStrings.EditHistoryName("Move Timeline"));
        command.Persist = recipe;
        return command;
    }

    private static bool RegionsMatch(WaveRegion[] left, WaveRegion[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
            {
                return false;
            }
        }

        return true;
    }

    private static MarkerSnapshot[] MarkersOf(long[]? frames, string[]? comments)
    {
        frames ??= [];
        comments ??= [];
        var markers = new MarkerSnapshot[frames.Length];
        for (var i = 0; i < frames.Length; i++)
        {
            markers[i] = new MarkerSnapshot(frames[i], i < comments.Length ? comments[i] : "");
        }

        return markers;
    }

    private static WaveRegion[] RegionsOf(long[]? starts, long[]? ends, string[]? names)
    {
        starts ??= [];
        ends ??= [];
        names ??= [];
        if (starts.Length == 0 || starts.Length != ends.Length)
        {
            return [];
        }

        var regions = new WaveRegion[starts.Length];
        for (var i = 0; i < starts.Length; i++)
        {
            regions[i] = new WaveRegion(
                new WaveSelection(starts[i], ends[i]),
                i < names.Length ? names[i] : "");
        }

        return regions;
    }

    public static HistoryRecipe FromPaste(int sourceRate, long insertFrame, AudioClip clip) =>
        new()
        {
            Kind = Paste,
            SourceRate = sourceRate,
            Frame = insertFrame,
            ClipSamples = clip.Interleaved,
            ClipChannels = clip.Channels,
            ClipRate = clip.SampleRate,
            ClipMarkerFrames = clip.Markers.Select(item => item.Frame).ToArray(),
            ClipMarkerComments = clip.Markers.Select(item => item.Comment ?? "").ToArray(),
            ClipRegionStarts = clip.Regions.Select(item => item.StartFrame).ToArray(),
            ClipRegionEnds = clip.Regions.Select(item => item.EndFrame).ToArray(),
            ClipRegionNames = clip.Regions.Select(item => item.Name).ToArray(),
        };

    public static HistoryRecipe FromMarkers(string kind, int sourceRate, IReadOnlyList<long> frames, long delta = 0) =>
        new()
        {
            Kind = kind,
            SourceRate = sourceRate,
            Frames = [.. frames],
            Delta = delta,
        };

    public static HistoryRecipe FromMarkerSnapshots(
        string kind,
        int sourceRate,
        IReadOnlyList<MarkerSnapshot> markers) =>
        new()
        {
            Kind = kind,
            SourceRate = sourceRate,
            Frames = markers.Select(item => item.Frame).ToArray(),
            Comments = markers.Select(item => item.Comment ?? "").ToArray(),
        };

    public static HistoryRecipe FromRegionSnapshots(
        string kind,
        int sourceRate,
        IReadOnlyList<WaveRegion> regions) =>
        new()
        {
            Kind = kind,
            SourceRate = sourceRate,
            Starts = regions.Select(item => item.StartFrame).ToArray(),
            Ends = regions.Select(item => item.EndFrame).ToArray(),
            Names = regions.Select(item => item.Name ?? "").ToArray(),
        };

    public static HistoryRecipe FromRanges(string kind, int sourceRate, IReadOnlyList<WaveSelection> ranges) =>
        new()
        {
            Kind = kind,
            SourceRate = sourceRate,
            Starts = ranges.Select(item => item.StartFrame).ToArray(),
            Ends = ranges.Select(item => item.EndFrame).ToArray(),
        };

    public static HistoryRecipe FromTimeline(
        int sourceRate,
        MarkerSnapshot[] markers,
        WaveRegion[] regions,
        WaveSelection loop) =>
        new()
        {
            Kind = MoveTimeline,
            SourceRate = sourceRate,
            Frames = markers.Select(item => item.Frame).ToArray(),
            Comments = markers.Select(item => item.Comment ?? "").ToArray(),
            Starts = regions.Select(item => item.StartFrame).ToArray(),
            Ends = regions.Select(item => item.EndFrame).ToArray(),
            Names = regions.Select(item => item.Name).ToArray(),
            Start = loop.StartFrame,
            End = loop.EndFrame,
        };
}
