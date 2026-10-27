using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal readonly record struct MatchLoudnessTargetResult(bool Accepted, double TargetLufs);

/// <summary>
/// ラウドネス揃えの簡易調整。既定ターゲットを上下／ホイールで 0.1／1 LKFS 刻み変更。
/// </summary>
internal sealed class MatchLoudnessTargetWindow : Window
{
    private readonly TextBox _targetBox;
    private bool _updatingText;
    private bool _accepted;

    public double TargetLufs { get; private set; }

    public static MatchLoudnessTargetResult Show(
        Window owner,
        int fileCount,
        double initialTargetLufs,
        bool saveAfter)
    {
        var dialog = new MatchLoudnessTargetWindow(fileCount, initialTargetLufs, saveAfter)
        {
            Owner = owner,
        };
        WindowPaintReveal.EnsureRevealedForDialog(owner);
        AcrylicDialogChrome.ShowDialog(dialog);
        return new MatchLoudnessTargetResult(dialog._accepted, dialog.TargetLufs);
    }

    private MatchLoudnessTargetWindow(int fileCount, double initialTargetLufs, bool saveAfter)
    {
        Title = UiStrings.LabelLoudnessTarget;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(this);
        TargetLufs = SnapTargetLufs(initialTargetLufs);

        var message = new TextBlock
        {
            Text = UiStrings.ConfirmMatchLoudnessTarget(fileCount, saveAfter),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 12),
        };

        _targetBox = PickerChrome.ValueBoxChars(7);
        _targetBox.Text = FormatTarget(TargetLufs);
        _targetBox.PreviewMouseWheel += (_, e) =>
        {
            if (TryNudge(Math.Sign(e.Delta)))
            {
                e.Handled = true;
            }
        };
        _targetBox.PreviewKeyDown += OnTargetBoxPreviewKeyDown;
        _targetBox.TextChanged += (_, _) =>
        {
            if (_updatingText)
            {
                return;
            }

            if (LoudnessMeterEngine.TryParseTargetLufs(_targetBox.Text, out var parsed))
            {
                TargetLufs = SnapTargetLufs(parsed);
            }
        };
        _targetBox.LostKeyboardFocus += (_, _) =>
        {
            if (!_updatingText)
            {
                SetTarget(TargetLufs);
            }
        };

        var unit = new TextBlock
        {
            Text = UiStrings.LabelLufs,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 16),
        };
        row.Children.Add(_targetBox);
        row.Children.Add(unit);

        var ok = CreateButton(UiStrings.ButtonOk, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancel, accept: false, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(row);
        root.Children.Add(buttons);

        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        PickerChrome.ReleaseInputOnBlankClick(this);
        AppDialogKeys.Attach(this, CancelAndClose);
        PreviewKeyDown += OnWindowPreviewKeyDown;
        Loaded += (_, _) =>
        {
            _targetBox.Focus();
            _targetBox.SelectAll();
        };
        OwnerCenteredMessageBox.PlayFor(MessageBoxImage.Question);
    }

    /// <summary>Shift で 1 LKFS、無修飾で 0.1 LKFS。</summary>
    public static double NudgeStep(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift ? 1d : 0.1d;

    public static double SnapTargetLufs(double value) =>
        LoudnessMeterEngine.ClampTargetLufs(
            Math.Round(value, 1, MidpointRounding.AwayFromZero));

    public static double NudgeTarget(double current, int direction, ModifierKeys modifiers)
    {
        if (direction == 0)
        {
            return SnapTargetLufs(current);
        }

        return SnapTargetLufs(current + direction * NudgeStep(modifiers));
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Enter
            && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows))
                == ModifierKeys.None)
        {
            // TextBox フォーカス中でも Enter は実行。
            if (TryAccept())
            {
                e.Handled = true;
            }
        }
    }

    private void OnTargetBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Up or Key.Down)
        {
            e.Handled = true;
            TryNudge(key == Key.Up ? 1 : -1);
            return;
        }

        if (key == Key.Enter
            && (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows))
                == ModifierKeys.None)
        {
            e.Handled = true;
            TryAccept();
        }
    }

    private bool TryNudge(int direction)
    {
        SetTarget(NudgeTarget(TargetLufs, direction, Keyboard.Modifiers));
        return true;
    }

    private void SetTarget(double targetLufs)
    {
        TargetLufs = SnapTargetLufs(targetLufs);
        var text = FormatTarget(TargetLufs);
        if (_targetBox.Text == text)
        {
            return;
        }

        _updatingText = true;
        try
        {
            _targetBox.Text = text;
            _targetBox.CaretIndex = text.Length;
        }
        finally
        {
            _updatingText = false;
        }
    }

    private bool TryAccept()
    {
        if (!LoudnessMeterEngine.TryParseTargetLufs(_targetBox.Text, out var parsed))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorLoudnessTargetRange,
                UiStrings.LabelLoudnessTarget,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            _targetBox.Focus();
            _targetBox.SelectAll();
            return false;
        }

        TargetLufs = SnapTargetLufs(parsed);
        _accepted = true;
        DialogResult = true;
        Close();
        return true;
    }

    private void CancelAndClose()
    {
        _accepted = false;
        DialogResult = false;
        Close();
    }

    private RoundedButton CreateButton(
        string text,
        bool accept,
        bool accent = false,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AppDialogKeys.PrepareActionButton(button, isDefault, isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        button.Click += (_, _) =>
        {
            if (accept)
            {
                TryAccept();
            }
            else
            {
                CancelAndClose();
            }
        };
        return button;
    }

    private static string FormatTarget(double targetLufs) =>
        SnapTargetLufs(targetLufs).ToString("0.#", CultureInfo.InvariantCulture);
}
