namespace MgaSonicAnvil.Wwise;

/// <summary>
/// WAAPI 接続設定（アプリ内固定。ユーザー設定には書かない）。
/// </summary>
internal sealed class WaapiSettings
{
    public const string DefaultUrl = "http://127.0.0.1:8090/waapi";
    public const int DefaultTimeoutMs = 3000;

    public string Url { get; init; } = DefaultUrl;

    public int TimeoutMs { get; init; } = DefaultTimeoutMs;

    public static WaapiSettings Load() => new();
}
