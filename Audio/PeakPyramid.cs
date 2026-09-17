namespace MgaSonicAnvil.Audio;

/// <summary>
/// 全体を一度走査して作る min/max のミップ階層。
/// 構築後は任意の表示窓を O(画面幅) で集計でき、ズームで生サンプルへ戻らない。
/// IM Importer / TimeCaster と同じ LOD 選択。
/// </summary>
internal sealed class PeakPyramid
{
    private const int EditorBaseBuckets = 1 << 21;

    /// <summary>拡大しない表示用。短いファイルは 1 サンプル粒度のまま。</summary>
    public const int DisplayBaseBuckets = 8192;

    /// <summary>プレイヤー波形用。表示用より粗いが、1024 だとピークが立ちすぎるので中間粒度。</summary>
    public const int PlayerDisplayBaseBuckets = 4096;

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

    /// <summary>表示用 LOD のままなので、編集のズームには作り直す。</summary>
    public bool NeedsEditorDetail
    {
        get
        {
            if (IsEmpty || FrameCount <= 0)
            {
                return false;
            }

            var editorBucket = (int)Math.Max(1L, (FrameCount + EditorBaseBuckets - 1) / EditorBaseBuckets);
            return BaseBucketFrames > editorBucket;
        }
    }

    public static PeakPyramid Empty { get; } = new([[]], [[]], 1, 0, 1);

    public static PeakPyramid Build(float[] interleaved, int channels) =>
        Build(interleaved, channels, interleaved.Length);

    public static PeakPyramid Build(float[] interleaved, int channels, int sampleCount) =>
        Build(interleaved, channels, sampleCount, EditorBaseBuckets);

    public static PeakPyramid BuildDisplay(float[] interleaved, int channels, int sampleCount) =>
        Build(interleaved, channels, sampleCount, DisplayBaseBuckets);

    /// <summary>プレイヤー用。モノラル・粗いバケット。各フレームのチャンネル包絡でピークを残す。</summary>
    public static PeakPyramid BuildPlayerDisplay(float[] interleaved, int channels, int sampleCount) =>
        BuildMonoEnvelope(interleaved, channels, sampleCount, PlayerDisplayBaseBuckets);

    /// <summary>
    /// プレイヤー用。再生用リングバッファを介さず、ファイルを直接シーケンシャル走査する。
    /// MediaFoundation のフルデコードは避けられないが、ポンプ待ちなしで Foobar に近い体感になる。
    /// </summary>
    public static PeakPyramid BuildPlayerDisplayFromPath(
        string path,
        Action<PeakPyramid>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var stream = AudioCodec.OpenPlaybackStream(path);
        var format = stream.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var block = Math.Max(1, format.BlockAlign);
        var frames = stream.Length > 0 ? stream.Length / block : 0;
        if (frames <= 0 && stream.TotalTime.TotalSeconds > 0 && format.SampleRate > 0)
        {
            frames = (long)Math.Round(stream.TotalTime.TotalSeconds * format.SampleRate);
        }

        if (frames <= 0)
        {
            return new PeakPyramid([[]], [[]], 1, 0, 1);
        }

        var provider = AudioCodec.AsSampleProvider(stream);
        return BuildPlayerDisplayFromProvider(provider, channels, frames, onProgress, cancellationToken);
    }

    /// <summary>
    /// プレイヤー用ストリーム走査（互換）。再生ポンプ経由なので遅い。パスがあるなら
    /// <see cref="BuildPlayerDisplayFromPath"/> を使う。
    /// </summary>
    public static PeakPyramid BuildPlayerDisplayStreaming(AudioStreamSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.SeekFrame(0);
        var channels = Math.Max(1, source.Channels);
        var frames = source.FrameCount;
        if (frames <= 0)
        {
            return new PeakPyramid([[]], [[]], 1, 0, 1);
        }

        var maxBaseBuckets = PlayerDisplayBaseBuckets;
        var baseBucket = (int)Math.Max(1L, (frames + maxBaseBuckets - 1) / maxBaseBuckets);
        var baseCount = (int)((frames + baseBucket - 1) / baseBucket);
        var mins = new float[baseCount];
        var maxs = new float[baseCount];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        const int chunkFrames = 65536;
        var chunk = new float[chunkFrames * channels];
        long frame = 0;
        while (frame < frames)
        {
            var want = (int)Math.Min(chunkFrames, frames - frame);
            var got = source.ReadFrames(chunk, 0, want, timeoutMs: 5000);
            if (got <= 0)
            {
                break;
            }

            AccumulateMonoEnvelope(chunk, channels, got, frame, baseBucket, mins, maxs);
            frame += got;
        }

        FinalizeUnsetBuckets(mins, maxs);
        return FromBasePeaks(mins, maxs, channels: 1, frames, baseBucket);
    }

