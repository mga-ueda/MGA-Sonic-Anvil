using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// フェード調整の非破壊プレビューがネイティブ／リサンプル再生の両方で聴こえること。
/// </summary>
public sealed class LoopFadePreviewPlaybackTests
{
    private const int Start = 100;
    private const int End = 400;
    private const int Pre = 40;
    private const float Body = 0.25f;
    private const float BeforeStart = 1f;

    [Fact]
    public void NativeLoop_AppliesPreCrossfadeNearEnd()
    {
        var provider = MakeProvider(sourceRate: 48000, deviceRate: 48000, withPreview: true);
        AssertNearEndIsMixed(provider);
    }

    [Fact]
    public void ResampledLoop_AppliesPreCrossfadeNearEnd()
    {
        // 44100→48000 は UsesNativeRead=false。以前はここでプレビューが消えていた。
        var provider = MakeProvider(sourceRate: 44100, deviceRate: 48000, withPreview: true);
        AssertNearEndIsMixed(provider);
    }

    [Fact]
    public void ResampledLoop_WithoutPreview_StaysDry()
    {
        var provider = MakeProvider(sourceRate: 44100, deviceRate: 48000, withPreview: false);
        SeekNearEnd(provider);
        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, v => Assert.Equal(Body, v, 2));
    }

    private static void AssertNearEndIsMixed(PlaybackSampleProvider provider)
    {
        SeekNearEnd(provider);
        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        // プリ中盤: Body*(1-t) + BeforeStart*t。線形なら Body より明らかに大きい。
        var mid = buffer[10 * 2];
        Assert.True(mid > Body + 0.15f, $"expected pre mix above dry body, got {mid}");
        Assert.True(mid < BeforeStart - 0.05f, $"expected pre mix below pure in-side, got {mid}");
    }

    private static void SeekNearEnd(PlaybackSampleProvider provider) =>
        provider.SeekFrame(End - Pre / 2);

    private static PlaybackSampleProvider MakeProvider(int sourceRate, int deviceRate, bool withPreview)
    {
        var frameCount = End + 80;
        var samples = new float[frameCount * 2];
        for (var frame = 0; frame < frameCount; frame++)
        {
            var value = frame >= Start - Pre && frame < Start ? BeforeStart : Body;
            samples[frame * 2] = value;
            samples[frame * 2 + 1] = value;
        }

        var document = new AudioDocument(
            samples,
            sourceRate,
            2,
            16,
            AudioFileKind.Wave,
            @"C:\tmp\fade-preview.wav");
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(deviceRate);
        provider.Bind(document, Start, new WaveSelection(Start, End), loop: true);
        if (withPreview)
        {
            provider.SetLoopFadePreview(new LoopFadePreview
            {
                StartFrame = Start,
                EndFrame = End,
                PreCrossfadeFrames = Pre,
                PostCrossfadeFrames = 0,
                PreOutShape = FadeShape.Linear,
                PreInShape = FadeShape.Linear,
            });
        }

        return provider;
    }
}
