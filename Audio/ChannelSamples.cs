namespace MgaSonicAnvil.Audio;

/// <summary>インターリーブ PCM の選んだチャンネルだけを触る。mask 0 は全チャンネル。</summary>
internal static class ChannelSamples
{
    public static bool IsScoped(int mask, int channels) =>
        ChannelSolo.ClampMask(mask, channels) != 0;

    public static void RestoreOthers(float[] before, float[] after, int channels, int mask)
    {
        if (!IsScoped(mask, channels) || before.Length != after.Length)
        {
            return;
        }

        for (var i = 0; i < after.Length; i++)
        {
            if (!ChannelSolo.Contains(mask, i % channels))
            {
                after[i] = before[i];
            }
        }
    }

    public static float Peak(ReadOnlySpan<float> interleaved, int channels, int mask)
    {
        var peak = 0f;
        if (!IsScoped(mask, channels))
        {
            foreach (var sample in interleaved)
            {
                peak = Math.Max(peak, Math.Abs(sample));
            }

            return peak;
        }

        for (var i = 0; i < interleaved.Length; i++)
        {
            if (ChannelSolo.Contains(mask, i % channels))
            {
                peak = Math.Max(peak, Math.Abs(interleaved[i]));
            }
        }

        return peak;
    }

    public static void ApplyGain(float[] interleaved, int channels, int mask, float gain)
    {
        if (channels < 1)
        {
            return;
        }

        for (var i = 0; i < interleaved.Length; i++)
        {
            if (ChannelSolo.Contains(mask, i % channels))
            {
                interleaved[i] *= gain;
            }
        }
    }

    public static void Silence(float[] interleaved, int channels, int mask)
    {
        if (!IsScoped(mask, channels))
        {
            Array.Clear(interleaved);
            return;
        }

        for (var i = 0; i < interleaved.Length; i++)
        {
            if (ChannelSolo.Contains(mask, i % channels))
            {
                interleaved[i] = 0;
            }
        }
    }

    public static (float[] Samples, int Channels) ExtractScope(float[] interleaved, int channels, int mask)
    {
        channels = Math.Max(1, channels);
        var frames = interleaved.Length / channels;
        mask = ChannelSolo.ClampMask(mask, channels);
        if (mask == 0)
        {
            return ((float[])interleaved.Clone(), channels);
        }

        var destChannels = ChannelSolo.Count(mask);
        var dest = new float[frames * destChannels];
        for (var frame = 0; frame < frames; frame++)
        {
            var di = 0;
            var src = frame * channels;
            var dst = frame * destChannels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (ChannelSolo.Contains(mask, ch))
                {
                    dest[dst + di] = interleaved[src + ch];
                    di++;
                }
            }
        }

        return (dest, destChannels);
    }

    public static void WriteFitted(
        float[] dest,
        int destChannels,
        int mask,
        float[] source,
        int sourceChannels)
    {
        destChannels = Math.Max(1, destChannels);
        sourceChannels = Math.Max(1, sourceChannels);
        mask = ChannelSolo.ClampMask(mask, destChannels);
        if (mask == 0)
        {
            return;
        }

        var destFrames = dest.Length / destChannels;
        var sourceFrames = source.Length / sourceChannels;
        var frames = Math.Min(destFrames, sourceFrames);
        var destList = List(mask, destChannels);
        for (var i = 0; i < frames; i++)
        {
            var src = i * sourceChannels;
            var dst = i * destChannels;
            for (var d = 0; d < destList.Length; d++)
            {
                var s = d < sourceChannels ? d : 0;
                dest[dst + destList[d]] = source[src + s];
            }
        }

        for (var i = frames; i < destFrames; i++)
        {
            var dst = i * destChannels;
            for (var d = 0; d < destList.Length; d++)
            {
                dest[dst + destList[d]] = 0;
            }
        }
    }

    public static int[] List(int mask, int channels)
    {
        channels = Math.Max(1, channels);
        mask = ChannelSolo.ClampMask(mask, channels);
        if (mask == 0)
        {
            var all = new int[channels];
            for (var i = 0; i < channels; i++)
            {
                all[i] = i;
            }

            return all;
        }

        var list = new int[ChannelSolo.Count(mask)];
        var n = 0;
        for (var i = 0; i < channels; i++)
        {
            if (ChannelSolo.Contains(mask, i))
            {
                list[n++] = i;
            }
        }

        return list;
    }
}
