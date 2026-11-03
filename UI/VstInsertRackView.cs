using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ラウドネス左の VST インサート（最大 8・左右 4+4）。エディタ専用。
/// Ctrl+クリックでバイパス。空スロットは認識済み一覧から選択。
/// 見た目はダイアログの OK／Cancel（<see cref="RoundedButton"/> + <see cref="ActionButtonLooks"/>）に揃える。
/// </summary>
internal sealed class VstInsertRackView : Grid
{
    private readonly RoundedButton[] _slots = new RoundedButton[VstInsertChain.MaxSlots];
    private readonly CheckBox _includeTailBox;
    private readonly TextBlock _includeTailLabel;
    private DocumentSession? _session;
    private Action? _onChanged;
    private Action<int>? _openEditor;
    private Action<int, VstPickerEntry, bool>? _assignPlugin;
    private Action<int, int>? _moveSlot;
    private Func<bool>? _canInsertAllTabs;
    /// <summary>永続化前にライブ state をスロットへ書き戻す（MainWindow）。</summary>
    private Action? _captureStates;
    /// <summary>永続内容／バイパスを全タブへ反映する（MainWindow）。</summary>
    private Action? _syncPersistedToAllTabs;
    /// <summary>
    /// 全タブの同じ番号を空ける（MainWindow）。
    /// 引数はスロット番号と、消す対象の表示名／パス／ClassId（名前一致が必須）。
    /// </summary>
    private Action<int, string?, string?, string?>? _clearMatchingSlotOnAllTabs;
    private Point _dragStart;
    /// <summary>掴んだ位置（元ボタン内座標）。ゴーストを指の下に貼り付ける。</summary>
    private Point _dragGrabOffset;
    private int _dragFrom = -1;
    private int _dragHover = -1;
    private bool _dragMoved;
    private RoundedButton? _dragSource;
    private readonly Border _dragGhost;
    private readonly TranslateTransform _dragGhostOffset = new();
    private readonly DropShadowEffect _dragGhostShadow = new()
    {
        BlurRadius = 10,
        ShadowDepth = 3,
        Direction = 270,
        Color = Colors.Black,
        Opacity = 0.55,
        RenderingBias = RenderingBias.Performance,
    };

