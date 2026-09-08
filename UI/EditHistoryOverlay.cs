using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal sealed class EditHistoryOverlay : Border
{
    private readonly StackPanel _items = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;

    public event EventHandler<int>? ItemChosen;

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
            Margin = new Thickness(10, 8, 10, 2),
            FontSize = 11,
            Foreground = (Brush)Application.Current.FindResource("MutedForeBrush"),
        };
        _hint = new TextBlock
        {
            Text = UiStrings.EditHistoryHint,
            Margin = new Thickness(10, 0, 10, 6),
            FontSize = 10,
            Foreground = (Brush)Application.Current.FindResource("MutedForeBrush"),
        };
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 300,
            Content = _items,
        };

        var root = new DockPanel();
        DockPanel.SetDock(_title, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Top);
        root.Children.Add(_title);
        root.Children.Add(_hint);
        root.Children.Add(_scroll);
        Child = root;
    }

    public void ApplyLocalizedText()
    {
        _title.Text = UiStrings.EditHistoryTitle;
        _hint.Text = UiStrings.EditHistoryHint;
    }

    public void SetItems(IReadOnlyList<EditHistoryEntry> items, int selectedIndex)
    {
        _items.Children.Clear();
        var selectedBack = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var selectedFore = (Brush)Application.Current.FindResource("SurfaceBackBrush");
        var idleFore = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var futureFore = (Brush)Application.Current.FindResource("MutedForeBrush");
        FrameworkElement? selectedRow = null;
        foreach (var item in items)
        {
            var selected = item.Index == selectedIndex;
            var future = item.Index > selectedIndex;
            var row = new Border
            {
                Tag = item.Index,
                Padding = new Thickness(10, 4, 10, 4),
                Background = selected ? selectedBack : Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = selected ? selectedFore : future ? futureFore : idleFore,
            };
            row.Child = label;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (row.Tag is int index)
                {
                    ItemChosen?.Invoke(this, index);
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
