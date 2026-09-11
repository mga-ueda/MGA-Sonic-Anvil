namespace MgaSonicAnvil.Audio;

/// <summary>波形レーンのソロ。0 は解除（全チャンネル）。bit i がそのレーン。</summary>
internal static class ChannelSolo
{
    public const int Off = -1;

    public static int Next(int current, int channels) => Step(current, channels, 1);

    public static int Previous(int current, int channels) => Step(current, channels, -1);

    public static int Step(int current, int channels, int delta)
    {
        channels = Math.Max(1, channels);
        if (delta == 0)
        {
            return Clamp(current, channels);
        }

        var count = channels + 1;
        var pos = current < 0 ? 0 : current + 1;
        pos = ((pos + delta) % count + count) % count;
        return pos == 0 ? Off : pos - 1;
    }

    public static int Clamp(int current, int channels)
    {
        channels = Math.Max(1, channels);
        return (uint)current >= (uint)channels ? Off : current;
    }

    public static int AllBits(int channels)
    {
        channels = Math.Clamp(channels, 0, 30);
        return channels <= 0 ? 0 : (1 << channels) - 1;
    }

    public static int MaskOf(int channel) => channel < 0 ? 0 : 1 << channel;

    public static int ClampMask(int mask, int channels)
    {
        var bits = mask & AllBits(channels);
        return bits == 0 || bits == AllBits(channels) ? 0 : bits;
    }

    public static int ResolveMask(int channel, int channelMask, int channels)
    {
        if (channelMask != 0)
        {
            return ClampMask(channelMask, channels);
        }

        return MaskOf(Clamp(channel, channels));
    }

    public static bool Contains(int mask, int channel) =>
        mask == 0 || ((mask >> channel) & 1) != 0;

    public static int Primary(int mask)
    {
        if (mask == 0)
        {
            return Off;
        }

        for (var i = 0; i < 31; i++)
        {
            if (((mask >> i) & 1) != 0)
            {
                return i;
            }
        }

        return Off;
    }

    public static int Count(int mask)
    {
        var n = 0;
        for (var bits = mask; bits != 0; bits &= bits - 1)
        {
            n++;
        }

        return n;
    }

    /// <summary>クリックは単独ソロ（同じレーン再クリックで解除）。Ctrl+クリックは加減算。</summary>
    public static int Toggle(int mask, int channel, int channels, bool add)
    {
        channels = Math.Max(1, channels);
        if ((uint)channel >= (uint)channels)
        {
            return ClampMask(mask, channels);
        }

        var bit = 1 << channel;
        if (add)
        {
            return ClampMask(mask ^ bit, channels);
        }

        return ClampMask(mask == bit ? 0 : bit, channels);
    }

    /// <summary>Shift+クリックはミュート。聞こえている集合から外す／戻す。0 は全チャンネル。</summary>
    public static int Mute(int mask, int channel, int channels)
    {
        channels = Math.Max(1, channels);
        if ((uint)channel >= (uint)channels)
        {
            return ClampMask(mask, channels);
        }

        var audible = mask == 0 ? AllBits(channels) : mask;
        return ClampMask(audible ^ (1 << channel), channels);
    }

    public static int StepMask(int mask, int channels, int delta) =>
        MaskOf(Step(Primary(mask), channels, delta));
}
