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
    /// ポスト＝[E, E＋Post): 元波形（E 以降）×in←0 ＋ ループスタート×out→0。
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
            && frame >= EndFrame
            && frame < EndFrame + PostCrossfadeFrames)
        {
            var x = PostCrossfadeFrames;
            var i = frame - EndFrame;
            // 元波形＝フェードイン、ループスタート上乗せ＝フェードアウト
            outSideFrame = StartFrame + i;
            inSideFrame = frame;
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

    /// <summary>ポスト: E 以降 × in ＋ ループスタート × out を [E, E＋X) へ焼く。</summary>
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
            var destFrame = endFrame + i;
            var destOff = (int)destFrame * channels;
            var srcBase = destOff;
            var srcOverlay = (int)(startFrame + i) * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                dest[destOff + ch] =
                    source[srcBase + ch] * inGain + source[srcOverlay + ch] * outGain;
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

        // ポストは [E, E＋Post) へ焼くため E|S 継ぎ目プレビュー（左＝E 手前／右＝S 直後）には出さない。
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

    /// <summary>ゼロクロス探索の既定半径。低域でも 1 周期内に届くよう長め。</summary>
    public const int DefaultZeroSnapRadius = 8192;

    /// <param name="alignToTwentyEight">true なら 28 の倍数の位置だけを候補にする。</param>
    public static long SnapToZeroCrossing(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame,
        int searchRadius = DefaultZeroSnapRadius,
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
            // 符号変化が無い位置も振幅で候補にする（逆相ステレオの平均0誤判定を避けるため）。
            var sample = SampleFramePeakAbs(interleaved, channels, frameCount, f);
            var dist = Math.Abs(f - frame);
            // 全chのピークがゼロに近いほど優先。同点なら現在位置に近い方。
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

    /// <summary>
    /// 半開区間の終点 E をゼロクロスへ寄せる。
    /// 継ぎ目に描画・再生されるのは E−1 なので、そちらを吸着対象にし E = seam+1 を返す。
    /// 28 倍数モードでは E を格子上に保ちつつ、E−1 の振幅／符号変化で採点する。
    /// </summary>
    public static long SnapEndToZeroCrossing(
        float[] interleaved,
        int channels,
        long frameCount,
        long endFrame,
        int searchRadius = DefaultZeroSnapRadius,
        bool alignToTwentyEight = false)
    {
        if (channels < 1 || frameCount <= 2)
        {
            return Math.Clamp(endFrame, 0, Math.Max(0, frameCount));
        }

        endFrame = Math.Clamp(endFrame, 0, frameCount);
        if (!alignToTwentyEight)
        {
            var seamHint = Math.Max(0, endFrame - 1);
            var seam = SnapToZeroCrossing(
                interleaved,
                channels,
                frameCount,
                seamHint,
                searchRadius,
                alignToTwentyEight: false);
            return Math.Clamp(seam + 1, 1, frameCount);
        }

        var step = CrossfadeEditState.LoopEndAlignStep;
        searchRadius = Math.Max(1, searchRadius);
        searchRadius = Math.Max(searchRadius, (int)(step * 24));

        var from = Math.Max(step, endFrame - searchRadius);
        var to = Math.Min(frameCount, endFrame + searchRadius);
        from = ((from + step - 1) / step) * step;

        long best = Math.Clamp(
            CrossfadeEditState.RoundToAlignStep(endFrame, step),
            0,
            frameCount);
        var bestScore = double.MaxValue;
        var found = false;
        for (var e = from; e <= to; e += step)
        {
            if (e < 1 || e > frameCount)
            {
                continue;
            }

            var seam = e - 1;
            var crossing = IsZeroCrossingAt(interleaved, channels, frameCount, seam)
                || IsExactZeroAt(interleaved, channels, frameCount, seam);
            var sample = SampleFramePeakAbs(interleaved, channels, frameCount, seam);
            var dist = Math.Abs(e - endFrame);
            var score = sample * 1_000_000d + dist;
            if (!crossing)
            {
                score += 1_000d;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = e;
                found = true;
            }
        }

        return found ? best : best;
    }

    /// <summary>描画と同じく、全チャンネルのうち一番大きい |振幅|。</summary>
    private static float SampleFramePeakAbs(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame)
    {
        if (frame < 0 || frame >= frameCount || channels < 1)
        {
            return 0f;
        }

        var offset = (int)frame * channels;
        var peak = 0f;
        for (var ch = 0; ch < channels; ch++)
        {
            var a = Math.Abs(interleaved[offset + ch]);
            if (a > peak)
            {
                peak = a;
            }
        }

        return peak;
    }

    /// <summary>全チャンネルが厳密に 0。平均では逆相をゼロと誤る。</summary>
    private static bool IsExactZeroAt(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame)
    {
        if (frame < 0 || frame >= frameCount || channels < 1)
        {
            return false;
        }

        var offset = (int)frame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            if (interleaved[offset + ch] != 0f)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>いずれかのチャンネルで符号変化（またはその ch が 0）。</summary>
    private static bool IsZeroCrossingAt(
        float[] interleaved,
        int channels,
        long frameCount,
        long frame)
    {
        if (frame <= 0 || frame >= frameCount || channels < 1)
        {
            return false;
        }

        var curOff = (int)frame * channels;
        var prevOff = curOff - channels;
        for (var ch = 0; ch < channels; ch++)
        {
            var cur = interleaved[curOff + ch];
            if (cur == 0f)
            {
                return true;
            }

            var prev = interleaved[prevOff + ch];
            if (prev == 0f)
            {
                continue;
            }

            if (Math.Sign(prev) != Math.Sign(cur))
            {
                return true;
            }
        }

        return false;
    }
}