    public VstInsertRackView()
    {
        Width = DesignMetrics.VstInsertRackWidth;
        MinWidth = DesignMetrics.VstInsertRackWidth;
        Height = DesignMetrics.SpectrumHeight;
        VerticalAlignment = VerticalAlignment.Stretch;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ToolTip = UiStrings.TooltipVstInsertSlot;
        // スロット並べ替えは OLE DragDrop ではなく、ラック上の Capture＋ヒットテストで行う。
        PreviewMouseMove += Rack_PreviewMouseMove;
        PreviewMouseLeftButtonUp += Rack_PreviewMouseLeftButtonUp;
        LostMouseCapture += Rack_LostMouseCapture;

        _dragGhost = new Border
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            Opacity = 0.95,
            Padding = new Thickness(6, 1, 6, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Effect = _dragGhostShadow,
            Child = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            RenderTransform = _dragGhostOffset,
        };
        Panel.SetZIndex(_dragGhost, 100);

        // Include Tail | スロット 1–4 | スロット 5–8（各列 4 段）。Bounce はメニュー／ツールバー／ショートカット。
        const int slotRows = VstInsertChain.MaxSlots / 2;
        const int slotColumnOffset = 1;
        ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(DesignMetrics.VstInsertTailColumnWidth),
        });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < slotRows; i++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        _includeTailBox = new CheckBox
        {
            IsChecked = AppStorage.Settings.ResolveVstBounceIncludeTail(),
            Focusable = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiStrings.TooltipVstBounceIncludeTail,
        };
        _includeTailBox.SetResourceReference(StyleProperty, "DarkCheckBoxStyle");
        _includeTailBox.Checked += (_, _) => PersistIncludeTail(true);
        _includeTailBox.Unchecked += (_, _) => PersistIncludeTail(false);

        _includeTailLabel = new TextBlock
        {
            Text = UiStrings.LabelVstBounceIncludeTail,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
            IsHitTestVisible = false,
        };

        var includeTailHost = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = UiStrings.TooltipVstBounceIncludeTail,
        };
        includeTailHost.Children.Add(_includeTailBox);
        includeTailHost.Children.Add(_includeTailLabel);
        SetColumn(includeTailHost, 0);
        SetRow(includeTailHost, 0);
        SetRowSpan(includeTailHost, slotRows);
        Children.Add(includeTailHost);

        var slotMargin = DesignMetrics.VstInsertSlotMargin;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var index = i;
            var button = CreateRackButton(
                SlotNumberLabel(index),
                UiStrings.TooltipVstInsertSlot,
                accent: false,
                onClick: null);
            ApplySlotButtonLook(button, empty: true, persisted: false);
            button.Tag = index;
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            // 空き／Ctrl+クリックは Click。使用中の並べ替えは Preview＋ラック Capture。
            button.Click += (_, _) => Slot_ClickFromButton(index);
            button.PreviewMouseLeftButtonDown += Slot_PreviewMouseLeftButtonDown;
            button.MouseRightButtonUp += Slot_MouseRightButtonUp;
            _slots[i] = button;
            // 左列 0–3、右列 4–7。四方に余白を付けてつめつめを避ける。
            SetColumn(button, (i < slotRows ? 0 : 1) + slotColumnOffset);
            SetRow(button, i % slotRows);
            button.Margin = new Thickness(slotMargin);

            Children.Add(button);
        }

        // 掴んだプラグイン名をポインタに追従させるゴースト。
        SetColumn(_dragGhost, slotColumnOffset);
        SetColumnSpan(_dragGhost, 2);
        SetRow(_dragGhost, 0);
        SetRowSpan(_dragGhost, slotRows);
        Children.Add(_dragGhost);

        ApplyTheme();
    }

    private static void PersistIncludeTail(bool includeTail)
    {
        if (AppStorage.Settings.VstBounceIncludeTail == includeTail)
        {
            return;
        }

        AppStorage.Settings.VstBounceIncludeTail = includeTail;
        AppStorage.Save();
    }

    private void SyncIncludeTailFromSettings()
    {
        var on = AppStorage.Settings.ResolveVstBounceIncludeTail();
        if (_includeTailBox.IsChecked == on)
        {
            return;
        }

        _includeTailBox.IsChecked = on;
    }

    /// <summary>Alt+Shift+I。Include Tail をトグルして設定へ保存する。</summary>
    public void ToggleIncludeTail()
    {
        _includeTailBox.IsChecked = _includeTailBox.IsChecked != true;
    }

    public void SetSession(
        DocumentSession? session,
        Action? onChanged = null,
        Action<int>? openEditor = null,
        Action<int, VstPickerEntry, bool>? assignPlugin = null,
        Action<int, int>? moveSlot = null,
        Action? captureStates = null,
        Func<bool>? canInsertAllTabs = null,
        Action? syncPersistedToAllTabs = null,
        Action<int, string?, string?, string?>? clearMatchingSlotOnAllTabs = null)
    {
        _session = session;
        _onChanged = onChanged;
        _openEditor = openEditor;
        _assignPlugin = assignPlugin;
        _moveSlot = moveSlot;
        _captureStates = captureStates;
        _canInsertAllTabs = canInsertAllTabs;
        _syncPersistedToAllTabs = syncPersistedToAllTabs;
        _clearMatchingSlotOnAllTabs = clearMatchingSlotOnAllTabs;
        Refresh();
    }

    private static RoundedButton CreateRackButton(string label, string tip, bool accent, Action? onClick)
    {
        var button = new RoundedButton
        {
            Content = label,
            ToolTip = tip,
            FontSize = 11,
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 0, 0, 1),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = false,
        };
        ApplyRackLook(button, accent);

        if (onClick is not null)
        {
            button.Click += (_, _) => onClick();
        }

        return button;
    }

    /// <summary>スロット見た目の下地（枠 1px）。空き／通常／永続の色分けは <see cref="ApplySlotButtonLook"/>。</summary>
    private static void ApplyRackLook(RoundedButton button, bool accent)
    {
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(button);
        }
        else
        {
            ActionButtonLooks.ApplyClear(button);
        }

        button.BorderSize = 1;
        button.InvalidateVisual();
    }

    /// <summary>
    /// 永続化中はオレンジ（空きでも番号だけ表示）。未永続の空きはグレー、通常の使用中は青。
    /// </summary>
    private static void ApplySlotButtonLook(RoundedButton button, bool empty, bool persisted)
    {
        if (persisted)
        {
            ActionButtonLooks.ApplyVstInsertPersist(button);
        }
        else if (empty)
        {
            ActionButtonLooks.ApplyVstInsertEmpty(button);
        }
        else
        {
            ActionButtonLooks.ApplyAccent(button);
            button.BorderSize = 1;
            button.InvalidateVisual();
        }
    }

    private static bool SlotIsPersisted(int index) =>
        DocumentSessionStore.SnapshotHasPluginAt(AppStorage.Settings.DefaultVstInserts, index);

    public void Refresh()
    {
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            PaintSlot(i, dropTarget: i == _dragHover);
        }
    }

    /// <summary>
    /// スロット見た目。ドラッグ中は掴み元を空け、ドロップ先へ移動／入れ替えプレビューを出す。
    /// </summary>
    private void PaintSlot(int index, bool dropTarget)
    {
        var slots = _session?.VstInserts.Slots;
        var slot = slots?[index];
        var empty = slot is null || slot.IsEmpty;
        var lifted = _dragMoved && index == _dragFrom;
        var previewDrop = _dragMoved && dropTarget && _dragFrom >= 0 && slots is not null;
        var swapPreviewOnSource = lifted
            && _dragHover >= 0
            && slots is not null
            && !slots[_dragHover].IsEmpty;
        var button = _slots[index];

        if (previewDrop)
        {
            // ドロップ先に名前だけ載せる。青ハイライトは付けない。
            var from = slots![_dragFrom];
            var persisted = SlotIsPersisted(_dragFrom);
            button.Content = SlotLabel(from)
                ?? "?";
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.Opacity = 1;
            ApplySlotButtonLook(button, empty: !persisted, persisted);
            return;
        }

        if (swapPreviewOnSource)
        {
            // 入れ替え時、元スロットには相手が入ってくるプレビュー。
            var incoming = slots![_dragHover];
            PaintFilledSlotLook(
                button,
                incoming,
                SlotIsPersisted(_dragHover),
                opacity: 0.7);
            return;
        }

        if (empty || lifted)
        {
            var persisted = SlotIsPersisted(index);
            button.Content = SlotNumberLabel(index);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
            button.ToolTip = persisted
                ? UiStrings.TooltipVstInsertPersist
                : UiStrings.TooltipVstInsertSlot;
            // 持ち上げた穴は薄く。空き番号だけ残す。
            button.Opacity = lifted ? 0.35 : 1;
            ApplySlotButtonLook(button, empty: true, persisted && !lifted);
            return;
        }

        PaintFilledSlotLook(
            button,
            slot!,
            SlotIsPersisted(index),
            opacity: slot!.Bypassed || slot.Faulted ? 0.55 : 1);
    }

    private static void PaintFilledSlotLook(
        RoundedButton button,
        VstInsertSlot slot,
        bool persisted,
        double opacity)
    {
        button.Content = SlotLabel(slot) ?? "?";
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.ToolTip = UiStrings.TooltipVstInsertSlotFilled
            + (slot.Faulted ? "\n" + UiStrings.ErrorVstInsertLoadFailed : string.Empty)
            + (persisted ? "\n" + UiStrings.TooltipVstInsertPersist : string.Empty);
        button.Opacity = opacity;
        ApplySlotButtonLook(button, empty: false, persisted);
    }

    /// <summary>メニュー／ツールバー／ショートカットの Bounce 有効判定。</summary>
    internal static bool SessionCanBake(DocumentSession? session) =>
        session is not null
        && session.VstInserts.HasProcessableSlot
        && session.Document.FrameCount > 0;

    public void ApplyTheme()
    {
        SyncIncludeTailFromSettings();
        _includeTailBox.ToolTip = UiStrings.TooltipVstBounceIncludeTail;
        _includeTailLabel.Text = UiStrings.LabelVstBounceIncludeTail;
        _includeTailLabel.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        Refresh();
    }

    /// <summary>
    /// ショートカット用。空きならインサートメニュー、使用中ならエディタを開く（Ctrl+クリックのバイパスはしない）。
    /// </summary>
    public bool ActivateSlot(int index)
    {
        if (_session is null || (uint)index >= VstInsertChain.MaxSlots)
        {
            return false;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            ShowEmptyInsertMenu(index, _slots[index]);
        }
        else
        {
            _openEditor?.Invoke(index);
        }

        return true;
    }

    private bool SlotIsEmpty(int index)
    {
        var slot = _session?.VstInserts.Slots[index];
        return slot is null || slot.IsEmpty;
    }

    private void Slot_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragMoved = false;
        _dragFrom = -1;
        _dragHover = -1;
        _dragSource = null;
        HideDragGhost();
        if (sender is not RoundedButton { Tag: int index } button
            || _session is null
            || e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Ctrl+クリックはバイパス。ドラッグは開始しない。
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            return;
        }

        if (_session.VstInserts.Slots[index].IsEmpty)
        {
            return;
        }

        _dragFrom = index;
        _dragSource = button;
        _dragStart = e.GetPosition(this);
        _dragGrabOffset = e.GetPosition(button);
        // ラック全体で Capture。小さなボタン外へ出ても Move/Up を取りこぼさない。
        CaptureMouse();
        e.Handled = true;
    }

    private void Rack_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragFrom < 0 || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (!_dragMoved)
        {
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return;
            }

            _dragMoved = true;
            Cursor = Cursors.SizeAll;
            ShowDragGhost(_dragFrom);
            // 掴んだ元スロットを空けて「持ち上げた」状態にする。
            RefreshDragSurface(pos);
            return;
        }

        RefreshDragSurface(pos);
    }

    private void RefreshDragSurface(Point pos)
    {
        var hover = HitTestSlotIndex(pos);
        if (hover == _dragFrom)
        {
            hover = -1;
        }

        _dragHover = hover;
        for (var i = 0; i < _slots.Length; i++)
        {
            PaintSlot(i, dropTarget: i == hover);
        }

        MoveDragGhost(pos);
    }

    private void ShowDragGhost(int from)
    {
        var slot = _session?.VstInserts.Slots[from];
        var source = _slots[from];
        if (slot is null || slot.IsEmpty || _dragGhost.Child is not TextBlock label)
        {
            HideDragGhost();
            return;
        }

        var name = SlotLabel(slot) ?? "?";
        var persisted = SlotIsPersisted(from);
        label.Text = name;
        label.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get(
            persisted ? "VstInsertSlotPersistForeBrush" : "ExportButtonForeBrush"));
        // RoundedButton の OnRender と同じく中央寄せ（左寄せだと持ち上げ時だけズレて見える）。
        label.HorizontalAlignment = HorizontalAlignment.Stretch;
        label.TextAlignment = TextAlignment.Center;
        label.FontWeight = FontWeights.Bold;
        _dragGhost.Background = WpfControlHelpers.FrozenBrush(Theme.Get(
            persisted ? "VstInsertSlotPersistFillBrush" : "ExportButtonFillBrush"));
        _dragGhost.BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get(
            persisted ? "VstInsertSlotPersistBackBrush" : "ExportButtonBackBrush"));
        _dragGhost.BorderThickness = new Thickness(1);
        _dragGhost.CornerRadius = new CornerRadius(DesignMetrics.ControlCornerRadius);
        // 元スロットと同じ大きさのカードを持ち上げる。
        _dragGhost.Width = Math.Max(1, source.ActualWidth);
        _dragGhost.Height = Math.Max(1, source.ActualHeight);
        _dragGhost.Visibility = Visibility.Visible;
    }

    private void MoveDragGhost(Point pos)
    {
        if (_dragGhost.Visibility != Visibility.Visible)
        {
            return;
        }

        // 掴んだ点を指の下に保ったまま追従（カードを持っている感じ）。
        _dragGhostOffset.X = pos.X - _dragGrabOffset.X;
        _dragGhostOffset.Y = pos.Y - _dragGrabOffset.Y;
    }

    private void HideDragGhost()
    {
        _dragGhost.Visibility = Visibility.Collapsed;
        _dragGhost.Width = double.NaN;
        _dragGhost.Height = double.NaN;
        _dragGhostOffset.X = 0;
        _dragGhostOffset.Y = 0;
    }

    private void Rack_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragFrom < 0)
        {
            return;
        }

        e.Handled = true;
        var from = _dragFrom;
        var moved = _dragMoved;
        var to = moved ? HitTestSlotIndex(e.GetPosition(this)) : -1;
        EndDragArm();

        if (moved)
        {
            if ((uint)to < VstInsertChain.MaxSlots && to != from && _session is not null && !SlotIsEmpty(from))
            {
                CommitSlotMove(from, to);
            }
            else
            {
                Refresh();
            }

            return;
        }

        Slot_Click(from);
    }

    private void Rack_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragFrom < 0)
        {
            return;
        }

        EndDragArm();
        Refresh();
    }

    private void CommitSlotMove(int from, int to)
    {
        if (_moveSlot is not null)
        {
            _moveSlot(from, to);
            return;
        }

        if (_session is null)
        {
            return;
        }

        _session.VstInserts.MoveSlot(from, to);
        DocumentSessionStore.MoveDefaultInsertSlot(AppStorage.Settings, from, to);
        AppStorage.Save();
        Refresh();
        _onChanged?.Invoke();
    }

    private int HitTestSlotIndex(Point pos)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            var topLeft = slot.TranslatePoint(new Point(0, 0), this);
            var bounds = new Rect(topLeft, slot.RenderSize);
            // Margin 内側の見た目に合わせ、少し広げて掴みやすくする。
            bounds.Inflate(2, 2);
            if (bounds.Contains(pos))
            {
                return i;
            }
        }

        return -1;
    }

    private void EndDragArm()
    {
        _dragFrom = -1;
        _dragHover = -1;
        _dragSource = null;
        _dragMoved = false;
        Cursor = Cursors.Arrow;
        HideDragGhost();
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <summary>Button.Click 用。ドラッグ武装中（Handled 済み）の使用中スロットでは来ない。</summary>
    private void Slot_ClickFromButton(int index)
    {
        if (_dragMoved || _dragFrom >= 0)
        {
            return;
        }

        Slot_Click(index);
    }

    private void Slot_Click(int index)
    {
        if (_session is null)
        {
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ToggleBypass(index);
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            // 空きは左クリックで「インサート／全てのタブにインサート」。右クリックは使わない。
            ShowEmptyInsertMenu(index, _slots[index]);
        }
        else
        {
            _openEditor?.Invoke(index);
        }
    }

    private void Slot_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not RoundedButton { Tag: int index } || _session is null)
        {
            return;
        }

        e.Handled = true;
        if (_session.VstInserts.Slots[index].IsEmpty)
        {
            return;
        }

        ShowFilledMenu(index, sender as UIElement);
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
        SyncPersistedBypass(index, slot.Bypassed);
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
            var slot = _session.VstInserts.Slots[index];
            slot.Bypassed = bypass.IsChecked;
            SyncPersistedBypass(index, slot.Bypassed);
            Refresh();
            _onChanged?.Invoke();
        };
        menu.Items.Add(bypass);

        var open = new MenuItem { Header = UiStrings.LabelVstInsertOpenEditor };
        open.Click += (_, _) => _openEditor?.Invoke(index);
        menu.Items.Add(open);

        AddInsertMenuItems(menu, index);

        menu.Items.Add(new Separator());
        AddPersistMenuItems(menu, index, allowPersist: true);

        var remove = new MenuItem { Header = UiStrings.LabelVstInsertRemove };
        remove.Click += (_, _) => ClearSlot(index, allTabs: false);
        menu.Items.Add(remove);

        // 永続化は既に全タブ共通なので「全てのタブから削除」は不要（グレー）。
        var removeAllTabs = new MenuItem
        {
            Header = UiStrings.LabelVstInsertRemoveAllTabs,
            IsEnabled = !SlotIsPersisted(index) && (_canInsertAllTabs?.Invoke() ?? false),
        };
        removeAllTabs.Click += (_, _) =>
        {
            if (SlotIsPersisted(index) || _canInsertAllTabs?.Invoke() != true)
            {
                return;
            }

            ClearSlot(index, allTabs: true);
        };
        menu.Items.Add(removeAllTabs);

        menu.Items.Add(new Separator());
        var removeAll = new MenuItem { Header = UiStrings.LabelVstInsertRemoveAll };
        removeAll.Click += (_, _) => ClearAllSlots();
        menu.Items.Add(removeAll);

        OpenContextMenu(menu, placement);
    }

    private void ShowEmptyInsertMenu(int index, UIElement? placement)
    {
        var menu = new ContextMenu { MaxHeight = VstInsertPluginMenu.ResolveMaxHeight() };
        AddInsertMenuItems(menu, index);

        // 空きでも永続中なら「永続化を解除」を出す（グレーの永続化は出さない）。
        if (SlotIsPersisted(index))
        {
            menu.Items.Add(new Separator());
            AddPersistMenuItems(menu, index, allowPersist: false);
        }

        OpenContextMenu(menu, placement);
    }

    private void AddInsertMenuItems(ContextMenu menu, int index)
    {
        var insert = new MenuItem { Header = UiStrings.LabelVstInsert };
        insert.Click += (_, _) => ShowPluginPicker(index, allTabs: false);
        menu.Items.Add(insert);

        var insertAll = new MenuItem
        {
            Header = UiStrings.LabelVstInsertAllTabs,
            IsEnabled = _canInsertAllTabs?.Invoke() ?? false,
        };
        insertAll.Click += (_, _) =>
        {
            if (_canInsertAllTabs?.Invoke() != true)
            {
                return;
            }

            ShowPluginPicker(index, allTabs: true);
        };
        menu.Items.Add(insertAll);
    }

    private void AddPersistMenuItems(ContextMenu menu, int index, bool allowPersist)
    {
        var alreadyPersisted = SlotIsPersisted(index);
        var matchesCurrent = alreadyPersisted
            && _session is not null
            && DocumentSessionStore.SlotMatchesPersisted(
                AppStorage.Settings.DefaultVstInserts,
                index,
                _session.VstInserts.Slots[index]);
        if (allowPersist)
        {
            var persist = new MenuItem
            {
                Header = UiStrings.LabelVstInsertPersist,
                ToolTip = UiStrings.TooltipVstInsertPersist,
                // 同じ内容が既に記憶済みのときだけグレー。差し替え後は再度永続化できる。
                IsEnabled = !matchesCurrent,
                // 黄色い閉じた鍵。
                Icon = CreateMenuIcon(TransportIcon.Lock, Theme.Get("KeepTargetLockForeBrush")),
            };
            persist.Click += (_, _) => PersistSlot(index);
            menu.Items.Add(persist);
        }

        var clear = new MenuItem
        {
            Header = UiStrings.LabelVstInsertPersistClear,
            ToolTip = UiStrings.TooltipVstInsertPersistClear,
            IsEnabled = alreadyPersisted,
            // 黄色い開いた鍵。
            Icon = CreateMenuIcon(TransportIcon.Unlock, Theme.Get("KeepTargetLockForeBrush")),
        };
        clear.Click += (_, _) =>
        {
            if (!SlotIsPersisted(index))
            {
                return;
            }

            ClearPersistedSlot(index);
        };
        menu.Items.Add(clear);
    }

    private void PersistSlot(int index)
    {
        if (_session is null || (uint)index >= VstInsertChain.MaxSlots)
        {
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        // ライブで刺さっている state をスロットへ戻してから記憶する。
        _captureStates?.Invoke();
        DocumentSessionStore.SetDefaultInsertSlot(
            AppStorage.Settings,
            index,
            DocumentSessionStore.CaptureSlot(slot));
        AppStorage.Save();
        // 永続化は全タブ共通。他タブの同じスロットにも載せる。
        _syncPersistedToAllTabs?.Invoke();
        Refresh();
    }

    private void ClearPersistedSlot(int index)
    {
        // 記憶だけ外す。スロット上のプラグインは残す（着色は空き／通常へ戻る）。
        DocumentSessionStore.ClearDefaultInsertSlot(AppStorage.Settings, index);
        AppStorage.Save();
        Refresh();
    }

    /// <summary>永続化中なら、今刺さっている内容で記憶を更新する。</summary>
    private void SyncPersistedFromCurrent(int index)
    {
        if (_session is null || (uint)index >= VstInsertChain.MaxSlots)
        {
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        _captureStates?.Invoke();
        if (DocumentSessionStore.TrySyncDefaultInsertFromSlot(AppStorage.Settings, index, slot))
        {
            AppStorage.Save();
            Refresh();
        }
    }

    /// <summary>永続化済みスロットなら、バイパス変更を記憶と全タブへ反映する。</summary>
    private void SyncPersistedBypass(int index, bool bypassed)
    {
        if (_session is null)
        {
            if (DocumentSessionStore.TrySyncDefaultInsertBypass(AppStorage.Settings, index, bypassed))
            {
                AppStorage.Save();
                _syncPersistedToAllTabs?.Invoke();
            }

            return;
        }

        _session.VstInserts.Slots[index].Bypassed = bypassed;
        if (!SlotIsPersisted(index))
        {
            return;
        }

        // バイパスだけでなく、刺さっている実体全体を記憶し直し、全タブへ載せる。
        SyncPersistedFromCurrent(index);
        _syncPersistedToAllTabs?.Invoke();
    }

    private static void OpenContextMenu(ContextMenu menu, UIElement? placement)
    {
        // スロットボタンは Bold なので、継承でメニュー文字が太くならないよう切る。
        menu.FontWeight = FontWeights.Normal;
        if (placement is not null)
        {
            menu.PlacementTarget = placement;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        }

        menu.IsOpen = true;
    }

    private static TransportIconGlyph CreateMenuIcon(TransportIcon icon, Color? fore = null) =>
        new()
        {
            Icon = icon,
            Width = 18,
            Height = 18,
            ForeOverride = fore,
        };

    private void ShowPluginPicker(int index, bool allTabs)
    {
        if (_session is null)
        {
            return;
        }

        var recognized = Vst3PluginList.Load().Plugins.Where(p => p.IsRecognized);
        var hostChannels = Math.Max(1, _session.Document.Channels);
        var plugins = CollectPickerEntries(recognized, hostChannels);
        var owner = Window.GetWindow(this) ?? Application.Current?.MainWindow;
        var picked = VstInsertPluginPickerWindow.Show(owner, plugins);
        if (picked is { } entry)
        {
            AssignPlugin(index, entry, allTabs);
        }
    }

    /// <summary>
    /// 認識済みをチャンネル適合で絞ったフラット一覧。選択ウィンドウの入力。
    /// WaveShell はスキャン時に ClassId 付きで展開済みであること（ここでは展開しない）。
    /// hostChannels に対し、Mono/Stereo 併記はトラック幅に合う方だけ残す（DAW 流儀）。
    /// </summary>
    internal static List<VstPickerEntry> CollectPickerEntries(
        IEnumerable<Vst3PluginEntry> recognized,
        int hostChannels = 2)
    {
        var collected = new List<VstPickerEntry>();
        foreach (var plugin in recognized)
        {
            if (Vst3FactoryProbe.IsWaveShellPath(plugin.Path)
                && string.IsNullOrWhiteSpace(plugin.ClassId))
            {
                continue;
            }

            var entry = VstPickerEntry.From(plugin);
            if (collected.Any(e =>
                    string.Equals(e.Name, entry.Name, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(e.ClassId, entry.ClassId, StringComparison.Ordinal)
                    && string.Equals(NormalizeVendorKey(e.Vendor), NormalizeVendorKey(entry.Vendor), StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Waves Shell 展開後の Vendor 表記を揃える。
            if (NormalizeVendorKey(entry.Vendor).Contains("WaveShell", StringComparison.OrdinalIgnoreCase))
            {
                entry = entry with { Vendor = "Waves" };
            }

            collected.Add(entry);
        }

        return VstPluginChannelFit.FilterSiblings(collected, e => e.Name, hostChannels);
    }

    private static string NormalizeVendorKey(string? vendor) =>
        string.IsNullOrWhiteSpace(vendor)
            ? UiStrings.LabelVstInsertVendorUnknown
            : vendor.Trim();

    private void AssignPlugin(int index, VstPickerEntry entry, bool allTabs)
    {
        if (_session is null)
        {
            return;
        }

        // 差し替えは MainWindow 側でアンロード→ロードの二段階にする
        if (_assignPlugin is not null)
        {
            _assignPlugin(index, entry, allTabs);
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

    /// <param name="allTabs">
    /// true のとき、同じスロット番号かつ表示名が一致するプラグインだけ全タブから外す。
    /// </param>
    private void ClearSlot(int index, bool allTabs)
    {
        if (_session is null || (uint)index >= VstInsertChain.MaxSlots)
        {
            return;
        }

        var slot = _session.VstInserts.Slots[index];
        if (slot.IsEmpty)
        {
            return;
        }

        var displayName = slot.DisplayName;
        var pluginPath = slot.PluginPath;
        var classId = slot.ClassId;
        var persisted = SlotIsPersisted(index);

        // 永続中なら記憶も外す（着色を戻し、再適用で蘇らないようにする）。
        if (persisted)
        {
            DocumentSessionStore.ClearDefaultInsertSlot(AppStorage.Settings, index);
            AppStorage.Save();
        }

        if (allTabs && _clearMatchingSlotOnAllTabs is not null)
        {
            _clearMatchingSlotOnAllTabs(index, displayName, pluginPath, classId);
        }
        else
        {
            _session.VstInserts.ClearSlot(index);
        }

        Refresh();
        _onChanged?.Invoke();
    }

    private void ClearAllSlots()
    {
        if (_session is null || _session.VstInserts.IsEmpty)
        {
            return;
        }

        var settings = AppStorage.Settings;
        var clearedPersist = false;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (_session.VstInserts.Slots[i].IsEmpty)
            {
                continue;
            }

            // 「すべて削除」は今のタブのみ。永続記憶だけは外して着色と再適用を止める。
            if (SlotIsPersisted(i))
            {
                DocumentSessionStore.ClearDefaultInsertSlot(settings, i);
                clearedPersist = true;
            }

            _session.VstInserts.ClearSlot(i);
        }

        if (clearedPersist)
        {
            AppStorage.Save();
        }

        Refresh();
        _onChanged?.Invoke();
    }

    /// <summary>表示名が取れるときは名前一致。無いときだけパス／ClassId。</summary>
    internal static bool SlotMatchesPluginForClear(
        VstInsertSlot slot,
        string? displayName,
        string? pluginPath,
        string? classId)
    {
        if (slot.IsEmpty)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return string.Equals(slot.DisplayName, displayName, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(slot.PluginPath, pluginPath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(slot.ClassId ?? string.Empty, classId ?? string.Empty, StringComparison.Ordinal);
    }

    private static string SlotNumberLabel(int index) => (index + 1).ToString();

    /// <summary>表示名。幅に応じた省略は <see cref="RoundedButton"/> の TextTrimming に任せる。</summary>
    private static string? SlotLabel(VstInsertSlot slot)
    {
        if (!string.IsNullOrWhiteSpace(slot.DisplayName))
        {
            return slot.DisplayName;
        }

        return System.IO.Path.GetFileNameWithoutExtension(slot.PluginPath);
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
