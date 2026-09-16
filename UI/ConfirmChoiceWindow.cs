using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>はい／いいえ（／キャンセル）をアプリの文言で出す確認。OS の MessageBox にはしない。</summary>
internal sealed class ConfirmChoiceWindow : Window
{
    public MessageBoxResult Result { get; private set; }

    public static MessageBoxResult Show(
        Window? owner,
        string text,
        string caption,
        MessageBoxButton buttons,
        MessageBoxResult defaultResult)
    {
        var dialog = new ConfirmChoiceWindow(text, caption, buttons, defaultResult);
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
        buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;

    internal static MessageBoxResult DismissResult(MessageBoxButton buttons) =>
        buttons == MessageBoxButton.YesNoCancel ? MessageBoxResult.Cancel : MessageBoxResult.No;

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
        MessageBoxResult defaultResult)
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
        row.Children.Add(CreateButton(
            UiStrings.ButtonYes,
            MessageBoxResult.Yes,
            accent: true,
            isDefault: IsDefaultChoice(MessageBoxResult.Yes, defaultResult, buttons)));
        row.Children.Add(CreateButton(
            UiStrings.ButtonNo,
            MessageBoxResult.No,
            isDefault: IsDefaultChoice(MessageBoxResult.No, defaultResult, buttons),
            isCancel: buttons == MessageBoxButton.YesNo));
        if (buttons == MessageBoxButton.YesNoCancel)
        {
            row.Children.Add(CreateButton(
                UiStrings.ButtonYesNoCancel,
                MessageBoxResult.Cancel,
                isDefault: IsDefaultChoice(MessageBoxResult.Cancel, defaultResult, buttons),
                isCancel: true));
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
