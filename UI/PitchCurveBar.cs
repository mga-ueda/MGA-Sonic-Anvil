using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal enum PitchCurveBarTool
{
    Points,
    Brush,
    /// <summary>カーブ編集せず、波形クリックでシークする。</summary>
    SeekBar,
}

internal enum PitchCurveCommitKind
{
    /// <summary>↑↓ での ±半音。常に平坦化する。</summary>
    Nudge,

    /// <summary>フォーカス離脱。平坦カーブのときだけ反映。</summary>
    Blur,

    /// <summary>Enter。平坦カーブのときだけ数値を反映する（モード確定はしない）。</summary>
    Enter,
}

/// <summary>
/// P キーでピッチカーブ編集モードに入ったときに、波形描画エリア直下へ出す操作バー。
/// 波形は持たない。カーブ編集は <see cref="PitchCurveEditLayer"/> がメイン波形の上に重ねて描く。
/// 幅に余裕があれば 1 行、足りなければ 2 行に切り替える。
/// </summary>
internal sealed class PitchCurveBar : UserControl
{
    private readonly TextBox _pitchBox;
    private readonly RadioButton _pointsRadio;
    private readonly RadioButton _brushRadio;
    private readonly RadioButton _seekBarRadio;
    private readonly CheckBox _snapScaleBox;
    private readonly CheckBox _noTimeStretchBox;
    private readonly CheckBox _lightweightBox;
    private readonly Panel _shapeButtons;
    private readonly FrameworkElement _pitchAccessLabel;
    private readonly FrameworkElement _semitoneLabel;
    private readonly FrameworkElement _shapeLabel;
    private readonly RoundedButton _addPointButton;
    private readonly RoundedButton _resetButton;
    private readonly RoundedButton _bypassButton;
    private readonly RoundedButton _okButton;
    private readonly RoundedButton _cancelButton;
    private readonly StackPanel _row1;
    private readonly StackPanel _row2;
    private readonly StackPanel _root;
    private readonly FrameworkElement[] _leadingItems;
    private readonly FrameworkElement[] _trailingItems;
    private FadeShape _selectedShape = FadeShape.Linear;
    private bool _shapeIconsRising = true;
    private bool _suppressToolEvent;
    private bool _suppressOptionEvents;
    private bool? _singleRowLayout;
    private const double LayoutHysteresisDip = 12;

    public event Action<PitchCurveBarTool>? ToolChanged;
    public event Action<float, PitchCurveCommitKind>? PitchCommitted;
    public event Action<FadeShape>? ShapeSelected;
    public event Action? AddPoint;
    public event Action? Reset;
    public event Action? Accepted;
    public event Action? Cancelled;
    public event Action? PreviewBypass;
    public event Action<bool>? SnapToScaleChanged;
    public event Action<bool>? NoTimeStretchChanged;
    public event Action<bool>? LightweightModeChanged;

    public PitchCurveBarTool Tool
    {
        get => _seekBarRadio.IsChecked == true
            ? PitchCurveBarTool.SeekBar
            : _brushRadio.IsChecked == true
                ? PitchCurveBarTool.Brush
                : PitchCurveBarTool.Points;
        set
        {
            _suppressToolEvent = true;
            try
            {
                if (value == PitchCurveBarTool.SeekBar)
                {
                    _seekBarRadio.IsChecked = true;
                }
                else if (value == PitchCurveBarTool.Brush)
                {
                    _brushRadio.IsChecked = true;
                }
                else
                {
                    _pointsRadio.IsChecked = true;
                }
            }
            finally
            {
                _suppressToolEvent = false;
            }

            RefreshPointModeControls();
        }
    }

    public bool SnapToScale
    {
        get => _snapScaleBox.IsChecked == true;
        set
        {
            _suppressOptionEvents = true;
            try
            {
                _snapScaleBox.IsChecked = value;
            }
            finally
            {
                _suppressOptionEvents = false;
            }

            RefreshPointModeControls();
        }
    }

    public bool NoTimeStretch => _noTimeStretchBox.IsChecked == true;

    public bool LightweightMode => _lightweightBox.IsChecked == true;

