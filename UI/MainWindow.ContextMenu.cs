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
    /// Popup 内 KeyDown は別 Hwnd のため MainWindow.Preview に届かない。
    /// 論理ツリーでは MenuItem → Menu へバブルする（WPF MenuItem.OnKeyDown → MenuItemNavigate →
    /// 失敗時に親 TopLevelHeader → Menu.NavigateByLine で隣トップレベルへ）。
    /// 左右はその標準経路に任せ、Escape だけ「全部閉じて開く前フォーカスへ」を上書きする。
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

        window.TryHandleMainMenuEscape(item, e);
    }

    private void ApplyMainMenuBarVisibility()
    {
        var show = AppStorage.Settings.ShowMainMenuBar
            && !(_libraryMinimalChrome && IsLibraryMaximized);
        MainMenuBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            RebuildMainMenuBar();
        }
    }

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
        WaveformContextMenuBuilder.Populate(
            MainMenuBar.Items,
            WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit), includeHere: false),
            ExecuteWaveMenu);
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
        && (IsMainMenuBarTakingKeys()
            || MainMenuBar.IsKeyboardFocusWithin
            || MenuBaseIsMenuModeProperty?.GetValue(MainMenuBar) is true);

    /// <summary>
    /// WPF <c>Menu.OnEnterMenuMode</c> / <c>MenuBase.OnKeyDown(System Alt)</c> 相当。
    /// Preview で Alt を Handled にしているため、KeyboardNavigation.EnterMenuMode は来ない。
    /// </summary>
    private void TryEnterOrLeaveMainMenuModeFromBareAlt()
    {
        if (MainMenuBar.Visibility != Visibility.Visible)
        {
            EnsureMainMenuBar();
        }

        if (MainMenuBar.Visibility != Visibility.Visible)
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

        if (MainMenuBar.Visibility != Visibility.Visible)
        {
            EnsureMainMenuBar();
        }

        if (MainMenuBar.Visibility != Visibility.Visible)
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
        var entries = WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit), includeHere: false);
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
    /// Escape: メニューをすべて閉じ、Alt+文字前のフォーカスへ戻す。
    /// ←→ は触らない（葉で MenuItemNavigate が失敗 → TopLevelHeader へバブル → Menu.NavigateByLine）。
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
        new() { Frame = Waveform.PlayheadFrame };

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
        var canEdit = hasDoc
            && !busy
            && !recording
            && !IsLibraryMaximized
            && !BlocksOutsideFadeToolActions;
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
            CenterLocked = Waveform.CenterLocked,
            SilentSkip = SilentSkipCheck.IsChecked == true,
            AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
            WaapiVisible = _waapiPanelVisible,
            PlayExit = WaapiBar.PlayPostExitChecked,
            RangeClick = _rangeClickEnabled,
            Shuffle = LibraryBrowser.ShuffleEnabled,
            AutoPlayNext = LibraryBrowser.AutoPlayNextEnabled,
            AutoPlay = LibraryBrowser.AutoPlayEnabled,
            CheckSelectMode = LibraryBrowser.CheckSelectMode,
            CanStepPlaylist = _sessions.Count > 0,
            CanRestartTrack = _sessions.Count > 0,
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
            CanMergeTabs = !busy && !recording && ResolveBatchEditTargets().Length >= 2,
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
                : canEdit && !document!.HasMarkerAt(hit.Frame),
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
