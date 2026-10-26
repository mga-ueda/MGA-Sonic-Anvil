using System.Windows;
using System.Windows.Controls;
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

/// <summary>未保存が複数あるときの保存確認。OK／No／Cancel に加え、残りをまとめて閉じられる。</summary>
internal sealed class ConfirmSaveWindow : Window
{
    public ConfirmSaveChoice Choice { get; private set; } = ConfirmSaveChoice.Cancel;

    public static ConfirmSaveChoice Show(Window owner, string displayName, int remainingDirty) =>
        Show(owner, displayName, remainingDirty, closing: true);

    public static ConfirmSaveChoice Show(Window owner, string displayName, int remainingDirty, bool closing)
    {
        var dialog = new ConfirmSaveWindow(displayName, remainingDirty, closing) { Owner = owner };
        WindowPaintReveal.ShowDialogWhenPainted(dialog);
        return dialog.Choice;
    }

    private ConfirmSaveWindow(string displayName, int remainingDirty, bool closing)
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
                ? UiStrings.ConfirmSaveFor(displayName) + Environment.NewLine + (closing
                    ? UiStrings.ConfirmSaveBatchHint(remainingDirty)
                    : UiStrings.ConfirmSaveBeforePlayerHint(remainingDirty))
                : UiStrings.ConfirmSaveFor(displayName),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var yes = CreateButton(UiStrings.ButtonOk, ConfirmSaveChoice.Save, accent: true, isDefault: true);
        var no = CreateButton(UiStrings.ButtonNo, ConfirmSaveChoice.Discard);
        var saveAll = CreateButton(
            closing ? UiStrings.ButtonSaveAllAndClose : UiStrings.ButtonSaveAll,
            ConfirmSaveChoice.SaveAll);
        var discardAll = CreateButton(
            closing ? UiStrings.ButtonDiscardAllAndClose : UiStrings.ButtonDiscardAll,
            ConfirmSaveChoice.DiscardAll);
        var cancel = CreateButton(UiStrings.ButtonYesNoCancel, ConfirmSaveChoice.Cancel, isCancel: true);

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
        WindowPaintReveal.Attach(this);
        AppDialogKeys.Attach(this, () =>
        {
            Choice = ConfirmSaveChoice.Cancel;
            Close();
        });
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
            Choice = choice;
            DialogResult = choice != ConfirmSaveChoice.Cancel;
            Close();
        };
        return button;
    }
}
