using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>OK／Cancel（2択）と OK／No／Cancel（3択）をアプリの文言で出す確認。OS の MessageBox にはしない。</summary>
internal sealed class ConfirmChoiceWindow : Window
{
    public MessageBoxResult Result { get; private set; }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxResult defaultResult,
        string? yesText = null,
        string? noText = null,
        string? cancelText = null)
    {
        var dialog = new ConfirmChoiceWindow(
            text, caption, buttons, defaultResult, yesText, noText, cancelText);
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

    private ConfirmChoiceWindow(
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxResult defaultResult,
        string? yesText = null,
        string? noText = null,
        string? cancelText = null)
    {
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
                UiStrings.ButtonOk,
                MessageBoxResult.OK,
                accent: true,
                isDefault: okDefault,
                isCancel: buttons == MessageBoxButton.OK));
            if (buttons == MessageBoxButton.OKCancel)
            {
                row.Children.Add(CreateButton(
                    UiStrings.ButtonCancel,
                    MessageBoxResult.Cancel,
                    isDefault: !okDefault,
                    isCancel: true));
            }
        }
        else if (buttons == MessageBoxButton.YesNo)
        {
            // 2択は OK／Cancel に統一（戻り値は従来どおり Yes／No）。
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(yesText) ? UiStrings.ButtonOk : yesText,
                MessageBoxResult.Yes,
                accent: true,
                isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(noText) ? UiStrings.ButtonCancel : noText,
                MessageBoxResult.No,
                isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons),
                isCancel: true));
        }
        else
        {
            // 3択は OK／No／Cancel（中間の Discard／Skip 相当は No）。
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(yesText) ? UiStrings.ButtonOk : yesText,
                MessageBoxResult.Yes,
                accent: true,
                isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(noText) ? UiStrings.ButtonNo : noText,
                MessageBoxResult.No,
                isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(cancelText) ? UiStrings.ButtonYesNoCancel : cancelText,
                MessageBoxResult.Cancel,
                isDefault: IsDefaultChoice(MessageBoxResult.Cancel, defaultResult, buttons),
                isCancel: true));
        }

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(row);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        AppDialogKeys.Attach(this, () =>
        {
            Result = DismissResult(buttons);
            Close();
        });
    }

    private RoundedButton CreateButton(
        string label,
        MessageBoxResult choice,
        bool accent = false,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = label,
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
            Result = choice;
            Close();
        };
        return button;
    }
}
