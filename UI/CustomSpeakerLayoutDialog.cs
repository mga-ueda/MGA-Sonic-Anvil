using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>設定用のカスタムスピーカー配置（名前・本数・チャンネルごとのラベル）。</summary>
internal sealed class CustomSpeakerLayoutDialog : Window
{
    private readonly TextBox _nameBox = new()
    {
        MinWidth = DesignMetrics.From96(220),
        Height = DesignMetrics.AudioInputHeight,
        VerticalContentAlignment = VerticalAlignment.Center,
        Padding = new Thickness(6, 1, 6, 1),
        FontSize = 11.333,
    };
    private readonly ComboBox _countCombo = new() { MinWidth = 80 };
    private readonly StackPanel _channelHost = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBlock _warning = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        MinHeight = DesignMetrics.From96(32),
        Visibility = Visibility.Hidden,
    };
    private readonly RoundedButton _okButton;
    private ComboBox[] _labelCombos = [];
    private bool _syncing;
    private readonly string _existingId;

    public SpeakerPreset? Result { get; private set; }

    public static SpeakerPreset? Show(Window? owner, SpeakerPreset? existing = null)
    {
        var dialog = new CustomSpeakerLayoutDialog(existing)
        {
            Owner = owner,
        };
        if (owner is not null)
        {
            WindowPaintReveal.EnsureRevealedForDialog(owner);
        }

        return AcrylicDialogChrome.ShowDialog(dialog) == true ? dialog.Result : null;
    }

    private CustomSpeakerLayoutDialog(SpeakerPreset? existing)
    {
        _existingId = existing is not null && existing.IsCustom
            ? existing.Id
            : SpeakerPreset.NewCustomId();
        Title = existing is null
            ? UiStrings.DialogCustomSpeakerLayoutAddTitle
            : UiStrings.DialogCustomSpeakerLayoutEditTitle;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        MinWidth = DesignMetrics.From96(440);
        Foreground = (Brush)FindResource("PrimaryForeBrush");
        AcrylicDialogChrome.Prepare(this);
        _warning.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        if (TryFindResource("DarkTextBoxStyle") is Style textStyle)
        {
            _nameBox.Style = textStyle;
        }

        if (TryFindResource("DarkComboBoxStyle") is Style comboStyle)
        {
            _countCombo.Style = comboStyle;
        }

        for (var n = 1; n <= ChannelLayout.MaxChannels; n++)
        {
            _countCombo.Items.Add(n);
        }

        _countCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            RebuildChannelCombos(KeepLabels());
            RefreshOk();
        };

        _okButton = CreateButton(UiStrings.ButtonOkMnemonic, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancelMnemonic, accept: false, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        buttons.Children.Add(_okButton);
        buttons.Children.Add(cancel);

        var root = new StackPanel();
        root.Children.Add(Labeled(_nameBox, UiStrings.LabelCustomSpeakerLayoutName));
        root.Children.Add(Labeled(_countCombo, UiStrings.LabelCustomSpeakerLayoutChannels));
        root.Children.Add(Labeled(_channelHost, UiStrings.LabelChannelLayoutChannelAssign));
        root.Children.Add(_warning);
        root.Children.Add(buttons);
        AcrylicDialogChrome.SetBody(this, root, DesignMetrics.AudioPad);
        AppDialogKeys.Attach(this, Close);

        _syncing = true;
        try
        {
            _nameBox.Text = existing?.DisplayName() ?? UiStrings.LabelCustomSpeakerLayoutDefaultName;
            var channels = existing is { Channels: > 0 }
                ? Math.Clamp(existing.Channels, 1, ChannelLayout.MaxChannels)
                : 6;
            _countCombo.SelectedItem = channels;
            RebuildChannelCombos(existing?.Labels);
        }
        finally
        {
            _syncing = false;
        }

        RefreshOk();
        _nameBox.TextChanged += (_, _) => RefreshOk();
    }

    private void RebuildChannelCombos(IReadOnlyList<string>? labels)
    {
        var channels = _countCombo.SelectedItem is int n
            ? Math.Clamp(n, 1, ChannelLayout.MaxChannels)
            : 2;
        _channelHost.Children.Clear();
        _labelCombos = new ComboBox[channels];
        Style? comboStyle = TryFindResource("DarkComboBoxStyle") as Style;
        for (var i = 0; i < channels; i++)
        {
            var combo = new ComboBox
            {
                MinWidth = DesignMetrics.From96(120),
                MinHeight = DesignMetrics.AudioInputHeight,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (comboStyle is not null)
            {
                combo.Style = comboStyle;
            }

            foreach (var name in ChannelMaskBits.LayoutLabelChoices())
            {
                combo.Items.Add(name);
            }

            var preferred = labels is not null && i < labels.Count && !string.IsNullOrWhiteSpace(labels[i])
                ? ChannelMaskBits.CanonicalLayoutLabel(labels[i])
                : DefaultLabelAt(i, channels);
            SelectLabel(combo, preferred);
            combo.SelectionChanged += (_, _) => RefreshOk();
            _labelCombos[i] = combo;

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
            _channelHost.Children.Add(row);
        }
    }

    private string[] KeepLabels()
    {
        var labels = new string[_labelCombos.Length];
        for (var i = 0; i < _labelCombos.Length; i++)
        {
            labels[i] = ReadLabel(_labelCombos[i]);
        }

        return labels;
    }

    private void RefreshOk()
    {
        var nameOk = !string.IsNullOrWhiteSpace(_nameBox.Text);
        var labels = KeepLabels();
        var dup = labels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != labels.Length;
        string? warning = null;
        if (!nameOk)
        {
            warning = UiStrings.CustomSpeakerLayoutNameRequired;
        }
        else if (dup)
        {
            warning = UiStrings.WaveChannelLayoutDuplicateWarning;
        }

        _warning.Text = warning ?? string.Empty;
        _warning.Visibility = warning is null ? Visibility.Hidden : Visibility.Visible;
        _okButton.IsEnabled = nameOk && !dup;
    }

    private void Accept()
    {
        RefreshOk();
        if (!_okButton.IsEnabled)
        {
            return;
        }

        Result = new SpeakerPreset
        {
            Id = _existingId,
            Name = _nameBox.Text.Trim(),
            Labels = KeepLabels(),
            Channels = _labelCombos.Length,
        }.Normalized();
        DialogResult = true;
        Close();
    }

    private RoundedButton CreateButton(string text, bool accept, bool accent = false, bool isDefault = false, bool isCancel = false)
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
            button.Click += (_, _) => Accept();
        }
        else
        {
            button.Click += (_, _) =>
            {
                DialogResult = false;
                Close();
            };
        }

        return button;
    }

    private static UIElement Labeled(UIElement content, string label)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        block.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, 0, 0, 4),
        });
        block.Children.Add(content);
        return block;
    }

    private static void SelectLabel(ComboBox combo, string label)
    {
        var want = ChannelMaskBits.CanonicalLayoutLabel(label);
        var wantBit = ChannelMaskBits.BitForLabel(want);
        foreach (var item in combo.Items)
        {
            if (item is not string text)
            {
                continue;
            }

            if (text.Equals(want, StringComparison.OrdinalIgnoreCase)
                || (wantBit != 0 && ChannelMaskBits.BitForLabel(text) == wantBit))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        if (combo.Items.Count > 0)
        {
            combo.SelectedIndex = 0;
        }
    }

    private static string ReadLabel(ComboBox combo) =>
        ChannelMaskBits.CanonicalLayoutLabel(combo.SelectedItem as string) is { Length: > 0 } name
            ? name
            : "L";

    private static string DefaultLabelAt(int index, int channels)
    {
        // 6ch は Side を既定に（ファイルの 0x60F 系と合わせやすい）。
        var preferred = channels == 6 && ChannelLayout.TryGet("5.1-Side", out var side)
            ? side
            : ChannelLayout.PreferredForChannels(channels);
        if ((uint)index < (uint)preferred.Labels.Length)
        {
            return ChannelMaskBits.CanonicalLayoutLabel(preferred.Labels[index]);
        }

        var choices = ChannelMaskBits.LayoutLabelChoices();
        return choices[Math.Clamp(index, 0, choices.Length - 1)];
    }
}
