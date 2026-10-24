using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>焼き込みなし再生プレビュー用のプリ／ポストクロスフェード。</summary>
internal readonly struct LoopFadePreview
{
    public long StartFrame { get; init; }
    public long EndFrame { get; init; }
    public long PreCrossfadeFrames { get; init; }
    public long PostCrossfadeFrames { get; init; }
    public FadeShape PreOutShape { get; init; }
    public FadeShape PreInShape { get; init; }
    public FadeShape PostOutShape { get; init; }
    public FadeShape PostInShape { get; init; }

    public static LoopFadePreview From(CrossfadeEditState state) => new()
    {
        StartFrame = state.StartFrame,
        EndFrame = state.EndFrame,
        PreCrossfadeFrames = state.PreCrossfadeFrames,
        PostCrossfadeFrames = state.PostCrossfadeFrames,
        PreOutShape = state.PreOutShape,
        PreInShape = state.PreInShape,
        PostOutShape = state.PostOutShape,
        PostInShape = state.PostInShape,
    };

    /// <summary>
    /// プリ＝[E−Pre, E): 元波形（エンド）×out→0 ＋ S 手前×in。
    /// ポスト＝[S, S＋Post): E 以降×out ＋ 元波形（スタート）×in←0。
    /// 折り返しは常に S。
    /// </summary>
    public bool TryCrossfadeMix(
        long frame,
        out long outSideFrame,
        out long inSideFrame,
        out float outGain,
        out float inGain)
    {
        outSideFrame = 0;
        inSideFrame = 0;
        outGain = 0;
        inGain = 0;
        if (EndFrame <= StartFrame)
        {
            return false;
        }

        if (PostCrossfadeFrames > 0
            && frame >= StartFrame
            && frame < StartFrame + PostCrossfadeFrames)
        {
            var x = PostCrossfadeFrames;
            var i = frame - StartFrame;
            // スタート側を in、ループ直後（E 以降）を out
            inSideFrame = frame;
            outSideFrame = EndFrame + i;
            FadeCurves.CrossfadeGainsAtFrame(PostOutShape, PostInShape, i, 0, x, out outGain, out inGain);
            return true;
        }

        if (PreCrossfadeFrames > 0
            && frame >= EndFrame - PreCrossfadeFrames
            && frame < EndFrame)
        {
            var x = PreCrossfadeFrames;
            var i = frame - (EndFrame - x);
            // エンド側を out、ループ直前（S 手前）を in
            outSideFrame = frame;
            inSideFrame = StartFrame - x + i;
            FadeCurves.CrossfadeGainsAtFrame(PreOutShape, PreInShape, i, 0, x, out outGain, out inGain);
            return true;
        }

        return false;
    }
}

