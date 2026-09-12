using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal static partial class ProcessEdits
{
    public static IEditCommand? SetSampleLoop(AudioDocument document, WaveSelection range)
    {
        if (!document.AllowsRegionsAndLoops && !range.IsEmpty)
        {
            return null;
        }

        var before = document.SampleLoop;
        var after = range.IsEmpty ? WaveSelection.Empty : range.Clamp(document.FrameCount);
        if (before == after)
        {
            if (after.IsEmpty)
            {
                return null;
            }

            after = WaveSelection.Empty;
        }

        var command = new SetSampleLoopCommand(before, after, SampleLoopSummary(document.SampleRate, after));
        var sourceRate = document.SampleRate;
        var sourceAfter = after;
        // 再適用は「after の状態にする」。トグルではないので同じ範囲でも解除にならない。
        command.Replay = target =>
        {
            var mapped = sourceAfter.IsEmpty
                ? WaveSelection.Empty
                : EditReplay.MapRange(sourceAfter, sourceRate, target);
            if (target.SampleLoop == mapped)
            {
                return null;
            }

            return new SetSampleLoopCommand(
                target.SampleLoop,
                mapped,
                SampleLoopSummary(target.SampleRate, mapped));
        };
        command.Persist = HistoryRecipes.Range(HistoryRecipes.SetSampleLoop, sourceRate, sourceAfter);
        return command;
    }

    private static string SampleLoopSummary(int sampleRate, WaveSelection after) =>
        after.IsEmpty
            ? UiStrings.EditHistoryName("Set Sample Loop") + "  解除"
            : UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Set Sample Loop"),
                sampleRate,
                after.StartFrame,
                after.EndFrame);

    public static IEditCommand? SetRegion(AudioDocument document, WaveSelection range)
    {
        if (!document.AllowsRegionsAndLoops && !range.IsEmpty)
        {
            return null;
        }

        var before = document.SnapshotRegions();
        var afterList = new List<WaveRegion>(before);
        if (range.IsEmpty)
        {
            if (afterList.Count == 0)
            {
                return null;
            }

            afterList.Clear();
        }
        else
        {
            var next = range.Clamp(document.FrameCount);
            if (next.IsEmpty)
            {
                return null;
            }

            var index = afterList.FindIndex(item => item.Range == next);
            if (index >= 0)
            {
                afterList.RemoveAt(index);
            }
            else
            {
                afterList.Add(new WaveRegion(next, string.Empty));
            }
        }

        var after = afterList.ToArray();
        var summary = after.Length < before.Length
            ? UiStrings.EditHistoryName("Set Region") + "  解除"
            : UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Set Region"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame);
        var command = new SetRegionCommand(before, after, summary);
        var sourceRate = document.SampleRate;
        var sourceRange = range;
        var wasRemoval = after.Length < before.Length;
        command.Replay = target =>
        {
            // 全解除は適用先も全解除。個別は追加／解除の向きを保つ（トグルの反転を防ぐ）。
            if (sourceRange.IsEmpty)
            {
                var targetBefore = target.SnapshotRegions();
                return targetBefore.Length == 0
                    ? null
                    : new SetRegionCommand(targetBefore, [], UiStrings.EditHistoryName("Set Region") + "  解除");
            }

            var mapped = EditReplay.MapRange(sourceRange, sourceRate, target);
            if (mapped.IsEmpty)
            {
                return null;
            }

            var exists = target.RegionNumber(mapped) > 0;
            if (wasRemoval)
            {
                return exists ? RemoveRegions(target, [mapped]) : null;
            }

            return exists ? null : SetRegion(target, mapped);
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.SetRegion,
            SourceRate = sourceRate,
            Start = sourceRange.StartFrame,
            End = sourceRange.EndFrame,
            Flag = wasRemoval,
        };
        return command;
    }

    public static IEditCommand? RemoveRegions(AudioDocument document, IReadOnlyList<WaveSelection> ranges)
    {
        if (ranges is null || ranges.Count == 0)
        {
            return null;
        }

        var before = document.SnapshotRegions();
        var remove = new HashSet<WaveSelection>();
        foreach (var range in ranges)
        {
            if (!range.IsEmpty)
            {
                remove.Add(range);
            }
        }

        var after = before.Where(region => !remove.Contains(region.Range)).ToArray();
        if (after.Length == before.Length)
        {
            return null;
        }

        var command = new SetRegionCommand(
            before,
            after,
            UiStrings.EditHistoryName("Set Region") + "  解除");
        var sourceRate = document.SampleRate;
        var removedRanges = remove.ToArray();
        command.Replay = target => RemoveRegions(
            target,
            removedRanges
                .Select(item => EditReplay.MapRange(item, sourceRate, target))
                .Where(item => !item.IsEmpty)
                .ToArray());
        command.Persist = HistoryRecipes.FromRanges(HistoryRecipes.RemoveRegions, sourceRate, removedRanges);
        return command;
    }

    public static IEditCommand AddMarker(AudioDocument document, long frame)
    {
        var command = new AddMarkerCommand(
            frame,
            document.SnapshotMarkers(),
            UiStrings.EditHistoryPoint(UiStrings.EditHistoryName("Add Marker"), document.SampleRate, frame));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
        {
            var mapped = EditReplay.MapFrame(frame, sourceRate, target);
            return target.HasMarkerAt(mapped) ? null : AddMarker(target, mapped);
        };
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.AddMarker,
            SourceRate = sourceRate,
            Frame = frame,
        };
        return command;
    }

    public static IEditCommand? DivideMarkers(
        AudioDocument document,
        WaveSelection range,
        int previousParts,
        int nextParts)
    {
        if (range.IsEmpty)
        {
            return null;
        }

        var after = RangeDivide.ApplyMarkers(document.SnapshotMarkers(), range, previousParts, nextParts);
        return ApplyMarkers(document, after);
    }

    public static IEditCommand? ApplyMarkers(AudioDocument document, MarkerSnapshot[] after) =>
        ApplyMarkers(document, document.SnapshotMarkers(), after);

    public static IEditCommand? ApplyMarkers(
        AudioDocument document,
        MarkerSnapshot[] before,
        MarkerSnapshot[] after)
    {
        var command = ReplaceMarkers(before, after, "Add Marker", document.SampleRate);
        if (command is null)
        {
            return null;
        }

        var sourceRate = document.SampleRate;
        command.Replay = target => ApplyMarkers(target, MapMarkers(after, sourceRate, target));
        command.Persist = HistoryRecipes.FromMarkerSnapshots(HistoryRecipes.ReplaceMarkers, sourceRate, after);
        return command;
    }

    public static IEditCommand? DivideRegions(
        AudioDocument document,
        WaveSelection range,
        int previousParts,
        int nextParts)
    {
        if (range.IsEmpty || !document.AllowsRegionsAndLoops)
        {
            return null;
        }

        var after = RangeDivide.ApplyRegions(document.SnapshotRegions(), range, previousParts, nextParts);
        return ApplyRegions(document, after, range);
    }

    public static IEditCommand? ApplyRegions(
        AudioDocument document,
        WaveRegion[] after,
        WaveSelection? span = null) =>
        ApplyRegions(document, document.SnapshotRegions(), after, span);

    public static IEditCommand? ApplyRegions(
        AudioDocument document,
        WaveRegion[] before,
        WaveRegion[] after,
        WaveSelection? span = null)
    {
        if (!document.AllowsRegionsAndLoops && after.Length > 0)
        {
            return null;
        }

        if (RegionsEqual(before, after))
        {
            return null;
        }

        var summary = span is { IsEmpty: false } range
            ? UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Set Region"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame)
            : UiStrings.EditHistoryName("Set Region");
        var command = new SetRegionCommand(before, after, summary);
        var sourceRate = document.SampleRate;
        command.Replay = target => ApplyRegions(target, MapRegions(after, sourceRate, target));
        command.Persist = HistoryRecipes.FromRegionSnapshots(HistoryRecipes.ReplaceRegions, sourceRate, after);
        return command;
    }

    private static MarkerSnapshot[] MapMarkers(
        IReadOnlyList<MarkerSnapshot> source,
        int sourceRate,
        AudioDocument target)
    {
        var mapped = new MarkerSnapshot[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            mapped[i] = new MarkerSnapshot(
                EditReplay.MapFrame(source[i].Frame, sourceRate, target),
                source[i].Comment);
        }

        return mapped;
    }

    private static WaveRegion[] MapRegions(
        IReadOnlyList<WaveRegion> source,
        int sourceRate,
        AudioDocument target)
    {
        var mapped = new WaveRegion[source.Count];
        for (var i = 0; i < source.Count; i++)
        {
            var range = EditReplay.MapRange(source[i].Range, sourceRate, target);
            mapped[i] = range.IsEmpty
                ? WaveRegion.Empty
                : new WaveRegion(range, source[i].Name);
        }

        return mapped;
    }

    public static IEditCommand? SetMarkerComment(AudioDocument document, long frame, string comment)
    {
        var before = document.MarkerCommentAt(frame);
        var after = MarkerRoles.Normalize(comment);
        if (before == after || !document.HasMarkerAt(frame))
        {
            return null;
        }

        var command = new SetMarkerCommentCommand(
            frame,
            before,
            after,
            UiStrings.EditHistoryPoint(
                UiStrings.EditHistoryName("Marker Comment"),
                document.SampleRate,
                frame,
                UiStrings.EditHistoryQuote(after)));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
            SetMarkerComment(target, EditReplay.MapFrame(frame, sourceRate, target), after);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.MarkerComment,
            SourceRate = sourceRate,
            Frame = frame,
            Text = after,
        };
        return command;
    }

    public static IEditCommand? SetRegionName(AudioDocument document, WaveSelection range, string name)
    {
        var before = document.RegionName(range);
        var after = MarkerRoles.Normalize(name);
        if (before == after || document.RegionNumber(range) <= 0)
        {
            return null;
        }

        var command = new SetRegionNameCommand(
            range,
            before,
            after,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Region Name"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                UiStrings.EditHistoryQuote(after)));
        var sourceRate = document.SampleRate;
        command.Replay = target =>
            SetRegionName(target, EditReplay.MapRange(range, sourceRate, target), after);
        command.Persist = new HistoryRecipe
        {
            Kind = HistoryRecipes.RegionName,
            SourceRate = sourceRate,
            Start = range.StartFrame,
            End = range.EndFrame,
            Text = after,
        };
        return command;
    }

    public static IEditCommand? RemoveMarkers(AudioDocument document, IReadOnlyList<long> frames)
    {
        var before = document.SnapshotMarkers();
        var remove = new HashSet<long>(frames);
        var after = before.Where(marker => !remove.Contains(marker.Frame)).ToArray();
        if (after.Length == before.Length)
        {
            return null;
        }

        var command = new ReplaceMarkersCommand(
            "Delete Markers",
            before,
            after,
            UiStrings.EditHistoryMarkers("Delete Markers", before, after, document.SampleRate));
        var sourceRate = document.SampleRate;
        var removedFrames = remove.ToArray();
        command.Replay = target => RemoveMarkers(
            target,
            removedFrames.Select(item => EditReplay.MapFrame(item, sourceRate, target)).ToArray());
        command.Persist = HistoryRecipes.FromMarkers(HistoryRecipes.RemoveMarkers, sourceRate, removedFrames);
        return command;
    }

    public static IEditCommand? MoveMarkers(
        AudioDocument document,
        IReadOnlyList<long> frames,
        long delta,
        out long appliedDelta)
    {
        appliedDelta = 0;
        var before = document.SnapshotMarkers();
        var moving = new List<long>();
        foreach (var frame in frames)
        {
            if (document.HasMarkerAt(frame) && !moving.Contains(frame))
            {
                moving.Add(frame);
            }
        }

        if (moving.Count == 0 || delta == 0)
        {
            return null;
        }

        moving.Sort();
        var occupied = new HashSet<long>();
        foreach (var marker in before)
        {
            if (moving.BinarySearch(marker.Frame) < 0)
            {
                occupied.Add(marker.Frame);
            }
        }

        appliedDelta = MarkerMoves.ResolveGroupDelta(moving, delta, occupied, Math.Max(0, document.FrameCount));
        if (appliedDelta == 0)
        {
            return null;
        }

        var moved = moving.ToHashSet();
        var after = new MarkerSnapshot[before.Length];
        for (var i = 0; i < before.Length; i++)
        {
            var frame = moved.Contains(before[i].Frame)
                ? before[i].Frame + appliedDelta
                : before[i].Frame;
            after[i] = new MarkerSnapshot(frame, before[i].Comment);
        }

        Array.Sort(after, (left, right) => left.Frame.CompareTo(right.Frame));
        var name = moving.Count == 1 ? "Move Marker" : "Move Markers";
        var command = new ReplaceMarkersCommand(
            name,
            before,
            after,
            UiStrings.EditHistoryMarkers(name, before, after, document.SampleRate));
        var sourceRate = document.SampleRate;
        var movingFrames = moving.ToArray();
        var deltaApplied = appliedDelta;
        command.Replay = target => MoveMarkers(
            target,
            movingFrames.Select(item => EditReplay.MapFrame(item, sourceRate, target)).ToArray(),
            EditReplay.ScaleDelta(deltaApplied, sourceRate, target.SampleRate),
            out _);
        command.Persist = HistoryRecipes.FromMarkers(
            HistoryRecipes.MoveMarkers,
            sourceRate,
            movingFrames,
            deltaApplied);
        return command;
    }

    public static IEditCommand? MoveTimelineItems(
        AudioDocument document,
        MarkerSnapshot[] markersBefore,
        WaveRegion[] regionsBefore,
        WaveSelection loopBefore)
    {
        var markersAfter = document.SnapshotMarkers();
        var regionsAfter = document.SnapshotRegions();
        var loopAfter = document.SampleLoop;
        if (markersBefore.AsSpan().SequenceEqual(markersAfter)
            && RegionsEqual(regionsBefore, regionsAfter)
            && loopBefore == loopAfter)
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
            UiStrings.EditHistoryTimelineMove(
                markersBefore,
                markersAfter,
                regionsBefore,
                regionsAfter,
                loopBefore,
                loopAfter,
                document.SampleRate));
        command.Persist = HistoryRecipes.FromTimeline(
            document.SampleRate,
            markersAfter,
            regionsAfter,
            loopAfter);
        return command;
    }

    private static bool RegionsEqual(WaveRegion[] left, WaveRegion[] right)
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

    public static IEditCommand? ReplaceMarkers(
        MarkerSnapshot[] before,
        MarkerSnapshot[] after,
        string name,
        int sampleRate)
    {
        if (before.AsSpan().SequenceEqual(after))
        {
            return null;
        }

        return new ReplaceMarkersCommand(
            name,
            before,
            after,
            UiStrings.EditHistoryMarkers(name, before, after, sampleRate));
    }

}
