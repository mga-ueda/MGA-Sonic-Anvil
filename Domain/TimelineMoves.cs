using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

internal readonly record struct RangeEdgeMove(WaveSelection Range, bool Start, bool End)
{
    public static RangeEdgeMove Translate(WaveSelection range) => new(range, true, true);

    public static RangeEdgeMove FromStart(WaveSelection range) => new(range, true, false);

    public static RangeEdgeMove FromEnd(WaveSelection range) => new(range, false, true);

    public bool IsEmpty => Range.IsEmpty || (!Start && !End);
}

internal static class TimelineMoves
{
    public static long ClampRangeDelta(IReadOnlyList<WaveSelection> ranges, long desiredDelta, long frameCount)
    {
        if (desiredDelta == 0 || ranges is null || ranges.Count == 0)
        {
            return 0;
        }

        var moves = new List<RangeEdgeMove>(ranges.Count);
        foreach (var range in ranges)
        {
            if (!range.IsEmpty)
            {
                moves.Add(RangeEdgeMove.Translate(range));
            }
        }

        return ClampEdgeMoves(moves, desiredDelta, frameCount);
    }

    public static long ClampEdgeMoves(IReadOnlyList<RangeEdgeMove> moves, long desiredDelta, long frameCount)
    {
        if (desiredDelta == 0 || moves is null || moves.Count == 0)
        {
            return 0;
        }

        var delta = desiredDelta;
        foreach (var move in moves)
        {
            if (move.IsEmpty)
            {
                continue;
            }

            var range = move.Range;
            if (move.Start && move.End)
            {
                if (delta > 0)
                {
                    delta = Math.Min(delta, Math.Max(0, frameCount - range.EndFrame));
                }
                else
                {
                    delta = Math.Max(delta, -range.StartFrame);
                }

                continue;
            }

            if (move.Start)
            {
                if (delta > 0)
                {
                    delta = Math.Min(delta, Math.Max(0, range.EndFrame - 1 - range.StartFrame));
                }
                else
                {
                    delta = Math.Max(delta, -range.StartFrame);
                }
            }

            if (move.End)
            {
                if (delta > 0)
                {
                    delta = Math.Min(delta, Math.Max(0, frameCount - range.EndFrame));
                }
                else
                {
                    delta = Math.Max(delta, range.StartFrame + 1 - range.EndFrame);
                }
            }
        }

        return delta;
    }

    public static long Resolve(
        IReadOnlyList<long> movingMarkersSorted,
        HashSet<long> occupiedMarkers,
        IReadOnlyList<WaveSelection> movingRanges,
        long desiredDelta,
        long frameCount)
    {
        var moves = new List<RangeEdgeMove>();
        if (movingRanges is not null)
        {
            foreach (var range in movingRanges)
            {
                if (!range.IsEmpty)
                {
                    moves.Add(RangeEdgeMove.Translate(range));
                }
            }
        }

        return Resolve(movingMarkersSorted, occupiedMarkers, moves, desiredDelta, frameCount);
    }

    public static long Resolve(
        IReadOnlyList<long> movingMarkersSorted,
        HashSet<long> occupiedMarkers,
        IReadOnlyList<RangeEdgeMove> edgeMoves,
        long desiredDelta,
        long frameCount)
    {
        if (desiredDelta == 0)
        {
            return 0;
        }

        var hasEdges = edgeMoves is { Count: > 0 };
        var hasMarkers = movingMarkersSorted.Count > 0;
        if (!hasEdges && !hasMarkers)
        {
            return 0;
        }

        var delta = desiredDelta;
        if (hasEdges)
        {
            delta = ClampEdgeMoves(edgeMoves, delta, frameCount);
        }

        if (hasMarkers)
        {
            delta = MarkerMoves.ResolveGroupDelta(
                movingMarkersSorted,
                delta,
                occupiedMarkers,
                Math.Max(0, frameCount));
        }

        return delta;
    }

    public static WaveSelection Shift(WaveSelection range, long delta) =>
        ShiftEdges(range, start: true, end: true, delta);

    public static WaveSelection ShiftEdges(WaveSelection range, bool start, bool end, long delta)
    {
        if (range.IsEmpty || delta == 0 || (!start && !end))
        {
            return range;
        }

        var nextStart = start ? range.StartFrame + delta : range.StartFrame;
        var nextEnd = end ? range.EndFrame + delta : range.EndFrame;
        return new WaveSelection(nextStart, nextEnd);
    }
}