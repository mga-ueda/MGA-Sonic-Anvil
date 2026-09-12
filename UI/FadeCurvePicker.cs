using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>I / O 用。TimeCaster と同じカーブ一覧を出し、既定は S 字。Space は試聴、Enter は確定。</summary>
internal static class FadeCurvePicker
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
        public required bool FadeIn { get; init; }
        public required FadeShape Initial { get; init; }
        public Action<FadeShape>? OnHighlight { get; init; }
    }

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        PlacementMode placement,
        bool fadeIn,
        Action<FadeShape> onCommit,
        Action<FadeShape> onPreview,
        Action<FadeShape>? onHighlight = null,
        FadeShape? initial = null)
    {
        var start = initial ?? FadeCurves.Default;
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = placement,
            StaysOpen = true,
            Tag = new MenuState { FadeIn = fadeIn, Initial = start, OnHighlight = onHighlight },
        };

        FadeCurveIcons.AddCurveChoices(menu.Items, start, fadeIn, onCommit);
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            item.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
                {
                    e.Handled = true;
                    onPreview(HighlightedShape(menu));
                }
            };
        }

        WireHighlightTracking(menu);

        menu.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (key == Key.Space)
            {
                e.Handled = true;
                onPreview(HighlightedShape(menu));
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
                () => HighlightShape(menu, start),
                DispatcherPriority.Input);
        };

        PickerChrome.FitListMenu(menu);
        menu.IsOpen = true;
        return menu;
    }

    public static FadeShape HighlightedShape(ContextMenu menu)
    {
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.IsHighlighted && item.Tag is FadeShape highlighted)
            {
                return highlighted;
            }
        }

        if (CurrentSelectionProperty?.GetValue(menu) is MenuItem selected
            && selected.Tag is FadeShape fromSelection)
        {
            return fromSelection;
        }

        if (Keyboard.FocusedElement is MenuItem focused && focused.Tag is FadeShape fromFocus)
        {
            return fromFocus;
        }

        return menu.Tag is MenuState state ? state.Initial : FadeCurves.Default;
    }

    public static bool HighlightByIndex(ContextMenu menu, int index)
    {
        if (menu.Tag is not MenuState state)
        {
            return false;
        }

        var order = FadeCurves.MenuOrder(state.FadeIn);
        if ((uint)index >= (uint)order.Count)
        {
            return false;
        }

        HighlightShape(menu, order[index]);
        return true;
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
        if (_updatingSelectionChrome
            || sender is not MenuItem { IsHighlighted: true, Tag: FadeShape shape } item)
        {
            return;
        }

        var menu = ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu
            ?? item.Parent as ContextMenu;
        if (menu is null)
        {
            return;
        }

        _updatingSelectionChrome = true;
        try
        {
            FadeCurveIcons.SyncSelectedBorder(menu.Items, shape);
            if (menu.Tag is MenuState { OnHighlight: { } onHighlight })
            {
                onHighlight(shape);
            }
        }
        finally
        {
            _updatingSelectionChrome = false;
        }
    }

    private static void HighlightShape(ContextMenu menu, FadeShape shape)
    {
        MenuItem? target = null;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is FadeShape tagged && tagged == shape)
            {
                target = item;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        _updatingSelectionChrome = true;
        try
        {
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                SetHighlighted(item, ReferenceEquals(item, target));
            }

            target.Focus();
            Keyboard.Focus(target);
            CurrentSelectionProperty?.SetValue(menu, target);
            FadeCurveIcons.SyncSelectedBorder(menu.Items, shape);
            if (menu.Tag is MenuState { OnHighlight: { } onHighlight })
            {
                onHighlight(shape);
            }
        }
        finally
        {
            _updatingSelectionChrome = false;
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
