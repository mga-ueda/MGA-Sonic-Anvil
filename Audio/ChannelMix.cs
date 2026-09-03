namespace MgaSonicAnvil.Audio;

/// <summary>多チャンネルを再生用の L/R に畳む。文書上のチャンネル数はそのまま残す。</summary>
internal static class ChannelMix
{
    private const float Center = 0.70710677f;

    public static void Downmix(float[] interleaved, int offset, int channels, out float left, out float right)
    {
        if (channels <= 1)
        {
            left = right = interleaved[offset];
            return;
        }

        if (channels == 2)
        {
            left = interleaved[offset];
            right = interleaved[offset + 1];
            return;
        }

        var l = interleaved[offset];
        var r = interleaved[offset + 1];
        if (channels > 2)
        {
            var center = interleaved[offset + 2] * Center;
            l += center;
            r += center;
        }

        if (channels > 3)
        {
            var lfe = interleaved[offset + 3] * Center;
            l += lfe;
            r += lfe;
        }

        if (channels > 4)
        {
            l += interleaved[offset + 4] * Center;
        }

        if (channels > 5)
        {
            r += interleaved[offset + 5] * Center;
        }

        if (channels > 6)
        {
            l += interleaved[offset + 6] * Center;
        }

        if (channels > 7)
        {
            r += interleaved[offset + 7] * Center;
        }

        for (var ch = 8; ch < channels; ch++)
        {
            var extra = interleaved[offset + ch] * 0.5f;
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
