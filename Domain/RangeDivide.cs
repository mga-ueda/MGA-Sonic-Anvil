using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Domain;

/// <summary>選択範囲を等分してマーカー／リージョンを打つ。1回目は両端（同じ種類が既にあればすぐ等分）、以降は 2、3…と打ち直す。回数の上限は無い。</summary>
internal static class RangeDivide
{
    public static int NextParts(int current)
    {
        if (current < 1)
        {
            return 1;
        }

        return current == int.MaxValue ? current : current + 1;
    }

    public static long[] MarkerFrames(WaveSelection range, int parts)
    {
        if (range.IsEmpty)
        {
            return [];
        }

        var start = range.StartFrame;
        var length = range.EndFrame - range.StartFrame;
        var n = ResolvePartCount(parts, length);
        var points = new List<long>(n + 1);
        for (var i = 0; i <= n; i++)
        {
            var frame = start + length * i / n;
            if (points.Count == 0 || points[^1] != frame)
            {
                points.Add(frame);
            }
        }

        return [.. points];
    }

    /// <summary>整数サンプル上で区別できる等分は、範囲の長さまで。</summary>
    public static int ResolvePartCount(int parts, long length)
    {
        if (parts < 1 || length <= 1)
        {
            return 1;
        }

        return parts > length ? (int)Math.Min(length, int.MaxValue) : parts;
    }

    public static long[] InteriorFrames(WaveSelection range, int parts)
    {
        var all = MarkerFrames(range, parts);
        return all.Length <= 2 ? [] : all[1..^1];
    }

    public static WaveSelection[] EqualRegions(WaveSelection range, int parts)
    {
        if (range.IsEmpty)
        {
            return [];
        }

        var n = ResolvePartCount(parts, range.EndFrame - range.StartFrame);
        if (n == 1)
        {
            return [range];
        }

        var points = MarkerFrames(range, n);
        var regions = new List<WaveSelection>(points.Length - 1);
        for (var i = 0; i < points.Length - 1; i++)
        {
            if (points[i + 1] > points[i])
            {
                regions.Add(new WaveSelection(points[i], points[i + 1]));
            }
        }

        return [.. regions];
    }

    public static MarkerSnapshot[] ApplyMarkers(
        IReadOnlyList<MarkerSnapshot> before,
        WaveSelection range,
        int previousParts,
        int nextParts)
    {
        var remove = previousParts >= 2
            ? new HashSet<long>(InteriorFrames(range, previousParts))
            : [];
        var map = new Dictionary<long, string>();
        foreach (var marker in before)
        {
            if (!remove.Contains(marker.Frame))
            {
                map[marker.Frame] = marker.Comment ?? string.Empty;
            }
        }

        foreach (var frame in MarkerFrames(range, nextParts))
        {
            map.TryAdd(frame, string.Empty);
        }

        var after = new MarkerSnapshot[map.Count];
        var i = 0;
        foreach (var pair in map.OrderBy(item => item.Key))
        {
            after[i++] = new MarkerSnapshot(pair.Key, pair.Value);
        }

        return after;
    }

    public static WaveRegion[] ApplyRegions(
        IReadOnlyList<WaveRegion> before,
        WaveSelection range,
        int previousParts,
        int nextParts)
    {
        var remove = previousParts >= 1
            ? new HashSet<WaveSelection>(EqualRegions(range, previousParts))
            : [];
        var pieces = EqualRegions(range, nextParts);
        var next = new List<WaveRegion>(before.Count + pieces.Length);
        foreach (var region in before)
        {
            if (!remove.Contains(region.Range))
            {
                next.Add(region);
            }
        }

        foreach (var piece in pieces)
        {
            var exists = false;
            foreach (var item in next)
            {
                if (item.Range == piece)
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                next.Add(new WaveRegion(piece, string.Empty));
            }
        }

        next.Sort(static (a, b) =>
        {
            var byStart = a.StartFrame.CompareTo(b.StartFrame);
            return byStart != 0 ? byStart : a.EndFrame.CompareTo(b.EndFrame);
        });
        return [.. next];
    }

    /// <summary>連続操作中は state。無ければ同じ種類の両端が既にあれば 1 回目済みとみなす。</summary>
    public static int ResolvePreviousParts(
        RangeDivideState? state,
        AudioDocument document,
        WaveSelection range,
        bool regions)
    {
        if (state is { } current && current.Matches(document, range))
        {
            return current.Parts;
        }

        return (regions ? HasRegionsAtEnds(document, range) : HasMarkersAtEnds(document, range))
            ? 1
            : 0;
    }

    /// <summary>選択の両端にマーカーがあれば、マーカーの両端打ちは済んでいる。</summary>
    public static bool HasMarkersAtEnds(AudioDocument document, WaveSelection range) =>
        !range.IsEmpty
        && document.HasMarkerAt(range.StartFrame)
        && document.HasMarkerAt(range.EndFrame);

    /// <summary>選択の両端にリージョン端があれば、リージョン 1 本は済んでいる。</summary>
    public static bool HasRegionsAtEnds(AudioDocument document, WaveSelection range)
    {
        if (range.IsEmpty)
        {
            return false;
        }

        var start = false;
        var end = false;
        foreach (var region in document.Regions)
        {
            if (region.StartFrame == range.StartFrame)
            {
                start = true;
            }

            if (region.EndFrame == range.EndFrame)
            {
                end = true;
            }

            if (start && end)
            {
                return true;
            }
        }

        return false;
    }
}

internal readonly record struct RangeDivideState(
    AudioDocument Document,
    long StartFrame,
    long EndFrame,
    int Parts)
{
    public bool Matches(AudioDocument document, WaveSelection range) =>
        ReferenceEquals(Document, document)
        && StartFrame == range.StartFrame
        && EndFrame == range.EndFrame
        && Parts > 0;
}
