using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>選択範囲の無音化。体裁は無音挿入と同じで、範囲の秒数と前後フェードを出す。</summary>
internal static class SilenceSelectionPicker
{
    private sealed class MenuState
    {
        public required Action OnCommit { get; init; }
        public TextBox SecondsBox { get; set; } = null!;
        public CheckBox EdgeFadeBox { get; set; } = null!;
        public bool EdgeFade { get; set; }
    }

    private static bool _lastEdgeFade = true;

    public static ContextMenu Show(FrameworkElement placementTarget, double seconds, Action onCommit)
    {
        var state = new MenuState
        {
            OnCommit = onCommit,
            EdgeFade = _lastEdgeFade,
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

        var box = PickerChrome.ValueBoxChars(7);
        box.Text = FormatSeconds(seconds);
        box.IsReadOnly = true;
        state.SecondsBox = box;

        var edge = PickerChrome.Option(UiStrings.LabelInsertSilenceEdgeFade);
        edge.IsChecked = state.EdgeFade;
        state.EdgeFadeBox = edge;
        edge.Checked += (_, _) => SetEdgeFade(state, true);
        edge.Unchecked += (_, _) => SetEdgeFade(state, false);

        var labelWidth = PickerChrome.LabelColumnWidth(UiStrings.LabelSilenceSeconds);
        var root = PickerChrome.Panel();
        root.Children.Add(PickerChrome.TitleBar(UiStrings.LabelSilenceSelection, menu));
        root.Children.Add(
            PickerChrome.FieldRow(UiStrings.LabelSilenceSeconds, box, UiStrings.LabelSecondsUnit, labelWidth));
        root.Children.Add(edge);
        menu.Items.Add(PickerChrome.FormHost(root));

        menu.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down)
            {
                e.Handled = true;
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                onCommit();
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

    public static bool ReadEdgeFade(ContextMenu menu) =>
        menu.Tag is not MenuState state || state.EdgeFade;

    private static void SetEdgeFade(MenuState state, bool edgeFade)
    {
        state.EdgeFade = edgeFade;
        _lastEdgeFade = edgeFade;
    }

    private static string FormatSeconds(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        return seconds.ToString("0.0##", CultureInfo.InvariantCulture);
    }
}
