namespace MgaSonicAnvil.UI;

/// <summary>色パネルのグループ分けと検索。</summary>
internal static class ColorDevCatalog
{
    public static string GroupOf(string label)
    {
        var mid = label.IndexOf('・');
        if (mid > 0)
        {
            return label[..mid].Trim();
        }

        const string english = " · ";
        var index = label.IndexOf(english, StringComparison.Ordinal);
        return index > 0 ? label[..index].Trim() : label;
    }

    public static bool Matches(string label, string key, string hex, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var q = query.Trim();
        return label.Contains(q, StringComparison.OrdinalIgnoreCase)
            || key.Contains(q, StringComparison.OrdinalIgnoreCase)
            || hex.Contains(q, StringComparison.OrdinalIgnoreCase);
    }
}
