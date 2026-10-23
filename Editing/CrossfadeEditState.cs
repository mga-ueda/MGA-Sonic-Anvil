using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

/// <summary>
/// フェード調整ツールの非破壊ドラフト。
/// 旗・長さバーの位置と、聴こえる重ね／焼き込み位置は分離する。
/// 範囲塗りは旗側のみ。重ね側は波形の色付けだけで塗らない。
/// プリ: 旗・塗り＝S 手前 / 波形重ね・焼き込み＝E 手前。
/// ポスト: 旗・塗り＝E 後ろ / 波形重ね・焼き込み＝S 先頭。
/// </summary>
internal sealed class CrossfadeEditState
{
    public bool IsActive { get; set; }

    /// <summary>ループ始点 S（半開区間の開始）。</summary>
    public long StartFrame { get; set; }

    /// <summary>ループ終点 E（半開区間の終了）。</summary>
    public long EndFrame { get; set; }

    /// <summary>プリクロスフェード長。</summary>
    public long PreCrossfadeFrames { get; set; }

    /// <summary>ポストクロスフェード長。</summary>
    public long PostCrossfadeFrames { get; set; }

    /// <summary>Loop Mix 時に [0,S) と [E, end) を切り落す。</summary>
    public bool TruncateOutside { get; set; }

    /// <summary>オンのとき始点／終点を 28 サンプル格子に揃える。</summary>
    public bool AlignLoopEndsToTwentyEight { get; set; }

    public const long LoopEndAlignStep = 28;

    /// <summary>継ぎ目のクロスフェード合成プレビュー。記憶せず、起動時は常にオフ。</summary>
    public bool ShowXfadePreview { get; set; }

    public bool LoopPlayback { get; set; } = true;

    /// <summary>プリ側アウト脚（減衰側）。既定はアウトメニュー先頭。</summary>
    public FadeShape PreOutShape { get; set; } = FadeCurves.MenuOrderFadeOut[0];

    /// <summary>プリ側イン脚（立ち上がり側）。既定はインメニュー先頭。</summary>
    public FadeShape PreInShape { get; set; } = FadeCurves.MenuOrderFadeIn[0];

    /// <summary>ポスト側アウト脚（減衰側）。既定はアウトメニュー先頭。</summary>
    public FadeShape PostOutShape { get; set; } = FadeCurves.MenuOrderFadeOut[0];

    /// <summary>ポスト側イン脚（立ち上がり側）。既定はインメニュー先頭。</summary>
    public FadeShape PostInShape { get; set; } = FadeCurves.MenuOrderFadeIn[0];

    public enum FocusField
    {
        Start,
        End,
        PreCrossfade,
        PostCrossfade,
    }

    public FocusField Focus { get; set; } = FocusField.PreCrossfade;

    public long LoopLength => Math.Max(0, EndFrame - StartFrame);

    /// <summary>緑旗の位置（S − Pre）。長さ指定用。重ね描画位置とは別。</summary>
    public long PreFlagFrame => StartFrame - PreCrossfadeFrames;

    /// <summary>プリの重ね描画・焼き込み開始（E − Pre）。</summary>
    public long PreDrawStartFrame => EndFrame - PreCrossfadeFrames;

    /// <summary>赤旗の位置（E ＋ Post）。長さ指定はエンド後ろへ伸ばす。</summary>
    public long PostFlagFrame => EndFrame + PostCrossfadeFrames;

    /// <summary>ポストの焼き込み・再生ミックス開始（＝S）。旗とは別。</summary>
    public long PostDrawStartFrame => StartFrame;

    public WaveSelection LoopRange => new(StartFrame, EndFrame);

    /// <summary>プリ長さバー／塗り [S−Pre, S)。旗と同じ側。</summary>
    public WaveSelection PreBarRange => new(PreFlagFrame, StartFrame);

    /// <summary>プリ波形重ね・焼き込み区間 [E−Pre, E)。塗らない。</summary>
    public WaveSelection PreDrawRange => new(PreDrawStartFrame, EndFrame);

    /// <summary>ポスト長さバー／塗り [E, E＋Post)。旗と同じ側。</summary>
    public WaveSelection PostBarRange => new(EndFrame, PostFlagFrame);

    /// <summary>ポスト波形重ね・焼き込み区間 [S, S＋Post)。塗らない。</summary>
    public WaveSelection PostDrawRange => new(StartFrame, StartFrame + PostCrossfadeFrames);

