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
        UnifyPlayerOverridesFromDark(AppStorage.Settings);
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
            var alpha = DefaultFor(theme, entry.Key).A;
            var normalized = Color.FromArgb(alpha, color.R, color.G, color.B);
            entry.Set(normalized);
            current.Add((entry.Key, normalized));
        }

        SetOverrides(AppStorage.Settings, theme, CollectOverrides(current, key => DefaultFor(theme, key)));
        MirrorPlayerOverrides(AppStorage.Settings, theme);
        AppStorage.Save();
    }

    public static void ResetToDefaults()
    {
        var theme = UiThemeService.Painted;
        SetOverrides(AppStorage.Settings, theme, []);
        MirrorPlayerOverrides(AppStorage.Settings, theme);
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
            Light = Snapshot(UiTheme.Light),
            Dark = Snapshot(UiTheme.Dark),
        };

    public static void ApplyScheme(UiColorScheme scheme)
    {
        var current = UiThemeService.Painted;
        if (scheme.Light is not null)
        {
            ImportBag(UiTheme.Light, scheme.Light);
        }

        if (scheme.Dark is not null)
        {
            ImportBag(UiTheme.Dark, scheme.Dark);
        }

        if (scheme.Light is null && scheme.Dark is null && scheme.Flat is not null)
        {
            ImportBag(current, scheme.Flat);
        }

        if (scheme.Dark is not null)
        {
            UnifyPlayerOverridesFromDark(AppStorage.Settings);
        }
        else if (scheme.Light is not null || scheme.Flat is not null)
        {
            MirrorPlayerOverrides(AppStorage.Settings, scheme.Light is not null ? UiTheme.Light : current);
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
            var live = Get(key);
            return Color.FromArgb(fallback.A, live.R, live.G, live.B);
        }

        var bag = OverridesOf(AppStorage.Settings, theme);
        if (bag is not null && bag.TryGetValue(key, out var text) && TryParseColor(text, out var parsed))
        {
            return Color.FromArgb(fallback.A, parsed.R, parsed.G, parsed.B);
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

            current.Add((key, color));
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

    internal static Dictionary<string, string> CollectOverrides(
        IEnumerable<(string Key, Color Value)> current,
        Func<string, Color> defaultFor)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in current)
        {
            var fallback = defaultFor(key);
            var normalized = Color.FromArgb(fallback.A, value.R, value.G, value.B);
            if (normalized != fallback)
            {
                values[key] = FormatColor(normalized);
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
        if (shared.Count == 0)
        {
            settings.ColorsLight = [];
            settings.ColorsDark = [];
            return;
        }

        settings.ColorsLight = new Dictionary<string, string>(shared, StringComparer.OrdinalIgnoreCase);
        settings.ColorsDark = new Dictionary<string, string>(shared, StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string>? OverridesOf(AppSettings settings, UiTheme theme) =>
        theme == UiTheme.Light ? settings.ColorsLight : settings.ColorsDark;

    private static void SetOverrides(AppSettings settings, UiTheme theme, Dictionary<string, string> values)
    {
        if (theme == UiTheme.Light)
        {
            settings.ColorsLight = values;
            return;
        }

        settings.ColorsDark = values;
    }

    /// <summary>
    /// いま保存したテーマのプレイヤー色を、もう一方のテーマへ写す。
    /// プレイヤー色はライト／ダークで一つの値。
    /// </summary>
    internal static void MirrorPlayerOverrides(AppSettings settings, UiTheme source)
    {
        var from = OverridesOf(settings, source);
        var other = source == UiTheme.Light ? UiTheme.Dark : UiTheme.Light;
        var to = OverridesOf(settings, other);
        if (ReferenceEquals(from, to))
        {
            return;
        }

        var created = to is null;
        to ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        StripPlayerKeys(to);
        if (from is not null)
        {
            CopyPlayerKeys(from, to);
        }

        if (created && to.Count == 0)
        {
            return;
        }

        SetOverrides(settings, other, to);
    }

    /// <summary>既存の分かれをダーク側のプレイヤー色へ揃える。</summary>
    internal static void UnifyPlayerOverridesFromDark(AppSettings settings)
    {
        var dark = settings.ColorsDark;
        if (settings.ColorsLight is null)
        {
            if (dark is null)
            {
                return;
            }

            var created = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            CopyPlayerKeys(dark, created);
            if (created.Count > 0)
            {
                settings.ColorsLight = created;
            }

            return;
        }

        StripPlayerKeys(settings.ColorsLight);
        if (dark is null)
        {
            return;
        }

        CopyPlayerKeys(dark, settings.ColorsLight);
    }

    private static void CopyPlayerKeys(Dictionary<string, string> from, Dictionary<string, string> to)
    {
        foreach (var (key, value) in from)
        {
            if (ColorDevCatalog.IsPlayerShared(key))
            {
                to[key] = value;
            }
        }
    }

    private static void StripPlayerKeys(Dictionary<string, string> bag)
    {
        List<string>? drop = null;
        foreach (var key in bag.Keys)
        {
            if (ColorDevCatalog.IsPlayerShared(key))
            {
                (drop ??= []).Add(key);
            }
        }

        if (drop is null)
        {
            return;
        }

        foreach (var key in drop)
        {
            bag.Remove(key);
        }
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

            var alpha = DefaultFor(theme, entry.Key).A;
            Set(entry.Key, Color.FromArgb(alpha, color.R, color.G, color.B));
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
