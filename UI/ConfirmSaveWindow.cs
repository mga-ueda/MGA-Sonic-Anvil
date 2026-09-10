using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum ConfirmSaveChoice
{
    Cancel,
    Save,
    Discard,
    SaveAll,
    DiscardAll,
}

/// <summary>未保存が複数あるときの保存確認。はい／いいえに加え、残りをまとめて終了できる。</summary>
internal sealed class ConfirmSaveWindow : Window
{
    public ConfirmSaveChoice Choice { get; private set; } = ConfirmSaveChoice.Cancel;

    public static ConfirmSaveChoice Show(Window owner, string displayName, int remainingDirty)
    {
        var dialog = new ConfirmSaveWindow(displayName, remainingDirty) { Owner = owner };
        dialog.ShowDialog();
        return dialog.Choice;
    }

    private ConfirmSaveWindow(string displayName, int remainingDirty)
    {
        Title = UiStrings.AppName;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)FindResource("WindowBackBrush");
        Foreground = (Brush)FindResource("PrimaryForeBrush");

        var message = new TextBlock
        {
            Text = remainingDirty >= 2
                ? UiStrings.ConfirmSaveFor(displayName) + Environment.NewLine + UiStrings.ConfirmSaveBatchHint(remainingDirty)
                : UiStrings.ConfirmSaveFor(displayName),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var yes = CreateButton(UiStrings.ButtonYes, ConfirmSaveChoice.Save, accent: true, isDefault: true);
        var no = CreateButton(UiStrings.ButtonNo, ConfirmSaveChoice.Discard);
        var saveAll = CreateButton(UiStrings.ButtonSaveAllAndExit, ConfirmSaveChoice.SaveAll);
        var discardAll = CreateButton(UiStrings.ButtonDiscardAllAndExit, ConfirmSaveChoice.DiscardAll);
        var cancel = CreateButton(UiStrings.ButtonCancel, ConfirmSaveChoice.Cancel, isCancel: true);

        var first = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8),
        };
        first.Children.Add(yes);
        first.Children.Add(no);
        first.Children.Add(cancel);

        var second = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        second.Children.Add(saveAll);
        second.Children.Add(discardAll);

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(first);
        root.Children.Add(second);

        Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };
        SourceInitialized += (_, _) => DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Choice = ConfirmSaveChoice.Cancel;
                e.Handled = true;
                Close();
            }
        };
        OwnerCenteredMessageBox.PlayFor(MessageBoxImage.Question);
    }

    private RoundedButton CreateButton(
        string text,
        ConfirmSaveChoice choice,
        bool accent = false,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = choice is ConfirmSaveChoice.SaveAll or ConfirmSaveChoice.DiscardAll
                ? DesignMetrics.SaveBatchButtonWidth
                : DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
            Focusable = true,
        };
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
            Choice = choice;
            DialogResult = choice != ConfirmSaveChoice.Cancel;
            Close();
        };
        return button;
    }
}
