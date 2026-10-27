using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>
/// After Drive と同じ Acrylic すりガラス枠なしダイアログの共通準備。
/// </summary>
internal static class AcrylicDialogChrome
{
    private const double CaptionHeight = 32;

    public static void Prepare(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        window.WindowStyle = WindowStyle.None;
        window.AllowsTransparency = false;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        window.SizeToContent = SizeToContent.WidthAndHeight;
        window.Background = Brushes.Transparent;
        window.Activated += (_, _) => AcrylicBackdrop.Apply(window);

        WindowPaintReveal.Attach(
            window,
            () =>
            {
                window.Background = Brushes.Transparent;
                AcrylicBackdrop.Apply(window);
                WindowPaintReveal.Reveal(window);
                AcrylicBackdrop.Apply(window);
            },
            autoReveal: false);
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
        // 本体（スクロールバー／ボタン）のクリックを DragMove が奪わないよう、帯だけ。
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

    /// <summary>
    /// <see cref="WindowPaintReveal.ShowDialogWhenPainted"/> は autoReveal を上書きするため使わない。
    /// Acrylic 適用後にクローク解除する。
    /// </summary>
    public static bool? ShowDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        // Prepare 済みでも Show 直前に autoReveal=false を再設定する。
        WindowPaintReveal.Attach(
            window,
            () =>
            {
                window.Background = Brushes.Transparent;
                AcrylicBackdrop.Apply(window);
                WindowPaintReveal.Reveal(window);
                AcrylicBackdrop.Apply(window);
            },
            autoReveal: false);

        _ = new WindowInteropHelper(window).EnsureHandle();
        DarkWindowChrome.DisableShowTransitions(window);
        DarkWindowChrome.SuppressEraseBackground(window);
        DarkWindowChrome.TrySetCloaked(window, true);
        AcrylicBackdrop.Apply(window);
        return window.ShowDialog();
    }
}
