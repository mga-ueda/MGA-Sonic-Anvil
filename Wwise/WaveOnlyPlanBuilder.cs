using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// Wwise IM Importer の Wave 単体モードと同じリージョン組み立て。
/// サンプルループがあればそれを優先し、なければマーカー接尾辞（-A/-L/-E）。
/// -A は次、-E は直前（-L）と同一セグメント。Custom Cue は出さない。
/// IM Importer の -R（除外）はこのアプリでは機能させない（通常マーカーと同じ分割点扱い）。
/// </summary>
internal static class WaveOnlyPlanBuilder
{
    private const string LoopKeyword = "Loop";

    public static WaveOnlyPlan Build(AudioDocument document)
    {
        var containerName = SanitizeWwiseName(ResolveContainerName(document));
        var regions = BuildRegions(document);
        var named = AssignUniqueNames(regions, containerName);
        return new WaveOnlyPlan
        {
            ContainerName = containerName,
            Regions = named,
            Segments = GroupIntoSegments(named, containerName),
        };
    }

    internal static IReadOnlyList<WaveOnlyRegion> BuildRegions(AudioDocument document)
    {
        if (document.FrameCount < 2)
        {
            return [];
        }

        if (!document.SampleLoop.IsEmpty)
        {
            return FromSampleLoop(document.SampleLoop, document.FrameCount);
        }

        var fromMarkers = FromMarkers(document.Markers, document.FrameCount);
        if (HasExportableSpecialRegions(fromMarkers))
        {
            return fromMarkers;
        }

        if (document.Regions.Count > 0)
        {
            return FromDocumentRegions(document);
        }

        return fromMarkers;
    }

