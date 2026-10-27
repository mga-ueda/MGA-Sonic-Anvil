using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>同梱 THIRD_PARTY_NOTICES.md を表示する Acrylic すりガラスダイアログ。</summary>
internal sealed class LicenseNoticesWindow : Window
{
    private const double BodyWidth = 560;
    private const double BodyMaxHeight = 420;

    public static void Show(Window? owner)
    {
        var dialog = new LicenseNoticesWindow();
        if (owner is not null)
        {
            WindowPaintReveal.EnsureRevealedForDialog(owner);
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        AcrylicDialogChrome.ShowDialog(dialog);
    }

    private LicenseNoticesWindow()
    {
        Title = UiStrings.DialogLicenseTitle;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        WindowIconHelper.Apply(this);
        AcrylicDialogChrome.Prepare(this);

        var text = new TextBlock
        {
            Text = LoadNoticesText(),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            Foreground = (Brush)FindResource("PrimaryForeBrush"),
        };

        var scroll = new ScrollViewer
        {
            Content = text,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Width = BodyWidth,
            MaxHeight = BodyMaxHeight,
            MinHeight = 160,
            Focusable = false,
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
        };

        var ok = new RoundedButton
        {
            Content = UiStrings.ButtonOk,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            IsDefault = true,
            IsCancel = true,
        };
        ActionButtonLooks.ApplyAccent(ok);
        AppDialogKeys.PrepareActionButton(ok, isDefault: true, isCancel: true);
        ok.Click += (_, _) =>
        {
            DialogResult = true;
            Close();
        };

        var root = new StackPanel();
        root.Children.Add(scroll);
        root.Children.Add(ok);
        AcrylicDialogChrome.SetBody(this, root, new Thickness(24, 8, 24, 18));

        AppDialogKeys.Attach(this, Close);
        Loaded += (_, _) => ok.Focus();
    }

    private static string LoadNoticesText()
    {
        if (ThirdPartyNotices.TryRead(out var text) && text.Length > 0)
        {
            return text;
        }

        return UiStrings.DialogLicenseMissing;
    }
}
