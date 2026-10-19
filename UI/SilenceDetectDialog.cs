using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal enum SilenceDetectAction
{
    DeleteSilence = 0,
    CreateRegions = 1,
    CreateMarkers = 2,
    CreateHitPointMarkers = 3,
}

internal readonly record struct SilenceDetectDialogResult(
    bool Accepted,
    SilenceDetectOptions Options,
    SilenceDetectAction Action);

/// <summary>
/// 無音・ヒット検出。オープン／クローズしきい値とゲート時間を決め、削除／リージョン／マーカー／ヒットポイントを実行する。
/// </summary>
internal sealed class SilenceDetectDialog : Window
{
    private readonly AudioDocument _document;
    private readonly WaveSelection _range;
    private readonly int _channelMask;
    private readonly bool _allowsRegions;
    private readonly SilenceDetectWavePreview _preview;
    private readonly DispatcherTimer _openWheelPreviewTimer;
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
    private readonly RadioButton _hitPointsRadio;
    private readonly StackPanel _openRow;
    private readonly StackPanel _closeRow;
    private readonly StackPanel _minCloseRow;
    private readonly StackPanel _preRollRow;
    private readonly StackPanel _postRollRow;
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
        SourceInitialized += (_, _) => DisableMinimizeBox(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = DesignMetrics.From96(1080);
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
            MinHeight = DesignMetrics.From96(80),
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _preview.ThresholdChanged += OnPreviewThresholdChanged;
        _preview.KeepsChanged += OnPreviewKeepsChanged;
        _openWheelPreviewTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _openWheelPreviewTimer.Tick += (_, _) =>
        {
            _openWheelPreviewTimer.Stop();
            _preview.Options = _options;
        };

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
        _hitPointsRadio = CreateRadio(
            UiStrings.LabelSilenceDetectHitPoints,
            action == SilenceDetectAction.CreateHitPointMarkers,
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
        _hitPointsRadio.Checked += (_, _) => SyncActionUi();
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

        _openRow = LabeledDbRow(UiStrings.LabelSilenceDetectOpenThreshold, _openBox);
        _closeRow = LabeledDbRow(UiStrings.LabelSilenceDetectCloseThreshold, _closeBox);
        _minCloseRow = LabeledMsRow(UiStrings.LabelSilenceDetectMinClose, _minCloseBox);
        _preRollRow = LabeledMsRow(UiStrings.LabelSilenceDetectPreRoll, _preRollBox);
        _postRollRow = LabeledMsRow(UiStrings.LabelSilenceDetectPostRoll, _postRollBox);

        var leftCol = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        leftCol.Children.Add(_statusText);
        leftCol.Children.Add(_openRow);
        leftCol.Children.Add(_linkBox);
        leftCol.Children.Add(_closeRow);
        leftCol.Children.Add(_fadeInRow);
        leftCol.Children.Add(_fadeOutRow);

        var rightCol = new StackPanel();
        rightCol.Children.Add(_minGateRow);
        rightCol.Children.Add(_minCloseRow);
        rightCol.Children.Add(_preRollRow);
        rightCol.Children.Add(_postRollRow);

        var paramGrid = new Grid
        {
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        paramGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rightCol.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(rightCol, 1);
        paramGrid.Children.Add(leftCol);
        paramGrid.Children.Add(rightCol);

        var actionRow = new WrapPanel
        {
            Margin = new Thickness(0, 0, 0, 6),
        };
        actionRow.Children.Add(_deleteRadio);
        actionRow.Children.Add(_regionsRadio);
        actionRow.Children.Add(_markersRadio);
        actionRow.Children.Add(_hitPointsRadio);

        var scopeRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10),
        };
        scopeRow.Children.Add(_edgesRadio);
        scopeRow.Children.Add(_entireRadio);

        _applyButton = CreateButton(UiStrings.ButtonOk, accept: true, accent: true, isDefault: true);
        var cancel = CreateButton(UiStrings.ButtonCancel, accept: false, isCancel: true);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
        };
        buttons.Children.Add(_applyButton);
        buttons.Children.Add(cancel);

        var settings = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        settings.Children.Add(targetText);
        settings.Children.Add(paramGrid);
        settings.Children.Add(actionRow);
        settings.Children.Add(scopeRow);
        settings.Children.Add(buttons);

        var bottom = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        bottom.Children.Add(settings);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(_preview);

