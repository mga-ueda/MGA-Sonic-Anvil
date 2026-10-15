namespace MgaSonicAnvil.Domain;

internal static partial class UiStrings
{
    public static string DialogManualTitle => Get("マニュアル", "Manual");

    public static string ErrManualOpenFailed(string detail) => Format(
        "マニュアルを開けませんでした。\n{0}",
        "Could not open the manual.\n{0}",
        detail);
}
