using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>V 用。dB を ↑↓／ホイールで動かし、波形全体の LKFS / RMS / Peak を先に出す。</summary>
internal static class VolumeGainPicker
{
    private sealed class MenuState
    {
        public required Action<double> OnCommit { get; init; }
        public required Action<double> OnPreview { get; init; }
        public required Action<double> OnChange { get; init; }
        public required double TargetLufs { get; init; }
        public TextBox GainBox { get; set; } = null!;
        public TextBlock LkfsBefore { get; set; } = null!;
        public TextBlock LkfsAfter { get; set; } = null!;
        public TextBlock RmsBefore { get; set; } = null!;
        public TextBlock RmsAfter { get; set; } = null!;
        public TextBlock PeakBefore { get; set; } = null!;
        public TextBlock PeakAfter { get; set; } = null!;
        public WaveformGainAnalyzer? Analyzer { get; set; }
        public double GainDb { get; set; }
        public bool UpdatingText { get; set; }
    }

    public static ContextMenu Show(
        FrameworkElement placementTarget,
        WaveformGainAnalyzer analyzer,
        double targetLufs,
        Action<double> onCommit,
        Action<double> onPreview,
        Action<double> onChange)
    {
        var state = new MenuState
        {
            OnCommit = onCommit,
            OnPreview = onPreview,
            OnChange = onChange,
            TargetLufs = targetLufs,
            Analyzer = analyzer,
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
            UiStrings.LabelVolume,
            UiStrings.LabelLufs,
            UiStrings.LabelRms,
            UiStrings.LabelPeak);
        var root = PickerChrome.Panel();
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelVolume, state.GainBox, UiStrings.LabelDb, labelWidth));
        root.Children.Add(
            PickerChrome.MetricRow(UiStrings.LabelLufs, state.LkfsBefore, state.LkfsAfter, labelWidth));
        root.Children.Add(
            PickerChrome.MetricRow(UiStrings.LabelRms, state.RmsBefore, state.RmsAfter, labelWidth));
        root.Children.Add(
            PickerChrome.MetricRow(UiStrings.LabelPeak, state.PeakBefore, state.PeakAfter, labelWidth));
        var item = PickerChrome.FormHost(root);
        menu.Items.Add(item);
        TipService.Set(menu, UiStrings.TipVolume);
        TipService.Set(item, UiStrings.TipVolume);
        TipService.Set(state.GainBox, UiStrings.TipVolume);

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
                onCommit(ReadGain(menu));
                return;
            }

            if (key == Key.Space && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                onPreview(ReadGain(menu));
            }
        };

        menu.PreviewMouseWheel += (_, e) =>
        {
            if (TryNudge(menu, Math.Sign(e.Delta)))
            {
                e.Handled = true;
            }
        };

        PaintReadings(state);
        menu.Opened += (_, _) =>
        {
            menu.Dispatcher.BeginInvoke(
                () =>
                {
                    state.GainBox.Focus();
                    state.GainBox.SelectAll();
                    onChange(state.GainDb);
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

        var step = NudgeStep(Keyboard.Modifiers);
        SetGain(state, state.GainDb + direction * step);
        return true;
    }

    public static double ReadGain(ContextMenu menu) =>
        menu.Tag is MenuState state ? state.GainDb : 0;

    public static double NudgeStep(ModifierKeys modifiers)
    {
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var control = (modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        if (shift && control)
        {
            return 6;
        }

        if (control)
        {
            return 3;
        }

        return shift ? 1 : 0.1;
    }

    private static void BuildEditors(MenuState state)
    {
        var box = PickerChrome.ValueBoxChars(5);
        box.Text = FormatGainBox(0);
        state.GainBox = box;
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

            if (TryParseGain(box.Text, out var parsed))
            {
                ApplyGain(state, WaveformGainAnalyzer.SnapGainDb(parsed), rewrite: false);
            }
        };

        var readingWidth = PickerChrome.CharBoxWidth(5);
        state.LkfsBefore = ReadingValue(readingWidth);
        state.LkfsAfter = ReadingValue(readingWidth);
        state.RmsBefore = ReadingValue(readingWidth);
        state.RmsAfter = ReadingValue(readingWidth);
        state.PeakBefore = ReadingValue(readingWidth);
        state.PeakAfter = ReadingValue(readingWidth);
    }

    private static TextBlock ReadingValue(double width)
    {
        var block = PickerChrome.MonoValue(emphasize: true);
        block.Width = width;
        block.MinWidth = width;
        return block;
    }

    private static void SetGain(MenuState state, double gainDb) =>
        ApplyGain(state, WaveformGainAnalyzer.SnapGainDb(gainDb), rewrite: true);

    private static void ApplyGain(MenuState state, double gainDb, bool rewrite)
    {
        if (Math.Abs(state.GainDb - gainDb) < 1e-9 && !rewrite)
        {
            PaintReadings(state);
            return;
        }

        state.GainDb = gainDb;
        if (rewrite)
        {
            state.UpdatingText = true;
            try
            {
                var text = FormatGainBox(gainDb);
                if (!string.Equals(state.GainBox.Text, text, StringComparison.Ordinal))
                {
                    state.GainBox.Text = text;
                    state.GainBox.CaretIndex = text.Length;
                }
            }
            finally
            {
                state.UpdatingText = false;
            }
        }

        PaintReadings(state);
        state.OnChange(gainDb);
    }

    private static void PaintReadings(MenuState state)
    {
        var current = state.Analyzer?.Current ?? new WaveformGainReading(
            float.NegativeInfinity,
            float.NegativeInfinity,
            float.NegativeInfinity);
        var next = state.Analyzer?.Predict(state.GainDb) ?? current;
        state.LkfsBefore.Text = FormatReading(current.IntegratedLufs);
        state.RmsBefore.Text = FormatReading(current.RmsDb);
        state.PeakBefore.Text = FormatReading(current.PeakDb);
        state.LkfsAfter.Text = FormatReading(next.IntegratedLufs);
        state.RmsAfter.Text = FormatReading(next.RmsDb);
        state.PeakAfter.Text = FormatReading(next.PeakDb);
        state.LkfsBefore.Foreground = TrafficBrush(LoudnessTrafficLight.ForLufs(current.IntegratedLufs, state.TargetLufs));
        state.LkfsAfter.Foreground = TrafficBrush(LoudnessTrafficLight.ForLufs(next.IntegratedLufs, state.TargetLufs));
        state.RmsBefore.Foreground = LevelBrush(current.RmsDb);
        state.RmsAfter.Foreground = LevelBrush(next.RmsDb);
        state.PeakBefore.Foreground = TrafficBrush(LoudnessTrafficLight.ForTruePeak(current.PeakDb));
        state.PeakAfter.Foreground = TrafficBrush(LoudnessTrafficLight.ForTruePeak(next.PeakDb));
    }

    private static Brush LevelBrush(float db)
    {
        if (!float.IsFinite(db))
        {
            return MutedBrush();
        }

        if (db >= 0)
        {
            return TrafficBrush(LoudnessTraffic.Danger);
        }

        return TrafficBrush(LoudnessTraffic.Safe);
    }

    private static Brush TrafficBrush(LoudnessTraffic traffic)
    {
        var key = traffic switch
        {
            LoudnessTraffic.Caution => "MarkerBrush",
            LoudnessTraffic.Danger => "StatusBarErrorDetailForeBrush",
            LoudnessTraffic.Safe => "VectorScopeTraceBrush",
            _ => "MutedForeBrush",
        };
        return WpfControlHelpers.FrozenBrush(Theme.Get(key));
    }

    private static Brush MutedBrush() =>
        WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));

    private static string FormatReading(float value)
    {
        if (!float.IsFinite(value))
        {
            return "--.-";
        }

        return value.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5);
    }

    private static string FormatGainBox(double gainDb) =>
        gainDb.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private static bool TryParseGain(string text, out double gainDb)
    {
        gainDb = 0;
        text = text.Trim().Replace("dB", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "", StringComparison.Ordinal);
        if (text.Length == 0 || text is "+" or "-" or "." or "+." or "-.")
        {
            return false;
        }

        return (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out gainDb)
                || double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out gainDb))
            && double.IsFinite(gainDb);
    }
}
