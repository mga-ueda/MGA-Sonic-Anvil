namespace MgaSonicAnvil.Domain;

internal enum UiTheme
{
    Dark,
    Light,
}

internal enum UiThemeChoice
{
    Auto,
    Dark,
    Light,
}

internal static class UiThemes
{
    public static UiThemeChoice ParseChoice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UiThemeChoice.Auto;
        }

        var trimmed = value.Trim();
        if (trimmed.Equals("auto", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("os", StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeChoice.Auto;
        }

        if (trimmed.Equals("light", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(nameof(UiTheme.Light), StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeChoice.Light;
        }

        if (trimmed.Equals("dark", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(nameof(UiTheme.Dark), StringComparison.OrdinalIgnoreCase))
        {
            return UiThemeChoice.Dark;
        }

        return UiThemeChoice.Auto;
    }

    public static UiTheme Resolve(UiThemeChoice choice, bool osLight)
    {
        if (choice == UiThemeChoice.Light)
        {
            return UiTheme.Light;
        }

        if (choice == UiThemeChoice.Dark)
        {
            return UiTheme.Dark;
        }

        return osLight ? UiTheme.Light : UiTheme.Dark;
    }

    public static string ToStoredValue(UiThemeChoice choice) =>
        choice switch
        {
            UiThemeChoice.Light => "light",
            UiThemeChoice.Dark => "dark",
            _ => "auto",
        };

    /// <summary>解決済みの配色を反転する。Auto は外れる。</summary>
    public static UiThemeChoice ToggledChoice(UiTheme current) =>
        current == UiTheme.Light ? UiThemeChoice.Dark : UiThemeChoice.Light;
}
