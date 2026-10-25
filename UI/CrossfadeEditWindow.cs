using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>フェード調整ツールの独立ウィンドウ（Hide で再利用。Close はしない）。</summary>
internal sealed class CrossfadeEditWindow : Window
{
    private readonly LoopSpliceView _splice = new()
    {
        MinHeight = 140,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly Border _spliceFrame = new()
    {
        BorderThickness = new Thickness(1),
        Padding = new Thickness(0),
        Margin = new Thickness(0, 0, 8, 0),
        CornerRadius = DesignMetrics.ControlCorner,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly TextBox _startBox = CreateValueBox();
    private readonly TextBox _endBox = CreateValueBox();
    private readonly TextBox _postBox = CreateValueBox();
    private readonly TextBox _fadeInBox = CreateValueBox();
    private readonly CheckBox _truncateCheck = new() { FontSize = 12 };
    private readonly CheckBox _deleteAfterLoopCheck = new() { FontSize = 12 };
    private readonly CheckBox _align28Check = new() { FontSize = 12 };
    private readonly CheckBox _xfadeCheck = new() { FontSize = 12 };
    private readonly TextBlock _hintText = new()
    {
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private CurveIconRow? _preOutCurve;
    private CurveIconRow? _preInCurve;
    private CurveIconRow? _postOutCurve;
    private CurveIconRow? _postInCurve;
    private StackPanel? _preCurveSide;
    private StackPanel? _postCurveSide;
    private CrossfadeEditState? _state;
    private bool _syncing;
    private bool _hidingFromHost;
    private bool _forceCloseFromHost;
    private bool _exitNotifyBusy;
    private bool _placementApplied;
    private readonly DispatcherTimer _valueNudgeTimer;
    private TextBox? _valueNudgeBox;
    private CrossfadeEditState.FocusField _valueNudgeFocus;
    private int _valueNudgeDir;
    private bool _valueNudgeRepeatStarted;
    private const int ValueNudgeRepeatDelayMs = 400;
    private const int ValueNudgeRepeatIntervalMs = 50;
    /// <summary>?????????????????? 0.5 ??????</summary>
    private readonly DispatcherTimer _valueCommitTimer;
    private TextBox? _valueCommitBox;
    private CrossfadeEditState.FocusField _valueCommitFocus;
    private const int ValueCommitDebounceMs = 500;

    /// <summary>??? true ??????????????? 0.5 ???????????</summary>
    public event Action<bool>? DraftChanged;
    public event Action? LoopMixRequested;
    public event Action? ExitRequested;
    public event Action<CrossfadeAutoSearchKind>? AutoSearchRequested;
    public event Action<CrossfadeEditState.FocusField>? SnapZeroRequested;

    public LoopSpliceView Splice => _splice;

    public CrossfadeEditState? State
    {
        get => _state;
        set
        {
            _state = value;
            SyncFromState();
            _splice.State = value;
        }
    }

    public CrossfadeEditWindow(Window owner)
    {
        Owner = owner;
        Title = UiStrings.CrossfadeEditTitle;
        // 規定サイズ＝最小サイズ（これより小さくリサイズ不可）。
        Width = DefaultWidth;
        MinWidth = DefaultWidth;
        Height = DefaultHeight;
        MinHeight = DefaultHeight;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        // ColorDev / SilenceDetect / TabTime と同じ右下リサイズ印。
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = BrushOf("WindowBackBrush");
        Foreground = BrushOf("PrimaryForeBrush");

        _spliceFrame.BorderBrush = BrushOf("ChromeBorderBrush");
        _spliceFrame.Background = BrushOf("WaveformBackBrush");
        _spliceFrame.Child = _splice;
        _hintText.Foreground = BrushOf("MutedForeBrush");

        if (TryFindResource("DarkCheckBoxStyle") is Style checkStyle)
        {
            _xfadeCheck.Style = checkStyle;
            _truncateCheck.Style = checkStyle;
            _deleteAfterLoopCheck.Style = checkStyle;
            _align28Check.Style = checkStyle;
        }

        _xfadeCheck.Foreground = BrushOf("PrimaryForeBrush");
        _truncateCheck.Foreground = BrushOf("PrimaryForeBrush");
        _deleteAfterLoopCheck.Foreground = BrushOf("PrimaryForeBrush");
        _align28Check.Foreground = BrushOf("PrimaryForeBrush");

        WireToggles();
        _splice.LoopEndsChanged += () =>
        {
            SyncFromState();
            DraftChanged?.Invoke(false);
        };

        _valueNudgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ValueNudgeRepeatDelayMs) };
        _valueNudgeTimer.Tick += (_, _) => OnValueNudgeTick();
        _valueCommitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ValueCommitDebounceMs) };
        _valueCommitTimer.Tick += (_, _) => OnValueCommitTimerTick();

        Content = new Border
        {
            Padding = new Thickness(12),
            Background = BrushOf("WindowBackBrush"),
            Child = BuildLayout(),
        };

