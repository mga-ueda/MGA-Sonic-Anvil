using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(RevealContextMenuWash));
        EditorContextMenuTheme.Apply(Resources);
        SlidingTabAccent.Install();
        ImeComposition.Install(this);
        UiColors.Load();
        UiThemeService.Start();
        UiScaleService.Start();
        base.OnStartup(e);
    }

    /// <summary>
    /// 右クリックメニューをプレイヤーのプルダウンと同じ透かしにする。
    /// ポップアップが不透明だと背面合成が黒になり、暗い半透明塗りが濁る。
    /// </summary>
    private static void RevealContextMenuWash(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || menu.Parent is not Popup popup)
        {
            return;
        }

        try
        {
            popup.AllowsTransparency = true;
        }
        catch (InvalidOperationException)
        {
        }
    }
}
