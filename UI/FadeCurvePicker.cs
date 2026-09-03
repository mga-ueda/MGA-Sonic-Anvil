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

    private static readonly DependencyPropertyDescriptor? IsHighlightedDescriptor =
        DependencyPropertyDescriptor.FromProperty(MenuItem.IsHighlightedProperty, typeof(MenuItem));

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        PlacementMode placement,
        bool fadeIn,
        Action<FadeShape> onCommit,
        Action<FadeShape> onPreview)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = placementTarget,
            Placement = placement,
            Tag = fadeIn,
        };

        FadeCurveIcons.AddCurveChoices(menu.Items, FadeCurves.Default, fadeIn, onCommit);
        WireHighlightTracking(menu);

        menu.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            e.Handled = true;
            onPreview(HighlightedShape(menu));
        };

        menu.Opened += (_, _) =>
        {
            menu.Dispatcher.BeginInvoke(
                () => HighlightDefault(menu),
                DispatcherPriority.Input);
        };

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

        return FadeCurves.Default;
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
        if (sender is not MenuItem { IsHighlighted: true, Tag: FadeShape shape } item)
        {
            return;
        }

        var menu = ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu
            ?? item.Parent as ContextMenu;
        if (menu is null || menu.Tag is not bool fadeIn)
        {
            return;
        }

        FadeCurveIcons.SyncSelectedIcons(menu.Items, shape, fadeIn);
        CurrentSelectionProperty?.SetValue(menu, item);
    }

    private static void HighlightDefault(ContextMenu menu)
    {
        MenuItem? target = null;
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag is FadeShape shape && shape == FadeCurves.Default)
            {
                target = item;
                break;
            }
        }

        if (target is null)
        {
            return;
        }

        target.Focus();
        Keyboard.Focus(target);
        CurrentSelectionProperty?.SetValue(menu, target);
        if (menu.Tag is bool fadeIn)
        {
            FadeCurveIcons.SyncSelectedIcons(menu.Items, FadeCurves.Default, fadeIn);
        }
    }
}