/// <summary>ループプリ／ポストクロスフェードのサンプル合成とゼロクロス吸着。</summary>
internal static class LoopCrossfade
{
    /// <summary>プリ: エンド手前 × out + S 手前 × in を [E−X, E) へ焼く。</summary>
    public static void BakePreCrossfade(
        float[] source,
        float[] dest,
        int channels,
        long frameCount,
        long startFrame,
        long endFrame,
        long crossfadeFrames,
        FadeShape outShape,
        FadeShape inShape)
    {
        if (!CanBakePre(channels, frameCount, startFrame, endFrame, crossfadeFrames))
        {
            return;
        }

        var x = (int)crossfadeFrames;
        var denom = Math.Max(1, x - 1);
        for (var i = 0; i < x; i++)
        {
            FadeCurves.CrossfadeGains(outShape, inShape, i / (double)denom, out var outGain, out var inGain);
            var destFrame = endFrame - crossfadeFrames + i;
            var destOff = (int)destFrame * channels;
            var srcOut = (int)destFrame * channels;
            var srcIn = (int)(startFrame - crossfadeFrames + i) * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                dest[destOff + ch] =
                    source[srcOut + ch] * outGain + source[srcIn + ch] * inGain;
            }
        }
    }

    /// <summary>ポスト: E 以降 × out + スタート先頭 × in を [S, S＋X) へ焼く。</summary>
    public static void BakePostCrossfade(
        float[] source,
        float[] dest,
        int channels,
        long frameCount,
        long startFrame,
        long endFrame,
        long crossfadeFrames,
        FadeShape outShape,
        FadeShape inShape)
    {
        if (!CanBakePost(channels, frameCount, startFrame, endFrame, crossfadeFrames))
        {
            return;
        }

        var x = (int)crossfadeFrames;
        var denom = Math.Max(1, x - 1);
        for (var i = 0; i < x; i++)
        {
            FadeCurves.CrossfadeGains(outShape, inShape, i / (double)denom, out var outGain, out var inGain);
            var destFrame = startFrame + i;
            var destOff = (int)destFrame * channels;
            var srcOut = (int)(endFrame + i) * channels;
            var srcIn = (int)destFrame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                dest[destOff + ch] =
                    source[srcOut + ch] * outGain + source[srcIn + ch] * inGain;
            }
        }
    }

    private static bool CanBakePre(
        int channels,
        long frameCount,
        long startFrame,
        long endFrame,
        long crossfadeFrames) =>
        channels >= 1
        && crossfadeFrames > 0
        && startFrame >= crossfadeFrames
        && endFrame <= frameCount
        && startFrame < endFrame
        && crossfadeFrames <= endFrame - startFrame;

    private static bool CanBakePost(
        int channels,
        long frameCount,
        long startFrame,
        long endFrame,
        long crossfadeFrames) =>
        channels >= 1
        && crossfadeFrames > 0
        && startFrame >= 0
        && endFrame + crossfadeFrames <= frameCount
        && startFrame < endFrame
        && crossfadeFrames <= endFrame - startFrame;

    public static void BuildSplicePreview(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        int halfFrames,
        out float[] left,
        out float[] right)
    {
        BuildSplicePreviewChannels(
            interleaved,
            channels,
            frameCount,
            state,
            halfFrames,
            panLeftFrames: 0,
            panRightFrames: 0,
            out var leftCh,
            out var rightCh);
        left = MixChannels(leftCh);
        right = MixChannels(rightCh);
    }

    /// <param name="panLeftFrames">左（エンド側）。正＝より手前を表示。</param>
    /// <param name="panRightFrames">右（スタート側）。正＝より後ろを表示。</param>
    /// <param name="leftByChannel">[ch][i] エンド側。</param>
    /// <param name="rightByChannel">[ch][i] スタート側。</param>
    public static void BuildSplicePreviewChannels(
        float[] interleaved,
        int channels,
        long frameCount,
        CrossfadeEditState state,
        int halfFrames,
        long panLeftFrames,
        long panRightFrames,
        out float[][] leftByChannel,
        out float[][] rightByChannel)
    {
        halfFrames = Math.Max(1, halfFrames);
        channels = Math.Max(1, channels);
        leftByChannel = AllocChannelBuffers(channels, halfFrames);
        rightByChannel = AllocChannelBuffers(channels, halfFrames);
        if (frameCount <= 0 || state.LoopLength <= 0)
        {
            return;
        }

        // 継ぎ目は中央固定。左右は独立に前後へずらす。チャンネルは分けたまま返す。
        for (var i = 0; i < halfFrames; i++)
        {
            SampleFrameChannels(
                interleaved,
                channels,
                frameCount,
                state.EndFrame - halfFrames + i - panLeftFrames,
                leftByChannel,
                i);
            SampleFrameChannels(
                interleaved,
                channels,
                frameCount,
                state.StartFrame + i + panRightFrames,
                rightByChannel,
                i);
        }

        if (!state.ShowXfadePreview)
        {
            return;
        }

        var endBuf = new float[channels];
        var startBuf = new float[channels];

        // 左＝エンド側にプリ（[E−Pre, E) ＝ エンド out ＋ S 手前 in）
        var pre = (int)Math.Min(state.PreCrossfadeFrames, halfFrames);
        if (pre > 0 && state.StartFrame >= pre)
        {
            var denom = Math.Max(1, pre - 1);
            for (var i = 0; i < pre; i++)
            {
                var frame = state.EndFrame - pre + i;
                var destIndex = (int)(frame - (state.EndFrame - halfFrames - panLeftFrames));
                if (destIndex < 0 || destIndex >= halfFrames)
                {
                    continue;
                }

                FadeCurves.CrossfadeGains(
                    state.PreOutShape,
                    state.PreInShape,
                    i / (double)denom,
                    out var outGain,
                    out var inGain);
                ReadFrameChannels(interleaved, channels, frameCount, frame, endBuf);
                ReadFrameChannels(interleaved, channels, frameCount, state.StartFrame - pre + i, startBuf);
                for (var ch = 0; ch < channels; ch++)
                {
                    leftByChannel[ch][destIndex] = endBuf[ch] * outGain + startBuf[ch] * inGain;
                }
            }
        }

        // 右＝スタート側にポスト（[S, S＋Post) ＝ E 以降 out ＋ スタート in）
        var post = (int)Math.Min(state.PostCrossfadeFrames, halfFrames);
        if (post > 0 && state.EndFrame + post <= frameCount)
        {
            var denom = Math.Max(1, post - 1);
            for (var i = 0; i < post; i++)
            {
                var frame = state.StartFrame + i;
                var destIndex = (int)(frame - (state.StartFrame + panRightFrames));
                if (destIndex < 0 || destIndex >= halfFrames)
                {
                    continue;
                }

                FadeCurves.CrossfadeGains(
                    state.PostOutShape,
                    state.PostInShape,
                    i / (double)denom,
                    out var outGain,
                    out var inGain);
                ReadFrameChannels(interleaved, channels, frameCount, state.EndFrame + i, endBuf);
                ReadFrameChannels(interleaved, channels, frameCount, frame, startBuf);
                for (var ch = 0; ch < channels; ch++)
                {
                    rightByChannel[ch][destIndex] = endBuf[ch] * outGain + startBuf[ch] * inGain;
                }
            }
        }
    }

    private static float[][] AllocChannelBuffers(int channels, int halfFrames)
    {
        var buffers = new float[channels][];
        for (var ch = 0; ch < channels; ch++)
        {
            buffers[ch] = new float[halfFrames];
        }

        return buffers;
    }

    private static float[] MixChannels(float[][] byChannel)
    {
        if (byChannel.Length == 0)
        {
            return [];
        }

        var len = byChannel[0].Length;
        var mixed = new float[len];
        for (var i = 0; i < len; i++)
        {
            double sum = 0;
            for (var ch = 0; ch < byChannel.Length; ch++)
            {
                sum += byChannel[ch][i];
            }

            mixed[i] = (float)(sum / byChannel.Length);
        }

        return mixed;
    }

    private static void SampleFrameChannels(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame,
        float[][] destByChannel,
        int index)
    {
        if (frame < 0 || frame >= frameCount)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                destByChannel[ch][index] = 0f;
            }

            return;
        }

        var offset = (int)frame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            destByChannel[ch][index] = interleaved[offset + ch];
        }
    }

    private static void ReadFrameChannels(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame,
        float[] dest)
    {
        if (frame < 0 || frame >= frameCount)
        {
            Array.Clear(dest);
            return;
        }

        var offset = (int)frame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            dest[ch] = interleaved[offset + ch];
        }
    }

    /// <summary>継ぎ目プレビュー用。常に全チャンネルの平均。</summary>
    private static float SampleFrameMixed(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame)
    {
        if (frame < 0 || frame >= frameCount)
        {
            return 0f;
        }

        var offset = (int)frame * channels;
        double sum = 0;
        for (var ch = 0; ch < channels; ch++)
        {
            sum += interleaved[offset + ch];
        }

        return (float)(sum / channels);
    }

    /// <param name="alignToTwentyEight">true なら 28 の倍数の位置だけを候補にする。</param>
    public static long SnapToZeroCrossing(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame,
        int searchRadius = 512,
        bool alignToTwentyEight = false)
    {
        if (channels < 1 || frameCount <= 2)
        {
            return Math.Clamp(frame, 0, Math.Max(0, frameCount));
        }

        frame = Math.Clamp(frame, 0, frameCount);
        searchRadius = Math.Max(1, searchRadius);
        var step = alignToTwentyEight ? CrossfadeEditState.LoopEndAlignStep : 1L;
        // 28 格子は候補が疎なので探索半径を広げる
        if (alignToTwentyEight)
        {
            searchRadius = Math.Max(searchRadius, (int)(step * 24));
        }

        var from = Math.Max(0, frame - searchRadius);
        var to = Math.Min(frameCount - 1, frame + searchRadius);
        if (alignToTwentyEight)
        {
            from = ((from + step - 1) / step) * step; // 区間内の最初の倍数
        }

        long best = alignToTwentyEight
            ? Math.Clamp(CrossfadeEditState.RoundToAlignStep(frame, step), 0, Math.Max(0, frameCount - 1))
            : frame;
        var bestScore = double.MaxValue;
        var found = false;

        for (var f = from; f <= to; f += step)
        {
            var crossing = IsZeroCrossingAt(interleaved, channels, frameCount, f)
                || IsExactZeroAt(interleaved, channels, frameCount, f);
            if (!crossing && !alignToTwentyEight)
            {
                continue;
            }

            var sample = Math.Abs(SampleFrameMixed(interleaved, channels, frameCount, f));
            var dist = Math.Abs(f - frame);
            // ゼロに近いほど優先。同点なら現在位置に近い方。
            var score = sample * 1_000_000d + dist;
            if (!crossing)
            {
                score += 1_000d; // 符号変化なしは劣後
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = f;
                found = true;
            }
        }

        return found ? best : best;
    }

    private static bool IsExactZeroAt(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame) =>
        frame >= 0
        && frame < frameCount
        && SampleFrameMixed(interleaved, channels, frameCount, frame) == 0f;

    private static bool IsZeroCrossingAt(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame)
    {
        if (frame <= 0 || frame >= frameCount)
        {
            return false;
        }

        var prev = SampleFrameMixed(interleaved, channels, frameCount, frame - 1);
        var cur = SampleFrameMixed(interleaved, channels, frameCount, frame);
        if (cur == 0f)
        {
            return true;
        }

        if (prev == 0f)
        {
            return false;
        }

        return Math.Sign(prev) != Math.Sign(cur);
    }
}
