using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

/// <summary>
/// Play -E（ループ折り返しの -E 二重再生）のプロバイダー挙動。
/// 本体 0.25 / -E 区間 0.5 の定数波形で、加算ミックスの有無を振幅で検証する。
/// </summary>
public sealed class ExitLayerPlaybackTests
{
    private const int LoopStart = 200;
    private const int LoopEnd = 800;
    private const int ExitEnd = 1000;
    private const float BodyValue = 0.25f;
    private const float ExitValue = 0.5f;

    [Fact]
    public void LoopWrap_WithPlayExitLayer_MixesExitRange()
    {
        var provider = MakeLoopingProvider(playExitLayer: true);

        // ループ 1 周分（600 フレーム）を読み切る。折り返し前は Exit は乗らない。
        var body = new float[600 * 2];
        Assert.Equal(body.Length, provider.Read(body, 0, body.Length));
        Assert.Equal(-1, provider.ExitCursorFrame);
        Assert.All(body, v => Assert.Equal(BodyValue, v, 3));

        // 折り返し後は本体 + Exit の加算ミックスになり、Exit ヘッドが -E 区間を進む。
        var mixed = new float[100 * 2];
        Assert.Equal(mixed.Length, provider.Read(mixed, 0, mixed.Length));
        Assert.InRange(provider.ExitCursorFrame, LoopEnd, ExitEnd);
        Assert.All(mixed, v => Assert.Equal(BodyValue + ExitValue, v, 3));
    }

    [Fact]
    public void LoopWrap_WithoutPlayExitLayer_PlaysLoopOnly()
    {
        var provider = MakeLoopingProvider(playExitLayer: false);

        // 折り返しを跨いでも Exit は始まらず、本体のみが続く。
        var buffer = new float[700 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(-1, provider.ExitCursorFrame);
        Assert.All(buffer, v => Assert.Equal(BodyValue, v, 3));
    }

    [Fact]
    public void ExitLayer_StopsAtExitEnd()
    {
        var provider = MakeLoopingProvider(playExitLayer: true);
        var skip = new float[600 * 2];
        _ = provider.Read(skip, 0, skip.Length); // 折り返し直前まで

        // Exit（200 フレーム）が尽きたら本体のみへ戻る。
        var tail = new float[300 * 2];
        Assert.Equal(tail.Length, provider.Read(tail, 0, tail.Length));
        Assert.Equal(-1, provider.ExitCursorFrame);
        Assert.Equal(BodyValue + ExitValue, tail[0], 3);
        Assert.Equal(BodyValue + ExitValue, tail[199 * 2], 3);
        Assert.Equal(BodyValue, tail[200 * 2], 3);
        Assert.Equal(BodyValue, tail[299 * 2], 3);
    }

    [Fact]
    public void DisablingPlayExitLayer_StopsRunningExit()
    {
        var provider = MakeLoopingProvider(playExitLayer: true);
        var skip = new float[650 * 2];
        _ = provider.Read(skip, 0, skip.Length); // 折り返しを跨いで Exit 進行中
        Assert.True(provider.ExitCursorFrame >= LoopEnd);

        provider.SetPlayExitLayer(false);
        Assert.Equal(-1, provider.ExitCursorFrame);
        var tail = new float[50 * 2];
        _ = provider.Read(tail, 0, tail.Length);
        Assert.All(tail, v => Assert.Equal(BodyValue, v, 3));
    }

    [Fact]
    public void SeekFrame_StopsRunningExit()
    {
        var provider = MakeLoopingProvider(playExitLayer: true);
        var skip = new float[650 * 2];
        _ = provider.Read(skip, 0, skip.Length);
        Assert.True(provider.ExitCursorFrame >= LoopEnd);

        provider.SeekFrame(LoopStart);
        Assert.Equal(-1, provider.ExitCursorFrame);
    }

    private static PlaybackSampleProvider MakeLoopingProvider(bool playExitLayer)
    {
        var samples = new float[ExitEnd * 2];
        for (var frame = 0; frame < ExitEnd; frame++)
        {
            var value = frame >= LoopEnd ? ExitValue : BodyValue;
            samples[frame * 2] = value;
            samples[frame * 2 + 1] = value;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, @"C:\tmp\loop.wav");
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, LoopStart, new WaveSelection(LoopStart, LoopEnd), loop: true);
        provider.SetExitSpan(new WaveSelection(LoopEnd, ExitEnd));
        provider.SetPlayExitLayer(playExitLayer);
        return provider;
    }
}
