namespace MgaSonicAnvil.Audio;

/// <summary>マーカーまたはリージョンで波形を分割して書き出す区間。</summary>
internal static class WaveSeparate
{
    public static bool TrySegmentsByMarkers(AudioDocument document, out WaveSelection[] segments)
    {
        var found = FromMarkers(document);
        if (found.Length < 2)
        {
            segments = [];
            return false;
        }

        segments = found;
        return true;
    }

    public static bool TrySegmentsByRegions(AudioDocument document, out WaveSelection[] segments)
    {
        var found = FromRegions(document);
        if (found.Length < 1)
        {
            segments = [];
            return false;
        }

        segments = found;
        return true;
    }

    public static WaveSelection[] FromMarkers(AudioDocument document)
    {
        if (document.FrameCount <= 0)
        {
            return [];
        }

        var points = new SortedSet<long> { 0, document.FrameCount };
        foreach (var marker in document.Markers)
        {
            points.Add(Math.Clamp(marker.Frame, 0, document.FrameCount));
        }

        return Consecutive(points);
    }

    public static WaveSelection[] FromRegions(AudioDocument document)
    {
        var regions = document.Regions;
        if (regions.Count == 0)
        {
            return [];
        }

        var segments = new List<WaveSelection>(regions.Count);
        foreach (var region in regions)
        {
            var clamped = region.Clamp(document.FrameCount);
            if (!clamped.IsEmpty)
            {
                segments.Add(clamped);
            }
        }

        return [.. segments];
    }

    /// <summary>9 個以内は 1 桁、99 個までは 2 桁、といった具合に末尾番号を付ける。</summary>
    public static string IndexedBaseName(string baseName, int index, int count)
    {
        var safe = AudioExport.SanitizeBaseName(baseName);
        var width = Math.Max(1, count.ToString().Length);
        return $"{safe}_{index.ToString().PadLeft(width, '0')}";
    }

    private static WaveSelection[] Consecutive(SortedSet<long> points)
    {
        if (points.Count < 2)
        {
            return [];
        }

        var ordered = new long[points.Count];
        points.CopyTo(ordered);
        var segments = new List<WaveSelection>(ordered.Length - 1);
        for (var i = 0; i < ordered.Length - 1; i++)
        {
            if (ordered[i + 1] > ordered[i])
            {
                segments.Add(new WaveSelection(ordered[i], ordered[i + 1]));
            }
        }

        return [.. segments];
    }
}