    public static CrossfadeEditState CreateInitial(AudioDocument document)
    {
        var state = new CrossfadeEditState { IsActive = true };
        var frames = document.FrameCount;
        if (frames <= 1)
        {
            return state;
        }

        WaveSelection seed;
        if (!document.Selection.IsEmpty)
        {
            seed = document.Selection.Clamp(frames);
        }
        else if (!document.SampleLoop.IsEmpty)
        {
            seed = document.SampleLoop.Clamp(frames);
        }
        else
        {
            var start = frames / 10;
            var end = frames - frames / 10;
            if (end <= start)
            {
                start = 0;
                end = frames;
            }

            seed = new WaveSelection(start, end);
        }

        state.StartFrame = seed.StartFrame;
        state.EndFrame = seed.EndFrame;
        // 既定: プリ＝0、ポスト＝0。
        state.PreCrossfadeFrames = 0;
        state.PostCrossfadeFrames = 0;
        // 曲線4件とも各メニューの先頭タイプ
        state.PreOutShape = FadeCurves.MenuOrderFadeOut[0];
        state.PreInShape = FadeCurves.MenuOrderFadeIn[0];
        state.PostOutShape = FadeCurves.MenuOrderFadeOut[0];
        state.PostInShape = FadeCurves.MenuOrderFadeIn[0];
        state.Clamp(frames);
        return state;
    }

    /// <summary>プリ＋ポストがループ長いっぱい（合計100%）か。</summary>
    public bool IsFadeSumFull
    {
        get
        {
            var loop = LoopLength;
            return loop > 0 && PreCrossfadeFrames + PostCrossfadeFrames >= loop;
        }
    }

    /// <summary>
    /// 合計100%連動を掛けるか。片方が 0 のときはオフ（0 側を自動で増やさない）。
    /// 0 側を再度増やして両方とも正になり合計が満杯なら、次の操作から連動が戻る。
    /// </summary>
    public bool ShouldMaintainFadeComplement =>
        IsFadeSumFull && PreCrossfadeFrames > 0 && PostCrossfadeFrames > 0;

    public void Clamp(long frameCount) => Clamp(frameCount, maintainFull: false);

    /// <param name="maintainFull">
    /// true のとき（変更前が合計100%かつ双方が正）、フォーカス側に合わせて相手を増減し合計を維持する。
    /// false のときは超過分だけ相手を減らす（相手は増やさない）。
    /// </param>
    public void Clamp(long frameCount, bool maintainFull)
    {
        if (frameCount <= 0)
        {
            StartFrame = 0;
            EndFrame = 0;
            PreCrossfadeFrames = 0;
            PostCrossfadeFrames = 0;
            return;
        }

        StartFrame = Math.Clamp(StartFrame, 0, frameCount);
        EndFrame = Math.Clamp(EndFrame, 0, frameCount);
        if (AlignLoopEndsToTwentyEight)
        {
            StartFrame = RoundToAlignStep(StartFrame);
            EndFrame = RoundToAlignStep(EndFrame);
            StartFrame = Math.Clamp(StartFrame, 0, frameCount);
            EndFrame = Math.Clamp(EndFrame, 0, frameCount);
        }

        if (EndFrame <= StartFrame)
        {
            if (StartFrame >= frameCount)
            {
                StartFrame = Math.Max(0, frameCount - 1);
            }

            var minLen = AlignLoopEndsToTwentyEight ? LoopEndAlignStep : 1;
            EndFrame = Math.Min(frameCount, StartFrame + minLen);
            if (AlignLoopEndsToTwentyEight)
            {
                EndFrame = RoundToAlignStep(EndFrame);
                if (EndFrame <= StartFrame)
                {
                    EndFrame = Math.Min(frameCount, StartFrame + LoopEndAlignStep);
                }
            }
        }

        var loop = Math.Max(0, LoopLength);
        // ファイル余白に加え、プリ＋ポスト≦ループ長。フォーカス側を優先。
        var maxPre = Math.Min(Math.Max(0, StartFrame), loop);
        var maxPost = Math.Min(Math.Max(0, frameCount - EndFrame), loop);
        if (Focus == FocusField.PostCrossfade)
        {
            PostCrossfadeFrames = Math.Clamp(PostCrossfadeFrames, 0, maxPost);
            if (maintainFull)
            {
                PreCrossfadeFrames = Math.Clamp(loop - PostCrossfadeFrames, 0, maxPre);
                // ファイル余白でプリが足りないときはポストを合わせて合計を最大化
                PostCrossfadeFrames = Math.Clamp(loop - PreCrossfadeFrames, 0, maxPost);
            }
            else
            {
                PreCrossfadeFrames = Math.Clamp(
                    PreCrossfadeFrames, 0, Math.Min(maxPre, Math.Max(0, loop - PostCrossfadeFrames)));
            }
        }
        else
        {
            PreCrossfadeFrames = Math.Clamp(PreCrossfadeFrames, 0, maxPre);
            if (maintainFull && Focus == FocusField.PreCrossfade)
            {
                PostCrossfadeFrames = Math.Clamp(loop - PreCrossfadeFrames, 0, maxPost);
                PreCrossfadeFrames = Math.Clamp(loop - PostCrossfadeFrames, 0, maxPre);
            }
            else
            {
                PostCrossfadeFrames = Math.Clamp(
                    PostCrossfadeFrames, 0, Math.Min(maxPost, Math.Max(0, loop - PreCrossfadeFrames)));
            }
        }
    }

