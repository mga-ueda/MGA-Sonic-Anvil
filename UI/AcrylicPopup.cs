using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
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

    /// <summary>メニューバーから開くメニューのリスト面。</summary>
    private const string MenuListBackBrushKey = "MenuListBackBrush";

    /// <summary>コンテキストメニューとコンボのリスト面。</summary>
    private const string PopupBackBrushKey = "MenuPopupBackBrush";

    /// <summary>
    /// リソースが無いときのヒット用。Acrylic HWND では Background=Transparent の空白がヒットしない。
    /// </summary>
    private static readonly Brush HitFill = CreateHitFill();

    private static int _installed;

    private static Brush CreateHitFill()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

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
        // マウスのサブメニューは SystemParameters.MenuShowDelay（~400ms）待ち。
        // キーボードは即開くので、マウスも即開くように揃える。
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.MouseEnterEvent,
            new MouseEventHandler(OnMenuItemMouseEnter));
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
        ClearOpaqueFill(menu);
        ScheduleFrost(menu, AcrylicBackdrop.TintBrushKey, roundCorners: true);
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

        FrostPopupSurface(
            popup,
            AcrylicBackdrop.TintBrushKey,
            roundCorners: true,
            IsUnderContextMenu(item) ? PopupBackBrushKey : MenuListBackBrushKey);
    }

    private static void OnMenuItemMouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not MenuItem { HasItems: true, IsEnabled: true, IsSubmenuOpen: false } item)
        {
            return;
        }

        // TopLevelHeader はメニューモード突入後のみ。SubmenuHeader の遅延が体感の主因。
        if (item.Role != MenuItemRole.SubmenuHeader)
        {
            return;
        }

        item.IsSubmenuOpen = true;
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

        FrostPopupSurface(popup, AcrylicBackdrop.TintBrushKey, roundCorners: true);
    }

    private static readonly ConditionalWeakTable<Popup, AttachOptions> AttachedOptions = new();

    private sealed class AttachOptions
    {
        public string TintBrushKey = AcrylicBackdrop.TintBrushKey;
        public bool RoundCorners = true;
    }

    /// <summary>色パレットなど、クラスハンドラ外の Popup 向け。</summary>
    public static void Attach(Popup popup) =>
        Attach(popup, tintBrushKey: null, roundCorners: true);

    /// <summary>
    /// <paramref name="tintBrushKey"/> を指定すると、そのブラシで Acrylic を載せる
    /// （トーストは <see cref="AcrylicBackdrop.ToastTintBrushKey"/>）。
    /// null／空はダイアログ／メニューと同じ <see cref="AcrylicBackdrop.TintBrushKey"/>。
    /// </summary>
    public static void Attach(Popup popup, string? tintBrushKey) =>
        Attach(popup, tintBrushKey, roundCorners: true);

    /// <param name="roundCorners">
    /// false はタイムコード専用（DWM 角丸／システム影なし）。
    /// </param>
    public static void Attach(Popup popup, string? tintBrushKey, bool roundCorners)
    {
        ArgumentNullException.ThrowIfNull(popup);
        AttachedOptions.Remove(popup);
        AttachedOptions.Add(
            popup,
            new AttachOptions
            {
                TintBrushKey = string.IsNullOrEmpty(tintBrushKey)
                    ? AcrylicBackdrop.TintBrushKey
                    : tintBrushKey,
                RoundCorners = roundCorners,
            });

        popup.Opened -= OnAttachedPopupOpened;
        popup.Opened += OnAttachedPopupOpened;
    }

    private static void OnAttachedPopupOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup popup)
        {
            return;
        }

        var options = AttachedOptions.TryGetValue(popup, out var stored)
            ? stored
            : new AttachOptions();
        FrostPopupSurface(popup, options.TintBrushKey, options.RoundCorners);
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

    private static void FrostPopupSurface(Popup popup, string tintBrushKey, bool roundCorners) =>
        FrostPopupSurface(popup, tintBrushKey, roundCorners, PopupBackBrushKey);

    private static void FrostPopupSurface(
        Popup popup,
        string tintBrushKey,
        bool roundCorners,
        string backBrushKey)
    {
        TryDisableTransparency(popup);
        ClearOpaqueFill(popup.Child, backBrushKey);
        ScheduleFrost(popup.Child as Visual ?? popup, tintBrushKey, roundCorners);
    }

    private static void ClearOpaqueFill(UIElement? child) =>
        ClearOpaqueFill(child, PopupBackBrushKey);

    private static void ClearOpaqueFill(UIElement? child, string backBrushKey)
    {
        switch (child)
        {
            case Border border:
                ApplyPopupBack(border, Border.BackgroundProperty, backBrushKey);
                ApplyScrollViewerBack(border, backBrushKey);
                break;
            case ContextMenu menu:
                ApplyPopupBack(menu, Control.BackgroundProperty, backBrushKey);
                break;
            case Panel panel:
                foreach (UIElement element in panel.Children)
                {
                    if (element is Border fill && fill.BorderThickness.Left < 0.5)
                    {
                        ApplyPopupBack(fill, Border.BackgroundProperty, backBrushKey);
                    }
                }

                ApplyScrollViewerBack(panel, backBrushKey);
                break;
        }
    }

    private static void ApplyScrollViewerBack(DependencyObject root, string backBrushKey)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                ApplyPopupBack(viewer, Control.BackgroundProperty, backBrushKey);
            }

            ApplyScrollViewerBack(child, backBrushKey);
        }
    }

    /// <summary>コンテキストメニュー配下のサブメニューはプルダウン側の色を保つ。</summary>
    private static bool IsUnderContextMenu(DependencyObject item)
    {
        DependencyObject? current = item;
        while (current is not null)
        {
            if (current is ContextMenu)
            {
                return true;
            }

            if (current is Menu)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private static void ApplyPopupBack(
        FrameworkElement target,
        DependencyProperty property,
        string backBrushKey)
    {
        if (Application.Current?.TryFindResource(backBrushKey) is Brush)
        {
            target.SetResourceReference(property, backBrushKey);
            return;
        }

        target.SetValue(property, HitFill);
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

    private static void ScheduleFrost(Visual? visual, string tintBrushKey, bool roundCorners)
    {
        if (visual is null)
        {
            return;
        }

        // 開いた直後に Loaded/Render で何度も DWM を叩くと、メニューの
        // IsHighlighted 更新が遅れたり欠けることがある。HWND 成立後に1回だけ。
        if (AcrylicBackdrop.TryApplyToVisual(visual, tintBrushKey, roundCorners))
        {
            return;
        }

        if (visual is DispatcherObject dispatcherObject)
        {
            dispatcherObject.Dispatcher.BeginInvoke(
                () => AcrylicBackdrop.TryApplyToVisual(visual, tintBrushKey, roundCorners),
                DispatcherPriority.Loaded);
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
