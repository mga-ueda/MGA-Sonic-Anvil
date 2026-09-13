using System.Text.Json;
using System.Text.Json.Serialization;

namespace MgaSonicAnvil.UI;

/// <summary>ライト／ダークの配色ファイル。</summary>
internal sealed class UiColorScheme
{
    public const string FormatId = "MgaSonicAnvil.ColorScheme";
    public const int CurrentVersion = 1;

    public Dictionary<string, string>? Light { get; init; }

    public Dictionary<string, string>? Dark { get; init; }

    public Dictionary<string, string>? Flat { get; init; }

    public static string Write(UiColorScheme scheme)
    {
        var dto = new UiColorSchemeDto
        {
            Format = FormatId,
            Version = CurrentVersion,
            Light = scheme.Light ?? [],
            Dark = scheme.Dark ?? [],
        };
        return JsonSerializer.Serialize(dto, UiColorSchemeJsonContext.Default.UiColorSchemeDto);
    }

    public static bool TryRead(string json, out UiColorScheme scheme)
    {
        scheme = new UiColorScheme();
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetProperty(root, "Format", out var format)
                && format.ValueKind == JsonValueKind.String
                && format.GetString() is { } id
                && !id.Equals(FormatId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var light = ReadMap(root, "Light") ?? ReadMap(root, "ColorsLight");
            var dark = ReadMap(root, "Dark") ?? ReadMap(root, "ColorsDark");
            var flat = ReadMap(root, "Colors") ?? ReadFlatRoot(root, light, dark);
            if (light is null && dark is null && flat is null)
            {
                return false;
            }

            scheme = new UiColorScheme
            {
                Light = light,
                Dark = dark,
                Flat = flat,
            };
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static Dictionary<string, string>? ReadMap(JsonElement root, string name)
    {
        if (!TryGetProperty(root, name, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in node.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String
                && property.Value.GetString() is { Length: > 0 } text)
            {
                map[property.Name] = text;
            }
        }

        return map;
    }

    private static Dictionary<string, string>? ReadFlatRoot(
        JsonElement root,
        Dictionary<string, string>? light,
        Dictionary<string, string>? dark)
    {
        if (light is not null || dark is not null)
        {
            return null;
        }

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in root.EnumerateObject())
        {
            if (IsReservedName(property.Name)
                || property.Value.ValueKind != JsonValueKind.String
                || property.Value.GetString() is not { Length: > 0 } text)
            {
                continue;
            }

            if (property.Name.EndsWith("Brush", StringComparison.OrdinalIgnoreCase)
                || LooksLikeHex(text))
            {
                map[property.Name] = text;
            }
        }

        return map.Count == 0 ? null : map;
    }

    private static bool TryGetProperty(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool IsReservedName(string name) =>
        name.Equals("Format", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Version", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Light", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Dark", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Colors", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ColorsLight", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ColorsDark", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeHex(string text)
    {
        var hex = text.StartsWith('#') ? text[1..] : text;
        return hex.Length is 3 or 6 or 8
            && hex.All(static ch => Uri.IsHexDigit(ch));
    }
}

internal sealed class UiColorSchemeDto
{
    public string Format { get; set; } = UiColorScheme.FormatId;

    public int Version { get; set; } = UiColorScheme.CurrentVersion;

    public Dictionary<string, string> Light { get; set; } = [];

    public Dictionary<string, string> Dark { get; set; } = [];
}

[JsonSerializable(typeof(UiColorSchemeDto))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class UiColorSchemeJsonContext : JsonSerializerContext;
