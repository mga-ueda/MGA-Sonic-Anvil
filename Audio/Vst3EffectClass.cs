namespace MgaSonicAnvil.Audio;

/// <summary>
/// 工場／moduleinfo／ホストキャッシュで「使える音のクラス」かを判定する。
/// Softube の Tape Multi Track は Audio Module Class ではなく Audio Mix Processor。
/// </summary>
internal static class Vst3EffectClass
{
    /// <summary>コントローラや互換クラスなど、種別の候補にしない。</summary>
    public static bool IsSkippableCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return true;
        }

        return category.Contains("Controller", StringComparison.OrdinalIgnoreCase)
            || category.Contains("Compatibility", StringComparison.OrdinalIgnoreCase)
            || category.Contains("Service", StringComparison.OrdinalIgnoreCase)
            || category.Contains("Preset", StringComparison.OrdinalIgnoreCase)
            || category.Contains("ARA Main Factory", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>subCategories があり、スキップ対象でないクラス。</summary>
    public static bool IsUsableEffectClass(string? category, string? subCategories) =>
        !string.IsNullOrWhiteSpace(subCategories) && !IsSkippableCategory(category);

    /// <summary>
    /// 複数クラスの種別を 1 つにまとめる。細分類（'|' あり）を先に、Instrument を Fx より先に。
    /// </summary>
    public static string MergeSubCategories(IEnumerable<string?> values)
    {
        var parts = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(Score)
            .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return parts.Length == 0 ? string.Empty : string.Join(" · ", parts);
    }

    /// <summary>名前選び。Audio Module Class を優先し、無ければ最初。</summary>
    public static int NamePreference(string? category) =>
        category is not null && category.Contains("Audio Module", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;

    private static int Score(string value)
    {
        var score = 0;
        if (!value.Contains('|'))
        {
            score += 10;
        }

        if (value.Equals("Fx", StringComparison.OrdinalIgnoreCase)
            || value.Equals("FX", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
        }
        else if (value.Equals("Instrument", StringComparison.OrdinalIgnoreCase))
        {
            score += 1;
        }

        return score;
    }
}
