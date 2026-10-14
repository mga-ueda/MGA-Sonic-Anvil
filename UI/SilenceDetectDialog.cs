using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum SilenceDetectAction
{
    DeleteSilence = 0,
    CreateRegions = 1,
    CreateMarkers = 2,
}

internal readonly record struct SilenceDetectDialogResult(
    bool Accepted,
    SilenceDetectOptions Options,
    SilenceDetectAction Action);

/// <summary>
/// Cubase 風の無音検出。オープン／クローズしきい値とゲート時間を決め、削除／リージョン／マーカーを実行する。
/// </summary>
internal sealed class SilenceDetectDialog : Window
{
    private readonly AudioDocument _document;
    private readonly WaveSelection _range;
    private readonly int _channelMask;
    private readonly bool _allowsRegions;
    private readonly SilenceDetectWavePreview _preview;
    private readonly TextBox _openBox;
    private readonly TextBox _closeBox;
    private readonly CheckBox _linkBox;
    private readonly TextBox _minGateBox;
    private readonly TextBox _minCloseBox;
    private readonly TextBox _preRollBox;
    private readonly TextBox _postRollBox;
    private readonly TextBox _fadeInBox;
    private readonly TextBox _fadeOutBox;
    private readonly StackPanel _fadeInRow;
    private readonly StackPanel _fadeOutRow;
    private readonly StackPanel _minGateRow;
    private readonly TextBlock _statusText;
    private readonly RadioButton _edgesRadio;
    private readonly RadioButton _entireRadio;
    private readonly RadioButton _deleteRadio;
    private readonly RadioButton _regionsRadio;
    private readonly RadioButton _markersRadio;
    private readonly RoundedButton _applyButton;
    private SilenceDetectOptions _options;
    private bool _updatingText;

    public SilenceDetectDialogResult Result { get; private set; }

    public static SilenceDetectDialogResult Show(
        Window owner,
        AudioDocument document,
        WaveSelection range,
        int channelMask,
        SilenceDetectOptions options,
        SilenceDetectAction action,
        bool allowsRegions,
        bool selectionScoped)
    {
        var dialog = new SilenceDetectDialog(
            document,
            range,
            channelMask,
            options,
            action,
            allowsRegions,
            selectionScoped)
        {
            Owner = owner,
        };
        WindowPaintReveal.ShowDialogWhenPainted(dialog);
        return dialog.Result;
    }

    private SilenceDetectDialog(
        AudioDocument document,
        WaveSelection range,
        int channelMask,
        SilenceDetectOptions options,
        SilenceDetectAction action,
        bool allowsRegions,
        bool selectionScoped)
    {
        _document = document;
        _range = range.Clamp(document.FrameCount);
        _channelMask = channelMask;
        _allowsRegions = allowsRegions;
        _options = options.Normalized();
        if (action == SilenceDetectAction.CreateRegions && !allowsRegions)
        {
            action = SilenceDetectAction.DeleteSilence;
        }

        Title = UiStrings.DialogSilenceDetectTitle;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = DesignMetrics.From96(720);
        MinWidth = DesignMetrics.From96(560);
        SizeToContent = SizeToContent.Height;
        Background = (Brush)FindResource("WindowBackBrush");
        Foreground = (Brush)FindResource("PrimaryForeBrush");

        _preview = new SilenceDetectWavePreview
        {
            Document = document,
            Range = _range,
            ChannelMask = channelMask,
            Options = _options,
            Height = DesignMetrics.From96(160),
            Margin = new Thickness(0, 0, 0, 10),
        };
        _preview.ThresholdChanged += OnPreviewThresholdChanged;
        _preview.KeepsChanged += OnPreviewKeepsChanged;

        _openBox = CreateDbBox(_options.OpenThresholdDb);
        _closeBox = CreateDbBox(_options.CloseThresholdDb);
        _linkBox = new CheckBox
        {
            Content = UiStrings.LabelSilenceDetectLink,
            IsChecked = _options.LinkThresholds,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 4),
        };
        if (TryFindResource("DarkCheckBoxStyle") is Style checkStyle)
        {
            _linkBox.Style = checkStyle;
        }

