namespace MgaSonicAnvil.Audio;

/// <summary>
/// 論理チャンネル ↔ ハードポートの割り当て。
/// map[論理ch] = ポート番号。負値は無音／未使用。
/// </summary>
internal static class ChannelRouter
{
    public const int Off = -1;

    public static bool IsEmpty(int[]? map) => map is not { Length: > 0 };

    /// <summary>空なら恒等。長さは論理チャンネル数。</summary>
    public static int[] Normalize(int[]? map, int logicalCount, int portCount)
    {
        logicalCount = Math.Clamp(logicalCount, 0, ChannelLayout.MaxChannels);
        var result = new int[logicalCount];
        for (var i = 0; i < logicalCount; i++)
        {
            var port = map is not null && i < map.Length ? map[i] : i;
            result[i] = port < 0 || port >= portCount ? Off : port;
        }

        return result;
    }

    public static int[] Identity(int logicalCount)
    {
        logicalCount = Math.Clamp(logicalCount, 0, ChannelLayout.MaxChannels);
        var map = new int[logicalCount];
        for (var i = 0; i < logicalCount; i++)
        {
            map[i] = i;
        }

        return map;
    }

    /// <summary>2ch 以下の出力で、明示マップが無い多ch は従来どおりダウンミックスする。</summary>
    public static bool ShouldDownmix(int sourceChannels, int destPorts, int[]? map) =>
        destPorts <= 2 && sourceChannels > 2 && IsEmpty(map);

    /// <summary>入力ポート → 論理チャンネル。dest[i] = source[map[i]]。</summary>
    public static void Gather(ReadOnlySpan<float> source, Span<float> dest, ReadOnlySpan<int> map)
    {
        dest.Clear();
        var n = Math.Min(dest.Length, map.Length);
        for (var i = 0; i < n; i++)
        {
            var port = map[i];
            if ((uint)port < (uint)source.Length)
            {
                dest[i] = source[port];
            }
        }
    }

    /// <summary>論理チャンネル → 出力ポート。同じポートへ重なったら加算。</summary>
    public static void Scatter(ReadOnlySpan<float> source, Span<float> dest, ReadOnlySpan<int> map)
    {
        dest.Clear();
        var n = Math.Min(source.Length, map.Length);
        for (var i = 0; i < n; i++)
        {
            var port = map[i];
            if ((uint)port < (uint)dest.Length)
            {
                dest[port] += source[i];
            }
        }
    }

    public static float[] MapInterleaved(
        float[] source,
        int sourceChannels,
        int destChannels,
        int[] map,
        bool gather)
    {
        sourceChannels = Math.Max(1, sourceChannels);
        destChannels = Math.Max(1, destChannels);
        var frames = source.Length / sourceChannels;
        var dest = new float[frames * destChannels];
        for (var frame = 0; frame < frames; frame++)
        {
            var src = source.AsSpan(frame * sourceChannels, sourceChannels);
            var dst = dest.AsSpan(frame * destChannels, destChannels);
            if (gather)
            {
                Gather(src, dst, map);
            }
            else
            {
                Scatter(src, dst, map);
            }
        }

        return dest;
    }
}