        Content = new Border { Padding = DesignMetrics.AudioPad, Child = root };
        PreviewMouseLeftButtonDown += OnBlankClickReleaseFieldFocus;
        WindowPaintReveal.Attach(this);
        AppDialogKeys.Attach(this, () => Close());
        Closing += (_, _) =>
        {
            _openWheelPreviewTimer.Stop();
            AppStorage.Settings.RememberSilenceDetectOptions(CurrentOptions());
            AppStorage.Settings.SilenceDetectAction = (int)SelectedAction;
            if (ActualWidth > 1 && ActualHeight > 1)
            {
                WindowPlacement.CaptureSilenceDetect(this, AppStorage.Settings);
            }

            AppStorage.Save();
        };
                        SyncDependentEnabled();
        Loaded += (_, _) =>
        {
            RefreshStatusFromPreview();
            SyncActionUi();
            // 未保存のときは 1080×543。保存済みのサイズがあればそれを使い、ここでは上書きしない。
            SizeToContent = SizeToContent.Manual;
            var contentHeight = ActualHeight;
            if (contentHeight > 0)
            {
                var previewHeight = _preview.ActualHeight;
                MinHeight = Math.Max(
                    _preview.MinHeight,
                    contentHeight - previewHeight + _preview.MinHeight);
            }

            _preview.ClearValue(FrameworkElement.HeightProperty);
            if (!WindowPlacement.TryApplySilenceDetect(this, AppStorage.Settings))
            {
                Width = DesignMetrics.From96(1080);
                Height = DesignMetrics.From96(543);
                UpdateLayout();
                WindowPlacement.CenterOnContainingMonitor(this, Owner);
            }

            _openBox.Focus();
            _openBox.SelectAll();
        };
    }

    private void OnBlankClickReleaseFieldFocus(object sender, MouseButtonEventArgs e)
    {
        if (Keyboard.FocusedElement is not TextBox box)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInsideTextBox(source))
        {
            return;
        }

        box.Focusable = false;
        _preview.Focus();
        Keyboard.Focus(_preview);
        box.Focusable = true;
    }

    private static bool IsInsideTextBox(DependencyObject source)
    {
        for (DependencyObject? node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is TextBox)
            {
                return true;
            }
        }

        return false;
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
        PaintThresholdBoxesFromOptions();
        ApplyPreviewOptionsNow();
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
        ApplyPreviewOptionsNow();
    }

    private void OnOptionsChanged()
    {
        CommitAllFields();
        _options = CurrentOptions();
        ApplyPreviewOptionsNow();
    }

    private void SyncActionUi() => SyncDependentEnabled();

    /// <summary>
    /// ラジオ選択に応じて意味のない項目を無効化。
    /// フェードは無音削除時のみ。次のゲートまでの最小時間は「全体」のみ（冒頭＆末尾は外側境界に影響しない）。
    /// </summary>
    private void SyncDependentEnabled()
    {
        var hitPoints = SelectedAction == SilenceDetectAction.CreateHitPointMarkers;
        var delete = SelectedAction == SilenceDetectAction.DeleteSilence;
        SetParamEnabled(_fadeInRow, _fadeInBox, delete && !hitPoints);
        SetParamEnabled(_fadeOutRow, _fadeOutBox, delete && !hitPoints);

        var entire = _entireRadio.IsChecked == true;
        SetParamEnabled(_minGateRow, _minGateBox, hitPoints || entire);
        SetParamEnabled(_minCloseRow, _minCloseBox, !hitPoints);
        SetParamEnabled(_preRollRow, _preRollBox, !hitPoints);
        SetParamEnabled(_postRollRow, _postRollBox, !hitPoints);
        SetParamEnabled(_closeRow, _closeBox, !hitPoints);
        _linkBox.IsEnabled = !hitPoints;
        _linkBox.Opacity = hitPoints ? 0.55 : 1;
        _edgesRadio.IsEnabled = !hitPoints;
        _entireRadio.IsEnabled = !hitPoints;
        _edgesRadio.Opacity = hitPoints ? 0.55 : 1;
        _entireRadio.Opacity = hitPoints ? 0.55 : 1;
        RowLabel(_openRow).Text = hitPoints
            ? UiStrings.LabelHitPointThreshold
            : UiStrings.LabelSilenceDetectOpenThreshold;
        RowLabel(_minGateRow).Text = hitPoints
            ? UiStrings.LabelHitPointMinGap
            : UiStrings.LabelSilenceDetectMinGate;

        var linked = _linkBox.IsChecked == true;
        if (!hitPoints)
        {
            _closeBox.IsEnabled = !linked;
            _closeRow.Opacity = linked ? 0.55 : 1;
            _closeBox.Opacity = 1;
        }

        _preview.HitPointMode = hitPoints;
    }

    private static void SetParamEnabled(StackPanel row, TextBox box, bool enabled)
    {
        row.IsEnabled = enabled;
        row.Opacity = enabled ? 1 : 0.55;
        box.IsEnabled = enabled;
    }

    private static TextBlock RowLabel(StackPanel row) => (TextBlock)row.Children[0];

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

            if (_hitPointsRadio.IsChecked == true)
            {
                return SilenceDetectAction.CreateHitPointMarkers;
            }

            return SilenceDetectAction.DeleteSilence;
        }
    }

    private void RefreshStatusFromPreview() =>
        _statusText.Text = _preview.HitPointMode
            ? UiStrings.HitPointDetectedCount(_preview.CueCount)
            : UiStrings.SilenceDetectDetectedSpans(_preview.KeepCount);

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
                NudgeDb(open, key == Key.Up ? 1 : -1, deferPreview: open);
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
            NudgeDb(open, Math.Sign(e.Delta), deferPreview: open);
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

    private void NudgeDb(bool open, int direction, bool deferPreview = false)
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
        if (deferPreview)
        {
            _preview.ShowThreshold(_options);
            _openWheelPreviewTimer.Stop();
            _openWheelPreviewTimer.Start();
            return;
        }

        ApplyPreviewOptionsNow();
    }

    /// <summary>保留中のホイール反映を取り消し、今の設定で範囲を一度描く。</summary>
    private void ApplyPreviewOptionsNow()
    {
        _openWheelPreviewTimer.Stop();
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
                return box;
    }

    private TextBox CreateMsBox(int ms)
    {
        var box = CreateSettingsStyleNumberBox(ms.ToString(CultureInfo.InvariantCulture));
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
            FocusVisualStyle = null,
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
        // 下線付き文字は AccessText になる。TextBlock 用の明るい色が当たらず黒のままになる。
        radio.SetResourceReference(ForegroundProperty, "PrimaryForeBrush");
        var access = new Style(typeof(AccessText));
        access.Setters.Add(new Setter(TextElement.ForegroundProperty, new DynamicResourceExtension("PrimaryForeBrush")));
        radio.Resources.Add(typeof(AccessText), access);
        return radio;
    }

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

    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x00020000;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

    /// <summary>最小化ボタンだけ外す。最大化とリサイズは残す。</summary>
    private static void DisableMinimizeBox(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        _ = SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style & ~WsMinimizeBox));
        _ = SetWindowPos(
            hwnd,
            IntPtr.Zero,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", CharSet = CharSet.Auto)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", CharSet = CharSet.Auto)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);

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
/// 選択範囲の波形と ±しきい値線。残す区間とヒットは適用時と同じ検出。
/// 波形はピークピラミッド（深く寄ると生サンプル）からビットマップにする。ホイールはカーソル位置を基準に時間方向へ拡縮する。
/// </summary>
internal sealed class SilenceDetectWavePreview : FrameworkElement
{
    private const int RawColumnMaxSamplesPerPixel = 96;
    private const int RawColumnMaxFrames = 1 << 18;

