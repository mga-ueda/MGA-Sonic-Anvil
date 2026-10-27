using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>バージョン情報（アプリアイコン・名前・版・ライセンス）。Acrylic すりガラス。</summary>
internal sealed class AboutWindow : Window
{
    private const double AppIconSize = 80;

    public static void Show(Window? owner)
    {
        var dialog = new AboutWindow();
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

    private AboutWindow()
    {
        Title = UiStrings.DialogAboutTitle;
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        WindowIconHelper.Apply(this);
        AcrylicDialogChrome.Prepare(this);

        var icon = new Image
        {
            Width = AppIconSize,
            Height = AppIconSize,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 14),
        };
        if (WindowIconHelper.GetIcon() is { } frame)
        {
            icon.Source = frame;
        }

        var name = new TextBlock
        {
            Text = AppVersion.ProductName,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        };
        var version = new TextBlock
        {
            Text = UiStrings.AboutVersionLabel(AppVersion.Current),
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
            Foreground = (Brush)FindResource("MutedForeBrush"),
        };

        var license = new BrandLicenseLabel
        {
            LinkText = UiStrings.CopyrightText,
            FontSize = DesignMetrics.BrandLicenseFontSize,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)FindResource("ActionCopyrightForeBrush"),
            Margin = new Thickness(0, 0, 0, 18),
        };
        license.LinkClick += OnLicenseLinkClick;

        var ok = new RoundedButton
        {
            Content = UiStrings.ButtonOk,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            HorizontalAlignment = HorizontalAlignment.Center,
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

        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MinWidth = 360,
        };
        root.Children.Add(icon);
        root.Children.Add(name);
        root.Children.Add(version);
        root.Children.Add(license);
        root.Children.Add(ok);
        Content = new Border
        {
            Padding = new Thickness(28, 20, 28, 20),
            Background = Brushes.Transparent,
            Child = root,
        };

        // バージョンはキャプション無し。余白ドラッグのみ（ボタン／リンクは対象外）。
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left
                || e.OriginalSource is DependencyObject source
                    && (FindAncestor<RoundedButton>(source) is not null
                        || FindAncestor<BrandLicenseLabel>(source) is not null))
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        AppDialogKeys.Attach(this, Close);
    }

    private static T? FindAncestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (DependencyObject? current = start;
            current is not null;
            current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private void OnLicenseLinkClick(object? sender, BrandLicenseLinkClickEventArgs e)
    {
        if (e.LinkId == "effetune")
        {
            LicenseNoticesWindow.Show(this);
            e.Handled = true;
            return;
        }

        var url = e.LinkId switch
        {
            "mit" => AppVersion.LicenseUrl,
            "lame" => AppVersion.LameProjectUrl,
            "ffmpeg" => AppVersion.FfmpegProjectUrl,
            _ => AppVersion.RepositoryUrl,
        };
        TryOpenUrl(url);
        e.Handled = true;
    }

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
        }
    }
}
