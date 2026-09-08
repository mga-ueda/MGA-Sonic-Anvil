namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string LabelTips => Get(
        "Tips (click to lock/unlock)",
        "Tips (click to lock/unlock)");

    public static string LabelTipsPinned => Get("Tips (Locked)", "Tips (Locked)");

    public static string TipTips => Get(
        "ポインタを置いた操作の説明です。見出しや本文をクリックでロックし、Locked 中はもう一度クリックで解除します。ロック中とパネル上では内容が変わりません。",
        "Description of the control under the pointer. Click the heading or body to lock; click again while Locked to unlock. The text does not change while locked or while the pointer is on the panel.");

    public static string TipTipsToggle => Get(
        "Tips 枠の表示をオン／オフします。",
        "Turn the Tips panel on or off.");

    public static string TipManualHelp => Get(
        "ユーザーマニュアル（GitHub Pages）をブラウザで開きます（表示言語に合わせて日本語／英語）。",
        "Open the user manual on GitHub Pages in your browser (Japanese or English matching the UI language).");

    public static string DialogManualTitle => Get("マニュアル", "Manual");

    public static string ErrManualOpenFailed(string detail) => Format(
        "マニュアルを開けませんでした。\n{0}",
        "Could not open the manual.\n{0}",
        detail);
}
