using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum FormatConvertKind
{
    SampleRate,
    BitDepth,
    Channels,
}

internal static class FormatConvertPicker
{
    private static readonly PropertyInfo? CurrentSelectionProperty =
        typeof(MenuBase).GetProperty("CurrentSelection", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly DependencyPropertyKey? IsHighlightedKey =
        typeof(MenuItem)
            .GetField("IsHighlightedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as DependencyPropertyKey;

    private static readonly DependencyPropertyDescriptor? IsHighlightedDescriptor =
        DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem));

    private static bool _updatingSelectionChrome;

    private sealed class MenuState
    {
        public required FormatConvertKind Kind { get; init; }
        public required bool AllowPreview { get; init; }
        public required bool AllowCustom { get; init; }
        public TextBox? CustomBox { get; set; }
        public int FallbackRate { get; init; }
        public Action<int>? OnCommit { get; init; }
        public Action<int>? OnPreview { get; init; }
        public Action<int>? OnHighlight { get; init; }
    }

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        FormatConvertKind kind,
        int currentValue,
        Action<int> onCommit,
        Action<int>? onPreview,
        Action<int>? onHighlight = null)
    {
        var allowCustom = kind == FormatConvertKind.SampleRate;
        var allowPreview = kind is FormatConvertKind.SampleRate or FormatConvertKind.BitDepth;
        var menu = new ContextMenu
        {
            MinWidth = 0,
            PlacementTarget = placementTarget,
            Placement = PlacementMode.Custom,
            CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
                PopupCursorAvoidance.Callback(
                    popupSize,
                    targetSize,
                    Mouse.GetPosition(placementTarget)),
            Tag = new MenuState
            {
                Kind = kind,
                AllowPreview = allowPreview,
                AllowCustom = allowCustom,
                FallbackRate = currentValue,
                OnCommit = onCommit,
                OnPreview = onPreview,
                OnHighlight = onHighlight,
            },
        };

        var presets = Presets(kind);
        var cyan = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
        var matched = Array.IndexOf(presets, currentValue);
        for (var i = 0; i < presets.Length; i++)
        {
            var value = presets[i];
            var item = new MenuItem
            {
                Header = PickerChrome.Numbered(i + 1, FormatLabel(kind, value)),
                InputGestureText = (i + 1).ToString(CultureInfo.InvariantCulture),
                Tag = value,
                Icon = AccentMark(value == currentValue, cyan),
            };
            item.Click += (_, _) => onCommit(value);
            TipService.Set(item, KindTip(kind));
            menu.Items.Add(item);
        }

        if (allowCustom)
        {
            var box = PickerChrome.ValueBoxChars(PickerChrome.CustomRateChars);
            box.Text = currentValue.ToString(CultureInfo.InvariantCulture);
            ((MenuState)menu.Tag).CustomBox = box;
            var custom = new MenuItem
            {
                Header = BuildCustomHeader(presets.Length + 1, box),
                Tag = "custom",
                StaysOpenOnClick = true,
                Icon = AccentMark(matched < 0, cyan),
            };
            custom.PreviewMouseLeftButtonUp += (_, _) =>
            {
                HighlightByIndex(menu, presets.Length);
                box.Focus();
                box.SelectAll();
            };
            box.TextChanged += (_, _) => NotifyHighlight(menu);
            box.PreviewMouseWheel += (_, e) =>
            {
                if (NudgeCustomBox(box, currentValue, Math.Sign(e.Delta)))
                {
                    e.Handled = true;
                }
            };
            custom.PreviewMouseWheel += (_, e) =>
            {
                if (NudgeCustomBox(box, currentValue, Math.Sign(e.Delta)))
                {
                    e.Handled = true;
                }
            };
            TipService.Set(custom, UiStrings.TipFormatCustomRate);
            TipService.Set(box, UiStrings.TipFormatCustomRate);
            box.PreviewKeyDown += (_, e) =>
            {
                var key = e.Key == Key.System ? e.SystemKey : e.Key;
                if (key == Key.Tab)
                {
                    // 数字キーで他の選択肢を選べるように、フォーカスをエディタの外へ出す。
                    e.Handled = true;
                    if (box.FindAncestor<MenuItem>() is { } owner)
                    {
                        owner.Focus();
                        Keyboard.Focus(owner);
                    }

                    return;
                }

                if (key is Key.Up or Key.Down)
                {
                    e.Handled = true;
                    NudgeCustomBox(box, currentValue, key == Key.Up ? 1 : -1);
                    return;
                }

                if (key == Key.Enter)
                {
                    e.Handled = true;
                    if (TryReadCustom(box, out var customRate))
                    {
                        onCommit(customRate);
                    }

                    return;
                }

                if (key == Key.Space && allowPreview)
                {
                    e.Handled = true;
                    if (TryReadCustom(box, out var customRate))
                    {
                        onPreview?.Invoke(customRate);
                    }
                }
            };
            menu.Items.Add(custom);
        }

        TipService.Set(menu, KindTip(kind));
        WireHighlightTracking(menu);
        menu.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down && TryNudgeCustomRate(menu, key == Key.Up ? 1 : -1))
            {
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (IsCustomBoxFocused(menu))
            {
                return;
            }

            if (key == Key.Space && allowPreview)
            {
                e.Handled = true;
                if (TryHighlightedValue(menu, out var value))
                {
                    onPreview?.Invoke(value);
                }

                return;
            }

            if (TryDigitIndex(key, out var index) && HighlightByIndex(menu, index))
            {
                e.Handled = true;
            }
        };

        menu.Opened += (_, _) =>
        {
            menu.Dispatcher.BeginInvoke(
                () =>
                {
                    if (matched >= 0)
                    {
                        HighlightByIndex(menu, matched);
                    }
                    else if (allowCustom)
                    {
                        HighlightByIndex(menu, presets.Length);
                    }
                    else
                    {
                        HighlightByIndex(menu, 0);
                    }
                },
                DispatcherPriority.Input);
        };

        PickerChrome.FitListMenu(menu);
        menu.IsOpen = true;
        return menu;
    }

    public static bool HighlightByIndex(ContextMenu menu, int index)
    {
        if ((uint)index >= (uint)menu.Items.Count)
        {
            return false;
        }

        if (menu.Items[index] is not MenuItem target)
        {
            return false;
        }

        HighlightItem(menu, target);
        if (menu.Tag is MenuState { CustomBox: { } box } && Equals(target.Tag, "custom"))
        {
            box.Focus();
            box.SelectAll();
        }

        return true;
    }

    public static bool TryHighlightedValue(ContextMenu menu, out int value)
    {
        value = 0;
        var item = HighlightedItem(menu);
        if (item is null)
        {
            return false;
        }

        if (item.Tag is int preset)
        {
            value = preset;
            return true;
        }

        return menu.Tag is MenuState { CustomBox: { } box } && TryReadCustom(box, out value);
    }

    public static bool IsCustomBoxFocused(ContextMenu menu) =>
        menu.Tag is MenuState { CustomBox: { } box } && box.IsKeyboardFocusWithin;

    public static bool TryNudgeCustomRate(ContextMenu menu, int direction)
    {
        if (menu.Tag is not MenuState { CustomBox: { } box } state || !IsCustomActive(menu))
        {
            return false;
        }

        return NudgeCustomBox(box, state.FallbackRate, direction);
    }

    private static bool IsCustomActive(ContextMenu menu)
    {
        if (IsCustomBoxFocused(menu))
        {
            return true;
        }

        var item = HighlightedItem(menu);
        return item is not null && Equals(item.Tag, "custom");
    }

    private static bool NudgeCustomBox(TextBox box, int fallbackRate, int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (!TryReadRaw(box, out var current))
        {
            current = Math.Clamp(fallbackRate, FormatConvert.MinSampleRate, FormatConvert.MaxSampleRate);
        }

        var next = FormatConvert.ApplySampleRateNudge(
            current,
            direction,
            NudgeStep(Keyboard.Modifiers));
        var text = next.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(box.Text, text, StringComparison.Ordinal))
        {
            box.Text = text;
            box.CaretIndex = text.Length;
        }

        return true;
    }

    private static int NudgeStep(ModifierKeys modifiers)
    {
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var control = (modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        return FormatConvert.SampleRateNudgeStep(shift, control);
    }

    private static bool TryReadRaw(TextBox box, out int value)
    {
        value = 0;
        var text = box.Text.Trim().Replace(",", "", StringComparison.Ordinal);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static int[] Presets(FormatConvertKind kind) => kind switch
    {
        FormatConvertKind.BitDepth => FormatConvert.BitDepths,
        FormatConvertKind.Channels => FormatConvert.ChannelCounts,
        _ => FormatConvert.SampleRates,
    };

    private static string KindTip(FormatConvertKind kind) => kind switch
    {
        FormatConvertKind.BitDepth => UiStrings.TipFormatBitDepth,
        FormatConvertKind.Channels => UiStrings.TipFormatChannels,
        _ => UiStrings.TipFormatSampleRate,
    };

    private static string FormatLabel(FormatConvertKind kind, int value) => kind switch
    {
        FormatConvertKind.BitDepth => $"{value} bit",
        FormatConvertKind.Channels => value == 1
            ? $"1 ch  {UiStrings.LabelMono}"
            : $"2 ch  {UiStrings.LabelStereo}",
        _ => $"{value} Hz",
    };

    private static object BuildCustomHeader(int index, TextBox box)
    {
        var row = new DockPanel();
        var label = new TextBlock
        {
            Text = PickerChrome.Numbered(index, string.Empty),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        DockPanel.SetDock(label, Dock.Left);
        var unit = PickerChrome.Unit(UiStrings.LabelHertz);
        DockPanel.SetDock(unit, Dock.Right);
        row.Children.Add(label);
        row.Children.Add(unit);
        row.Children.Add(box);
        return row;
    }

    private static Border AccentMark(bool selected, Brush cyan) =>
        new()
        {
            Width = 10,
            Height = 10,
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? cyan : Brushes.Transparent,
            Background = selected ? cyan : Brushes.Transparent,
        };

    private static bool TryReadCustom(TextBox box, out int value)
    {
        value = 0;
        var text = box.Text.Trim().Replace(",", "", StringComparison.Ordinal);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
            && FormatConvert.IsValidSampleRate(value);
    }

    private static bool TryDigitIndex(Key key, out int index)
    {
        index = key switch
        {
            Key.D1 or Key.NumPad1 => 0,
            Key.D2 or Key.NumPad2 => 1,
            Key.D3 or Key.NumPad3 => 2,
            Key.D4 or Key.NumPad4 => 3,
            Key.D5 or Key.NumPad5 => 4,
            Key.D6 or Key.NumPad6 => 5,
            Key.D7 or Key.NumPad7 => 6,
            Key.D8 or Key.NumPad8 => 7,
            Key.D9 or Key.NumPad9 => 8,
            _ => -1,
        };
        return index >= 0;
    }

    private static MenuItem? HighlightedItem(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.IsHighlighted)
            {
                return item;
            }
        }

        if (CurrentSelectionProperty?.GetValue(menu) is MenuItem selected)
        {
            return selected;
        }

        return Keyboard.FocusedElement as MenuItem
            ?? (Keyboard.FocusedElement as DependencyObject)?.FindAncestor<MenuItem>();
    }

    private static void WireHighlightTracking(ContextMenu menu)
    {
        if (IsHighlightedDescriptor is null)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            IsHighlightedDescriptor.AddValueChanged(item, OnItemIsHighlightedChanged);
        }

        menu.Closed += OnMenuClosed;
    }

    private static void OnMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu)
        {
            return;
        }

        menu.Closed -= OnMenuClosed;
        if (IsHighlightedDescriptor is null)
        {
            return;
        }

        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            IsHighlightedDescriptor.RemoveValueChanged(item, OnItemIsHighlightedChanged);
        }
    }

    private static void OnItemIsHighlightedChanged(object? sender, EventArgs e)
    {
        if (_updatingSelectionChrome || sender is not MenuItem { IsHighlighted: true } item)
        {
            return;
        }

        var menu = ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu
            ?? item.Parent as ContextMenu;
        if (menu is null)
        {
            return;
        }

        SyncMarks(menu, item);
        NotifyHighlight(menu);
    }

    private static void NotifyHighlight(ContextMenu menu)
    {
        if (menu.Tag is MenuState { OnHighlight: { } onHighlight }
            && TryHighlightedValue(menu, out var value))
        {
            onHighlight(value);
        }
    }

    private static void HighlightItem(ContextMenu menu, MenuItem target)
    {
        _updatingSelectionChrome = true;
        try
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                SetHighlighted(item, ReferenceEquals(item, target));
            }

            if (!Equals(target.Tag, "custom"))
            {
                target.Focus();
                Keyboard.Focus(target);
            }

            CurrentSelectionProperty?.SetValue(menu, target);
            SyncMarks(menu, target);
        }
        finally
        {
            _updatingSelectionChrome = false;
        }

        NotifyHighlight(menu);
    }

    private static void SyncMarks(ContextMenu menu, MenuItem selected)
    {
        var cyan = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Icon is not Border mark)
            {
                continue;
            }

            var on = ReferenceEquals(item, selected);
            mark.BorderBrush = on ? cyan : Brushes.Transparent;
            mark.Background = on ? cyan : Brushes.Transparent;
        }
    }

    private static void SetHighlighted(MenuItem item, bool highlighted)
    {
        if (IsHighlightedKey is null)
        {
            return;
        }

        item.SetValue(IsHighlightedKey, highlighted);
    }
}

internal static class VisualTreeExtensions
{
    public static T? FindAncestor<T>(this DependencyObject? origin)
        where T : DependencyObject
    {
        while (origin is not null)
        {
            if (origin is T match)
            {
                return match;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }
}
