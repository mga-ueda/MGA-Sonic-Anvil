using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed class ChannelRoutingEditor
{
    private readonly StackPanel _host;
    private readonly List<ComboBox> _boxes = [];
    private readonly List<ComboBox> _fileBoxes = [];
    private readonly List<ChannelLevelBar> _meters = [];
    private readonly List<RoundedButton> _sineButtons = [];
    private readonly List<RoundedButton?> _voiceButtons = [];
    private readonly bool _showMeters;
    private readonly bool _showTestButtons;
    private string[] _channelNames = [];
    private double _labelColumnWidth;
    private double _fileComboWidth;
    private double _testButtonsWidth;
    private bool _rebuilding;

    public event Action? MapChanged;

    public event Action<int, SettingsProbeKind>? TestClicked;

    public ChannelRoutingEditor(StackPanel host, bool showMeters = false, bool showTestButtons = false)
    {
        _host = host;
        _showMeters = showMeters;
        _showTestButtons = showTestButtons;
    }

    public void Rebuild(string[] channelNames, string[] portNames, int[] currentMap, int[]? fileMap = null)
    {
        _rebuilding = true;
        try
        {
            _channelNames = channelNames;
            _labelColumnWidth = MeasureLabelColumnWidth(channelNames);
            _host.Children.Clear();
            _boxes.Clear();
            _fileBoxes.Clear();
            _meters.Clear();
            _sineButtons.Clear();
            _voiceButtons.Clear();
            var lanes = ChannelRouter.Normalize(fileMap, channelNames.Length, ChannelLayout.MaxChannels);
            for (var i = 0; i < channelNames.Length; i++)
            {
                var row = new Grid { Height = DesignMetrics.AudioInputHeight, Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_labelColumnWidth) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (_showMeters)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (_showTestButtons)
                {
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                }

                var label = new TextBlock
                {
                    Text = channelNames[i],
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var box = CreateIndexCombo();
                box.Items.Add(new PortItem(ChannelRouter.Off, UiStrings.LabelPortOff));
                for (var port = 0; port < portNames.Length; port++)
                {
                    box.Items.Add(new PortItem(port, portNames[port]));
                }

                var selected = currentMap.Length > i ? currentMap[i] : i;
                SelectPort(box, selected);
                box.SelectionChanged += OnComboChanged;
                row.Children.Add(label);
                row.Children.Add(box);
                Grid.SetColumn(box, 1);
                var next = 2;
                if (_showMeters)
                {
                    var meter = new ChannelLevelBar { Margin = new Thickness(8, 0, 0, 0), Channel = i };
                    TipService.Set(meter, UiStrings.TipInputLevel);
                    row.Children.Add(meter);
                    Grid.SetColumn(meter, next);
                    _meters.Add(meter);
                    next++;
                }

                var fileBox = CreateIndexCombo();
                fileBox.Margin = new Thickness(DesignMetrics.SettingsLabelComboGap, 0, 0, 0);
                fileBox.Items.Add(new PortItem(ChannelRouter.Off, UiStrings.LabelPortOff));
                for (var lane = 0; lane < ChannelLayout.MaxChannels; lane++)
                {
                    fileBox.Items.Add(new PortItem(lane, UiStrings.LabelWaveformLane(lane + 1)));
                }

                SelectPort(fileBox, lanes.Length > i ? lanes[i] : i);
                fileBox.SelectionChanged += OnComboChanged;
                TipService.Set(fileBox, UiStrings.TipFileChannelMap);
                row.Children.Add(fileBox);
                Grid.SetColumn(fileBox, next);
                next++;
                _fileBoxes.Add(fileBox);

                if (_showTestButtons)
                {
                    var channel = i;
                    var sine = CreateTestButton(UiStrings.ButtonSineMinusTwenty, UiStrings.TipSineMinusTwenty);
                    sine.Click += (_, _) => TestClicked?.Invoke(channel, SettingsProbeKind.Sine);
                    row.Children.Add(sine);
                    Grid.SetColumn(sine, next);
                    _sineButtons.Add(sine);
                    if (SettingsTone.IsLfe(channelNames[i]))
                    {
                        _voiceButtons.Add(null);
                    }
                    else
                    {
                        var voice = CreateTestButton(UiStrings.ButtonChannelVoice, UiStrings.TipChannelVoice);
                        voice.Click += (_, _) => TestClicked?.Invoke(channel, SettingsProbeKind.Voice);
                        row.Children.Add(voice);
                        Grid.SetColumn(voice, next + 1);
                        _voiceButtons.Add(voice);
                    }
                }

                _host.Children.Add(row);
                _boxes.Add(box);
            }

            FitPortCombos();
            FitFileCombos();
            FitTestButtons();
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

    public int[] ReadMap() => ReadBoxes(_boxes);

    public int[] ReadFileMap() => ReadBoxes(_fileBoxes);

    public void SetFileMap(int[] map)
    {
        _rebuilding = true;
        try
        {
            for (var i = 0; i < _fileBoxes.Count; i++)
            {
                SelectPort(_fileBoxes[i], i < map.Length ? map[i] : i);
            }
        }
        finally
        {
            _rebuilding = false;
        }
    }

    public string[] ChannelNames => _channelNames;

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
                + (_showMeters ? DesignMetrics.SettingsLevelBarWidth + 8 : 0)
                + DesignMetrics.SettingsLabelComboGap
                + Math.Max(_fileComboWidth, DesignMetrics.SettingsFileLaneComboMinWidth)
                + _testButtonsWidth;
        }
    }

    public void SetTestState(int channel, SettingsProbeKind? kind)
    {
        for (var i = 0; i < _sineButtons.Count; i++)
        {
            if (kind == SettingsProbeKind.Sine && i == channel)
            {
                ActionButtonLooks.ApplyAccent(_sineButtons[i]);
            }
            else
            {
                ActionButtonLooks.ApplyClear(_sineButtons[i]);
            }

            if (i >= _voiceButtons.Count || _voiceButtons[i] is not { } voice)
            {
                continue;
            }

            if (kind == SettingsProbeKind.Voice && i == channel)
            {
                ActionButtonLooks.ApplyAccent(voice);
            }
            else
            {
                ActionButtonLooks.ApplyClear(voice);
            }
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
            FitFileCombos();
            FitTestButtons();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    public void Refit()
    {
        FitPortCombos();
        FitFileCombos();
        FitTestButtons();
    }

    private void OnComboChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_rebuilding)
        {
            MapChanged?.Invoke();
        }
    }

    private ComboBox CreateIndexCombo() =>
        new()
        {
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(DesignMetrics.SettingsLabelComboGap, 0, 0, 0),
        };

    private RoundedButton CreateTestButton(string label, string tip)
    {
        var button = new RoundedButton
        {
            Content = label,
            Height = DesignMetrics.AudioInputHeight,
            Width = DesignMetrics.SettingsSineButtonWidth,
            Margin = new Thickness(DesignMetrics.SettingsTestButtonGap, 0, 0, 0),
            FontSize = 11.333,
            Padding = new Thickness(8, 1, 8, 1),
        };
        ActionButtonLooks.ApplyClear(button);
        TipService.Set(button, tip);
        return button;
    }

    private void FitTestButtons()
    {
        if (!_showTestButtons)
        {
            _testButtonsWidth = 0;
            return;
        }

        var sine = Math.Max(
            DesignMetrics.SettingsSineButtonWidth,
            ComboBoxFit.MeasureText(_host, UiStrings.ButtonSineMinusTwenty, 11.333, FontWeights.Bold) + 20);
        var voice = Math.Max(
            DesignMetrics.SettingsVoiceButtonWidth,
            ComboBoxFit.MeasureText(_host, UiStrings.ButtonChannelVoice, 11.333, FontWeights.Bold) + 20);
        foreach (var button in _sineButtons)
        {
            button.Width = sine;
            button.MinWidth = sine;
            button.MaxWidth = sine;
        }

        foreach (var button in _voiceButtons)
        {
            if (button is null)
            {
                continue;
            }

            button.Width = voice;
            button.MinWidth = voice;
            button.MaxWidth = voice;
        }

        _testButtonsWidth = (DesignMetrics.SettingsTestButtonGap * 2) + sine + voice;
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

    private void FitFileCombos()
    {
        var max = Math.Max(
            DesignMetrics.SettingsFileLaneComboMinWidth,
            ComboBoxFit.MeasureText(_host, UiStrings.LabelWaveformLane(ChannelLayout.MaxChannels), 11.333) + 36);
        max = Math.Max(max, ComboBoxFit.MeasureText(_host, UiStrings.LabelPortOff, 11.333) + 36);
        foreach (var box in _fileBoxes)
        {
            box.Width = max;
            box.MinWidth = max;
            box.MaxWidth = max;
        }

        _fileComboWidth = _fileBoxes.Count > 0 ? max : 0;
    }

    private static int[] ReadBoxes(List<ComboBox> boxes)
    {
        var map = new int[boxes.Count];
        for (var i = 0; i < boxes.Count; i++)
        {
            map[i] = boxes[i].SelectedItem is PortItem item ? item.Port : i;
        }

        return map;
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