    private static PeakPyramid BuildPlayerDisplayFromProvider(
        NAudio.Wave.ISampleProvider provider,
        int channels,
        long frames,
        Action<PeakPyramid>? onProgress,
        CancellationToken cancellationToken)
    {
        channels = Math.Max(1, channels);
        var maxBaseBuckets = PlayerDisplayBaseBuckets;
        var baseBucket = (int)Math.Max(1L, (frames + maxBaseBuckets - 1) / maxBaseBuckets);
        var baseCount = (int)((frames + baseBucket - 1) / baseBucket);
        var mins = new float[baseCount];
        var maxs = new float[baseCount];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        // 大きなチャンクで MF デコードの呼び出し回数を減らす。
        const int chunkFrames = 65536;
        var chunk = new float[chunkFrames * channels];
        long frame = 0;
        var lastProgressMs = Environment.TickCount64;
        while (frame < frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var wantSamples = (int)Math.Min((long)chunk.Length, (frames - frame) * channels);
            var gotSamples = provider.Read(chunk, 0, wantSamples);
            if (gotSamples <= 0)
            {
                break;
            }

            var gotFrames = gotSamples / channels;
            if (gotFrames <= 0)
            {
                break;
            }

            AccumulateMonoEnvelope(chunk, channels, gotFrames, frame, baseBucket, mins, maxs);
            frame += gotFrames;

            if (onProgress is not null)
            {
                var now = Environment.TickCount64;
                if (now - lastProgressMs >= 40 || frame >= frames)
                {
                    lastProgressMs = now;
                    onProgress(SnapshotPlayerPeaks(mins, maxs, frames, baseBucket));
                }
            }
        }

        FinalizeUnsetBuckets(mins, maxs);
        return FromBasePeaks(mins, maxs, channels: 1, frames, baseBucket);
    }

    private static void AccumulateMonoEnvelope(
        float[] chunk,
        int channels,
        int gotFrames,
        long startFrame,
        int baseBucket,
        float[] mins,
        float[] maxs)
    {
        for (var i = 0; i < gotFrames; i++)
        {
            var src = i * channels;
            float min;
            float max;
            if (channels == 1)
            {
                min = max = chunk[src];
            }
            else if (channels == 2)
            {
                var l = chunk[src];
                var r = chunk[src + 1];
                min = Math.Min(l, r);
                max = Math.Max(l, r);
            }
            else
            {
                ChannelMix.Envelope(chunk.AsSpan(src, channels), out min, out max);
            }

            var bucket = (int)((startFrame + i) / baseBucket);
            if (min < mins[bucket])
            {
                mins[bucket] = min;
            }

            if (max > maxs[bucket])
            {
                maxs[bucket] = max;
            }
        }
    }

    private static void FinalizeUnsetBuckets(float[] mins, float[] maxs)
    {
        for (var i = 0; i < mins.Length; i++)
        {
            if (mins[i] > maxs[i])
            {
                mins[i] = 0;
                maxs[i] = 0;
            }
        }
    }

    private static PeakPyramid SnapshotPlayerPeaks(
        float[] mins,
        float[] maxs,
        long frames,
        int baseBucket)
    {
        var snapMin = (float[])mins.Clone();
        var snapMax = (float[])maxs.Clone();
        FinalizeUnsetBuckets(snapMin, snapMax);
        return FromBasePeaks(snapMin, snapMax, channels: 1, frames, baseBucket);
    }

    private static PeakPyramid BuildMonoEnvelope(
        float[] interleaved,
        int channels,
        int sampleCount,
        int maxBaseBuckets)
    {
        channels = Math.Max(1, channels);
        sampleCount = Math.Clamp(sampleCount, 0, interleaved.Length);
        if (sampleCount < channels)
        {
            return new PeakPyramid([[]], [[]], 1, 0, 1);
        }

        var frames = sampleCount / channels;
        maxBaseBuckets = Math.Max(1, maxBaseBuckets);
        var baseBucket = (int)Math.Max(1L, (frames + maxBaseBuckets - 1) / maxBaseBuckets);
        var baseCount = (int)((frames + baseBucket - 1) / baseBucket);
        var mins = new float[baseCount];
        var maxs = new float[baseCount];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        for (var frame = 0; frame < frames; frame++)
        {
            var src = frame * channels;
            ChannelMix.Envelope(interleaved.AsSpan(src, channels), out var min, out var max);
            var bucket = frame / baseBucket;
            if (min < mins[bucket])
            {
                mins[bucket] = min;
            }

            if (max > maxs[bucket])
            {
                maxs[bucket] = max;
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

        return FromBasePeaks(mins, maxs, channels: 1, frames, baseBucket);
    }

    public static PeakPyramid Build(float[] interleaved, int channels, int sampleCount, int maxBaseBuckets)
    {
        channels = Math.Max(1, channels);
        sampleCount = Math.Clamp(sampleCount, 0, interleaved.Length);
        if (sampleCount < channels)
        {
            return new PeakPyramid([[]], [[]], channels, 0, 1);
        }

        var frames = sampleCount / channels;
        maxBaseBuckets = Math.Max(1, maxBaseBuckets);
        var baseBucket = (int)Math.Max(1L, (frames + maxBaseBuckets - 1) / maxBaseBuckets);
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