    private float[] _columnMins = [];
    private float[] _columnMaxs = [];
    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private double _bitmapViewStart;
    private double _bitmapViewSpan;
    private float _bitmapGain;
    private bool _waveDirty = true;
    private List<WaveSelection> _keeps = [];
    private List<long> _hitFrames = [];
    private bool _hitPointMode;
    private SilenceDetectOptions _options = SilenceDetectOptions.FromThreshold(SilentSkip.DefaultThresholdDb);
    private bool _dragging;
    private bool _dragOpen = true;
    private AudioDocument? _document;
    private WaveSelection _range;
    private int _channelMask;
    private double _viewStart;
    private double _viewSpan = 1;
    private long _seekFrame = -1;
    private DispatcherTimer? _seekNudgeTimer;
    private int _seekNudgeDirection;
    private bool _seekNudgeRepeatStarted;
    private long _seekNudgeLastAt;
    private double _hoverX = double.NaN;
    private static readonly Pen HoverBarPen = CreateHoverBarPen();
    private Pen? _seekGlowOuter;
    private Pen? _seekGlowInner;
    private Pen? _seekCore;
    private Color _seekPenColor;
    private float _displayGain = 1;
    private double _ampZoom = 1;
    private float DrawnGain => _displayGain * (float)_ampZoom;

    public event Action<double, bool>? ThresholdChanged;
    public event Action? KeepsChanged;

    public int KeepCount => _keeps.Count;

    internal IReadOnlyList<WaveSelection> Keeps => _keeps;

    public int CueCount => _hitPointMode ? _hitFrames.Count : _keeps.Count;

    public bool HitPointMode
    {
        get => _hitPointMode;
        set
        {
            if (_hitPointMode == value)
            {
                return;
            }

            _hitPointMode = value;
            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            _document = value;
            ResetView();
            RefreshDisplayGain();
            MarkWaveDirty();
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
            ResetView();
            RefreshDisplayGain();
            MarkWaveDirty();
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
            RefreshDisplayGain();
            MarkWaveDirty();
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
            if (_dragging)
            {
                InvalidateVisual();
                return;
            }

            RefreshKeepsFromEnvelope();
            InvalidateVisual();
        }
    }

    /// <summary>しきい値線だけ描き直す。検出範囲は変えない。</summary>
    public void ShowThreshold(SilenceDetectOptions options)
    {
        _options = options.Normalized();
        InvalidateVisual();
    }

