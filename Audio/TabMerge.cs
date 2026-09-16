using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>選択タブの波形を重ねて合成（ミックス）する。フォーマットは先頭タブに合わせる。</summary>
internal static class TabMerge
{
    /// <summary>バウンス結果のファイル名の元。重複時は "Bounce 2" のように振る。</summary>
    public const string MergedBaseName = "Bounce";

    public static AudioDocument Mix(
        IReadOnlyList<AudioDocument> sources,
        IProgress<double>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(sources));
        }

        var destRate = Math.Max(1, sources[0].SampleRate);
        var destChannels = Math.Max(1, sources[0].Channels);
        var destBits = Math.Max(1, sources[0].BitsPerSample);
        var chunks = new float[sources.Count][];
        var frameCounts = new int[sources.Count];
        var maxFrames = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            var slice = SliceProgress(progress, i, sources.Count);
            chunks[i] = Adapt(sources[i], destRate, destChannels, slice);
            frameCounts[i] = chunks[i].Length / destChannels;
            maxFrames = Math.Max(maxFrames, frameCounts[i]);
        }

        var interleaved = new float[checked(maxFrames * destChannels)];
        foreach (var chunk in chunks)
        {
            for (var s = 0; s < chunk.Length; s++)
            {
                interleaved[s] += chunk[s];
            }
        }

        var merged = new AudioDocument(
            interleaved,
            destRate,
            destChannels,
            destBits,
            AudioFileKind.Wave,
            sourcePath: null);
        merged.SetChannelMask(sources[0].ChannelMask);
        ApplyTimeline(merged, sources, destRate, frameCounts);
        merged.SetDirty(true);
        progress?.Report(1);
        return merged;
    }

    public static float[] AdaptChannels(float[] interleaved, int sourceChannels, int destChannels)
    {
        sourceChannels = Math.Max(1, sourceChannels);
        destChannels = Math.Max(1, destChannels);
        if (destChannels == sourceChannels)
        {
            return interleaved;
        }

        if (destChannels <= 2)
        {
            return FormatConvert.Remix(interleaved, sourceChannels, destChannels);
        }

        var frames = interleaved.Length / sourceChannels;
        if (frames <= 0)
        {
            return [];
        }

        var dest = new float[frames * destChannels];
        for (var frame = 0; frame < frames; frame++)
        {
            if (sourceChannels == 1)
            {
                dest[frame * destChannels] = interleaved[frame];
                dest[frame * destChannels + 1] = interleaved[frame];
                continue;
            }

            Array.Copy(
                interleaved,
                frame * sourceChannels,
                dest,
                frame * destChannels,
                Math.Min(sourceChannels, destChannels));
        }

        return dest;
    }

    public static int InsertIndex(int sessionCount, IReadOnlyList<int> selectedIndices)
    {
        var last = -1;
        foreach (var index in selectedIndices)
        {
            if (index > last)
            {
                last = index;
            }
        }

        var insertAt = last < 0 ? sessionCount : last + 1;
        return Math.Clamp(insertAt, 0, Math.Max(0, sessionCount));
    }

    private static float[] Adapt(
        AudioDocument document,
        int destRate,
        int destChannels,
        IProgress<double>? progress)
    {
        var samples = document.Interleaved;
        var count = Math.Max(0, document.SampleCount);
        if (count <= 0)
        {
            progress?.Report(1);
            return [];
        }

        if (count < samples.Length)
        {
            var exact = new float[count];
            Array.Copy(samples, exact, count);
            samples = exact;
        }

        samples = AdaptChannels(samples, document.Channels, destChannels);
        if (document.SampleRate != destRate)
        {
            samples = FormatConvert.Resample(samples, destChannels, document.SampleRate, destRate, progress);
        }
        else
        {
            progress?.Report(1);
        }

        return samples;
    }

    /// <summary>
    /// マーカーとリージョンは位置を保ったまま統合する。同じ位置のマーカーは
    /// 1 つにまとめる（同じコメントなら片方を破棄。違うときは先のタブを優先）。
    /// </summary>
    private static void ApplyTimeline(
        AudioDocument merged,
        IReadOnlyList<AudioDocument> sources,
        int destRate,
        IReadOnlyList<int> destFrameCounts)
    {
        var markers = new List<MarkerSnapshot>();
        var seenFrames = new HashSet<long>();
        var regions = new List<WaveRegion>();
        var seenRegions = new HashSet<WaveSelection>();
        for (var i = 0; i < sources.Count; i++)
        {
            var document = sources[i];
            var destFrames = destFrameCounts[i];
            if (destFrames <= 0)
            {
                continue;
            }

            foreach (var marker in document.SnapshotMarkers())
            {
                var dest = FormatConvert.ScaleFrame(
                    marker.Frame,
                    document.SampleRate,
                    destRate,
                    destFrames);
                if (!seenFrames.Add(dest))
                {
                    continue;
                }

                markers.Add(new MarkerSnapshot(dest, marker.Comment));
            }

            if (document.AllowsRegionsAndLoops)
            {
                foreach (var region in document.SnapshotRegions())
                {
                    var dest = FormatConvert.ScaleSelection(
                        region.Range,
                        document.SampleRate,
                        destRate,
                        destFrames);
                    if (dest.IsEmpty || !seenRegions.Add(dest))
                    {
                        continue;
                    }

                    regions.Add(new WaveRegion(dest, region.Name));
                }
            }
        }

        merged.ReplaceMarkers(markers, markDirty: false, normalizeComments: false);
        if (regions.Count > 0)
        {
            merged.SetRegions(regions, markDirty: false);
        }
    }

    private static IProgress<double>? SliceProgress(IProgress<double>? progress, int index, int count)
    {
        if (progress is null || count <= 0)
        {
            return progress;
        }

        return new Progress<double>(value =>
            progress.Report((index + Math.Clamp(value, 0, 1)) / count));
    }
}
