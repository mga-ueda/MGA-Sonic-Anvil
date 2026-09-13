using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MgaSonicAnvil.UI;

/// <summary>
/// WPF の TextEditor は変換中のクリック／フォーカス外れで CompleteComposition を呼ぶ。
/// TerminateComposition → OnEndComposition で FailFast し、例外では止められない。
/// 変換中は TextEditor にマウスとフォーカス移動を渡さない。IME を強制終了しない。
/// </summary>
internal static class ImeComposition
{
    private static bool _installed;
    private static bool _composing;

    public static bool IsComposing => _composing;

    public static void Install(Application app)
    {
        if (_installed || app is null)
        {
            return;
        }

        _installed = true;
        app.DispatcherUnhandledException += (_, e) =>
        {
            if (IsTerminateFailure(e.Exception))
            {
                _composing = false;
                e.Handled = true;
            }
        };

        EventManager.RegisterClassHandler(
            typeof(UIElement),
            TextCompositionManager.PreviewTextInputStartEvent,
            new TextCompositionEventHandler((_, _) => _composing = true));
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            TextCompositionManager.PreviewTextInputUpdateEvent,
            new TextCompositionEventHandler((_, _) => _composing = true));
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            TextCompositionManager.PreviewTextInputEvent,
            new TextCompositionEventHandler((_, _) => _composing = false));
        EventManager.RegisterClassHandler(
            typeof(TextBoxBase),
            UIElement.PreviewMouseDownEvent,
            new MouseButtonEventHandler(OnPreviewMouseDown),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(TextBoxBase),
            Keyboard.PreviewLostKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler(OnPreviewLostKeyboardFocus),
            handledEventsToo: true);
    }

    public static void Disable(DependencyObject target) =>
        InputMethod.SetIsInputMethodEnabled(target, false);

    public static bool ShouldBlockTextEditor(bool composing) => composing;

    public static bool ShouldInterceptImeMouse(bool imeEnabled) => imeEnabled;

    public static bool IsTerminateFailure(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is COMException or InvalidCastException or NullReferenceException
                && HasCompositionFrame(current.StackTrace))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HasCompositionFrame(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return false;
        }

        return stackTrace.Contains("TerminateComposition", StringComparison.Ordinal)
            || stackTrace.Contains("CompleteCurrentComposition", StringComparison.Ordinal)
            || stackTrace.Contains("CompleteComposition", StringComparison.Ordinal)
            || stackTrace.Contains("OnEndComposition", StringComparison.Ordinal);
    }

    private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not DependencyObject target || !ShouldInterceptImeMouse(InputMethod.GetIsInputMethodEnabled(target)))
        {
            return;
        }

        if (ShouldBlockTextEditor(_composing) || e.ChangedButton != MouseButton.Left || sender is not TextBox box)
        {
            e.Handled = true;
            return;
        }

        PlaceCaretFromMouse(box, e);
        e.Handled = true;
    }

    private static void PlaceCaretFromMouse(TextBox box, MouseButtonEventArgs e)
    {
        if (!box.IsKeyboardFocusWithin)
        {
            box.Focus();
        }

        var index = box.GetCharacterIndexFromPoint(e.GetPosition(box), snapToText: true);
        if (index < 0)
        {
            index = box.Text.Length;
        }

        if (e.ClickCount >= 2)
        {
            box.SelectAll();
            return;
        }

        box.CaretIndex = index;
        box.SelectionLength = 0;
    }

    private static void OnPreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!ShouldBlockTextEditor(_composing)
            || e.NewFocus is not DependencyObject dest
            || sender is not DependencyObject current)
        {
            return;
        }

        var from = Window.GetWindow(current);
        if (from is not null && from != Window.GetWindow(dest))
        {
            return;
        }

        e.Handled = true;
    }
}