        _minGateBox = CreateMsBox(_options.MinGateMs);
        _minCloseBox = CreateMsBox(_options.MinCloseMs);
        _preRollBox = CreateMsBox(_options.PreRollMs);
        _postRollBox = CreateMsBox(_options.PostRollMs);
        _fadeInBox = CreateMsBox(_options.FadeInMs);
        _fadeOutBox = CreateMsBox(_options.FadeOutMs);

        WireDbBox(_openBox, open: true);
        WireDbBox(_closeBox, open: false);
        WireMsBox(_minGateBox);
        WireMsBox(_minCloseBox);
        WireMsBox(_preRollBox);
        WireMsBox(_postRollBox);
        WireMsBox(_fadeInBox);
        WireMsBox(_fadeOutBox);
        _linkBox.Checked += (_, _) => OnLinkChanged(linked: true);
        _linkBox.Unchecked += (_, _) => OnLinkChanged(linked: false);

        _statusText = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        };

        _deleteRadio = CreateRadio(
            UiStrings.LabelSilenceDetectDelete,
            action == SilenceDetectAction.DeleteSilence,
            "SilenceDetectAction");
        _regionsRadio = CreateRadio(
            UiStrings.LabelSilenceDetectCreateRegions,
            action == SilenceDetectAction.CreateRegions,
            "SilenceDetectAction");
        _markersRadio = CreateRadio(
            UiStrings.LabelSilenceDetectCreateMarkers,
            action == SilenceDetectAction.CreateMarkers,
            "SilenceDetectAction");
        _regionsRadio.IsEnabled = allowsRegions;
        if (!allowsRegions)
        {
            _regionsRadio.Opacity = 0.45;
        }

        _edgesRadio = CreateRadio(
            UiStrings.LabelSilenceDetectEdgesOnly,
            _options.EdgesOnly,
            "SilenceDetectScope");
        _entireRadio = CreateRadio(
            UiStrings.LabelSilenceDetectEntire,
            !_options.EdgesOnly,
            "SilenceDetectScope");
        _deleteRadio.Checked += (_, _) => SyncActionUi();
        _regionsRadio.Checked += (_, _) => SyncActionUi();
        _markersRadio.Checked += (_, _) => SyncActionUi();
        _edgesRadio.Checked += (_, _) =>
        {
            OnOptionsChanged();
            SyncDependentEnabled();
        };
        _entireRadio.Checked += (_, _) =>
        {
            OnOptionsChanged();
            SyncDependentEnabled();
        };

        var targetText = new TextBlock
        {
            Text = selectionScoped
                ? UiStrings.LabelSilenceDetectTargetSelection
                : UiStrings.LabelSilenceDetectTargetEntire,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.85,
        };

        _fadeInRow = LabeledMsRow(UiStrings.LabelSilenceDetectFadeIn, _fadeInBox);
        _fadeOutRow = LabeledMsRow(UiStrings.LabelSilenceDetectFadeOut, _fadeOutBox);
        _minGateRow = LabeledMsRow(UiStrings.LabelSilenceDetectMinGate, _minGateBox);

        var leftCol = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        leftCol.Children.Add(_statusText);
        leftCol.Children.Add(LabeledDbRow(UiStrings.LabelSilenceDetectOpenThreshold, _openBox));
        leftCol.Children.Add(_linkBox);
        leftCol.Children.Add(LabeledDbRow(UiStrings.LabelSilenceDetectCloseThreshold, _closeBox));
        leftCol.Children.Add(_fadeInRow);
        leftCol.Children.Add(_fadeOutRow);

        var rightCol = new StackPanel();
        rightCol.Children.Add(_minGateRow);
        rightCol.Children.Add(LabeledMsRow(UiStrings.LabelSilenceDetectMinClose, _minCloseBox));
        rightCol.Children.Add(LabeledMsRow(UiStrings.LabelSilenceDetectPreRoll, _preRollBox));
        rightCol.Children.Add(LabeledMsRow(UiStrings.LabelSilenceDetectPostRoll, _postRollBox));

        var paramGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(rightCol, 1);
        paramGrid.Children.Add(leftCol);
        paramGrid.Children.Add(rightCol);

        var actionRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 6),
        };
        actionRow.Children.Add(_deleteRadio);
        actionRow.Children.Add(_regionsRadio);
        actionRow.Children.Add(_markersRadio);

        var scopeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        scopeRow.Children.Add(_edgesRadio);
        scopeRow.Children.Add(_entireRadio);

        _applyButton = CreateButton(UiStrings.ButtonSilenceDetectDelete, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancel, accept: false, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        buttons.Children.Add(_applyButton);
        buttons.Children.Add(cancel);

        var bottom = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        bottom.Children.Add(targetText);
        bottom.Children.Add(paramGrid);
        bottom.Children.Add(actionRow);
        bottom.Children.Add(scopeRow);
        bottom.Children.Add(buttons);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_preview);

        Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };
        WindowPaintReveal.Attach(this);
        AppDialogKeys.Attach(this, () => Close());
        TipService.Set(this, UiStrings.TipSilenceDetectDialog);
        TipService.Set(_preview, UiStrings.TipSilenceDetectDialog);
        SyncDependentEnabled();
        Loaded += (_, _) =>
        {
            RefreshStatusFromPreview();
            SyncActionUi();
            // 高さは内容に合わせたあと固定。横幅だけ変えられる。
            var h = ActualHeight;
            if (h > 0)
            {
                MinHeight = h;
                MaxHeight = h;
            }

            _openBox.Focus();
            _openBox.SelectAll();
        };
    }

    private void OnPreviewThresholdChanged(double db, bool openLine)
    {
        db = SilentSkip.ClampThresholdDb(db);
        if (_options.LinkThresholds || openLine)
        {
            _options = _options with
            {
                OpenThresholdDb = db,
                CloseThresholdDb = _options.LinkThresholds ? db : _options.CloseThresholdDb,
            };
        }
        else
        {
            _options = _options with { CloseThresholdDb = Math.Min(db, _options.OpenThresholdDb) };
        }

        _options = _options.Normalized();
        // プレビューは表示波形ベースの近似。厳密検出は適用時のみ。
        PaintThresholdBoxesFromOptions();
        _preview.Options = _options;
    }

    private void OnPreviewKeepsChanged() => RefreshStatusFromPreview();

    private void OnLinkChanged(bool linked)
    {
        _options = _options with { LinkThresholds = linked };
        if (linked)
        {
            _options = _options with { CloseThresholdDb = _options.OpenThresholdDb };
        }

        _options = _options.Normalized();
        PaintBoxesFromOptions();
        SyncDependentEnabled();
        _preview.Options = _options;
    }

    private void OnOptionsChanged()
    {
        CommitAllFields();
        _options = CurrentOptions();
        _preview.Options = _options;
    }

    private void SyncActionUi()
    {
        SyncApplyButton();
        SyncDependentEnabled();
    }

    private void SyncApplyButton()
    {
        _applyButton.Content = SelectedAction switch
        {
            SilenceDetectAction.CreateRegions => UiStrings.ButtonSilenceDetectCreateRegions,
            SilenceDetectAction.CreateMarkers => UiStrings.ButtonSilenceDetectCreateMarkers,
            _ => UiStrings.ButtonSilenceDetectDelete,
        };
    }

    /// <summary>
    /// ラジオ選択に応じて意味のない項目を無効化。
    /// フェードは無音削除時のみ。次のゲートまでの最小時間は「全体」のみ（冒頭＆末尾は外側境界に影響しない）。
    /// </summary>
    private void SyncDependentEnabled()
    {
        var delete = SelectedAction == SilenceDetectAction.DeleteSilence;
        SetParamEnabled(_fadeInRow, _fadeInBox, delete);
        SetParamEnabled(_fadeOutRow, _fadeOutBox, delete);

        var entire = _entireRadio.IsChecked == true;
        SetParamEnabled(_minGateRow, _minGateBox, entire);

        var linked = _linkBox.IsChecked == true;
        _closeBox.IsEnabled = !linked;
        _closeBox.Opacity = linked ? 0.55 : 1;
    }

    private static void SetParamEnabled(StackPanel row, TextBox box, bool enabled)
    {
        row.IsEnabled = enabled;
        row.Opacity = enabled ? 1 : 0.55;
        box.IsEnabled = enabled;
    }

    private SilenceDetectAction SelectedAction
    {
        get
        {
            if (_allowsRegions && _regionsRadio.IsChecked == true)
            {
                return SilenceDetectAction.CreateRegions;
            }

            if (_markersRadio.IsChecked == true)
            {
                return SilenceDetectAction.CreateMarkers;
            }

            return SilenceDetectAction.DeleteSilence;
        }
    }

    private void RefreshStatusFromPreview() =>
        _statusText.Text = UiStrings.SilenceDetectDetectedSpans(_preview.KeepCount);

    private SilenceDetectOptions CurrentOptions()
    {
        CommitAllFields();
        return _options.Normalized() with { EdgesOnly = _edgesRadio.IsChecked == true };
    }

    private void CommitAllFields()
    {
        if (SilentSkip.TryParseThresholdDb(_openBox.Text, out var open))
        {
            _options = _options with { OpenThresholdDb = open };
        }

        if (_linkBox.IsChecked == true)
        {
            _options = _options with
            {
                LinkThresholds = true,
                CloseThresholdDb = _options.OpenThresholdDb,
            };
        }
        else if (SilentSkip.TryParseThresholdDb(_closeBox.Text, out var close))
        {
            _options = _options with
            {
                LinkThresholds = false,
                CloseThresholdDb = close,
            };
        }

        if (TryParseMs(_minGateBox.Text, out var gate))
        {
            _options = _options with { MinGateMs = gate };
        }

        if (TryParseMs(_minCloseBox.Text, out var minClose))
        {
            _options = _options with { MinCloseMs = minClose };
        }

        if (TryParseMs(_preRollBox.Text, out var pre))
        {
            _options = _options with { PreRollMs = pre };
        }

        if (TryParseMs(_postRollBox.Text, out var post))
        {
            _options = _options with { PostRollMs = post };
        }

        if (TryParseFadeMs(_fadeInBox.Text, out var fadeIn))
        {
            _options = _options with { FadeInMs = fadeIn };
        }

        if (TryParseFadeMs(_fadeOutBox.Text, out var fadeOut))
        {
            _options = _options with { FadeOutMs = fadeOut };
        }

        _options = _options.Normalized();
        PaintBoxesFromOptions();
    }

    private void PaintBoxesFromOptions()
    {
        _updatingText = true;
        PaintThresholdBoxesFromOptionsCore();
        _minGateBox.Text = _options.MinGateMs.ToString(CultureInfo.InvariantCulture);
        _minCloseBox.Text = _options.MinCloseMs.ToString(CultureInfo.InvariantCulture);
        _preRollBox.Text = _options.PreRollMs.ToString(CultureInfo.InvariantCulture);
        _postRollBox.Text = _options.PostRollMs.ToString(CultureInfo.InvariantCulture);
        _fadeInBox.Text = _options.FadeInMs.ToString(CultureInfo.InvariantCulture);
        _fadeOutBox.Text = _options.FadeOutMs.ToString(CultureInfo.InvariantCulture);
        _updatingText = false;
    }

    private void PaintThresholdBoxesFromOptions()
    {
        _updatingText = true;
        PaintThresholdBoxesFromOptionsCore();
        _updatingText = false;
    }

    private void PaintThresholdBoxesFromOptionsCore()
    {
        _openBox.Text = FormatDb(_options.OpenThresholdDb);
        _closeBox.Text = FormatDb(_options.CloseThresholdDb);
    }

    private void Accept()
    {
        var options = CurrentOptions();
        Result = new SilenceDetectDialogResult(true, options, SelectedAction);
        DialogResult = true;
        Close();
    }

    private void WireDbBox(TextBox box, bool open)
    {
        box.LostKeyboardFocus += (_, _) =>
        {
            if (_updatingText)
            {
                return;
            }

            OnOptionsChanged();
        };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down)
            {
                e.Handled = true;
                NudgeDb(open, key == Key.Up ? 1 : -1);
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Accept();
            }
        };
        box.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            NudgeDb(open, Math.Sign(e.Delta));
        };
    }

    private void WireMsBox(TextBox box)
    {
        box.LostKeyboardFocus += (_, _) =>
        {
            if (!_updatingText)
            {
                OnOptionsChanged();
            }
        };
        box.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                Accept();
            }
        };
    }

    private void NudgeDb(bool open, int direction)
    {
        if (direction == 0)
        {
            return;
        }

        if (open || _options.LinkThresholds)
        {
            var next = NudgeThresholdDb(_options.OpenThresholdDb, direction);
            _options = _options with
            {
                OpenThresholdDb = next,
                CloseThresholdDb = _options.LinkThresholds ? next : _options.CloseThresholdDb,
            };
        }
        else
        {
            _options = _options with
            {
                CloseThresholdDb = NudgeThresholdDb(_options.CloseThresholdDb, direction),
            };
        }

        _options = _options.Normalized();
        PaintThresholdBoxesFromOptions();
        _preview.Options = _options;
    }

    /// <summary>ホイール／↑↓: 0.1 dB。Shift で 1 dB。</summary>
    internal static double ThresholdNudgeStep(ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Shift) != 0 ? 1.0 : 0.1;

    internal static double NudgeThresholdDb(double currentDb, int direction, ModifierKeys modifiers)
    {
        var step = ThresholdNudgeStep(modifiers);
        var next = currentDb + direction * step;
        next = Math.Round(next / step, MidpointRounding.AwayFromZero) * step;
        return SilentSkip.ClampThresholdDb(next);
    }

    private static double NudgeThresholdDb(double currentDb, int direction) =>
        NudgeThresholdDb(currentDb, direction, Keyboard.Modifiers);

    private TextBox CreateDbBox(double db)
    {
        var box = CreateSettingsStyleNumberBox(FormatDb(db));
        TipService.Set(box, UiStrings.TipSilenceDetectDialog);
        return box;
    }

    private TextBox CreateMsBox(int ms)
    {
        var box = CreateSettingsStyleNumberBox(ms.ToString(CultureInfo.InvariantCulture));
        TipService.Set(box, UiStrings.TipSilenceDetectDialog);
        return box;
    }

    private TextBox CreateSettingsStyleNumberBox(string text)
    {
        var box = new TextBox
        {
            Width = DesignMetrics.SettingsLoudnessBoxWidth,
            Height = DesignMetrics.AudioInputHeight,
            Padding = new Thickness(6, 1, 6, 1),
            FontSize = 11.333,
            VerticalContentAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Text = text,
        };
        InputMethod.SetIsInputMethodEnabled(box, false);
        if (TryFindResource("DarkTextBoxStyle") is Style style)
        {
            box.Style = style;
        }

        return box;
    }

    private static StackPanel LabeledDbRow(string label, TextBox box) =>
        LabeledValueRow(label, box, UiStrings.LabelDb);

    private static StackPanel LabeledMsRow(string label, TextBox box) =>
        LabeledValueRow(label, box, UiStrings.LabelMs);

    private static StackPanel LabeledValueRow(string label, TextBox box, string unit)
    {
        // dB / ms 行でラベル幅を揃え、入力欄の左端を縦に一致させる。
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Width = DesignMetrics.From96(180),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        row.Children.Add(box);
        row.Children.Add(new TextBlock
        {
            Text = unit,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        return row;
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

        button.Click += (_, _) =>
        {
            if (accept)
            {
                Accept();
            }
            else
            {
                Close();
            }
        };
        return button;
    }

    private static RadioButton CreateRadio(string content, bool isChecked, string group) =>
        new()
        {
            Content = content,
            IsChecked = isChecked,
            Margin = new Thickness(0, 0, 12, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            GroupName = group,
        };

    private static string FormatDb(double db) =>
        SilentSkip.ClampThresholdDb(db).ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryParseMs(string? text, out int ms)
    {
        text = text?.Trim() ?? string.Empty;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            ms = 0;
            return false;
        }

        ms = SilenceDetectOptions.ClampTimingMs(parsed);
        return true;
    }

    private static bool TryParseFadeMs(string? text, out int ms)
    {
        text = text?.Trim() ?? string.Empty;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && !int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
        {
            ms = 0;
            return false;
        }

        ms = SilenceDetectOptions.ClampFadeMs(parsed);
        return true;
    }
}

/// <summary>
/// 選択範囲の波形と ±しきい値線。ハイライトは表示用エンベロープの近似（適用時は厳密検出）。
/// </summary>
internal sealed class SilenceDetectWavePreview : FrameworkElement
{
    private const int MaxBins = 1200;

    private float[] _mins = [];
    private float[] _maxs = [];
    private List<WaveSelection> _keeps = [];
    private SilenceDetectOptions _options = SilenceDetectOptions.FromThreshold(SilentSkip.DefaultThresholdDb);
    private bool _dragging;
    private bool _dragOpen = true;
    private AudioDocument? _document;
    private WaveSelection _range;
    private int _channelMask;

    public event Action<double, bool>? ThresholdChanged;
    public event Action? KeepsChanged;

    public int KeepCount => _keeps.Count;

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            RebuildEnvelope();
            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    public WaveSelection Range
    {
        get => _range;
        set
        {
            _range = value;
            RebuildEnvelope();
            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    public int ChannelMask
    {
        get => _channelMask;
        set
        {
            _channelMask = value;
            RebuildEnvelope();
            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    public SilenceDetectOptions Options
    {
        get => _options;
        set
        {
            _options = value.Normalized();
            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    public SilenceDetectWavePreview()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.SizeNS;
        // Preview で確実に拾う（親やフォーカスに奪われない）。
        PreviewMouseWheel += OnPreviewMouseWheel;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        RebuildEnvelope();
        RefreshKeepsFromEnvelope();
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        _dragging = true;
        var y = e.GetPosition(this).Y;
        _dragOpen = PickOpenLine(y);
        ApplyMouseY(y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        ApplyMouseY(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var direction = Math.Sign(e.Delta);
        if (direction == 0)
        {
            return;
        }

        var openLine = _options.LinkThresholds || PickOpenLine(e.GetPosition(this).Y);
        var current = openLine ? _options.OpenThresholdDb : _options.CloseThresholdDb;
        var next = SilenceDetectDialog.NudgeThresholdDb(current, direction, Keyboard.Modifiers);
        ThresholdChanged?.Invoke(next, openLine);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1)
        {
            return;
        }

        var back = WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush"));
        var fill = WpfControlHelpers.FrozenBrush(Theme.Get("WaveFillBrush"));
        var zero = WpfControlHelpers.FrozenBrush(Theme.Get("WaveZeroLineBrush"));
        var keep = WpfControlHelpers.FrozenBrush(Theme.Get("WaveSelectionFillBrush"));
        var thresholdBrush = WpfControlHelpers.FrozenBrush(Theme.Get("LevelMeterClipOnBrush"));
        dc.DrawRectangle(back, null, new Rect(0, 0, width, height));

        var mid = height * 0.5;
        var amp = height * 0.5;
        if (_mins.Length == 0 || _maxs.Length != _mins.Length)
        {
            RebuildEnvelope();
        }

        var bins = Math.Min(_mins.Length, _maxs.Length);
        if (_document is not null && !_range.IsEmpty && bins > 0)
        {
            var rangeLen = Math.Max(1.0, _range.Length);
            foreach (var span in _keeps)
            {
                var x0 = (span.StartFrame - _range.StartFrame) / rangeLen * width;
                var x1 = (span.EndFrame - _range.StartFrame) / rangeLen * width;
                if (x1 <= x0)
                {
                    continue;
                }

                dc.DrawRectangle(keep, null, new Rect(x0, 0, x1 - x0, height));
            }

            var pen = new Pen(fill, 1);
            pen.Freeze();
            for (var i = 0; i < bins; i++)
            {
                var x = (i + 0.5) / bins * width;
                var y0 = mid - _maxs[i] * amp;
                var y1 = mid - _mins[i] * amp;
                if (y1 < y0)
                {
                    (y0, y1) = (y1, y0);
                }

                if (y1 - y0 < 1)
                {
                    y1 = y0 + 1;
                }

                dc.DrawLine(pen, new Point(x, y0), new Point(x, y1));
            }
        }

        var zeroPen = new Pen(zero, 1);
        zeroPen.Freeze();
        dc.DrawLine(zeroPen, new Point(0, mid), new Point(width, mid));

        DrawThresholdPair(dc, thresholdBrush, mid, amp, width, SilentSkip.LinearFromDb(_options.OpenThresholdDb), thick: true);
        if (!_options.LinkThresholds
            && Math.Abs(_options.OpenThresholdDb - _options.CloseThresholdDb) > 1e-3)
        {
            DrawThresholdPair(dc, thresholdBrush, mid, amp, width, SilentSkip.LinearFromDb(_options.CloseThresholdDb), thick: false);
        }
    }

    private static void DrawThresholdPair(
        DrawingContext dc,
        Brush brush,
        double mid,
        double amp,
        double width,
        float linear,
        bool thick)
    {
        var yPos = mid - linear * amp;
        var yNeg = mid + linear * amp;
        var pen = new Pen(brush, thick ? 1.5 : 1);
        if (!thick)
        {
            pen.DashStyle = DashStyles.Dash;
        }

        pen.Freeze();
        dc.DrawLine(pen, new Point(0, yPos), new Point(width, yPos));
        dc.DrawLine(pen, new Point(0, yNeg), new Point(width, yNeg));
        if (!thick)
        {
            return;
        }

        var handle = 6.0;
        dc.DrawRectangle(brush, null, new Rect(0, yPos - handle * 0.5, handle, handle));
        dc.DrawRectangle(brush, null, new Rect(width - handle, yPos - handle * 0.5, handle, handle));
        dc.DrawRectangle(brush, null, new Rect(0, yNeg - handle * 0.5, handle, handle));
        dc.DrawRectangle(brush, null, new Rect(width - handle, yNeg - handle * 0.5, handle, handle));
    }

    private bool PickOpenLine(double y)
    {
        if (_options.LinkThresholds)
        {
            return true;
        }

        var mid = ActualHeight * 0.5;
        var amp = ActualHeight * 0.5;
        var openY = mid - SilentSkip.LinearFromDb(_options.OpenThresholdDb) * amp;
        var closeY = mid - SilentSkip.LinearFromDb(_options.CloseThresholdDb) * amp;
        var dOpen = Math.Min(Math.Abs(y - openY), Math.Abs(y - (mid * 2 - openY)));
        var dClose = Math.Min(Math.Abs(y - closeY), Math.Abs(y - (mid * 2 - closeY)));
        return dOpen <= dClose;
    }

    private void ApplyMouseY(double y)
    {
        var height = ActualHeight;
        if (height <= 1)
        {
            return;
        }

        var mid = height * 0.5;
        var amp = height * 0.5;
        var linear = Math.Clamp(Math.Abs(y - mid) / amp, 1e-6, 1.0);
        var db = 20.0 * Math.Log10(linear);
        ThresholdChanged?.Invoke(SilentSkip.ClampThresholdDb(db), _dragOpen);
    }

    private void RebuildEnvelope()
    {
        if (_document is null || _range.IsEmpty || ActualWidth <= 1)
        {
            _mins = [];
            _maxs = [];
            return;
        }

        var bins = Math.Clamp((int)Math.Round(ActualWidth), 1, MaxBins);
        _mins = new float[bins];
        _maxs = new float[bins];
        Array.Fill(_mins, 0f);
        Array.Fill(_maxs, 0f);

        var interleaved = _document.Interleaved;
        var channels = Math.Max(1, _document.Channels);
        var start = _range.StartFrame;
        var end = _range.EndFrame;
        var length = Math.Max(1L, end - start);
        var frameCount = interleaved.Length / channels;
        for (var i = 0; i < bins; i++)
        {
            var frame0 = start + i * length / bins;
            var frame1 = start + (i + 1) * length / bins;
            if (frame1 <= frame0)
            {
                frame1 = frame0 + 1;
            }

            float min = 0;
            float max = 0;
            var first = true;
            for (var frame = frame0; frame < frame1 && frame < end && frame < frameCount; frame++)
            {
                var baseIndex = (int)(frame * channels);
                if (baseIndex < 0 || baseIndex + channels > interleaved.Length)
                {
                    break;
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    if (!ChannelSolo.Contains(_channelMask, ch))
                    {
                        continue;
                    }

                    var sample = interleaved[baseIndex + ch];
                    if (first)
                    {
                        min = max = sample;
                        first = false;
                    }
                    else
                    {
                        if (sample < min)
                        {
                            min = sample;
                        }

                        if (sample > max)
                        {
                            max = sample;
                        }
                    }
                }
            }

            _mins[i] = min;
            _maxs[i] = max;
        }
    }

    /// <summary>
    /// 表示ビンのピークとオープンスレッショルドで可聴区間を近似する（ゲート時間は見ない）。
    /// </summary>
    private void RefreshKeepsFromEnvelope()
    {
        if (_document is null || _range.IsEmpty || _mins.Length == 0 || _maxs.Length != _mins.Length)
        {
            _keeps = [];
            KeepsChanged?.Invoke();
            return;
        }

        var bins = _mins.Length;
        var thr = SilentSkip.LinearFromDb(_options.OpenThresholdDb);
        if (!_options.LinkThresholds)
        {
            // クローズが低いほどゲートが開きやすいので、プレビューは低い方で残す見込みを見せる。
            thr = Math.Min(thr, SilentSkip.LinearFromDb(_options.CloseThresholdDb));
        }

        var start = _range.StartFrame;
        var length = Math.Max(1L, _range.Length);
        var keeps = new List<WaveSelection>();
        long? runStart = null;
        for (var i = 0; i < bins; i++)
        {
            var peak = Math.Max(Math.Abs(_mins[i]), Math.Abs(_maxs[i]));
            var audible = peak >= thr;
            if (audible)
            {
                runStart ??= start + i * length / bins;
                continue;
            }

            if (runStart is long run)
            {
                var runEnd = start + i * length / bins;
                if (runEnd > run)
                {
                    keeps.Add(new WaveSelection(run, runEnd));
                }

                runStart = null;
            }
        }

        if (runStart is long lastStart)
        {
            keeps.Add(new WaveSelection(lastStart, _range.EndFrame));
        }

        if (_options.EdgesOnly && keeps.Count > 0)
        {
            keeps = [new WaveSelection(keeps[0].StartFrame, keeps[^1].EndFrame)];
        }

        _keeps = keeps;
        KeepsChanged?.Invoke();
    }
}
