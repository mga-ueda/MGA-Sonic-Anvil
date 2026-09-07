namespace MgaSonicAnvil.Audio;

/// <summary>
/// 終了時にドライバ／ミキサの先読みを無音で置き換える待ち時間。
/// </summary>
internal static class AudioOutputFlush
{
    /// <summary>いきなり 0 にするとクリックになるので、先にこの時間で落とす。</summary>
    public const int FadeMilliseconds = 20;

    /// <summary>
    /// 旧下限 400ms では仮想ミキサ hop が残ることがあった。
    /// 測れる先読みの 3 周＋デバイス尾で足りるので、床は 0.5 秒に留める。
    /// </summary>
    public const int MinMilliseconds = 500;

    /// <summary>巨大バッファでも終了待ちを伸ばしすぎない。</summary>
    public const int MaxMilliseconds = 2000;

    /// <summary>WASAPI / 最終出力 1 ホップ程度。</summary>
    public const int DeviceTailMilliseconds = 150;

    /// <summary>NAudio → ミキサ → ハードウェアの 3 段。</summary>
    public const int PipelineHops = 3;

    public static int EstimateMilliseconds(
        AudioOutputApi api,
        int sampleRate,
        int framesPerBuffer = 0,
        int playbackLatencySamples = 0,
        int waveDesiredLatencyMs = 0,
        int waveBufferCount = 0)
    {
        sampleRate = Math.Max(1, sampleRate);
        var pipelineMs = api switch
        {
            AudioOutputApi.Asio => EstimateAsioPipelineMilliseconds(
                sampleRate,
                framesPerBuffer,
                playbackLatencySamples),
            AudioOutputApi.Wasapi => AudioOutputFactory.WasapiLatencyMs * 8,
            _ => Math.Max(1, waveDesiredLatencyMs) * Math.Max(1, waveBufferCount) * 2,
        };

        return Math.Clamp(FadeMilliseconds + pipelineMs, MinMilliseconds, MaxMilliseconds);
    }

    public static int FadeFrames(int sampleRate) =>
        Math.Max(32, Math.Max(1, sampleRate) * FadeMilliseconds / 1000);

    private static int EstimateAsioPipelineMilliseconds(
        int sampleRate,
        int framesPerBuffer,
        int playbackLatencySamples)
    {
        var frames = Math.Max(1, framesPerBuffer);
        var queuedSamples = Math.Max(Math.Max(0, playbackLatencySamples), frames * 2);
        var queuedMs = queuedSamples * 1000.0 / sampleRate;
        return (int)Math.Ceiling(queuedMs * PipelineHops + DeviceTailMilliseconds);
    }
}
