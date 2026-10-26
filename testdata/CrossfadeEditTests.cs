using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class CrossfadeEditTests
{
    [Fact]
    public void CreateInitial_PrefersSelectionThenSampleLoop()
    {
        var document = MakeDocument(1000);
        document.Selection = new WaveSelection(100, 400);
        var fromSelection = CrossfadeEditState.CreateInitial(document);
        Assert.Equal(100, fromSelection.StartFrame);
        Assert.Equal(400, fromSelection.EndFrame);

        document.Selection = WaveSelection.Empty;
        document.SetSampleLoop(new WaveSelection(50, 250));
        var fromLoop = CrossfadeEditState.CreateInitial(document);
        Assert.Equal(50, fromLoop.StartFrame);
        Assert.Equal(250, fromLoop.EndFrame);
        // Default is Pre=0 / Post=0.
        Assert.Equal(0, fromLoop.PreCrossfadeFrames);
        Assert.Equal(0, fromLoop.PostCrossfadeFrames);
        Assert.Equal(fromLoop.StartFrame - fromLoop.PreCrossfadeFrames, fromLoop.PreFlagFrame);
        Assert.Equal(fromLoop.EndFrame - fromLoop.PreCrossfadeFrames, fromLoop.PreDrawStartFrame);
        Assert.Equal(fromLoop.EndFrame + fromLoop.PostCrossfadeFrames, fromLoop.PostFlagFrame);
        Assert.Equal(fromLoop.StartFrame, fromLoop.PostDrawStartFrame);
        Assert.Equal(fromLoop.PreFlagFrame, fromLoop.PreBarRange.StartFrame);
        Assert.Equal(fromLoop.StartFrame, fromLoop.PreBarRange.EndFrame);
        Assert.Equal(fromLoop.EndFrame, fromLoop.PostBarRange.StartFrame);
    }

    [Fact]
    public void CreateInitial_DefaultsToZeroPreAndZeroPost()
    {
        var document = MakeDocument(200_000);
        document.SetSampleLoop(new WaveSelection(48_000, 96_000));
        var state = CrossfadeEditState.CreateInitial(document);
        Assert.Equal(0, state.PreCrossfadeFrames);
        Assert.Equal(0, state.PostCrossfadeFrames);
    }

    [Fact]
    public void UiStrings_FadeToolTitle_Japanese()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Contains("フェード調整", UiStrings.CrossfadeEditTitle);
            Assert.Contains("プリクロス", UiStrings.CrossfadeLabelFadeIn);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FlagAndDrawPositions_AreSeparated()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 300,
            PreCrossfadeFrames = 20,
            PostCrossfadeFrames = 15,
        };
        Assert.Equal(80, state.PreFlagFrame);
        Assert.Equal(280, state.PreDrawStartFrame);
        Assert.NotEqual(state.PreFlagFrame, state.PreDrawStartFrame);
        Assert.Equal(315, state.PostFlagFrame);
        Assert.Equal(100, state.PostDrawStartFrame);
        Assert.Equal(300, state.PostBarRange.StartFrame);
        Assert.Equal(315, state.PostBarRange.EndFrame);
        Assert.NotEqual(state.PostFlagFrame, state.PostDrawStartFrame);
    }

    [Fact]
    public void BakePre_WritesIntoEndBefore()
    {
        var frames = 40;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = i < 28 ? 1f : -1f;
            samples[i * 2 + 1] = samples[i * 2];
        }

        var start = 20L;
        var end = 36L;
        var x = 8L;
        var baked = (float[])samples.Clone();
        LoopCrossfade.BakePreCrossfade(
            samples, baked, 2, frames, start, end, x, FadeShape.Linear, FadeShape.Linear);
        var mid = baked[(int)(end - x / 2) * 2];
        Assert.True(Math.Abs(mid) < 0.2f);
    }

    [Fact]
    public void BakePost_WritesIntoLoopStart()
    {
        var frames = 50;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = i < 28 ? 1f : -1f;
            samples[i * 2 + 1] = samples[i * 2];
        }

        var start = 20L;
        var end = 36L;
        var x = 8L; // needs [36,44) after E
        var baked = (float[])samples.Clone();
        LoopCrossfade.BakePostCrossfade(
            samples, baked, 2, frames, start, end, x, FadeShape.Linear, FadeShape.Linear);
        var mid = baked[(int)(start + x / 2) * 2];
        Assert.True(Math.Abs(mid) < 0.2f);
        // Sample just before End is untouched
        Assert.Equal(-1f, baked[(int)(end - 1) * 2]);
    }

    [Fact]
    public void LoopFadePreview_PreAtEnd_PostAtStart_UsesOutside()
    {
        var preview = new LoopFadePreview
        {
            StartFrame = 100,
            EndFrame = 300,
            PreCrossfadeFrames = 20,
            PostCrossfadeFrames = 15,
            PreOutShape = FadeShape.Linear,
            PreInShape = FadeShape.Linear,
            PostOutShape = FadeShape.Linear,
            PostInShape = FadeShape.Linear,
        };

        Assert.True(preview.TryCrossfadeMix(105, out var outSide, out var inSide, out _, out _));
        Assert.Equal(105, inSide); // start side
        Assert.Equal(305, outSide); // End+5 after E

        Assert.True(preview.TryCrossfadeMix(285, out outSide, out inSide, out _, out _));
        Assert.Equal(285, outSide); // end side
        Assert.Equal(85, inSide); // Start-20+5 before S
    }

    [Fact]
    public void Clamp_LimitsPreByStartFrame()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 300,
            PreCrossfadeFrames = 500,
            PostCrossfadeFrames = 500,
        };
        state.Clamp(frameCount: 320);
        Assert.Equal(40, state.PreCrossfadeFrames);
        Assert.Equal(20, state.PostCrossfadeFrames); // min(file after E, loop-Pre)
        Assert.True(state.PreCrossfadeFrames + state.PostCrossfadeFrames <= state.LoopLength);
    }

    [Fact]
    public void Clamp_PrePlusPost_AtMostLoopLength()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 90,
            PostCrossfadeFrames = 90,
            Focus = CrossfadeEditState.FocusField.PreCrossfade,
        };
        state.Clamp(frameCount: 400);
        Assert.Equal(90, state.PreCrossfadeFrames);
        Assert.Equal(10, state.PostCrossfadeFrames);

        state.PreCrossfadeFrames = 90;
        state.PostCrossfadeFrames = 90;
        state.Focus = CrossfadeEditState.FocusField.PostCrossfade;
        state.Clamp(frameCount: 400);
        Assert.Equal(90, state.PostCrossfadeFrames);
        Assert.Equal(10, state.PreCrossfadeFrames);
    }

    [Fact]
    public void SetPost_OverHalf_ShrinksPreToKeepSumWithinLoop()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200, // loop = 100
            PreCrossfadeFrames = 50,
            PostCrossfadeFrames = 40,
        };
        state.SetPostCrossfade(60, frameCount: 1000);
        Assert.Equal(60, state.PostCrossfadeFrames);
        Assert.Equal(40, state.PreCrossfadeFrames);
        Assert.Equal(100, state.PreCrossfadeFrames + state.PostCrossfadeFrames);
    }

    [Fact]
    public void SetPre_OverHalf_ShrinksPostToKeepSumWithinLoop()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 40,
            PostCrossfadeFrames = 50,
        };
        state.SetPreCrossfade(60, frameCount: 1000);
        Assert.Equal(60, state.PreCrossfadeFrames);
        Assert.Equal(40, state.PostCrossfadeFrames);
        Assert.Equal(100, state.PreCrossfadeFrames + state.PostCrossfadeFrames);
    }

    [Fact]
    public void SetPost_WhenSumFull_DecreasingPostGrowsPre()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 50,
            PostCrossfadeFrames = 50,
        };
        Assert.True(state.IsFadeSumFull);
        state.SetPostCrossfade(40, frameCount: 1000);
        Assert.Equal(40, state.PostCrossfadeFrames);
        Assert.Equal(60, state.PreCrossfadeFrames);
        Assert.True(state.IsFadeSumFull);
    }

    [Fact]
    public void SetPre_WhenSumFull_DecreasingPreGrowsPost()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 50,
            PostCrossfadeFrames = 50,
        };
        state.SetPreCrossfade(30, frameCount: 1000);
        Assert.Equal(30, state.PreCrossfadeFrames);
        Assert.Equal(70, state.PostCrossfadeFrames);
        Assert.True(state.IsFadeSumFull);
    }

    [Fact]
    public void NudgePost_WhenSumFull_KeepsSumAtLoopLength()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 50,
            PostCrossfadeFrames = 50,
        };
        state.Nudge(CrossfadeEditState.FocusField.PostCrossfade, 10, frameCount: 1000);
        Assert.Equal(60, state.PostCrossfadeFrames);
        Assert.Equal(40, state.PreCrossfadeFrames);
        state.Nudge(CrossfadeEditState.FocusField.PostCrossfade, -15, frameCount: 1000);
        Assert.Equal(45, state.PostCrossfadeFrames);
        Assert.Equal(55, state.PreCrossfadeFrames);
        Assert.Equal(100, state.PreCrossfadeFrames + state.PostCrossfadeFrames);
    }

    [Fact]
    public void SetPost_WhenNotFull_DecreasingDoesNotGrowPre()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 20,
            PostCrossfadeFrames = 30,
        };
        state.SetPostCrossfade(10, frameCount: 1000);
        Assert.Equal(10, state.PostCrossfadeFrames);
        Assert.Equal(20, state.PreCrossfadeFrames);
        Assert.False(state.IsFadeSumFull);
    }

    [Fact]
    public void SetPost_WhenPreIsZero_DecreasingPostDoesNotGrowPre()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 100,
        };
        Assert.True(state.IsFadeSumFull);
        Assert.False(state.ShouldMaintainFadeComplement);
        state.SetPostCrossfade(80, frameCount: 1000);
        Assert.Equal(80, state.PostCrossfadeFrames);
        Assert.Equal(0, state.PreCrossfadeFrames);
    }

    [Fact]
    public void SetPre_WhenPostIsZero_DecreasingPreDoesNotGrowPost()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 100,
            PostCrossfadeFrames = 0,
        };
        Assert.False(state.ShouldMaintainFadeComplement);
        state.SetPreCrossfade(70, frameCount: 1000);
        Assert.Equal(70, state.PreCrossfadeFrames);
        Assert.Equal(0, state.PostCrossfadeFrames);
    }

    [Fact]
    public void AlignLoopEndsToTwentyEight_QuantizesStartAndEndOnClamp()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 250,
            AlignLoopEndsToTwentyEight = true,
        };
        state.Clamp(frameCount: 1000);
        Assert.Equal(112, state.StartFrame); // nearest 28*4=112
        Assert.Equal(252, state.EndFrame); // nearest 28*9=252
    }

    [Fact]
    public void NudgeStart_WhenAligned_MovesByTwentyEight()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 112,
            EndFrame = 280,
            AlignLoopEndsToTwentyEight = true,
        };
        state.Nudge(CrossfadeEditState.FocusField.Start, 1, frameCount: 1000);
        Assert.Equal(140, state.StartFrame);
        state.Nudge(CrossfadeEditState.FocusField.Start, -1, frameCount: 1000);
        Assert.Equal(112, state.StartFrame);
    }

    [Fact]
    public void SetPre_FromZero_WhilePostFillsLoop_ShrinksPostAndRestoresComplement()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 200,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 100,
        };
        // 0 側を増やす → 超過削減でポストが減り、双方が正になって連動条件が戻る
        state.SetPreCrossfade(20, frameCount: 1000);
        Assert.Equal(20, state.PreCrossfadeFrames);
        Assert.Equal(80, state.PostCrossfadeFrames);
        Assert.True(state.ShouldMaintainFadeComplement);
        state.SetPostCrossfade(60, frameCount: 1000);
        Assert.Equal(60, state.PostCrossfadeFrames);
        Assert.Equal(40, state.PreCrossfadeFrames);
        Assert.True(state.IsFadeSumFull);
    }

    [Fact]
    public void LoopMix_BakesAndSetsSampleLoop()
    {
        var frames = 200;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = i < 100 ? 1f : -1f;
            samples[i * 2 + 1] = samples[i * 2];
        }

        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 140,
            PreCrossfadeFrames = 10,
            PreOutShape = FadeShape.Linear,
            PreInShape = FadeShape.Linear,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state);
        Assert.NotNull(command);
        command!.Apply(document);
        Assert.Equal(new WaveSelection(40, 140), document.SampleLoop);
        Assert.True(Math.Abs(document.Interleaved[(140 - 5) * 2]) < 1f);
    }

    [Fact]
    public void LoopMix_Seam_PreUsesMaterialBeforeStart()
    {
        var frames = 200;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = (i / (float)frames) * 2f - 1f;
            samples[i * 2 + 1] = samples[i * 2];
        }

        var document = new AudioDocument(samples, 48000, 2, 24, AudioFileKind.Wave, null);
        const long start = 40;
        const long end = 140;
        const long pre = 10;
        var state = new CrossfadeEditState
        {
            StartFrame = start,
            EndFrame = end,
            PreCrossfadeFrames = pre,
            PreOutShape = FadeShape.Linear,
            PreInShape = FadeShape.Linear,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state)!;
        command.Apply(document);

        Assert.Equal(new WaveSelection(start, end), document.SampleLoop);
        // E-1 ≈ S-1 → wrap to S keeps file continuity
        var endLast = document.Interleaved[(int)(end - 1) * 2];
        var beforeStart = samples[(int)(start - 1) * 2];
        Assert.Equal(beforeStart, endLast, precision: 5);
    }

    [Fact]
    public void LoopMix_TruncateOutside_ShortensFileAndResetsLoop()
    {
        var document = MakeDocument(500);
        FillRamp(document);
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 300,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 20,
            TruncateOutside = true,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state);
        Assert.NotNull(command);
        command!.Apply(document);
        Assert.Equal(200, document.FrameCount);
        Assert.Equal(new WaveSelection(0, 200), document.SampleLoop);
    }

    [Fact]
    public void LoopMix_DeleteAfterLoop_KeepsBeforeStart()
    {
        var document = MakeDocument(500);
        FillRamp(document);
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 300,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 20,
            DeleteAfterLoop = true,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state);
        Assert.NotNull(command);
        command!.Apply(document);
        Assert.Equal(300, document.FrameCount);
        Assert.Equal(new WaveSelection(100, 300), document.SampleLoop);
    }

    [Fact]
    public void LoopMix_UndoRestoresSamplesAndLoop()
    {
        var document = MakeDocument(200);
        FillRamp(document);
        document.SetSampleLoop(new WaveSelection(10, 50));
        var beforeLoop = document.SampleLoop;
        var before0 = document.Interleaved[0];

        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 120,
            PreCrossfadeFrames = 8,
            PostCrossfadeFrames = 4,
        };
        var command = ProcessEdits.ApplyLoopMix(document, state)!;
        command.Apply(document);
        Assert.Equal(new WaveSelection(40, 120), document.SampleLoop);
        command.Revert(document);
        Assert.Equal(beforeLoop, document.SampleLoop);
        Assert.Equal(before0, document.Interleaved[0]);
    }

    [Fact]
    public void LoopMix_ReportsClipWhenEqualPowerStyleSumExceedsFullScale()
    {
        var frames = 200;
        var samples = new float[frames];
        Array.Fill(samples, 1f);
        var document = new AudioDocument(samples, 48000, 1, 24, AudioFileKind.Wave, null);
        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 140,
            PreCrossfadeFrames = 20,
            PreOutShape = FadeShape.Exp3,
            PreInShape = FadeShape.Linear,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state, lowerClippedCrossfade: false, out var clip);
        Assert.NotNull(command);
        Assert.True(clip.ExceedsFullScale);
        Assert.True(clip.Peak > 1f);

        command!.Apply(document);
        var bakedPeak = 0f;
        for (var i = (int)(140 - 20); i < 140; i++)
        {
            bakedPeak = Math.Max(bakedPeak, Math.Abs(document.Interleaved[i]));
        }

        Assert.True(bakedPeak > 1f);
    }

    [Fact]
    public void LoopMix_LowerClippedCrossfade_LowersEntireWaveformWithinFullScale()
    {
        var frames = 200;
        var samples = new float[frames];
        Array.Fill(samples, 1f);
        var document = new AudioDocument(samples, 48000, 1, 24, AudioFileKind.Wave, null);
        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 140,
            PreCrossfadeFrames = 20,
            PostCrossfadeFrames = 20,
            PreOutShape = FadeShape.Exp3,
            PreInShape = FadeShape.Linear,
            PostOutShape = FadeShape.Exp3,
            PostInShape = FadeShape.Linear,
        };

        var command = ProcessEdits.ApplyLoopMix(document, state, lowerClippedCrossfade: true, out var clip);
        Assert.NotNull(command);
        Assert.False(clip.ExceedsFullScale);
        Assert.True(command!.Persist!.Amount > 0);

        command.Apply(document);
        for (var i = 0; i < frames; i++)
        {
            Assert.InRange(Math.Abs(document.Interleaved[i]), 0f, 1f);
        }

        // クロスフェード外も同じ倍率で下がる（境界での段差を作らない）。
        Assert.True(Math.Abs(document.Interleaved[0]) < 1f);
        Assert.True(Math.Abs(document.Interleaved[100]) < 1f);
        Assert.True(Math.Abs(document.Interleaved[199]) < 1f);
    }

    [Fact]
    public void ConfirmCrossfadeClip_UsesOkCancelWords()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var japanese = UiStrings.ConfirmCrossfadeClip("1.5");
            Assert.Contains("0 dBFS を 1.5 dB 超えています", japanese);
            Assert.Contains("波形全体を下げて", japanese);
            Assert.Contains("\n\nOK:", japanese);
            Assert.Contains("\nCancel:", japanese);
            Assert.DoesNotContain("\nYes:", japanese);
            Assert.DoesNotContain("\nNo:", japanese);

            UiStrings.SetLanguage(UiLanguage.English);
            var english = UiStrings.ConfirmCrossfadeClip("1.5");
            Assert.Contains("1.5 dB over 0 dBFS", english);
            Assert.Contains("lower the entire waveform", english);
            Assert.Contains("\n\nOK:", english);
            Assert.Contains("\nCancel:", english);
            Assert.DoesNotContain("\nYes:", english);
            Assert.DoesNotContain("\nNo:", english);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void SnapToZeroCrossing_FindsSignChange()
    {
        var frames = 100;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = i < 50 ? -0.5f : 0.5f;
        }

        var snapped = LoopCrossfade.SnapToZeroCrossing(samples, 1, frames, 48, searchRadius: 20);
        Assert.Equal(50, snapped);
    }

    [Fact]
    public void SnapToZeroCrossing_FindsExactZeroSample()
    {
        var frames = 80;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = i == 40 ? 0f : (i < 40 ? -0.4f : 0.4f);
        }

        var snapped = LoopCrossfade.SnapToZeroCrossing(samples, 1, frames, 37, searchRadius: 20);
        Assert.Equal(40, snapped);
    }

    [Fact]
    public void SnapToZeroCrossing_AlignTwentyEight_StaysOnGridNearZero()
    {
        var frames = 400;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            // 140 (=28*5) で符号変化
            samples[i] = i < 140 ? -0.6f : 0.6f;
        }

        var snapped = LoopCrossfade.SnapToZeroCrossing(
            samples, 1, frames, 130, searchRadius: 40, alignToTwentyEight: true);
        Assert.Equal(140, snapped);
        Assert.Equal(0, snapped % 28);
    }

    [Fact]
    public void SnapEndToZeroCrossing_TargetsSeamSampleNotEndFrame()
    {
        var frames = 100;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            // フレーム 50 で符号変化 → 継ぎ目 seam=50、半開の E は 51
            samples[i] = i < 50 ? -0.5f : 0.5f;
        }

        var snapped = LoopCrossfade.SnapEndToZeroCrossing(samples, 1, frames, 60, searchRadius: 20);
        Assert.Equal(51, snapped);
        // 始点用の吸着とは 1 サンプルずれる
        Assert.Equal(50, LoopCrossfade.SnapToZeroCrossing(samples, 1, frames, 60, searchRadius: 20));
    }

    [Fact]
    public void SnapEndToZeroCrossing_AlignTwentyEight_ScoresSeamNotEnd()
    {
        var frames = 400;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            // 継ぎ目 139 で符号変化 → E=140（28 格子）。
            // E 自体が 0 の 168 は旧ロジック向けの罠。
            samples[i] = i < 139 ? -0.6f : 0.6f;
        }

        samples[168] = 0f;

        var snapped = LoopCrossfade.SnapEndToZeroCrossing(
            samples, 1, frames, 150, searchRadius: 40, alignToTwentyEight: true);
        Assert.Equal(140, snapped);
        Assert.Equal(0, snapped % 28);
        // 終点フレーム自体を見ると 168 の方が「ゼロ」に見える
        Assert.Equal(168, LoopCrossfade.SnapToZeroCrossing(
            samples, 1, frames, 150, searchRadius: 40, alignToTwentyEight: true));
    }

    [Fact]
    public void SnapEndToZeroCrossing_StereoOutOfPhase_UsesPeakNotAverage()
    {
        // L=+0.5 / R=-0.5 だと平均は常に 0。旧実装はどこでも「ゼロ」と誤認して動かなかった。
        var frames = 200;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.5f;
            samples[i * 2 + 1] = -0.5f;
        }

        // フレーム 80 だけ両 ch を 0 に（真の継ぎ目）
        samples[80 * 2] = 0f;
        samples[80 * 2 + 1] = 0f;

        var snapped = LoopCrossfade.SnapEndToZeroCrossing(
            samples, 2, frames, 100, searchRadius: 40);
        Assert.Equal(81, snapped);
    }

    [Fact]
    public void SnapToZeroCrossing_StereoOutOfPhase_FindsSharedZero()
    {
        var frames = 200;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = 0.5f;
            samples[i * 2 + 1] = -0.5f;
        }

        samples[80 * 2] = 0f;
        samples[80 * 2 + 1] = 0f;

        var snapped = LoopCrossfade.SnapToZeroCrossing(
            samples, 2, frames, 100, searchRadius: 40);
        Assert.Equal(80, snapped);
    }

    [Fact]
    public void BuildSplicePreview_FillsLeftAndRight()
    {
        var document = MakeDocument(200);
        FillRamp(document);
        var state = new CrossfadeEditState
        {
            StartFrame = 40,
            EndFrame = 160,
            PreCrossfadeFrames = 8,
            ShowXfadePreview = true,
        };
        LoopCrossfade.BuildSplicePreview(
            document.Interleaved,
            document.Channels,
            document.FrameCount,
            state,
            halfFrames: 32,
            out var left,
            out var right);
        Assert.Equal(32, left.Length);
        Assert.Equal(32, right.Length);
    }

    [Fact]
    public void BuildSplicePreview_PreMixesOnLeft_PostMixesOnRight()
    {
        // 左=エンド側に定数 0.2、S 手前=1.0、右=スタート側に 0.2、E 以降=-1.0。
        // 線形クロスの中点は左右とも約 0.6 / -0.4 になる（ドライの 0.2 とは違う）。
        const int start = 40;
        const int end = 160;
        const int pre = 10;
        const int post = 10;
        var document = MakeDocument(200);
        var samples = document.Interleaved;
        for (var i = 0; i < document.FrameCount; i++)
        {
            float v = 0.2f;
            if (i >= start - pre && i < start)
            {
                v = 1f;
            }
            else if (i >= end && i < end + post)
            {
                v = -1f;
            }

            samples[i * 2] = v;
            samples[i * 2 + 1] = v;
        }

        var state = new CrossfadeEditState
        {
            StartFrame = start,
            EndFrame = end,
            PreCrossfadeFrames = pre,
            PostCrossfadeFrames = post,
            PreOutShape = FadeShape.Linear,
            PreInShape = FadeShape.Linear,
            PostOutShape = FadeShape.Linear,
            PostInShape = FadeShape.Linear,
            ShowXfadePreview = true,
        };
        LoopCrossfade.BuildSplicePreviewChannels(
            samples,
            2,
            document.FrameCount,
            state,
            halfFrames: 32,
            panLeftFrames: 0,
            panRightFrames: 0,
            out var leftByCh,
            out var rightByCh);

        const int mid = 5;
        FadeCurves.CrossfadeGains(
            FadeShape.Linear,
            FadeShape.Linear,
            mid / (double)(pre - 1),
            out var outGain,
            out var inGain);
        var expectedPre = 0.2f * outGain + 1f * inGain;
        var expectedPost = -1f * outGain + 0.2f * inGain;
        // 左 half の末尾寄り＝プリ（destIndex = 32-10+5 = 27）
        Assert.Equal(expectedPre, leftByCh[0][32 - pre + mid], 3);
        // 右 half の先頭寄り＝ポスト（destIndex = 5）
        Assert.Equal(expectedPost, rightByCh[0][mid], 3);
    }

    [Fact]
    public void SyncLoopFromSampleLoop_KeepsFadeLengthsAndMovesFlags()
    {
        var state = new CrossfadeEditState
        {
            StartFrame = 100,
            EndFrame = 300,
            PreCrossfadeFrames = 20,
            PostCrossfadeFrames = 15,
        };
        state.SyncLoopFromSampleLoop(new WaveSelection(140, 340), frameCount: 1000);
        Assert.Equal(140, state.StartFrame);
        Assert.Equal(340, state.EndFrame);
        Assert.Equal(20, state.PreCrossfadeFrames);
        Assert.Equal(15, state.PostCrossfadeFrames);
        Assert.Equal(120, state.PreFlagFrame);
        Assert.Equal(355, state.PostFlagFrame);
    }

    private static AudioDocument MakeDocument(int frames) =>
        new(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);

    private static void FillRamp(AudioDocument document)
    {
        var samples = document.Interleaved;
        var channels = document.Channels;
        for (var i = 0; i < document.FrameCount; i++)
        {
            var v = (i / (float)document.FrameCount) * 2f - 1f;
            for (var ch = 0; ch < channels; ch++)
            {
                samples[i * channels + ch] = v;
            }
        }
    }
}
