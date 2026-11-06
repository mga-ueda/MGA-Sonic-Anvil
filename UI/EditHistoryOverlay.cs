using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>履歴オーバーレイの行クリック（修飾キー付き）。</summary>
internal readonly record struct HistoryItemClick(int Index, ModifierKeys Modifiers);

internal sealed class EditHistoryOverlay : Border
{
    private readonly StackPanel _items = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBlock _title;
    private readonly TextBlock _hint;
    private readonly DispatcherTimer _statusTimer;
    private Popup? _acrylicHost;

    public event EventHandler<HistoryItemClick>? ItemClicked;

    public event EventHandler? CloseRequested;

    /// <summary>すりガラス用 Popup が開いているか。</summary>
    public bool IsPanelOpen => _acrylicHost?.IsOpen == true;

    public EditHistoryOverlay()
    {
        MinWidth = DesignMetrics.EditHistoryOverlayMinWidth;
        MinHeight = DesignMetrics.EditHistoryOverlayMinHeight;
        Width = DesignMetrics.EditHistoryOverlayDefaultWidth;
        Height = DesignMetrics.EditHistoryOverlayDefaultHeight;
        Padding = new Thickness(0, 0, 0, 6);
        // 塗りは DWM Acrylic ティント。枠だけ描く。
        Background = Brushes.Transparent;
        SetResourceReference(BorderBrushProperty, "PlayerComboDropBorderBrush");
        BorderThickness = new Thickness(1);
        DesignMetrics.BindCorner(this);
        SnapsToDevicePixels = true;
        Focusable = false;

        _title = new TextBlock
        {
            Text = UiStrings.EditHistoryTitle,
            Margin = new Thickness(10, 8, 4, 2),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _title.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        var caption = new DockPanel();
        var close = OverlayCaption.CloseButton(
            () => CloseRequested?.Invoke(this, EventArgs.Empty),
            clearRest: true);
        OverlayCaption.PinCorner(close);
        DockPanel.SetDock(close, Dock.Right);
        caption.Children.Add(close);
        caption.Children.Add(_title);
        _hint = new TextBlock
        {
            Text = UiStrings.EditHistoryHint,
            Margin = new Thickness(10, 0, 10, 6),
            FontSize = 10,
            TextWrapping = TextWrapping.Wrap,
        };
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        _scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _items,
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _hint.Text = UiStrings.EditHistoryHint;
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
        };

        var body = new DockPanel();
        DockPanel.SetDock(caption, Dock.Top);
        DockPanel.SetDock(_hint, Dock.Top);
        body.Children.Add(caption);
        body.Children.Add(_hint);
        body.Children.Add(_scroll);

        var root = new Grid();
        root.Children.Add(body);
        root.Children.Add(CreateResizeGrip());
        Child = root;
    }

    public void Open(UIElement placementTarget)
    {
        ArgumentNullException.ThrowIfNull(placementTarget);
        EnsureAcrylicHost(placementTarget);
        ApplySavedSize();
        Visibility = Visibility.Visible;
        // まず中央に出してから Absolute で左上を固定（リサイズが普通の窓と同じになる）。
        _acrylicHost!.Placement = PlacementMode.Center;
        _acrylicHost.HorizontalOffset = 0;
        _acrylicHost.VerticalOffset = 0;
        _acrylicHost.IsOpen = true;
        Dispatcher.BeginInvoke(PinTopLeftAbsolute, DispatcherPriority.Loaded);
    }

    public void Close()
    {
        PersistSize();
        if (_acrylicHost is not null)
        {
            _acrylicHost.IsOpen = false;
        }

        Visibility = Visibility.Collapsed;
    }

    public void ApplyLocalizedText()
    {
        _statusTimer.Stop();
        _title.Text = UiStrings.EditHistoryTitle;
        _hint.Text = UiStrings.EditHistoryHint;
        _hint.SetResourceReference(TextBlock.ForegroundProperty, "MutedForeBrush");
    }

