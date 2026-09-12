using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>履歴オーバーレイの行クリック（修飾キー付き）。</summary>
internal readonly record struct HistoryItemClick(int Index, ModifierKeys Modifiers);

internal sealed class EditHistoryOverlay : Border
{
    private readonly StackPanel _items = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _statusTimer;

    public event EventHandler<HistoryItemClick>? ItemClicked;

    public event EventHandler? CloseRequested;

    public EditHistoryOverlay()
    {
        Width = 340;
        MaxHeight = 360;
        Padding = new Thickness(0, 0, 0, 6);
        Background = (Brush)Application.Current.FindResource("ColorPanelBackBrush");
        BorderBrush = (Brush)Application.Current.FindResource("ChromeBorderBrush");
        BorderThickness = new Thickness(1);
        SnapsToDevicePixels = true;
        Focusable = false;

        _title = new TextBlock
        {
            Text = UiStrings.EditHistoryTitle,
            Margin = new Thickness(10, 8, 4, 2),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)Application.Current.FindResource("MutedForeBrush"),
        };
        var caption = new DockPanel();
        var close = OverlayCaption.CloseButton(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        OverlayCaption.PinCorner(close);
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        caption.Children.Add(_title);
        _hint = new TextBlock
        {
            Text = UiStrings.EditHistoryHint,
            Margin = new Thickness(10, 0, 10, 6),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.FindResource("MutedForeBrush"),
        };
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 300,
            Content = _items,
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _hint.Text = UiStrings.EditHistoryHint;
            _hint.Foreground = (Brush)Application.Current.FindResource("MutedForeBrush");
        };

        var root = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Top);
        root.Children.Add(caption);
        root.Children.Add(_hint);
        root.Children.Add(_scroll);
        Child = root;
    }

    public void ApplyLocalizedText()
    {
        _statusTimer.Stop();
        _title.Text = UiStrings.EditHistoryTitle;
        _hint.Text = UiStrings.EditHistoryHint;
        _hint.Foreground = (Brush)Application.Current.FindResource("MutedForeBrush");
    }

    /// <summary>ヒント行に一時メッセージ（コピー／適用件数など）を表示する。</summary>
    public void FlashStatus(string text)
    {
        _hint.Text = text;
        _hint.Foreground = (Brush)Application.Current.FindResource("AccentCyanBrush");
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public void SetItems(
        IReadOnlyList<EditHistoryEntry> items,
        int selectedIndex,
        IReadOnlySet<int>? copySelected = null)
    {
        _items.Children.Clear();
        var selectedBack = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var selectedFore = (Brush)Application.Current.FindResource("SurfaceBackBrush");
        var idleFore = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var futureFore = (Brush)Application.Current.FindResource("MutedForeBrush");
        var copyMark = (Brush)Application.Current.FindResource("AccentCyanBrush");
        FrameworkElement? selectedRow = null;
        foreach (var item in items)
        {
            var selected = item.Index == selectedIndex;
            var future = item.Index > selectedIndex;
            var copying = copySelected is not null && copySelected.Contains(item.Index);
            var row = new Border
            {
                Tag = item.Index,
                Padding = new Thickness(8, 4, 10, 4),
                Background = selected ? selectedBack : Brushes.Transparent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = copying ? copyMark : Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = copying && !selected
                    ? copyMark
                    : selected ? selectedFore : future ? futureFore : idleFore,
            };
            row.Child = label;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (row.Tag is int index)
                {
                    ItemClicked?.Invoke(this, new HistoryItemClick(index, Keyboard.Modifiers));
                }
            };
            _items.Children.Add(row);
            if (selected)
            {
                selectedRow = row;
            }
        }

        selectedRow?.BringIntoView();
    }
}
