namespace MgaSonicAnvil.Audio;

/// <summary>範囲を時間方向に反転する。チャンネルは入れ替えない。</summary>
internal static class Reverse
{
    public static float[] Apply(float[] interleaved, int channels)
    {
        var copy = (float[])interleaved.Clone();
        ReverseInPlace(copy, channels);
        return copy;
    }

    public static void ReverseInPlace(float[] interleaved, int channels)
    {
        channels = Math.Max(1, channels);
        var frames = interleaved.Length / channels;
        if (frames < 2)
        {
            return;
        }

        for (var i = 0; i < frames / 2; i++)
        {
            var j = frames - 1 - i;
            var left = i * channels;
            var right = j * channels;
            for (var channel = 0; channel < channels; channel++)
            {
                (interleaved[left + channel], interleaved[right + channel]) =
                    (interleaved[right + channel], interleaved[left + channel]);
            }
        }
    }
}
