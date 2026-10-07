using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class EditorContextMenuThemeTests
{
    [Fact]
    public void WpfEditorTypes_StillExist()
    {
        Assert.NotNull(EditorContextMenuTheme.Resolve(EditorContextMenuTheme.ContextMenuTypeName));
        Assert.All(
            EditorContextMenuTheme.MenuItemTypeNames,
            name => Assert.NotNull(EditorContextMenuTheme.Resolve(name)));
    }

    [Fact]
    public void Apply_BridgesDarkStylesOntoEditorTypes()
    {
        RunSta(() =>
        {
            var menuStyle = new Style(typeof(ContextMenu));
            var itemStyle = new Style(typeof(MenuItem));
            var resources = new ResourceDictionary
            {
                ["DarkContextMenuStyle"] = menuStyle,
                ["DarkMenuItemStyle"] = itemStyle,
            };

            EditorContextMenuTheme.Apply(resources);
            EditorContextMenuTheme.Apply(resources);

            var menuType = EditorContextMenuTheme.Resolve(EditorContextMenuTheme.ContextMenuTypeName);
            Assert.NotNull(menuType);
            var bridgedMenu = Assert.IsType<Style>(resources[menuType]);
            Assert.Equal(menuType, bridgedMenu.TargetType);
            Assert.Same(menuStyle, bridgedMenu.BasedOn);

            foreach (var name in EditorContextMenuTheme.MenuItemTypeNames)
            {
                var itemType = EditorContextMenuTheme.Resolve(name);
                Assert.NotNull(itemType);
                var bridgedItem = Assert.IsType<Style>(resources[itemType]);
                Assert.Equal(itemType, bridgedItem.TargetType);
                Assert.Same(itemStyle, bridgedItem.BasedOn);
            }
        });
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