    public void RefreshAppearance() => ApplyLocalizedText();

    /// <summary>ヒント行に一時メッセージ（コピー／適用件数など）を表示する。</summary>
    public void FlashStatus(string text)
    {
        _hint.Text = text;
        _hint.Foreground = (Brush)Application.Current.FindResource("AccentCyanBrush");
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    public void SetItems(
        IReadOnlyList<EditHistoryEntry> items,
        int selectedIndex,
        IReadOnlySet<int>? copySelected = null)
    {
        _items.Children.Clear();
        // 選択行はメニューのハイライトと同じ配色（テーマ変更にも追従する）。
        var selectedBack = (Brush)Application.Current.FindResource("MenuHighlightBackBrush");
        var selectedFore = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var idleFore = (Brush)Application.Current.FindResource("PrimaryForeBrush");
        var futureFore = (Brush)Application.Current.FindResource("MutedForeBrush");
        var copyMark = (Brush)Application.Current.FindResource("AccentCyanBrush");
        FrameworkElement? selectedRow = null;
        foreach (var item in items)
        {
            var selected = item.Index == selectedIndex;
            var future = item.Index > selectedIndex;
            var copying = copySelected is not null && copySelected.Contains(item.Index);
            var row = new Border
            {
                Tag = item.Index,
                Padding = new Thickness(8, 4, 10, 4),
                Background = selected ? selectedBack : Brushes.Transparent,
                BorderThickness = new Thickness(2, 0, 0, 0),
                BorderBrush = copying ? copyMark : Brushes.Transparent,
                Cursor = Cursors.Hand,
            };
            var label = new TextBlock
            {
                Text = item.Title,
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = item.Title,
                Foreground = copying && !selected
                    ? copyMark
                    : selected ? selectedFore : future ? futureFore : idleFore,
            };
            row.Child = label;
            row.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                if (row.Tag is int index)
                {
                    ItemClicked?.Invoke(this, new HistoryItemClick(index, Keyboard.Modifiers));
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

    private Thumb CreateResizeGrip()
    {
        var gripSize = DesignMetrics.EditHistoryOverlayResizeGrip;
        var thumb = new Thumb
        {
            Width = gripSize,
            Height = gripSize,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            Focusable = false,
            Margin = new Thickness(0, 0, 2, 2),
            Template = CreateGripTemplate(gripSize),
        };
        thumb.DragDelta += (_, e) => ResizeBy(e.HorizontalChange, e.VerticalChange);
        thumb.DragCompleted += (_, _) => PersistSize();
        return thumb;
    }

    private static ControlTemplate CreateGripTemplate(double size)
    {
        var template = new ControlTemplate(typeof(Thumb));
        var root = new FrameworkElementFactory(typeof(Grid));
        var hit = new FrameworkElementFactory(typeof(Border));
        hit.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        root.AppendChild(hit);

        var canvas = new FrameworkElementFactory(typeof(Canvas));
        canvas.SetValue(WidthProperty, size);
        canvas.SetValue(HeightProperty, size);
        canvas.SetValue(IsHitTestVisibleProperty, false);
        AppendGripLineFactory(canvas, size, inset: 4);
        AppendGripLineFactory(canvas, size, inset: 8);
        root.AppendChild(canvas);
        template.VisualTree = root;
        return template;
    }

    private static void AppendGripLineFactory(FrameworkElementFactory canvas, double size, double inset)
    {
        var line = new FrameworkElementFactory(typeof(Line));
        line.SetValue(Line.X1Property, inset);
        line.SetValue(Line.Y1Property, size - 2);
        line.SetValue(Line.X2Property, size - 2);
        line.SetValue(Line.Y2Property, inset);
        line.SetValue(Shape.StrokeThicknessProperty, 1.0);
        line.SetResourceReference(Shape.StrokeProperty, "MutedForeBrush");
        line.SetValue(UIElement.OpacityProperty, 0.7);
        canvas.AppendChild(line);
    }

    private void ResizeBy(double dx, double dy)
    {
        // Absolute 固定後に幅・高さだけ変える（左上は固定、右下へ伸びる）。
        if (_acrylicHost is { Placement: not PlacementMode.Absolute })
        {
            PinTopLeftAbsolute();
        }

        var max = MaxPanelSize();
        Width = Math.Clamp(Width + dx, MinWidth, max.Width);
        Height = Math.Clamp(Height + dy, MinHeight, max.Height);
    }

    /// <summary>
    /// 画面上の左上を Absolute に固定する。Center のままだとリサイズで全体が動く。
    /// </summary>
    private void PinTopLeftAbsolute()
    {
        if (_acrylicHost is null || !_acrylicHost.IsOpen)
        {
            return;
        }

        Point screen;
        try
        {
            screen = PointToScreen(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var dip = ScreenPixelsToDip(screen);
        _acrylicHost.Placement = PlacementMode.Absolute;
        _acrylicHost.HorizontalOffset = dip.X;
        _acrylicHost.VerticalOffset = dip.Y;
    }

    private Point ScreenPixelsToDip(Point screenPixels)
    {
        var source = PresentationSource.FromVisual(this)
            ?? (_acrylicHost?.PlacementTarget is Visual target
                ? PresentationSource.FromVisual(target)
                : null);
        if (source?.CompositionTarget is null)
        {
            return screenPixels;
        }

        return source.CompositionTarget.TransformFromDevice.Transform(screenPixels);
    }

    private void ApplySavedSize()
    {
        var settings = AppStorage.Settings;
        var max = MaxPanelSize();
        var width = settings.EditHistoryOverlayWidth > 0
            ? settings.EditHistoryOverlayWidth
            : DesignMetrics.EditHistoryOverlayDefaultWidth;
        var height = settings.EditHistoryOverlayHeight > 0
            ? settings.EditHistoryOverlayHeight
            : DesignMetrics.EditHistoryOverlayDefaultHeight;
        Width = Math.Clamp(width, MinWidth, max.Width);
        Height = Math.Clamp(height, MinHeight, max.Height);
    }

    private void PersistSize()
    {
        var settings = AppStorage.Settings;
        var width = (int)Math.Round(Width);
        var height = (int)Math.Round(Height);
        if (settings.EditHistoryOverlayWidth == width
            && settings.EditHistoryOverlayHeight == height)
        {
            return;
        }

        settings.EditHistoryOverlayWidth = width;
        settings.EditHistoryOverlayHeight = height;
        AppStorage.Save();
    }

    private static Size MaxPanelSize()
    {
        var work = SystemParameters.WorkArea;
        return new Size(
            Math.Max(DesignMetrics.EditHistoryOverlayMinWidth, work.Width * 0.9),
            Math.Max(DesignMetrics.EditHistoryOverlayMinHeight, work.Height * 0.9));
    }

    private void EnsureAcrylicHost(UIElement placementTarget)
    {
        if (_acrylicHost is not null)
        {
            _acrylicHost.PlacementTarget = placementTarget;
            return;
        }

        DetachFromParent();
        Background = Brushes.Transparent;
        _acrylicHost = new Popup
        {
            AllowsTransparency = false,
            Placement = PlacementMode.Center,
            PlacementTarget = placementTarget,
            StaysOpen = true,
            Focusable = false,
            Child = this,
        };
        AcrylicPopup.Attach(_acrylicHost);
    }

    private void DetachFromParent()
    {
        switch (Parent)
        {
            case Panel panel:
                panel.Children.Remove(this);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, this):
                decorator.Child = null;
                break;
            case ContentControl content when ReferenceEquals(content.Content, this):
                content.Content = null;
                break;
            case Popup popup when ReferenceEquals(popup.Child, this):
                popup.Child = null;
                break;
        }
    }
}
