using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>無音挿入。秒数を ↑↓／ホイールで動かし、Enter で挿入する。</summary>
internal static class SilenceInsertPicker
{
    private sealed class MenuState
    {
        public required Action<double> OnCommit { get; init; }
        public TextBox SecondsBox { get; set; } = null!;
        public double Seconds { get; set; }
        public bool UpdatingText { get; set; }
    }

    private static double _lastSeconds = 1;

    public static ContextMenu Show(FrameworkElement placementTarget, Action<double> onCommit)
    {
        var state = new MenuState
        {
            OnCommit = onCommit,
            Seconds = Snap(_lastSeconds),
        };
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
            StaysOpen = true,
            Tag = state,
        };

        BuildEditors(state);
        var labelWidth = PickerChrome.LabelColumnWidth(UiStrings.LabelSilenceSeconds);
        var root = PickerChrome.Panel();
        root.Children.Add(PickerChrome.TitleBar(UiStrings.LabelInsertSilence, menu));
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelSilenceSeconds, state.SecondsBox, UiStrings.LabelSecondsUnit, labelWidth));
        var item = PickerChrome.FormHost(root);
        menu.Items.Add(item);

        menu.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down)
            {
                e.Handled = true;
                TryNudge(menu, key == Key.Up ? 1 : -1);
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                onCommit(ReadSeconds(menu));
            }
        };

        menu.PreviewMouseWheel += (_, e) =>
        {
            if (TryNudge(menu, Math.Sign(e.Delta)))
            {
                e.Handled = true;
            }
        };

        menu.Opened += (_, _) =>
        {
            menu.Dispatcher.BeginInvoke(
                () =>
                {
                    state.SecondsBox.Focus();
                    state.SecondsBox.SelectAll();
                },
                DispatcherPriority.Input);
        };

        PickerChrome.FitFormMenu(menu);
        menu.IsOpen = true;
        return menu;
    }

    public static bool TryNudge(ContextMenu menu, int direction)
    {
        if (direction == 0 || menu.Tag is not MenuState state)
        {
            return false;
        }

        SetSeconds(state, state.Seconds + direction * NudgeStep(Keyboard.Modifiers));
        return true;
    }

    public static double ReadSeconds(ContextMenu menu) =>
        menu.Tag is MenuState state ? state.Seconds : _lastSeconds;

    public static double NudgeStep(ModifierKeys modifiers)
    {
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var control = (modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        if (shift && control)
        {
            return 60;
        }

        if (control)
        {
            return 10;
        }

        return shift ? 1 : 0.1;
    }

    public static double Snap(double seconds)
    {
        if (!double.IsFinite(seconds))
        {
            seconds = 1;
        }

        return Math.Round(
            Math.Clamp(seconds, ProcessEdits.InsertSilenceMinSeconds, ProcessEdits.InsertSilenceMaxSeconds),
            3,
            MidpointRounding.AwayFromZero);
    }

    private static void BuildEditors(MenuState state)
    {
        var box = PickerChrome.ValueBoxChars(7);
        box.Text = FormatSeconds(state.Seconds);
        state.SecondsBox = box;
        box.PreviewMouseWheel += (_, e) =>
        {
            var menu = box.FindAncestor<ContextMenu>();
            if (menu is not null && TryNudge(menu, Math.Sign(e.Delta)))
            {
                e.Handled = true;
            }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down)
            {
                e.Handled = true;
                var menu = box.FindAncestor<ContextMenu>();
                if (menu is not null)
                {
                    TryNudge(menu, key == Key.Up ? 1 : -1);
                }
            }
        };
        box.TextChanged += (_, _) =>
        {
            if (state.UpdatingText)
            {
                return;
            }

            if (TryParseSeconds(box.Text, out var parsed))
            {
                ApplySeconds(state, Snap(parsed), rewrite: false);
            }
        };
    }

    private static void SetSeconds(MenuState state, double seconds) =>
        ApplySeconds(state, Snap(seconds), rewrite: true);

    private static void ApplySeconds(MenuState state, double seconds, bool rewrite)
    {
        state.Seconds = seconds;
        _lastSeconds = seconds;
        if (!rewrite)
        {
            return;
        }

        state.UpdatingText = true;
        try
        {
            var text = FormatSeconds(seconds);
            if (!string.Equals(state.SecondsBox.Text, text, StringComparison.Ordinal))
            {
                state.SecondsBox.Text = text;
                state.SecondsBox.CaretIndex = text.Length;
            }
        }
        finally
        {
            state.UpdatingText = false;
        }
    }

    private static string FormatSeconds(double seconds) =>
        seconds.ToString("0.0##", CultureInfo.InvariantCulture);

    private static bool TryParseSeconds(string text, out double seconds)
    {
        seconds = 0;
        text = text.Trim()
            .Replace("秒", "", StringComparison.Ordinal)
            .Replace("s", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0 || text is "+" or "-" or "." or "+." or "-.")
        {
            return false;
        }

        return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
            && double.IsFinite(seconds)
            && seconds > 0;
    }
}
