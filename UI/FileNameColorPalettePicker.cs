using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>設定の色。閉じているときは見本、開くと右クリックと同じパレット。</summary>
internal sealed class FileNameColorPalettePicker : Border
{
    private readonly Border _chip;
    private readonly Popup _popup;
    private readonly FileNameColorPaletteBoard _board;

    public FileNameColorPalettePicker(int colorIndex)
    {
        Width = 72;
        Height = DesignMetrics.AudioInputHeight;
        Margin = new Thickness(8, 0, 8, 0);
        BorderThickness = new Thickness(1);
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        SnapsToDevicePixels = true;
        SetResourceReference(BackgroundProperty, "PlayerComboFillBrush");
        SetResourceReference(BorderBrushProperty, "ChromeBorderBrush");

        _chip = new Border
        {
            Width = FileNameColorPaletteBoard.SwatchSize,
            Height = FileNameColorPaletteBoard.SwatchSize,
            // 完全な円。パレット見本と同じく、設定の GUI 角丸は使わない。
            CornerRadius = FileNameColorPaletteBoard.SwatchCorner,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
        };
        DesignMetrics.BindCorner(this);
        Child = _chip;

        _board = new FileNameColorPaletteBoard { Margin = new Thickness(4, 2, 4, 4) };
        var drop = DesignMetrics.BindCorner(new Border
        {
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0, 2, 0, 0),
            Child = _board,
            SnapsToDevicePixels = true,
        });
        drop.Background = Brushes.Transparent;
        drop.SetResourceReference(BorderBrushProperty, "PlayerComboDropBorderBrush");
        _popup = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = false,
            Child = drop,
        };
        AcrylicPopup.Attach(_popup);
        _board.ColorSelected += (_, index) =>
        {
            SetIndex(index);
            _popup.IsOpen = false;
        };
        MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            Open();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space or Key.Down))
            {
                return;
            }

            e.Handled = true;
            Open();
        };
        Unloaded += (_, _) => _popup.IsOpen = false;
        SetIndex(colorIndex);
    }

    public int SelectedIndex { get; private set; }

    private void Open()
    {
        _board.Mark(SelectedIndex);
        _popup.IsOpen = true;
    }

    private void SetIndex(int index)
    {
        if (!FileNameColorMarkerVisual.TryBrush(index, out var brush))
        {
            index = 0;
            FileNameColorMarkerVisual.TryBrush(index, out brush);
        }

        SelectedIndex = index;
        _chip.Background = brush;
        ToolTip = FileNameColorPalette.HexLabel(index);
        _board.Mark(index);
    }
}
