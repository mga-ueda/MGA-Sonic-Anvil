namespace MgaSonicAnvil.Audio;

/// <summary>
/// 全体を一度走査して作る min/max のミップ階層。
/// 構築後は任意の表示窓を O(画面幅) で集計でき、ズームで生サンプルへ戻らない。
/// IM Importer / TimeCaster と同じ LOD 選択。
/// </summary>
internal sealed class PeakPyramid
{
    private const int TargetBaseBuckets = 1 << 21;

    private readonly float[][] _minLevels;
    private readonly float[][] _maxLevels;

    private PeakPyramid(
        float[][] minLevels,
        float[][] maxLevels,
        int channels,
        long frameCount,
        int baseBucketFrames)
    {
        _minLevels = minLevels;
        _maxLevels = maxLevels;
        Channels = channels;
        FrameCount = frameCount;
        BaseBucketFrames = baseBucketFrames;
    }

    public int Channels { get; }

    public long FrameCount { get; }

    /// <summary>基底レベル 1 バケットあたりのフレーム数（1 なら全サンプル保持と等価）。</summary>
    public int BaseBucketFrames { get; }

    public bool IsEmpty => FrameCount <= 0 || _minLevels.Length == 0;

    public static PeakPyramid Empty { get; } = new([[]], [[]], 1, 0, 1);

    public static PeakPyramid Build(float[] interleaved, int channels)
    {
        channels = Math.Max(1, channels);
        if (interleaved.Length < channels)
        {
            return new PeakPyramid([[]], [[]], channels, 0, 1);
        }

        var frames = interleaved.Length / channels;
        var baseBucket = (int)Math.Max(1L, (frames + TargetBaseBuckets - 1) / TargetBaseBuckets);
        var baseCount = (int)((frames + baseBucket - 1) / baseBucket);
        var mins = new float[baseCount * channels];
        var maxs = new float[baseCount * channels];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        for (var frame = 0; frame < frames; frame++)
        {
            var bucket = frame / baseBucket;
            var src = frame * channels;
            var dst = bucket * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                var sample = interleaved[src + ch];
                var index = dst + ch;
                if (sample < mins[index])
                {
                    mins[index] = sample;
                }

                if (sample > maxs[index])
                {
                    maxs[index] = sample;
                }
            }
        }

        for (var i = 0; i < mins.Length; i++)
        {
            if (mins[i] > maxs[i])
            {
                mins[i] = 0;
                maxs[i] = 0;
            }
        }

