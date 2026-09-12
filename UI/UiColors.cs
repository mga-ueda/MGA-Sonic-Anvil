using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>アプリ色の実行時ソース。XAML 既定をスナップし、設定を上書きする。</summary>
internal static class UiColors
{
    private static readonly Dictionary<string, Color> Defaults = new(StringComparer.OrdinalIgnoreCase);
    private static UiColorEntry[] _entries = [];

    public static IReadOnlyList<UiColorEntry> Entries => _entries;

    public static void Load()
    {
        CaptureDefaults();
        var values = AppStorage.Settings.Colors;
        if (values is null || values.Count == 0)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (values.TryGetValue(entry.Key, out var text) && TryParseColor(text, out var color))
            {
                var alpha = GetDefaultAlpha(entry.Key);
                Set(entry.Key, Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }
    }

    public static void Save()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            var color = entry.Get();
            var alpha = GetDefaultAlpha(entry.Key);
            var normalized = Color.FromArgb(alpha, color.R, color.G, color.B);
            entry.Set(normalized);
            values[entry.Key] = FormatColor(normalized);
        }

        AppStorage.Settings.Colors = values;
        AppStorage.Save();
    }

    public static void ResetToDefaults()
    {
        foreach (var (key, color) in Defaults)
        {
            Set(key, color);
        }

        UiThemePalette.Apply(UiThemeService.Current);
    }

    public static Color Default(string key) =>
        Defaults.TryGetValue(key, out var color) ? color : Get(key);

    public static Color Get(string key) => Theme.Get(key);

    public static void Set(string key, Color color)
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (TryWrite(app.Resources, key, color))
        {
            return;
        }

        app.Resources[key] = new SolidColorBrush(color);
    }

    /// <summary>
    /// 既存の <see cref="SolidColorBrush"/> を同じインスタンスのまま塗り替える。
    /// DynamicResource と FindResource 済みの参照が、差し替えなしで追従する。
    /// </summary>
    internal static bool TryWrite(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary.Contains(key) && dictionary[key] is SolidColorBrush brush)
        {
            if (!brush.IsFrozen)
            {
                if (brush.Color != color)
                {
                    brush.Color = color;
                }

                return true;
            }

            var copy = brush.Clone();
            copy.Color = color;
            try
            {
                dictionary[key] = copy;
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (TryWrite(merged, key, color))
            {
                return true;
            }
        }

        return false;
    }

    public static byte GetDefaultAlpha(string key) =>
        Defaults.TryGetValue(key, out var color) ? color.A : (byte)255;

    public static string FormatColor(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

    public static bool TryParseColor(string text, out Color color)
    {
        color = default;
        var hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }

        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        color = hex.Length == 6
            ? Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public static SolidColorBrush Brush(Color color) => WpfControlHelpers.FrozenBrush(color);

    private static void CaptureDefaults()
    {
        Defaults.Clear();
        if (Application.Current is { } app)
        {
            Collect(app.Resources, Defaults);
        }

        _entries = Defaults.Keys
            .OrderBy(key => key, StringComparer.OrdinalIgnoreCase)
            .Select(key => new UiColorEntry(key))
            .ToArray();
    }

    private static void Collect(ResourceDictionary dictionary, Dictionary<string, Color> dest)
    {
        foreach (var merged in dictionary.MergedDictionaries)
        {
            Collect(merged, dest);
        }

        foreach (var keyObj in dictionary.Keys)
        {
            if (keyObj is string key && dictionary[keyObj] is SolidColorBrush brush)
            {
                dest.TryAdd(key, brush.Color);
            }
        }
    }
}

internal sealed class UiColorEntry
{
    public UiColorEntry(string key) => Key = key;

    public string Key { get; }

    public string Label => UiStrings.ColorLabel(Key);

    public Color Get() => UiColors.Get(Key);

    public void Set(Color color) => UiColors.Set(Key, color);
}
