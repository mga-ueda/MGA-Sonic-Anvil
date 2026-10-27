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

        if (owner is not null)
        {
            WindowPaintReveal.EnsureRevealedForDialog(owner);
        }

        AcrylicDialogChrome.ShowDialog(dialog);
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
            Foreground = (System.Windows.Media.Brush)FindResource("PrimaryForeBrush");
            AcrylicDialogChrome.Prepare(this);
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
            AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
            Loaded += (_, _) =>
            {
                _box.Focus();
                var length = selectStem ? DocumentFileNames.StemSelectLength(_box.Text) : _box.Text.Length;
                _box.Select(0, length);
            };
            AppDialogKeys.Attach(this, Close);
        }
    }
}
