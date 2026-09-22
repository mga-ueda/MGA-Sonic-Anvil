using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>等分の連続操作をドキュメント状態と突き合わせる。</summary>
internal static class DocumentRangeDivide
{
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
        RangeDivide.HasMarkersAtEnds(range, document.HasMarkerAt);

    /// <summary>選択の両端にリージョン端があれば、リージョン 1 本は済んでいる。</summary>
    public static bool HasRegionsAtEnds(AudioDocument document, WaveSelection range) =>
        RangeDivide.HasRegionsAtEnds(range, document.Regions);

    /// <summary>
    /// いまの選択内のマーカー位置と、選択内にあるリージョン端。
    /// 選択が空なら空。セッションの等分状態には依存しない。
    /// </summary>
    public static long[] ClickFrames(AudioDocument document)
    {
        var range = document.Selection;
        if (range.IsEmpty)
        {
            return [];
        }

        var set = new SortedSet<long>();
        foreach (var marker in document.Markers)
        {
            if (marker.Frame >= range.StartFrame && marker.Frame <= range.EndFrame)
            {
                set.Add(marker.Frame);
            }
        }

        foreach (var region in document.Regions)
        {
            if (region.StartFrame >= range.StartFrame && region.StartFrame <= range.EndFrame)
            {
                set.Add(region.StartFrame);
            }

            if (region.EndFrame >= range.StartFrame && region.EndFrame <= range.EndFrame)
            {
                set.Add(region.EndFrame);
            }
        }

        return [.. set];
    }

    /// <summary>
    /// Undo／Redo 後など、選択が残っているときにドキュメント上の等分から
    /// 連続操作状態を復元する。見つからなければ両方 null。
    /// </summary>
    public static void RestoreStates(
        AudioDocument document,
        out RangeDivideState? markerDivide,
        out RangeDivideState? regionDivide)
    {
        markerDivide = null;
        regionDivide = null;
        var range = document.Selection;
        if (range.IsEmpty)
        {
            return;
        }

        var markerParts = InferMarkerParts(document, range);
        if (markerParts > 0)
        {
            markerDivide = new RangeDivideState(document, range.StartFrame, range.EndFrame, markerParts);
        }

        var regionParts = InferRegionParts(document, range);
        if (regionParts > 0)
        {
            regionDivide = new RangeDivideState(document, range.StartFrame, range.EndFrame, regionParts);
        }
    }

    /// <summary>選択内のマーカーが等分になっていれば、その分割数。無ければ 0。</summary>
    public static int InferMarkerParts(AudioDocument document, WaveSelection range)
    {
        if (range.IsEmpty || !HasMarkersAtEnds(document, range))
        {
            return 0;
        }

        var inRange = 0;
        foreach (var marker in document.Markers)
        {
            if (marker.Frame >= range.StartFrame && marker.Frame <= range.EndFrame)
            {
                inRange++;
            }
        }

        for (var parts = Math.Max(1, inRange - 1); parts >= 1; parts--)
        {
            if (HasAllMarkerFrames(document, range, parts))
            {
                return parts;
            }
        }

        return 0;
    }

    /// <summary>選択を覆うリージョンが等分になっていれば、その分割数。無ければ 0。</summary>
    public static int InferRegionParts(AudioDocument document, WaveSelection range)
    {
        if (range.IsEmpty || !document.AllowsRegionsAndLoops)
        {
            return 0;
        }

        var inside = 0;
        foreach (var region in document.Regions)
        {
            if (region.StartFrame >= range.StartFrame && region.EndFrame <= range.EndFrame)
            {
                inside++;
            }
        }

        for (var parts = Math.Max(1, inside); parts >= 1; parts--)
        {
            if (HasAllEqualRegions(document, range, parts))
            {
                return parts;
            }
        }

        return 0;
    }

    private static bool HasAllMarkerFrames(AudioDocument document, WaveSelection range, int parts)
    {
        foreach (var frame in RangeDivide.MarkerFrames(range, parts))
        {
            if (!document.HasMarkerAt(frame))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasAllEqualRegions(AudioDocument document, WaveSelection range, int parts)
    {
        foreach (var piece in RangeDivide.EqualRegions(range, parts))
        {
            var found = false;
            foreach (var region in document.Regions)
            {
                if (region == piece)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
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
