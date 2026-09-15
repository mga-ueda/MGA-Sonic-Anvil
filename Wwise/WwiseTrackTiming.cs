using System.Globalization;

namespace MgaSonicAnvil.Wwise;

/// <summary>
/// Wwise EXPORT の Music Track ストリーム設定。
/// Stream は常にオン（UI なし）。Prefetch は先頭（Zero Latency）、Look-ahead は 2 本目以降。
/// </summary>
internal static class WwiseTrackTiming
{
    public const int DefaultPrefetchLengthMs = 500;
    public const int DefaultLookAheadTimeMs = 500;
    public const int FirstSegmentLookAheadMs = 50;
    public const int MinMs = 0;
    public const int MaxMs = 10000;

    public static int ClampPrefetchLengthMs(int value) => Math.Clamp(value, MinMs, MaxMs);

    public static int ClampLookAheadTimeMs(int value) => Math.Clamp(value, MinMs, MaxMs);

    public static bool TryParsePrefetchLengthMs(string? text, out int ms) =>
        TryParseMs(text, DefaultPrefetchLengthMs, out ms);

    public static bool TryParseLookAheadTimeMs(string? text, out int ms) =>
        TryParseMs(text, DefaultLookAheadTimeMs, out ms);

    public static int ResolveLookAheadTimeMs(bool isFirst, int lookAheadTimeMs) =>
        isFirst ? FirstSegmentLookAheadMs : ClampLookAheadTimeMs(lookAheadTimeMs);

    public static void WriteTrackProperties(
        IDictionary<string, object?> track,
        bool isFirst,
        int prefetchLengthMs,
        int lookAheadTimeMs)
    {
        track["@IsStreamingEnabled"] = true;
        track["@IsZeroLatency"] = isFirst;
        track["@LookAheadTime"] = ResolveLookAheadTimeMs(isFirst, lookAheadTimeMs);
        if (isFirst)
        {
            track["@PreFetchLength"] = ClampPrefetchLengthMs(prefetchLengthMs);
        }
    }

    private static bool TryParseMs(string? text, int fallback, out int ms)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            ms = fallback;
            return false;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            ms = fallback;
            return false;
        }

        if (parsed < MinMs || parsed > MaxMs)
        {
            ms = fallback;
            return false;
        }

        ms = parsed;
        return true;
    }
}
