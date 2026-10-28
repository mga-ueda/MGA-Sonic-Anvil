using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
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
            Margin = new Thickness(0, 0, 0, 6),
            Foreground = (Brush)FindResource("MutedForeBrush"),
        };
        var releases = CreateReleasesLink();

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
        root.Children.Add(releases);
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
            if (e.ChangedButton != MouseButton.Left
                || e.OriginalSource is DependencyObject source && IsInteractiveChrome(source))
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

    private TextBlock CreateReleasesLink()
    {
        var link = new Hyperlink(new Run(UiStrings.AboutReleasesLink))
        {
            TextDecorations = null,
            Cursor = Cursors.Hand,
            Focusable = false,
            Foreground = (Brush)FindResource("ActionLinkForeBrush"),
        };
        link.Click += (_, _) => TryOpenUrl(AppVersion.ReleasesUrl);
        link.MouseEnter += (_, _) =>
            link.Foreground = (Brush)FindResource("ActionLinkHoverForeBrush");
        link.MouseLeave += (_, _) =>
            link.Foreground = (Brush)FindResource("ActionLinkForeBrush");

        return new TextBlock
        {
            FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 14),
            Inlines = { link },
        };
    }

    private static bool IsInteractiveChrome(DependencyObject start)
    {
        for (DependencyObject? current = start; current is not null;)
        {
            if (current is RoundedButton or BrandLicenseLabel or Hyperlink)
            {
                return true;
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return false;
    }

    private void OnLicenseLinkClick(object? sender, BrandLicenseLinkClickEventArgs e)
    {
        var url = e.LinkId switch
        {
            "mit" => AppVersion.LicenseUrl,
            "lame" => AppVersion.LameProjectUrl,
            "ffmpeg" => AppVersion.FfmpegProjectUrl,
            "effetune" => AppVersion.EffetuneProjectUrl,
            "winblur" => AppVersion.WinBlurProjectUrl,
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