    private static string ResolveContainerName(AudioDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.SourcePath))
        {
            var name = Path.GetFileNameWithoutExtension(document.SourcePath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return UiStrings.UntitledDocument;
    }

    private static List<WaveOnlyRegion> FromSampleLoop(WaveSelection loop, long frameCount)
    {
        var regions = new List<WaveOnlyRegion>();
        var start = Math.Clamp(loop.StartFrame, 0, frameCount);
        var end = Math.Clamp(loop.EndFrame, 0, frameCount);
        if (end <= start)
        {
            return [WholeFile(frameCount)];
        }

        if (start > 0)
        {
            regions.Add(Make(0, start, WaveOnlyRegionKind.Body, "Intro"));
        }

        regions.Add(Make(start, end, WaveOnlyRegionKind.Loop, "L"));
        if (end < frameCount)
        {
            regions.Add(Make(end, frameCount, WaveOnlyRegionKind.Exit, "E"));
        }

        return regions;
    }

    private static List<WaveOnlyRegion> FromDocumentRegions(AudioDocument document)
    {
        var regions = new List<WaveOnlyRegion>();
        var index = 1;
        foreach (var range in document.Regions)
        {
            if (range.IsEmpty)
            {
                continue;
            }

            var start = Math.Clamp(range.StartFrame, 0, document.FrameCount);
            var end = Math.Clamp(range.EndFrame, 0, document.FrameCount);
            if (end <= start)
            {
                continue;
            }

            var stored = document.RegionName(range);
            var name = string.IsNullOrWhiteSpace(stored)
                ? index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : stored;
            regions.Add(Make(start, end, WaveOnlyRegionKind.Body, name));
            index++;
        }

        return regions.Count == 0
            ? [WholeFile(document.FrameCount)]
            : regions;
    }

    internal static List<WaveOnlyRegion> FromMarkers(IReadOnlyList<WaveMarker> markers, long frameCount)
    {
        if (frameCount < 2)
        {
            return [];
        }

        var ordered = markers
            .Where(marker => marker.Frame >= 0 && marker.Frame < frameCount)
            .GroupBy(marker => marker.Frame)
            .Select(group => group.Last())
            .OrderBy(marker => marker.Frame)
            .ToArray();

        if (ordered.Length == 0)
        {
            return [WholeFile(frameCount)];
        }

        var splits = new SortedSet<long> { 0, frameCount };
        var loopStarts = new HashSet<long>();
        var exitStarts = new HashSet<long>();
        var anacrusisStarts = new HashSet<long>();

        var useImplicitTwoMarkerLoop = ordered.Length == 2
            && !ordered.Any(marker => HasExactSuffix(marker.Comment));

        if (useImplicitTwoMarkerLoop)
        {
            foreach (var marker in ordered)
            {
                splits.Add(marker.Frame);
            }

            ApplyLoopRange(ordered[0].Frame, ordered[1].Frame, splits, loopStarts);
        }
        else
        {
            var loopKeywordMarkers = new List<WaveMarker>();
            foreach (var marker in ordered)
            {
                splits.Add(marker.Frame);
                var role = MarkerRoles.FromComment(marker.Comment);
                switch (role)
                {
                    case MarkerRole.Loop:
                        loopStarts.Add(marker.Frame);
                        break;
                    case MarkerRole.Exit:
                        exitStarts.Add(marker.Frame);
                        break;
                    case MarkerRole.Anacrusis:
                        anacrusisStarts.Add(marker.Frame);
                        break;
                }

                if (ContainsLoopKeyword(marker.Comment) && role != MarkerRole.Loop)
                {
                    loopKeywordMarkers.Add(marker);
                }
            }

            ApplyLoopKeywordMarkers(loopKeywordMarkers, splits, loopStarts);
        }

        if (loopStarts.Count == 0
            && exitStarts.Count == 0
            && anacrusisStarts.Count == 0)
        {
            return [WholeFile(frameCount)];
        }

        var splitList = splits.ToArray();
        var regions = new List<WaveOnlyRegion>(splitList.Length - 1);
        for (var i = 0; i + 1 < splitList.Length; i++)
        {
            var start = splitList[i];
            var end = splitList[i + 1];
            if (end <= start)
            {
                continue;
            }

            if (loopStarts.Contains(start))
            {
                regions.Add(Make(start, end, WaveOnlyRegionKind.Loop, "L"));
                continue;
            }

            if (exitStarts.Contains(start))
            {
                regions.Add(Make(start, end, WaveOnlyRegionKind.Exit, "E"));
                continue;
            }

            if (anacrusisStarts.Contains(start))
            {
                regions.Add(Make(start, end, WaveOnlyRegionKind.Anacrusis, "A"));
                continue;
            }

            regions.Add(Make(start, end, WaveOnlyRegionKind.Body, "Body"));
        }

        ApplyAutoExitAfterLoop(regions);
        return regions;
    }

    private static void ApplyLoopKeywordMarkers(
        IReadOnlyList<WaveMarker> loopKeywordMarkers,
        SortedSet<long> splits,
        HashSet<long> loopStarts)
    {
        if (loopKeywordMarkers.Count == 0)
        {
            return;
        }

        if (loopKeywordMarkers.Count == 1)
        {
            loopStarts.Add(loopKeywordMarkers[0].Frame);
            return;
        }

        for (var i = 0; i + 1 < loopKeywordMarkers.Count; i += 2)
        {
            ApplyLoopRange(
                loopKeywordMarkers[i].Frame,
                loopKeywordMarkers[i + 1].Frame,
                splits,
                loopStarts);
        }

        if (loopKeywordMarkers.Count % 2 == 1)
        {
            loopStarts.Add(loopKeywordMarkers[^1].Frame);
        }
    }

    private static void ApplyLoopRange(
        long start,
        long end,
        SortedSet<long> splits,
        HashSet<long> loopStarts)
    {
        if (end <= start)
        {
            return;
        }

        foreach (var sample in splits)
        {
            if (sample >= start && sample < end)
            {
                loopStarts.Add(sample);
            }
        }
    }

    private static void ApplyAutoExitAfterLoop(List<WaveOnlyRegion> regions)
    {
        for (var i = 0; i + 1 < regions.Count; i++)
        {
            if (regions[i].Kind != WaveOnlyRegionKind.Loop
                || regions[i + 1].Kind != WaveOnlyRegionKind.Body)
            {
                continue;
            }

            regions[i + 1] = Make(
                regions[i + 1].StartFrame,
                regions[i + 1].EndFrame,
                WaveOnlyRegionKind.Exit,
                "E");
        }
    }

    private static bool HasExportableSpecialRegions(IReadOnlyList<WaveOnlyRegion> regions) =>
        regions.Any(region =>
            region.Kind is WaveOnlyRegionKind.Anacrusis
                or WaveOnlyRegionKind.Loop
                or WaveOnlyRegionKind.Exit);

    private static bool HasExactSuffix(string? comment)
    {
        var role = MarkerRoles.FromComment(comment);
        return role is MarkerRole.Anacrusis or MarkerRole.Loop or MarkerRole.Exit or MarkerRole.Remove
            && string.Equals((comment ?? string.Empty).Trim(), RoleTag(role), StringComparison.OrdinalIgnoreCase);
    }

    private static string RoleTag(MarkerRole role) => role switch
    {
        MarkerRole.Anacrusis => "-A",
        MarkerRole.Loop => "-L",
        MarkerRole.Exit => "-E",
        MarkerRole.Remove => "-R",
        _ => string.Empty,
    };

    private static bool ContainsLoopKeyword(string? comment) =>
        !string.IsNullOrEmpty(comment)
        && comment.Contains(LoopKeyword, StringComparison.OrdinalIgnoreCase);

    private static WaveOnlyRegion WholeFile(long frameCount) =>
        Make(0, frameCount, WaveOnlyRegionKind.Body, "Body");

    private static WaveOnlyRegion Make(long start, long end, WaveOnlyRegionKind kind, string name) =>
        new()
        {
            StartFrame = start,
            EndFrame = end,
            Kind = kind,
            Name = name,
        };

    /// <summary>
    /// <c>-A</c> は次のリージョンと、<c>-E</c> は直前のリージョンと同じ Music Segment にする。
    /// </summary>
    internal static IReadOnlyList<WaveOnlySegment> GroupIntoSegments(
        IReadOnlyList<WaveOnlyRegion> regions,
        string containerName)
    {
        var source = regions.Where(region => region.Export).ToArray();
        var groups = new List<List<WaveOnlyRegion>>();
        var i = 0;
        while (i < source.Length)
        {
            var group = new List<WaveOnlyRegion> { source[i] };
            if (source[i].Kind == WaveOnlyRegionKind.Anacrusis && i + 1 < source.Length)
            {
                i++;
                group.Add(source[i]);
            }

            if (i + 1 < source.Length && source[i + 1].Kind == WaveOnlyRegionKind.Exit)
            {
                i++;
                group.Add(source[i]);
            }

            groups.Add(group);
            i++;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = new WaveOnlySegment[groups.Count];
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var first = group[0];
            var last = group[^1];
            var entry = first.Kind == WaveOnlyRegionKind.Anacrusis && group.Count > 1
                ? group[1].StartFrame
                : first.StartFrame;
            var exit = last.Kind == WaveOnlyRegionKind.Exit && group.Count > 1
                ? last.StartFrame
                : last.EndFrame;
            var name = UniqueName(used, BuildSegmentName(containerName, g, groups.Count));
            segments[g] = new WaveOnlySegment
            {
                Name = name,
                StartFrame = first.StartFrame,
                EndFrame = last.EndFrame,
                EntryCueFrame = entry,
                ExitCueFrame = exit,
                LoopInfinite = group.Any(region => region.Kind == WaveOnlyRegionKind.Loop),
            };
        }

        return segments;
    }

    /// <summary>セグメントが 1 件なら接尾辞なし、複数なら _a, _b…（IM Importer と同じ）。</summary>
    private static string BuildSegmentName(string segmentBase, int index, int segmentCount) =>
        segmentCount == 1
            ? segmentBase
            : $"{segmentBase}_{IndexToLetters(index)}";

    /// <summary>0→a, 1→b, …, 25→z, 26→aa。</summary>
    private static string IndexToLetters(int index)
    {
        var buffer = new System.Text.StringBuilder();
        var n = index;
        do
        {
            buffer.Insert(0, (char)('a' + n % 26));
            n = n / 26 - 1;
        }
        while (n >= 0);

        return buffer.ToString();
    }

    private static string UniqueName(HashSet<string> used, string baseName)
    {
        var name = SanitizeWwiseName(baseName);
        var unique = name;
        var n = 2;
        while (!used.Add(unique))
        {
            unique = $"{name}_{n}";
            n++;
        }

        return unique;
    }

    private static IReadOnlyList<WaveOnlyRegion> AssignUniqueNames(
        IReadOnlyList<WaveOnlyRegion> regions,
        string containerName)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new WaveOnlyRegion[regions.Count];
        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var baseName = region.Kind switch
            {
                WaveOnlyRegionKind.Anacrusis => $"{containerName}_A",
                WaveOnlyRegionKind.Loop => $"{containerName}_L",
                WaveOnlyRegionKind.Exit => $"{containerName}_E",
                _ => regions.Count == 1 ? containerName : $"{containerName}_{region.Name}",
            };

            var name = SanitizeWwiseName(baseName);
            var unique = name;
            var n = 2;
            while (!used.Add(unique))
            {
                unique = $"{name}_{n}";
                n++;
            }

            result[i] = new WaveOnlyRegion
            {
                StartFrame = region.StartFrame,
                EndFrame = region.EndFrame,
                Kind = region.Kind,
                Name = unique,
            };
        }

        return result;
    }

    internal static string SanitizeWwiseName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return UiStrings.UntitledDocument;
        }

        var buffer = name.Trim().ToCharArray();
        for (var i = 0; i < buffer.Length; i++)
        {
            var c = buffer[i];
            if (c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            {
                buffer[i] = '_';
            }
        }

        var sanitized = new string(buffer).Trim();
        return sanitized.Length == 0 ? UiStrings.UntitledDocument : sanitized;
    }
}
