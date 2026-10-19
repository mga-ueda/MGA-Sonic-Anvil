using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>設定の Wwise タブ。文字列と 28 色のどれかを並べる。</summary>
internal sealed class FileNameColorMarkerSettingsEditor
{
    private readonly StackPanel _host;
    private readonly RoundedButton _addButton;
    private readonly List<Row> _rows = [];

    public FileNameColorMarkerSettingsEditor(StackPanel host, RoundedButton addButton)
    {
        _host = host;
        _addButton = addButton;
        _addButton.Click += (_, _) => AddRow(string.Empty, colorIndex: 0, focus: true);
    }

    public void Load(IEnumerable<FileNameColorRule>? rules)
    {
        _host.Children.Clear();
        _rows.Clear();
        foreach (var rule in FileNameColorRules.Normalize(rules))
        {
            AddRow(rule.Pattern, rule.ColorIndex, focus: false);
        }

        KeepAddButtonLast();
    }

    public FileNameColorRule[] Read() =>
        FileNameColorRules.Normalize(_rows.Select(row => new FileNameColorRule
        {
            Pattern = row.Pattern.Text,
            ColorIndex = row.Color.SelectedIndex,
        }));

    private void AddRow(string pattern, int colorIndex, bool focus)
    {
        var box = new TextBox
        {
            Text = pattern,
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (_host.TryFindResource("DarkTextBoxStyle") is Style textStyle)
        {
            box.Style = textStyle;
        }

        var color = new FileNameColorPalettePicker(colorIndex);

        var up = MoveButton(UiStrings.ButtonFileNameColorMarkerUp);
        var down = MoveButton(UiStrings.ButtonFileNameColorMarkerDown);
        var remove = new RoundedButton
        {
            Content = UiStrings.ButtonFileNameColorMarkerRemove,
            Height = DesignMetrics.AudioInputHeight,
            MinWidth = 64,
            Padding = new Thickness(10, 0, 10, 0),
        };
        ActionButtonLooks.ApplyClear(remove);
        AppDialogKeys.PrepareActionButton(remove);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        Grid.SetColumn(color, 1);
        Grid.SetColumn(up, 2);
        Grid.SetColumn(down, 3);
        Grid.SetColumn(remove, 4);
        grid.Children.Add(box);
        grid.Children.Add(color);
        grid.Children.Add(up);
        grid.Children.Add(down);
        grid.Children.Add(remove);

        var row = new Row(box, color, up, down, grid);
        up.Click += (_, _) => Move(row, -1);
        down.Click += (_, _) => Move(row, 1);
        remove.Click += (_, _) => Remove(row);
        _rows.Add(row);
        _host.Children.Add(grid);
        KeepAddButtonLast();
        SyncMoveButtons();
        if (focus)
        {
            box.Focus();
            box.SelectAll();
        }
    }

    private void Remove(Row row)
    {
        _rows.Remove(row);
        _host.Children.Remove(row.Host);
        SyncMoveButtons();
    }

    private void Move(Row row, int delta)
    {
        var index = _rows.IndexOf(row);
        var next = index + delta;
        if (index < 0 || next < 0 || next >= _rows.Count)
        {
            return;
        }

        _rows.RemoveAt(index);
        _rows.Insert(next, row);
        _host.Children.RemoveAt(index);
        _host.Children.Insert(next, row.Host);
        SyncMoveButtons();
        row.Host.BringIntoView();
    }

    private void KeepAddButtonLast()
    {
        if (_host.Children.Contains(_addButton))
        {
            _host.Children.Remove(_addButton);
        }

        _host.Children.Add(_addButton);
    }

    private void SyncMoveButtons()
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            _rows[i].Up.IsEnabled = i > 0;
            _rows[i].Down.IsEnabled = i < _rows.Count - 1;
        }
    }

    private static RoundedButton MoveButton(string label)
    {
        var button = new RoundedButton
        {
            Content = label,
            Height = DesignMetrics.AudioInputHeight,
            MinWidth = 36,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0, 0, 8, 0),
        };
        ActionButtonLooks.ApplyClear(button);
        AppDialogKeys.PrepareActionButton(button);
        return button;
    }

    private sealed class Row(TextBox pattern, FileNameColorPalettePicker color, RoundedButton up, RoundedButton down, Grid host)
    {
        public TextBox Pattern { get; } = pattern;

        public FileNameColorPalettePicker Color { get; } = color;

        public RoundedButton Up { get; } = up;

        public RoundedButton Down { get; } = down;

        public Grid Host { get; } = host;
    }
}