    public PitchCurveBar()
    {
        Height = DesignMetrics.PitchCurveBarHeight;
        MinHeight = DesignMetrics.PitchCurveBarSingleRowHeight;
        Focusable = false;
        Background = Brushes.Transparent;
        Visibility = Visibility.Collapsed;
        ClipToBounds = true;

        _pointsRadio = CreateRadio(UiStrings.LabelPitchCurvePoints, isChecked: true, "pitchBarTool");
        _brushRadio = CreateRadio(UiStrings.LabelPitchCurveBrush, isChecked: false, "pitchBarTool");
        _seekBarRadio = CreateRadio(UiStrings.LabelPitchCurveSeekBar, isChecked: false, "pitchBarTool");
        _pointsRadio.Checked += (_, _) =>
        {
            RefreshPointModeControls();
            if (!_suppressToolEvent)
            {
                ToolChanged?.Invoke(PitchCurveBarTool.Points);
            }
        };
        _brushRadio.Checked += (_, _) =>
        {
            RefreshPointModeControls();
            if (!_suppressToolEvent)
            {
                ToolChanged?.Invoke(PitchCurveBarTool.Brush);
            }
        };
        _seekBarRadio.Checked += (_, _) =>
        {
            RefreshPointModeControls();
            if (!_suppressToolEvent)
            {
                ToolChanged?.Invoke(PitchCurveBarTool.SeekBar);
            }
        };

        _pitchBox = new TextBox
        {
            Width = DesignMetrics.From96(64),
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            Text = "0",
            TextAlignment = TextAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
        };
        if (TryFindResource("DarkTextBoxStyle") is Style pitchStyle)
        {
            _pitchBox.Style = pitchStyle;
        }

        _pitchBox.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            NudgePitch(Math.Sign(e.Delta));
        };
        _pitchBox.LostKeyboardFocus += (_, _) => CommitPitchBox(PitchCurveCommitKind.Blur);
        _pitchBox.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down
                && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                var step = Keyboard.Modifiers == ModifierKeys.Shift ? 12 : 1;
                NudgePitch(key == Key.Up ? step : -step);
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                CommitPitchBox(PitchCurveCommitKind.Enter);
            }
        };

        _snapScaleBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveSnapToScale,
            IsChecked = AppStorage.Settings.PitchCurveSnapToScale,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 0),
        };
        _noTimeStretchBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveNoTimeStretch,
            IsChecked = AppStorage.Settings.PitchCurveNoTimeStretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
        };
        _lightweightBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveLightweightMode,
            IsChecked = AppStorage.Settings.PitchCurveLightweightMode,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            ToolTip = UiStrings.TipPitchCurveLightweightMode,
        };
        if (TryFindResource("DarkCheckBoxStyle") is Style checkStyle)
        {
            _snapScaleBox.Style = checkStyle;
            _noTimeStretchBox.Style = checkStyle;
            _lightweightBox.Style = checkStyle;
        }

        _snapScaleBox.Checked += (_, _) => RaiseSnapToScaleChanged(true);
        _snapScaleBox.Unchecked += (_, _) => RaiseSnapToScaleChanged(false);
        _noTimeStretchBox.Checked += (_, _) => RaiseNoTimeStretchChanged(true);
        _noTimeStretchBox.Unchecked += (_, _) => RaiseNoTimeStretchChanged(false);
        _lightweightBox.Checked += (_, _) => RaiseLightweightModeChanged(true);
        _lightweightBox.Unchecked += (_, _) => RaiseLightweightModeChanged(false);

        _shapeButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var shapeOrder = FadeCurves.MenuOrderFadeIn;
        for (var i = 0; i < shapeOrder.Count; i++)
        {
            _shapeButtons.Children.Add(CreateShapeButton(shapeOrder[i], number: i + 1));
        }

        _addPointButton = CreateChromeButton(UiStrings.LabelPitchCurveAddPoint);
        _addPointButton.Click += (_, _) => AddPoint?.Invoke();

        _resetButton = CreateChromeButton(UiStrings.LabelPitchCurveReset);
        _resetButton.Click += (_, _) => Reset?.Invoke();

        // ピッチ適用再生は Space。バーには「無視して再生」だけ残す。
        _bypassButton = CreateChromeButton(UiStrings.LabelPitchCurvePlayBypass);
        _bypassButton.Click += (_, _) => PreviewBypass?.Invoke();

        _pointsRadio.Margin = new Thickness(12, 0, 0, 0);
        _brushRadio.Margin = new Thickness(8, 0, 0, 0);
        _seekBarRadio.Margin = new Thickness(8, 0, 0, 0);

        _okButton = new RoundedButton
        {
            Content = UiStrings.ButtonVolumeCurveOk,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ActionButtonLooks.ApplyAccent(_okButton);
        _okButton.Click += (_, _) => Accepted?.Invoke();

        _cancelButton = new RoundedButton
        {
            Content = UiStrings.ButtonCancelMnemonic,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ActionButtonLooks.ApplyClear(_cancelButton);
        _cancelButton.Click += (_, _) => Cancelled?.Invoke();

        _addPointButton.Margin = new Thickness(0, 0, 8, 0);
        _resetButton.Margin = new Thickness(0, 0, 8, 0);
        _bypassButton.Margin = new Thickness(8, 0, 8, 0);

        _pitchAccessLabel = CreateAccessLabel(UiStrings.LabelPitchCurvePitchValue, _pitchBox);
        _pitchAccessLabel.Margin = new Thickness(0, 0, 4, 0);
        _semitoneLabel = CreateLabel(UiStrings.LabelSemitone, margin: new Thickness(6, 0, 0, 0));
        _shapeLabel = CreateLabel(UiStrings.LabelPitchCurveShape, margin: new Thickness(0, 0, 6, 0));

        _leadingItems =
        [
            _pitchAccessLabel,
            _pitchBox,
            _semitoneLabel,
            _pointsRadio,
            _brushRadio,
            _seekBarRadio,
            _snapScaleBox,
            _noTimeStretchBox,
            _lightweightBox,
        ];
        _trailingItems =
        [
            _shapeLabel,
            _shapeButtons,
            _addPointButton,
            _resetButton,
            _bypassButton,
            _okButton,
            _cancelButton,
        ];

        _row1 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _row2 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = _root;

        ApplyRowLayout(singleRow: false);
        SizeChanged += (_, _) => UpdateRowLayout();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                // 表示直後は ActualWidth が揃ってから測る。
                Dispatcher.BeginInvoke(UpdateRowLayout, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        };

        SelectShape(_selectedShape);
        RefreshPointModeControls();
    }

    /// <summary>親幅に合わせて 1 行／2 行を切り替える。</summary>
    private void UpdateRowLayout()
    {
        if (!IsVisible || ActualWidth < 2)
        {
            return;
        }

        var needed = MeasureSingleRowNeededWidth();
        if (needed < 1)
        {
            return;
        }

        var hysteresis = DesignMetrics.From96(LayoutHysteresisDip);
        var preferSingle = _singleRowLayout == true
            ? ActualWidth >= needed
            : ActualWidth >= needed + hysteresis;
        ApplyRowLayout(preferSingle);
    }

    private double MeasureSingleRowNeededWidth()
    {
        // いまの親子に関係なく DesiredSize を取り、StackPanel 相当の横幅（余白込み）を積む。
        var available = new Size(double.PositiveInfinity, double.PositiveInfinity);
        var width = DesignMetrics.From96(20); // 左右マージン相当
        width += MeasureItemsWidth(_leadingItems, available);
        width += MeasureItemsWidth(_trailingItems, available);
        // 1 行時に形状ラベルへ足す左マージン分。
        width += DesignMetrics.From96(16);
        return width;
    }

    private static double MeasureItemsWidth(FrameworkElement[] items, Size available)
    {
        var width = 0d;
        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            item.Measure(available);
            width += item.DesiredSize.Width + item.Margin.Left + item.Margin.Right;
        }

        return width;
    }

    private void ApplyRowLayout(bool singleRow)
    {
        if (_singleRowLayout == singleRow && _root.Children.Count > 0)
        {
            return;
        }

        _singleRowLayout = singleRow;
        _row1.Children.Clear();
        _row2.Children.Clear();
        _root.Children.Clear();

        if (singleRow)
        {
            Height = DesignMetrics.PitchCurveBarSingleRowHeight;
            _row1.Margin = new Thickness(10, 2, 10, 2);
            _shapeLabel.Margin = new Thickness(16, 0, 6, 0);
            foreach (var item in _leadingItems)
            {
                _row1.Children.Add(item);
            }

            foreach (var item in _trailingItems)
            {
                _row1.Children.Add(item);
            }

            _root.Children.Add(_row1);
            return;
        }

        Height = DesignMetrics.PitchCurveBarHeight;
        _row1.Margin = new Thickness(10, 2, 10, 1);
        _row2.Margin = new Thickness(10, 1, 10, 2);
        _shapeLabel.Margin = new Thickness(0, 0, 6, 0);
        foreach (var item in _leadingItems)
        {
            _row1.Children.Add(item);
        }

        foreach (var item in _trailingItems)
        {
            _row2.Children.Add(item);
        }

        _root.Children.Add(_row1);
        _root.Children.Add(_row2);
    }

    public void FocusPitchBox()
    {
        _pitchBox.Focus();
        _pitchBox.SelectAll();
    }

    public void WriteSemitones(float value) =>
        _pitchBox.Text = PitchCurve.SnapSemitone(value).ToString("+0;-0;0", CultureInfo.InvariantCulture);

    /// <summary>無段階（ブラシ等）の半音をそのまま表示する。整数に近ければ整数表記。</summary>
    public void WriteSemitonesRaw(float value)
    {
        _pitchBox.Text = Math.Abs(value - MathF.Round(value)) < 1e-3f
            ? PitchCurve.SnapSemitone(value).ToString("+0;-0;0", CultureInfo.InvariantCulture)
            : value.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
    }

    public void SetPointsModeEnabled(bool enabled) => ApplyPointsModeEnabled(enabled);

    public void SetBypassActive(bool active)
    {
        if (active)
        {
            ActionButtonLooks.ApplyAccent(_bypassButton);
        }
        else
        {
            ActionButtonLooks.ApplyClear(_bypassButton);
        }
    }

    /// <summary>テスト用。ブラシ／音階スナップ時の形状ボタン無効化／グレーアウト確認。</summary>
    internal bool IsShapeButtonsEnabled => _shapeButtons.IsEnabled;

    /// <summary>テスト用。</summary>
    internal double ShapeButtonsOpacity => _shapeButtons.Opacity;

    /// <summary>選択中ポイントの出セグメント形状、または未選択時は既定形状を同期する。</summary>
    public void SyncShapeSelection(FadeShape shape, bool rising)
    {
        _shapeIconsRising = !rising; // force refresh below
        SelectShape(shape);
        RefreshShapeButtonIcons(rising, force: true);
    }

    private void RefreshPointModeControls() =>
        ApplyPointsModeEnabled(Tool == PitchCurveBarTool.Points && !SnapToScale);

    /// <summary>
    /// ポイントモード専用 UI（追加・カーブ形状）。ブラシ中または音階スナップ中は無効化し、アイコンをグレーアウトする。
    /// </summary>
    private void ApplyPointsModeEnabled(bool enabled)
    {
        _addPointButton.IsEnabled = Tool == PitchCurveBarTool.Points;
        _shapeButtons.IsEnabled = enabled;
        _shapeButtons.Opacity = enabled ? 1 : 0.4;
    }

    private void RaiseSnapToScaleChanged(bool value)
    {
        RefreshPointModeControls();
        if (!_suppressOptionEvents)
        {
            SnapToScaleChanged?.Invoke(value);
        }
    }

    private void RaiseNoTimeStretchChanged(bool value)
    {
        if (!_suppressOptionEvents)
        {
            NoTimeStretchChanged?.Invoke(value);
        }
    }

    private void RaiseLightweightModeChanged(bool value)
    {
        if (!_suppressOptionEvents)
        {
            LightweightModeChanged?.Invoke(value);
        }
    }

    private void NudgePitch(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        if (!TryParseSemitone(_pitchBox.Text, out var value))
        {
            value = 0;
        }

        var next = PitchCurve.SnapSemitone(value + direction);
        if (Math.Abs(next - value) < 1e-6f)
        {
            return;
        }

        WriteSemitones(next);
        PitchCommitted?.Invoke(next, PitchCurveCommitKind.Nudge);
    }

    private void CommitPitchBox(PitchCurveCommitKind kind)
    {
        if (!TryParseSemitone(_pitchBox.Text, out var value))
        {
            WriteSemitones(0);
            value = 0;
        }
        else
        {
            WriteSemitones(value);
        }

        PitchCommitted?.Invoke(value, kind);
    }

    private static bool TryParseSemitone(string? text, out float value)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            value = 0;
            return false;
        }

        if (!(float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)))
        {
            value = 0;
            return false;
        }

        value = PitchCurve.SnapSemitone(value);
        return true;
    }

    private void SelectShape(FadeShape shape)
    {
        if (shape == FadeShape.Constant)
        {
            shape = FadeShape.Linear;
        }

        _selectedShape = shape;
        foreach (var child in _shapeButtons.Children)
        {
            if (!TryGetShapeChoiceBorder(child, out var border, out var buttonShape))
            {
                continue;
            }

            FadeCurveIcons.ApplyChoiceButtonChrome(border, buttonShape == shape);
        }
    }

    private void RefreshShapeButtonIcons(bool rising, bool force)
    {
        if (!force && rising == _shapeIconsRising)
        {
            return;
        }

        _shapeIconsRising = rising;
        foreach (var child in _shapeButtons.Children)
        {
            if (!TryGetShapeChoiceBorder(child, out var border, out var buttonShape))
            {
                continue;
            }

            if (border.Child is not Image image)
            {
                continue;
            }

            image.Source = FadeCurveIcons.Create(buttonShape, isFadeIn: rising, FadeCurveIcons.ChoiceIconPixelSize);
        }
    }

    private static bool TryGetShapeChoiceBorder(object child, out Border border, out FadeShape shape)
    {
        border = null!;
        shape = default;
        if (child is Border direct && direct.Tag is FadeShape directShape)
        {
            border = direct;
            shape = directShape;
            return true;
        }

        if (child is Panel panel)
        {
            foreach (var nested in panel.Children)
            {
                if (nested is Border nestedBorder && nestedBorder.Tag is FadeShape nestedShape)
                {
                    border = nestedBorder;
                    shape = nestedShape;
                    return true;
                }
            }
        }

        return false;
    }

    private FrameworkElement CreateShapeButton(FadeShape shape, int number)
    {
        var tip = PickerChrome.Numbered(number, UiStrings.LabelFadeCurve((int)shape));
        var border = FadeCurveIcons.CreateChoiceButton(shape, isFadeIn: true, toolTip: tip);
        var outerMargin = border.Margin;
        border.Margin = new Thickness(0);
        FadeCurveIcons.WireChoiceButtonHover(
            border,
            isSelected: () => border.Tag is FadeShape s && s == _selectedShape,
            isEnabled: () => _shapeButtons.IsEnabled);
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (!_shapeButtons.IsEnabled)
            {
                return;
            }

            SelectShape(shape);
            ShapeSelected?.Invoke(shape);
            e.Handled = true;
        };

        var badge = new TextBlock
        {
            Text = number.ToString(CultureInfo.InvariantCulture),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
            Margin = new Thickness(3, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        badge.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");

        var overlay = new Grid
        {
            Width = FadeCurveIcons.ChoiceButtonSide,
            Height = FadeCurveIcons.ChoiceButtonSide,
            Margin = outerMargin,
            Tag = shape,
            ToolTip = tip,
        };
        overlay.Children.Add(border);
        overlay.Children.Add(badge);
        return overlay;
    }

    private static TextBlock CreateLabel(string text, double width = double.NaN, Thickness? margin = null)
    {
        var block = new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        };
        if (!double.IsNaN(width))
        {
            block.Width = width;
        }

        if (margin is { } m)
        {
            block.Margin = m;
        }

        block.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        return block;
    }

    private static Label CreateAccessLabel(string text, UIElement target, double width = double.NaN, Thickness? margin = null)
    {
        var label = new Label
        {
            Content = text,
            Target = target,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (!double.IsNaN(width))
        {
            label.Width = width;
        }

        if (margin is { } m)
        {
            label.Margin = m;
        }

        label.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        var access = new Style(typeof(AccessText));
        access.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        label.Resources.Add(typeof(AccessText), access);
        return label;
    }

    private static RoundedButton CreateChromeButton(string text)
    {
        var button = new RoundedButton
        {
            Content = text,
            MinWidth = DesignMetrics.From96(88),
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ActionButtonLooks.ApplyClear(button);
        return button;
    }

    private static RadioButton CreateRadio(string content, bool isChecked, string group)
    {
        var radio = new RadioButton
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 12, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            GroupName = group,
        };
        radio.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        var access = new Style(typeof(AccessText));
        access.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        radio.Resources.Add(typeof(AccessText), access);
        return radio;
    }
}
