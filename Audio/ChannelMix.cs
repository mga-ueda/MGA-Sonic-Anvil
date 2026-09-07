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
