using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// VST 選択メニュー。メーカー → プラグインの入れ子（フォルダ手繰り）。
/// ContextMenu は MaxHeight で切れるだけでスクロールしないため、
/// Popup + ScrollViewer で同じ構成を自前実装する。
/// </summary>
internal static class VstInsertPluginMenu
{
    /// <summary>VST の SubCategories（Fx|Reverb）を表示用に整形。'|' は意味のある区切りなので " / " にする。</summary>
    public static string FormatCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return string.Empty;
        }

        return string.Join(
            " / ",
            category.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    public static string VendorKey(Vst3PluginEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Vendor)
            ? UiStrings.LabelVstInsertVendorUnknown
            : entry.Vendor.Trim();

    public static IReadOnlyList<IGrouping<string, Vst3PluginEntry>> GroupByVendor(
        IEnumerable<Vst3PluginEntry> plugins) =>
        plugins
            .Where(p => p.IsRecognized)
            .GroupBy(VendorKey, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static double ResolveMaxHeight()
    {
        var max = DesignMetrics.VstInsertMenuMaxHeight;
        try
        {
            max = Math.Min(max, SystemParameters.WorkArea.Height * 0.85);
        }
        catch
        {
        }

        return Math.Max(240, max);
    }

    /// <summary>
    /// メーカー一覧（縦スクロール）＋ホバーで右にプラグイン一覧（縦スクロール）。
    /// </summary>
    public static Popup Show(
        UIElement placement,
        IReadOnlyList<KeyValuePair<string, List<VstPickerEntry>>> groups,
        Action<VstPickerEntry> onPick,
        Action? onClear)
    {
        var maxHeight = ResolveMaxHeight();
        Popup? flyout = null;
        Border? activeVendorRow = null;
        TextBlock? activeArrow = null;
        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };

        var rootPopup = new Popup
        {
            PlacementTarget = placement,
            Placement = PlacementMode.Bottom,
            StaysOpen = true,
            AllowsTransparency = true,
        };

        var rootBorder = MakeChrome(maxHeight, minWidth: 240, maxWidth: 360);
        var rootScroll = MakeScroll();
        var rootStack = new StackPanel();

        void CloseFlyoutOnly()
        {
            if (flyout is not null)
            {
                flyout.IsOpen = false;
                flyout = null;
            }

            if (activeArrow is not null)
            {
                activeArrow.Text = "▸  ";
            }

            if (activeVendorRow is not null)
            {
                activeVendorRow.Background = Brushes.Transparent;
            }

            activeVendorRow = null;
            activeArrow = null;
        }

        void CloseAll()
        {
            closeTimer.Stop();
            CloseFlyoutOnly();
            rootPopup.IsOpen = false;
        }

        void OnOutsideDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src
                && (IsUnder(rootPopup.Child, src) || (flyout?.Child is not null && IsUnder(flyout.Child, src))))
            {
                return;
            }

            CloseAll();
        }

        void CancelClose() => closeTimer.Stop();

        void ScheduleClose()
        {
            closeTimer.Stop();
            closeTimer.Start();
        }

        bool IsMouseOverMenu()
        {
            if (rootBorder.IsMouseOver)
            {
                return true;
            }

            return flyout?.Child is FrameworkElement fe && fe.IsMouseOver;
        }

        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            if (IsMouseOverMenu())
            {
                return;
            }

            CloseAll();
        };

        void OpenVendorFlyout(
            Border vendorRow,
            TextBlock arrow,
            string vendor,
            IReadOnlyList<VstPickerEntry> plugins)
        {
            CancelClose();
            if (ReferenceEquals(activeVendorRow, vendorRow) && flyout is { IsOpen: true })
            {
                return;
            }

            CloseFlyoutOnly();
            activeVendorRow = vendorRow;
            activeArrow = arrow;
            arrow.Text = "▾  ";
            vendorRow.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

            var chrome = MakeChrome(maxHeight, minWidth: 280, maxWidth: 480);
            var scroll = MakeScroll();
            var stack = new StackPanel();
            foreach (var plugin in plugins.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                var entry = plugin;
                var cat = FormatCategory(entry.Category);
                stack.Children.Add(MakeRow(
                    entry.Name,
                    string.IsNullOrEmpty(cat) ? null : cat,
                    () =>
                    {
                        CloseAll();
                        onPick(entry);
                    }));
            }

            scroll.Content = stack;
            chrome.Child = scroll;
            WireWheel(chrome, scroll);
            chrome.MouseEnter += (_, _) => CancelClose();
            chrome.MouseLeave += (_, _) => ScheduleClose();

            flyout = new Popup
            {
                PlacementTarget = vendorRow,
                Placement = PlacementMode.Right,
                HorizontalOffset = 2,
                StaysOpen = true,
                AllowsTransparency = true,
                Child = chrome,
                IsOpen = true,
            };
        }

        if (groups.Count == 0)
        {
            rootStack.Children.Add(new TextBlock
            {
                Text = UiStrings.LabelVstInsertNoneRecognized,
                Foreground = TryBrush("StatusBarDetailForeBrush", Brushes.Gray),
                Margin = new Thickness(4),
                TextWrapping = TextWrapping.Wrap,
            });
        }
        else
        {
            foreach (var group in groups)
            {
                var vendor = group.Key;
                var plugins = group.Value;
                var arrow = new TextBlock
                {
                    Text = "▸  ",
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEC)),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var label = new TextBlock
                {
                    Text = vendor + "  (" + plugins.Count + ")",
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TryBrush("StatusBarTitleForeBrush", Brushes.White),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var header = new StackPanel { Orientation = Orientation.Horizontal };
                header.Children.Add(arrow);
                header.Children.Add(label);

                var row = new Border
                {
                    Child = header,
                    Padding = new Thickness(6, 5, 6, 5),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                };
                row.MouseEnter += (_, _) => OpenVendorFlyout(row, arrow, vendor, plugins);
                row.MouseLeave += (_, _) => ScheduleClose();
                rootStack.Children.Add(row);
            }
        }

        if (onClear is not null)
        {
            rootStack.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 6) });
            rootStack.Children.Add(MakeRow(UiStrings.LabelVstInsertRemove, null, () =>
            {
                CloseAll();
                onClear();
            }));
        }

        rootScroll.Content = rootStack;
        rootBorder.Child = rootScroll;
        WireWheel(rootBorder, rootScroll);
        rootBorder.MouseEnter += (_, _) => CancelClose();
        rootBorder.MouseLeave += (_, _) => ScheduleClose();
        rootPopup.Child = rootBorder;

        var host = Application.Current?.MainWindow ?? placement;
        Mouse.AddPreviewMouseDownHandler(host, OnOutsideDown);
        rootPopup.Closed += (_, _) =>
        {
            closeTimer.Stop();
            CloseFlyoutOnly();
            Mouse.RemovePreviewMouseDownHandler(host, OnOutsideDown);
        };

        rootPopup.IsOpen = true;
        return rootPopup;
    }

    private static Border MakeChrome(double maxHeight, double minWidth, double maxWidth) =>
        new()
        {
            Background = TryBrush("TransportBackBrush", new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E))),
            BorderBrush = TryBrush("StatusBarDetailForeBrush", Brushes.Gray),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            MaxHeight = maxHeight,
            MinWidth = minWidth,
            MaxWidth = maxWidth,
        };

    private static ScrollViewer MakeScroll() =>
        new()
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            Focusable = true,
        };

    private static void WireWheel(UIElement root, ScrollViewer scroll) =>
        root.PreviewMouseWheel += (_, e) =>
        {
            scroll.ScrollToVerticalOffset(scroll.VerticalOffset - e.Delta);
            e.Handled = true;
        };

    private static bool IsUnder(DependencyObject? root, DependencyObject? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, root))
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node)
                   ?? (node is FrameworkElement fe ? fe.Parent : null);
        }

        return false;
    }

    private static Border MakeRow(string title, string? subtitle, Action click)
    {
        var text = new StackPanel();
        text.Children.Add(new TextBlock
        {
            Text = title,
            Foreground = TryBrush("StatusBarDetailForeBrush", Brushes.WhiteSmoke),
        });
        if (!string.IsNullOrEmpty(subtitle))
        {
            text.Children.Add(new TextBlock
            {
                Text = subtitle,
                FontSize = 10,
                Opacity = 0.7,
                Foreground = TryBrush("StatusBarDetailForeBrush", Brushes.Gray),
            });
        }

        var border = new Border
        {
            Child = text,
            Padding = new Thickness(6, 4, 6, 4),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        border.MouseEnter += (_, _) =>
            border.Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
        border.MouseLeave += (_, _) => border.Background = Brushes.Transparent;
        border.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            click();
        };
        return border;
    }

    private static Brush TryBrush(string key, Brush fallback)
    {
        try
        {
            if (Application.Current?.TryFindResource(key) is Brush brush)
            {
                return brush;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
