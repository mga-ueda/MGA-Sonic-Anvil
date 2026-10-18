using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>OK／Cancel、Yes／No をアプリの文言で出す確認。OS の MessageBox にはしない。</summary>
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
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        WindowPaintReveal.ShowDialogWhenPainted(dialog);
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
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)FindResource("WindowBackBrush");
        Foreground = (Brush)FindResource("PrimaryForeBrush");
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
        else
        {
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(yesText) ? UiStrings.ButtonYes : yesText,
                MessageBoxResult.Yes,
                accent: true,
                isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
            row.Children.Add(CreateButton(
                string.IsNullOrEmpty(noText) ? UiStrings.ButtonNo : noText,
                MessageBoxResult.No,
                isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons),
                isCancel: buttons == MessageBoxButton.YesNo));
            if (buttons == MessageBoxButton.YesNoCancel)
            {
                row.Children.Add(CreateButton(
                    string.IsNullOrEmpty(cancelText) ? UiStrings.ButtonYesNoCancel : cancelText,
                    MessageBoxResult.Cancel,
                    isDefault: IsDefaultChoice(MessageBoxResult.Cancel, defaultResult, buttons),
                    isCancel: true));
            }
        }

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(row);
        Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };

        WindowPaintReveal.Attach(this);
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
