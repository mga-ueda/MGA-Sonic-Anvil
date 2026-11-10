using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal enum VolumeCurveBarTool
{
    Points,
    Brush,
}

internal enum VolumeCurveGainCommitKind
{
    /// <summary>↑↓ での ±dB。常に平坦化する。</summary>
    Nudge,

    /// <summary>フォーカス離脱。平坦カーブのときだけ反映。</summary>
    Blur,

    /// <summary>Enter。平坦カーブのときだけ数値を反映する（モード確定はしない）。</summary>
    Enter,
}

/// <summary>
/// V キーで音量カーブ編集モードに入ったときに、波形描画エリア直下へ出す操作バー。
/// 波形は持たない。カーブ編集・プレビュー波形は <see cref="VolumeCurveEditLayer"/> がメイン波形の上に重ねて描く。
/// 旧 <c>VolumeCurveEditorDialog</c> の UI 生成コードから移植。
/// </summary>
internal sealed class VolumeCurveBar : UserControl
{
    private readonly TextBox _gainBox;
    private readonly RadioButton _pointsRadio;
    private readonly RadioButton _brushRadio;
    private readonly Panel _shapeButtons;
    private readonly RoundedButton _addPointButton;
    private readonly RoundedButton _resetButton;
    private readonly RoundedButton _okButton;
    private readonly RoundedButton _cancelButton;
    private FadeShape _selectedShape = FadeShape.Linear;
    private bool _shapeIconsRising = true;
    private bool _suppressToolEvent;

    public event Action<VolumeCurveBarTool>? ToolChanged;
    public event Action<float, VolumeCurveGainCommitKind>? GainCommitted;
    public event Action<FadeShape>? ShapeSelected;
    public event Action? AddPoint;
    public event Action? Reset;
    public event Action? Accepted;
    public event Action? Cancelled;

    public VolumeCurveBarTool Tool
    {
        get => _brushRadio.IsChecked == true ? VolumeCurveBarTool.Brush : VolumeCurveBarTool.Points;
        set
        {
            _suppressToolEvent = true;
            try
            {
                if (value == VolumeCurveBarTool.Brush)
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

    public VolumeCurveBar()
    {
        Height = DesignMetrics.VolumeCurveBarHeight;
        Focusable = false;
        Background = Brushes.Transparent;
        Visibility = Visibility.Collapsed;

        _pointsRadio = CreateRadio(UiStrings.LabelPitchCurvePoints, isChecked: true, "volumeBarTool");
        _brushRadio = CreateRadio(UiStrings.LabelPitchCurveBrush, isChecked: false, "volumeBarTool");
        _pointsRadio.Checked += (_, _) =>
        {
            RefreshPointModeControls();
            if (!_suppressToolEvent)
            {
                ToolChanged?.Invoke(VolumeCurveBarTool.Points);
            }
        };
        _brushRadio.Checked += (_, _) =>
        {
            RefreshPointModeControls();
            if (!_suppressToolEvent)
            {
                ToolChanged?.Invoke(VolumeCurveBarTool.Brush);
            }
        };

        _gainBox = new TextBox
        {
            Width = DesignMetrics.From96(64),
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            Text = "0.0",
            TextAlignment = TextAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            FocusVisualStyle = null,
        };
        if (TryFindResource("DarkTextBoxStyle") is Style gainStyle)
        {
            _gainBox.Style = gainStyle;
        }

        _gainBox.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            NudgeGain(Math.Sign(e.Delta));
        };
        _gainBox.LostKeyboardFocus += (_, _) => CommitGainBox(VolumeCurveGainCommitKind.Blur);
        _gainBox.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down
                && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                var step = Keyboard.Modifiers == ModifierKeys.Shift ? 6 : 1;
                NudgeGain(key == Key.Up ? step : -step);
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                CommitGainBox(VolumeCurveGainCommitKind.Enter);
            }
        };

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

        _pointsRadio.Margin = new Thickness(12, 0, 0, 0);
        _brushRadio.Margin = new Thickness(8, 0, 0, 0);

        _resetButton = CreateChromeButton(UiStrings.LabelPitchCurveReset);
        _resetButton.Click += (_, _) => Reset?.Invoke();

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
            Content = UiStrings.ButtonCancel,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
        };
        ActionButtonLooks.ApplyClear(_cancelButton);
        _cancelButton.Click += (_, _) => Cancelled?.Invoke();

