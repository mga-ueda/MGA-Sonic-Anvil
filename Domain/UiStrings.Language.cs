namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string LanguageBadgeJapanese => Get("JP", "JP");
    public static string LanguageBadgeEnglish => Get("EN", "EN");

    public static string TipLanguageJapanese => Get(
        "現在: 日本語。クリックで英語に切り替えます。",
        "Current: Japanese. Click to switch to English.");

    public static string TipLanguageEnglish => Get(
        "現在: 英語。クリックで日本語に切り替えます。",
        "Current: English. Click to switch to Japanese.");

    public static string TipManualHelp => Get(
        "ユーザーマニュアル（GitHub Pages）をブラウザで開きます（表示言語に合わせて日本語／英語）。",
        "Open the user manual on GitHub Pages in your browser (Japanese or English matching the UI language).");

    public static string DialogManualTitle => Get("マニュアル", "Manual");

    public static string ErrManualOpenFailed(string detail) => Format(
        "マニュアルを開けませんでした。\n{0}",
        "Could not open the manual.\n{0}",
        detail);
}
