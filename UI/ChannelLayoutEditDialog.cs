using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>WAVE fmt / AIFF CHAN の channel bitmap を編集する。</summary>
internal sealed class ChannelLayoutEditDialog : Window
{
    private readonly IReadOnlyList<WaveChannelLayoutWriter.TargetInfo> _targets;
    private readonly int _channels;
    private readonly ComboBox _presetCombo = new() { MinWidth = 180 };
    private readonly TextBlock _maskLabel = new();
    private readonly TextBlock _warning = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        MinHeight = DesignMetrics.From96(48),
        Visibility = Visibility.Hidden,
    };
    private readonly ComboBox[] _channelCombos;
    private readonly RoundedButton _okButton;
    private bool _syncing;
    private bool _sizeLocked;
    private int _mask;

    public int SelectedMask => _mask;

    public static int? Show(Window? owner, IReadOnlyList<WaveChannelLayoutWriter.TargetInfo> targets)
    {
        if (targets.Count == 0)
        {
            return null;
        }

        var dialog = new ChannelLayoutEditDialog(targets)
        {
            Owner = owner,
        };
        if (owner is not null)
        {
            WindowPaintReveal.EnsureRevealedForDialog(owner);
        }

        return AcrylicDialogChrome.ShowDialog(dialog) == true ? dialog.SelectedMask : null;
    }

    private ChannelLayoutEditDialog(IReadOnlyList<WaveChannelLayoutWriter.TargetInfo> targets)
    {
        _targets = targets;
        _channels = Math.Clamp(targets[0].Channels, 1, ChannelMaskBits.MaxChannels);
        _mask = targets[0].CurrentMask;
        for (var i = 1; i < targets.Count; i++)
        {
            if (targets[i].CurrentMask != _mask)
            {
                _mask = 0;
                break;
            }
        }

        _channelCombos = new ComboBox[_channels];
        Title = UiStrings.DialogChannelLayoutEditTitle;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = DesignMetrics.From96(440);
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(this);
        _warning.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");

        BuildPresetCombo();
        _okButton = CreateButton(UiStrings.ButtonOkMnemonic, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancelMnemonic, accept: false, isCancel: true);
        var clear = CreateButton(UiStrings.ButtonChannelLayoutClear, accept: false);
        clear.Click += (_, _) => ClearAssignment();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(clear);
        buttons.Children.Add(_okButton);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(BuildTargetsBlock());
        root.Children.Add(Labeled(_presetCombo, UiStrings.LabelChannelLayoutPreset));
        root.Children.Add(Labeled(_maskLabel, UiStrings.LabelChannelLayoutMask));
        root.Children.Add(BuildChannelAssignPanel());
        root.Children.Add(_warning);
        root.Children.Add(buttons);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        AppDialogKeys.Attach(this, Close);
        ContentRendered += (_, _) => LockDialogSize();
        ApplyMaskToCombos(_mask);
        RefreshFromCombos();
    }

    private void LockDialogSize()
    {
        if (_sizeLocked)
        {
            return;
        }

        _sizeLocked = true;
        SizeToContent = SizeToContent.Manual;
        Width = Math.Max(ActualWidth, MinWidth);
        Height = ActualHeight;
        MinHeight = ActualHeight;
    }

    private UIElement BuildTargetsBlock()
    {
        var list = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        list.Children.Add(new TextBlock
        {
            Text = UiStrings.LabelChannelLayoutTargets,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var max = Math.Min(_targets.Count, 8);
        for (var i = 0; i < max; i++)
        {
            var t = _targets[i];
            list.Children.Add(new TextBlock
            {
                Text = UiStrings.WaveChannelLayoutTargetLine(
                    Path.GetFileName(t.Path),
                    t.Channels,
                    t.CurrentMask),
                Opacity = 0.85,
            });
        }

        if (_targets.Count > max)
        {
            list.Children.Add(new TextBlock
            {
                Text = $"+{_targets.Count - max}",
                Opacity = 0.7,
            });
        }

        return list;
    }

    private UIElement BuildChannelAssignPanel()
    {
        var rows = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        Style? comboStyle = TryFindResource("DarkComboBoxStyle") as Style;
        for (var i = 0; i < _channels; i++)
        {
            var combo = new ComboBox
            {
                MinWidth = DesignMetrics.From96(120),
                MinHeight = DesignMetrics.AudioInputHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = i,
            };
            if (comboStyle is not null)
            {
                combo.Style = comboStyle;
            }

            combo.Items.Add(new ComboBoxItem
            {
                Content = UiStrings.LabelChannelLayoutSpeakerUnset,
                Tag = 0,
            });
            foreach (var (bit, name) in ChannelMaskBits.Speakers)
            {
                combo.Items.Add(new ComboBoxItem
                {
                    Content = name,
                    Tag = bit,
                });
            }

            var channelIndex = i;
            combo.SelectionChanged += (_, _) => OnChannelSelectionChanged(channelIndex);
            _channelCombos[i] = combo;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = new TextBlock
            {
                Text = UiStrings.LabelChannelLayoutChannelRow(i + 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            Grid.SetColumn(label, 0);
            Grid.SetColumn(combo, 1);
            row.Children.Add(label);
            row.Children.Add(combo);
            rows.Children.Add(row);
        }

        return Labeled(rows, UiStrings.LabelChannelLayoutChannelAssign);
    }

    private void BuildPresetCombo()
    {
        _presetCombo.Items.Add(new ComboBoxItem
        {
            Content = UiStrings.LabelChannelLayoutUnspecified,
            Tag = 0,
        });
        foreach (var preset in ChannelLayoutMasks.KnownPresets)
        {
            if (preset.Channels != _channels)
            {
                continue;
            }

            _presetCombo.Items.Add(new ComboBoxItem
            {
                Content = preset.MenuLabel,
                Tag = preset.Mask,
            });
        }

        _presetCombo.Items.Add(new ComboBoxItem
        {
            Content = UiStrings.LabelChannelLayoutCustom,
            Tag = null,
        });
        if (TryFindResource("DarkComboBoxStyle") is Style comboStyle)
        {
            _presetCombo.Style = comboStyle;
        }

        _presetCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            if (_presetCombo.SelectedItem is ComboBoxItem { Tag: int mask })
            {
                ApplyMaskToCombos(mask);
                RefreshFromCombos();
            }
        };
    }

    private void OnChannelSelectionChanged(int channelIndex)
    {
        if (_syncing)
        {
            return;
        }

        var bit = ReadComboBit(_channelCombos[channelIndex]);
        if (bit != 0)
        {
            // 同じスピーカーを他チャンネルから外す。
            _syncing = true;
            try
            {
                for (var i = 0; i < _channelCombos.Length; i++)
                {
                    if (i == channelIndex)
                    {
                        continue;
                    }

                    if (ReadComboBit(_channelCombos[i]) == bit)
                    {
                        _channelCombos[i].SelectedIndex = 0;
                    }
                }
            }
            finally
            {
                _syncing = false;
            }
        }

        RefreshFromCombos();
    }

    private void ClearAssignment()
    {
        ApplyMaskToCombos(0);
        RefreshFromCombos();
    }

    private void ApplyMaskToCombos(int mask)
    {
        _syncing = true;
        try
        {
            var bits = ChannelMaskBits.BitsFromMask(mask, _channels);
            for (var i = 0; i < _channelCombos.Length; i++)
            {
                SelectComboBit(_channelCombos[i], bits[i]);
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RefreshFromCombos()
    {
        var bits = new int[_channels];
        var unset = 0;
        for (var i = 0; i < _channels; i++)
        {
            bits[i] = ReadComboBit(_channelCombos[i]);
            if (bits[i] == 0)
            {
                unset++;
            }
        }

        var mixedCounts = _targets.Any(t => t.Channels != _channels);
        string? warning = null;
        var canOk = true;
        var nonCanonicalOrder = false;

        if (mixedCounts)
        {
            warning = UiStrings.WaveChannelLayoutMixedChannelCountWarning;
        }

        if (unset == _channels)
        {
            _mask = 0;
        }
        else if (unset > 0)
        {
            _mask = ChannelMaskBits.MaskFromBits(bits);
            warning = UiStrings.WaveChannelLayoutIncompleteWarning;
            canOk = false;
        }
        else if (bits.Distinct().Count() != bits.Length)
        {
            _mask = ChannelMaskBits.MaskFromBits(bits);
            warning = UiStrings.WaveChannelLayoutDuplicateWarning;
            canOk = false;
        }
        else
        {
            // マスクはスピーカー集合のみ。並びがビット昇順と違っても書き込み可（注意表示のみ）。
            _mask = ChannelMaskBits.MaskFromBits(bits);
            if (!ChannelMaskBits.IsValidWaveChannelOrder(bits))
            {
                nonCanonicalOrder = true;
                warning = UiStrings.WaveChannelLayoutOrderNoteWarning;
            }
        }

        _maskLabel.Text = _mask == 0 && unset == _channels
            ? UiStrings.LabelChannelLayoutUnspecified
            : $"0x{_mask.ToString("X", CultureInfo.InvariantCulture)}";

        _warning.Text = warning ?? string.Empty;
        _warning.Visibility = warning is null ? Visibility.Hidden : Visibility.Visible;
        _okButton.IsEnabled = canOk;
        // 並びがビット昇順と違う／未確定のときはカスタムを維持（プリセット一致でコンボを上書きしない）。
        var presetMask = unset == _channels
            ? 0
            : (!canOk || nonCanonicalOrder ? -1 : _mask);
        SelectPresetForMask(presetMask);
    }

    private void SelectPresetForMask(int mask)
    {
        _syncing = true;
        try
        {
            ComboBoxItem? custom = null;
            ComboBoxItem? match = null;
            ComboBoxItem? unspecified = null;
            foreach (var item in _presetCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is null)
                {
                    custom = item;
                    continue;
                }

                if (item.Tag is int value)
                {
                    if (value == 0)
                    {
                        unspecified = item;
                    }

                    if (mask >= 0 && value == mask)
                    {
                        match = item;
                    }
                }
            }

            if (mask == 0)
            {
                _presetCombo.SelectedItem = unspecified ?? _presetCombo.Items[0];
            }
            else if (match is not null)
            {
                _presetCombo.SelectedItem = match;
            }
            else
            {
                _presetCombo.SelectedItem = custom ?? _presetCombo.Items[^1];
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static int ReadComboBit(ComboBox combo) =>
        combo.SelectedItem is ComboBoxItem { Tag: int bit } ? bit : 0;

    private static void SelectComboBit(ComboBox combo, int bit)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is int value && value == bit)
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static StackPanel Labeled(UIElement child, string label)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(child);
        return panel;
    }

    private RoundedButton CreateButton(
        string text,
        bool accept,
        bool accent = false,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        AppDialogKeys.PrepareActionButton(button, isDefault, isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        if (accept)
        {
            button.Click += (_, _) =>
            {
                if (!button.IsEnabled)
                {
                    return;
                }

                DialogResult = true;
                Close();
            };
        }
        else if (isCancel)
        {
            button.Click += (_, _) => Close();
        }

        return button;
    }
}
