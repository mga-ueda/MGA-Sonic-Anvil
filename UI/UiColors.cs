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
        MigrateLegacyColors(AppStorage.Settings);
        MigrateLightColors(AppStorage.Settings);
    }

    public static void ApplySaved(UiTheme theme)
    {
        foreach (var entry in _entries)
        {
            if (!UiThemePalette.IsThemeable(entry.Key))
            {
                Set(entry.Key, Default(entry.Key));
            }
        }

        ApplyMap(OverridesOf(AppStorage.Settings, theme), theme);
    }

    public static void Save()
    {
        var theme = UiThemeService.Painted;
        var current = new List<(string Key, Color Value)>(_entries.Length);
        foreach (var entry in _entries)
        {
            var color = entry.Get();
            current.Add((entry.Key, color));
        }

        SetOverrides(AppStorage.Settings, theme, CollectOverrides(current, key => DefaultFor(theme, key)));
        AppStorage.Save();
    }

    public static void ResetToDefaults()
    {
        var theme = UiThemeService.Painted;
        SetOverrides(AppStorage.Settings, theme, []);
        foreach (var (key, color) in Defaults)
        {
            Set(key, color);
        }

        UiThemePalette.Apply(UiThemeService.Painted);
        ApplySaved(UiThemeService.Painted);
    }

    public static UiColorScheme CaptureScheme() =>
        new()
        {
            Dark = Snapshot(UiTheme.Dark),
        };

    public static void ApplyScheme(UiColorScheme scheme)
    {
        var current = UiThemeService.Painted;

        // ダーク固定なので、取り込みはすべてダークの一つの袋へまとめる。
        // 旧ファイルの Light / 旧フラット袋も互換で読み、ダークへ合成する。
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hasBag = false;
        if (scheme.Light is not null)
        {
            hasBag = true;
            foreach (var (key, value) in scheme.Light)
            {
                merged[key] = value;
            }
        }

        if (scheme.Dark is not null)
        {
            hasBag = true;
            foreach (var (key, value) in scheme.Dark)
            {
                merged[key] = value;
            }
        }

        if (!hasBag && scheme.Flat is not null)
        {
            hasBag = true;
            foreach (var (key, value) in scheme.Flat)
            {
                merged[key] = value;
            }
        }

        if (hasBag)
        {
            ImportBag(UiTheme.Dark, merged);
        }

        foreach (var (key, color) in Defaults)
        {
            Set(key, color);
        }

        UiThemePalette.Apply(current);
        ApplySaved(current);
    }

    public static Dictionary<string, string> Snapshot(UiTheme theme)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            map[entry.Key] = FormatColor(Resolve(theme, entry.Key));
        }

        return map;
    }

    internal static Color Resolve(UiTheme theme, string key)
    {
        var fallback = DefaultFor(theme, key);
        if (theme == UiThemeService.Painted)
        {
            return Get(key);
        }

        var bag = OverridesOf(AppStorage.Settings, theme);
        if (bag is not null && bag.TryGetValue(key, out var text) && TryParseColor(text, out var parsed))
        {
            return WithParsedAlpha(parsed, text, fallback.A);
        }

        return fallback;
    }

    private static void ImportBag(UiTheme theme, Dictionary<string, string> colors)
    {
        var current = new List<(string Key, Color Value)>(colors.Count);
        foreach (var (key, text) in colors)
        {
            if (!ColorDevCatalog.Keys.Contains(key, StringComparer.OrdinalIgnoreCase)
                || !TryParseColor(text, out var color))
            {
                continue;
            }

            var fallback = DefaultFor(theme, key);
            current.Add((key, WithParsedAlpha(color, text, fallback.A)));
        }

        SetOverrides(AppStorage.Settings, theme, CollectOverrides(current, key => DefaultFor(theme, key)));
    }

    public static Color Default(string key) =>
        Defaults.TryGetValue(key, out var color) ? color : Get(key);

    /// <summary>いまの配色での既定。ライトはパレット、ダークは XAML スナップ。</summary>
    public static Color DefaultFor(UiTheme theme, string key) =>
        UiThemePalette.ColorFor(theme, key);

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
        DefaultFor(UiThemeService.Painted, key).A;

    public static string FormatColor(Color color) =>
        color.A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}");

    /// <summary>6 桁は不透明 RGB。8 桁はアルファ付き（#AARRGGBB）。</summary>
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

    /// <summary>古い 6 桁上書きは既定アルファを保ち、8 桁は指定アルファを使う。</summary>
    internal static Color WithParsedAlpha(Color parsed, string text, byte fallbackAlpha)
    {
        var hex = text.Trim();
        if (hex.StartsWith('#'))
        {
            hex = hex[1..];
        }

        return hex.Length == 8
            ? parsed
            : Color.FromArgb(fallbackAlpha, parsed.R, parsed.G, parsed.B);
    }

    public static SolidColorBrush Brush(Color color) => WpfControlHelpers.FrozenBrush(color);

    internal static Dictionary<string, string> CollectOverrides(
        IEnumerable<(string Key, Color Value)> current,
        Func<string, Color> defaultFor)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in current)
        {
            var fallback = defaultFor(key);
            if (value != fallback)
            {
                values[key] = FormatColor(value);
            }
        }

        return values;
    }

    internal static void MigrateLegacyColors(AppSettings settings)
    {
        if (settings.ColorsLight is not null || settings.ColorsDark is not null)
        {
            return;
        }

        if (settings.Colors is not { Count: > 0 })
        {
            return;
        }

        var shared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, text) in settings.Colors)
        {
            if (UiThemePalette.IsThemeable(key) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            shared[key] = text;
        }

        settings.Colors = null;
        settings.ColorsDark = shared.Count == 0
            ? []
            : new Dictionary<string, string>(shared, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 旧ライト袋を一度だけダークへ畳む。ダークが空ならそのまま採用し、
    /// 以後はライト袋を書かない。
    /// </summary>
    internal static void MigrateLightColors(AppSettings settings)
    {
        var light = settings.ColorsLight;
        if (light is null)
        {
            return;
        }

        if (light.Count > 0 && settings.ColorsDark is not { Count: > 0 })
        {
            settings.ColorsDark = new Dictionary<string, string>(light, StringComparer.OrdinalIgnoreCase);
        }

        settings.ColorsLight = null;
    }

    private static Dictionary<string, string>? OverridesOf(AppSettings settings, UiTheme theme)
    {
        _ = theme;
        return settings.ColorsDark;
    }

    private static void SetOverrides(AppSettings settings, UiTheme theme, Dictionary<string, string> values)
    {
        _ = theme;
        settings.ColorsDark = values;
    }

    private static void ApplyMap(Dictionary<string, string>? values, UiTheme theme)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        foreach (var entry in _entries)
        {
            if (!values.TryGetValue(entry.Key, out var text) || !TryParseColor(text, out var color))
            {
                continue;
            }

            var fallback = DefaultFor(theme, entry.Key);
            Set(entry.Key, WithParsedAlpha(color, text, fallback.A));
        }
    }

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
