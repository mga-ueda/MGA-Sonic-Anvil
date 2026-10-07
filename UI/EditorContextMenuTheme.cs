using System.Windows;
using System.Windows.Controls;

namespace MgaSonicAnvil.UI;

/// <summary>
/// TextBox 既定の右クリックメニューは内部型（EditorContextMenu / EditorMenuItem）なので、
/// TargetType=ContextMenu / MenuItem の暗黙スタイルが当たらない。同じ見た目を橋渡しする。
/// </summary>
internal static class EditorContextMenuTheme
{
    internal const string ContextMenuTypeName =
        "System.Windows.Documents.TextEditorContextMenu+EditorContextMenu";

    internal static readonly string[] MenuItemTypeNames =
    [
        "System.Windows.Documents.TextEditorContextMenu+EditorMenuItem",
        "System.Windows.Documents.TextEditorContextMenu+ReconversionMenuItem",
    ];

    public static void Apply(ResourceDictionary resources)
    {
        var assembly = typeof(FrameworkElement).Assembly;
        if (resources["DarkContextMenuStyle"] is Style menuStyle)
        {
            Bridge(resources, assembly, ContextMenuTypeName, menuStyle);
        }

        if (resources["DarkMenuItemStyle"] is Style itemStyle)
        {
            foreach (var name in MenuItemTypeNames)
            {
                Bridge(resources, assembly, name, itemStyle);
            }
        }
    }

    internal static Type? Resolve(string typeName) =>
        typeof(FrameworkElement).Assembly.GetType(typeName);

    private static void Bridge(
        ResourceDictionary resources,
        System.Reflection.Assembly assembly,
        string typeName,
        Style basedOn)
    {
        var type = assembly.GetType(typeName);
        if (type is null || resources.Contains(type))
        {
            return;
        }

        resources[type] = new Style(type, basedOn);
    }
}
