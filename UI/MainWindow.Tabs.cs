using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private readonly record struct ClosedTab(DocumentSession Session, int Index);

    /// <summary>
    /// 複数選択されたタブ。Ctrl+クリックで個別追加、Shift+クリックで範囲、
    /// タブ上の Ctrl+A で全選択。Esc で解除。選択中の Ctrl+V は選択タブへの
    /// 履歴レシピ適用になる。タブのアクティブ化で解除。
    /// </summary>
    private readonly HashSet<DocumentSession> _selectedTabs = [];

    /// <summary>閉じたタブ。末尾が一番新しく、Ctrl+Shift+T でそこから戻す。</summary>
    private readonly List<ClosedTab> _closedTabs = [];

    /// <summary>Shift+クリックの範囲選択の起点。</summary>
    private DocumentSession? _tabSelectionAnchor;

    private const int ClosedTabLimit = 32;

    private bool _tabLayoutBusy;

    private bool HasTabSelection => _selectedTabs.Count > 0;

    private bool AllTabsSelected => _sessions.Count > 0 && _selectedTabs.Count == _sessions.Count;
    private void CaptureActiveSessionView()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.TimeZoom = Waveform.TimeZoom;
        _activeSession.AmpZoom = Waveform.AmpZoom;
        _activeSession.ViewStart = Waveform.ViewStart;
        _activeSession.PlayheadFrame = Waveform.PlayheadFrame;
        _activeSession.LoopEnabled = Waveform.LoopEnabled;
        _activeSession.SelectedMarkerFrames.Clear();
        _activeSession.SelectedMarkerFrames.AddRange(Waveform.SelectedMarkerFrames);
    }

    private void ActivateSession(DocumentSession session)
    {
        _selectedTabs.Clear();
        _tabSelectionAnchor = session;
        if (ReferenceEquals(_activeSession, session) && ReferenceEquals(_document, session.Document))
        {
            RebuildTabBar();
            return;
        }

        if (_activeSession is not null && _sessions.Contains(_activeSession))
        {
            CaptureActiveSessionView();
        }

        BindWorkspace(session);
    }

    private void ActivateAdjacentTab(int delta)
    {
        if (_sessions.Count <= 1 || _activeSession is null)
        {
            return;
        }

        var index = _sessions.IndexOf(_activeSession);
        if (index < 0)
        {
            return;
        }

        var count = _sessions.Count;
        var next = ((index + delta) % count + count) % count;
        ActivateSession(_sessions[next]);
    }

    /// <summary>閉じたら true。保存確認でキャンセルされたら false（連続クローズを中断する）。</summary>
    private bool CloseSession(DocumentSession session)
    {
        if (!OfferSaveIfDirty(session))
        {
            return false;
        }

        var index = _sessions.IndexOf(session);
        if (index < 0)
        {
            return true;
        }

        _selectedTabs.Remove(session);
        if (ReferenceEquals(_tabSelectionAnchor, session))
        {
            _tabSelectionAnchor = null;
        }

        var closingActive = ReferenceEquals(session, _activeSession);
        if (closingActive)
        {
            CaptureActiveSessionView();
        }

        RememberClosedTab(session, index);
        _sessions.RemoveAt(index);
        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            ForgetClosedDocument();
            return true;
        }

        if (closingActive)
        {
            BindWorkspace(_sessions[Math.Min(index, _sessions.Count - 1)]);
        }
        else
        {
            RebuildTabBar();
            RefreshTabHeaders();
        }

        return true;
    }

    private void RememberClosedTab(DocumentSession session, int index)
    {
        _closedTabs.Add(new ClosedTab(session, index));
        if (_closedTabs.Count > ClosedTabLimit)
        {
            _closedTabs.RemoveAt(0);
        }
    }

    private void ReopenLastClosedTab()
    {
        if (_closedTabs.Count == 0)
        {
            return;
        }

        var closed = _closedTabs[^1];
        _closedTabs.RemoveAt(_closedTabs.Count - 1);
        var session = closed.Session;
        if (_sessions.Contains(session))
        {
            ActivateSession(session);
            return;
        }

        if (session.Document.SourcePath is { } path
            && FindSessionByPath(path) is { } existing)
        {
            ActivateSession(existing);
            return;
        }

        var index = Math.Clamp(closed.Index, 0, _sessions.Count);
        _sessions.Insert(index, session);
        ActivateSession(session);
    }

    private void CloseOtherTabs(DocumentSession keep)
    {
        var targets = _sessions.Where(session => !ReferenceEquals(session, keep)).ToArray();
        RunCloseBatch(targets, () =>
        {
            foreach (var session in targets)
            {
                if (!CloseSession(session))
                {
                    return;
                }
            }
        });
    }

    /// <summary>このタブを含め、右側（または左側）を全部閉じる。</summary>
    private void CloseTabsFrom(DocumentSession session, bool rightSide)
    {
        var index = _sessions.IndexOf(session);
        if (index < 0)
        {
            return;
        }

        var targets = rightSide
            ? _sessions.Skip(index).ToArray()
            : _sessions.Take(index + 1).ToArray();
        RunCloseBatch(targets, () =>
        {
            foreach (var target in targets)
            {
                if (!CloseSession(target))
                {
                    return;
                }
            }
        });
    }

    private void CloseAllTabs() => CloseTabs(_sessions.ToArray());

    private void SelectAllTabs()
    {
        if (_sessions.Count == 0)
        {
            return;
        }

        _selectedTabs.Clear();
        foreach (var session in _sessions)
        {
            _selectedTabs.Add(session);
        }

        RebuildTabBar();
    }

    /// <summary>Ctrl+クリック。選択に個別追加／解除する（アクティブ化はしない）。</summary>
    private void ToggleTabSelection(DocumentSession session)
    {
        if (!_selectedTabs.Add(session))
        {
            _selectedTabs.Remove(session);
        }

        _tabSelectionAnchor = session;
        RebuildTabBar();
    }

    /// <summary>Shift+クリック。起点（前回操作したタブ、無ければアクティブ）から範囲選択。</summary>
    private void SelectTabRange(DocumentSession session)
    {
        var anchor = _tabSelectionAnchor ?? _activeSession;
        var from = anchor is null ? -1 : _sessions.IndexOf(anchor);
        var to = _sessions.IndexOf(session);
        if (to < 0)
        {
            return;
        }

        if (from < 0)
        {
            from = to;
        }

        var start = Math.Min(from, to);
        var end = Math.Max(from, to);
        for (var i = start; i <= end; i++)
        {
            _selectedTabs.Add(_sessions[i]);
        }

        RebuildTabBar();
    }

    /// <summary>Esc から呼ぶ。解除したら true。</summary>
    private bool ClearTabSelection()
    {
        if (!HasTabSelection)
        {
            return false;
        }

        _selectedTabs.Clear();
        RebuildTabBar();
        return true;
    }

    /// <summary>選択タブをタブバーの並び順で返す。</summary>
    private DocumentSession[] SelectedTabsInOrder() =>
        _sessions.Where(_selectedTabs.Contains).ToArray();

    private void CloseTabs(IReadOnlyList<DocumentSession> targets)
    {
        RunCloseBatch(targets, () =>
        {
            foreach (var session in targets)
            {
                if (!CloseSession(session))
                {
                    return;
                }
            }
        });
    }

    private void OpenTabContextMenu(FrameworkElement anchor, DocumentSession session)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.MousePoint,
        };

        if (HasTabSelection)
        {
            var targets = SelectedTabsInOrder();
            menu.Items.Add(AllTabsSelected
                ? CreateTabMenuItem(UiStrings.TabMenuCloseAll, CloseAllTabs, "Ctrl+Shift+W")
                : CreateTabMenuItem(UiStrings.TabMenuCloseSelected, () => CloseTabs(targets)));
            menu.Items.Add(CreateTabMenuItem(
                AllTabsSelected ? UiStrings.TabMenuPasteToAll : UiStrings.TabMenuPasteToSelected,
                () => PasteHistoryRecipesToTabs(targets),
                "Ctrl+V"));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateTabMenuItem(
                AllTabsSelected ? UiStrings.TabMenuExportWaveAll : UiStrings.TabMenuExportWaveSelected,
                () => ExportTabs(targets, AudioFileKind.Wave),
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                AllTabsSelected ? UiStrings.TabMenuExportMp3All : UiStrings.TabMenuExportMp3Selected,
                () => ExportTabs(targets, AudioFileKind.Mp3),
                AllTabsSelected ? "Ctrl+Shift+Alt+M" : null,
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                AllTabsSelected ? UiStrings.TabMenuExportWaveByMarkersAll : UiStrings.TabMenuExportWaveByMarkersSelected,
                () => ExportTabsSeparated(targets, TabExportSplit.Markers),
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                AllTabsSelected ? UiStrings.TabMenuExportWaveByRegionsAll : UiStrings.TabMenuExportWaveByRegionsSelected,
                () => ExportTabsSeparated(targets, TabExportSplit.Regions),
                enabled: !IsUiBusy));
        }
        else
        {
            menu.Items.Add(CreateTabMenuItem(UiStrings.TabMenuCloseOthers, () => CloseOtherTabs(session)));
            menu.Items.Add(CreateTabMenuItem(UiStrings.TabMenuCloseRight, () => CloseTabsFrom(session, rightSide: true)));
            menu.Items.Add(CreateTabMenuItem(UiStrings.TabMenuCloseLeft, () => CloseTabsFrom(session, rightSide: false)));
            menu.Items.Add(CreateTabMenuItem(UiStrings.TabMenuCloseAllNormal, CloseAllTabs, "Ctrl+Shift+W"));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateTabMenuItem(UiStrings.TabMenuSelectAll, SelectAllTabs));
            menu.Items.Add(new Separator());
            menu.Items.Add(CreateTabMenuItem(
                UiStrings.TabMenuExportWave,
                () => ExportTabs([session], AudioFileKind.Wave),
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                UiStrings.TabMenuExportMp3,
                () => ExportTabs([session], AudioFileKind.Mp3),
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                UiStrings.TabMenuExportWaveByMarkers,
                () => ExportTabsSeparated([session], TabExportSplit.Markers),
                enabled: !IsUiBusy));
            menu.Items.Add(CreateTabMenuItem(
                UiStrings.TabMenuExportWaveByRegions,
                () => ExportTabsSeparated([session], TabExportSplit.Regions),
                enabled: !IsUiBusy));
        }

        menu.IsOpen = true;
    }

    private static MenuItem CreateTabMenuItem(
        string header,
        Action action,
        string? gesture = null,
        bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (gesture is not null)
        {
            item.InputGestureText = gesture;
        }

        item.Click += (_, _) => action();
        TipService.Set(item, gesture is null ? header : $"{header}\n{gesture}");
        return item;
    }

    private DocumentSession? FindSessionByPath(string path)
    {
        if (!TryNormalizePath(path, out var full))
        {
            return null;
        }

        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is not { } existing
                || !TryNormalizePath(existing, out var existingFull))
            {
                continue;
            }

            if (string.Equals(existingFull, full, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    private static bool TryNormalizePath(string path, out string full)
    {
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch
        {
            full = path;
            return !string.IsNullOrWhiteSpace(path);
        }
    }

    private void RebuildTabBar()
    {
        DocumentTabs.Children.Clear();
        foreach (var session in _sessions)
        {
            DocumentTabs.Children.Add(CreateTabItem(session));
        }

        Dispatcher.BeginInvoke(SyncTabOverflow, DispatcherPriority.Loaded);
    }

    private void DocumentTabHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SyncTabOverflow();

    private void DocumentTabScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) =>
        SyncTabOverflow();

    private void DocumentTabScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (DocumentTabScroll.ExtentWidth <= DocumentTabScroll.ViewportWidth)
        {
            return;
        }

        ScrollTabs(e.Delta > 0 ? -1 : 1, step: 80);
        e.Handled = true;
    }

    private void TabScrollLeft_Click(object sender, RoutedEventArgs e)
    {
        ScrollTabs(-1);
        e.Handled = true;
    }

    private void TabScrollRight_Click(object sender, RoutedEventArgs e)
    {
        ScrollTabs(1);
        e.Handled = true;
    }

    private void ScrollTabs(int direction, double? step = null)
    {
        var amount = step ?? Math.Max(80, DocumentTabScroll.ViewportWidth * 0.6);
        var max = Math.Max(0, DocumentTabScroll.ExtentWidth - DocumentTabScroll.ViewportWidth);
        DocumentTabScroll.ScrollToHorizontalOffset(
            Math.Clamp(DocumentTabScroll.HorizontalOffset + direction * amount, 0, max));
    }

    private void SyncTabOverflow()
    {
        if (_tabLayoutBusy || DocumentTabHost.Visibility != Visibility.Visible)
        {
            return;
        }

        var hostWidth = DocumentTabHost.ActualWidth;
        if (hostWidth <= 1)
        {
            return;
        }

        var tabs = DocumentTabs.Children.OfType<Border>().ToArray();
        if (tabs.Length == 0)
        {
            SetTabScrollButtons(show: false);
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var slots = new DocumentTabSlot[tabs.Length];
        for (var i = 0; i < tabs.Length; i++)
        {
            var title = tabs[i].Tag is DocumentSession session ? session.TabTitle : "";
            var preferred = DocumentTabLayout.PreferredWidth(title, dpi);
            var min = Math.Min(preferred, DocumentTabLayout.MinWidth(title, dpi));
            var keepWide = tabs[i].Tag is DocumentSession tab
                && ReferenceEquals(tab, _activeSession);
            slots[i] = new DocumentTabSlot(preferred, min, keepWide);
        }

        var showArrows = !DocumentTabLayout.FitsWithoutArrows(slots, hostWidth);
        var widths = new double[tabs.Length];
        DocumentTabLayout.Allocate(slots, hostWidth, widths);

        _tabLayoutBusy = true;
        try
        {
            SetTabScrollButtons(showArrows);
            for (var i = 0; i < tabs.Length; i++)
            {
                var border = tabs[i];
                var width = widths[i];
                border.MinWidth = 0;
                border.MaxWidth = DocumentTabLayout.MaxTabWidth;
                if (double.IsNaN(border.Width) || Math.Abs(border.Width - width) > 0.5)
                {
                    border.Width = width;
                }
            }

            if (!showArrows && DocumentTabScroll.HorizontalOffset > 0)
            {
                DocumentTabScroll.ScrollToHorizontalOffset(0);
            }

            BringActiveTabIntoView(tabs);
            RefreshTabScrollEnabled();
        }
        finally
        {
            _tabLayoutBusy = false;
        }
    }

    private void SetTabScrollButtons(bool show)
    {
        var visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (TabScrollLeft.Visibility != visibility)
        {
            TabScrollLeft.Visibility = visibility;
            TabScrollRight.Visibility = visibility;
        }
    }

    private void RefreshTabScrollEnabled()
    {
        var canLeft = DocumentTabScroll.HorizontalOffset > 1;
        var canRight = DocumentTabScroll.HorizontalOffset + DocumentTabScroll.ViewportWidth
            < DocumentTabScroll.ExtentWidth - 1;
        TabScrollLeft.IsEnabled = canLeft;
        TabScrollRight.IsEnabled = canRight;
    }

    private void BringActiveTabIntoView(IReadOnlyList<Border> tabs)
    {
        foreach (var border in tabs)
        {
            if (border.Tag is DocumentSession session && ReferenceEquals(session, _activeSession))
            {
                border.BringIntoView();
                return;
            }
        }
    }

    private void RefreshTabHeaders()
    {
        foreach (var border in DocumentTabs.Children.OfType<Border>())
        {
            if (border.Tag is not DocumentSession session || border.Child is not DockPanel dock)
            {
                continue;
            }

            ApplyTabChrome(session, dock);
        }

        SyncTabOverflow();
    }

    private FrameworkElement CreateTabItem(DocumentSession session)
    {
        var active = ReferenceEquals(session, _activeSession) || _selectedTabs.Contains(session);
        var border = new Border
        {
            Tag = session,
            Background = BrushOrTransparent(active ? "ChromeMidBrush" : "WaveformBackBrush"),
            BorderBrush = (Brush)FindResource("ChromeBorderBrush"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Cursor = Cursors.Hand,
            MinWidth = 0,
            MaxWidth = DocumentTabLayout.MaxTabWidth,
        };

        var title = new TextBlock
        {
            Text = session.TabTitle,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 6, 0),
        };
        TipService.Set(title, session.Document.SourcePath ?? UiStrings.UntitledDocument);

        var close = new TextBlock
        {
            Text = "×",
            Width = 14,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("MutedForeBrush"),
            Cursor = Cursors.Hand,
        };
        TipService.Set(close, UiStrings.TipCloseTab);
        close.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            CloseSession(session);
        };

        var grid = new Grid { Margin = new Thickness(10, 0, 4, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 0);
        Grid.SetColumn(close, 1);
        grid.Children.Add(title);
        grid.Children.Add(close);

        var body = new DockPanel();
        var underline = new Border { Height = 2 };
        DockPanel.SetDock(underline, Dock.Bottom);
        body.Children.Add(underline);
        body.Children.Add(grid);
        ApplyTabChrome(session, body);
        border.Child = body;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                e.Handled = true;
                ToggleTabSelection(session);
            }
            else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                e.Handled = true;
                SelectTabRange(session);
            }
            else
            {
                ActivateSession(session);
            }
        };
        border.MouseDown += (_, e) =>
        {
            if (e.ChangedButton == MouseButton.Middle)
            {
                e.Handled = true;
                CloseSession(session);
            }
        };
        border.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenTabContextMenu(border, session);
        };
        return border;
    }

    private void RefreshTabLocalizedTips()
    {
        foreach (var border in DocumentTabs.Children.OfType<Border>())
        {
            if (border.Child is not DockPanel dock)
            {
                continue;
            }

            var session = border.Tag as DocumentSession;
            foreach (var block in dock.Children.OfType<Grid>().SelectMany(grid => grid.Children.OfType<TextBlock>()))
            {
                if (block.Text == "×")
                {
                    TipService.Set(block, UiStrings.TipCloseTab);
                }
                else if (session is not null)
                {
                    TipService.Set(block, session.Document.SourcePath ?? UiStrings.UntitledDocument);
                }
            }
        }
    }

    private Brush BrushOrTransparent(string? key) =>
        key is null ? Brushes.Transparent : (Brush)FindResource(key);

    private void ApplyTabChrome(DocumentSession session, DockPanel dock)
    {
        var dirty = session.Document.IsDirty;
        var active = ReferenceEquals(session, _activeSession) || _selectedTabs.Contains(session);
        var accent = (Brush)FindResource(dirty ? "DirtyAccentBrush" : "AccentCyanBrush");
        var titleBrush = dirty
            ? accent
            : (Brush)FindResource(active ? "PrimaryForeBrush" : "MutedForeBrush");

        if (dock.Parent is Border host)
        {
            host.Background = BrushOrTransparent(active ? "ChromeMidBrush" : "WaveformBackBrush");
        }

        foreach (var child in dock.Children)
        {
            if (child is Border underline && underline.Height == 2)
            {
                underline.Background = active ? accent : Brushes.Transparent;
                underline.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (child is Grid grid
                && grid.Children.OfType<TextBlock>().FirstOrDefault() is { } title)
            {
                title.Text = session.TabTitle;
                title.Foreground = titleBrush;
            }
        }
    }
}
