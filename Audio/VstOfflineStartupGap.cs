namespace MgaSonicAnvil.Audio;

/// <summary>
/// オフライン先頭の、getLatencySamples では説明できない無音。
/// 重いプラグインが初回 process に追いつかず、出力だけ遅れている分。
/// </summary>
internal static class VstOfflineStartupGap
{
    public static int FindOnset(
        float[] interleaved,
        int frameOffset,
        int frameCount,
        int channels,
        float threshold)
    {
        if (frameCount <= 0 || channels <= 0 || threshold < 0f)
        {
            return -1;
        }

        var frames = interleaved.Length / channels;
        var last = Math.Min(frames, frameOffset + frameCount);
        for (var frame = Math.Max(0, frameOffset); frame < last; frame++)
        {
            var at = frame * channels;
            var peak = 0f;
            for (var c = 0; c < channels; c++)
            {
                var abs = Math.Abs(interleaved[at + c]);
                if (abs > peak)
                {
                    peak = abs;
                }
            }

            if (peak >= threshold)
            {
                return frame - frameOffset;
            }
        }

        return -1;
    }

    /// <summary>出力の出だしが入力より遅いフレーム数。キャップを超えたら 0（意図したディレイは動かさない）。</summary>
    public static int ExtraFrames(int sourceOnset, int outputOnset, int maxExtra)
    {
        if (sourceOnset < 0 || outputOnset < 0 || maxExtra <= 0)
        {
            return 0;
        }

        var extra = outputOnset - sourceOnset;
        if (extra <= 0 || extra > maxExtra)
        {
            return 0;
        }

        return extra;
    }
}
