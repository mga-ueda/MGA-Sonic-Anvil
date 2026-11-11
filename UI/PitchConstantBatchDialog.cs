using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>一括編集向けの定数ピッチ入力。旧 <c>PitchCurveEditorDialog.cs</c> から分離（ダイアログ本体は廃止）。</summary>
internal static class PitchConstantBatchDialog
{
    public static bool TryPrompt(Window owner, out int semitones)
    {
        semitones = 0;
        var box = new TextBox
        {
            Text = "0",
            Width = DesignMetrics.From96(80),
            Margin = new Thickness(0, 8, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var chosen = 0;
        var ok = false;
        var window = new Window
        {
            Title = UiStrings.DialogPitchConstantTitle,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Foreground = (Brush)owner.FindResource("PrimaryForeBrush"),
        };
        AcrylicDialogChrome.Prepare(window);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new RoundedButton
        {
            Content = UiStrings.ButtonCancelMnemonic,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ActionButtonLooks.ApplyClear(cancel);
        AppDialogKeys.PrepareActionButton(cancel);
        cancel.Click += (_, _) => window.Close();
        var accept = new RoundedButton
        {
            Content = UiStrings.ButtonOkMnemonic,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ActionButtonLooks.ApplyAccent(accept);
        AppDialogKeys.PrepareActionButton(accept, isDefault: true);
        accept.Click += (_, _) =>
        {
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && !int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            {
                return;
            }

            chosen = PitchShift.Snap(value);
            ok = !PitchShift.IsNoOp(chosen);
            window.DialogResult = ok;
            window.Close();
        };
        // 他ダイアログと同じ: OK → Cancel
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var pitchLabel = new TextBlock { Text = UiStrings.LabelPitch };
        pitchLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        if (owner.TryFindResource("DarkTextBoxStyle") is Style flatStyle)
        {
            box.Style = flatStyle;
        }

        var root = new StackPanel();
        root.Children.Add(pitchLabel);
        root.Children.Add(box);
        root.Children.Add(buttons);
        AcrylicDialogChrome.SetBody(window, root, DesignMetrics.AudioPad);
        AppDialogKeys.Attach(window, () => window.Close());
        WindowPaintReveal.EnsureRevealedForDialog(owner);
        AcrylicDialogChrome.ShowDialog(window);
        semitones = chosen;
        return ok;
    }
}
