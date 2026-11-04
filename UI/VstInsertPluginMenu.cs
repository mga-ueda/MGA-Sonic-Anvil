using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>VST 選択ツリーの手繰り方（メーカー／種別）。</summary>
internal enum VstInsertPickerGroupMode
{
    Vendor = 0,
    Category = 1,
}

/// <summary>
/// VST 選択のグルーピング／表示ヘルパ。
/// ウィンドウ本体は <see cref="VstInsertPluginPickerWindow"/>。
/// </summary>
internal static class VstInsertPluginMenu
{
    public const string GroupByVendorSetting = "vendor";
    public const string GroupByCategorySetting = "category";

    public static string[] CategorySegments(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        return category.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// 種別セグメント（先頭 Fx は除く。Fx だけのときは残す）。
    /// メーカー表示では入れ子パス、種別表示ではさらにフラット配置へ展開する。
    /// </summary>
    public static string[] CategoryTreeSegments(string? category)
    {
        var segments = CategorySegments(category);
        if (segments.Length > 1
            && segments[0].Equals("Fx", StringComparison.OrdinalIgnoreCase))
        {
            return segments[1..];
        }

        return segments;
    }

    /// <summary>
    /// メーカー表示用の入れ子パス1本。先頭 Fx を除いた種別階層をそのまま使う。
    /// 「 · 」が複数あるときは最初の種別だけ（同一プラグインの重複配置を避ける）。
    /// 例: Fx|Pitch Shift|Spatial → [Pitch Shift, Spatial]
    /// </summary>
    public static string[] CategoryTreeHierarchyPath(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        var first = category.Split(
            " · ",
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return CategoryTreeSegments(first);
    }

    /// <summary>
    /// 種別表示用。階層は「種別 → プラグイン」まで。
    /// 「 · 」区切りおよび '|' の各段を、それぞれ別の種別ノード直下へ載せる。
    /// 例: Fx|Pitch Shift|Spatial → [Pitch Shift]、[Spatial]
    /// </summary>
    public static IReadOnlyList<string[]> CategoryTreePaths(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        var paths = new List<string[]>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in category.Split(
                     " · ",
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in CategoryTreeSegments(part))
            {
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                {
                    continue;
                }

                paths.Add([name]);
            }
        }

        return paths;
    }

    public static string VendorKey(VstPickerEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Vendor)
            ? UiStrings.LabelVstInsertVendorUnknown
            : entry.Vendor.Trim();

    /// <summary>
    /// 検索ヒット。ライブラリと同じ構文（空白 AND、<c>|</c> OR、引用、* / ?）。
    /// プラグイン名は常に対象。メーカー表示ではメーカー名だけ、種別表示では種別ノード名だけを
    /// 追加で見る（交差しない。AND 語は対象フィールドをまたいでよい）。
    /// </summary>
    public static bool MatchesPickerFilter(
        VstPickerEntry plugin,
        IReadOnlyList<string[]> groups,
        VstInsertPickerGroupMode mode)
    {
        if (groups.Count == 0)
        {
            return true;
        }

        return TileSearchQuery.MatchesAny(PickerFilterTexts(plugin, mode), groups);
    }

    /// <summary>表示モードごとの照合テキスト（ライブラリの行テキスト相当）。</summary>
    public static IEnumerable<string> PickerFilterTexts(
        VstPickerEntry plugin,
        VstInsertPickerGroupMode mode)
    {
        yield return plugin.Name;
        if (mode == VstInsertPickerGroupMode.Vendor)
        {
            yield return VendorKey(plugin);
            yield break;
        }

        foreach (var label in CategoryFilterLabels(plugin.Category))
        {
            yield return label;
        }
    }

    /// <summary>種別ツリーに出るノード名（先頭 Fx を除いた各段）。</summary>
    public static IEnumerable<string> CategoryFilterLabels(string? category)
    {
        var paths = CategoryTreePaths(category);
        if (paths.Count == 0)
        {
            yield return UiStrings.LabelVstInsertCategoryUnknown;
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segments in paths)
        {
            foreach (var segment in segments)
            {
                if (seen.Add(segment))
                {
                    yield return segment;
                }
            }
        }
    }

    public static IReadOnlyList<IGrouping<string, VstPickerEntry>> GroupPickerByVendor(
        IEnumerable<VstPickerEntry> plugins) =>
        plugins
            .GroupBy(VendorKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static VstInsertPickerGroupMode ParseGroupMode(string? setting)
    {
        if (string.Equals(setting, GroupByCategorySetting, StringComparison.OrdinalIgnoreCase))
        {
            return VstInsertPickerGroupMode.Category;
        }

        return VstInsertPickerGroupMode.Vendor;
    }

    public static string ToGroupModeSetting(VstInsertPickerGroupMode mode) =>
        mode == VstInsertPickerGroupMode.Category
            ? GroupByCategorySetting
            : GroupByVendorSetting;

    public static double ResolveMaxHeight()
    {
        var max = DesignMetrics.VstInsertMenuMaxHeight;
        try
        {
            max = Math.Min(max, System.Windows.SystemParameters.WorkArea.Height * 0.85);
        }
        catch
        {
        }

        return Math.Max(240, max);
    }
}