        var optionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 2, 10, 2),
        };
        var gainAccessLabel = CreateAccessLabel(UiStrings.LabelVolumeCurveGainValue, _gainBox);
        gainAccessLabel.Margin = new Thickness(0, 0, 4, 0);
        optionsRow.Children.Add(gainAccessLabel);
        optionsRow.Children.Add(_gainBox);
        optionsRow.Children.Add(CreateLabel(UiStrings.LabelDb, margin: new Thickness(6, 0, 0, 0)));
        optionsRow.Children.Add(_pointsRadio);
        optionsRow.Children.Add(_brushRadio);
        optionsRow.Children.Add(CreateLabel(
            UiStrings.LabelPitchCurveShape,
            margin: new Thickness(16, 0, 6, 0)));
        optionsRow.Children.Add(_shapeButtons);

        _addPointButton.Margin = new Thickness(0, 0, 8, 0);
        _resetButton.Margin = new Thickness(0, 0, 8, 0);
        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 2, 10, 4),
        };
        actionRow.Children.Add(_addPointButton);
        actionRow.Children.Add(_resetButton);
        actionRow.Children.Add(_okButton);
        actionRow.Children.Add(_cancelButton);

        var root = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        root.Children.Add(optionsRow);
        root.Children.Add(actionRow);
        Content = root;

        SelectShape(_selectedShape);
        RefreshPointModeControls();
    }

    public void FocusGainBox()
    {
        _gainBox.Focus();
        _gainBox.SelectAll();
    }

    public void WriteGainDb(float value)
    {
        var snapped = VolumeCurve.SnapGainStep(value);
        _gainBox.Text = float.IsNegativeInfinity(snapped)
            ? "-∞"
            : snapped.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);
    }

    public void SetPointsModeEnabled(bool enabled)
    {
        ApplyPointsModeEnabled(enabled);
    }

    /// <summary>テスト用。ブラシ時の形状ボタン無効化／グレーアウト確認。</summary>
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
        ApplyPointsModeEnabled(Tool == VolumeCurveBarTool.Points);

    /// <summary>
    /// ポイントモード専用 UI（追加・カーブ形状）。ブラシ時は無効化し、アイコンをグレーアウトする。
    /// DrawingImage は IsEnabled だけでは色が落ちないので Opacity も下げる。
    /// </summary>
    private void ApplyPointsModeEnabled(bool enabled)
    {
        _addPointButton.IsEnabled = enabled;
        _shapeButtons.IsEnabled = enabled;
        _shapeButtons.Opacity = enabled ? 1 : 0.4;
    }

    private void NudgeGain(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        if (!TryParseGainDb(_gainBox.Text, out var value))
        {
            value = 0;
        }

        var next = VolumeCurve.SnapGainStep(value + direction);
        if (Math.Abs(next - value) < 1e-6f)
        {
            return;
        }

        WriteGainDb(next);
        GainCommitted?.Invoke(next, VolumeCurveGainCommitKind.Nudge);
    }

    private void CommitGainBox(VolumeCurveGainCommitKind kind)
    {
        if (!TryParseGainDb(_gainBox.Text, out var value))
        {
            WriteGainDb(0);
            value = 0;
        }
        else
        {
            WriteGainDb(value);
        }

        GainCommitted?.Invoke(value, kind);
    }

    private static bool TryParseGainDb(string? text, out float value)
    {
        text = text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            value = 0;
            return false;
        }

        if (text is "-∞" or "-inf" or "-Inf" or "-INF" or "-infinity" or "-Infinity")
        {
            value = VolumeCurve.MinGainDb;
            return true;
        }

        if (!(float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            || float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)))
        {
            value = 0;
            return false;
        }

        value = VolumeCurve.SnapGainStep(value);
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
