using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>フォルダ名／ファイル名を1行で聞く。</summary>
internal static class LibraryNameWindow
{
    public static string? Ask(Window? owner, string title, string initial, bool selectStem)
    {
        var dialog = new NameDialog(title, initial, selectStem);
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
        return dialog.Accepted ? dialog.Typed : null;
    }

    private sealed class NameDialog : Window
    {
        private readonly TextBox _box;

        public bool Accepted { get; private set; }

        public string Typed => _box.Text;

        public NameDialog(string title, string initial, bool selectStem)
        {
            Title = title;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = (System.Windows.Media.Brush)FindResource("WindowBackBrush");
            Foreground = (System.Windows.Media.Brush)FindResource("PrimaryForeBrush");
            _box = new TextBox
            {
                Text = initial,
                MinWidth = 320,
                Margin = new Thickness(0, 0, 0, 16),
            };
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var ok = new RoundedButton
            {
                Content = UiStrings.ButtonOk,
                MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
                Height = DesignMetrics.AudioDialogButtonHeight,
                Margin = new Thickness(8, 0, 0, 0),
            };
            var cancel = new RoundedButton
            {
                Content = UiStrings.LibraryCancel,
                MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
                Height = DesignMetrics.AudioDialogButtonHeight,
                Margin = new Thickness(8, 0, 0, 0),
            };
            AppDialogKeys.PrepareActionButton(ok, isDefault: true, isCancel: false);
            AppDialogKeys.PrepareActionButton(cancel, isDefault: false, isCancel: true);
            ActionButtonLooks.ApplyAccent(ok);
            ActionButtonLooks.ApplyClear(cancel);
            ok.Click += (_, _) =>
            {
                Accepted = true;
                Close();
            };
            cancel.Click += (_, _) => Close();
            row.Children.Add(ok);
            row.Children.Add(cancel);
            var root = new StackPanel();
            root.Children.Add(_box);
            root.Children.Add(row);
            Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };
            Loaded += (_, _) =>
            {
                _box.Focus();
                var length = selectStem ? DocumentFileNames.StemSelectLength(_box.Text) : _box.Text.Length;
                _box.Select(0, length);
            };
            WindowPaintReveal.Attach(this);
            AppDialogKeys.Attach(this, Close);
        }
    }
}
