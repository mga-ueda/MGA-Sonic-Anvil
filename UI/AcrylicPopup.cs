using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 右クリック／メニューバー下開き／コンボのドロップなどへ、
/// ダイアログと同じ DWM Acrylic すりガラスを載せる。
/// </summary>
internal static class AcrylicPopup
{
    private static readonly FieldInfo? ParentPopupField = typeof(ContextMenu).GetField(
        "_parentPopup",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? HookupParentPopupMethod = typeof(ContextMenu).GetMethod(
        "HookupParentPopup",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        // ContextMenu は内部 Popup が AllowsTransparency=true のまま開くと Acrylic が濁る。
        // Opening 時点でホストを用意し、非レイヤードにしてから開く。
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.ContextMenuOpeningEvent,
            new ContextMenuEventHandler(OnOwnerContextMenuOpening),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(ContextMenu),
            ContextMenu.OpenedEvent,
            new RoutedEventHandler(OnContextMenuOpened));
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.SubmenuOpenedEvent,
            new RoutedEventHandler(OnMenuItemSubmenuOpened));
        // ComboBox.DropDownOpened は CLR イベントのみなので Loaded で購読する。
        EventManager.RegisterClassHandler(
            typeof(ComboBox),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnComboLoaded));
    }

    private static void OnOwnerContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = (sender as FrameworkElement)?.ContextMenu
            ?? (e.Source as FrameworkElement)?.ContextMenu;
        if (menu is null)
        {
            return;
        }

        PrepareContextMenuHost(menu);
    }

    private static void OnComboLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        combo.DropDownOpened -= OnComboDropDownOpened;
        combo.DropDownOpened += OnComboDropDownOpened;
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        PrepareContextMenuHost(menu);
        menu.Background = Brushes.Transparent;
        ClearOpaqueFill(menu);
        ScheduleFrost(menu);
    }

    private static void OnMenuItemSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        item.ApplyTemplate();
        if (item.Template?.FindName("PART_Popup", item) is not Popup popup)
        {
            return;
        }

        FrostPopupSurface(popup);
    }

    private static void OnComboDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo)
        {
            return;
        }

        combo.ApplyTemplate();
        if (FindDescendantPopup(combo) is not { } popup)
        {
            return;
        }

        FrostPopupSurface(popup);
    }

    /// <summary>色パレットなど、クラスハンドラ外の Popup 向け。</summary>
    public static void Attach(Popup popup)
    {
        ArgumentNullException.ThrowIfNull(popup);
        popup.Opened -= OnAttachedPopupOpened;
        popup.Opened += OnAttachedPopupOpened;
    }

    private static void OnAttachedPopupOpened(object? sender, EventArgs e)
    {
        if (sender is Popup popup)
        {
            FrostPopupSurface(popup);
        }
    }

    private static void PrepareContextMenuHost(ContextMenu menu)
    {
        try
        {
            if (ParentPopupField?.GetValue(menu) is not Popup
                && HookupParentPopupMethod is not null
                && !menu.IsOpen)
            {
                HookupParentPopupMethod.Invoke(menu, null);
            }

            if (ParentPopupField?.GetValue(menu) is Popup host)
            {
                TryDisableTransparency(host);
            }
            else if (menu.Parent is Popup parent)
            {
                TryDisableTransparency(parent);
            }
        }
        catch (TargetInvocationException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void FrostPopupSurface(Popup popup)
    {
        TryDisableTransparency(popup);
        ClearOpaqueFill(popup.Child);
        ScheduleFrost(popup.Child as Visual ?? popup);
    }

    private static void ClearOpaqueFill(UIElement? child)
    {
        switch (child)
        {
            case Border border:
                border.Background = Brushes.Transparent;
                break;
            case ContextMenu menu:
                if (menu.Template?.FindName("Bd", menu) is Border chrome)
                {
                    chrome.Background = Brushes.Transparent;
                }

                break;
            case Panel panel:
                foreach (UIElement element in panel.Children)
                {
                    if (element is Border fill && fill.BorderThickness.Left < 0.5)
                    {
                        fill.Background = Brushes.Transparent;
                    }
                }

                break;
        }
    }

    private static void TryDisableTransparency(Popup popup)
    {
        if (!popup.AllowsTransparency)
        {
            return;
        }

        try
        {
            popup.AllowsTransparency = false;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static void ScheduleFrost(Visual? visual)
    {
        if (visual is null)
        {
            return;
        }

        AcrylicBackdrop.TryApplyToVisual(visual);
        if (visual is DispatcherObject dispatcherObject)
        {
            dispatcherObject.Dispatcher.BeginInvoke(
                () => AcrylicBackdrop.TryApplyToVisual(visual),
                DispatcherPriority.Loaded);
            dispatcherObject.Dispatcher.BeginInvoke(
                () => AcrylicBackdrop.TryApplyToVisual(visual),
                DispatcherPriority.Render);
        }
    }

    private static Popup? FindDescendantPopup(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is Popup popup)
            {
                return popup;
            }

            if (FindDescendantPopup(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
