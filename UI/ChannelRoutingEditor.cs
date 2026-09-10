using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed class ChannelRoutingEditor
{
    private readonly StackPanel _host;
    private readonly List<ComboBox> _boxes = [];
    private readonly List<ChannelLevelBar> _meters = [];
    private readonly bool _showMeters;
    private string[] _channelNames = [];
    private double _labelColumnWidth;
    private bool _rebuilding;

    public event Action? MapChanged;

    public ChannelRoutingEditor(StackPanel host, bool showMeters = false)
    {
        _host = host;
        _showMeters = showMeters;
    }

    public void Rebuild(string[] channelNames, string[] portNames, int[] currentMap)
    {
        _rebuilding = true;
        try
        {
            _channelNames = channelNames;
            _labelColumnWidth = MeasureLabelColumnWidth(channelNames);
            _host.Children.Clear();
            _boxes.Clear();
            _meters.Clear();
            for (var i = 0; i < channelNames.Length; i++)
            {
                var row = new Grid { Height = DesignMetrics.AudioInputHeight, Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_labelColumnWidth) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (_showMeters)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }

                var label = new TextBlock
                {
                    Text = channelNames[i],
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var box = new ComboBox
                {
                    Height = DesignMetrics.AudioInputHeight,
                    Padding = new Thickness(6, 1, 6, 1),
                    FontSize = 11.333,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(DesignMetrics.SettingsLabelComboGap, 0, 0, 0),
                };
                box.Items.Add(new PortItem(ChannelRouter.Off, UiStrings.LabelPortOff));
                for (var port = 0; port < portNames.Length; port++)
                {
                    box.Items.Add(new PortItem(port, portNames[port]));
                }

                var selected = currentMap.Length > i ? currentMap[i] : i;
                SelectPort(box, selected);
                box.SelectionChanged += (_, _) =>
                {
                    if (!_rebuilding)
                    {
                        MapChanged?.Invoke();
                    }
                };
                row.Children.Add(label);
                row.Children.Add(box);
                Grid.SetColumn(box, 1);
                if (_showMeters)
                {
                    var meter = new ChannelLevelBar { Margin = new Thickness(8, 0, 0, 0) };
                    TipService.Set(meter, UiStrings.TipInputLevel);
                    row.Children.Add(meter);
                    Grid.SetColumn(meter, 2);
                    _meters.Add(meter);
                }

                _host.Children.Add(row);
                _boxes.Add(box);
            }

            FitPortCombos();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    public void ApplyPeaks(ReadOnlySpan<float> peaks)
    {
        for (var i = 0; i < _meters.Count; i++)
        {
            _meters[i].ApplyLinearPeak(i < peaks.Length ? peaks[i] : 0);
        }
    }

    public int[] ReadMap()
    {
        var map = new int[_boxes.Count];
        for (var i = 0; i < _boxes.Count; i++)
        {
            map[i] = _boxes[i].SelectedItem is PortItem item ? item.Port : i;
        }

        return map;
    }

    public string[] ChannelNames => _channelNames;

    public void Refit() => FitPortCombos();

    public double FittedRowWidth
    {
        get
        {
            var combo = 0d;
            foreach (var box in _boxes)
            {
                combo = Math.Max(combo, box.Width);
            }

            if (combo < 1)
            {
                combo = 80;
            }

            return _labelColumnWidth
                + DesignMetrics.SettingsLabelComboGap
                + combo
                + (_showMeters ? DesignMetrics.SettingsLevelBarWidth + 8 : 0);
        }
    }

    public void ReplacePortNames(string[] portNames)
    {
        _rebuilding = true;
        try
        {
            foreach (var box in _boxes)
            {
                var selected = box.SelectedItem is PortItem item ? item.Port : ChannelRouter.Off;
                box.Items.Clear();
                box.Items.Add(new PortItem(ChannelRouter.Off, UiStrings.LabelPortOff));
                for (var port = 0; port < portNames.Length; port++)
                {
                    box.Items.Add(new PortItem(port, portNames[port]));
                }

                SelectPort(box, selected);
            }

            FitPortCombos();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private double MeasureLabelColumnWidth(string[] names)
    {
        var max = 0d;
        foreach (var name in names)
        {
            max = Math.Max(max, ComboBoxFit.MeasureText(_host, name, 12));
        }

        return Math.Ceiling(max);
    }

    private void FitPortCombos()
    {
        var max = Math.Max(80, SystemParameters.WorkArea.Width * 0.45);
        foreach (var box in _boxes)
        {
            ComboBoxFit.Apply(box, max);
        }
    }

    private static void SelectPort(ComboBox box, int port)
    {
        foreach (PortItem item in box.Items)
        {
            if (item.Port == port)
            {
                box.SelectedItem = item;
                return;
            }
        }

        box.SelectedIndex = 0;
    }

    private sealed record PortItem(int Port, string Label)
    {
        public override string ToString() => Label;
    }
}
