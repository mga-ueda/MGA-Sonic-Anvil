namespace MgaSonicAnvil.Config;

/// <summary>
/// ファイルを渡さず、前回ドキュメントも復元しない起動で入る画面。空文字はエディタ。
/// エディタで開いていたファイルを復元するときは使わない。
/// </summary>
internal enum AppLaunchMode
{
    Editor,
    Player,
    PlayerCompact,
    Waveform,
    Analyzers,
}

internal static class AppLaunchModes
{
    public static AppLaunchMode Parse(string? stored) =>
        stored?.Trim().ToLowerInvariant() switch
        {
            "player" => AppLaunchMode.Player,
            "compact" => AppLaunchMode.PlayerCompact,
            "waveform" => AppLaunchMode.Waveform,
            "analyzers" => AppLaunchMode.Analyzers,
            _ => AppLaunchMode.Editor,
        };

    public static string Format(AppLaunchMode mode) =>
        mode switch
        {
            AppLaunchMode.Player => "player",
            AppLaunchMode.PlayerCompact => "compact",
            AppLaunchMode.Waveform => "waveform",
            AppLaunchMode.Analyzers => "analyzers",
            _ => "editor",
        };

    public static bool HoldsReveal(AppLaunchMode mode) => mode != AppLaunchMode.Editor;
}
