namespace MgaSonicAnvil.Audio;

/// <summary>RGB。WPF に依存しない。</summary>
internal readonly record struct ChannelRgb(byte R, byte G, byte B);

/// <summary>
/// よくあるチャンネル色の登場順。9.1.6（Atmos）の 16 本。
/// 3ch 以上のときだけ、波形レーン番号と同じ順で名前・波形・レベルメーターに使う。
/// </summary>
internal static class ChannelColors
{
    public static int Count => Order.Length;

    /// <summary>モノ／ステレオは着色しない。</summary>
    public static bool UsesLaneTint(int channels) => channels > 2;

    // 虹の登場順のあと、同じ順で少し明るい 2 周目。
    private static readonly int[] Order =
    [
        0xF25B5B, // 赤
        0xF08A32, // 橙
        0xE8C83C, // 黄
        0x4EC86A, // 緑
        0x3EC8E8, // シアン
        0x5B8CFF, // 青
        0xA86CFF, // 紫
        0xE85CAA, // マゼンタ
        0xE88888, // 赤 2
        0xE8B078, // 橙 2
        0xE8E088, // 黄 2
        0x88D8A0, // 緑 2
        0x88D8E8, // シアン 2
        0xA0B8FF, // 青 2
        0xC8A8FF, // 紫 2
        0xF0A0C8, // マゼンタ 2
    ];

    public static int Index(int channel)
    {
        var n = Order.Length;
        return ((channel % n) + n) % n;
    }

    public static ChannelRgb At(int channel) => FromPacked(Order[Index(channel)]);

    /// <summary>ソロでミュートしたレーンの四角。色相は残して暗くする。</summary>
    public static ChannelRgb Dim(int channel)
    {
        var c = At(channel);
        return new ChannelRgb(Fade(c.R), Fade(c.G), Fade(c.B));
    }

    private static ChannelRgb FromPacked(int rgb) =>
        new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    private static byte Fade(byte value) => (byte)(0x38 + value * 96 / 255);
}
