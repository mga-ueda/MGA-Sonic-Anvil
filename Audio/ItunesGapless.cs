using System.Globalization;

namespace MgaSonicAnvil.Audio;

/// <summary>iTunes の iTunSMPB（delay／padding／元の長さ）。LAME が無いときのギャップレス用。</summary>
internal static class ItunesGapless
{
    public const string SmpbName = "iTunSMPB";

    public static bool IsHiddenComment(string description) =>
        description.StartsWith("iTun", StringComparison.OrdinalIgnoreCase);

    public static bool TryApply(AudioFileTagsBuilder builder, string description, string text, bool overwrite)
    {
        if (!description.Equals(SmpbName, StringComparison.OrdinalIgnoreCase)
            || !TryParse(text, out var delay, out var padding, out var original))
        {
            return false;
        }

        if (overwrite || (builder.EncoderDelayFrames <= 0 && builder.EncoderPaddingFrames <= 0))
        {
            builder.EncoderDelayFrames = delay;
            builder.EncoderPaddingFrames = padding;
        }

        if (original > 0 && (overwrite || builder.EncoderOriginalFrames <= 0))
        {
            builder.EncoderOriginalFrames = original;
        }

        return true;
    }

    public static bool TryParse(string text, out int delay, out int padding, out long original)
    {
        delay = 0;
        padding = 0;
        original = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 4
            || !TryParseHex(parts[1], out var delayRaw)
            || !TryParseHex(parts[2], out var paddingRaw)
            || !TryParseHex64(parts[3], out var originalRaw))
        {
            return false;
        }

        if (delayRaw > int.MaxValue || paddingRaw > int.MaxValue || originalRaw > long.MaxValue)
        {
            return false;
        }

        delay = (int)delayRaw;
        padding = (int)paddingRaw;
        original = (long)originalRaw;
        return delay > 0 || padding > 0;
    }

    private static bool TryParseHex(string text, out uint value) =>
        uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    private static bool TryParseHex64(string text, out ulong value) =>
        ulong.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
}