        return FromBasePeaks(mins, maxs, channels, frames, baseBucket);
    }

    /// <summary>
    /// [startFrame, endFrame) を peakCount バケットへ集計する。
    /// 戻り値は実際に書いたバケット数。mins/maxs は呼び出し側バッファ。
    /// </summary>
    public int ReadRange(long startFrame, long endFrame, int peakCount, int channel, float[] mins, float[] maxs)
    {
        if ((uint)channel >= (uint)Channels || mins.Length < peakCount || maxs.Length < peakCount)
        {
            return 0;
        }

        return ReadRangeCore(startFrame, endFrame, peakCount, mins, maxs, packed: false, channel);
    }

    /// <summary>
    /// 全チャンネルを 1 走査で集計する。mins/maxs は [column * Channels + ch]。
    /// </summary>
    public int ReadRangePacked(long startFrame, long endFrame, int peakCount, float[] mins, float[] maxs)
    {
        var needed = peakCount * Math.Max(1, Channels);
        if (mins.Length < needed || maxs.Length < needed)
        {
            return 0;
        }

        return ReadRangeCore(startFrame, endFrame, peakCount, mins, maxs, packed: true, channel: 0);
    }

    private int ReadRangeCore(
        long startFrame,
        long endFrame,
        int peakCount,
        float[] mins,
        float[] maxs,
        bool packed,
        int channel)
    {
        if (IsEmpty || peakCount <= 0)
        {
            return 0;
        }

        startFrame = Math.Clamp(startFrame, 0, FrameCount);
        endFrame = Math.Clamp(endFrame, startFrame, FrameCount);
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return 0;
        }

        var channels = Channels;
        var buckets = (int)Math.Min(peakCount, rangeFrames);
        var framesPerBucket = rangeFrames / (double)buckets;
        var level = 0;
        while (level + 1 < _minLevels.Length
            && ((long)BaseBucketFrames << (level + 1)) <= framesPerBucket)
        {
            level++;
        }

        var levelBucketFrames = (long)BaseBucketFrames << level;
        var levelMins = _minLevels[level];
        var levelMaxs = _maxLevels[level];
        var levelBuckets = levelMins.Length / channels;
        if (levelBuckets <= 0)
        {
            return 0;
        }

        for (var i = 0; i < buckets; i++)
        {
            var f0 = startFrame + i * rangeFrames / buckets;
            var f1 = startFrame + (i + 1) * rangeFrames / buckets;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            var b0 = (int)Math.Clamp(f0 / levelBucketFrames, 0, levelBuckets - 1);
            var b1 = (int)Math.Clamp((f1 - 1) / levelBucketFrames, b0, levelBuckets - 1);
            if (packed)
            {
                var dest = i * channels;
                if (b0 == b1)
                {
                    var src = b0 * channels;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        mins[dest + ch] = levelMins[src + ch];
                        maxs[dest + ch] = levelMaxs[src + ch];
                    }

                    continue;
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    mins[dest + ch] = float.MaxValue;
                    maxs[dest + ch] = float.MinValue;
                }

                for (var b = b0; b <= b1; b++)
                {
                    var src = b * channels;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var min = levelMins[src + ch];
                        var max = levelMaxs[src + ch];
                        if (min < mins[dest + ch])
                        {
                            mins[dest + ch] = min;
                        }

                        if (max > maxs[dest + ch])
                        {
                            maxs[dest + ch] = max;
                        }
                    }
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    if (mins[dest + ch] > maxs[dest + ch])
                    {
                        mins[dest + ch] = 0;
                        maxs[dest + ch] = 0;
                    }
                }
            }
            else
            {
                var min = float.MaxValue;
                var max = float.MinValue;
                for (var b = b0; b <= b1; b++)
                {
                    var index = b * channels + channel;
                    if (levelMins[index] < min)
                    {
                        min = levelMins[index];
                    }

                    if (levelMaxs[index] > max)
                    {
                        max = levelMaxs[index];
                    }
                }

                if (min > max)
                {
                    min = 0;
                    max = 0;
                }

                mins[i] = min;
                maxs[i] = max;
            }
        }

        return buckets;
    }

    private static PeakPyramid FromBasePeaks(
        float[] mins,
        float[] maxs,
        int channels,
        long frameCount,
        int baseBucket)
    {
        var minLevels = new List<float[]> { mins };
        var maxLevels = new List<float[]> { maxs };
        while (minLevels[^1].Length / channels > 2048)
        {
            var prevMin = minLevels[^1];
            var prevMax = maxLevels[^1];
            var prevBuckets = prevMin.Length / channels;
            var nextBuckets = (prevBuckets + 1) / 2;
            var nextMin = new float[nextBuckets * channels];
            var nextMax = new float[nextBuckets * channels];
            for (var i = 0; i < nextBuckets; i++)
            {
                var a = i * 2;
                var b = Math.Min(a + 1, prevBuckets - 1);
                for (var ch = 0; ch < channels; ch++)
                {
                    var dst = i * channels + ch;
                    nextMin[dst] = Math.Min(prevMin[a * channels + ch], prevMin[b * channels + ch]);
                    nextMax[dst] = Math.Max(prevMax[a * channels + ch], prevMax[b * channels + ch]);
                }
            }

            minLevels.Add(nextMin);
            maxLevels.Add(nextMax);
        }

        return new PeakPyramid([.. minLevels], [.. maxLevels], channels, frameCount, baseBucket);
    }
}
