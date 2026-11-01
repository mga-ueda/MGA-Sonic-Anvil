using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ラウドネス左の VST インサート（最大 8）。エディタ専用。
/// Ctrl+クリックでバイパス。空スロットは認識済み一覧から選択。
/// </summary>
internal sealed class VstInsertRackView : Grid
{
    private readonly Border[] _slotChrome = new Border[VstInsertChain.MaxSlots];
    private readonly TextBlock[] _slotText = new TextBlock[VstInsertChain.MaxSlots];
    private DocumentSession? _session;
    private Action? _onChanged;
    private Action<int>? _openEditor;
    private Action<int, VstPickerEntry>? _assignPlugin;

    public VstInsertRackView()
    {
        Width = DesignMetrics.VstInsertRackWidth;
        MinWidth = DesignMetrics.VstInsertRackWidth;
        Height = DesignMetrics.SpectrumHeight;
        VerticalAlignment = VerticalAlignment.Stretch;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ToolTip = UiStrings.TooltipVstInsertSlot;

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        var title = new TextBlock
        {
            Text = UiStrings.LabelVstInsertRack,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 2),
            Foreground = Brushes.Gray,
            IsHitTestVisible = false,
        };
        SetRow(title, 0);
        Children.Add(title);

        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var index = i;
            var text = new TextBlock
            {
                Text = UiStrings.LabelVstInsertEmpty,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(4, 0, 4, 0),
                IsHitTestVisible = false,
            };
            var border = new Border
            {
                Child = text,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 0, 1),
                Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Cursor = Cursors.Hand,
                Tag = index,
            };
            border.MouseLeftButtonUp += Slot_MouseLeftButtonUp;
            border.MouseRightButtonUp += Slot_MouseRightButtonUp;
            _slotChrome[i] = border;
            _slotText[i] = text;
            SetRow(border, i + 1);
            Children.Add(border);
        }

        ApplyTheme();
    }

    public void SetSession(
        DocumentSession? session,
        Action? onChanged = null,
        Action<int>? openEditor = null,
        Action<int, VstPickerEntry>? assignPlugin = null)
    {
        _session = session;
        _onChanged = onChanged;
        _openEditor = openEditor;
        _assignPlugin = assignPlugin;
        Refresh();
    }

    public void Refresh()
    {
        var chain = _session?.VstInserts;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var slot = chain?.Slots[i];
            var empty = slot is null || slot.IsEmpty;
            var text = _slotText[i];
            var chrome = _slotChrome[i];
            if (empty)
            {
                text.Text = UiStrings.LabelVstInsertEmpty;
                text.Opacity = 0.55;
                chrome.Opacity = 1;
                chrome.ToolTip = UiStrings.TooltipVstInsertSlot;
            }
            else
            {
                text.Text = ShortName(slot!.DisplayName) ?? System.IO.Path.GetFileNameWithoutExtension(slot.PluginPath) ?? "?";
                text.Opacity = slot.Bypassed || slot.Faulted ? 0.35 : 1;
                chrome.Opacity = slot.Bypassed || slot.Faulted ? 0.55 : 1;
                chrome.ToolTip = UiStrings.TooltipVstInsertSlotFilled
                    + (slot.Faulted ? "\n" + UiStrings.ErrorVstInsertLoadFailed : string.Empty);
            }
        }
    }

    public void ApplyTheme()
    {
        Brush fore;
        try
        {
            fore = (Brush)FindResource("StatusBarDetailForeBrush");
        }
        catch
        {
            fore = Brushes.LightGray;
        }

        for (var i = 0; i < _slotText.Length; i++)
        {
            _slotText[i].Foreground = fore;
        }
    }

    private void Slot_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int index } || _session is null)
        {
            return;
        }

        e.Handled = true;
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ToggleBypass(index);
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            ShowPluginPicker(index, sender as UIElement);
        }
        else
        {
            _openEditor?.Invoke(index);
        }
    }

    private void Slot_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: int index } || _session is null)
        {
            return;
        }

        e.Handled = true;
        if (_session.VstInserts.Slots[index].IsEmpty)
        {
            ShowPluginPicker(index, sender as UIElement);
        }
        else
        {
            ShowFilledMenu(index, sender as UIElement);
        }
    }

    private void ToggleBypass(int index)
    {
        if (_session is null)
        {
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        slot.Bypassed = !slot.Bypassed;
        Refresh();
        _onChanged?.Invoke();
    }

    private void ShowFilledMenu(int index, UIElement? placement)
    {
        if (_session is null)
        {
            return;
        }

        var menu = new ContextMenu { MaxHeight = VstInsertPluginMenu.ResolveMaxHeight() };
        var bypass = new MenuItem
        {
            Header = UiStrings.LabelVstInsertBypass,
            IsCheckable = true,
            IsChecked = _session.VstInserts.Slots[index].Bypassed,
        };
        bypass.Click += (_, _) =>
        {
            _session.VstInserts.Slots[index].Bypassed = bypass.IsChecked;
            Refresh();
            _onChanged?.Invoke();
        };
        menu.Items.Add(bypass);

        var open = new MenuItem { Header = UiStrings.LabelVstInsertOpenEditor };
        open.Click += (_, _) => _openEditor?.Invoke(index);
        menu.Items.Add(open);

        var replace = new MenuItem { Header = UiStrings.LabelVstInsertPickPlugin };
        replace.Click += (_, _) => ShowPluginPicker(index, placement);
        menu.Items.Add(replace);

        var remove = new MenuItem { Header = UiStrings.LabelVstInsertRemove };
        remove.Click += (_, _) => ClearSlot(index);
        menu.Items.Add(remove);

        if (placement is not null)
        {
            menu.PlacementTarget = placement;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }

        menu.IsOpen = true;
    }

    private void ShowPluginPicker(int index, UIElement? placement)
    {
        if (_session is null || placement is null)
        {
            return;
        }

        // フォルダ手繰り（メーカー → 右にプラグイン）。双方 ScrollViewer 付き。
        // ContextMenu は MaxHeight で切れるだけでスクロールしないため使わない。
        var recognized = Vst3PluginList.Load().Plugins.Where(p => p.IsRecognized);
        var groups = BuildPickerEntries(recognized);
        Action? onClear = _session.VstInserts.Slots[index].IsEmpty
            ? null
            : () => ClearSlot(index);
        VstInsertPluginMenu.Show(
            placement,
            groups,
            entry => AssignPlugin(index, entry),
            onClear);
    }

    /// <summary>
    /// 認識済み一覧をメーカー別にまとめる。
    /// WaveShell はスキャン時に ClassId 付きで展開済みであること（ここでは展開しない）。
    /// </summary>
    internal static List<KeyValuePair<string, List<VstPickerEntry>>> BuildPickerEntries(
        IEnumerable<Vst3PluginEntry> recognized)
    {
        var byVendor = new Dictionary<string, List<VstPickerEntry>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plugin in recognized)
        {
            if (Vst3FactoryProbe.IsWaveShellPath(plugin.Path)
                && string.IsNullOrWhiteSpace(plugin.ClassId))
            {
                continue;
            }

            AddPickerEntry(byVendor, VstPickerEntry.From(plugin));
        }

        return byVendor
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddPickerEntry(
        Dictionary<string, List<VstPickerEntry>> byVendor,
        VstPickerEntry entry)
    {
        var key = string.IsNullOrWhiteSpace(entry.Vendor)
            ? UiStrings.LabelVstInsertVendorUnknown
            : entry.Vendor.Trim();
        if (key.Contains("WaveShell", StringComparison.OrdinalIgnoreCase))
        {
            key = "Waves";
        }

        if (!byVendor.TryGetValue(key, out var list))
        {
            list = [];
            byVendor[key] = list;
        }

        if (list.Any(e =>
                string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                && string.Equals(e.ClassId, entry.ClassId, StringComparison.Ordinal)))
        {
            return;
        }

        list.Add(entry);
    }

    private void AssignPlugin(int index, VstPickerEntry entry)
    {
        if (_session is null)
        {
            return;
        }

        // 差し替えは MainWindow 側でアンロード→ロードの二段階にする
        if (_assignPlugin is not null)
        {
            _assignPlugin(index, entry);
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        slot.PluginPath = entry.Path;
        slot.DisplayName = entry.Name;
        slot.ClassId = entry.ClassId;
        slot.StateBlob = null;
        slot.Bypassed = false;
        slot.Faulted = false;
        Refresh();
        _openEditor?.Invoke(index);
    }

    private void ClearSlot(int index)
    {
        if (_session is null)
        {
            return;
        }

        _session.VstInserts.ClearSlot(index);
        Refresh();
        _onChanged?.Invoke();
    }

    private static string? ShortName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        return name.Length <= 14 ? name : name[..13] + "…";
    }
}

/// <summary>メニュー行。WaveShell 展開後は ClassId 必須。</summary>
internal readonly record struct VstPickerEntry(
    string Name,
    string Path,
    string Vendor,
    string Category,
    string? ClassId)
{
    public static VstPickerEntry From(Vst3PluginEntry entry) =>
        new(entry.Name, entry.Path, entry.Vendor, entry.Category, entry.ClassId);
}
