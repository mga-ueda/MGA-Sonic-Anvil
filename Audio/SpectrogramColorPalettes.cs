namespace MgaSonicAnvil.Audio;

/// <summary>スペクトログラムの色調。既定は青系。</summary>
internal enum SpectrogramColorPalette
{
    Blue,
    Red,
}

/// <summary>設定文字列と <see cref="SpectrogramColorPalette"/> の相互変換。</summary>
internal static class SpectrogramColorPalettes
{
    public static SpectrogramColorPalette Default => SpectrogramColorPalette.Blue;

    public static SpectrogramColorPalette Parse(string? stored) =>
        string.Equals(stored?.Trim(), "red", StringComparison.OrdinalIgnoreCase)
            ? SpectrogramColorPalette.Red
            : SpectrogramColorPalette.Blue;

    public static string Format(SpectrogramColorPalette palette) =>
        palette == SpectrogramColorPalette.Red ? "red" : "blue";
}
