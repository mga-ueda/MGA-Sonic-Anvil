using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>P 用。半音を ↑↓／ホイールで動かし、Shift で 1 オクターブ。</summary>
internal static class PitchShiftPicker
{
    private sealed class MenuState
    {
        public required Action<int, bool> OnCommit { get; init; }
        public required Action<int, bool> OnPreview { get; init; }
        public required Action<int, bool> OnChange { get; init; }
        public TextBox SemitoneBox { get; set; } = null!;
        public CheckBox TimeStretchBox { get; set; } = null!;
        public int Semitones { get; set; }
        public bool TimeStretch { get; set; } = true;
        public bool UpdatingText { get; set; }
    }

    public static bool LastTimeStretch { get; private set; } = true;

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        Action<int, bool> onCommit,
        Action<int, bool> onPreview,
        Action<int, bool> onChange)
    {
        var state = new MenuState
        {
            OnCommit = onCommit,
            OnPreview = onPreview,
            OnChange = onChange,
            TimeStretch = LastTimeStretch,
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
        var root = PickerChrome.Panel();
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelPitch, state.SemitoneBox, UiStrings.LabelSemitone));
        root.Children.Add(state.TimeStretchBox);
        PickerChrome.PrependClose(root, menu);
        var item = PickerChrome.FormHost(root);
        menu.Items.Add(item);
        TipService.Set(menu, UiStrings.TipPitch);
        TipService.Set(item, UiStrings.TipPitch);
        TipService.Set(state.SemitoneBox, UiStrings.TipPitch);
        TipService.Set(state.TimeStretchBox, UiStrings.TipPitchTimeStretch);

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
                onCommit(ReadSemitones(menu), ReadTimeStretch(menu));
                return;
            }

            if (key == Key.Tab)
            {
                e.Handled = true;
                TryMoveFocus(menu, reverse: (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift);
                return;
            }

            if (key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (IsTimeStretchFocused(menu))
                {
                    e.Handled = true;
                    TryToggleTimeStretch(menu);
                    return;
                }

                e.Handled = true;
                onPreview(ReadSemitones(menu), ReadTimeStretch(menu));
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
                    state.SemitoneBox.Focus();
                    state.SemitoneBox.SelectAll();
                    onChange(state.Semitones, state.TimeStretch);
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

        var octave = (Keyboard.Modifiers & (ModifierKeys.Shift | ModifierKeys.Control)) != 0;
        SetSemitones(state, state.Semitones + direction * PitchShift.NudgeStep(octave));
        return true;
    }

    public static int ReadSemitones(ContextMenu menu) =>
        menu.Tag is MenuState state ? state.Semitones : 0;

    public static bool ReadTimeStretch(ContextMenu menu) =>
        menu.Tag is not MenuState state || state.TimeStretch;

    public static bool IsTimeStretchFocused(ContextMenu menu) =>
        menu.Tag is MenuState state && state.TimeStretchBox.IsKeyboardFocusWithin;

    public static bool TryMoveFocus(ContextMenu menu, bool reverse)
    {
        if (menu.Tag is not MenuState state)
        {
            return false;
        }

        if (reverse ? !state.TimeStretchBox.IsKeyboardFocusWithin : state.TimeStretchBox.IsKeyboardFocusWithin)
        {
            state.SemitoneBox.Focus();
            state.SemitoneBox.SelectAll();
        }
        else
        {
            state.TimeStretchBox.Focus();
        }

        return true;
    }

    public static bool TryToggleTimeStretch(ContextMenu menu)
    {
        if (menu.Tag is not MenuState state)
        {
            return false;
        }

        SetTimeStretch(state, !state.TimeStretch);
        return true;
    }

    private static void BuildEditors(MenuState state)
    {
        var box = PickerChrome.ValueBoxChars(4);
        box.Text = FormatBox(0);
        state.SemitoneBox = box;
        KeyboardNavigation.SetTabIndex(box, 0);
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

            if (TryParse(box.Text, out var parsed))
            {
                ApplySemitones(state, PitchShift.Snap(parsed), rewrite: false);
            }
        };

        var stretch = PickerChrome.Option(UiStrings.LabelPitchTimeStretch);
        stretch.IsChecked = state.TimeStretch;
        KeyboardNavigation.SetTabIndex(stretch, 1);
        state.TimeStretchBox = stretch;
        stretch.Checked += (_, _) => SetTimeStretch(state, true);
        stretch.Unchecked += (_, _) => SetTimeStretch(state, false);
    }

    private static void SetSemitones(MenuState state, int semitones) =>
        ApplySemitones(state, PitchShift.Snap(semitones), rewrite: true);

    private static void ApplySemitones(MenuState state, int semitones, bool rewrite)
    {
        if (state.Semitones == semitones && !rewrite)
        {
            return;
        }

        state.Semitones = semitones;
        if (rewrite)
        {
            state.UpdatingText = true;
            try
            {
                var text = FormatBox(semitones);
                if (!string.Equals(state.SemitoneBox.Text, text, StringComparison.Ordinal))
                {
                    state.SemitoneBox.Text = text;
                    state.SemitoneBox.CaretIndex = text.Length;
                }
            }
            finally
            {
                state.UpdatingText = false;
            }
        }

        state.OnChange(semitones, state.TimeStretch);
    }

    private static void SetTimeStretch(MenuState state, bool timeStretch)
    {
        if (state.TimeStretch == timeStretch
            && state.TimeStretchBox.IsChecked == timeStretch)
        {
            return;
        }

        state.TimeStretch = timeStretch;
        LastTimeStretch = timeStretch;
        if (state.TimeStretchBox.IsChecked != timeStretch)
        {
            state.TimeStretchBox.IsChecked = timeStretch;
        }

        state.OnChange(state.Semitones, timeStretch);
    }

    private static string FormatBox(int semitones) =>
        semitones.ToString("+0;-0;0", CultureInfo.InvariantCulture);

    private static bool TryParse(string text, out int semitones)
    {
        semitones = 0;
        text = text.Trim()
            .Replace("st", "", StringComparison.OrdinalIgnoreCase)
            .Replace("半音", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0 || text is "+" or "-")
        {
            return false;
        }

        if (!(int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out semitones)
            || int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out semitones)))
        {
            return false;
        }

        semitones = PitchShift.Snap(semitones);
        return true;
    }
}
