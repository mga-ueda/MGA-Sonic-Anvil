namespace MgaSonicAnvil.Editing;

internal enum CrossfadeAutoSearchKind
{
    StartAndEnd,
    StartOnly,
    EndOnly,
}

/// <summary>ループ継ぎ目の自動探索（相互相関／二乗誤差）。</summary>
internal static class LoopAutoSearch
{
    private const int CompareFrames = 512;
    private const int DefaultSearchRadius = 4096;
    private const int MaxPasses = 256;

    /// <summary>
    /// 長さを保ったまま S/E を平行移動し、継ぎ目誤差が最小の位置へ。
    /// 窓端に当たったら中心をずらして再探索し、利用可能範囲の端まで進める。
    /// </summary>
    public static void AdjustStartAndEnd(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        int searchRadius = DefaultSearchRadius)
    {
        if (!TryPrepare(interleaved, channels, frameCount, state, out var loopLen))
        {
            return;
        }

        searchRadius = Math.Max(1, searchRadius);
        var seam = SeamLength(state);
        var hardMin = Math.Max(seam, 0L);
        var hardMax = Math.Max(hardMin, frameCount - loopLen);
        if (hardMax < hardMin)
        {
            return;
        }

        var current = Math.Clamp(state.StartFrame, hardMin, hardMax);
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var minStart = Math.Max(hardMin, current - searchRadius);
            var maxStart = Math.Min(hardMax, current + searchRadius);
            if (maxStart < minStart)
            {
                break;
            }

            FindBestStart(
                interleaved,
                channels,
                frameCount,
                minStart,
                maxStart,
                loopLen,
                seam,
                out var bestStart,
                out var bestScore);

            if (bestScore >= double.MaxValue / 2d)
            {
                break;
            }

            var moved = bestStart != current;
            current = bestStart;
            // 窓の内側で最小 → 局所解。端なら次パスでさらに外側へ。
            if (!moved || (bestStart > minStart && bestStart < maxStart))
            {
                break;
            }

            if (bestStart <= hardMin || bestStart >= hardMax)
            {
                break;
            }
        }