    public void SetPreCrossfade(long frames, long frameCount)
    {
        var maintain = ShouldMaintainFadeComplement;
        Focus = FocusField.PreCrossfade;
        PreCrossfadeFrames = frames;
        Clamp(frameCount, maintainFull: maintain);
    }

    public void SetPostCrossfade(long frames, long frameCount)
    {
        var maintain = ShouldMaintainFadeComplement;
        Focus = FocusField.PostCrossfade;
        PostCrossfadeFrames = frames;
        Clamp(frameCount, maintainFull: maintain);
    }

    public void Nudge(FocusField field, long delta, long frameCount)
    {
        Focus = field;
        switch (field)
        {
            case FocusField.Start:
                NudgeLoopEnd(isStart: true, delta, frameCount);
                break;
            case FocusField.End:
                NudgeLoopEnd(isStart: false, delta, frameCount);
                break;
            case FocusField.PreCrossfade:
                SetPreCrossfade(PreCrossfadeFrames + delta, frameCount);
                break;
            case FocusField.PostCrossfade:
                SetPostCrossfade(PostCrossfadeFrames + delta, frameCount);
                break;
        }
    }

    private void NudgeLoopEnd(bool isStart, long delta, long frameCount)
    {
        if (delta == 0)
        {
            return;
        }

        if (AlignLoopEndsToTwentyEight)
        {
            var units = Math.Max(1L, Math.Abs(delta));
            var step = Math.Sign(delta) * LoopEndAlignStep * units;
            if (isStart)
            {
                StartFrame = RoundToAlignStep(StartFrame) + step;
            }
            else
            {
                EndFrame = RoundToAlignStep(EndFrame) + step;
            }
        }
        else if (isStart)
        {
            StartFrame += delta;
        }
        else
        {
            EndFrame += delta;
        }

        Clamp(frameCount);
    }

    /// <summary>サンプルループ移動に合わせて S/E を合わせる。プリ／ポスト長はそのまま（旗が一緒に動く）。</summary>
    public void SyncLoopFromSampleLoop(WaveSelection loop, long frameCount)
    {
        if (loop.IsEmpty)
        {
            return;
        }

        StartFrame = loop.StartFrame;
        EndFrame = loop.EndFrame;
        Clamp(frameCount);
    }

    public void SetStart(long start, long frameCount)
    {
        StartFrame = AlignLoopEndsToTwentyEight ? RoundToAlignStep(start) : start;
        Clamp(frameCount);
    }

    public void SetEnd(long end, long frameCount)
    {
        EndFrame = AlignLoopEndsToTwentyEight ? RoundToAlignStep(end) : end;
        Clamp(frameCount);
    }

    public static long RoundToAlignStep(long value, long step = LoopEndAlignStep)
    {
        if (step <= 0)
        {
            return value;
        }

        return (long)Math.Round(value / (double)step, MidpointRounding.AwayFromZero) * step;
    }

    public CrossfadeEditState Clone() => new()
    {
        IsActive = IsActive,
        StartFrame = StartFrame,
        EndFrame = EndFrame,
        PreCrossfadeFrames = PreCrossfadeFrames,
        PostCrossfadeFrames = PostCrossfadeFrames,
        TruncateOutside = TruncateOutside,
        AlignLoopEndsToTwentyEight = AlignLoopEndsToTwentyEight,
        ShowXfadePreview = ShowXfadePreview,
        LoopPlayback = LoopPlayback,
        PreOutShape = PreOutShape,
        PreInShape = PreInShape,
        PostOutShape = PostOutShape,
        PostInShape = PostInShape,
        Focus = Focus,
    };
}
