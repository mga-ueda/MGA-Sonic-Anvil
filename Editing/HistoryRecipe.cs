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

    /// <summary>1 始まり。0 は全チャンネル（旧レシピ）。</summary>
    public int Channel { get; set; }

    /// <summary>複数ソロ。0 は <see cref="Channel"/> を使う。</summary>
    public int ChannelMask { get; set; }

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
    public const string PitchShift = "PitchShift";
    public const string TimeStretch = "TimeStretch";
    public const string Reverse = "Reverse";
    public const string NormalizePerRegion = "NormalizePerRegion";
    public const string Delete = "Delete";
    public const string DeleteSilence = "DeleteSilence";
    public const string Paste = "Paste";
    public const string Record = "Record";
    public const string SetSampleLoop = "SetSampleLoop";
    public const string SetRegion = "SetRegion";
    public const string RemoveRegions = "RemoveRegions";
    public const string ClearAllRegions = "ClearAllRegions";
    public const string AddMarker = "AddMarker";
    public const string ReplaceMarkers = "ReplaceMarkers";
    public const string ReplaceRegions = "ReplaceRegions";
    public const string MarkerComment = "MarkerComment";
    public const string RegionName = "RegionName";
    public const string RemoveMarkers = "RemoveMarkers";
    public const string ClearAllMarkers = "ClearAllMarkers";
    public const string MoveMarkers = "MoveMarkers";
    public const string MoveTimeline = "MoveTimeline";
    public const string ConvertRate = "ConvertRate";
    public const string ConvertBits = "ConvertBits";
    public const string ConvertChannels = "ConvertChannels";

    private static readonly HashSet<string> KnownKinds = new(StringComparer.Ordinal)
    {
        FadeIn,
        FadeOut,
        FadeAround,
        Normalize,
        Gain,
        PitchShift,
        TimeStretch,
        Reverse,
        NormalizePerRegion,
        Delete,
        DeleteSilence,
        Paste,
        Record,
        SetSampleLoop,
        SetRegion,
        RemoveRegions,
        ClearAllRegions,
        AddMarker,
        ReplaceMarkers,
        ReplaceRegions,
        MarkerComment,
        RegionName,
        RemoveMarkers,
        ClearAllMarkers,
        MoveMarkers,
        MoveTimeline,
        ConvertRate,
        ConvertBits,
        ConvertChannels,
    };

    private static readonly HashSet<string> SampleKinds = new(StringComparer.Ordinal)
    {
        FadeIn,
        FadeOut,
        FadeAround,
        Normalize,
        Gain,
        PitchShift,
        TimeStretch,
        Reverse,
        NormalizePerRegion,
        Delete,
        DeleteSilence,
        Paste,
        Record,
        ConvertRate,
        ConvertBits,
        ConvertChannels,
    };

    public static bool IsKnownKind(string? kind) =>
        kind is { Length: > 0 } && KnownKinds.Contains(kind);

    public static bool CanImport(HistorySessionSnapshot? snapshot) =>
        snapshot?.Recipes is { Length: > 0 } recipes
        && recipes.All(recipe => IsKnownKind(recipe.Kind));

    public static bool AffectsSamples(HistoryRecipe recipe) =>
        recipe.Kind is { Length: > 0 } && SampleKinds.Contains(recipe.Kind);

    public static bool AffectsSamples(HistorySessionSnapshot snapshot) =>
        snapshot.Recipes is { Length: > 0 } recipes && recipes.Any(AffectsSamples);

    public static HistoryRecipe Range(
        string kind,
        int sourceRate,
        WaveSelection range,
        int value = 0,
        int channel = ChannelSolo.Off,
        int channelMask = 0) =>
        WithChannel(
            new HistoryRecipe
            {
                Kind = kind,
                SourceRate = sourceRate,
                Start = range.StartFrame,
                End = range.EndFrame,
                Value = value,
            },
            channel,
            channelMask);

    public static HistoryRecipe FromGain(
        int sourceRate,
        WaveSelection range,
        double gainDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0) =>
        WithChannel(
            new HistoryRecipe
            {
                Kind = Gain,
                SourceRate = sourceRate,
                Start = range.StartFrame,
                End = range.EndFrame,
                Amount = gainDb,
            },
            channel,
            channelMask);

    public static HistoryRecipe FromPitchShift(
        int sourceRate,
        WaveSelection range,
        int semitones,
        bool timeStretch = true,
        int channel = ChannelSolo.Off,
        int channelMask = 0) =>
        WithChannel(
            new HistoryRecipe
            {
                Kind = PitchShift,
                SourceRate = sourceRate,
                Start = range.StartFrame,
                End = range.EndFrame,
                Value = Audio.PitchShift.Snap(semitones),
                Flag = !timeStretch,
            },
            channel,
            channelMask);

    public static HistoryRecipe FromDeleteSilence(
        int sourceRate,
        WaveSelection range,
        double thresholdDb,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = ClickGuard.DefaultFadeMilliseconds) =>
        WithChannel(
            new HistoryRecipe
            {
                Kind = DeleteSilence,
                SourceRate = sourceRate,
                Start = range.StartFrame,
                End = range.EndFrame,
                Amount = SilentSkip.ClampThresholdDb(thresholdDb),
                Value = ClickGuard.ClampFadeMs(fadeMs),
            },
            channel,
            channelMask);

    public static HistoryRecipe FromTimeStretch(
        int sourceRate,
        WaveSelection range,
        double ratio,
        int channel = ChannelSolo.Off,
        int channelMask = 0) =>
        WithChannel(
            new HistoryRecipe
            {
                Kind = TimeStretch,
                SourceRate = sourceRate,
                Start = range.StartFrame,
                End = range.EndFrame,
                Amount = ratio,
            },
            channel,
            channelMask);

    public static IEditCommand? TryCreate(AudioDocument document, HistoryRecipe recipe)
    {
        return recipe.Kind switch
        {
            FadeIn => ProcessEdits.FadeIn(document, RangeOf(recipe), ShapeOf(recipe), channelMask: MaskOf(recipe)),
            FadeOut => ProcessEdits.FadeOut(document, RangeOf(recipe), ShapeOf(recipe), channelMask: MaskOf(recipe)),
            FadeAround => ProcessEdits.FadeAroundPlayhead(
                document,
                RangeOf(recipe),
                recipe.Playhead,
                channelMask: MaskOf(recipe)),
            Normalize => ProcessEdits.Normalize(document, RangeOf(recipe), channelMask: MaskOf(recipe)),
            Gain => ProcessEdits.Gain(document, RangeOf(recipe), recipe.Amount, channelMask: MaskOf(recipe)),
            PitchShift => ProcessEdits.PitchShift(
                document,
                RangeOf(recipe),
                recipe.Value,
                timeStretch: !recipe.Flag,
                channelMask: MaskOf(recipe)),
            TimeStretch => ProcessEdits.TimeStretch(
                document,
                RangeOf(recipe),
                Audio.TimeStretch.DestFrameCountFromRatio((int)RangeOf(recipe).Length, recipe.Amount),
                channelMask: MaskOf(recipe)),
            Reverse => ProcessEdits.Reverse(document, RangeOf(recipe), channelMask: MaskOf(recipe)),
            NormalizePerRegion => ProcessEdits.NormalizePerRegion(
                document,
                RangesOf(recipe),
                channelMask: MaskOf(recipe),
                fadeMs: FadeMsOf(recipe)),
            Delete => ProcessEdits.Delete(document, RangeOf(recipe), channelMask: MaskOf(recipe)),
            DeleteSilence => ProcessEdits.DeleteSilence(
                document,
                RangeOf(recipe),
                recipe.Amount,
                channelMask: MaskOf(recipe),
                fadeMs: FadeMsOf(recipe)),
            Paste => TryPaste(document, recipe),
            Record => TryRecord(document, recipe),
            SetSampleLoop => TrySetSampleLoop(document, recipe),
            SetRegion => TrySetRegion(document, recipe),
            RemoveRegions => ProcessEdits.RemoveRegions(document, RangesOf(recipe)),
            ClearAllRegions => ProcessEdits.ClearAllRegions(document),
            AddMarker => TryAddMarker(document, recipe),
            ReplaceMarkers => ProcessEdits.ApplyMarkers(document, MarkersOf(recipe.Frames, recipe.Comments)),
            ReplaceRegions => ProcessEdits.ApplyRegions(document, RegionsOf(recipe.Starts, recipe.Ends, recipe.Names)),
            MarkerComment => ProcessEdits.SetMarkerComment(document, recipe.Frame, recipe.Text ?? ""),
            RegionName => ProcessEdits.SetRegionName(document, RangeOf(recipe), recipe.Text ?? ""),
            RemoveMarkers => ProcessEdits.RemoveMarkers(document, recipe.Frames ?? []),
            ClearAllMarkers => ProcessEdits.ClearAllMarkers(document),
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
        return ProcessEdits.Paste(document, clip, recipe.Frame, channelMask: MaskOf(recipe));
    }

    private static IEditCommand? TrySetSampleLoop(AudioDocument document, HistoryRecipe recipe)
    {
        var after = recipe.Start == recipe.End
            ? WaveSelection.Empty
            : new WaveSelection(recipe.Start, recipe.End).Clamp(document.FrameCount);
        return ProcessEdits.ApplySampleLoop(document, after);
    }

    private static IEditCommand? TrySetRegion(AudioDocument document, HistoryRecipe recipe)
    {
        if (recipe.Start == recipe.End)
        {
            return ProcessEdits.SetRegion(document, WaveSelection.Empty);
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
        return ProcessEdits.ApplyTimeline(document, markersAfter, regionsAfter, loopAfter);
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

    private static IEditCommand? TryRecord(AudioDocument document, HistoryRecipe recipe)
    {
        var samples = recipe.ClipSamples;
        if (samples is not { Length: > 0 } || recipe.ClipChannels < 1)
        {
            return null;
        }

        if (recipe.ClipRate > 0 && recipe.ClipRate != document.SampleRate)
        {
            samples = FormatConvert.Resample(samples, recipe.ClipChannels, recipe.ClipRate, document.SampleRate);
        }

        if (recipe.ClipChannels != document.Channels)
        {
            samples = new AudioClip(samples, recipe.ClipChannels, document.SampleRate).AdaptTo(document.Channels);
        }

        return ProcessEdits.RecordOverwrite(document, recipe.Frame, samples);
    }

    public static HistoryRecipe FromRecord(int sourceRate, long startFrame, float[] take, int channels) =>
        new()
        {
            Kind = Record,
            SourceRate = sourceRate,
            Frame = startFrame,
            ClipSamples = take,
            ClipChannels = channels,
            ClipRate = sourceRate,
        };

    public static HistoryRecipe FromPaste(
        int sourceRate,
        long insertFrame,
        AudioClip clip,
        int channel = ChannelSolo.Off,
        int channelMask = 0) =>
        WithChannel(
            new HistoryRecipe
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
            },
            channel,
            channelMask);

    private static HistoryRecipe WithChannel(HistoryRecipe recipe, int channel, int channelMask = 0)
    {
        if (channelMask != 0 && ChannelSolo.Count(channelMask) > 1)
        {
            recipe.Channel = 0;
            recipe.ChannelMask = channelMask;
            return recipe;
        }

        recipe.Channel = channel < 0 ? 0 : channel + 1;
        if (recipe.Channel == 0 && channelMask != 0)
        {
            var primary = ChannelSolo.Primary(channelMask);
            recipe.Channel = primary < 0 ? 0 : primary + 1;
        }

        recipe.ChannelMask = 0;
        return recipe;
    }

    internal static int MaskOf(HistoryRecipe recipe)
    {
        if (recipe.ChannelMask != 0)
        {
            return recipe.ChannelMask;
        }

        return recipe.Channel <= 0 ? 0 : 1 << (recipe.Channel - 1);
    }

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

    public static HistoryRecipe FromRegionEdits(
        string kind,
        int sourceRate,
        IReadOnlyList<WaveSelection> ranges,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        int fadeMs = ClickGuard.DefaultFadeMilliseconds)
    {
        var recipe = FromRanges(kind, sourceRate, ranges);
        recipe.Value = ClickGuard.ClampFadeMs(fadeMs);
        return WithChannel(recipe, channel, channelMask);
    }

    internal static int FadeMsOf(HistoryRecipe recipe) =>
        recipe.Value > 0
            ? ClickGuard.ClampFadeMs(recipe.Value)
            : ClickGuard.DefaultFadeMilliseconds;

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