        state.StartFrame = current;
        state.EndFrame = current + loopLen;
        state.Clamp(frameCount);
    }

    /// <summary>E 固定で S だけ探索。窓端に当たったら再探索して利用範囲の端まで進める。</summary>
    public static void AdjustStart(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        int searchRadius = DefaultSearchRadius)
    {
        if (!TryPrepare(interleaved, channels, frameCount, state, out _))
        {
            return;
        }

        searchRadius = Math.Max(1, searchRadius);
        var seam = SeamLength(state);
        var hardMin = Math.Max(seam, 0L);
        // ループ長 ≥ 2×seam（終点探索と同じ制約の対称）
        var hardMax = Math.Min(state.EndFrame - 1, state.EndFrame - Math.Max(1L, seam * 2));
        if (hardMax < hardMin)
        {
            return;
        }

        var current = Math.Clamp(state.StartFrame, hardMin, hardMax);
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var minStart = Math.Max(hardMin, current - searchRadius);
            var maxStart = Math.Min(hardMax, current + searchRadius);
            if (maxStart < minStart)
            {
                break;
            }

            FindBestStartFixedEnd(
                interleaved,
                channels,
                frameCount,
                minStart,
                maxStart,
                state.EndFrame,
                seam,
                out var bestStart,
                out var bestScore);

            if (bestScore >= double.MaxValue / 2d)
            {
                break;
            }

            var moved = bestStart != current;
            current = bestStart;
            if (!moved || (bestStart > minStart && bestStart < maxStart))
            {
                break;
            }

            if (bestStart <= hardMin || bestStart >= hardMax)
            {
                break;
            }
        }

        state.StartFrame = current;
        state.Clamp(frameCount);
    }

    /// <summary>S 固定で E だけ探索。窓端に当たったら再探索して利用範囲の端まで進める。</summary>
    public static void AdjustEnd(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        int searchRadius = DefaultSearchRadius)
    {
        if (!TryPrepare(interleaved, channels, frameCount, state, out _))
        {
            return;
        }

        searchRadius = Math.Max(1, searchRadius);
        var seam = SeamLength(state);
        var hardMin = Math.Max(state.StartFrame + 1, state.StartFrame + Math.Max(1L, seam * 2));
        var hardMax = frameCount;
        if (hardMax < hardMin)
        {
            return;
        }

        var current = Math.Clamp(state.EndFrame, hardMin, hardMax);
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var minEnd = Math.Max(hardMin, current - searchRadius);
            var maxEnd = Math.Min(hardMax, current + searchRadius);
            if (maxEnd < minEnd)
            {
                break;
            }

            FindBestEnd(
                interleaved,
                channels,
                frameCount,
                state.StartFrame,
                minEnd,
                maxEnd,
                seam,
                out var bestEnd,
                out var bestScore);

            if (bestScore >= double.MaxValue / 2d)
            {
                break;
            }

            var moved = bestEnd != current;
            current = bestEnd;
            if (!moved || (bestEnd > minEnd && bestEnd < maxEnd))
            {
                break;
            }

            if (bestEnd <= hardMin || bestEnd >= hardMax)
            {
                break;
            }
        }

        state.EndFrame = current;
        state.Clamp(frameCount);
    }

    private static void FindBestStart(
        float[] interleaved,
        int channels,
        long frameCount,
        long minStart,
        long maxStart,
        long loopLen,
        long seam,
        out long bestStart,
        out double bestScore)
    {
        bestStart = minStart;
        bestScore = double.MaxValue;
        for (var start = minStart; start <= maxStart; start++)
        {
            var end = start + loopLen;
            var score = SeamError(interleaved, channels, frameCount, start, end, seam);
            if (score < bestScore)
            {
                bestScore = score;
                bestStart = start;
            }
        }
    }

    private static void FindBestStartFixedEnd(
        float[] interleaved,
        int channels,
        long frameCount,
        long minStart,
        long maxStart,
        long end,
        long seam,
        out long bestStart,
        out double bestScore)
    {
        bestStart = minStart;
        bestScore = double.MaxValue;
        for (var start = minStart; start <= maxStart; start++)
        {
            var score = SeamError(interleaved, channels, frameCount, start, end, seam);
            if (score < bestScore)
            {
                bestScore = score;
                bestStart = start;
            }
        }
    }

    private static void FindBestEnd(
        float[] interleaved,
        int channels,
        long frameCount,
        long start,
        long minEnd,
        long maxEnd,
        long seam,
        out long bestEnd,
        out double bestScore)
    {
        bestEnd = minEnd;
        bestScore = double.MaxValue;
        for (var end = minEnd; end <= maxEnd; end++)
        {
            var score = SeamError(interleaved, channels, frameCount, start, end, seam);
            if (score < bestScore)
            {
                bestScore = score;
                bestEnd = end;
            }
        }
    }

    private static long SeamLength(CrossfadeEditState state) =>
        Math.Max(state.PreCrossfadeFrames, state.PostCrossfadeFrames);

    private static bool TryPrepare(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        out long loopLen)
    {
        loopLen = state.LoopLength;
        return channels >= 1
            && frameCount > 0
            && interleaved.Length >= frameCount * channels
            && loopLen > 1
            && state.StartFrame >= 0
            && state.EndFrame <= frameCount;
    }

    /// <summary>継ぎ目まわりの二乗誤差（小さいほど良い）。</summary>
    internal static double SeamError(
        float[] interleaved,
        int channels,
        long frameCount,
        long start,
        long end,
        long crossfadeFrames)
    {
        var compare = (int)Math.Min(CompareFrames, Math.Min(start, end - start));
        if (compare < 2)
        {
            return double.MaxValue;
        }

        if (crossfadeFrames > 0)
        {
            compare = (int)Math.Min(compare, crossfadeFrames);
        }

        double error = 0;
        for (var i = 0; i < compare; i++)
        {
            var aFrame = end - compare + i;
            var bFrame = start - compare + i;
            if (bFrame < 0 || aFrame < 0 || aFrame >= frameCount)
            {
                return double.MaxValue;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                var a = interleaved[(int)aFrame * channels + ch];
                var b = interleaved[(int)bFrame * channels + ch];
                var d = a - b;
                error += d * d;
            }
        }

        return error;
    }
}
