using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>T 用。時間と割合を相互に連動させ、↑↓／ホイールで動かす。</summary>
internal static class TimeStretchPicker
{
    private sealed class MenuState
    {
        public required Action<int> OnCommit { get; init; }
        public required Action<int> OnPreview { get; init; }
        public required Action<int> OnChange { get; init; }
        public required int SourceFrames { get; init; }
        public required int SampleRate { get; init; }
        public required bool ShowSamples { get; init; }
        public TextBox TimeBox { get; set; } = null!;
        public TextBox PercentBox { get; set; } = null!;
        public TextBlock SourceText { get; set; } = null!;
        public int DestFrames { get; set; }
        public bool UpdatingText { get; set; }
    }

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        int sourceFrames,
        int sampleRate,
        Action<int> onCommit,
        Action<int> onPreview,
        Action<int> onChange)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        var state = new MenuState
        {
            OnCommit = onCommit,
            OnPreview = onPreview,
            OnChange = onChange,
            SourceFrames = sourceFrames,
            SampleRate = Math.Max(1, sampleRate),
            ShowSamples = AppStorage.Settings.StatusShowSamples,
            DestFrames = sourceFrames,
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
        var labelWidth = PickerChrome.LabelColumnWidth(
            UiStrings.LabelTimeStretchSource,
            UiStrings.LabelTimeStretchDest,
            UiStrings.LabelTimeStretchPercent);
        var root = PickerChrome.Panel();
        root.Children.Add(PickerChrome.TitleBar(UiStrings.LabelTimeStretch, menu));
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelTimeStretchSource, state.SourceText, null, labelWidth));
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelTimeStretchDest, state.TimeBox, null, labelWidth));
        root.Children.Add(
            PickerChrome.FieldRow(
                UiStrings.LabelTimeStretchPercent,
                state.PercentBox,
                UiStrings.LabelPercent,
                labelWidth));
        var item = PickerChrome.FormHost(root);
        menu.Items.Add(item);
        TipService.Set(menu, UiStrings.TipTimeStretch);
        TipService.Set(item, UiStrings.TipTimeStretch);
        TipService.Set(state.TimeBox, UiStrings.TipTimeStretchTime);
        TipService.Set(state.PercentBox, UiStrings.TipTimeStretchPercent);

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
                onCommit(ReadDestFrames(menu));
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
                e.Handled = true;
                onPreview(ReadDestFrames(menu));
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
                    state.TimeBox.Focus();
                    state.TimeBox.SelectAll();
                    onChange(state.DestFrames);
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

        if (IsPercentFocused(menu))
        {
            var percent = TimeStretch.PercentOf(state.SourceFrames, state.DestFrames)
                + direction * TimeStretch.PercentNudgeStep(
                    (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift,
                    (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
                    && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.None);
            SetDestFrames(state, TimeStretch.DestFrameCount(state.SourceFrames, percent));
            return true;
        }

        var step = StatusTimeEdit.NudgeStep(state.ShowSamples, state.SampleRate, Keyboard.Modifiers);
        SetDestFrames(state, TimeStretch.ClampDestFrames(state.SourceFrames, state.DestFrames + direction * (int)step));
        return true;
    }

    public static int ReadDestFrames(ContextMenu menu) =>
        menu.Tag is MenuState state ? state.DestFrames : 0;

    public static bool IsPercentFocused(ContextMenu menu) =>
        menu.Tag is MenuState state && state.PercentBox.IsKeyboardFocusWithin;

    public static bool TryMoveFocus(ContextMenu menu, bool reverse)
    {
        if (menu.Tag is not MenuState state)
        {
            return false;
        }

        if (reverse ? !state.PercentBox.IsKeyboardFocusWithin : state.PercentBox.IsKeyboardFocusWithin)
        {
            state.TimeBox.Focus();
            state.TimeBox.SelectAll();
        }
        else
        {
            state.PercentBox.Focus();
            state.PercentBox.SelectAll();
        }

        return true;
    }

    private static void BuildEditors(MenuState state)
    {
        var fieldWidth = PickerChrome.CharBoxWidth(state.ShowSamples
            ? Math.Max(10, state.SourceFrames.ToString(CultureInfo.InvariantCulture).Length)
            : 10);
        state.SourceText = PickerChrome.MonoValue();
        state.SourceText.Width = fieldWidth;
        state.SourceText.MinWidth = fieldWidth;
        var timeBox = PickerChrome.ValueBox(fieldWidth);
        state.TimeBox = timeBox;
        KeyboardNavigation.SetTabIndex(timeBox, 0);
        BindBox(timeBox, state, percent: false);
        var percentBox = PickerChrome.ValueBox(fieldWidth);
        state.PercentBox = percentBox;
        KeyboardNavigation.SetTabIndex(percentBox, 1);
        BindBox(percentBox, state, percent: true);
        WriteBoxes(state, rewriteTime: true, rewritePercent: true);
    }

    private static void BindBox(TextBox box, MenuState state, bool percent)
    {
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

            if (percent)
            {
                if (TryParsePercent(box.Text, out var parsed))
                {
                    SetDestFrames(state, TimeStretch.DestFrameCount(state.SourceFrames, parsed), rewritePercent: false);
                }

                return;
            }

            if (UiStrings.TryParseStatusTime(box.Text, state.SampleRate, state.ShowSamples, out var frames))
            {
                SetDestFrames(state, TimeStretch.ClampDestFrames(state.SourceFrames, (int)Math.Min(int.MaxValue, frames)), rewriteTime: false);
            }
        };
    }

    private static void SetDestFrames(MenuState state, int destFrames, bool rewriteTime = true, bool rewritePercent = true)
    {
        destFrames = TimeStretch.ClampDestFrames(state.SourceFrames, destFrames);
        if (state.DestFrames == destFrames)
        {
            WriteBoxes(state, rewriteTime, rewritePercent);
            return;
        }

        state.DestFrames = destFrames;
        WriteBoxes(state, rewriteTime, rewritePercent);
        state.OnChange(destFrames);
    }

    private static void WriteBoxes(MenuState state, bool rewriteTime, bool rewritePercent)
    {
        state.UpdatingText = true;
        try
        {
            state.SourceText.Text = FormatTime(state.SourceFrames, state.SampleRate, state.ShowSamples);
            if (rewriteTime)
            {
                var text = FormatTime(state.DestFrames, state.SampleRate, state.ShowSamples);
                if (!string.Equals(state.TimeBox.Text, text, StringComparison.Ordinal))
                {
                    state.TimeBox.Text = text;
                    state.TimeBox.CaretIndex = text.Length;
                }
            }

            if (rewritePercent)
            {
                var text = FormatPercent(TimeStretch.PercentOf(state.SourceFrames, state.DestFrames));
                if (!string.Equals(state.PercentBox.Text, text, StringComparison.Ordinal))
                {
                    state.PercentBox.Text = text;
                    state.PercentBox.CaretIndex = text.Length;
                }
            }
        }
        finally
        {
            state.UpdatingText = false;
        }
    }

    private static string FormatTime(int frames, int sampleRate, bool showSamples) =>
        UiStrings.FormatStatusTime(frames, sampleRate, showSamples);

    private static string FormatPercent(double percent) =>
        percent.ToString("0.0", CultureInfo.InvariantCulture);

    private static bool TryParsePercent(string text, out double percent)
    {
        percent = 0;
        text = text.Trim()
            .Replace("%", "", StringComparison.Ordinal)
            .Replace("％", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0 || text is "+" or "-" or "." or "+." or "-.")
        {
            return false;
        }

        return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out percent))
            && double.IsFinite(percent);
    }
}
