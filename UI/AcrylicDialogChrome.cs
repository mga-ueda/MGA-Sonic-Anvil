using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace MgaSonicAnvil.UI;

/// <summary>
/// After Drive と同じ Acrylic すりガラス枠なしダイアログの共通準備。
/// </summary>
internal static class AcrylicDialogChrome
{
    private const double CaptionHeight = 32;

    public static void Prepare(
        Window window,
        ResizeMode resizeMode = ResizeMode.NoResize,
        SizeToContent sizeToContent = SizeToContent.WidthAndHeight,
        bool showInTaskbar = false,
        Action? onPainted = null,
        bool activateOnReveal = false)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = false;
        window.ResizeMode = resizeMode;
        window.ShowInTaskbar = showInTaskbar;
        window.SizeToContent = sizeToContent;
        window.Background = Brushes.Transparent;
        AcrylicBackdrop.Track(window);
        window.Activated += (_, _) => AcrylicBackdrop.Apply(window);

        if (resizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = CaptionHeight,
                ResizeBorderThickness = new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                UseAeroCaptionButtons = false,
            });
        }

        ArmReveal(window, onPainted, activateOnReveal);
    }

    /// <summary>
    /// 枠なし用のタイトル帯＋本体。OS タイトルバーの代わり（本文に見出しを重ねない）。
    /// </summary>
    public static void SetBody(Window window, UIElement body, Thickness bodyPadding)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(body);

        var title = new TextBlock
        {
            Text = window.Title,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");

        var caption = new Border
        {
            Height = CaptionHeight,
            Padding = new Thickness(14, 0, 14, 0),
            Background = Brushes.Transparent,
            Child = title,
        };
        caption.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
            }
        };

        var root = new DockPanel { Background = Brushes.Transparent };
        DockPanel.SetDock(caption, Dock.Top);
        root.Children.Add(caption);
        root.Children.Add(new Border
        {
            Padding = bodyPadding,
            Background = Brushes.Transparent,
            Child = body,
        });
        window.Content = root;
    }

    /// <summary>XAML／既存 Content をキャプション付きすりガラス本体へ包む。</summary>
    public static void AdoptContent(Window window, Thickness bodyPadding = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.Content is not UIElement body)
        {
            return;
        }

        window.Content = null;
        SetBody(window, body, bodyPadding);
    }

    public static bool? ShowDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        CloakForAcrylic(window);
        return window.ShowDialog();
    }

    public static void Show(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (window.IsVisible)
        {
            window.Show();
            AcrylicBackdrop.Apply(window);
            return;
        }

        CloakForAcrylic(window);
        window.Show();
    }

    private static void ArmReveal(Window window, Action? onPainted = null, bool activateOnReveal = false)
    {
        WindowPaintReveal.Attach(
            window,
            () =>
            {
                window.Background = Brushes.Transparent;
                AcrylicBackdrop.Apply(window);
                WindowPaintReveal.Reveal(window);
                AcrylicBackdrop.Apply(window);
                onPainted?.Invoke();
            },
            activateOnReveal,
            autoReveal: false);
    }

    private static void CloakForAcrylic(Window window)
    {
        _ = new WindowInteropHelper(window).EnsureHandle();
        DarkWindowChrome.DisableShowTransitions(window);
        DarkWindowChrome.SuppressEraseBackground(window);
        DarkWindowChrome.TrySetCloaked(window, true);
        AcrylicBackdrop.Apply(window);
    }
}