        RefreshUiStrings();
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        UiThemeService.Changed += OnUiThemeChanged;
        Closed += (_, _) => UiThemeService.Changed -= OnUiThemeChanged;
        AppDialogKeys.Attach(this, RequestExit);
        Closing += OnClosing;
        LocationChanged += (_, _) => PersistPlacement();
        SizeChanged += (_, _) => PersistPlacement();
        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += OnPreviewKeyUp;
    }

    private void OnUiThemeChanged(object? sender, EventArgs e) =>
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Up or Key.Down or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl)
        {
            // Shift/Ctrl を離しても ↑↓ 保持中はタイマー継続。↑↓ を離したら停止判定。
            if (!Keyboard.IsKeyDown(Key.Up) && !Keyboard.IsKeyDown(Key.Down))
            {
                StopValueNudge();
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        if (_splice.TryHandleZoomKey(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    public void Present()
    {
        RefreshUiStrings();
        if (IsVisible)
        {
            Activate();
            return;
        }

        if (!_placementApplied)
        {
            if (!WindowPlacement.TryApplyFadeTool(this, AppStorage.Settings))
            {
                WindowPlacement.CenterOnContainingMonitor(this, Owner);
            }

            _placementApplied = true;
        }

        Show();
        Activate();
    }

    /// <summary>ホスト側から隠す（Close しないので再表示可能）。</summary>
    public void HideFromHost()
    {
        StopValueNudge();
        CancelPendingValueCommit();
        _hidingFromHost = true;
        try
        {
            PersistPlacement();
            if (IsVisible)
            {
                Hide();
            }
        }
        finally
        {
            _hidingFromHost = false;
        }
    }

    /// <summary>アプリ終了時など、本当に閉じる（通常の X は Hide のみ）。</summary>
    public void ForceCloseFromHost()
    {
        StopValueNudge();
        CancelPendingValueCommit();
        _forceCloseFromHost = true;
        _hidingFromHost = true;
        try
        {
            PersistPlacement();
            if (IsVisible)
            {
                Hide();
            }

            Close();
        }
        finally
        {
            _hidingFromHost = false;
        }
    }

    private void PersistPlacement()
    {
        if (!IsVisible && !_placementApplied)
        {
            return;
        }

        WindowPlacement.CaptureFadeTool(this, AppStorage.Settings);
        AppStorage.Save();
    }

    public void RefreshUiStrings()
    {
        Title = UiStrings.CrossfadeEditTitle;
        _hintText.Text = UiStrings.CrossfadeWindowHint;
        _truncateCheck.Content = UiStrings.CrossfadeLabelTruncate;
        _truncateCheck.ToolTip = UiStrings.CrossfadeTipTruncate;
        _deleteAfterLoopCheck.Content = UiStrings.CrossfadeLabelDeleteAfterLoop;
        _deleteAfterLoopCheck.ToolTip = UiStrings.CrossfadeTipDeleteAfterLoop;
        _align28Check.Content = UiStrings.CrossfadeLabelAlignTwentyEight;
        _align28Check.ToolTip = UiStrings.CrossfadeTipAlignTwentyEight;
        _xfadeCheck.Content = UiStrings.CrossfadeLabelXfade;
        _xfadeCheck.ToolTip = UiStrings.CrossfadeTipXfade;
        SyncFromState();
    }

    public void NotifyDocumentChanged() => _splice.InvalidateVisual();

    public void SyncFromHost(bool updateSplice = true) => SyncFromState(updateSplice);

    public void SetDocument(AudioDocument? document)
    {
        _splice.Document = document;
        _splice.InvalidateVisual();
    }

    private void RequestExit()
    {
        if (_exitNotifyBusy)
        {
            return;
        }

        _exitNotifyBusy = true;
        try
        {
            ExitRequested?.Invoke();
        }
        finally
        {
            _exitNotifyBusy = false;
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PersistPlacement();
        if (_forceCloseFromHost)
        {
            // アプリ終了: 本当に閉じる。
            return;
        }

        // 破棄せず隠す。ホストから Hide 中なら通知しない。
        e.Cancel = true;
        if (_hidingFromHost)
        {
            return;
        }

        if (IsVisible)
        {
            Hide();
        }

        RequestExit();
    }

    private void WireToggles()
    {
        _xfadeCheck.Checked += (_, _) => SetXfadePreview(true);
        _xfadeCheck.Unchecked += (_, _) => SetXfadePreview(false);
        _truncateCheck.Checked += (_, _) => SetTruncate(true);
        _truncateCheck.Unchecked += (_, _) => SetTruncate(false);
        _deleteAfterLoopCheck.Checked += (_, _) => SetDeleteAfterLoop(true);
        _deleteAfterLoopCheck.Unchecked += (_, _) => SetDeleteAfterLoop(false);
        _align28Check.Checked += (_, _) => SetAlignTwentyEight(true);
        _align28Check.Unchecked += (_, _) => SetAlignTwentyEight(false);
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        // 窓内タイトルは出さない。ヒントだけ左寄せ。
        DockPanel.SetDock(_hintText, Dock.Top);
        root.Children.Add(_hintText);

        // フッタ: 左＝継ぎ目プレビュー＋XFADE／右＝切り落とし＋OK／Cancel
        var actions = ActionRow();
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        // 左＝波形（拡縮）／右＝操作（固定幅・見切れ防止）
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = WaveformPaneMinWidth,
        });
        columns.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(ControlsPaneWidth),
        });

        _spliceFrame.Margin = new Thickness(0, 0, 4, 0);
        Grid.SetColumn(_spliceFrame, 0);
        columns.Children.Add(_spliceFrame);

        var scroll = new ScrollViewer
        {
            Width = ControlsPaneWidth,
            MinWidth = ControlsPaneWidth,
            MaxWidth = ControlsPaneWidth,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(4, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var body = new StackPanel();
        body.Children.Add(SectionLabel(UiStrings.CrossfadeAutoSearch, UiStrings.CrossfadeTipAutoSearch));
        body.Children.Add(AutoSearchRow());

        body.Children.Add(SectionHeaderWithNudgeHint(UiStrings.CrossfadeSectionParams));
        // 左＝始点／終点、右＝プリ／ポスト（目標スクショの二列）
        body.Children.Add(LoopParamsGrid());
        _align28Check.Margin = new Thickness(0, 4, 0, 6);
        body.Children.Add(_align28Check);

        body.Children.Add(CurveSection());

        scroll.Content = body;
        Grid.SetColumn(scroll, 1);
        columns.Children.Add(scroll);
        root.Children.Add(columns);
        return root;
    }

    /// <summary>
    /// ループ: 全列 Auto の1グリッド。
    /// ラベル｜入力｜0｜余白｜ラベル｜入力｜0 — Star 分割しないのでラベル〜入力に隙間が開かない。
    /// </summary>
    private UIElement LoopParamsGrid()
    {
        ApplyValueBoxWidth(_startBox, CompactValueBoxWidth);
        ApplyValueBoxWidth(_endBox, CompactValueBoxWidth);
        ApplyValueBoxWidth(_fadeInBox, CompactValueBoxWidth);
        ApplyValueBoxWidth(_postBox, CompactValueBoxWidth);

        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Left };
        // 0:左ラベル 1:左入力 2:左0 3:列間 4:右ラベル 5:右入力 6:右0
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ValueRowControlHeight + 4) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ValueRowControlHeight + 4) });

        PlaceLoopField(
            grid,
            row: 0,
            labelCol: 0,
            boxCol: 1,
            zeroCol: 2,
            UiStrings.CrossfadeLabelStart,
            UiStrings.CrossfadeTipStart,
            _startBox,
            CrossfadeEditState.FocusField.Start);
        PlaceLoopField(
            grid,
            row: 1,
            labelCol: 0,
            boxCol: 1,
            zeroCol: 2,
            UiStrings.CrossfadeLabelEnd,
            UiStrings.CrossfadeTipEnd,
            _endBox,
            CrossfadeEditState.FocusField.End);
        PlaceLoopField(
            grid,
            row: 0,
            labelCol: 4,
            boxCol: 5,
            zeroCol: 6,
            UiStrings.CrossfadeLabelFadeIn,
            UiStrings.CrossfadeTipFadeIn,
            _fadeInBox,
            CrossfadeEditState.FocusField.PreCrossfade);
        PlaceLoopField(
            grid,
            row: 1,
            labelCol: 4,
            boxCol: 5,
            zeroCol: 6,
            UiStrings.CrossfadeLabelPostFade,
            UiStrings.CrossfadeTipPostFade,
            _postBox,
            CrossfadeEditState.FocusField.PostCrossfade);
        return grid;
    }

    private void PlaceLoopField(
        Grid grid,
        int row,
        int labelCol,
        int boxCol,
        int zeroCol,
        string label,
        string tip,
        TextBox box,
        CrossfadeEditState.FocusField focus)
    {
        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = tip,
            Foreground = BrushOf("MutedForeBrush"),
        };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, labelCol);
        grid.Children.Add(text);

        PrepareValueBox(box, tip, focus);
        box.Height = ValueRowControlHeight;
        box.MinHeight = ValueRowControlHeight;
        box.MaxHeight = ValueRowControlHeight;
        box.Margin = new Thickness(0);
        box.Padding = new Thickness(6, 1, 6, 1);
        box.VerticalAlignment = VerticalAlignment.Center;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(box, row);
        Grid.SetColumn(box, boxCol);
        grid.Children.Add(box);

        var zero = MakeTinyButton("0", () =>
        {
            if (_state is not null)
            {
                _state.Focus = focus;
                SnapZeroRequested?.Invoke(focus);
            }
        }, UiStrings.CrossfadeTipSnapZero);
        zero.Width = ValueRowControlHeight;
        zero.Height = ValueRowControlHeight;
        zero.MinWidth = ValueRowControlHeight;
        zero.MinHeight = ValueRowControlHeight;
        zero.MaxHeight = ValueRowControlHeight;
        zero.Margin = new Thickness(2, 0, 0, 0);
        zero.Padding = new Thickness(0);
        zero.VerticalAlignment = VerticalAlignment.Center;
        zero.HorizontalAlignment = HorizontalAlignment.Left;
        Grid.SetRow(zero, row);
        Grid.SetColumn(zero, zeroCol);
        grid.Children.Add(zero);
    }

    private void PrepareValueBox(TextBox box, string tip, CrossfadeEditState.FocusField focus)
    {
        box.ToolTip = tip + "\n" + UiStrings.CrossfadeTipValueNudge;
        box.GotKeyboardFocus -= OnLoopBoxGotFocus;
        box.LostKeyboardFocus -= OnLoopBoxLostFocus;
        box.PreviewKeyDown -= OnLoopBoxPreviewKeyDown;
        box.PreviewMouseWheel -= OnLoopBoxPreviewMouseWheel;
        box.TextChanged -= OnLoopBoxTextChanged;
        box.Tag = focus;
        box.GotKeyboardFocus += OnLoopBoxGotFocus;
        box.LostKeyboardFocus += OnLoopBoxLostFocus;
        box.PreviewKeyDown += OnLoopBoxPreviewKeyDown;
        box.PreviewMouseWheel += OnLoopBoxPreviewMouseWheel;
        box.TextChanged += OnLoopBoxTextChanged;
    }

    private void OnLoopBoxGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Tag: CrossfadeEditState.FocusField focus } && _state is not null)
        {
            _state.Focus = focus;
        }
    }

    private void OnLoopBoxLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { Tag: CrossfadeEditState.FocusField focus } box)
        {
            StopValueNudge();
            CancelPendingValueCommit();
            CommitBox(box, focus, revertOnInvalid: true);
        }
    }

    private void OnLoopBoxTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing || sender is not TextBox { Tag: CrossfadeEditState.FocusField focus } box)
        {
            return;
        }

        // ?????????????? 0.5 ???????????
        ScheduleValueCommit(box, focus);
    }

    private void ScheduleValueCommit(TextBox box, CrossfadeEditState.FocusField focus)
    {
        _valueCommitBox = box;
        _valueCommitFocus = focus;
        _valueCommitTimer.Stop();
        _valueCommitTimer.Interval = TimeSpan.FromMilliseconds(ValueCommitDebounceMs);
        _valueCommitTimer.Start();
    }

    private void CancelPendingValueCommit()
    {
        _valueCommitTimer.Stop();
        _valueCommitBox = null;
    }

    private void OnValueCommitTimerTick()
    {
        _valueCommitTimer.Stop();
        if (_valueCommitBox is not { } box)
        {
            return;
        }

        var focus = _valueCommitFocus;
        _valueCommitBox = null;
        // ????????????????????????????Enter ????
        CommitBox(box, focus, revertOnInvalid: false);
    }

    private void OnLoopBoxPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: CrossfadeEditState.FocusField focus } box)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            StopValueNudge();
            CancelPendingValueCommit();
            CommitBox(box, focus, revertOnInvalid: true);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Up or Key.Down)
        {
            if (BeginOrContinueValueNudge(box, focus, e.Key == Key.Up ? 1 : -1))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
        {
            if (_valueNudgeTimer.IsEnabled && ReferenceEquals(_valueNudgeBox, box))
            {
                ApplyValueNudgeStep();
                e.Handled = true;
            }
        }
    }

    private void OnLoopBoxPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not TextBox { Tag: CrossfadeEditState.FocusField focus } box
            || !box.IsKeyboardFocusWithin)
        {
            return;
        }

        if (TryNudgeValueBox(box, focus, e.Delta > 0 ? 1 : -1, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private static TextBlock SectionLabel(string text, string? tip) =>
        new()
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 4),
            ToolTip = tip,
            Foreground = BrushOf("PrimaryForeBrush"),
        };

    private static UIElement SectionHeaderWithNudgeHint(string title)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrushOf("PrimaryForeBrush"),
        });
        row.Children.Add(new TextBlock
        {
            Text = UiStrings.CrossfadeHintValueNudge,
            FontSize = 10,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = BrushOf("MutedForeBrush"),
            ToolTip = UiStrings.CrossfadeTipValueNudge,
        });
        return row;
    }

    private UIElement CurveSection()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 2) };
        panel.Children.Add(SectionLabel(UiStrings.CrossfadeLabelCurve, UiStrings.CrossfadeTipCurve));

        // 上段＝イン（プリ）、下段＝ポスト。各行内は左＝アウト脚、右＝イン脚。
        var rows = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };

        _preOutCurve = CreateCurveIconRow(
            UiStrings.CrossfadeLabelCurveLegOut,
            isFadeIn: false,
            shape =>
            {
                if (_state is null)
                {
                    return;
                }

                _state.PreOutShape = shape;
                RefreshCurveIcons();
                DraftChanged?.Invoke(false);
            });
        _preInCurve = CreateCurveIconRow(
            UiStrings.CrossfadeLabelCurveLegIn,
            isFadeIn: true,
            shape =>
            {
                if (_state is null)
                {
                    return;
                }

                _state.PreInShape = shape;
                RefreshCurveIcons();
                DraftChanged?.Invoke(false);
            });
        _postOutCurve = CreateCurveIconRow(
            UiStrings.CrossfadeLabelCurveLegOut,
            isFadeIn: false,
            shape =>
            {
                if (_state is null)
                {
                    return;
                }

                _state.PostOutShape = shape;
                RefreshCurveIcons();
                DraftChanged?.Invoke(false);
            });
        _postInCurve = CreateCurveIconRow(
            UiStrings.CrossfadeLabelCurveLegIn,
            isFadeIn: true,
            shape =>
            {
                if (_state is null)
                {
                    return;
                }

                _state.PostInShape = shape;
                RefreshCurveIcons();
                DraftChanged?.Invoke(false);
            });

        _preCurveSide = CurveSideRow(
            UiStrings.CrossfadeLabelCurveIn,
            UiStrings.CrossfadeTipCurveIn,
            _preOutCurve,
            _preInCurve,
            bottomMargin: 8);
        _postCurveSide = CurveSideRow(
            UiStrings.CrossfadeLabelCurveOut,
            UiStrings.CrossfadeTipCurveOut,
            _postOutCurve,
            _postInCurve,
            bottomMargin: 0);
        rows.Children.Add(_preCurveSide);
        rows.Children.Add(_postCurveSide);
        panel.Children.Add(rows);
        RefreshCurveEnabled();
        return panel;
    }

    /// <summary>1行分。タイトルの下に、左＝アウト脚・右＝イン脚を横並び。</summary>
    private static StackPanel CurveSideRow(
        string title,
        string tip,
        CurveIconRow outLeg,
        CurveIconRow inLeg,
        double bottomMargin)
    {
        var row = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, bottomMargin),
            ToolTip = tip,
        };
        row.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
            Foreground = BrushOf("MutedForeBrush"),
            ToolTip = tip,
        });
        var legs = new Grid();
        legs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        legs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        outLeg.Root.Margin = new Thickness(0, 0, 6, 0);
        inLeg.Root.Margin = new Thickness(6, 0, 0, 0);
        Grid.SetColumn(outLeg.Root, 0);
        Grid.SetColumn(inLeg.Root, 1);
        legs.Children.Add(outLeg.Root);
        legs.Children.Add(inLeg.Root);
        row.Children.Add(legs);
        return row;
    }

    private CurveIconRow CreateCurveIconRow(string legLabel, bool isFadeIn, Action<FadeShape> onSelected)
    {
        var order = FadeCurves.MenuOrder(isFadeIn);
        var buttons = new List<Border>(order.Count);
        var icons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var row = new CurveIconRow(
            new StackPanel { Margin = new Thickness(0, 0, 0, 2) },
            buttons,
            order,
            order[0],
            isFadeIn,
            legLabel);

        row.Root.Children.Add(new TextBlock
        {
            Text = legLabel,
            FontSize = 10,
            Margin = new Thickness(0, 0, 0, 2),
            Foreground = BrushOf("MutedForeBrush"),
        });
        row.Root.Children.Add(icons);

        foreach (var shape in order)
        {
            var capture = shape;
            var frame = FadeCurveIcons.CreateChoiceButton(
                shape,
                isFadeIn,
                toolTip: $"{legLabel}: {UiStrings.LabelFadeCurveShort((int)shape)}");
            FadeCurveIcons.WireChoiceButtonHover(
                frame,
                isSelected: () => row.Selected == capture);
            frame.MouseLeftButtonUp += (_, _) =>
            {
                row.Selected = capture;
                HighlightCurveIconRow(row);
                onSelected(capture);
            };
            buttons.Add(frame);
            icons.Children.Add(frame);
        }

        HighlightCurveIconRow(row);
        return row;
    }

    private void RefreshCurveIcons()
    {
        if (_state is null)
        {
            RefreshCurveEnabled();
            return;
        }

        SetCurveIconRow(_preOutCurve, _state.PreOutShape);
        SetCurveIconRow(_preInCurve, _state.PreInShape);
        SetCurveIconRow(_postOutCurve, _state.PostOutShape);
        SetCurveIconRow(_postInCurve, _state.PostInShape);
        RefreshCurveEnabled();
    }

    /// <summary>プリ／ポスト各クロスフェード長が 0 より大きいときだけ、対応する曲線行を有効にする。</summary>
    private void RefreshCurveEnabled()
    {
        var preOn = _state is { PreCrossfadeFrames: > 0 };
        var postOn = _state is { PostCrossfadeFrames: > 0 };
        if (_preCurveSide is not null)
        {
            _preCurveSide.IsEnabled = preOn;
            _preCurveSide.Opacity = preOn ? 1.0 : 0.45;
        }

        if (_postCurveSide is not null)
        {
            _postCurveSide.IsEnabled = postOn;
            _postCurveSide.Opacity = postOn ? 1.0 : 0.45;
        }
    }

    private static void SetCurveIconRow(CurveIconRow? row, FadeShape shape)
    {
        if (row is null)
        {
            return;
        }

        row.Selected = shape;
        HighlightCurveIconRow(row);
    }

    private static void HighlightCurveIconRow(CurveIconRow row)
    {
        for (var i = 0; i < row.Buttons.Count; i++)
        {
            var match = i < row.Order.Count && row.Order[i] == row.Selected;
            FadeCurveIcons.ApplyChoiceButtonChrome(row.Buttons[i], match);
        }
    }

    private sealed class CurveIconRow(
        StackPanel root,
        List<Border> buttons,
        IReadOnlyList<FadeShape> order,
        FadeShape selected,
        bool isFadeIn,
        string legLabel)
    {
        public StackPanel Root { get; } = root;
        public List<Border> Buttons { get; } = buttons;
        public IReadOnlyList<FadeShape> Order { get; } = order;
        public FadeShape Selected { get; set; } = selected;
        public bool IsFadeIn { get; } = isFadeIn;
        public string LegLabel { get; } = legLabel;
    }

    private UIElement AutoSearchRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        // 並び: 始点のみ → 終点のみ → 始点と終点（2番目と3番目を入れ替え）
        row.Children.Add(MakeToolButton(
            UiStrings.CrossfadeAdjustStart,
            UiStrings.CrossfadeTipAdjustStart,
            () => AutoSearchRequested?.Invoke(CrossfadeAutoSearchKind.StartOnly)));
        row.Children.Add(MakeToolButton(
            UiStrings.CrossfadeAdjustEnd,
            UiStrings.CrossfadeTipAdjustEnd,
            () => AutoSearchRequested?.Invoke(CrossfadeAutoSearchKind.EndOnly)));
        row.Children.Add(MakeToolButton(
            UiStrings.CrossfadeAdjustSE,
            UiStrings.CrossfadeTipAdjustSE,
            () => AutoSearchRequested?.Invoke(CrossfadeAutoSearchKind.StartAndEnd)));
        return row;
    }

    private UIElement ActionRow()
    {
        var row = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = true };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        DockPanel.SetDock(left, Dock.Left);
        _xfadeCheck.Margin = new Thickness(0, 0, 0, 0);
        _xfadeCheck.VerticalAlignment = VerticalAlignment.Center;
        left.Children.Add(_xfadeCheck);
        row.Children.Add(left);

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(right, Dock.Right);
        _truncateCheck.Margin = new Thickness(0, 0, 8, 0);
        _truncateCheck.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(_truncateCheck);
        _deleteAfterLoopCheck.Margin = new Thickness(0, 0, 12, 0);
        _deleteAfterLoopCheck.VerticalAlignment = VerticalAlignment.Center;
        right.Children.Add(_deleteAfterLoopCheck);
        right.Children.Add(MakeDialogActionButton(
            UiStrings.ButtonOk,
            UiStrings.CrossfadeTipLoopMix,
            () => LoopMixRequested?.Invoke(),
            accent: true,
            isDefault: true));
        right.Children.Add(MakeDialogActionButton(
            UiStrings.ButtonCancel,
            UiStrings.CrossfadeTipExit,
            RequestExit,
            accent: false,
            isCancel: true));
        row.Children.Add(right);
        return row;
    }

    /// <summary>
    /// ↑↓＝1 / Shift＝10 / Ctrl+Shift＝100。
    /// Shift・Ctrl を後から足してもタイマーで連続変化する。
    /// </summary>
    private bool BeginOrContinueValueNudge(
        TextBox box,
        CrossfadeEditState.FocusField focus,
        int direction)
    {
        if (direction == 0 || !CanNudgeValueBox(box))
        {
            return false;
        }

        if (_valueNudgeDir == direction
            && _valueNudgeTimer.IsEnabled
            && ReferenceEquals(_valueNudgeBox, box)
            && _valueNudgeFocus == focus)
        {
            return true;
        }

        _valueNudgeBox = box;
        _valueNudgeFocus = focus;
        _valueNudgeDir = direction;
        _valueNudgeRepeatStarted = false;
        if (!ApplyValueNudgeStep())
        {
            StopValueNudge();
            return false;
        }

        _valueNudgeTimer.Stop();
        _valueNudgeTimer.Interval = TimeSpan.FromMilliseconds(ValueNudgeRepeatDelayMs);
        _valueNudgeTimer.Start();
        return true;
    }

    private void OnValueNudgeTick()
    {
        if (_valueNudgeDir == 0 || !IsValueNudgeHeld())
        {
            StopValueNudge();
            return;
        }

        // 押している ↑↓ に合わせて方向を更新（途中で反対キーへ持ち替えた場合）
        if (Keyboard.IsKeyDown(Key.Up) && !Keyboard.IsKeyDown(Key.Down))
        {
            _valueNudgeDir = 1;
        }
        else if (Keyboard.IsKeyDown(Key.Down) && !Keyboard.IsKeyDown(Key.Up))
        {
            _valueNudgeDir = -1;
        }

        ApplyValueNudgeStep();
        if (_valueNudgeRepeatStarted)
        {
            return;
        }

        _valueNudgeRepeatStarted = true;
        _valueNudgeTimer.Stop();
        _valueNudgeTimer.Interval = TimeSpan.FromMilliseconds(ValueNudgeRepeatIntervalMs);
        _valueNudgeTimer.Start();
    }

    private bool IsValueNudgeHeld() =>
        Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down);

    private void StopValueNudge()
    {
        _valueNudgeDir = 0;
        _valueNudgeBox = null;
        _valueNudgeRepeatStarted = false;
        _valueNudgeTimer.Stop();
        _valueNudgeTimer.Interval = TimeSpan.FromMilliseconds(ValueNudgeRepeatDelayMs);
    }

    private bool ApplyValueNudgeStep()
    {
        if (_valueNudgeBox is null || _valueNudgeDir == 0)
        {
            return false;
        }

        return TryNudgeValueBox(
            _valueNudgeBox,
            _valueNudgeFocus,
            _valueNudgeDir,
            Keyboard.Modifiers);
    }

    private bool CanNudgeValueBox(TextBox box) =>
        !_syncing
        && _state is not null
        && _splice.Document is not null
        && box.IsKeyboardFocusWithin;

    private bool TryNudgeValueBox(
        TextBox box,
        CrossfadeEditState.FocusField focus,
        int direction,
        ModifierKeys modifiers)
    {
        if (!CanNudgeValueBox(box) || direction == 0)
        {
            return false;
        }

        // Ctrl alone / Alt などはキャレット移動などに任せる
        if (modifiers is not (ModifierKeys.None or ModifierKeys.Shift
            or (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            return false;
        }

        var step = NudgeStepSamples(modifiers);
        _state!.Focus = focus;
        _state.Nudge(focus, direction * step, _splice.Document!.FrameCount);
        SyncFromState();
        // ?????????: ????????? 0.5 ?????????????
        DraftChanged?.Invoke(true);
        return true;
    }

    /// <summary>なし＝1 / Shift＝10 / Ctrl+Shift＝100 サンプル。</summary>
    private static long NudgeStepSamples(ModifierKeys modifiers) =>
        modifiers switch
        {
            ModifierKeys.Control | ModifierKeys.Shift => 100,
            ModifierKeys.Shift => 10,
            _ => 1,
        };

    private void SetTruncate(bool value)
    {
        if (_syncing || _state is null)
        {
            return;
        }

        _state.TruncateOutside = value;
        if (_splice.Document is not null)
        {
            _state.Clamp(_splice.Document.FrameCount);
        }

        DraftChanged?.Invoke(false);
    }

    private void SetDeleteAfterLoop(bool value)
    {
        if (_syncing || _state is null)
        {
            return;
        }

        _state.DeleteAfterLoop = value;
        if (_splice.Document is not null)
        {
            _state.Clamp(_splice.Document.FrameCount);
        }

        DraftChanged?.Invoke(false);
    }

    private void SetAlignTwentyEight(bool value)
    {
        if (_syncing || _state is null)
        {
            return;
        }

        _state.AlignLoopEndsToTwentyEight = value;
        AppStorage.Settings.FadeToolAlignLoopEndsToTwentyEight = value;
        AppStorage.Save();
        if (_splice.Document is not null)
        {
            _state.Clamp(_splice.Document.FrameCount);
        }

        SyncFromState();
        DraftChanged?.Invoke(false);
    }

    private void SetXfadePreview(bool value)
    {
        if (_syncing || _state is null)
        {
            return;
        }

        _state.ShowXfadePreview = value;
        _splice.InvalidateVisual();
        DraftChanged?.Invoke(false);
    }

    private void CommitBox(TextBox box, CrossfadeEditState.FocusField focus, bool revertOnInvalid = true)
    {
        if (_syncing || _state is null || _splice.Document is null)
        {
            return;
        }

        var frames = _splice.Document.FrameCount;
        var raw = box.Text;
        var dbIdx = raw.IndexOf('(');
        if (dbIdx >= 0)
        {
            raw = raw[..dbIdx];
        }

        raw = raw.Trim().TrimEnd('%');
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            if (revertOnInvalid)
            {
                SyncFromState();
            }

            return;
        }

        switch (focus)
        {
            case CrossfadeEditState.FocusField.Start:
                _state.SetStart(value, frames);
                break;
            case CrossfadeEditState.FocusField.End:
                _state.SetEnd(value, frames);
                break;
            case CrossfadeEditState.FocusField.PostCrossfade:
                _state.SetPostCrossfade(value, frames);
                break;
            case CrossfadeEditState.FocusField.PreCrossfade:
                _state.SetPreCrossfade(value, frames);
                break;
            default:
                _state.Focus = focus;
                _state.Clamp(frames);
                break;
        }

        var caret = box.IsKeyboardFocusWithin ? box.CaretIndex : -1;
        SyncFromState();
        if (caret >= 0 && box.IsKeyboardFocusWithin)
        {
            box.CaretIndex = Math.Clamp(caret, 0, box.Text.Length);
        }

        // Enter ????????????????????? 0.5 ????????
        DraftChanged?.Invoke(false);
    }

    private void SyncFromState(bool updateSplice = true)
    {
        if (_state is null)
        {
            return;
        }

        _syncing = true;
        try
        {
            _startBox.Text = _state.StartFrame.ToString(CultureInfo.InvariantCulture);
            _endBox.Text = _state.EndFrame.ToString(CultureInfo.InvariantCulture);
            _fadeInBox.Text = _state.PreCrossfadeFrames.ToString(CultureInfo.InvariantCulture);
            _postBox.Text = _state.PostCrossfadeFrames.ToString(CultureInfo.InvariantCulture);
            _truncateCheck.IsChecked = _state.TruncateOutside;
            _deleteAfterLoopCheck.IsChecked = _state.DeleteAfterLoop;
            _align28Check.IsChecked = _state.AlignLoopEndsToTwentyEight;
            _xfadeCheck.IsChecked = _state.ShowXfadePreview;
            RefreshCurveIcons();
            _splice.State = _state;
            // ???????????????????????????????? S/E ??????
            if (updateSplice)
            {
                _splice.InvalidateVisual();
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>サンプル位置／フェード長用。桁数で伸びないよう固定幅。</summary>
    private const double ValueBoxWidth = 120;

    /// <summary>ループ二列レイアウト用。</summary>
    private const double CompactValueBoxWidth = 88;

    /// <summary>ループ行の入力欄と 0 ボタンの共通高さ（縦位置揃え）。</summary>
    private const double ValueRowControlHeight = 28;

    /// <summary>右操作ペイン（自動探索／ループ／曲線）。ウィンドウ拡縮でも幅固定。</summary>
    private const double ControlsPaneWidth = 600;

    /// <summary>左波形ペインの最小幅。</summary>
    private const double WaveformPaneMinWidth = 200;

    /// <summary>フェード調整ウィンドウの規定サイズ（＝最小サイズ）。</summary>
    private const double DefaultWidth = ControlsPaneWidth + 420;

    private const double DefaultHeight = 520;

    private static void ApplyValueBoxWidth(TextBox box, double width)
    {
        box.Width = width;
        box.MinWidth = width;
        box.MaxWidth = width;
    }

    private static TextBox CreateValueBox()
    {
        var box = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Width = ValueBoxWidth,
            MinWidth = ValueBoxWidth,
            MaxWidth = ValueBoxWidth,
            HorizontalAlignment = HorizontalAlignment.Left,
            TextAlignment = TextAlignment.Center,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = BrushOf("PrimaryForeBrush"),
            Background = BrushOf("DialogInputBackBrush"),
            BorderBrush = BrushOf("ChromeBorderBrush"),
            CaretBrush = BrushOf("AccentCyanBrush"),
        };
        box.SetResourceReference(StyleProperty, "DarkTextBoxStyle");
        return box;
    }

    private static RoundedButton CreateToolButton(string content, string tip)
    {
        var btn = new RoundedButton
        {
            Content = content,
            ToolTip = string.IsNullOrEmpty(tip) ? null : tip,
            Margin = new Thickness(0, 0, 4, 4),
            Padding = new Thickness(10, 4, 10, 4),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            MinHeight = 28,
            CornerRadius = DesignMetrics.ControlCornerRadius,
        };
        ActionButtonLooks.ApplyClear(btn);
        return btn;
    }

    private static RoundedButton MakeToolButton(string content, string tip, Action click)
    {
        var btn = CreateToolButton(content, tip);
        btn.Click += (_, _) => click();
        return btn;
    }

    private static RoundedButton MakeTinyButton(string content, Action click, string? tip = null)
    {
        var btn = new RoundedButton
        {
            Content = content,
            ToolTip = tip,
            Margin = new Thickness(0),
            Padding = new Thickness(6, 0, 6, 0),
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            MinWidth = 28,
            Width = 28,
            Height = ValueRowControlHeight,
            MinHeight = ValueRowControlHeight,
            MaxHeight = ValueRowControlHeight,
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            CornerRadius = DesignMetrics.ControlCornerRadius,
        };
        ActionButtonLooks.ApplyClear(btn);
        btn.Click += (_, _) => click();
        return btn;
    }

    private static RoundedButton MakeDialogActionButton(
        string content,
        string tip,
        Action click,
        bool accent,
        bool isDefault = false,
        bool isCancel = false)
    {
        var btn = new RoundedButton
        {
            Content = content,
            ToolTip = tip,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
            CornerRadius = DesignMetrics.ControlCornerRadius,
        };
        AppDialogKeys.PrepareActionButton(btn, isDefault, isCancel);
        if (accent)
        {
            ActionButtonLooks.ApplyAccent(btn);
        }
        else
        {
            ActionButtonLooks.ApplyClear(btn);
        }

        btn.Click += (_, _) => click();
        return btn;
    }

    private static Brush BrushOf(string key) =>
        WpfControlHelpers.FrozenBrush(Theme.Get(key));
}

internal enum CrossfadeEditJump
{
    Start,
    End,
    StartPlusX,
    EndPlusX,
    StartAndEnd,
    FadeAndCut,
}
