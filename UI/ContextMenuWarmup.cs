using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 起動後アイドルで ContextMenu / MenuItem のカスタムテンプレと Popup を一度展開する。
/// 初回右クリックだけテンプレ適用が走って遅くなるのを避ける。
/// </summary>
internal static class ContextMenuWarmup
{
    /// <summary>
    /// 画面外・透明のメニューを短く開いて閉じる。dismiss ガードは付けない。
    /// マウスボタン押下中は触らず、次のアイドルへ持ち越す。
    /// </summary>
    public static void Run(FrameworkElement host)
    {
        if (host is null || !host.Dispatcher.CheckAccess())
        {
            return;
        }

        if (!host.IsLoaded)
        {
            return;
        }

        // ユーザー操作中に開くとフォーカスや dismiss と競合しうる。
        if (Mouse.LeftButton == MouseButtonState.Pressed
            || Mouse.RightButton == MouseButtonState.Pressed
            || Mouse.MiddleButton == MouseButtonState.Pressed)
        {
            host.Dispatcher.BeginInvoke(() => Run(host), DispatcherPriority.ApplicationIdle);
            return;
        }

        var child = new MenuItem { Header = "_" };
        var submenu = new MenuItem { Header = "_" };
        submenu.Items.Add(child);
        var root = new MenuItem { Header = "_" };
        var menu = new ContextMenu
        {
            PlacementTarget = host,
            Placement = PlacementMode.AbsolutePoint,
            HorizontalOffset = -20000,
            VerticalOffset = -20000,
            Opacity = 0,
            Focusable = false,
            IsHitTestVisible = false,
            StaysOpen = true,
        };
        menu.Items.Add(root);
        menu.Items.Add(submenu);

        try
        {
            menu.IsOpen = true;
            menu.UpdateLayout();
            // サブメニュー用 PART_Popup も初回展開する。
            submenu.IsSubmenuOpen = true;
            submenu.UpdateLayout();
        }
        finally
        {
            submenu.IsSubmenuOpen = false;
            menu.IsOpen = false;
            menu.Items.Clear();
            menu.PlacementTarget = null;
        }
    }
}
