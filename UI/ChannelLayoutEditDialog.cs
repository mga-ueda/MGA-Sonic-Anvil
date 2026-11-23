using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>チャンネルレイアウト編集の結果。SpeakerPresetId はプリセットから選んだときだけ。</summary>
internal readonly record struct ChannelLayoutEditResult(int Mask, string? SpeakerPresetId);

/// <summary>WAVE fmt / AIFF CHAN の channel bitmap を編集する。</summary>
internal sealed class ChannelLayoutEditDialog : Window
{
    private readonly record struct PresetChoice(string? SpeakerId, int? Mask, int[]? Bits);

    private readonly IReadOnlyList<WaveChannelLayoutWriter.TargetInfo> _targets;
    private readonly int _channels;
    private readonly ComboBox _presetCombo = new() { MinWidth = 180 };
    private readonly TextBlock _maskLabel = new();
    private readonly TextBlock _warning = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
        Visibility = Visibility.Collapsed,
    };
    private readonly ComboBox[] _channelCombos;
    private readonly RoundedButton _okButton;
    private bool _syncing;
    private bool _sizeLocked;
    private int _mask;
    private string? _speakerPresetId;

    public int SelectedMask => _mask;

    public string? SelectedSpeakerPresetId => _speakerPresetId;

    public static ChannelLayoutEditResult? Show(
        Window? owner,
        IReadOnlyList<WaveChannelLayoutWriter.TargetInfo> targets)
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

        return AcrylicDialogChrome.ShowDialog(dialog) == true
            ? new ChannelLayoutEditResult(dialog.SelectedMask, dialog.SelectedSpeakerPresetId)
            : null;
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
        // ボタン行（未指定に戻す / OK / Cancel）が下限。余分な横余白は出さない。
        MinWidth = DesignMetrics.From96(320);
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

        _presetCombo.HorizontalAlignment = HorizontalAlignment.Left;
        _maskLabel.VerticalAlignment = VerticalAlignment.Center;
        var root = new StackPanel();
        Grid.SetIsSharedSizeScope(root, true);
        root.Children.Add(BuildTargetsBlock());
        root.Children.Add(FormRow(UiStrings.LabelChannelLayoutPreset, _presetCombo));
        root.Children.Add(FormRow(UiStrings.LabelChannelLayoutMask, _maskLabel));
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
        // SizeToContent の実測幅を採用（MinWidth で引き延ばさない）。
        SizeToContent = SizeToContent.Manual;
        var width = Math.Max(ActualWidth, MinWidth);
        if (Content is FrameworkElement root)
        {
            root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = Math.Max(Math.Ceiling(root.DesiredSize.Width), MinWidth);
        }

        Width = width;
        Height = ActualHeight;
        MinHeight = ActualHeight;
        MinWidth = width;
    }

    private void FitHeightToContent()
    {
        if (!_sizeLocked || Content is not FrameworkElement root)
        {
            return;
        }

        // 警告の出し入れで高さが足りなく／余らないよう、キャプション込みで測り直す。
        root.Measure(new Size(Width, double.PositiveInfinity));
        var needed = Math.Ceiling(root.DesiredSize.Height);
        if (needed <= 0)
        {
            return;
        }

        Height = needed;
        MinHeight = needed;
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
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = UiStrings.LabelChannelLayoutChannelAssign,
            Margin = new Thickness(0, 0, 0, 4),
        });
        Style? comboStyle = TryFindResource("DarkComboBoxStyle") as Style;
        for (var i = 0; i < _channels; i++)
        {
            var combo = new ComboBox
            {
                MinWidth = DesignMetrics.From96(120),
                MinHeight = DesignMetrics.AudioInputHeight,
                HorizontalAlignment = HorizontalAlignment.Left,
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
                    // WAVE 名とエディタ略称を併記（BL=Ls / SL=Sl の取り違え防止）。
                    Content = FormatSpeakerChoice(name),
                    Tag = bit,
                });
            }

            var channelIndex = i;
            combo.SelectionChanged += (_, _) => OnChannelSelectionChanged(channelIndex);
            _channelCombos[i] = combo;
            panel.Children.Add(FormRow(UiStrings.LabelChannelLayoutChannelRow(i + 1), combo, topMargin: 0, bottomMargin: 6));
        }

        return panel;
    }

    private void BuildPresetCombo()
    {
        if (TryFindResource("DarkComboBoxStyle") is Style comboStyle)
        {
            _presetCombo.Style = comboStyle;
        }

        RebuildPresetComboItems();
        _presetCombo.SelectionChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            if (_presetCombo.SelectedItem is not ComboBoxItem { Tag: PresetChoice choice })
            {
                return;
            }

            _speakerPresetId = choice.SpeakerId;
            if (choice.Bits is { Length: > 0 } bits)
            {
                ApplyBitsToCombos(bits);
                RefreshFromCombos();
            }
            else if (choice.Mask is int mask)
            {
                ApplyMaskToCombos(mask);
                RefreshFromCombos();
            }
        };
    }

    private void RebuildPresetComboItems()
    {
        var previousSpeakerId = _speakerPresetId;
        _presetCombo.Items.Clear();
        _presetCombo.Items.Add(new ComboBoxItem
        {
            Content = UiStrings.LabelChannelLayoutUnspecified,
            Tag = new PresetChoice(null, Mask: 0, Bits: null),
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
                Tag = new PresetChoice(preset.Id, preset.Mask, Bits: null),
            });
        }

        // 有効なカスタム配置も選べる（ラベル順でビットを当て、その集合をマスクとして書く）。
        foreach (var preset in EnumerateCustomPresetsForChannels(_channels))
        {
            var bits = ChannelMaskBits.BitsFromLabels(preset.ToLayout().Labels);
            if (bits is null || bits.Length != _channels)
            {
                continue;
            }

            _presetCombo.Items.Add(new ComboBoxItem
            {
                Content = preset.DisplayName(),
                Tag = new PresetChoice(preset.Id, Mask: null, bits),
            });
        }

        _presetCombo.Items.Add(new ComboBoxItem
        {
            Content = UiStrings.LabelChannelLayoutCustom,
            Tag = new PresetChoice(null, Mask: null, Bits: null),
        });

        if (!string.IsNullOrWhiteSpace(previousSpeakerId))
        {
            SelectPresetChoiceBySpeakerId(previousSpeakerId);
        }
    }

    private static IEnumerable<SpeakerPreset> EnumerateCustomPresetsForChannels(int channels)
    {
        var settings = AppStorage.Settings;
        var visible = new HashSet<string>(
            settings.ResolvedVisibleSpeakerIds(),
            StringComparer.OrdinalIgnoreCase);
        foreach (var preset in settings.SpeakerPresets)
        {
            if (preset is null
                || !preset.IsCustom
                || preset.Channels != channels
                || !visible.Contains(preset.Id))
            {
                continue;
            }

            yield return preset;
        }
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

    private void ApplyMaskToCombos(int mask) =>
        ApplyBitsToCombos(ChannelMaskBits.BitsFromMask(mask, _channels));

    private void ApplyBitsToCombos(IReadOnlyList<int> bits)
    {
        _syncing = true;
        try
        {
            for (var i = 0; i < _channelCombos.Length; i++)
            {
                var bit = i < bits.Count ? bits[i] : 0;
                SelectComboBit(_channelCombos[i], bit);
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
            : ChannelMaskBits.FormatMaskDisplay(_mask);

        var warningVisible = warning is not null;
        var warningChanged = (_warning.Visibility == Visibility.Visible) != warningVisible
            || !string.Equals(_warning.Text, warning ?? string.Empty, StringComparison.Ordinal);
        _warning.Text = warning ?? string.Empty;
        _warning.Visibility = warningVisible ? Visibility.Visible : Visibility.Collapsed;
        if (warningChanged)
        {
            FitHeightToContent();
        }

        _okButton.IsEnabled = canOk;
        // 未確定は freeform。非カノニカル並びでもスピーカーカスタムとの一致は見る。
        var presetMask = unset == _channels
            ? 0
            : (!canOk ? -1 : _mask);
        SelectPresetForMask(presetMask, preferSpeakerCustomOrder: nonCanonicalOrder);
    }

    private void SelectPresetChoiceBySpeakerId(string speakerId)
    {
        foreach (var item in _presetCombo.Items.OfType<ComboBoxItem>())
        {
            if (item.Tag is PresetChoice { SpeakerId: { } id }
                && id.Equals(speakerId, StringComparison.OrdinalIgnoreCase))
            {
                _syncing = true;
                try
                {
                    _presetCombo.SelectedItem = item;
                    _speakerPresetId = speakerId;
                }
                finally
                {
                    _syncing = false;
                }

                if (item.Tag is PresetChoice { Bits: { Length: > 0 } bits })
                {
                    ApplyBitsToCombos(bits);
                }

                return;
            }
        }
    }

    private void SelectPresetForMask(int mask, bool preferSpeakerCustomOrder = false)
    {
        _syncing = true;
        try
        {
            ComboBoxItem? freeform = null;
            ComboBoxItem? match = null;
            ComboBoxItem? unspecified = null;
            ComboBoxItem? speakerCustomMatch = null;
            var currentBits = ReadCurrentBits();
            foreach (var item in _presetCombo.Items.OfType<ComboBoxItem>())
            {
                if (item.Tag is not PresetChoice choice)
                {
                    continue;
                }

                if (choice.Mask is null && choice.Bits is null && choice.SpeakerId is null)
                {
                    freeform = item;
                    continue;
                }

                if (choice.Mask == 0 && choice.Bits is null)
                {
                    unspecified = item;
                }

                if (choice.Bits is { Length: > 0 } itemBits && BitsEqual(itemBits, currentBits))
                {
                    speakerCustomMatch = item;
                }
                else if (mask >= 0 && choice.Mask == mask && choice.Bits is null)
                {
                    match = item;
                }
            }

            ComboBoxItem? selected;
            if (mask == 0)
            {
                selected = unspecified ?? _presetCombo.Items.OfType<ComboBoxItem>().FirstOrDefault();
            }
            else if (speakerCustomMatch is not null
                && (preferSpeakerCustomOrder || match is null))
            {
                selected = speakerCustomMatch;
            }
            else if (match is not null)
            {
                selected = match;
            }
            else
            {
                selected = freeform ?? _presetCombo.Items.OfType<ComboBoxItem>().LastOrDefault();
            }

            _presetCombo.SelectedItem = selected;
            _speakerPresetId = selected?.Tag is PresetChoice chosen ? chosen.SpeakerId : null;
        }
        finally
        {
            _syncing = false;
        }
    }

    private int[] ReadCurrentBits()
    {
        var bits = new int[_channels];
        for (var i = 0; i < _channels; i++)
        {
            bits[i] = ReadComboBit(_channelCombos[i]);
        }

        return bits;
    }

    private static bool BitsEqual(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>WAVE 名にアプリ側略称を足す。FL→FL (L)、BL→BL (Ls)、SL→SL (Sl)。</summary>
    internal static string FormatSpeakerChoice(string waveName)
    {
        var shortName = ChannelMaskBits.CanonicalLayoutLabel(waveName);
        // SL と Sl は大小だけ違うが、Side 略称として併記する（IgnoreCase だと潰れる）。
        if (string.IsNullOrEmpty(shortName)
            || string.Equals(shortName, waveName, StringComparison.Ordinal))
        {
            return waveName;
        }

        return $"{waveName} ({shortName})";
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

    private static Grid FormRow(
        string label,
        UIElement child,
        double topMargin = 8,
        double bottomMargin = 0)
    {
        var row = new Grid { Margin = new Thickness(0, topMargin, 0, bottomMargin) };
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
            SharedSizeGroup = "ChannelLayoutLabel",
        });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelBlock = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        Grid.SetColumn(labelBlock, 0);
        Grid.SetColumn(child, 1);
        row.Children.Add(labelBlock);
        row.Children.Add(child);
        return row;
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
