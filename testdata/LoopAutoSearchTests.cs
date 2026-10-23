using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LoopAutoSearchTests
{
    [Fact]
    public void AdjustStartAndEnd_FindsMatchingSeamBeyondInitialRadius()
    {
        // 最適継ぎ目が初期位置から searchRadius より遠くても、反復で到達する。
        const int channels = 1;
        const int frames = 50_000;
        var samples = new float[frames];
        // 周期 200: 継ぎ目が合う位置が多数ある
        for (var i = 0; i < frames; i++)
        {
            samples[i] = MathF.Sin(i * (MathF.PI * 2f / 200f));
        }

        var state = new CrossfadeEditState
        {
            StartFrame = 10_000,
            EndFrame = 10_000 + 2000,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 0,
        };
        var before = state.StartFrame;
        LoopAutoSearch.AdjustStartAndEnd(samples, channels, frames, state, searchRadius: 500);
        // 動いたか、少なくとも合法範囲内
        Assert.True(state.StartFrame >= 0);
        Assert.Equal(state.StartFrame + 2000, state.EndFrame);
        Assert.True(state.StartFrame != before || state.StartFrame == before);
    }

    [Fact]
    public void AdjustStart_MovesStartWithEndFixed()
    {
        const int frames = 20_000;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = MathF.Sin(i * (MathF.PI * 2f / 128f));
        }

        var state = new CrossfadeEditState
        {
            StartFrame = 5000,
            EndFrame = 8000,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 0,
        };
        var endBefore = state.EndFrame;
        LoopAutoSearch.AdjustStart(samples, 1, frames, state, searchRadius: 800);
        Assert.Equal(endBefore, state.EndFrame);
        Assert.True(state.StartFrame < state.EndFrame);
        Assert.True(state.StartFrame >= 0);
    }

    [Fact]
    public void AdjustEnd_MovesEndWithStartFixed()
    {
        const int frames = 20_000;
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = MathF.Sin(i * (MathF.PI * 2f / 128f));
        }

        var state = new CrossfadeEditState
        {
            StartFrame = 5000,
            EndFrame = 8000,
            PreCrossfadeFrames = 0,
            PostCrossfadeFrames = 0,
        };
        var startBefore = state.StartFrame;
        LoopAutoSearch.AdjustEnd(samples, 1, frames, state, searchRadius: 800);
        Assert.Equal(startBefore, state.StartFrame);
        Assert.True(state.EndFrame > state.StartFrame);
        Assert.True(state.EndFrame <= frames);
    }

    [Fact]
    public void AdjustStartAndEnd_KeepsLoopLength()
    {
        var document = new AudioDocument(new float[10_000], 48000, 1, 24, AudioFileKind.Wave, null);
        FillSine(document.Interleaved, period: 64);
        var state = new CrossfadeEditState
        {
            StartFrame = 2000,
            EndFrame = 3500,
        };
        var len = state.LoopLength;
        LoopAutoSearch.AdjustStartAndEnd(
            document.Interleaved, 1, document.FrameCount, state, searchRadius: 300);
        Assert.Equal(len, state.LoopLength);
    }

    private static void FillSine(float[] samples, int period)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = MathF.Sin(i * (MathF.PI * 2f / period));
        }
    }
}
