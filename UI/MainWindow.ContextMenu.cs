using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private WaveformContextHit _waveMenuHit = new();
    private bool _pendingContextMenuKey;
    private bool _mainMenuBarHooked;
    private static bool _mainMenuItemKeyHandlerRegistered;

    /// <summary>
    /// Alt+文字でメニューを開く直前のフォーカス。
    /// Escape／メニューモード解除時に親フォーカススコープへ戻す。
    /// </summary>
    private IInputElement? _mainMenuFocusRestore;

    /// <summary>
    /// キーボードで開いた直後の SubmenuOpened。Populate の Clear でフォーカスが落ちないよう、
    /// ヘッダに仮フォーカスしてから子を差し替え、WPF 同様先頭へ戻す。
    /// </summary>
    private bool _mainMenuOpenedWithKeyboard;

    /// <summary>
    /// WPF <see cref="MenuItem"/> の OnAccessKey(TopLevelHeader) と同じ:
    /// IsSubmenuOpen=true のあと NavigateToStart（先頭項目へキーボードフォーカス）。
    /// </summary>
    private static readonly MethodInfo? OpenSubmenuWithKeyboardMethod = typeof(MenuItem).GetMethod(
        "OpenSubmenuWithKeyboard",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// MenuBase.IsMenuMode。閉じるときはこれを false にし、WPF の KeyboardLeaveMenuMode と同じ経路を使う。
    /// </summary>
    private static readonly PropertyInfo? MenuBaseIsMenuModeProperty = typeof(MenuBase).GetProperty(
        "IsMenuMode",
        BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>上部メニューの表示反映。設定プレビューからも呼ぶ。</summary>
    internal void EnsureMainMenuBar()
    {
        if (!_mainMenuBarHooked)
        {
            _mainMenuBarHooked = true;
            // WPF Menu 既定は true。明示して KeyboardNavigation.EnterMenuMode（単独 Alt）を受ける。
            MainMenuBar.IsMainMenu = true;
            MainMenuBar.AddHandler(
                System.Windows.Controls.MenuItem.SubmenuOpenedEvent,
                new RoutedEventHandler(MainMenuBar_SubmenuOpened));
            EnsureMainMenuItemKeyHandler();
        }

        ApplyMainMenuBarVisibility();
    }

    /// <summary>
    /// メニューバー項目列の左／中央／右寄せ。設定の MainMenuBarAlign を反映する。
    /// </summary>
    internal void ApplyMainMenuBarAlign()
    {
        var horizontal = AppStorage.Settings.ResolvedMainMenuBarAlign() switch
        {
            LayoutMainMenuBarAlign.Center => HorizontalAlignment.Center,
            LayoutMainMenuBarAlign.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left,
        };

        if (TrySetMainMenuItemsPanelAlign(horizontal))
        {
            return;
        }

        var factory = new FrameworkElementFactory(typeof(StackPanel));
        factory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, horizontal);
        MainMenuBar.ItemsPanel = new ItemsPanelTemplate(factory);
    }

    private bool TrySetMainMenuItemsPanelAlign(HorizontalAlignment horizontal)
    {
        if (FindMainMenuItemsPanel() is not { } panel)
        {
            return false;
        }

        panel.HorizontalAlignment = horizontal;
        return true;
    }

    private Panel? FindMainMenuItemsPanel()
    {
        if (!MainMenuBar.IsLoaded)
        {
            return null;
        }

        return FindVisualChild<Panel>(MainMenuBar);
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// ツールバーの表示。設定オフ／アイコン全非表示／動画ミニは畳む。
    /// 本再生クローム隠れは Opacity 側（PlaylistVideoChromeTargets）。
    /// </summary>
    internal void ApplyTransportToolbarVisibility()
    {
        ApplyTransportToolbarDock();
        var settings = AppStorage.Settings;
        Transport.ApplyLayoutFromSettings(settings);
        var show = settings.ShowTransportToolbar
            && Transport.HasVisibleCommands
            && !IsVideoMiniPlayerActive();
        if (!show)
        {
            TransportToolbarHost.BeginAnimation(UIElement.OpacityProperty, null);
            TransportToolbarHost.Opacity = 1;
            TransportToolbarHost.IsHitTestVisible = true;
            TransportToolbarHost.Visibility = Visibility.Collapsed;
            return;
        }

        TransportToolbarHost.Visibility = Visibility.Visible;
        if (!_playlistVideoChromeVisible)
        {
            LibraryPlayerMode.FadeElementOpacity(
                TransportToolbarHost,
                0,
                instant: true,
                hitTestVisible: false);
        }
        else if (LibraryPlayerMode.NeedsForcedOpaque(TransportToolbarHost))
        {
            LibraryPlayerMode.FadeElementOpacity(
                TransportToolbarHost,
                1,
                instant: true,
                hitTestVisible: true);
        }
    }

    /// <summary>
    /// ツールバーをメニュー直下（Top）かステータスバー上（Bottom）へ置く。
    /// 位置はアクティブな F9–F12 プリセットごと。DockPanel は子の宣言順で外側から積むため、
    /// Bottom 時は Waapi のあとに差し込む。
    /// </summary>
    internal void ApplyTransportToolbarDock()
    {
        var slot = _activeLayoutSlot ?? WindowLayoutSlot.F12;
        var preset = AppStorage.Settings.ResolvedLayoutPreset(slot);
        Transport.SetItemsAlignment(preset.ResolvedTransportToolbarAlign());
        var dockBottom = preset.ResolvedTransportToolbarDock() == LayoutTransportToolbarDock.Bottom;
        DockPanel.SetDock(TransportToolbarHost, dockBottom ? Dock.Bottom : Dock.Top);

        var children = RootDock.Children;
        var current = children.IndexOf(TransportToolbarHost);
        if (current < 0)
        {
            return;
        }

        int target;
        if (dockBottom)
        {
            // Status → Waapi → Toolbar → Work。Work の直前へ。
            var workIndex = children.IndexOf(WorkGrid);
            target = workIndex >= 0 ? workIndex : children.Count;
        }
        else
        {
            // Menu → Toolbar → …。メニューの直後へ。
            var menuIndex = children.IndexOf(MainMenuBar);
            target = menuIndex >= 0 ? menuIndex + 1 : 0;
        }

        // Remove すると後ろの index が 1 つ前へずれる。
        if (current < target)
        {
            target--;
        }

        if (current == target)
        {
            return;
        }

        children.RemoveAt(current);
        children.Insert(target, TransportToolbarHost);
    }

    /// <summary>
    /// Popup 内 KeyDown は別 Hwnd のため MainWindow.Preview に届かない。
    /// 論理ツリーでは MenuItem → Menu へバブルする（WPF MenuItem.OnKeyDown → MenuItemNavigate →
    /// 失敗時に親階層や Menu.NavigateByLine へ）。
    /// ↑↓ は同一ポップアップ内だけ動かし端ではループする（親へ戻すのは ←）。
    /// Escape は「全部閉じて開く前フォーカスへ」を上書きする。
    /// MenuBase.OnKeyDown(Escape) はサブメニューを一段閉じるだけなので、Handled にする。
    /// </summary>
    private static void EnsureMainMenuItemKeyHandler()
    {
        if (_mainMenuItemKeyHandlerRegistered)
        {
            return;
        }

        _mainMenuItemKeyHandlerRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            Keyboard.KeyDownEvent,
            new KeyEventHandler(MainMenuItem_ClassKeyDown),
            handledEventsToo: false);
    }

    private static void MainMenuItem_ClassKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled
            || sender is not MenuItem item
            || Window.GetWindow(item) is not MainWindow window)
        {
            return;
        }

        window.TryHandleMenuItemVerticalNav(item, e);
        if (e.Handled)
        {
            return;
        }

        window.TryHandleMainMenuEscape(item, e);
    }

    private void ApplyMainMenuBarVisibility()
    {
        // 設定オフ／F9 最小は領域ごと畳む。動画ミニは Opacity で隠すため Visible を保つ。
        var show = AppStorage.Settings.ShowMainMenuBar
            && !(_libraryMinimalChrome && IsLibraryMaximized && !_videoLaunchPlacement);
        if (!show)
        {
            MainMenuBar.BeginAnimation(UIElement.OpacityProperty, null);
            MainMenuBar.Opacity = 1;
            MainMenuBar.IsHitTestVisible = true;
            MainMenuBar.Visibility = Visibility.Collapsed;
            ApplyTransportToolbarVisibility();
            return;
        }

        MainMenuBar.Visibility = Visibility.Visible;
        ApplyMainMenuBarAlign();
        RebuildMainMenuBar();
        if (!_playlistVideoChromeVisible)
        {
            // クローム隠れ中に設定で出したときは、領域だけ残して即透明。
            SyncMainMenuBarChromeOpacity(instant: true);
        }
        else if (LibraryPlayerMode.NeedsForcedOpaque(MainMenuBar))
        {
            // フェードイン中は触らず、不透明なのに固まったときだけ直す。
            LibraryPlayerMode.FadeElementOpacity(MainMenuBar, 1, instant: true, hitTestVisible: true);
        }

        ApplyTransportToolbarVisibility();
    }

    /// <summary>
    /// 動画本再生／PDF 明るい表示／動画ミニでは見た目だけ消す（レイアウト高さは残す）。
    /// </summary>
    private void SyncMainMenuBarChromeOpacity(bool instant)
    {
        if (MainMenuBar.Visibility != Visibility.Visible)
        {
            return;
        }

        var visible = _playlistVideoChromeVisible;
        if (!visible && FindOpenMainMenuRoot() is not null)
        {
            DismissMainMenuAndRestoreFocus();
        }

        LibraryPlayerMode.FadeElementOpacity(
            MainMenuBar,
            visible ? 1 : 0,
            instant,
            hitTestVisible: visible,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
    }

    /// <summary>設定上は出していても、クローム隠れ中はキー／フォーカスを受けない。</summary>
    private bool IsMainMenuBarVisuallyAvailable() =>
        MainMenuBar.Visibility == Visibility.Visible && MainMenuBar.IsHitTestVisible;

    private void RebuildMainMenuBar()
    {
        if (MainMenuBar.Visibility != Visibility.Visible)
        {
            return;
        }

        // 開いているサブメニューがあるときはルートを壊さない（子は SubmenuOpened で更新）。
        if (IsMainMenuBarTakingKeys())
        {
            return;
        }

        var hit = CaptureMenuBarHit();
        var entries = WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit));
        if (TryRefreshMainMenuRootsInPlace(entries))
        {
            return;
        }

        WaveformContextMenuBuilder.Populate(MainMenuBar.Items, entries, ExecuteWaveMenu);
    }

    /// <summary>
    /// 見出しの並びが同じなら、ルートを作り直さず有効状態だけ更新する。
    /// 再生／停止のクローム同期からも呼ばれ、作り直すとメニューバー全体が点滅する。
    /// 子項目は開くたびに <see cref="RefreshMainMenuRootChildren"/> で組み直すので古くてよい。
    /// </summary>
    private bool TryRefreshMainMenuRootsInPlace(IReadOnlyList<WaveMenuEntry> entries)
    {
        var items = MainMenuBar.Items;
        if (items.Count == 0 || items.Count != entries.Count)
        {
            return false;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            switch (entries[i], items[i])
            {
                case (WaveMenuSeparatorEntry, Separator):
                    continue;
                case (WaveMenuItemEntry { Command: null, Children.Count: > 0 } entry, MenuItem { HasItems: true } item)
                    when string.Equals(item.Header as string, entry.Header, StringComparison.Ordinal):
                    continue;
                default:
                    return false;
            }
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i] is WaveMenuItemEntry entry && items[i] is MenuItem item)
            {
                item.IsEnabled = entry.Enabled;
            }
        }

        return true;
    }

    /// <summary>上部メニューのドロップダウンが開いている。</summary>
    private bool IsMainMenuBarTakingKeys() =>
        MainMenuBar.Visibility == Visibility.Visible
        && FindOpenMainMenuRoot() is not null;

    /// <summary>
    /// サブメニュー展開中、または単独 Alt の EnterMenuMode でバーにフォーカスがあるとき。
    /// Alt アップの LeaveWin32MenuMode やアプリショートカットを抑止する判定に使う。
    /// </summary>
    private bool IsMainMenuBarSessionActive() =>
        MainMenuBar.Visibility == Visibility.Visible
        && (FindOpenMainMenuRoot() is not null
            || (MainMenuBar.IsHitTestVisible
                && (MainMenuBar.IsKeyboardFocusWithin
                    || MenuBaseIsMenuModeProperty?.GetValue(MainMenuBar) is true)));

    /// <summary>
    /// WPF <c>Menu.OnEnterMenuMode</c> / <c>MenuBase.OnKeyDown(System Alt)</c> 相当。
    /// Preview で Alt を Handled にしているため、KeyboardNavigation.EnterMenuMode は来ない。
    /// </summary>
    private void TryEnterOrLeaveMainMenuModeFromBareAlt()
    {
        if (!IsMainMenuBarVisuallyAvailable())
        {
            EnsureMainMenuBar();
        }

        if (!IsMainMenuBarVisuallyAvailable())
        {
            return;
        }

        // Menu.OnEnterMenuMode: 他がキャプチャ中は入らない。
        if (Mouse.Captured is not null && !ReferenceEquals(Mouse.Captured, MainMenuBar))
        {
            return;
        }

        // メニューセッション中の単独 Alt → 解除（MenuBase の SystemKey Alt）。
        if (IsMainMenuBarSessionActive())
        {
            DismissMainMenuAndRestoreFocus();
            return;
        }

        if (MainMenuBar.Items.Count == 0)
        {
            RebuildMainMenuBar();
        }

        RememberMainMenuFocusRestore();
        if (IsLibraryMaximized)
        {
            LibraryBrowser.CancelPendingFocusRequests();
        }

        // Menu.OnEnterMenuMode: 先頭の有効なトップレベルへフォーカス（サブメニューはまだ開かない）。
        foreach (var item in MainMenuBar.Items.OfType<MenuItem>())
        {
            if (!item.IsEnabled)
            {
                continue;
            }

            if (item.Focus())
            {
                return;
            }
        }
    }

    private MenuItem? FindOpenMainMenuRoot()
    {
        foreach (var item in MainMenuBar.Items.OfType<MenuItem>())
        {
            if (item.IsSubmenuOpen)
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>開いているルート配下（Popup 内の論理子を含む）か。</summary>
    private static bool IsInOpenMainMenuSubtree(MenuItem root, MenuItem node)
    {
        for (MenuItem? current = node; current is not null;)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = ItemsControl.ItemsControlFromItemContainer(current) as MenuItem
                ?? LogicalTreeHelper.GetParent(current) as MenuItem;
        }

        return false;
    }

    private bool IsMainMenuBarItem(MenuItem item)
    {
        if (MainMenuBar.Visibility != Visibility.Visible)
        {
            return false;
        }

        if (ReferenceEquals(ItemsControl.ItemsControlFromItemContainer(item), MainMenuBar)
            || ReferenceEquals(item.Parent, MainMenuBar))
        {
            return true;
        }

        return FindOpenMainMenuRoot() is { } open && IsInOpenMainMenuSubtree(open, item);
    }

    /// <summary>
    /// Alt+文字でルートメニューを開く。Preview でアプリショートカットより先に処理する
    /// （Handled にしないと AccessKeyManager より先に自前 Alt 系が食う）。
    /// 挙動は MenuItem.OnAccessKey → OpenSubmenuWithKeyboard と同じ。
    /// </summary>
    private bool TryOpenMainMenuAccessKey(Key key, ModifierKeys modifiers)
    {
        // Alt のみ（Shift/Ctrl なし）。menuModifiers 側で System キー時の Alt ビットは呼び出し元が立てる。
        if ((modifiers & ModifierKeys.Alt) == 0
            || (modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Windows)) != 0
            || MenuAccessKeys.FromKey(key) is not { } access)
        {
            return false;
        }

        if (!IsMainMenuBarVisuallyAvailable())
        {
            EnsureMainMenuBar();
        }

        if (!IsMainMenuBarVisuallyAvailable())
        {
            return false;
        }

        // 開くたびに Rebuild しない（新規 MenuItem の IsLoaded=false で CoerceIsSubmenuOpen が false になる）。
        if (MainMenuBar.Items.Count == 0)
        {
            RebuildMainMenuBar();
        }

        MenuItem? match = null;
        foreach (var item in MainMenuBar.Items.OfType<MenuItem>())
        {
            if (MenuAccessKeys.Read(item.Header as string ?? string.Empty) != access)
            {
                continue;
            }

            match = item;
            break;
        }

        if (match is null || !match.IsEnabled)
        {
            return false;
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.CancelPendingFocusRequests();
        }

        // Alt+文字を処理したので、続く Alt アップを「単独 Alt」扱いにしない（開いて即閉じを防ぐ）。
        _altMenuChordHadOtherKey = true;

        // すでに同じルートが開いている: 閉じず先頭へフォーカスだけ（吸い戻し防止）。
        if (match.IsSubmenuOpen)
        {
            FocusFirstMainMenuChild(match);
            return true;
        }

        RememberMainMenuFocusRestore();
        _mainMenuOpenedWithKeyboard = true;

        foreach (var item in MainMenuBar.Items.OfType<MenuItem>())
        {
            if (!ReferenceEquals(item, match) && item.IsSubmenuOpen)
            {
                item.IsSubmenuOpen = false;
            }
        }

        OpenMainMenuRootWithKeyboard(match);
        // 未 Loaded の Coerce 待ちでも AccessKey は消費する（AKM 二重起動とショートカット食いを防ぐ）。
        return true;
    }

    private void RememberMainMenuFocusRestore()
    {
        // メニューセッション中の再オープン（隣メニューへの AccessKey 等）では上書きしない。
        if (_mainMenuFocusRestore is not null && FindOpenMainMenuRoot() is not null)
        {
            return;
        }

        var focused = Keyboard.FocusedElement;
        if (focused is null || ReferenceEquals(focused, MainMenuBar))
        {
            _mainMenuFocusRestore = null;
            return;
        }

        if (focused is MenuItem menuItem && IsMainMenuBarItem(menuItem))
        {
            return;
        }

        _mainMenuFocusRestore = focused;
    }

    /// <summary>
    /// WPF MenuItem.OpenSubmenuWithKeyboard と同じ入口。
    /// OpenMenu は未 Loaded だと CoerceIsSubmenuOpen で false のまま true を返すことがあるため、
    /// 開いたことを確認し、だめなら IsSubmenuOpen を直接立てて Input 優先度で先頭フォーカスする
    /// （MenuItem.OpenOnLoad が Input で Coerce し直すのと同じタイミング）。
    /// </summary>
    private void OpenMainMenuRootWithKeyboard(MenuItem root)
    {
        if (OpenSubmenuWithKeyboardMethod is not null)
        {
            OpenSubmenuWithKeyboardMethod.Invoke(root, null);
        }

        if (!root.IsSubmenuOpen)
        {
            root.IsSubmenuOpen = true;
        }

        if (root.IsSubmenuOpen)
        {
            FocusFirstMainMenuChild(root);
            return;
        }

        // Coerce 待ち（未 Loaded）。Loaded/Input 後に再度開いてフォーカス。
        root.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (!root.IsSubmenuOpen)
            {
                root.IsSubmenuOpen = true;
            }

            if (root.IsSubmenuOpen)
            {
                FocusFirstMainMenuChild(root);
            }
        }));
    }

    /// <summary>
    /// OpenSubmenuWithKeyboard / Menu.FocusItem が行う NavigateToStart 相当。
    /// SubmenuOpened の Populate 後に呼び、キーボードフォーカスを先頭葉へ戻す。
    /// </summary>
    private void FocusFirstMainMenuChild(MenuItem top)
    {
        if (!top.IsSubmenuOpen || MainMenuBar.Visibility != Visibility.Visible)
        {
            return;
        }

        foreach (var child in top.Items.OfType<MenuItem>())
        {
            if (!child.IsEnabled)
            {
                continue;
            }

            if (child.Focus() || ReferenceEquals(Keyboard.Focus(child), child))
            {
                return;
            }
        }

        top.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
    }

    private void RefreshMainMenuRootChildren(MenuItem top)
    {
        var hit = CaptureMenuBarHit();
        _waveMenuHit = hit;
        var entries = WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit));
        var header = top.Header as string ?? string.Empty;
        foreach (var entry in entries.OfType<WaveMenuItemEntry>())
        {
            if (!string.Equals(entry.Header, header, StringComparison.Ordinal))
            {
                continue;
            }

            top.IsEnabled = entry.Enabled;
            if (entry.Children is { Count: > 0 })
            {
                WaveformContextMenuBuilder.Populate(top.Items, entry.Children, ExecuteWaveMenu);
            }

            return;
        }
    }

    /// <summary>
    /// サブメニュー内の ↑↓。同一階層の兄弟だけを行き来し、端ではループする。
    /// トップレベル見出しは WPF 任せ（↓ で開く等）。親階層へ戻るのは ←。
    /// </summary>
    private void TryHandleMenuItemVerticalNav(MenuItem item, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is not (Key.Up or Key.Down) || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        // ポップアップ内の項目だけ。TopLevelHeader / TopLevelItem は触らない。
        if (item.Role is not (MenuItemRole.SubmenuItem or MenuItemRole.SubmenuHeader))
        {
            return;
        }

        TryFocusSiblingMenuItem(item, key == Key.Down ? 1 : -1);
        // 成否にかかわらず Handled。失敗時のバブルで親メニューや隣トップレベルへ飛ばない。
        e.Handled = true;
    }

    /// <summary>同じ ItemsControl 内の、フォーカス可能な前後の MenuItem へ移る。</summary>
    private static bool TryFocusSiblingMenuItem(MenuItem item, int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        var parent = ItemsControl.ItemsControlFromItemContainer(item);
        if (parent is null)
        {
            return false;
        }

        var siblings = new List<MenuItem>();
        for (var i = 0; i < parent.Items.Count; i++)
        {
            var container = parent.ItemContainerGenerator.ContainerFromIndex(i) as MenuItem
                ?? parent.Items[i] as MenuItem;
            if (container is null
                || !container.IsEnabled
                || container.Visibility != Visibility.Visible
                || !container.Focusable)
            {
                continue;
            }

            siblings.Add(container);
        }

        var index = siblings.IndexOf(item);
        if (index < 0 || siblings.Count == 0)
        {
            return false;
        }

        // 端では反対側へループ。
        var next = (index + direction) % siblings.Count;
        if (next < 0)
        {
            next += siblings.Count;
        }

        var target = siblings[next];
        return target.Focus() || ReferenceEquals(Keyboard.Focus(target), target);
    }

    /// <summary>
    /// Escape: メニューをすべて閉じ、Alt+文字前のフォーカスへ戻す。
    /// ←→ は WPF 標準（← で親階層／隣トップレベル）。
    /// </summary>
    private void TryHandleMainMenuEscape(MenuItem item, KeyEventArgs e)
    {
        if (!IsMainMenuBarTakingKeys() || !IsMainMenuBarItem(item))
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.CancelPendingFocusRequests();
        }

        DismissMainMenuAndRestoreFocus();
        e.Handled = true;
    }

    /// <summary>
    /// メニューをすべて閉じ、開く前の要素へフォーカスを戻す。
    /// MenuBase.RestorePreviousFocus は Keyboard.Focus(null) で親スコープの FocusedElement を使う。
    /// 先に親スコープへ復帰先を書き込み、IsMenuMode=false で WPF と同じ解除経路を通す。
    /// </summary>
    private void DismissMainMenuAndRestoreFocus()
    {
        var restore = _mainMenuFocusRestore;
        _mainMenuFocusRestore = null;
        _mainMenuOpenedWithKeyboard = false;

        SeedParentFocusScopeForMenuRestore(restore);

        foreach (var item in MainMenuBar.Items.OfType<MenuItem>())
        {
            if (item.IsSubmenuOpen)
            {
                item.IsSubmenuOpen = false;
            }
        }

        if (ReferenceEquals(Mouse.Captured, MainMenuBar))
        {
            Mouse.Capture(null);
        }

        // MenuBase.KeyboardLeaveMenuMode: IsMenuMode=false → capture 解除 → RestorePreviousFocus。
        if (MenuBaseIsMenuModeProperty is not null
            && MenuBaseIsMenuModeProperty.GetValue(MainMenuBar) is true)
        {
            MenuBaseIsMenuModeProperty.SetValue(MainMenuBar, false);
        }

        if (!TryRestoreMainMenuFocus(restore))
        {
            RestoreDefaultContentFocusAfterMenu();
        }
    }

    /// <summary>
    /// Menu は FocusScope。親スコープの FocusedElement を開く前の要素にしておくと、
    /// MenuBase.RestorePreviousFocus の Keyboard.Focus(null) がライブラリ等へ吸われない。
    /// </summary>
    private void SeedParentFocusScopeForMenuRestore(IInputElement? restore)
    {
        if (restore is null)
        {
            return;
        }

        DependencyObject? parent = MainMenuBar.Parent as DependencyObject
            ?? VisualTreeHelper.GetParent(MainMenuBar);
        if (parent is null)
        {
            return;
        }

        var parentScope = FocusManager.GetFocusScope(parent);
        if (parentScope is null || ReferenceEquals(parentScope, MainMenuBar))
        {
            parentScope = this;
        }

        FocusManager.SetFocusedElement(parentScope, restore);
    }

    private static bool TryRestoreMainMenuFocus(IInputElement? restore)
    {
        if (restore is not DependencyObject)
        {
            return false;
        }

        if (restore is UIElement element)
        {
            if (!element.IsVisible || !element.IsEnabled || !element.Focusable)
            {
                return false;
            }

            return element.Focus() || ReferenceEquals(Keyboard.Focus(element), element);
        }

        Keyboard.Focus(restore);
        return ReferenceEquals(Keyboard.FocusedElement, restore);
    }

    private void RestoreDefaultContentFocusAfterMenu()
    {
        if (IsLibraryMaximized)
        {
            LibraryBrowser.RequestListFocus();
            return;
        }

        Waveform.Focus();
    }

    /// <summary>
    /// フォーカスがメイン Hwnd（メニューバー見出し等）に残っているときの Preview。
    /// Popup 内の ←→ は WPF に任せる。Escape のみここで拾う（クラスハンドラと二重でも可）。
    /// Alt+文字は触らない。
    /// </summary>
    private bool TryHandleMainMenuBarKey(KeyEventArgs e)
    {
        if (!IsMainMenuBarTakingKeys())
        {
            return false;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.CancelPendingFocusRequests();
        }

        DismissMainMenuAndRestoreFocus();
        e.Handled = true;
        return true;
    }

    private void MainMenuBar_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not MenuItem top
            || !ReferenceEquals(top.Parent, MainMenuBar))
        {
            return;
        }

        var keyboardOpen = _mainMenuOpenedWithKeyboard;
        _mainMenuOpenedWithKeyboard = false;

        // Populate(Clear) はフォーカス中の子を破棄する。
        // フォーカスがメニュー外へ一瞬でも出ると MenuBase.OnIsKeyboardFocusWithinChanged が
        // IsMenuMode=false → RestorePreviousFocus し、ライブラリ等へ吸われる。
        // キーボードオープン時はヘッダへ仮フォーカスしてから差し替える（IsKeyboardFocusWithin を維持）。
        if (keyboardOpen)
        {
            top.Focus();
        }

        RefreshMainMenuRootChildren(top);

        if (keyboardOpen && top.IsSubmenuOpen)
        {
            FocusFirstMainMenuChild(top);
        }
    }

    /// <summary>メニューバーはクリック位置が無いので再生ヘッド基準（キーボード右クリックと同趣旨）。</summary>
    private WaveformContextHit CaptureMenuBarHit() =>
        Waveform.CaptureContextHitAtPlayhead();

    private void OpenWaveformContextMenu(WaveformContextHit hit)
    {
        if (IsUiBusy || LibraryPlayerMode.HidesWaveformContextMenu(IsLibraryMaximized))
        {
            return;
        }

        _waveMenuHit = hit;
        if (_waveMenu is { IsOpen: true })
        {
            _waveMenu.IsOpen = false;
        }

        var menu = WaveformContextMenuBuilder.Create(
            WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit)),
            ExecuteWaveMenu,
            Waveform);
        // プレイリスト側と同じ。外側クリックでメニューを閉じた貫通が、空領域の選択解除や
        // プレイヤー解除へ落ちてリストが消えるのを、閉じた直後のアイドルまで握りつぶす。
        LibraryBrowser.ArmContextMenuDismissGuard(menu);
        _waveMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_waveMenu, menu))
            {
                _waveMenu = null;
            }
        };
        menu.IsOpen = true;
    }

    private void Overview_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsUiBusy)
        {
            return;
        }

        e.Handled = true;
        OpenOverviewContextMenu(e.GetPosition(Overview).X);
    }

    private bool OpenContextMenuFromKeyboard()
    {
        if (IsUiBusy)
        {
            return true;
        }

        // 前面 UI 隠れ中でもツリー／お気に入りのメニューは出す。
        // リストの「プレイリストからクリア」だけは、見えない選択を消す事故になるので出さない。
        if (LibraryPlayerMode.BlocksPlaylistMutationWhileChromeHidden(_playlistVideoChromeVisible)
            && LibraryBrowser.IsListKeyboardFocused)
        {
            return true;
        }

        if (LibraryBrowser.TryOpenKeyboardContextMenu())
        {
            return true;
        }

        if (TryOpenTabContextMenuUnderPointer())
        {
            return true;
        }

        if (Overview.IsMouseOver)
        {
            OpenOverviewContextMenu(System.Windows.Input.Mouse.GetPosition(Overview).X);
            return true;
        }

        if (LibraryPlayerMode.HidesWaveformContextMenu(IsLibraryMaximized))
        {
            return true;
        }

        return Waveform.TryRequestContextMenuFromKeyboard();
    }

    private void OpenOverviewContextMenu(double x)
    {
        OpenWaveformContextMenu(new WaveformContextHit
        {
            Frame = (long)Math.Round(Overview.FrameAt(x)),
        });
    }

    private bool TryOpenTabContextMenuUnderPointer()
    {
        foreach (var border in DocumentTabs.Children.OfType<System.Windows.Controls.Border>())
        {
            if (border.IsMouseOver && border.Tag is DocumentSession session)
            {
                OpenTabContextMenu(border, session);
                return true;
            }
        }

        if (_tileMode)
        {
            foreach (var pane in _tilePanes)
            {
                if (pane.Header.IsMouseOver)
                {
                    OpenTabContextMenu(pane.Header, pane.Session);
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsPointerOverTileHeader()
    {
        if (!_tileMode)
        {
            return false;
        }

        foreach (var pane in _tilePanes)
        {
            if (pane.Header.IsMouseOver)
            {
                return true;
            }
        }

        return false;
    }

    private WaveformContextMenuModel CaptureWaveMenuModel(WaveformContextHit hit)
    {
        var document = _document;
        var hasDoc = document is not null;
        var recording = IsRecording;
        var busy = IsUiBusy;
        var selectedTabs = SelectedTabsInOrder();
        // フェード調整中は破壊編集メニューを無効化（再生・シーク系の CanNavigate は残す）。
        // V モード中は波形破壊編集だけ落とし、マーカー／リージョンは CanEditMarkers で残す。
        var canEditMarkers = hasDoc
            && !busy
            && !recording
            && !IsLibraryMaximized
            && !IsCrossfadeEditMode;
        var canEdit = canEditMarkers && !IsVolumeCurveMode;
        var canScratch = hasDoc && !busy && !recording && IsPlayerScratchContext();
        var canNavigate = hasDoc && !busy && !recording;
        var hasSelection = hasDoc && !document!.Selection.IsEmpty;
        var hasScratchMarkers = canScratch && Waveform.HasPlayerScratchMarkers;
        var scratchHasAtHit = canScratch && Waveform.PlayerScratchHasMarkerAt(hit.Frame);
        var scratchHasAtPlayhead = canScratch && Waveform.PlayerScratchHasMarkerAt(Waveform.PlayheadFrame);
        return new WaveformContextMenuModel
        {
            HasDocument = hasDoc,
            CanEdit = canEdit,
            CanEditMarkers = canEditMarkers,
            RestrictMenusToNavigateAndMarkers = IsVolumeCurveMode,
            CanNavigate = canNavigate,
            IsBusy = busy,
            IsPlaying = IsPlaybackActive(),
            IsRecording = recording,
            HasSelection = hasSelection,
            HasAnySelection = hasSelection
                || Waveform.HasSelectedMarkers
                || Waveform.HasSelectedRegions
                || Waveform.HasSelectedTimelineItems,
            CanCopySelectionTimes = SelectionClipboardText.CanCopy(CaptureSelectionClipboardItems()),
            CanUndo = ResolveMenuCanUndo(canScratch),
            CanRedo = ResolveMenuCanRedo(canScratch),
            CanPasteAudio = _clipboard is { IsEmpty: false },
            CanPasteHistory = _historyRecipeClipboard.Count > 0,
            HasMarkers = IsLibraryMaximized
                ? hasScratchMarkers
                : hasDoc && document!.Markers.Count > 0,
            HasRegions = hasDoc && document!.Regions.Count > 0 && !IsLibraryMaximized,
            HasSelectedMarkers = Waveform.HasSelectedMarkers,
            HasSelectedRegions = Waveform.HasSelectedRegions && !IsLibraryMaximized,
            HasSampleLoop = hasDoc && !document!.SampleLoop.IsEmpty && !IsLibraryMaximized,
            AllowsRegionsAndLoops = hasDoc && document!.AllowsRegionsAndLoops && !IsLibraryMaximized,
            // プレイヤー捨てマーカーはコメント改名しない。
            CanRenameMarker = !IsLibraryMaximized
                && hasDoc
                && (Waveform.HasSelectedMarkers || document!.HasMarkerAt(Waveform.PlayheadFrame)),
            CanRenameRegion = hasDoc
                && document!.AllowsRegionsAndLoops
                && !IsLibraryMaximized
                && (Waveform.HasSelectedRegions || document.Regions.Count > 0),
            HasSolo = (_activeSession?.SoloMask ?? 0) != 0,
            CenterLocked = !IsLibraryMaximized && Waveform.CenterLocked,
            SilentSkip = SilentSkipLatch.IsLatched,
            AutoLoop = LibraryBrowser.AutoLoopEnabled,
            AlwaysOnTop = AlwaysOnTopLatch.IsLatched,
            WaapiVisible = _waapiPanelVisible,
            PlayExit = WaapiBar.PlayPostExitChecked,
            RangeClick = _rangeClickEnabled,
            Shuffle = LibraryBrowser.ShuffleEnabled,
            AutoPlayNext = LibraryBrowser.AutoPlayNextEnabled,
            AutoPlay = LibraryBrowser.AutoPlayEnabled,
            CheckSelectMode = LibraryBrowser.CheckSelectMode,
            CanStepPlaylist = _sessions.Count > 0,
            CanRestartTrack = _sessions.Count > 0,
            CanSelectSameColor = IsLibraryMaximized
                ? LibraryBrowser.CanSelectSameColorRows()
                : CanSelectSameColorTabs(),
            VideoMini = IsVideoMiniPlayerActive(),
            MinimalChrome = _activeLayoutSlot == WindowLayoutSlot.F9,
            CanToggleVideoMini = LibraryPlayerMode.CanToggleVideoMiniPlayer(
                IsVideoMiniPlayerActive(),
                IsVideoMiniEnterEligible(),
                hasPlayableSession: _sessions.Count > 0),
            VideoFullscreen = _playlistVideoFullscreen,
            CanToggleVideoFullscreen = IsLibraryMaximized
                && (_playlistVideoFullscreen
                    || (LibraryBrowser.PlaylistVisualShown
                        && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf))),
            CanScaleVideoMini = CanScaleVideoLaunchWindow(),
            VideoHud = _playlistVideoHud,
            CanToggleVideoHud = IsPlaylistVisualHudContext(),
            VideoTimecode = _playlistVideoTimecode,
            CanToggleVideoTimecode = IsPlaylistVisualHudContext(),
            PdfBackgroundPinned = LibraryBrowser.PdfBackgroundPinned,
            CanTogglePdfBackground = IsLibraryMaximized
                && (LibraryBrowser.PlaylistVisualIsPdf || LibraryBrowser.PdfBackgroundPinned),
            CanExitRealPlayback = CanOfferExitRealPlayback(),
            CanForceVideoProxy = CanOfferForceVideoProxy(),
            TileMode = _tileMode,
            TileSearchActive = TileSearchBox.Text.Length > 0,
            KeepTarget = WaapiBar.KeepTargetChecked,
            AutoActive = WaapiBar.AutoActiveChecked,
            CanKeepTarget = WaapiBar.KeepTargetToggleEnabled,
            WaapiExportEnabled = WaapiBar.ExportEnabled,
            CanReopenTab = _workspace.ClosedTabs.Count > 0,
            HasMultipleTabs = _sessions.Count > 1,
            CanCloseOtherTabs = hasDoc && !busy && !recording && _sessions.Count > 1,
            CanCloseTabsToRight = CanCloseTabsFromActive(rightSide: true, hasDoc, busy, recording),
            CanCloseTabsToLeft = CanCloseTabsFromActive(rightSide: false, hasDoc, busy, recording),
            CanMergeTabs = CanMergeSelectedTabs(),
            CanBounce = CanBounceSelectedVstInserts(),
            HasSelectedTabs = IsLibraryMaximized
                ? LibraryBrowser.SelectedSessions.Length > 0
                : selectedTabs.Length > 0,
            HasMultipleSelectedTabs = IsLibraryMaximized
                ? LibraryBrowser.SelectedSessions.Length >= 2
                : selectedTabs.Length >= 2,
            AllTabsSelected = AllTabsSelected,
            SelectedTabsHaveMarkers = AnyTabHasMarkers(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            SelectedTabsHaveRegions = AnyTabHasRegions(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            SelectedTabsHaveSampleLoop = AnyTabHasSampleLoop(
                IsLibraryMaximized ? ResolvePlaylistLoudnessMatchTargets() : selectedTabs),
            WaveTileArrange = _tileArrange,
            CanTileHorizontal = CanOfferTileArrange(WaveformTileArrange.Horizontal),
            CanTileVertical = CanOfferTileArrange(WaveformTileArrange.Vertical),
            CanTileGrid = CanOfferTileArrange(WaveformTileArrange.Grid),
            WaveformMaximized = _activeLayoutSlot == WindowLayoutSlot.F11,
            AnalyzersMaximized = _activeLayoutSlot == WindowLayoutSlot.F12,
            LibraryMaximized = _activeLayoutSlot == WindowLayoutSlot.F10,
            CanExportMp3 = !IsLibraryMaximized
                || LibraryPlayerMode.HasMp3ExportCandidate(ResolvePlaylistLoudnessMatchTargets()),
            CanLoopPlay = canNavigate
                && (hasSelection
                    || !document!.SampleLoop.IsEmpty
                    || document.TryGetRoleSpan(MarkerRole.Loop, Waveform.PlayheadFrame, out _)),
            CanAddMarkerHere = IsLibraryMaximized
                ? canScratch && !scratchHasAtHit
                : canEditMarkers && !document!.HasMarkerAt(hit.Frame),
            AnalysisView = Waveform.AnalysisView,
            Hit = hit,
        };
    }


    private void CopySelectionTimes(
        SelectionClipboardPathKind pathKind,
        IReadOnlyList<DocumentSession>? sessions = null)
    {
        var text = SelectionClipboardText.Format(
            CaptureSelectionClipboardItems(sessions),
            pathKind,
            AppStorage.Settings.StatusShowSamples);
        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, this);
    }

    private (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[] CaptureSelectionClipboardItems(
        IReadOnlyList<DocumentSession>? sessions = null)
    {
        sessions ??= HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null ? [] : [_activeSession];
        if (sessions.Count == 0)
        {
            return [];
        }

        var items = new (string? SourcePath, string DisplayName, WaveSelection Selection, int SampleRate)[sessions.Count];
        for (var i = 0; i < sessions.Count; i++)
        {
            var document = sessions[i].Document;
            items[i] = (document.SourcePath, sessions[i].DisplayName, document.Selection, document.SampleRate);
        }

        return items;
    }

    private void CopySessionPathTexts(bool fullPath)
    {
        var sessions = HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null ? [] : [_activeSession];
        CopySessionPathTexts(sessions, fullPath);
    }

    private void CopySessionPathTexts(IReadOnlyList<DocumentSession> sessions, bool fullPath)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        string text;
        if (fullPath)
        {
            var paths = new string?[sessions.Count];
            for (var i = 0; i < sessions.Count; i++)
            {
                paths[i] = sessions[i].Document.SourcePath;
            }

            text = DocumentFileNames.ClipboardFullPaths(paths);
        }
        else
        {
            var items = new (string? SourcePath, string DisplayName)[sessions.Count];
            for (var i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                items[i] = (session.Document.SourcePath, session.DisplayName);
            }

            text = DocumentFileNames.ClipboardFileNames(items);
        }

        if (text.Length == 0)
        {
            return;
        }

        SystemClipboard.TrySetText(text, this);
    }

    private void PlayFromFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        SeekFrame(frame);
        if (!IsPlaybackActive())
        {
            StartPlayback(frame, prerollSeconds: 0);
        }
    }

    private void AddMarkerAtFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, _document.FrameCount);
        SeekFrame(frame);
        if (_document.HasMarkerAt(frame))
        {
            return;
        }

        ApplyPlaceLive(ProcessEdits.AddMarker(_document, frame));
        AfterMarkerEdit();
    }

    private void ClearChannelSolo()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.SoloMask = 0;
        ApplyChannelSolo();
    }

    /// <summary>メニュー／ショートカット／トランスポート共通。選択があれば選択、なければアクティブ（プレイヤーはプレイリスト選択）。</summary>
    private void ExportMp3FromUi()
    {
        if (IsLibraryMaximized)
        {
            ExportPlaylistSelectionMp3();
        }
        else if (HasTabSelection)
        {
            ExportTabs(SelectedTabsInOrder(), AudioFileKind.Mp3);
        }
        else
        {
            ExportActiveTab(AudioFileKind.Mp3);
        }
    }

    private void ExportActiveTab(AudioFileKind kind)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabs([_activeSession], kind);
    }

    private void ExportActiveTabSeparated(TabExportSplit split)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabsSeparated([_activeSession], split);
    }

    private bool CanCloseTabsFromActive(bool rightSide, bool hasDoc, bool busy, bool recording)
    {
        if (!hasDoc || busy || recording || _activeSession is null)
        {
            return false;
        }

        var index = _sessions.IndexOf(_activeSession);
        if (index < 0)
        {
            return false;
        }

        return rightSide ? index < _sessions.Count - 1 : index > 0;
    }
}