    public SilenceDetectWavePreview()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
        // Preview で確実に拾う（親やフォーカスに奪われない）。
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.NumPad7 && _seekNudgeDirection < 0
                || key == Key.NumPad9 && _seekNudgeDirection > 0)
            {
                StopSeekNudge();
            }
        };
        Unloaded += (_, _) => StopSeekNudge();
        LostKeyboardFocus += (_, _) => StopSeekNudge();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MarkWaveDirty();
        InvalidateVisual();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || _range.IsEmpty)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (TryHandleWaveKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// エディタ波形の時間ズーム、振幅ズーム、シーク移動。選択・マーカー・リージョンはここには無い。
    /// </summary>
    private bool TryHandleWaveKey(Key key, ModifierKeys modifiers)
    {
        if (modifiers == ModifierKeys.None && key is >= Key.D0 and <= Key.D9)
        {
            JumpSeekPercent((key - Key.D0) / 10d);
            return true;
        }

        if (modifiers == ModifierKeys.None && key is Key.NumPad7 or Key.NumPad9)
        {
            BeginSeekNudge(key == Key.NumPad7 ? -1 : 1);
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Control)
        {
            var zoom = CurrentTimeZoom;
            var next = zoom + 1e-9 < WaveformView.TimeZoomStepMax
                ? WaveformView.TimeZoomStepMax
                : WaveformView.TimeZoomMax;
            ZoomTimeTo(next);
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Control)
        {
            ZoomTimeTo(1d);
            return true;
        }

        if (key == Key.Up && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SetAmpZoom(WaveformView.AmpZoomMax);
            return true;
        }

        if (key == Key.Down && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SetAmpZoom(1d);
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Shift)
        {
            SetAmpZoom(_ampZoom * WaveformView.TimeZoomStep);
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Shift)
        {
            SetAmpZoom(_ampZoom / WaveformView.TimeZoomStep);
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.None)
        {
            ZoomTimeBy(WaveformView.TimeZoomStep);
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.None)
        {
            ZoomTimeBy(1d / WaveformView.TimeZoomStep);
            return true;
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.Shift)
        {
            return true;
        }

        if (key == Key.Left && modifiers == ModifierKeys.None)
        {
            NudgeSeek(-1);
            return true;
        }

        if (key == Key.Right && modifiers == ModifierKeys.None)
        {
            NudgeSeek(1);
            return true;
        }

        if (key == Key.Home && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveSeek(ViewEdgeFrame(-1), ensureVisible: false);
            return true;
        }

        if (key == Key.End && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveSeek(ViewEdgeFrame(1), ensureVisible: false);
            return true;
        }

        if (key == Key.PageUp && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveSeek(SeekFrame + (long)Math.Round(_viewSpan * -0.05), ensureVisible: true);
            return true;
        }

        if (key == Key.PageDown && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            MoveSeek(SeekFrame + (long)Math.Round(_viewSpan * 0.05), ensureVisible: true);
            return true;
        }

        if (key == Key.Home && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            MoveSeek(_range.StartFrame, ensureVisible: true);
            return true;
        }

        if (key == Key.End && modifiers is ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift))
        {
            MoveSeek(Math.Max(_range.StartFrame, _range.EndFrame - 1), ensureVisible: true);
            return true;
        }

        return false;
    }

    internal double AmpZoom => _ampZoom;

    private void SetAmpZoom(double zoom)
    {
        var next = Math.Clamp(zoom, 1d, WaveformView.AmpZoomMax);
        if (Math.Abs(next - _ampZoom) < 1e-12)
        {
            return;
        }

        _ampZoom = next;
        MarkWaveDirty();
        InvalidateVisual();
    }

    private double CurrentTimeZoom =>
        _viewSpan <= 0 ? 1d : Math.Max(1d, _range.Length / _viewSpan);

    private long SeekFrame => _seekFrame >= 0 ? _seekFrame : ViewEdgeFrame(0);

    private void ZoomTimeBy(double factor) =>
        ZoomTimeTo(Math.Clamp(CurrentTimeZoom * factor, 1d, WaveformView.TimeZoomMax));

    private void ZoomTimeTo(double zoom)
    {
        zoom = Math.Clamp(zoom, 1d, WaveformView.TimeZoomMax);
        var span = Math.Max(1d, _range.Length / zoom);
        var seek = SeekFrame;
        if (_seekFrame < 0)
        {
            _seekFrame = seek;
        }

        _viewSpan = span;
        var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - span);
        _viewStart = Math.Clamp(seek - span * 0.5, _range.StartFrame, maxStart);
        MarkWaveDirty();
        InvalidateVisual();
    }

    private void BeginSeekNudge(int direction)
    {
        if (direction == 0 || _document is null)
        {
            return;
        }

        if (_seekNudgeDirection == direction && _seekNudgeTimer is { IsEnabled: true })
        {
            return;
        }

        NudgeSeekBySeconds(direction * LibraryPlayerMode.SeekNudgeSeconds);
        _seekNudgeDirection = direction;
        _seekNudgeRepeatStarted = false;
        _seekNudgeLastAt = Environment.TickCount64;
        _seekNudgeTimer ??= new DispatcherTimer();
        _seekNudgeTimer.Tick -= OnSeekNudgeTick;
        _seekNudgeTimer.Tick += OnSeekNudgeTick;
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        _seekNudgeTimer.Start();
    }

    private void OnSeekNudgeTick(object? sender, EventArgs e)
    {
        var held = _seekNudgeDirection < 0
            ? Keyboard.IsKeyDown(Key.NumPad7)
            : Keyboard.IsKeyDown(Key.NumPad9);
        if (_seekNudgeDirection == 0 || !held || Keyboard.Modifiers != ModifierKeys.None)
        {
            StopSeekNudge();
            return;
        }

        var now = Environment.TickCount64;
        var seconds = LibraryPlayerMode.SeekNudgeCatchUpSeconds(
            now - _seekNudgeLastAt,
            _seekNudgeRepeatStarted);
        NudgeSeekBySeconds(_seekNudgeDirection * seconds);
        _seekNudgeLastAt = now;
        if (_seekNudgeRepeatStarted || _seekNudgeTimer is null)
        {
            return;
        }

        _seekNudgeRepeatStarted = true;
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: true));
    }

    private void StopSeekNudge()
    {
        _seekNudgeDirection = 0;
        _seekNudgeRepeatStarted = false;
        _seekNudgeTimer?.Stop();
    }

    private void NudgeSeekBySeconds(double seconds)
    {
        var rate = _document?.SampleRate ?? 0;
        if (rate <= 0 || seconds == 0)
        {
            return;
        }

        var frames = (long)Math.Round(rate * seconds);
        MoveSeek(SeekFrame + frames, ensureVisible: true);
    }

    private void NudgeSeek(int direction)
    {
        var width = Math.Max(1d, ActualWidth);
        var step = _viewSpan <= Math.Min(WaveformView.SamplePointMaxVisibleFrames, width)
            ? 1L
            : Math.Max(1L, (long)Math.Round(_viewSpan / width));
        MoveSeek(SeekFrame + direction * step, ensureVisible: true);
    }

    private void JumpSeekPercent(double percent) =>
        MoveSeek((long)Math.Round(_viewStart + _viewSpan * percent), ensureVisible: true);

    private long ViewEdgeFrame(int direction)
    {
        var frame = direction < 0
            ? (long)Math.Round(_viewStart)
            : direction > 0
                ? (long)Math.Round(_viewStart + _viewSpan)
                : (long)Math.Round(_viewStart + _viewSpan * 0.5);
        return ClampSeek(frame);
    }

    private void MoveSeek(long frame, bool ensureVisible)
    {
        _seekFrame = ClampSeek(frame);
        if (ensureVisible)
        {
            EnsureSeekVisible(_seekFrame);
        }

        InvalidateVisual();
    }

    private long ClampSeek(long frame)
    {
        var last = Math.Max(_range.StartFrame, _range.EndFrame - 1);
        return Math.Clamp(frame, _range.StartFrame, last);
    }

    private void EnsureSeekVisible(long frame)
    {
        if (_viewSpan <= 1)
        {
            return;
        }

        var margin = Math.Max(1d, _viewSpan * 0.02);
        double? next = null;
        if (frame < _viewStart + margin)
        {
            next = frame - margin;
        }
        else if (frame > _viewStart + _viewSpan - margin)
        {
            next = frame - _viewSpan + margin;
        }

        if (next is not double start)
        {
            return;
        }

        var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - _viewSpan);
        var clamped = Math.Clamp(start, _range.StartFrame, maxStart);
        if (Math.Abs(clamped - _viewStart) < 0.0001)
        {
            return;
        }

        _viewStart = clamped;
        MarkWaveDirty();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var pos = e.GetPosition(this);
        var y = pos.Y;
        if (!IsOverThresholdLine(y))
        {
            PlaceSeek(pos.X);
            e.Handled = true;
            return;
        }

        Focus();
        CaptureMouse();
        _dragging = true;
        _dragOpen = _hitPointMode || PickOpenLine(y);
        Cursor = Cursors.SizeNS;
        ApplyMouseY(y);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        UpdateHoverBar(pos.X);
        var y = pos.Y;
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            Cursor = Cursors.SizeNS;
            ApplyMouseY(y);
            e.Handled = true;
            return;
        }

        Cursor = IsOverThresholdLine(y) ? Cursors.SizeNS : Cursors.Arrow;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ClearHoverBar();
        if (!_dragging)
        {
            Cursor = Cursors.Arrow;
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            FinishThresholdDrag();
            ReleaseMouseCapture();
            Cursor = IsOverThresholdLine(e.GetPosition(this).Y) ? Cursors.SizeNS : Cursors.Arrow;
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        FinishThresholdDrag();
    }

    /// <summary>ドラッグ中は線だけ動かす。範囲の再計算は離したときに一度。</summary>
    private void FinishThresholdDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        RefreshKeepsFromEnvelope();
        InvalidateVisual();
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ApplyPointerWheel(e.Delta, Keyboard.Modifiers, e.GetPosition(this).X))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Ctrl はエディタと同じ振幅ズーム。それ以外は時間ズーム。
    /// </summary>
    internal bool ApplyPointerWheel(int delta, ModifierKeys modifiers, double pointerX)
    {
        if (delta == 0 || _range.IsEmpty)
        {
            return false;
        }

        if (modifiers == ModifierKeys.Control)
        {
            SetAmpZoom(delta > 0
                ? _ampZoom * WaveformView.TimeZoomStep
                : _ampZoom / WaveformView.TimeZoomStep);
            return true;
        }

        if (ActualWidth <= 1)
        {
            return false;
        }

        var fraction = _seekFrame >= 0
            ? (_seekFrame - _viewStart) / Math.Max(1.0, _viewSpan)
            : pointerX / ActualWidth;
        var factor = delta > 0 ? WaveformView.WheelTimeStep : 1d / WaveformView.WheelTimeStep;
        var minSpan = Math.Max(1d, _range.Length / WaveformView.TimeZoomMax);
        (_viewStart, _viewSpan) = ZoomAround(
            _viewStart,
            _viewSpan,
            _range.StartFrame,
            _range.Length,
            fraction,
            factor,
            minSpan);
        MarkWaveDirty();
        InvalidateVisual();
        return true;
    }

    /// <summary>factor が 1 より大きいと span が縮む。アンカー割合のフレームは動かさない。</summary>
    internal static (double Start, double Span) ZoomAround(
        double start,
        double span,
        double fullStart,
        double fullLength,
        double anchorFraction,
        double factor,
        double minSpan)
    {
        fullLength = Math.Max(1, fullLength);
        minSpan = Math.Clamp(minSpan, 1, fullLength);
        span = Math.Clamp(span, minSpan, fullLength);
        factor = Math.Max(1e-6, factor);
        anchorFraction = Math.Clamp(anchorFraction, 0, 1);
        var anchor = start + anchorFraction * span;
        var nextSpan = Math.Clamp(span / factor, minSpan, fullLength);
        var nextStart = anchor - anchorFraction * nextSpan;
        var fullEnd = fullStart + fullLength;
        if (nextStart < fullStart)
        {
            nextStart = fullStart;
        }

        if (nextStart + nextSpan > fullEnd)
        {
            nextStart = fullEnd - nextSpan;
        }

        return (nextStart, nextSpan);
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
        var zero = WpfControlHelpers.FrozenBrush(Theme.Get("WaveZeroLineBrush"));
        var keep = WpfControlHelpers.FrozenBrush(Theme.Get("WaveSelectionFillBrush"));
        var thresholdBrush = WpfControlHelpers.FrozenBrush(Theme.Get("LevelMeterClipOnBrush"));
        dc.DrawRectangle(back, null, new Rect(0, 0, width, height));

        var mid = height * 0.5;
        var amp = height * 0.5;
        var gain = DrawnGain;

        EnsureWaveBitmap(width, height);
        if (_document is not null && !_range.IsEmpty)
        {
            var viewStart = _viewStart;
            var viewSpan = Math.Max(1.0, _viewSpan);
            if (!_hitPointMode)
            {
                foreach (var span in _keeps)
                {
                    var x0 = (span.StartFrame - viewStart) / viewSpan * width;
                    var x1 = (span.EndFrame - viewStart) / viewSpan * width;
                    if (x1 <= x0)
                    {
                        continue;
                    }

                    dc.DrawRectangle(keep, null, new Rect(x0, 0, x1 - x0, height));
                }
            }

            if (_bitmap is not null)
            {
                dc.DrawImage(_bitmap, new Rect(0, 0, width, height));
            }

            if (_hitPointMode && _hitFrames.Count > 0)
            {
                var marker = WpfControlHelpers.FrozenBrush(Theme.Get("MarkerBrush"));
                var markerPen = new Pen(marker, 1.5);
                markerPen.Freeze();
                foreach (var frame in _hitFrames)
                {
                    var x = (frame - viewStart) / viewSpan * width;
                    dc.DrawLine(markerPen, new Point(x, 0), new Point(x, height));
                }
            }
        }

        var zeroPen = new Pen(zero, 1);
        zeroPen.Freeze();
        dc.DrawLine(zeroPen, new Point(0, mid), new Point(width, mid));

        DrawThresholdPair(dc, thresholdBrush, mid, amp, width, SilentSkip.LinearFromDb(_options.OpenThresholdDb) * gain, thick: true);
        if (!_hitPointMode
            && !_options.LinkThresholds
            && Math.Abs(_options.OpenThresholdDb - _options.CloseThresholdDb) > 1e-3)
        {
            DrawThresholdPair(dc, thresholdBrush, mid, amp, width, SilentSkip.LinearFromDb(_options.CloseThresholdDb) * gain, thick: false);
        }

        DrawHoverBar(dc, width, height);
        DrawSeekBar(dc, width, height);
    }

    private void UpdateHoverBar(double x)
    {
        if (!double.IsNaN(_hoverX) && Math.Abs(_hoverX - x) < 0.5)
        {
            return;
        }

        _hoverX = x;
        InvalidateVisual();
    }

    private void ClearHoverBar()
    {
        if (double.IsNaN(_hoverX))
        {
            return;
        }

        _hoverX = double.NaN;
        InvalidateVisual();
    }

    private static Pen CreateHoverBarPen()
    {
        var pen = new Pen(Brushes.White, 1);
        pen.Freeze();
        return pen;
    }

    private void DrawHoverBar(DrawingContext dc, double width, double height)
    {
        if (double.IsNaN(_hoverX) || width <= 1 || height <= 1)
        {
            return;
        }

        var x = Math.Clamp(_hoverX, 0, width);
        dc.DrawLine(HoverBarPen, new Point(x, 0), new Point(x, height));
    }

    private void PlaceSeek(double x)
    {
        if (_range.IsEmpty || ActualWidth <= 1 || _viewSpan <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(x / ActualWidth, 0, 1);
        var frame = (long)Math.Round(_viewStart + fraction * _viewSpan);
        var last = Math.Max(_range.StartFrame, _range.EndFrame - 1);
        _seekFrame = Math.Clamp(frame, _range.StartFrame, last);
        InvalidateVisual();
    }

    private void DrawSeekBar(DrawingContext dc, double width, double height)
    {
        if (_seekFrame < 0 || _viewSpan <= 0)
        {
            return;
        }

        var x = (_seekFrame - _viewStart) / _viewSpan * width;
        if (x < -1 || x > width + 1)
        {
            return;
        }

        EnsureSeekPens();
        dc.DrawLine(_seekGlowOuter!, new Point(x, 0), new Point(x, height));
        dc.DrawLine(_seekGlowInner!, new Point(x, 0), new Point(x, height));
        dc.DrawLine(_seekCore!, new Point(x, 0), new Point(x, height));
    }

    /// <summary>エディタの再生ヘッドと同じ PlayheadBrush（シアン）とグロー。</summary>
    private void EnsureSeekPens()
    {
        var color = Theme.Get("PlayheadBrush");
        if (_seekCore is not null && _seekPenColor == color)
        {
            return;
        }

        _seekPenColor = color;
        _seekGlowOuter = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(40, color.R, color.G, color.B)), 3);
        _seekGlowInner = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(90, color.R, color.G, color.B)), 1.5);
        _seekCore = new Pen(WpfControlHelpers.FrozenBrush(color), 1);
        _seekGlowOuter.Freeze();
        _seekGlowInner.Freeze();
        _seekCore.Freeze();
    }

    /// <summary>表示範囲のピークが上下いっぱいに見える倍率。スレッショルド線も同じ倍率。</summary>
    internal static float DisplayGain(float[] mins, float[] maxs)
    {
        float peak = 0;
        var count = Math.Min(mins.Length, maxs.Length);
        for (var i = 0; i < count; i++)
        {
            peak = Math.Max(peak, Math.Abs(mins[i]));
            peak = Math.Max(peak, Math.Abs(maxs[i]));
        }

        return peak > 1e-6f ? 1f / peak : 1f;
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

    private const double ThresholdHitSlop = 6;

    /// <summary>描いている赤線（上下）の近くだけ。ハンドルの一辺と同じ 6px。</summary>
    private bool IsOverThresholdLine(double y)
    {
        foreach (var lineY in ThresholdLineYs())
        {
            if (Math.Abs(y - lineY) <= ThresholdHitSlop)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<double> ThresholdLineYs()
    {
        var height = ActualHeight;
        if (height <= 1)
        {
            yield break;
        }

        var mid = height * 0.5;
        var amp = height * 0.5;
        var gain = DrawnGain;
        foreach (var y in PairYs(mid, amp, SilentSkip.LinearFromDb(_options.OpenThresholdDb) * gain))
        {
            yield return y;
        }

        if (_hitPointMode
            || _options.LinkThresholds
            || Math.Abs(_options.OpenThresholdDb - _options.CloseThresholdDb) <= 1e-3)
        {
            yield break;
        }

        foreach (var y in PairYs(mid, amp, SilentSkip.LinearFromDb(_options.CloseThresholdDb) * gain))
        {
            yield return y;
        }
    }

    private static IEnumerable<double> PairYs(double mid, double amp, float linear)
    {
        yield return mid - linear * amp;
        yield return mid + linear * amp;
    }

    private bool PickOpenLine(double y)
    {
        if (_hitPointMode || _options.LinkThresholds)
        {
            return true;
        }

        var mid = ActualHeight * 0.5;
        var amp = ActualHeight * 0.5;
        var gain = DrawnGain;
        var openY = mid - SilentSkip.LinearFromDb(_options.OpenThresholdDb) * gain * amp;
        var closeY = mid - SilentSkip.LinearFromDb(_options.CloseThresholdDb) * gain * amp;
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
        var gain = DrawnGain;
        var linear = Math.Clamp(Math.Abs(y - mid) / amp / gain, 1e-6, 1.0);
        var db = 20.0 * Math.Log10(linear);
        ThresholdChanged?.Invoke(SilentSkip.ClampThresholdDb(db), _dragOpen);
    }

    private void ResetView()
    {
        _viewStart = _range.StartFrame;
        _viewSpan = Math.Max(1, _range.Length);
    }

    private void MarkWaveDirty() => _waveDirty = true;

    private static long RawColumnBudget(int width) =>
        Math.Min(RawColumnMaxFrames, (long)Math.Max(1, width) * RawColumnMaxSamplesPerPixel);

    private void RefreshDisplayGain()
    {
        if (_document is null || _range.IsEmpty)
        {
            _displayGain = 1;
            return;
        }

        var count = FillVisibleColumns(512, _range.StartFrame, _range.EndFrame);
        float peak = 0;
        for (var i = 0; i < count; i++)
        {
            peak = Math.Max(peak, Math.Abs(_columnMins[i]));
            peak = Math.Max(peak, Math.Abs(_columnMaxs[i]));
        }

        _displayGain = peak > 1e-6f ? 1f / peak : 1f;
        MarkWaveDirty();
    }

    private void EnsureWaveBitmap(double width, double height)
    {
        if (_document is null || _range.IsEmpty)
        {
            _bitmap = null;
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var pxW = Math.Max(1, (int)Math.Round(width * dpi.DpiScaleX));
        var pxH = Math.Max(1, (int)Math.Round(height * dpi.DpiScaleY));
        if (!_waveDirty
            && _bitmap is not null
            && _bitmapWidth == pxW
            && _bitmapHeight == pxH
            && Math.Abs(_bitmapViewStart - _viewStart) < 0.5
            && Math.Abs(_bitmapViewSpan - _viewSpan) < 0.5
            && Math.Abs(_bitmapGain - DrawnGain) < 1e-6f)
        {
            return;
        }

        var start = (long)Math.Floor(_viewStart);
        var end = (long)Math.Ceiling(_viewStart + _viewSpan);
        var lastFrame = Math.Max(_range.StartFrame, _range.EndFrame - 1);
        start = Math.Clamp(start, _range.StartFrame, lastFrame);
        end = Math.Clamp(end, start + 1, _range.EndFrame);
        var count = FillVisibleColumns(pxW, start, end);
        var bitmap = new WriteableBitmap(pxW, pxH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        var wave = ToPbgra(Theme.Get("WaveFillBrush"));
        var sampleFrames = end - start;
        var traceSamples = sampleFrames > 0 && sampleFrames <= pxW;
        bitmap.Lock();
        unsafe
        {
            var buffer = (uint*)bitmap.BackBuffer;
            var stride = bitmap.BackBufferStride / 4;
            var pixels = stride * pxH;
            for (var i = 0; i < pixels; i++)
            {
                buffer[i] = 0;
            }

            var mid = pxH * 0.5;
            var amp = pxH * 0.5 * DrawnGain;
            if (traceSamples)
            {
                PaintSampleTrace(buffer, stride, pxW, pxH, start, end, mid, amp, wave, dpi.DpiScaleX, width);
            }

            if (!traceSamples)
            for (var i = 0; i < count; i++)
            {
                var y0 = mid - _columnMaxs[i] * amp;
                var y1 = mid - _columnMins[i] * amp;
                if (y1 < y0)
                {
                    (y0, y1) = (y1, y0);
                }

                var top = (int)Math.Floor(y0);
                var bottom = (int)Math.Ceiling(y1);
                if (bottom <= top)
                {
                    bottom = top + 1;
                }

                top = Math.Clamp(top, 0, pxH - 1);
                bottom = Math.Clamp(bottom, top + 1, pxH);
                var x0 = i * pxW / count;
                var x1 = (i + 1) * pxW / count;
                if (x1 <= x0)
                {
                    x1 = Math.Min(pxW, x0 + 1);
                }

                for (var x = x0; x < x1; x++)
                {
                    var column = buffer + x;
                    for (var y = top; y < bottom; y++)
                    {
                        column[y * stride] = wave;
                    }
                }
            }
        }

        bitmap.AddDirtyRect(new Int32Rect(0, 0, pxW, pxH));
        bitmap.Unlock();
        bitmap.Freeze();
        _bitmap = bitmap;
        _bitmapWidth = pxW;
        _bitmapHeight = pxH;
        _bitmapViewStart = _viewStart;
        _bitmapViewSpan = _viewSpan;
        _bitmapGain = DrawnGain;
        _waveDirty = false;
    }

    /// <summary>表示フレームが画素幅以下のとき、エディタと同じくサンプルを結ぶ線と点を描く。</summary>
    private unsafe void PaintSampleTrace(
        uint* buffer,
        int stride,
        int pxW,
        int pxH,
        long start,
        long end,
        double mid,
        double amp,
        uint color,
        double dpiScaleX,
        double dipWidth)
    {
        var document = _document;
        if (document is null || _viewSpan <= 0)
        {
            return;
        }

        var channels = Math.Max(1, document.Channels);
        var samples = document.Interleaved;
        var frameCount = samples.Length / channels;
        var count = (int)(end - start);
        var dots = count > 0 && count <= Math.Min(WaveformView.SamplePointMaxVisibleFrames, dipWidth);
        var radius = Math.Max(1, (int)Math.Round(WaveformView.SamplePointRadius * dpiScaleX));
        var havePrev = false;
        var prevX = 0;
        var prevY = 0;
        for (var i = 0; i < count; i++)
        {
            var frame = start + i;
            if (frame < 0 || frame >= frameCount)
            {
                continue;
            }

            var x = (int)Math.Round((frame - _viewStart) / _viewSpan * pxW);
            var y = (int)Math.Round(mid - SampleAt(samples, channels, frame, frameCount) * amp);
            y = Math.Clamp(y, 0, pxH - 1);
            if (count == 1)
            {
                FillVertical(buffer, stride, pxW, pxH, x, y - 3, y + 3, color);
            }
            else if (havePrev)
            {
                DrawTraceLine(buffer, stride, pxW, pxH, prevX, prevY, x, y, color);
                DrawTraceLine(buffer, stride, pxW, pxH, prevX, prevY + 1, x, y + 1, color);
            }

            if (dots)
            {
                FillTraceDot(buffer, stride, pxW, pxH, x, y, radius, color);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
        }
    }

    private float SampleAt(float[] samples, int channels, long frame, int frameCount)
    {
        if (frame < 0 || frame >= frameCount)
        {
            return 0;
        }

        var chosen = 0f;
        var peak = -1f;
        var any = false;
        var origin = (int)frame * channels;
        for (var ch = 0; ch < channels; ch++)
        {
            if (!ChannelSolo.Contains(_channelMask, ch))
            {
                continue;
            }

            var sample = samples[origin + ch];
            var abs = Math.Abs(sample);
            if (!any || abs > peak)
            {
                chosen = sample;
                peak = abs;
                any = true;
            }
        }

        return chosen;
    }

    private static unsafe void FillVertical(uint* buffer, int stride, int width, int height, int x, int y0, int y1, uint color)
    {
        if ((uint)x >= (uint)width)
        {
            return;
        }

        if (y0 > y1)
        {
            (y0, y1) = (y1, y0);
        }

        y0 = Math.Clamp(y0, 0, height - 1);
        y1 = Math.Clamp(y1, 0, height - 1);
        for (var y = y0; y <= y1; y++)
        {
            buffer[y * stride + x] = color;
        }
    }

    private static unsafe void DrawTraceLine(
        uint* buffer,
        int stride,
        int width,
        int height,
        int x0,
        int y0,
        int x1,
        int y1,
        uint color)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        while (true)
        {
            if ((uint)x0 < (uint)width && (uint)y0 < (uint)height)
            {
                buffer[y0 * stride + x0] = color;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }

    private static unsafe void FillTraceDot(
        uint* buffer,
        int stride,
        int width,
        int height,
        int cx,
        int cy,
        int radius,
        uint color)
    {
        var r2 = radius * radius;
        var y0 = Math.Max(0, cy - radius);
        var y1 = Math.Min(height - 1, cy + radius);
        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(width - 1, cx + radius);
        for (var y = y0; y <= y1; y++)
        {
            var dy = y - cy;
            var row = buffer + y * stride;
            for (var x = x0; x <= x1; x++)
            {
                var dx = x - cx;
                if (dx * dx + dy * dy <= r2)
                {
                    row[x] = color;
                }
            }
        }
    }

    private int FillVisibleColumns(int columns, long start, long end)
    {
        if (_document is null || columns <= 0 || end <= start)
        {
            return 0;
        }

        var frames = end - start;
        var useRaw = frames <= RawColumnBudget(columns);
        if (!useRaw)
        {
            if (_document.Peaks is { IsEmpty: false } peaks)
            {
                var peakChannels = Math.Max(1, peaks.Channels);
                EnsureColumns(columns * peakChannels);
                var count = peaks.ReadRangePacked(start, end, columns, _columnMins, _columnMaxs);
                if (count > 0)
                {
                    FoldSolo(_columnMins, _columnMaxs, count, peakChannels, _channelMask);
                    return count;
                }
            }
        }

        if (frames > RawColumnBudget(columns))
        {
            return 0;
        }

        return FillRawColumns(columns, start, end);
    }

    private int FillRawColumns(int columns, long start, long end)
    {
        var document = _document;
        if (document is null)
        {
            return 0;
        }

        var channels = Math.Max(1, document.Channels);
        var frames = end - start;
        var buckets = (int)Math.Min(columns, Math.Max(1, frames));
        if (buckets <= 0)
        {
            return 0;
        }

        EnsureColumns(buckets);
        var samples = document.Interleaved;
        var frameCount = samples.Length / channels;
        for (var i = 0; i < buckets; i++)
        {
            var frame0 = start + i * frames / buckets;
            var frame1 = start + (i + 1) * frames / buckets;
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
                if (baseIndex < 0 || baseIndex + channels > samples.Length)
                {
                    break;
                }

                for (var ch = 0; ch < channels; ch++)
                {
                    if (!ChannelSolo.Contains(_channelMask, ch))
                    {
                        continue;
                    }

                    var sample = samples[baseIndex + ch];
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

            _columnMins[i] = min;
            _columnMaxs[i] = max;
        }

        return buckets;
    }

    private void EnsureColumns(int count)
    {
        if (_columnMins.Length >= count)
        {
            return;
        }

        _columnMins = new float[count];
        _columnMaxs = new float[count];
    }

    private static void FoldSolo(float[] mins, float[] maxs, int count, int channels, int mask)
    {
        for (var i = 0; i < count; i++)
        {
            var src = i * channels;
            float min = 0;
            float max = 0;
            var first = true;
            for (var ch = 0; ch < channels; ch++)
            {
                if (!ChannelSolo.Contains(mask, ch))
                {
                    continue;
                }

                var lo = mins[src + ch];
                var hi = maxs[src + ch];
                if (first)
                {
                    min = lo;
                    max = hi;
                    first = false;
                }
                else
                {
                    if (lo < min)
                    {
                        min = lo;
                    }

                    if (hi > max)
                    {
                        max = hi;
                    }
                }
            }

            mins[i] = min;
            maxs[i] = max;
        }
    }

    private static uint ToPbgra(Color color)
    {
        var a = color.A;
        var r = color.R * a / 255;
        var g = color.G * a / 255;
        var b = color.B * a / 255;
        return (uint)(b | (g << 8) | (r << 16) | (a << 24));
    }

    /// <summary>塗りの区間は適用と同じ検出。最小ゲート、クローズ時間、プリ／ポストロールを含む。</summary>
    private void RefreshKeepsFromEnvelope()
    {
        if (_hitPointMode)
        {
            RefreshHitPoints();
            return;
        }

        _hitFrames = [];
        if (_document is null || _range.IsEmpty)
        {
            _keeps = [];
            KeepsChanged?.Invoke();
            return;
        }

        _keeps = SilenceDetect.CollectKeepSpans(
            _document.Interleaved,
            _document.Channels,
            _range.StartFrame,
            _range.EndFrame,
            _document.SampleRate,
            _channelMask,
            _options);
        KeepsChanged?.Invoke();
    }

    /// <summary>プレビューの縦線は適用時と同じ検出結果。</summary>
    private void RefreshHitPoints()
    {
        _keeps = [];
        if (_document is null || _range.IsEmpty)
        {
            _hitFrames = [];
            KeepsChanged?.Invoke();
            return;
        }

        _hitFrames = HitPointDetect.CollectFrames(
            _document.Interleaved,
            _document.Channels,
            _range.StartFrame,
            _range.EndFrame,
            _document.SampleRate,
            _channelMask,
            _options);
        KeepsChanged?.Invoke();
    }
}
