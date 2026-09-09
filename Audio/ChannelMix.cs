namespace MgaSonicAnvil.Audio;

/// <summary>多チャンネルを再生用の L/R に畳む。文書上のチャンネル数はそのまま残す。</summary>
internal static class ChannelMix
{
    private const float Center = 0.70710677f;

    public static void Downmix(float[] interleaved, int offset, int channels, out float left, out float right) =>
        Downmix(interleaved.AsSpan(offset, channels), out left, out right);

    public static float Mid(ReadOnlySpan<float> frame)
    {
        Downmix(frame, out var left, out var right);
        return 0.5f * (left + right);
    }

    /// <summary>
    /// 1 フレームのチャンネル包絡。重ね波形と全体波形が使う。
    /// Mid は逆相で消えるので、表示の有無判定には使わない。
    /// </summary>
    public static void FrameEnvelope(
        float[] interleaved,
        int channels,
        long frame,
        long frameCount,
        out float min,
        out float max)
    {
        if (!TryFrameOffset(interleaved, channels, frame, frameCount, out var offset))
        {
            min = 0;
            max = 0;
            return;
        }

        Envelope(interleaved.AsSpan(offset, channels), out min, out max);
    }

    public static void Envelope(ReadOnlySpan<float> frame, out float min, out float max)
    {
        if (frame.Length == 0)
        {
            min = 0;
            max = 0;
            return;
        }

        min = max = frame[0];
        for (var i = 1; i < frame.Length; i++)
        {
            var sample = frame[i];
            if (sample < min)
            {
                min = sample;
            }

            if (sample > max)
            {
                max = sample;
            }
        }
    }

    /// <summary>
    /// packed ピーク列 [column * channels + ch] を、各列のチャンネル和集合へ畳む。
    /// 書き込みは先頭 count 要素。
    /// </summary>
    public static void FoldPackedPeaksToUnion(float[] mins, float[] maxs, int count, int channels)
    {
        if (channels <= 1 || count <= 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var src = i * channels;
            Envelope(mins.AsSpan(src, channels), out var min, out _);
            Envelope(maxs.AsSpan(src, channels), out _, out var max);
            mins[i] = min;
            maxs[i] = max;
        }
    }

    internal static bool TryFrameOffset(
        float[] interleaved,
        int channels,
        long frame,
        long frameCount,
        out int offset)
    {
        if (frame < 0 || frame >= frameCount || interleaved.Length < channels)
        {
            offset = 0;
            return false;
        }

        offset = (int)Math.Clamp(frame * (long)channels, 0, interleaved.Length - channels);
        return true;
    }

    public static void Downmix(ReadOnlySpan<float> frame, out float left, out float right)
    {
        var channels = frame.Length;
        if (channels <= 1)
        {
            left = right = channels == 0 ? 0 : frame[0];
            return;
        }

        if (channels == 2)
        {
            left = frame[0];
            right = frame[1];
            return;
        }

        var l = frame[0];
        var r = frame[1];
        if (channels > 2)
        {
            var center = frame[2] * Center;
            l += center;
            r += center;
        }

        if (channels > 3)
        {
            var lfe = frame[3] * Center;
            l += lfe;
            r += lfe;
        }

        if (channels > 4)
        {
            l += frame[4] * Center;
        }

        if (channels > 5)
        {
            r += frame[5] * Center;
        }

        if (channels > 6)
        {
            l += frame[6] * Center;
        }

        if (channels > 7)
        {
            r += frame[7] * Center;
        }

        for (var ch = 8; ch < channels; ch++)
        {
            var extra = frame[ch] * 0.5f;
            if ((ch & 1) == 0)
            {
                l += extra;
            }
            else
            {
                r += extra;
            }
        }

        left = l;
        right = r;
    }
}
