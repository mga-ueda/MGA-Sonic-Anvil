using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>OK／Cancel（2択）と OK／No／Cancel（3択）をアプリの文言で出す確認。OS の MessageBox にはしない。</summary>
internal sealed class ConfirmChoiceWindow : Window
{
    private readonly Dictionary<MessageBoxResult, Button> _choiceButtons = [];
    private readonly MessageBoxButton _buttons;

    public MessageBoxResult Result { get; private set; }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxResult defaultResult,
        string? yesText = null,
        string? noText = null,
        string? cancelText = null,
        bool orangeNo = false)
    {
        var dialog = new ConfirmChoiceWindow(
            text, caption, buttons, defaultResult, yesText, noText, cancelText, orangeNo);
        if (owner is not null)
        {
            // 起動クローク中の本体を Owner にすると、ダイアログも見えず操作不能になる。
            WindowPaintReveal.EnsureRevealedForDialog(owner);
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        AcrylicDialogChrome.ShowDialog(dialog);
        return dialog.Result;
    }

    internal static bool UsesAppButtons(MessageBoxButton buttons) =>
        buttons is MessageBoxButton.OK
            or MessageBoxButton.OKCancel
            or MessageBoxButton.YesNo
            or MessageBoxButton.YesNoCancel;

    internal static MessageBoxResult DismissResult(MessageBoxButton buttons) =>
        buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };

    internal static bool IsDefaultChoice(
        MessageBoxResult choice,
        MessageBoxResult defaultResult,
        MessageBoxButton buttons)
    {
        if (defaultResult is MessageBoxResult.Yes or MessageBoxResult.No or MessageBoxResult.Cancel
            && (defaultResult != MessageBoxResult.Cancel || buttons == MessageBoxButton.YesNoCancel))
        {
            return choice == defaultResult;
        }

        return choice == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Alt+O／Y／N／C の割当。
    /// OK 系は O、Yes は Y（および OK ラベル時の O）、No は N、Cancel は C（YesNo の Cancel ラベル No 含む）。
    /// </summary>
    internal static MessageBoxResult? ChoiceForAltKey(Key key, MessageBoxButton buttons) =>
        key switch
        {
            Key.O when buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel => MessageBoxResult.OK,
            Key.O when buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            Key.Y when buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
            Key.N when buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            Key.C when buttons == MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            Key.C when buttons == MessageBoxButton.YesNo => MessageBoxResult.No,
            Key.C when buttons == MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            _ => null,
        };

    private ConfirmChoiceWindow(
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxResult defaultResult,
        string? yesText = null,
        string? noText = null,
        string? cancelText = null,
        bool orangeNo = false)
    {
        _buttons = buttons;
        Result = DismissResult(buttons);
        Title = caption;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(this);
        var message = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        if (buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel)
        {
            var okDefault = defaultResult != MessageBoxResult.Cancel;
            row.Children.Add(CreateButton(
                UiStrings.ButtonOkMnemonic,
                MessageBoxResult.OK,
                accent: true,
                isDefault: okDefault));
            if (buttons == MessageBoxButton.OKCancel)
            {
                row.Children.Add(CreateButton(
                    UiStrings.ButtonCancelMnemonic,
                    MessageBoxResult.Cancel,
                    isDefault: !okDefault));
            }
        }
        else if (buttons == MessageBoxButton.YesNo)
        {
            // 2択は OK／Cancel に統一（戻り値は従来どおり Yes／No）。
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(yesText) ? UiStrings.ButtonOkMnemonic : yesText,
                MessageBoxResult.Yes,
                accent: true,
                isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(noText) ? UiStrings.ButtonCancelMnemonic : noText,
                MessageBoxResult.No,
                orange: orangeNo,
                isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons)));
        }
        else
        {
            // 3択は OK／No／Cancel（中間の Discard／Skip 相当は No）。
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(yesText) ? UiStrings.ButtonOkMnemonic : yesText,
                MessageBoxResult.Yes,
                accent: true,
                isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(noText) ? UiStrings.ButtonNoMnemonic : noText,
                MessageBoxResult.No,
                orange: orangeNo,
                isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(cancelText) ? UiStrings.ButtonCancelMnemonic : cancelText,
                MessageBoxResult.Cancel,
                isDefault: IsDefaultChoice(MessageBoxResult.Cancel, defaultResult, buttons)));
        }

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(row);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        // Esc では閉じない。Alt+O／Y／N／C は下で結果ボタンへ。
        AppDialogKeys.Attach(
            this,
            onCancel: () => { },
            closeOnEscape: false,
            bindAltLetterShortcuts: false);
        PreviewKeyDown += OnChoicePreviewKeyDown;
    }

    private void OnChoicePreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        if (key is not (Key.O or Key.Y or Key.N or Key.C)
            || !AppDialogKeys.IsAltLetter(key, modifiers, key))
        {
            return;
        }

        var choice = ChoiceForAltKey(key, _buttons);
        if (choice is null || !TryClickChoice(choice.Value))
        {
            return;
        }

        e.Handled = true;
    }

    private bool TryClickChoice(MessageBoxResult choice)
    {
        if (!_choiceButtons.TryGetValue(choice, out var button) || !button.IsEnabled)
        {
            return false;
        }

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return true;
    }

    private RoundedButton CreateButton(
        string label,
        MessageBoxResult choice,
        bool accent = false,
        bool orange = false,
        bool isDefault = false)
    {
        var button = new RoundedButton
        {
            Content = label,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        // Esc で閉じないため IsCancel は付けない。Enter 用の IsDefault のみ。
        AppDialogKeys.PrepareActionButton(button, isDefault, isCancel: false);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else if (orange)
        {
            ActionButtonLooks.ApplyOrange(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        button.Click += (_, _) =>
        {
            Result = choice;
            Close();
        };
        _choiceButtons[choice] = button;
        return button;
    }
}
