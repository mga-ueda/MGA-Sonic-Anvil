using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ダイアログ共通。Tab で入力とボタンを循環し、既定は Esc／Alt+C でキャンセル、Alt+O で既定ボタン。
/// </summary>
internal static class AppDialogKeys
{
    public static void Attach(
        Window window,
        Action onCancel,
        Func<bool>? interceptEscape = null,
        bool closeOnEscape = true,
        bool bindAltLetterShortcuts = true)
    {
        KeyboardNavigation.SetTabNavigation(window, KeyboardNavigationMode.Cycle);
        window.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var modifiers = Keyboard.Modifiers;

            if (bindAltLetterShortcuts)
            {
                // Alt+O: IsDefault の OK など（ラベル _OK）。Enter とは別に明示ショートカット。
                if (IsAltLetter(key, modifiers, Key.O))
                {
                    if (TryClickDefaultButton(window))
                    {
                        e.Handled = true;
                    }

                    return;
                }

                // Alt+C: Cancel／Close（ラベル _Cancel／_Close）。
                if (IsAltLetter(key, modifiers, Key.C))
                {
                    onCancel();
                    e.Handled = true;
                    return;
                }
            }

            if (!closeOnEscape || !IsEscape(key, modifiers))
            {
                return;
            }

            if (interceptEscape?.Invoke() == true)
            {
                e.Handled = true;
                return;
            }

            onCancel();
            e.Handled = true;
        };
        window.Loaded += (_, _) =>
        {
            if (window.Owner is { Topmost: true })
            {
                window.Topmost = true;
            }

            TryFocusInitial(window);
        };
    }

    public static void PrepareActionButton(
        Button button,
        bool isDefault = false,
        bool isCancel = false)
    {
        button.Focusable = true;
        button.IsTabStop = true;
        button.IsDefault = isDefault;
        button.IsCancel = isCancel;
        KeyboardNavigation.SetIsTabStop(button, true);
    }

    public static void AllowTabToLeave(TabControl tabs)
    {
        KeyboardNavigation.SetTabNavigation(tabs, KeyboardNavigationMode.Continue);
        KeyboardNavigation.SetControlTabNavigation(tabs, KeyboardNavigationMode.Continue);
    }

    internal static bool IsEscape(Key key, ModifierKeys modifiers) =>
        key == Key.Escape
        && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows))
            == ModifierKeys.None;

    internal static bool IsAltLetter(Key key, ModifierKeys modifiers, Key letter) =>
        key == letter
        && (modifiers & ModifierKeys.Alt) != 0
        && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0;

    internal static bool TryClickDefaultButton(Window window)
    {
        foreach (var child in Walk(window))
        {
            if (child is not Button button
                || !button.IsDefault
                || !button.IsEnabled
                || button.Visibility != Visibility.Visible)
            {
                continue;
            }

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        return false;
    }

    internal static bool IsInputTabStop(DependencyObject element)
    {
        if (element is not UIElement ui
            || !ui.Focusable
            || !ui.IsEnabled
            || ui.Visibility != Visibility.Visible
            || element is Button)
        {
            return false;
        }

        return KeyboardNavigation.GetIsTabStop(ui)
            && element is TextBox or ComboBox or CheckBox or ListBox or Slider;
    }

    internal static bool ShouldFocusDefaultButton(bool hasInputTabStop) => !hasInputTabStop;

    internal static void TryFocusInitial(Window window)
    {
        if (!ShouldFocusDefaultButton(HasInputTabStop(window)))
        {
            return;
        }

        FindDefaultOrFirstButton(window)?.Focus();
    }

    private static bool HasInputTabStop(DependencyObject root)
    {
        foreach (var child in Walk(root))
        {
            if (IsInputTabStop(child))
            {
                return true;
            }
        }

        return false;
    }

    private static Button? FindDefaultOrFirstButton(DependencyObject root)
    {
        Button? first = null;
        foreach (var child in Walk(root))
        {
            if (child is not Button button
                || !button.IsEnabled
                || button.Visibility != Visibility.Visible
                || !button.Focusable
                || !KeyboardNavigation.GetIsTabStop(button))
            {
                continue;
            }

            first ??= button;
            if (button.IsDefault)
            {
                return button;
            }
        }

        return first;
    }

    private static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var n = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Walk(child))
            {
                yield return nested;
            }
        }
    }
}
