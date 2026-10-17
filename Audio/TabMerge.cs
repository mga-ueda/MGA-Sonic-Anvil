using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// 選択タブの波形を先頭サンプルで加算する。
/// サンプルレート、チャンネル数、ビット深度が先頭と違うファイルは変換せず中止する。
/// </summary>
internal static class TabMerge
{
    /// <summary>バウンス結果のファイル名の元。重複時は "Bounce 2" のように振る。</summary>
    public const string MergedBaseName = "Bounce";

    public readonly struct BounceClipInfo
    {
        public float Peak { get; init; }

        /// <summary>0 dBFS を超えた量。超えていなければ 0。</summary>
        public double OverDb { get; init; }

        public bool ExceedsFullScale => Peak > 1f;
    }

    public static AudioDocument Mix(
        IReadOnlyList<AudioDocument> sources,
        IProgress<double>? progress = null)
        => Mix(sources, out _, progress);

    public static AudioDocument Mix(
        IReadOnlyList<AudioDocument> sources,
        out BounceClipInfo clip,
        IProgress<double>? progress = null,
        IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one document is required.", nameof(sources));
        }

        EnsureSameFormat(sources, names);

        var destRate = Math.Max(1, sources[0].SampleRate);
        var destChannels = Math.Max(1, sources[0].Channels);
        var destBits = Math.Max(1, sources[0].BitsPerSample);
        var chunks = new float[sources.Count][];
        var frameCounts = new int[sources.Count];
        var maxFrames = 0;
        for (var i = 0; i < sources.Count; i++)
        {
            var slice = SliceProgress(progress, i, sources.Count);
            chunks[i] = CopySamples(sources[i], slice);
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
        clip = MeasureClip(interleaved);
        progress?.Report(1);
        return merged;
    }

    /// <summary>先頭タブとサンプルレート・チャンネル数・ビット深度が違うファイルがあれば、名前を挙げて中止する。</summary>
    public static void EnsureSameFormat(
        IReadOnlyList<AudioDocument> sources,
        IReadOnlyList<string>? names = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count <= 1)
        {
            return;
        }

        var reference = sources[0];
        var mismatches = new List<string>();
        for (var i = 1; i < sources.Count; i++)
        {
            var document = sources[i];
            if (document.SampleRate == reference.SampleRate
                && document.Channels == reference.Channels
                && document.BitsPerSample == reference.BitsPerSample)
            {
                continue;
            }

            mismatches.Add(UiStrings.MergeFormatFile(
                Label(document, names, i),
                document.SampleRate,
                document.Channels,
                document.BitsPerSample));
        }

        if (mismatches.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(UiStrings.ErrorMergeFormatMismatch(
            UiStrings.MergeFormatFile(
                Label(reference, names, 0),
                reference.SampleRate,
                reference.Channels,
                reference.BitsPerSample),
            string.Join(Environment.NewLine, mismatches)));
    }

    public static BounceClipInfo MeasureClip(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                return new BounceClipInfo { Peak = float.PositiveInfinity, OverDb = double.PositiveInfinity };
            }

            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        if (!(peak > 1f))
        {
            return new BounceClipInfo { Peak = peak, OverDb = 0 };
        }

        return new BounceClipInfo { Peak = peak, OverDb = LevelMeterEngine.ToDb(peak) };
    }

    /// <summary>検出したピークが ±1 になるよう、ミックス全体を同じ倍率で下げる。</summary>
    public static void LowerByOverage(float[] samples, float peak)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (!float.IsFinite(peak))
        {
            for (var i = 0; i < samples.Length; i++)
            {
                if (!float.IsFinite(samples[i]))
                {
                    samples[i] = 0;
                }
            }

            peak = MeasureClip(samples).Peak;
        }

        for (var pass = 0; pass < 3 && peak > 1f; pass++)
        {
            var scale = 1d / peak;
            var next = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                var scaled = (float)(samples[i] * scale);
                samples[i] = scaled;
                var magnitude = Math.Abs(scaled);
                if (magnitude > next)
                {
                    next = magnitude;
                }
            }

            peak = next;
        }
    }

    /// <summary>1 dB ちょうどは "1"。0.1 dB 未満の超過は "&lt; 0.1"。</summary>
    public static string FormatOverDb(double overDb)
    {
        if (double.IsPositiveInfinity(overDb))
        {
            return "∞";
        }

        if (!(overDb > 0) || !double.IsFinite(overDb))
        {
            return "0";
        }

        if (overDb < 0.05)
        {
            return "< 0.1";
        }

        return overDb.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
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

    private static float[] CopySamples(AudioDocument document, IProgress<double>? progress)
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
            progress?.Report(1);
            return samples;
        }

        progress?.Report(1);
        return samples;
    }

    private static string Label(AudioDocument document, IReadOnlyList<string>? names, int index)
    {
        if (names is not null
            && (uint)index < (uint)names.Count
            && !string.IsNullOrWhiteSpace(names[index]))
        {
            return names[index];
        }

        if (document.SourcePath is { Length: > 0 } path)
        {
            return Path.GetFileName(path);
        }

        return UiStrings.UntitledDocument;
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
