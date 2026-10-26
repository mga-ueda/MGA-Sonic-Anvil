namespace MgaSonicAnvil.Config;

/// <summary>
/// ファイルを渡さず、前回ドキュメントも復元しない起動で入るレイアウト（F9–F12）。
/// 空／不明／旧 editor は既定の F12。旧 player／compact／waveform／analyzers も受け付ける。
/// エディタで開いていたファイルを復元するときは使わない。
/// </summary>
internal static class AppLaunchModes
{
    public static WindowLayoutSlot DefaultSlot => WindowLayoutSlot.F12;

    public static WindowLayoutSlot Parse(string? stored) =>
        TryParse(stored, out var slot) ? slot : DefaultSlot;

    /// <summary>空／不明は false（既定 F12 にはしない）。</summary>
    public static bool TryParse(string? stored, out WindowLayoutSlot slot)
    {
        switch (stored?.Trim().ToLowerInvariant())
        {
            case "f9" or "compact":
                slot = WindowLayoutSlot.F9;
                return true;
            case "f10" or "player":
                slot = WindowLayoutSlot.F10;
                return true;
            case "f11" or "waveform":
                slot = WindowLayoutSlot.F11;
                return true;
            case "f12" or "analyzers" or "editor":
                slot = WindowLayoutSlot.F12;
                return true;
            default:
                slot = DefaultSlot;
                return false;
        }
    }

    public static string Format(WindowLayoutSlot slot) =>
        slot switch
        {
            WindowLayoutSlot.F9 => "f9",
            WindowLayoutSlot.F10 => "f10",
            WindowLayoutSlot.F11 => "f11",
            _ => "f12",
        };

}
