using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum ConfirmRecordSilentSkipChoice
{
    Cancel,
    Record,
}

internal readonly record struct ConfirmRecordSilentSkipResult(
    ConfirmRecordSilentSkipChoice Choice,
    bool UseSilentSkip,
    bool AddRegion);

/// <summary>録音開始確認。Silent Skip のオン／オフを毎回聞く。</summary>
internal sealed class ConfirmRecordSilentSkipWindow : Window
{
    public ConfirmRecordSilentSkipChoice Choice { get; private set; } = ConfirmRecordSilentSkipChoice.Cancel;

    public bool UseSilentSkip { get; private set; }

    public bool AddRegion { get; private set; }

    public static ConfirmRecordSilentSkipResult Show(
        Window owner,
        double thresholdDb,
        int padMs,
        bool useSilentSkip,
        bool addRegion)
    {
        var dialog = new ConfirmRecordSilentSkipWindow(thresholdDb, padMs, useSilentSkip, addRegion)
        {
            Owner = owner,
        };
        dialog.ShowDialog();
        return new ConfirmRecordSilentSkipResult(dialog.Choice, dialog.UseSilentSkip, dialog.AddRegion);
    }

    private ConfirmRecordSilentSkipWindow(
        double thresholdDb,
        int padMs,
        bool useSilentSkip,
        bool addRegion)
    {
        Title = UiStrings.AppName;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = (Brush)FindResource("WindowBackBrush");
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        UseSilentSkip = useSilentSkip;
        AddRegion = addRegion;

        var message = new TextBlock
        {
            Text = UiStrings.ConfirmRecord
                + Environment.NewLine
                + Environment.NewLine
                + UiStrings.ConfirmRecordSilentSkip(thresholdDb, padMs),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 420,
            Margin = new Thickness(0, 0, 0, 16),
        };

        var style = TryFindResource("DarkCheckBoxStyle") as Style;
        var silentSkipBox = new CheckBox
        {
            Content = UiStrings.LabelConfirmRecordSilentSkip,
            IsChecked = useSilentSkip,
            Margin = new Thickness(0, 0, 0, 8),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var addRegionBox = new CheckBox
        {
            Content = UiStrings.LabelSilentSkipRecordAddRegion,
            IsChecked = addRegion,
            Margin = new Thickness(0, 0, 0, 16),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (style is not null)
        {
            silentSkipBox.Style = style;
            addRegionBox.Style = style;
        }

        TipService.Set(silentSkipBox, UiStrings.TipConfirmRecordSilentSkip);
        TipService.Set(addRegionBox, UiStrings.TipSilentSkipRecordAddRegion);

        void SyncAddRegionEnabled()
        {
            var on = silentSkipBox.IsChecked == true;
            UseSilentSkip = on;
            addRegionBox.IsEnabled = on;
            addRegionBox.Opacity = on ? 1 : 0.45;
        }

        silentSkipBox.Checked += (_, _) => SyncAddRegionEnabled();
        silentSkipBox.Unchecked += (_, _) => SyncAddRegionEnabled();
        silentSkipBox.Click += (_, _) => SyncAddRegionEnabled();
        addRegionBox.Checked += (_, _) => AddRegion = true;
        addRegionBox.Unchecked += (_, _) => AddRegion = false;
        SyncAddRegionEnabled();
        Loaded += (_, _) => SyncAddRegionEnabled();

        var ok = CreateButton(UiStrings.ButtonOk, ConfirmRecordSilentSkipChoice.Record, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancel, ConfirmRecordSilentSkipChoice.Cancel, isCancel: true);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(message);
        root.Children.Add(silentSkipBox);
        root.Children.Add(addRegionBox);
        root.Children.Add(buttons);

        Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };
        SourceInitialized += (_, _) => DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        AppDialogKeys.Attach(this, () =>
        {
            Choice = ConfirmRecordSilentSkipChoice.Cancel;
            Close();
        });
        OwnerCenteredMessageBox.PlayFor(MessageBoxImage.Question);
    }

    private RoundedButton CreateButton(
        string text,
        ConfirmRecordSilentSkipChoice choice,
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
            Choice = choice;
            DialogResult = choice != ConfirmRecordSilentSkipChoice.Cancel;
            Close();
        };
        return button;
    }
}
