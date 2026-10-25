using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal readonly record struct PitchCurveEditorDialogResult(
    bool Accepted,
    PitchCurve Curve,
    WaveSelection Range,
    float DocumentGainScale,
    int EdgeFadeMs,
    bool TimeStretch,
    float[]? PrecomputedAfterRange = null);

/// <summary>
/// 本体 <see cref="AudioPlayer"/> 経由のプレビュー。別インスタンスだと ASIO 等でデバイスを奪えない。
/// </summary>
internal sealed class PitchCurvePreviewPlayback
{
    public required Action<AudioDocument, WaveSelection, long> Play { get; init; }
    public required Action Stop { get; init; }
    public required Func<bool> IsPlaying { get; init; }
    public required Func<long> CursorFrame { get; init; }
}

/// <summary>
/// ピッチカーブ編集。モノ波形の上に赤線でカーブを描き、プレビュー再生してから適用する。
/// </summary>
internal sealed class PitchCurveEditorDialog : Window
{
    private enum EditTool
    {
        Brush,
        Points,
    }

    private readonly AudioDocument _document;
    private readonly WaveSelection _range;
    private readonly int _channelMask;
    private readonly PitchCurve _curve;
    private readonly PitchCurvePreviewPlayback _playback;
    private readonly PitchCurveWavePreview _preview;
    private readonly TextBox _flatBox;
    private readonly Panel _shapeButtons;
    private readonly RoundedButton _addPointButton;
    private readonly RadioButton _brushRadio;
    private readonly RadioButton _pointsRadio;
    private readonly CheckBox _snapScaleBox;
    private readonly CheckBox _noTimeStretchBox;
    private readonly CheckBox _lightweightBox;
    private readonly RoundedButton _playButton;
    private readonly RoundedButton _ignoreCurveButton;
    private readonly DispatcherTimer _playheadTimer;
    private readonly Grid _contentHost;
    private readonly Border _contentBorder;
    private readonly BusyGlassOverlay _busyGlass = new();
    private bool _previewPlaying;
    private long _previewStartedAt;
    /// <summary>プレビュー音声のフレーム数。タイムストレッチなしで長さが変わったとき、シーク対応に使う。</summary>
    private long _previewPlaybackFrames;
    /// <summary>
    /// タイムストレッチなし時: プレビュー出力フレーム → 元レンジ内オフセット。
    /// 単純比例だと変化カーブで音とシークがずれる。
    /// </summary>
    private int[]? _previewToSourceMap;
    private bool _ignorePitchCurve;
    private bool _accepting;
    private bool _rendering;
    private bool _updatingShapeButtons;
    private FadeShape _selectedShape;
    /// <summary>再生プレビューと OK 適用で共有するピッチ計算キャッシュ。</summary>
    private float[]? _cachedAfterRange;
    private float _cachedPeak;
    private string? _cacheKey;
    private CancellationTokenSource? _renderCts;
    /// <summary>スナップ／再生の割り込みで古い非同期処理を捨てるための世代。</summary>
    private int _pitchOpEpoch;
    /// <summary>形状アイコンが上昇向きか。選択点→次点の高低で決める。</summary>
    private bool _shapeIconsRising = true;
    /// <summary>ウィンドウ内だけの編集履歴。閉じると捨てる。</summary>
    private readonly List<PitchCurveEditSnapshot> _undoStack = [];
    private readonly List<PitchCurveEditSnapshot> _redoStack = [];
    private const int MaxCurveEditUndo = 64;

    private readonly record struct PitchCurveEditSnapshot(PitchCurve Curve, int SelectedIndex);

    public PitchCurveEditorDialogResult Result { get; private set; }

    public static PitchCurveEditorDialogResult Show(
        Window owner,
        AudioDocument document,
        WaveSelection range,
        int channelMask,
        bool selectionScoped,
        PitchCurvePreviewPlayback playback)
    {
        var dialog = new PitchCurveEditorDialog(document, range, channelMask, selectionScoped, playback)
        {
            Owner = owner,
        };
        WindowPaintReveal.ShowDialogWhenPainted(dialog);
        return dialog.Result;
    }

    private PitchCurveEditorDialog(
        AudioDocument document,
        WaveSelection range,
        int channelMask,
        bool selectionScoped,
        PitchCurvePreviewPlayback playback)
    {
        _document = document;
        _range = range.Clamp(document.FrameCount);
        _channelMask = channelMask;
        _playback = playback;
        _curve = PitchCurve.Flat(0);
        _curve.DefaultShape = FadeCurves.ParseStored(AppStorage.Settings.PitchCurveDefaultShape);

        Title = UiStrings.DialogPitchCurveTitle
            + (selectionScoped
                ? UiStrings.DialogPitchCurveSelectionSuffix
                : UiStrings.DialogPitchCurveEntireSuffix);
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = false;
        SourceInitialized += (_, _) => DisableMinimizeBox(this);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = DesignMetrics.From96(1080);
        // 仮の下限。Loaded で下部 UI の実測幅に差し替える。
        MinWidth = DesignMetrics.From96(560);
        SizeToContent = SizeToContent.Height;
        Background = (Brush)FindResource("WindowBackBrush");
        Foreground = (Brush)FindResource("PrimaryForeBrush");

        _preview = new PitchCurveWavePreview
        {
            Document = document,
            Range = _range,
            ChannelMask = channelMask,
            Curve = _curve,
            // ±24 半音の目盛り数字が全部見える高さ。数値入力で端に寄っても切れないようにする。
            Height = DesignMetrics.From96(520),
            // ウィンドウ最小高（旧 860 の半分）に追従できるようプレビュー下限も半分。
            MinHeight = DesignMetrics.From96(200),
            Margin = new Thickness(0, 0, 0, 10),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _preview.CurveChanged += OnCurveChanged;
        _preview.BeforePointEdit += CaptureBeforePointEdit;
        _preview.DiscardPointEditCapture += DiscardLastPointEditCapture;

        _pointsRadio = CreateRadio(UiStrings.LabelPitchCurvePoints, isChecked: true, "pitchTool");
        _brushRadio = CreateRadio(UiStrings.LabelPitchCurveBrush, isChecked: false, "pitchTool");
        _pointsRadio.Checked += (_, _) => SetTool(EditTool.Points);
        _brushRadio.Checked += (_, _) => SetTool(EditTool.Brush);

        // 半音数値欄。無音検出など他ダイアログの数値欄と同じ高さ・余白・フォント。数値は中央揃え。
        _flatBox = new TextBox
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
        if (TryFindResource("DarkTextBoxStyle") is Style flatStyle)
        {
            _flatBox.Style = flatStyle;
        }

        _flatBox.Height = DesignMetrics.AudioInputHeight;
        _flatBox.Padding = new Thickness(6, 1, 6, 1);
        _flatBox.FontSize = 11.333;
        _flatBox.TextAlignment = TextAlignment.Center;
        _flatBox.HorizontalContentAlignment = HorizontalAlignment.Center;

        _flatBox.PreviewMouseWheel += (_, e) =>
        {
            e.Handled = true;
            NudgeFlat(Math.Sign(e.Delta));
        };
        _flatBox.LostKeyboardFocus += (_, _) => CommitFlatBox();
        // ↑↓: ±1 半音。Shift+↑↓: ±12（1 オクターブ）。Enter: 適用して閉じる。
        _flatBox.PreviewKeyDown += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Up or Key.Down
                && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                e.Handled = true;
                var step = Keyboard.Modifiers == ModifierKeys.Shift ? 12 : 1;
                NudgeFlat(key == Key.Up ? step : -step);
                return;
            }

            if (key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                // 定数カーブ中だけ数値欄を反映（手描きカーブを Enter で潰さない）。
                // ↑↓ 後は既に SetConstant 済みなのでそのまま決定・閉じる。
                if (_curve.IsConstant(out float _))
                {
                    ApplyFlatFromBox();
                }

                Accept();
            }
        };

        // プルダウンではなく、フェードと同じアイコンボタンで形状を選ぶ。
        _shapeButtons = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // フェードと同じ並び。ツールチップに 1 始まりの番号（数字キーと対応）。
        var shapeOrder = FadeCurves.MenuOrderFadeIn;
        for (var i = 0; i < shapeOrder.Count; i++)
        {
            _shapeButtons.Children.Add(CreateShapeButton(shapeOrder[i], number: i + 1));
        }

        _selectedShape = _curve.DefaultShape;
        SelectShape(_selectedShape);
        _preview.SelectionChanged += SyncShapeButtonsToSelection;

        _snapScaleBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveSnapToScale,
            IsChecked = AppStorage.Settings.PitchCurveSnapToScale,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 4),
        };
        // 規定オフ＝長さを保つ。Alt+T でトグル。状態は設定に覚える。
        _noTimeStretchBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveNoTimeStretch,
            IsChecked = AppStorage.Settings.PitchCurveNoTimeStretch,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 4),
        };
        // 規定オフ。プレビューのみ軽量演算。OK 本適用は常に通常品質。
        _lightweightBox = new CheckBox
        {
            Content = UiStrings.LabelPitchCurveLightweightMode,
            IsChecked = AppStorage.Settings.PitchCurveLightweightMode,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 0, 4),
            ToolTip = UiStrings.TipPitchCurveLightweightMode,
        };
        if (TryFindResource("DarkCheckBoxStyle") is Style checkStyle)
        {
            _snapScaleBox.Style = checkStyle;
            _noTimeStretchBox.Style = checkStyle;
            _lightweightBox.Style = checkStyle;
        }

        _snapScaleBox.Checked += (_, _) => SetSnapToScale(true);
        _snapScaleBox.Unchecked += (_, _) => SetSnapToScale(false);
        _noTimeStretchBox.Checked += (_, _) => SetNoTimeStretch(true);
        _noTimeStretchBox.Unchecked += (_, _) => SetNoTimeStretch(false);
        _lightweightBox.Checked += (_, _) => SetLightweightMode(true);
        _lightweightBox.Unchecked += (_, _) => SetLightweightMode(false);

        _addPointButton = CreateChromeButton(UiStrings.LabelPitchCurveAddPoint);
        _addPointButton.Click += (_, _) => AddPointBesideSelection();

        // チェックの左余白をコンパクトに（中央寄せの1行に収める）。
        _snapScaleBox.Margin = new Thickness(12, 0, 0, 0);
        _noTimeStretchBox.Margin = new Thickness(12, 0, 0, 0);
        _lightweightBox.Margin = new Thickness(12, 0, 0, 0);
        _pointsRadio.Margin = new Thickness(12, 0, 0, 0);
        _brushRadio.Margin = new Thickness(8, 0, 0, 0);

        _playButton = CreateChromeButton(UiStrings.LabelPitchCurvePlay);
        _playButton.ToolTip = UiStrings.TipPitchCurvePlay;
        _playButton.Click += (_, _) => TogglePreview();
        _ignoreCurveButton = CreateChromeButton(UiStrings.LabelPitchCurvePlayBypass);
        _ignoreCurveButton.Click += (_, _) => ToggleIgnoreCurvePreview();
        RefreshIgnoreCurveButton();
        var reset = CreateChromeButton(UiStrings.LabelPitchCurveReset);
        reset.Click += (_, _) =>
        {
            StopPreview();
            CaptureBeforePointEdit();
            _curve.SetConstant(0);
            InvalidatePitchRenderCache();
            WriteFlatBox(0);
            _preview.InvalidateCurve();
        };

        // 上段: ピッチ → ポイント/ブラシ → スナップ類 → カーブ形状。
        var optionsRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 8),
        };
        var pitchAccessLabel = CreateAccessLabel(UiStrings.LabelPitchCurvePitchValue, _flatBox);
        pitchAccessLabel.Margin = new Thickness(0, 0, 4, 0);
        optionsRow.Children.Add(pitchAccessLabel);
        optionsRow.Children.Add(_flatBox);
        optionsRow.Children.Add(CreateLabel(UiStrings.LabelSemitone, margin: new Thickness(6, 0, 0, 0)));
        optionsRow.Children.Add(_pointsRadio);
        optionsRow.Children.Add(_brushRadio);
        optionsRow.Children.Add(_snapScaleBox);
        optionsRow.Children.Add(_noTimeStretchBox);
        optionsRow.Children.Add(_lightweightBox);
        optionsRow.Children.Add(CreateLabel(
            UiStrings.LabelPitchCurveShape,
            margin: new Thickness(16, 0, 6, 0)));
        optionsRow.Children.Add(_shapeButtons);
        // 初期ツールはポイント。音階スナップ設定も反映して形状の有効／無効を揃える。
        RefreshPointModeControls();

        // 下段: 塊の左＝追加・リセット・再生・無視再生／塊の右＝OK・Cancel。
        // 再生セット前だけ少し空け、OK/Cancel は塊幅の右端（窓端ではない）。
        _playButton.Margin = new Thickness(16, 0, 8, 0);
        var leftActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        leftActions.Children.Add(_addPointButton);
        leftActions.Children.Add(reset);
        leftActions.Children.Add(_playButton);
        leftActions.Children.Add(_ignoreCurveButton);

        var rightActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        rightActions.Children.Add(CreateButton(UiStrings.ButtonOk, accept: true, accent: true, isDefault: true));
        rightActions.Children.Add(CreateButton(UiStrings.ButtonCancel, accept: false, isCancel: true));

        var actionRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(leftActions, 0);
        Grid.SetColumn(rightActions, 2);
        actionRow.Children.Add(leftActions);
        actionRow.Children.Add(rightActions);

        // 下部 UI 全体を一塊にしてウィンドウ中央へ。塊幅は上段（オプション行）に合わせる。
        // 下端の「0 半音」等のステータス行は出さない。
        var settings = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        settings.Children.Add(optionsRow);
        settings.Children.Add(actionRow);

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(settings, Dock.Bottom);
        root.Children.Add(settings);
        root.Children.Add(_preview);
        _contentBorder = new Border { Padding = DesignMetrics.AudioPad, Child = root };
        _contentHost = new Grid();
        _contentHost.Children.Add(_contentBorder);
        Content = _contentHost;
        SizeChanged += (_, _) =>
        {
            if (_busyGlass.IsShowingBusy)
            {
                _busyGlass.SyncBounds(GetBusyGlassCoverBounds());
            }
        };

        _playheadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _playheadTimer.Tick += (_, _) =>
        {
            if (!_previewPlaying)
            {
                return;
            }

            // タイムストレッチなしだとプレビュー長≠元レンジ長。進捗を元波形へ比例対応させる。
            _preview.PlayheadFrame = MapPreviewCursorToSourceFrame(_playback.CursorFrame());
            // 開始直後の Stop イベント誤爆を避ける（本体の effect preview と同じ 250ms）。
            if (Environment.TickCount64 - _previewStartedAt >= 250 && !_playback.IsPlaying())
            {
                StopPreview();
            }
        };

        PreviewKeyDown += OnPreviewKeyDown;
        PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Left or Key.Right)
            {
                _preview.StopTimeNudge();
            }

            // ↑↓ 押し続けの半音移動も 1 ジェスチャ 1 undo。
            if (key is Key.Up or Key.Down)
            {
                _preview.ResetPointEditCapture();
            }
        };
        WindowPaintReveal.Attach(this);
        AppDialogKeys.Attach(this, () => Close());
        Closing += (_, _) =>
        {
            _preview.StopTimeNudge();
            StopPreview(cancelRender: true);
            HidePitchBusyGlass(fade: false);
            // ウィンドウを閉じたら編集履歴は捨てる（次回は空から）。
            ClearPointEditHistory();
            InvalidatePitchRenderCache();
            if (ActualWidth > 1 && ActualHeight > 1)
            {
                WindowPlacement.CapturePitchCurve(this, AppStorage.Settings);
            }

            AppStorage.Save();
        };
        Loaded += (_, _) =>
        {
            SizeToContent = SizeToContent.Manual;
            // 旧最小 860 の半分まで縮められるようにする。
            var minWindowHeight = DesignMetrics.From96(430);
            var contentHeight = ActualHeight;
            if (contentHeight > 0)
            {
                var previewHeight = _preview.ActualHeight;
                MinHeight = Math.Max(
                    minWindowHeight,
                    Math.Max(
                        _preview.MinHeight,
                        contentHeight - previewHeight + _preview.MinHeight));
            }
            else
            {
                MinHeight = minWindowHeight;
            }

            // 下部 UI（オプション行／左右ボタン）が見切れない幅を最小幅にする。
            optionsRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            leftActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            rightActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var bottomContentW = Math.Max(
                optionsRow.DesiredSize.Width,
                leftActions.DesiredSize.Width + rightActions.DesiredSize.Width + DesignMetrics.From96(16));
            var padW = DesignMetrics.AudioPad.Left + DesignMetrics.AudioPad.Right;
            UpdateLayout();
            var frameW = Math.Max(0, ActualWidth - Math.Max(1, _contentHost.ActualWidth));
            MinWidth = Math.Ceiling(bottomContentW + padW + frameW);

            _preview.ClearValue(FrameworkElement.HeightProperty);
            if (!WindowPlacement.TryApplyPitchCurve(this, AppStorage.Settings))
            {
                Width = Math.Max(DesignMetrics.From96(1080), MinWidth);
                Height = DesignMetrics.From96(900);
                UpdateLayout();
                WindowPlacement.CenterOnContainingMonitor(this, Owner);
            }

            if (Width < MinWidth)
            {
                Width = MinWidth;
            }

            if (Height < MinHeight)
            {
                Height = MinHeight;
            }

            SetTool(EditTool.Points);
            SetSnapToScale(_snapScaleBox.IsChecked == true, persist: false);
            _flatBox.Focus();
            _flatBox.SelectAll();
        };

        Result = new PitchCurveEditorDialogResult(
            false,
            _curve.Clone(),
            _range,
            1f,
            ClickGuard.DefaultFadeMilliseconds,
            TimeStretch: true);
    }

    private bool WantsTimeStretch() => _noTimeStretchBox.IsChecked != true;

    private bool WantsLightweightPreview() => _lightweightBox.IsChecked == true;

    private void SetNoTimeStretch(bool enabled)
    {
        AppStorage.Settings.PitchCurveNoTimeStretch = enabled;
        InvalidatePitchRenderCache();
    }

    private void SetLightweightMode(bool enabled)
    {
        AppStorage.Settings.PitchCurveLightweightMode = enabled;
        InvalidatePitchRenderCache();
    }

    private void InvalidatePitchRenderCache()
    {
        _cachedAfterRange = null;
        _cachedPeak = 0f;
        _cacheKey = null;
    }

    private string BuildPitchRenderCacheKey(bool timeStretch, bool lightweight)
    {
        var sb = new StringBuilder(256);
        sb.Append(timeStretch ? '1' : '0').Append('|')
            .Append(lightweight ? 'L' : 'Q').Append('|')
            .Append(_snapScaleBox.IsChecked == true ? 'S' : 'C').Append('|')
            .Append(_channelMask).Append('|')
            .Append(_range.StartFrame).Append('-').Append(_range.Length).Append('|')
            .Append(_document.SampleRate).Append('|')
            .Append(ClickGuard.DefaultFadeMilliseconds).Append('|')
            .Append((int)_curve.DefaultShape).Append('|');
        foreach (var value in _curve.ToPackedSamples())
        {
            sb.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(',');
        }

        sb.Append('|');
        foreach (var id in _curve.ToShapeIds())
        {
            sb.Append(id).Append(',');
        }

        return sb.ToString();
    }

    private Rect GetBusyGlassCoverBounds() =>
        new(0, 0, Math.Max(0, _contentHost.ActualWidth), Math.Max(0, _contentHost.ActualHeight));

    private void ShowPitchBusyGlass()
    {
        _contentHost.UpdateLayout();
        _busyGlass.ShowOverlay(
            _contentHost,
            _contentBorder,
            GetBusyGlassCoverBounds(),
            UiStrings.OverlayPitchShift);
    }

    private void HidePitchBusyGlass(bool fade)
    {
        if (!_busyGlass.IsShowingBusy && _busyGlass.Visibility != Visibility.Visible)
        {
            return;
        }

        if (fade)
        {
            _busyGlass.BeginFadeOut();
        }
        else
        {
            _busyGlass.HideOverlay();
        }
    }

    /// <summary>
    /// ピッチ処理結果を取得。キャッシュが効けば再利用、なければ計算してキャッシュする。
    /// <paramref name="forPreview"/> が true かつ軽量モード ON のときだけ軽量演算。
    /// OK 本適用は forPreview=false で常に通常品質（軽量キャッシュは使わない）。
    /// </summary>
    private async Task<(float[] AfterRange, float Peak)?> EnsurePitchRenderAsync(
        CancellationToken cancellationToken,
        bool forPreview)
    {
        var timeStretch = WantsTimeStretch();
        var lightweight = forPreview && WantsLightweightPreview();
        var key = BuildPitchRenderCacheKey(timeStretch, lightweight);
        if (_cachedAfterRange is not null && _cacheKey == key)
        {
            return (_cachedAfterRange, _cachedPeak);
        }

        ShowPitchBusyGlass();
        _rendering = true;
        Exception? error = null;
        PitchCurveRenderResult rendered = default;
        try
        {
            var curve = _curve.Clone();
            var document = _document;
            var range = _range;
            var mask = _channelMask;
            var edgeFadeMs = ClickGuard.DefaultFadeMilliseconds;
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            rendered = await Task.Run(
                    () => ProcessEdits.RenderPitchCurveRange(
                        document,
                        range,
                        curve,
                        mask,
                        timeStretch,
                        edgeFadeMs,
                        progress,
                        lightweight),
                    cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            _cachedAfterRange = rendered.AfterRange;
            _cachedPeak = rendered.PeakAbs;
            _cacheKey = key;
            return (_cachedAfterRange, _cachedPeak);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            error = ex;
            InvalidatePitchRenderCache();
            return null;
        }
        finally
        {
            _rendering = false;
            HidePitchBusyGlass(fade: error is null);
            if (error is not null)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    error.Message,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var focused = Keyboard.FocusedElement;
        var modifiers = Keyboard.Modifiers;

        // Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y: ピッチ編集の undo/redo（ウィンドウ内のみ）。
        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            TryUndoPointEdit();
            return;
        }

        if (key == Key.Y && modifiers == ModifierKeys.Control
            || key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            e.Handled = true;
            TryRedoPointEdit();
            return;
        }

        // ボタン上の Space は Click と二重になるのでここでは扱わない。
        if (key == Key.Space
            && modifiers == ModifierKeys.None
            && focused is not TextBox
            && focused is not Button)
        {
            e.Handled = true;
            TogglePreview();
            return;
        }

        if (key == Key.Delete && _preview.Tool == PitchCurveWavePreview.EditTool.Points)
        {
            if (_preview.TryDeleteSelectedPoint())
            {
                e.Handled = true;
            }

            return;
        }

        // Alt+A: ポイントモード時のみ（ブラシ中はボタンと同様に無効）。
        if (key == Key.A
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            e.Handled = true;
            if (_addPointButton.IsEnabled)
            {
                AddPointBesideSelection();
            }

            return;
        }

        // Alt+P: 再生／停止トグル（ポイントは Alt+I。数値欄フォーカス中でも効かせる）。
        if (key == Key.P
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            e.Handled = true;
            TogglePreview();
            return;
        }

        // Alt+L: 軽量演算モードのオン／オフ（数値欄フォーカス中でも効かせる）。
        if (key == Key.L
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            e.Handled = true;
            _lightweightBox.IsChecked = _lightweightBox.IsChecked != true;
            return;
        }

        // Alt+S: 音階スナップ（数値欄フォーカス中でも効かせる。Alt+N と競合しない）。
        if (key == Key.S
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            e.Handled = true;
            _snapScaleBox.IsChecked = _snapScaleBox.IsChecked != true;
            return;
        }

        // Alt+N: ピッチ数値欄へフォーカス（スナップは Alt+S）。
        if (key == Key.N
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            e.Handled = true;
            _flatBox.Focus();
            _flatBox.SelectAll();
            return;
        }

        if (focused is TextBox)
        {
            return;
        }

        // ポイント選択中: メイン／テンキー 1–9 でカーブ形状（並びはフェードと同じ、1 始まり）。
        if (modifiers == ModifierKeys.None
            && _preview.SelectedPointIndex >= 0
            && _shapeButtons.IsEnabled
            && TryApplyShapeFromDigitKey(key))
        {
            e.Handled = true;
            return;
        }

        // 選択中ポイント: ↑↓＝±1 半音、Shift+↑↓＝±12（1 オクターブ）。未選択時は拡縮キーへ。
        if (key is Key.Up or Key.Down
            && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
        {
            var step = modifiers == ModifierKeys.Shift ? 12f : 1f;
            var delta = key == Key.Up ? step : -step;
            if (_preview.TryNudgeSelectedSemitones(delta))
            {
                e.Handled = true;
                return;
            }
        }

        // Alt+←→：前後のポイントへ選択移動。
        if (key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            if (_preview.TrySelectAdjacentPoint(key == Key.Left ? -1 : 1))
            {
                e.Handled = true;
                SyncShapeButtonsToSelection();
                return;
            }
        }

        // ←→：選択中はポイントの時間移動（1px／Shift=3px）、未選択は表示パン。
        // Shift 押下で OS リピートが止まっても、タイマーで押し続け移動を継続する。
        if (key is Key.Left or Key.Right
            && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
        {
            if (_preview.BeginOrContinueTimeNudge(key == Key.Left ? -1 : 1))
            {
                e.Handled = true;
                return;
            }
        }

        if (_preview.TryHandleZoomKey(key, Keyboard.Modifiers))
        {
            e.Handled = true;
        }
    }

    private void SetTool(EditTool tool)
    {
        _preview.Tool = tool == EditTool.Brush
            ? PitchCurveWavePreview.EditTool.Brush
            : PitchCurveWavePreview.EditTool.Points;
        RefreshPointModeControls();
    }

    /// <summary>
    /// ポイント追加・カーブ形状はポイント編集モード専用。
    /// ブラシ中は無効。音階スナップ中は形状のみ従来どおり無効。
    /// </summary>
    private void RefreshPointModeControls()
    {
        var pointsMode = _preview.Tool == PitchCurveWavePreview.EditTool.Points;
        _addPointButton.IsEnabled = pointsMode;
        _shapeButtons.IsEnabled = pointsMode && _snapScaleBox.IsChecked != true;
    }

    private void AddPointBesideSelection()
    {
        // Alt+A / ボタン共通。ブラシ中は無効（モード切替で勝手に追加しない）。
        if (_preview.Tool != PitchCurveWavePreview.EditTool.Points)
        {
            return;
        }

        // 追加ポイントを選択し、フォーカスは数値欄ではなくプレビューへ。
        EnsurePointsToolSelected();
        CaptureBeforePointEdit();
        if (!_preview.TryAddPointBesideSelection(out var index))
        {
            // 追加失敗時は積んだ履歴を戻す。
            if (_undoStack.Count > 0)
            {
                _undoStack.RemoveAt(_undoStack.Count - 1);
            }

            return;
        }

        AffirmAddedPointSelection(index);
        // クリック／Alt 処理やラジオのフォーカス移動の後に、数値欄へ戻らないよう再確定する。
        Dispatcher.BeginInvoke(
            () => AffirmAddedPointSelection(index),
            DispatcherPriority.Input);
    }

    /// <summary>
    /// ピッチ編集の直前スナップショット。
    /// 対象: ポイント追加・移動・半音変更・カーブ形状選択。ズーム／再生等は対象外。
    /// </summary>
    private void CaptureBeforePointEdit()
    {
        _undoStack.Add(new PitchCurveEditSnapshot(_curve.Clone(), _preview.SelectedPointIndex));
        if (_undoStack.Count > MaxCurveEditUndo)
        {
            _undoStack.RemoveAt(0);
        }

        _redoStack.Clear();
    }

    /// <summary>キャプチャ直後に編集が起きなかったとき、空の履歴を捨てる。</summary>
    private void DiscardLastPointEditCapture()
    {
        if (_undoStack.Count > 0)
        {
            _undoStack.RemoveAt(_undoStack.Count - 1);
        }
    }

    private void ClearPointEditHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _preview.ResetPointEditCapture();
    }

    private void TryUndoPointEdit()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var current = new PitchCurveEditSnapshot(_curve.Clone(), _preview.SelectedPointIndex);
        var previous = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(current);
        ApplyPointEditSnapshot(previous);
    }

    private void TryRedoPointEdit()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var current = new PitchCurveEditSnapshot(_curve.Clone(), _preview.SelectedPointIndex);
        var next = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(current);
        ApplyPointEditSnapshot(next);
    }

    private void ApplyPointEditSnapshot(PitchCurveEditSnapshot snapshot)
    {
        _curve.CopyFrom(snapshot.Curve);
        if (snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < _curve.Points.Count)
        {
            _preview.SelectPoint(snapshot.SelectedIndex);
        }
        else
        {
            _preview.ClearPointSelection();
        }

        _preview.InvalidateCurve();
        SyncShapeButtonsToSelection();
        RefreshShapeButtonIcons(force: true);
    }

    private void EnsurePointsToolSelected()
    {
        // IsChecked でラジオにフォーカスが吸われないようにする。
        var focusable = _pointsRadio.Focusable;
        _pointsRadio.Focusable = false;
        _pointsRadio.IsChecked = true;
        _pointsRadio.Focusable = focusable;
        SetTool(EditTool.Points);
    }

    private void AffirmAddedPointSelection(int index)
    {
        EnsurePointsToolSelected();
        _preview.SelectPoint(index);
        SyncShapeButtonsToSelection();
        // 数値エディタではなく、追加したポイントをキー操作できるプレビューをアクティブに。
        _preview.Focus();
        Keyboard.Focus(_preview);
    }

    private void SetSnapToScale(bool enabled, bool persist = true)
    {
        // 再生中／再計算中に押されたら、先に止めてからスナップ→再計算（再生側から来た場合も同じ）。
        var wasPlayingOrRendering = _previewPlaying || _playback.IsPlaying() || _rendering;
        if (wasPlayingOrRendering)
        {
            _ = RestartSnapThenRecalculateAsync(enabled, persist, thenPlay: false);
            return;
        }

        ApplySnapToScaleState(enabled, persist);
    }

    /// <summary>
    /// 停止 → スナップ適用 → 再計算。再生中のスナップ／スナップ再計算中の再生の両方向で使う。
    /// </summary>
    private async Task RestartSnapThenRecalculateAsync(bool snapEnabled, bool persist, bool thenPlay)
    {
        if (_accepting)
        {
            return;
        }

        var op = ++_pitchOpEpoch;
        StopPreview(cancelRender: true);
        await WaitUntilNotRenderingAsync().ConfigureAwait(true);
        if (op != _pitchOpEpoch || !IsLoaded || _accepting)
        {
            return;
        }

        ApplySnapToScaleState(snapEnabled, persist);

        if (thenPlay)
        {
            StartPreview(ignoreCurve: _ignorePitchCurve);
            return;
        }

        if (_curve.IsNoOp())
        {
            return;
        }

        _renderCts?.Cancel();
        _renderCts?.Dispose();
        _renderCts = new CancellationTokenSource();
        await EnsurePitchRenderAsync(_renderCts.Token, forPreview: true).ConfigureAwait(true);
    }

    private void ApplySnapToScaleState(bool enabled, bool persist)
    {
        _preview.SnapToScale = enabled;
        RefreshPointModeControls();
        if (enabled)
        {
            _curve.QuantizeToScaleSteps();
            // Constant はコンボに無い。表示は無効化済みなので触らず、補間だけ階段にする。
        }
        else
        {
            // ON 中に付けた Constant（階段）が残ると、OFF でも音階スナップしたように聞こえる／見える。
            RestoreContinuousShapes();
        }

        // 量子化で定数になったのに数値欄が古い 0 のままだと、再生時 ApplyFlatFromBox がカーブを潰す。
        SyncFlatBoxFromCurveIfConstant();
        InvalidatePitchRenderCache();
        _preview.InvalidateCurve();

        if (persist)
        {
            AppStorage.Settings.PitchCurveSnapToScale = enabled;
        }
    }

    /// <summary>定数カーブのときだけ数値欄をカーブに合わせる（逆方向で点を潰さない）。</summary>
    private void SyncFlatBoxFromCurveIfConstant()
    {
        if (_curve.IsConstant(out var semi))
        {
            WriteFlatBox(semi);
        }
    }

    private async Task WaitUntilNotRenderingAsync()
    {
        for (var i = 0; i < 6000 && _rendering; i++)
        {
            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    /// <summary>音階スナップ解除時: 階段補間をやめ、保存済みの連続形状へ戻す。</summary>
    private void RestoreContinuousShapes()
    {
        var restored = FadeCurves.ParseStored(AppStorage.Settings.PitchCurveDefaultShape);
        if (restored == FadeShape.Constant)
        {
            restored = FadeCurves.Default;
        }

        _curve.DefaultShape = restored;
        _curve.SetAllSegmentShapes(restored);
        SelectShape(restored);
    }

    private void OnCurveChanged()
    {
        InvalidatePitchRenderCache();
        if (_curve.IsConstant(out var semi))
        {
            // ブラシの無段階値はそのまま。数値欄からの適用時だけ半音スナップ。
            _flatBox.Text = Math.Abs(semi - MathF.Round(semi)) < 1e-3f
                ? PitchCurve.SnapSemitone(semi).ToString("+0;-0;0", CultureInfo.InvariantCulture)
                : semi.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture);
        }
        SyncShapeButtonsToSelection();
        RefreshShapeButtonIcons();
    }

    private void NudgeFlat(int direction)
    {
        if (direction == 0)
        {
            return;
        }

        CommitFlatBox();
        if (!TryParseFlatSemitone(_flatBox.Text, out var value))
        {
            value = 0;
        }

        var next = PitchCurve.SnapSemitone(value + direction);
        if (Math.Abs(next - value) < 1e-6f)
        {
            return;
        }

        CaptureBeforePointEdit();
        WriteFlatBox(next);
        _curve.SetConstant(next);
        InvalidatePitchRenderCache();
        _preview.InvalidateCurve();
    }

    private void ApplyFlatFromBox()
    {
        CommitFlatBox();
        if (!TryParseFlatSemitone(_flatBox.Text, out var value))
        {
            return;
        }

        // ポイント編集中は数値欄で全体を平坦化しない（再生／OK 前の同期で点が消えないようにする）。
        if (!_curve.IsConstant(out var current))
        {
            return;
        }

        if (Math.Abs(current - value) < 1e-6f)
        {
            return;
        }

        CaptureBeforePointEdit();
        _curve.SetConstant(value);
        InvalidatePitchRenderCache();
        _preview.InvalidateCurve();
    }

    private void CommitFlatBox()
    {
        if (!TryParseFlatSemitone(_flatBox.Text, out var value))
        {
            WriteFlatBox(0);
            return;
        }

        WriteFlatBox(value);
    }

    private void WriteFlatBox(float value) =>
        _flatBox.Text = PitchCurve.SnapSemitone(value).ToString("+0;-0;0", CultureInfo.InvariantCulture);

    /// <summary>数値入力は半音整数にスナップ。</summary>
    private static bool TryParseFlatSemitone(string? text, out float value)
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

    private void SyncShapeButtonsToSelection()
    {
        var index = _preview.SelectedPointIndex;
        var shape = index >= 0
            ? _curve.OutgoingShapeOfPoint(index)
            : _curve.DefaultShape;
        SelectShape(shape);
    }

    private void ApplyShape(FadeShape shape)
    {
        if (_updatingShapeButtons)
        {
            return;
        }

        if (shape == FadeShape.Constant)
        {
            shape = FadeCurves.Default;
        }

        // 選択中ポイントの出セグメントだけ変更。未選択時は新規点の既定形状。
        // カーブ選択もピッチ編集履歴の対象（Ctrl+Z / Ctrl+Y）。
        if (_preview.SelectedPointIndex >= 0)
        {
            if (_curve.OutgoingShapeOfPoint(_preview.SelectedPointIndex) == shape)
            {
                SelectShape(shape);
                return;
            }

            CaptureBeforePointEdit();
            _curve.SetPointOutgoingShape(_preview.SelectedPointIndex, shape);
            InvalidatePitchRenderCache();
        }
        else
        {
            if (_curve.DefaultShape == shape)
            {
                SelectShape(shape);
                return;
            }

            CaptureBeforePointEdit();
            _curve.DefaultShape = shape;
            AppStorage.Settings.PitchCurveDefaultShape = _curve.DefaultShape.ToString();
            InvalidatePitchRenderCache();
        }

        SelectShape(shape);
        _preview.InvalidateCurve();
    }

    private void SelectShape(FadeShape shape)
    {
        if (shape == FadeShape.Constant)
        {
            shape = FadeCurves.Default;
        }

        _updatingShapeButtons = true;
        try
        {
            _selectedShape = shape;
            foreach (var child in _shapeButtons.Children)
            {
                if (!TryGetShapeChoiceBorder(child, out var border, out var buttonShape))
                {
                    continue;
                }

                FadeCurveIcons.ApplyChoiceButtonChrome(border, buttonShape == shape);
            }

            RefreshShapeButtonIcons();
        }
        finally
        {
            _updatingShapeButtons = false;
        }
    }

    /// <summary>
    /// 選択点と次点のピッチ高低で、形状アイコンを上昇／下降向きに揃える。
    /// （見た目と実際の補間が逆にならないようにする。）
    /// </summary>
    private void RefreshShapeButtonIcons(bool force = false)
    {
        var rising = ResolveShapeIconRising();
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

            image.Source = FadeCurveIcons.Create(
                buttonShape,
                isFadeIn: rising,
                FadeCurveIcons.ChoiceIconPixelSize);
        }
    }

    private bool ResolveShapeIconRising()
    {
        var points = _curve.Points;
        var index = _preview.SelectedPointIndex;
        if (index >= 0 && index + 1 < points.Count)
        {
            // 今 → 次。次の方が高ければ上昇アイコン。
            return points[index + 1].Semitones >= points[index].Semitones;
        }

        if (index > 0 && index < points.Count)
        {
            // 最終点は手前 → 今の向きで代用。
            return points[index].Semitones >= points[index - 1].Semitones;
        }

        return true;
    }

    private static bool TryGetShapeChoiceBorder(
        object child,
        out Border border,
        out FadeShape shape)
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

    private bool TryApplyShapeFromDigitKey(Key key)
    {
        if (!FadeCurveIcons.TryShapeFromDigitKey(key, isFadeIn: true, out var shape))
        {
            return false;
        }

        ApplyShape(shape);
        return true;
    }

    /// <summary>クロスフェードと同じボタン。1 始まり番号をアイコン左上に重ねる。</summary>
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

            ApplyShape(shape);
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

    private void TogglePreview()
    {
        if (_accepting)
        {
            return;
        }

        // 再生中 → 停止。
        if (_previewPlaying || _playback.IsPlaying())
        {
            StopPreview(cancelRender: true);
            return;
        }

        // スナップ再計算中に再生 → 停止→スナップやり直し→再計算→再生（逆方向と同じ流れ）。
        if (_rendering)
        {
            var snapOn = _snapScaleBox.IsChecked == true;
            _ = RestartSnapThenRecalculateAsync(snapOn, persist: false, thenPlay: true);
            return;
        }

        StartPreview(ignoreCurve: _ignorePitchCurve);
    }

    /// <summary>
    /// 「ピッチカーブを無視して再生」: モード切替＋押した時点で元波形再生（または停止）。
    /// </summary>
    private void ToggleIgnoreCurvePreview()
    {
        if (_previewPlaying || _playback.IsPlaying())
        {
            // 無視モードで再生中なら停止。通常再生中なら無視モードへ切り替えて聞き直し。
            if (_ignorePitchCurve)
            {
                _ignorePitchCurve = false;
                RefreshIgnoreCurveButton();
                StopPreview(cancelRender: true);
                return;
            }

            _ignorePitchCurve = true;
            RefreshIgnoreCurveButton();
            StartPreview(ignoreCurve: true);
            return;
        }

        _ignorePitchCurve = !_ignorePitchCurve;
        RefreshIgnoreCurveButton();
        if (_ignorePitchCurve)
        {
            StartPreview(ignoreCurve: true);
        }
    }

    private async void StartPreview(bool ignoreCurve)
    {
        if (_accepting)
        {
            return;
        }

        var op = ++_pitchOpEpoch;
        try
        {
            StopPreview(cancelRender: true);
            await WaitUntilNotRenderingAsync().ConfigureAwait(true);
            if (op != _pitchOpEpoch || !IsLoaded || _accepting)
            {
                return;
            }

            // 数値欄にフォーカスがあるときだけ欄→カーブ。それ以外はカーブ→欄（古い 0 で点を潰さない）。
            if (_flatBox.IsKeyboardFocusWithin && _curve.IsConstant(out _))
            {
                ApplyFlatFromBox();
            }
            else
            {
                SyncFlatBoxFromCurveIfConstant();
            }

            _ignorePitchCurve = ignoreCurve;
            RefreshIgnoreCurveButton();
            RefreshPlayButton(playing: true);

            float[] pitched;
            if (ignoreCurve || _curve.IsNoOp())
            {
                // 無視モード／平坦 0: 元波形のまま再生（ピッチキャッシュは触らない）。
                pitched = _document.CopyRange(_range.StartFrame, _range.Length);
            }
            else
            {
                _renderCts?.Cancel();
                _renderCts?.Dispose();
                _renderCts = new CancellationTokenSource();
                var token = _renderCts.Token;
                var rendered = await EnsurePitchRenderAsync(token, forPreview: true).ConfigureAwait(true);
                if (op != _pitchOpEpoch
                    || rendered is null
                    || token.IsCancellationRequested
                    || !IsLoaded)
                {
                    RefreshPlayButton(playing: false);
                    return;
                }

                pitched = rendered.Value.AfterRange;
                // 空バッファは「計算スキップ」扱いにして元音を誤再生しない。
                if (pitched.Length < _document.Channels)
                {
                    RefreshPlayButton(playing: false);
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorPitchShiftFailed,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }
            }

            if (op != _pitchOpEpoch || !IsLoaded)
            {
                RefreshPlayButton(playing: false);
                return;
            }

            var previewDoc = new AudioDocument(
                pitched,
                _document.SampleRate,
                _document.Channels,
                _document.BitsPerSample,
                _document.SourceKind,
                null);
            var previewRange = new WaveSelection(0, previewDoc.FrameCount);
            _previewPlaybackFrames = Math.Max(1, previewDoc.FrameCount);
            _previewToSourceMap = BuildPreviewToSourceMap(pitched, ignoreCurve);
            // 本体プレイヤー経由（旧 P ピッカーと同じ）。別 AudioPlayer は ASIO で鳴らない。
            _playback.Play(previewDoc, previewRange, _range.StartFrame);
            _previewPlaying = true;
            _previewStartedAt = Environment.TickCount64;
            _preview.PlayheadFrame = _range.StartFrame;
            _playheadTimer.Start();
            RefreshPlayButton(playing: true);
        }
        catch (Exception ex)
        {
            StopPreview(cancelRender: true);
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// プレビュー再生カーソルを元レンジ上のフレームへ写す。
    /// 長さ据え置き時は 1:1。タイムストレッチなしは速度式の読み位置マップ（無ければ比例）。
    /// </summary>
    private long MapPreviewCursorToSourceFrame(long previewCursorFrame)
    {
        var rangeLength = Math.Max(1L, _range.Length);
        if (_previewToSourceMap is { Length: > 0 } map)
        {
            var index = (int)Math.Clamp(previewCursorFrame, 0, map.Length - 1);
            var offset = Math.Clamp(map[index], 0, (int)Math.Min(int.MaxValue, rangeLength - 1));
            return _range.StartFrame + offset;
        }

        var previewFrames = Math.Max(1L, _previewPlaybackFrames);
        if (previewFrames == rangeLength)
        {
            return _range.StartFrame + Math.Clamp(previewCursorFrame, 0, rangeLength);
        }

        var t = Math.Clamp(previewCursorFrame / (double)previewFrames, 0d, 1d);
        return _range.StartFrame + (long)Math.Floor(t * rangeLength);
    }

    /// <summary>タイムストレッチなしのときだけ、ApplyCurve と同じ読み進めの対応表を作る。</summary>
    private int[]? BuildPreviewToSourceMap(float[] pitched, bool ignoreCurve)
    {
        if (ignoreCurve || WantsTimeStretch() || _curve.IsNoOp() || _range.IsEmpty)
        {
            return null;
        }

        var sourceFrames = (int)Math.Min(int.MaxValue, Math.Max(1, _range.Length));
        var previewFrames = pitched.Length / Math.Max(1, _document.Channels);
        if (previewFrames <= 0)
        {
            return null;
        }

        // 長さ据え置きと同じ尺なら 1:1（マップ不要）。
        if (previewFrames == sourceFrames)
        {
            return null;
        }

        var table = _curve.BuildSemitoneTable(sourceFrames);
        // 軽量プレビューの非ストレッチは平滑化なし。通常品質は平滑化あり。
        var smooth = !WantsLightweightPreview();
        var map = PitchShift.BuildNoTimeStretchSourceMap(table, _document.SampleRate, smooth);
        if (map.Length != previewFrames)
        {
            // 実バッファ長と理論長がずれたときは比例へ退行。
            return null;
        }

        return map;
    }

    private void StopPreview(bool cancelRender = false)
    {
        if (cancelRender)
        {
            _renderCts?.Cancel();
        }

        _playheadTimer.Stop();
        var wasPlaying = _previewPlaying;
        _previewPlaying = false;
        _previewStartedAt = 0;
        _previewPlaybackFrames = 0;
        _previewToSourceMap = null;
        _preview.PlayheadFrame = -1;
        if (wasPlaying || _playback.IsPlaying())
        {
            try
            {
                _playback.Stop();
            }
            catch
            {
                // ignore
            }
        }

        // 末尾自動停止・手動停止どちらでも、無視再生の青（アクティブ）を通常に戻す。
        _ignorePitchCurve = false;
        RefreshPlayButton(playing: false);
        RefreshIgnoreCurveButton();
    }

    private void RefreshPlayButton(bool playing)
    {
        _playButton.Content = playing
            ? UiStrings.LabelPitchCurveStop
            : UiStrings.LabelPitchCurvePlay;
        if (playing)
        {
            ActionButtonLooks.ApplyAccent(_playButton);
        }
        else
        {
            ActionButtonLooks.ApplyClear(_playButton);
        }
    }

    private void RefreshIgnoreCurveButton()
    {
        if (_ignorePitchCurve)
        {
            ActionButtonLooks.ApplyAccent(_ignoreCurveButton);
        }
        else
        {
            ActionButtonLooks.ApplyClear(_ignoreCurveButton);
        }
    }

    private async void Accept()
    {
        if (_accepting || _rendering)
        {
            return;
        }

        _accepting = true;
        StopPreview(cancelRender: true);
        ApplyFlatFromBox();
        var timeStretch = WantsTimeStretch();
        if (_curve.IsNoOp())
        {
            Result = new PitchCurveEditorDialogResult(
                false,
                _curve.Clone(),
                _range,
                1f,
                ClickGuard.DefaultFadeMilliseconds,
                timeStretch);
            DialogResult = false;
            Close();
            return;
        }

        float gain = 1f;
        float[]? precomputed = null;
        try
        {
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            // OK 本適用・ピーク判定は常に通常品質（軽量プレビューキャッシュは使わない）。
            var rendered = await EnsurePitchRenderAsync(_renderCts.Token, forPreview: false).ConfigureAwait(true);
            if (rendered is null || !IsLoaded)
            {
                _accepting = false;
                return;
            }

            precomputed = rendered.Value.AfterRange;
            var peak = rendered.Value.Peak;
            if (peak > 1.001f)
            {
                var answer = OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.MessagePitchCurveClipAsk,
                    UiStrings.AppName,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel)
                {
                    _accepting = false;
                    return;
                }

                if (answer == MessageBoxResult.Yes)
                {
                    gain = Math.Clamp(0.99f / peak, 1e-4f, 1f);
                }
            }
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            _accepting = false;
            return;
        }

        Result = new PitchCurveEditorDialogResult(
            true,
            _curve.Clone(),
            _range,
            gain,
            ClickGuard.DefaultFadeMilliseconds,
            timeStretch,
            precomputed);
        DialogResult = true;
        Close();
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

    /// <summary>アクセスキー付きラベル。Target にフォーカスを移す（ピッチ数値欄など）。</summary>
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

    private const int GwlStyle = -16;
    private const int WsMinimizeBox = 0x00020000;
    private const int SwpFrameChanged = 0x0020;
    private const int SwpNoMove = 0x0002;
    private const int SwpNoSize = 0x0001;
    private const int SwpNoZOrder = 0x0004;
    private const int SwpNoActivate = 0x0010;

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

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        int uFlags);
}

/// <summary>一括編集向けの定数ピッチ入力。</summary>
internal static class PitchConstantBatchDialog
{
    public static bool TryPrompt(Window owner, out int semitones)
    {
        semitones = 0;
        var box = new TextBox
        {
            Text = "0",
            Width = DesignMetrics.From96(80),
            Margin = new Thickness(0, 8, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var chosen = 0;
        var ok = false;
        var window = new Window
        {
            Title = UiStrings.DialogPitchConstantTitle,
            WindowStyle = WindowStyle.SingleBorderWindow,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.WidthAndHeight,
            Background = (Brush)owner.FindResource("WindowBackBrush"),
            Foreground = (Brush)owner.FindResource("PrimaryForeBrush"),
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = new RoundedButton
        {
            Content = UiStrings.ButtonCancel,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
            IsCancel = true,
        };
        ActionButtonLooks.ApplyClear(cancel);
        cancel.Click += (_, _) => window.Close();
        var accept = new RoundedButton
        {
            Content = UiStrings.ButtonOk,
            MinWidth = DesignMetrics.ConfirmSaveButtonWidth,
            Height = DesignMetrics.AudioDialogButtonHeight,
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = true,
        };
        ActionButtonLooks.ApplyAccent(accept);
        accept.Click += (_, _) =>
        {
            if (!int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && !int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
            {
                return;
            }

            chosen = PitchShift.Snap(value);
            ok = !PitchShift.IsNoOp(chosen);
            window.DialogResult = ok;
            window.Close();
        };
        // 他ダイアログと同じ: OK → Cancel
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        var pitchLabel = new TextBlock { Text = UiStrings.LabelPitch };
        pitchLabel.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryForeBrush");
        if (owner.TryFindResource("DarkTextBoxStyle") is Style flatStyle)
        {
            box.Style = flatStyle;
        }

        var root = new StackPanel { Margin = DesignMetrics.AudioPad };
        root.Children.Add(pitchLabel);
        root.Children.Add(box);
        root.Children.Add(buttons);
        window.Content = root;
        AppDialogKeys.Attach(window, () => window.Close());
        WindowPaintReveal.ShowDialogWhenPainted(window);
        semitones = chosen;
        return ok;
    }
}

/// <summary>モノ相当波形＋赤いピッチカーブ。波形はエディタ本体と同じ PeakPyramid／輪郭接続／LOD／折れ線切替。</summary>
internal sealed class PitchCurveWavePreview : FrameworkElement
{
    internal enum EditTool
    {
        Brush,
        Points,
    }

    private const int RawColumnMaxSamplesPerPixel = 96;
    private const int RawColumnMaxFrames = 1 << 18;
    /// <summary>表示ピッチ範囲は上下 2 オクターブ（±24）。</summary>
    private const float PitchAxisSemitones = 24f;
    private const float ZeroSnapRadius = 0.35f;
    private const double KeyboardWidthDip = 40;
    /// <summary>左端の半音目盛り（"+24" 等）用ガター。</summary>
    private const double PitchLabelWidthDip = 40;
    /// <summary>上下の目盛り数字が ClipToBounds で切れない余白。</summary>
    private const double PitchAxisPadDip = 20;
    /// <summary>←→ 押し続け。メインのマーカナッジと同じ間隔。</summary>
    private const int TimeNudgeRepeatDelayMs = 400;
    private const int TimeNudgeRepeatIntervalMs = 50;

    private float[] _columnMins = [];
    private float[] _columnMaxs = [];
    private WriteableBitmap? _bitmap;
    private int _bitmapWidth;
    private int _bitmapHeight;
    private double _bitmapViewStart;
    private double _bitmapViewSpan;
    private float _bitmapGain;
    private bool _waveDirty = true;
    private AudioDocument? _document;
    private WaveSelection _range;
    private int _channelMask;
    private PitchCurve _curve = PitchCurve.Flat(0);
    private double _viewStart;
    private double _viewSpan = 1;
    private float _displayGain = 1;
    private double _pitchZoom = 1;
    private bool _snapToScale;
    private EditTool _tool = EditTool.Points;
    private bool _dragging;
    private int _dragPoint = -1;
    private long _playheadFrame = -1;
    private readonly DispatcherTimer _timeNudgeTimer;
    private int _timeNudgeDirection;
    private bool _timeNudgeRepeatStarted;
    /// <summary>ドラッグ／押し続け移動で、1 ジェスチャにつき undo を 1 回だけ積む。</summary>
    private bool _pointEditCapturedForGesture;

    private static readonly Pen CurvePen = CreateCurvePen();
    private static readonly Pen SemitoneGridPen = CreateSemitoneGridPen();
    private static readonly Pen OctaveGridPen = CreateOctaveGridPen();
    private static readonly Pen PlayheadPen = CreatePlayheadPen();
    private static readonly Pen SelectedPointPen = CreateSelectedPointPen();
    private static readonly Brush PointFill = CreatePointFill();

    public event Action? CurveChanged;

    public event Action? SelectionChanged;

    /// <summary>ポイント追加・移動・半音変更の直前。ダイアログ側が undo スナップショットを積む。</summary>
    public event Action? BeforePointEdit;

    /// <summary>キャプチャしたが実際のピッチ変更が無かったときの取り消し。</summary>
    public event Action? DiscardPointEditCapture;

    public int SelectedPointIndex => _dragPoint;

    public bool SnapToScale
    {
        get => _snapToScale;
        set
        {
            if (_snapToScale == value)
            {
                return;
            }

            _snapToScale = value;
            // Constant への切替／復元はダイアログ側 SetSnapToScale が担う。
            MarkWaveDirty();
            InvalidateVisual();
        }
    }

    private double KeyboardGutter =>
        _snapToScale ? DesignMetrics.From96(KeyboardWidthDip) : 0;

    private double PitchLabelGutter => DesignMetrics.From96(PitchLabelWidthDip);

    private double PitchAxisPad => DesignMetrics.From96(PitchAxisPadDip);

    /// <summary>鍵盤＋目盛り数字の左余白。</summary>
    private double LeftChrome => KeyboardGutter + PitchLabelGutter;

    public EditTool Tool
    {
        get => _tool;
        set
        {
            // ポイントは Points モード時のみ描画するため、切替のたびに再描画する。
            // 以前はブラシへ移るとき（選択解除時）だけ Invalidate しており、
            // Points に戻しても点が消えたままになることがあった。
            if (_tool == value)
            {
                return;
            }

            _tool = value;
            Cursor = value == EditTool.Brush ? Cursors.Pen : Cursors.Cross;
            if (value == EditTool.Brush && _dragPoint >= 0)
            {
                _dragPoint = -1;
                SelectionChanged?.Invoke();
            }

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
            InvalidateVisual();
        }
    }

    public PitchCurve Curve
    {
        get => _curve;
        set
        {
            _curve = value;
            InvalidateVisual();
        }
    }

    public long PlayheadFrame
    {
        get => _playheadFrame;
        set
        {
            if (_playheadFrame == value)
            {
                return;
            }

            _playheadFrame = value;
            InvalidateVisual();
        }
    }

    public PitchCurveWavePreview()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        _timeNudgeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatDelayMs),
        };
        _timeNudgeTimer.Tick += (_, _) => OnTimeNudgeTick();
        PreviewMouseWheel += OnPreviewMouseWheel;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Handled)
            {
                return;
            }

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            // 数字キーはダイアログ側で処理（形状適用＋UI 同期）。ここでは拡縮・移動のみ。
            if (key is Key.Up or Key.Down
                && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                var step = Keyboard.Modifiers == ModifierKeys.Shift ? 12f : 1f;
                var delta = key == Key.Up ? step : -step;
                if (TryNudgeSelectedSemitones(delta))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.Alt)
            {
                StopTimeNudge();
                if (TrySelectAdjacentPoint(key == Key.Left ? -1 : 1))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (key is Key.Left or Key.Right
                && (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift))
            {
                if (BeginOrContinueTimeNudge(key == Key.Left ? -1 : 1))
                {
                    e.Handled = true;
                    return;
                }
            }

            if (TryHandleZoomKey(key, Keyboard.Modifiers))
            {
                e.Handled = true;
            }
        };
        PreviewKeyUp += (_, e) =>
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.Left or Key.Right)
            {
                StopTimeNudge();
            }
        };
        Unloaded += (_, _) => StopTimeNudge();
        LostKeyboardFocus += (_, _) => StopTimeNudge();
    }

    /// <summary>
    /// ←／→ の押し続け移動。Shift を後から押しても OS リピートが止まっても継続し、
    /// その時点の Shift 状態で 1px／3px を切り替える。
    /// ポイント移動の undo はジェスチャ開始時に 1 回だけ積む（キーリピートはまとめ）。
    /// </summary>
    public bool BeginOrContinueTimeNudge(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        // 同一方向の押し続け／OS リピート中は新規履歴を積まず、タイマー側で動かす。
        if (_timeNudgeDirection == direction && _timeNudgeTimer.IsEnabled)
        {
            return true;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_tool == EditTool.Points && _dragPoint >= 0)
        {
            // ポイント移動のみ履歴対象。端点で動けなければパン（パンは履歴なし）。
            if (!TryNudgeSelectedTimeWithUndo(direction, fast)
                && !TryPanTimeAxis(direction, fast))
            {
                return false;
            }
        }
        else if (!TryPanTimeAxis(direction, fast))
        {
            return false;
        }

        _timeNudgeDirection = direction;
        _timeNudgeRepeatStarted = false;
        _timeNudgeTimer.Stop();
        _timeNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatDelayMs);
        _timeNudgeTimer.Start();
        return true;
    }

    private void OnTimeNudgeTick()
    {
        if (_timeNudgeDirection == 0 || !IsTimeNudgeHeld(_timeNudgeDirection))
        {
            StopTimeNudge();
            return;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_tool == EditTool.Points && _dragPoint >= 0)
        {
            // リピート中は追加の undo を積まない。端点ならパンへ。
            if (!TryNudgeSelectedTime(_timeNudgeDirection, fast))
            {
                TryPanTimeAxis(_timeNudgeDirection, fast);
            }
        }
        else
        {
            TryPanTimeAxis(_timeNudgeDirection, fast);
        }

        if (_timeNudgeRepeatStarted)
        {
            return;
        }

        _timeNudgeRepeatStarted = true;
        _timeNudgeTimer.Stop();
        _timeNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatIntervalMs);
        _timeNudgeTimer.Start();
    }

    /// <summary>ポイント時間移動。ジェスチャ初回だけ undo を 1 つ積む。</summary>
    private bool TryNudgeSelectedTimeWithUndo(int direction, bool fast)
    {
        var alreadyCaptured = _pointEditCapturedForGesture;
        NotifyBeforePointEditOnce();
        if (TryNudgeSelectedTime(direction, fast))
        {
            return true;
        }

        // 端点などで動かなかった → 空履歴を捨ててジェスチャ未開始に戻す。
        if (!alreadyCaptured)
        {
            _pointEditCapturedForGesture = false;
            DiscardPointEditCapture?.Invoke();
        }

        return false;
    }

    private static bool IsTimeNudgeHeld(int direction)
    {
        var arrowDown = direction < 0
            ? Keyboard.IsKeyDown(Key.Left)
            : Keyboard.IsKeyDown(Key.Right);
        if (!arrowDown)
        {
            return false;
        }

        // Alt+←→ は隣接選択。時間移動の押し続けは Alt なしのときだけ。
        return (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
    }

    public void StopTimeNudge()
    {
        _timeNudgeDirection = 0;
        _timeNudgeRepeatStarted = false;
        _pointEditCapturedForGesture = false;
        _timeNudgeTimer.Stop();
        _timeNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatDelayMs);
    }

    public void ResetPointEditCapture() => _pointEditCapturedForGesture = false;

    public void ClearPointSelection()
    {
        if (_dragPoint < 0)
        {
            return;
        }

        _dragPoint = -1;
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    private void NotifyBeforePointEditOnce()
    {
        if (_pointEditCapturedForGesture)
        {
            return;
        }

        _pointEditCapturedForGesture = true;
        BeforePointEdit?.Invoke();
    }

    public void InvalidateCurve() => InvalidateVisual();

    public bool TryDeleteSelectedPoint()
    {
        if (_dragPoint < 0)
        {
            return false;
        }

        if (!_curve.RemovePoint(_dragPoint))
        {
            return false;
        }

        _dragPoint = -1;
        CurveChanged?.Invoke();
        SelectionChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// 選択中ポイントのちょっと隣に新規ポイントを追加して選択する。
    /// 未選択時は既存ポイント（いちばん広い隙間の左側）の隣へ追加する。
    /// </summary>
    public bool TryAddPointBesideSelection(out int newIndex)
    {
        newIndex = -1;
        var count = _curve.Points.Count;
        if (count < 2)
        {
            return false;
        }

        Tool = EditTool.Points;

        int leftIndex;
        int rightIndex;
        if (_dragPoint >= 0 && _dragPoint < count)
        {
            // 選択あり: 右側に隙間があればその隣、端なら左側の隣。
            if (_dragPoint < count - 1
                && _curve.Points[_dragPoint + 1].T - _curve.Points[_dragPoint].T > 2e-3)
            {
                leftIndex = _dragPoint;
                rightIndex = _dragPoint + 1;
            }
            else if (_dragPoint > 0
                && _curve.Points[_dragPoint].T - _curve.Points[_dragPoint - 1].T > 2e-3)
            {
                leftIndex = _dragPoint - 1;
                rightIndex = _dragPoint;
            }
            else
            {
                return false;
            }
        }
        else
        {
            // 未選択: いちばん広い隙間の左側ポイントの隣。
            leftIndex = 0;
            rightIndex = 1;
            var bestGap = -1d;
            for (var i = 0; i < count - 1; i++)
            {
                var segGap = _curve.Points[i + 1].T - _curve.Points[i].T;
                if (segGap > bestGap)
                {
                    bestGap = segGap;
                    leftIndex = i;
                    rightIndex = i + 1;
                }
            }

            if (bestGap <= 2e-3)
            {
                return false;
            }
        }

        var left = _curve.Points[leftIndex];
        var right = _curve.Points[rightIndex];
        var span = right.T - left.T;
        // 左ポイントの「ちょっと隣」（隙間の 20%、最低でもマージを避ける距離）。
        var offset = Math.Clamp(span * 0.2, 0.008, span * 0.5);
        var t = left.T + offset;
        if (t >= right.T - 1e-4)
        {
            t = (left.T + right.T) * 0.5;
        }

        var semi = _curve.SampleAt(t);
        if (_snapToScale)
        {
            semi = PitchCurve.SnapToScale(semi);
        }

        // 音階スナップONのときだけ階段。OFF時は DefaultShape（連続）のまま。
        if (_snapToScale && _curve.DefaultShape != FadeShape.Constant)
        {
            _curve.DefaultShape = FadeShape.Constant;
        }

        newIndex = _curve.Upsert(t, semi);
        SelectPoint(newIndex);
        CurveChanged?.Invoke();
        EnsureSelectedPointVisible();
        return true;
    }

    /// <summary>ポイントをアクティブ（選択）表示にする。追加直後の再確定にも使う。</summary>
    public void SelectPoint(int index)
    {
        if (index < 0 || index >= _curve.Points.Count)
        {
            return;
        }

        Tool = EditTool.Points;
        var prev = _dragPoint;
        _dragPoint = index;
        if (_dragPoint != prev)
        {
            SelectionChanged?.Invoke();
        }

        EnsureSelectedPointVisible();
        InvalidateVisual();
    }

    /// <summary>
    /// 選択中ポイントを半音単位で上下。複数選択時は同じ量だけ動かす（現状は単一選択）。
    /// </summary>
    public bool TryNudgeSelectedSemitones(float deltaSemitones)
    {
        if (_tool != EditTool.Points || Math.Abs(deltaSemitones) < 1e-6f)
        {
            return false;
        }

        // 将来の複数選択に備え、動かすインデックスを列挙する。
        Span<int> selected = stackalloc int[1];
        var count = 0;
        if (_dragPoint >= 0)
        {
            selected[count++] = _dragPoint;
        }

        if (count == 0)
        {
            return false;
        }

        NotifyBeforePointEditOnce();
        for (var i = 0; i < count; i++)
        {
            var index = selected[i];
            if ((uint)index >= (uint)_curve.Points.Count)
            {
                continue;
            }

            var point = _curve.Points[index];
            var next = PitchCurve.SnapSemitone(point.Semitones + deltaSemitones);
            if (_snapToScale)
            {
                next = PitchCurve.SnapToScale(next);
            }

            _curve.MovePoint(index, point.T, next);
        }

        CurveChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// 時間軸上で隣接するポイントへ選択を移す。端では動かさない。
    /// </summary>
    public bool TrySelectAdjacentPoint(int direction)
    {
        if (_tool != EditTool.Points || direction == 0 || _curve.Points.Count == 0)
        {
            return false;
        }

        if (_dragPoint < 0)
        {
            // 未選択なら、左＝末尾／右＝先頭から選び始める。
            _dragPoint = direction < 0 ? _curve.Points.Count - 1 : 0;
            SelectionChanged?.Invoke();
            EnsureSelectedPointVisible();
            InvalidateVisual();
            return true;
        }

        var next = _dragPoint + (direction < 0 ? -1 : 1);
        if (next < 0 || next >= _curve.Points.Count)
        {
            return true;
        }

        _dragPoint = next;
        SelectionChanged?.Invoke();
        EnsureSelectedPointVisible();
        InvalidateVisual();
        return true;
    }

    private void EnsureSelectedPointVisible()
    {
        if (_dragPoint < 0 || (uint)_dragPoint >= (uint)_curve.Points.Count || _viewSpan <= 0)
        {
            return;
        }

        var point = _curve.Points[_dragPoint];
        var frame = _range.StartFrame + point.T * Math.Max(0, _range.Length - 1);
        var margin = _viewSpan * 0.1;
        if (frame < _viewStart + margin)
        {
            var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - _viewSpan);
            _viewStart = Math.Clamp(frame - margin, _range.StartFrame, maxStart);
            MarkWaveDirty();
        }
        else if (frame > _viewStart + _viewSpan - margin)
        {
            var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - _viewSpan);
            _viewStart = Math.Clamp(frame - (_viewSpan - margin), _range.StartFrame, maxStart);
            MarkWaveDirty();
        }
    }

    /// <summary>
    /// 選択中ポイントの正規化時間 t を左右に動かす。端点は時間固定のため失敗し、表示パンへ譲る。
    /// 通常＝表示上 1 ピクセル相当。Shift（<paramref name="fast"/>）＝3 ピクセル。
    /// </summary>
    public bool TryNudgeSelectedTime(int direction, bool fast)
    {
        if (_tool != EditTool.Points || direction == 0 || _range.IsEmpty || _viewSpan <= 0)
        {
            return false;
        }

        Span<int> selected = stackalloc int[1];
        var count = 0;
        if (_dragPoint >= 0)
        {
            selected[count++] = _dragPoint;
        }

        if (count == 0)
        {
            return false;
        }

        var contentW = ContentWidth;
        if (contentW < 1)
        {
            return false;
        }

        var length = Math.Max(1d, _range.Length - 1);
        var pixels = fast ? 3d : 1d;
        var deltaFrames = direction * pixels * (_viewSpan / contentW);
        var deltaT = deltaFrames / length;
        var moved = false;
        for (var i = 0; i < count; i++)
        {
            var index = selected[i];
            if ((uint)index >= (uint)_curve.Points.Count)
            {
                continue;
            }

            var point = _curve.Points[index];
            var before = point.T;
            _curve.MovePoint(index, before + deltaT, point.Semitones);
            if (Math.Abs(_curve.Points[index].T - before) > 1e-12)
            {
                moved = true;
            }
        }

        if (!moved)
        {
            return false;
        }

        CurveChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// 表示時間窓を左右にパン。WPF のキーリピートで連続移動する。
    /// <paramref name="fast"/> 時は 3 倍（Shift+←→）。
    /// </summary>
    public bool TryPanTimeAxis(int direction, bool fast)
    {
        if (_range.IsEmpty || _viewSpan <= 0 || direction == 0)
        {
            return false;
        }

        // 表示幅の 5%／回。Shift で 15%。
        var fraction = fast ? 0.15 : 0.05;
        var delta = direction * _viewSpan * fraction;
        var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - _viewSpan);
        var next = Math.Clamp(_viewStart + delta, _range.StartFrame, maxStart);
        if (Math.Abs(next - _viewStart) < 1e-9)
        {
            return true;
        }

        _viewStart = next;
        MarkWaveDirty();
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// エディタ本体／無音検出と同じ拡縮キー。
    /// ↑↓＝時間、Shift+↑↓＝ピッチ(振幅相当)、Ctrl+↑↓＝時間最大段階／リセット、Ctrl+Shift＝ピッチ最大／リセット。
    /// ポイント選択中の素の↑↓／Shift+↑↓はダイアログ側で半音／オクターブ移動に使う。
    /// </summary>
    public bool TryHandleZoomKey(Key key, ModifierKeys modifiers)
    {
        if (_range.IsEmpty)
        {
            return false;
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
            SetPitchZoom(WaveformView.AmpZoomMax);
            return true;
        }

        if (key == Key.Down && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            SetPitchZoom(1d);
            return true;
        }

        if (key == Key.Up && modifiers == ModifierKeys.Shift)
        {
            SetPitchZoom(_pitchZoom * WaveformView.TimeZoomStep);
            return true;
        }

        if (key == Key.Down && modifiers == ModifierKeys.Shift)
        {
            SetPitchZoom(_pitchZoom / WaveformView.TimeZoomStep);
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

        return false;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MarkWaveDirty();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 2 || height < 2 || _range.IsEmpty)
        {
            return;
        }

        var keyboard = KeyboardGutter;
        var labelW = PitchLabelGutter;
        var contentX = LeftChrome;
        var contentW = Math.Max(1, width - contentX);
        var back = TryFindResource("WindowBackBrush") as Brush ?? Brushes.Black;
        dc.DrawRectangle(back, null, new Rect(0, 0, width, height));
        // 半音グリッドは波形の裏。波形を半透明にして透かして見せる。
        DrawPitchGuides(dc, contentX, contentW, height, keyboard, labelW);
        EnsureWaveBitmap(contentW, height);
        if (_bitmap is not null)
        {
            dc.PushOpacity(0.55);
            dc.DrawImage(_bitmap, new Rect(contentX, 0, contentW, height));
            dc.Pop();
        }

        if (_snapToScale)
        {
            DrawKeyboard(dc, keyboard, height);
        }

        // 赤カーブと再生ヘッドは波形の手前。
        DrawCurve(dc, contentX, contentW, height);

        if (_playheadFrame >= _range.StartFrame && _playheadFrame < _range.EndFrame && _viewSpan > 0)
        {
            var x = contentX + (_playheadFrame - _viewStart) / _viewSpan * contentW;
            dc.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_range.IsEmpty || ActualWidth < 2 || ActualHeight < 2)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (pos.X < LeftChrome)
        {
            e.Handled = true;
            return;
        }

        MapPoint(pos, out var t, out var semi);
        if (_tool == EditTool.Points)
        {
            var prev = _dragPoint;
            var hitRadius = VisibleHalfSemitones() * 0.08f;
            _dragPoint = _curve.HitTestPoint(t, semi, tRadius: 14 / Math.Max(1, ContentWidth), semiRadius: Math.Max(0.4f, hitRadius));
            if (_dragPoint < 0)
            {
                // クリックでポイント追加 → ピッチ履歴。
                NotifyBeforePointEditOnce();
                UpsertDrawn(t, semi);
                _dragPoint = _curve.HitTestPoint(t, semi, tRadius: 14 / Math.Max(1, ContentWidth), semiRadius: Math.Max(0.4f, hitRadius));
                CurveChanged?.Invoke();
            }

            if (_dragPoint != prev)
            {
                SelectionChanged?.Invoke();
            }

            _dragging = true;
            CaptureMouse();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // ブラシ描画はポイント履歴の対象外。
        _dragging = true;
        CaptureMouse();
        UpsertDrawn(t, semi);
        CurveChanged?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = e.GetPosition(this);
        MapPoint(pos, out var t, out var semi);
        if (_tool == EditTool.Points && _dragPoint >= 0)
        {
            // 既存点のドラッグ移動 → ピッチ履歴（1 ジェスチャ 1 回）。
            NotifyBeforePointEditOnce();
            _curve.MovePoint(_dragPoint, t, semi);
            CurveChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_tool == EditTool.Brush)
        {
            UpsertDrawn(t, semi);
            CurveChanged?.Invoke();
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void UpsertDrawn(double t, float semi)
    {
        // 音階スナップONのときだけ階段。OFF時は DefaultShape（連続）のまま。
        if (_snapToScale && _curve.DefaultShape != FadeShape.Constant)
        {
            _curve.DefaultShape = FadeShape.Constant;
        }

        _curve.Upsert(t, semi);
    }

    private double ContentWidth => Math.Max(1, ActualWidth - LeftChrome);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        _pointEditCapturedForGesture = false;
        ReleaseMouseCapture();
        if (_tool == EditTool.Brush)
        {
            _curve.Simplify(semiTolerance: 0.08f);
            CurveChanged?.Invoke();
            InvalidateVisual();
        }

        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_range.IsEmpty || ActualWidth < 2 || e.Delta == 0)
        {
            return;
        }

        e.Handled = true;
        // エディタ／無音検出と同じ: Ctrl＝振幅(ピッチ)ズーム、それ以外＝時間ズーム。
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            SetPitchZoom(e.Delta > 0
                ? _pitchZoom * WaveformView.TimeZoomStep
                : _pitchZoom / WaveformView.TimeZoomStep);
            return;
        }

        var pos = e.GetPosition(this);
        var contentW = ContentWidth;
        var fraction = contentW <= 1 ? 0.5 : Math.Clamp((pos.X - LeftChrome) / contentW, 0, 1);
        var factor = e.Delta > 0 ? WaveformView.WheelTimeStep : 1d / WaveformView.WheelTimeStep;
        var minSpan = Math.Max(1d, _range.Length / WaveformView.TimeZoomMax);
        (_viewStart, _viewSpan) = SilenceDetectWavePreview.ZoomAround(
            _viewStart,
            _viewSpan,
            _range.StartFrame,
            _range.Length,
            fraction,
            factor,
            minSpan);
        MarkWaveDirty();
        InvalidateVisual();
    }

    private double CurrentTimeZoom =>
        _viewSpan <= 0 ? 1d : Math.Max(1d, _range.Length / _viewSpan);

    private void ZoomTimeBy(double factor) =>
        ZoomTimeTo(Math.Clamp(CurrentTimeZoom * factor, 1d, WaveformView.TimeZoomMax));

    private void ZoomTimeTo(double zoom)
    {
        zoom = Math.Clamp(zoom, 1d, WaveformView.TimeZoomMax);
        var span = Math.Max(1d, _range.Length / zoom);
        var center = _viewStart + _viewSpan * 0.5;
        _viewSpan = span;
        var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - span);
        _viewStart = Math.Clamp(center - span * 0.5, _range.StartFrame, maxStart);
        MarkWaveDirty();
        InvalidateVisual();
    }

    private void SetPitchZoom(double zoom)
    {
        var next = Math.Clamp(zoom, 1d, WaveformView.AmpZoomMax);
        if (Math.Abs(next - _pitchZoom) < 1e-12)
        {
            return;
        }

        _pitchZoom = next;
        InvalidateVisual();
    }

    private float VisibleHalfSemitones() =>
        Math.Max(1f, PitchAxisSemitones / (float)_pitchZoom);

    private void DrawPitchGuides(
        DrawingContext dc,
        double contentX,
        double contentW,
        double height,
        double labelX,
        double labelW)
    {
        var half = VisibleHalfSemitones();
        var maxGuide = (int)Math.Floor(half);
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var labelBrush = TryFindResource("PrimaryForeBrush") as Brush ?? Brushes.White;
        var minLabelGap = DesignMetrics.From96(13);
        double? lastLabelY = null;
        for (var semi = -maxGuide; semi <= maxGuide; semi++)
        {
            var y = SemitoneToY(semi, height);
            if (y < -1 || y > height + 1)
            {
                continue;
            }

            if (semi != 0)
            {
                var pen = semi % 12 == 0 ? OctaveGridPen : SemitoneGridPen;
                dc.DrawLine(pen, new Point(contentX, y), new Point(contentX + contentW, y));
            }

            // オクターブは常に、拡大時は半音ごと。上下が重ならないときだけ描く。
            var labelEvery = half <= 8f ? 1 : 12;
            if (semi % labelEvery != 0)
            {
                continue;
            }

            if (lastLabelY is double prev && Math.Abs(y - prev) < minLabelGap)
            {
                continue;
            }

            var text = semi.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            var formatted = WpfControlHelpers.MonoRegularText(text, 10, labelBrush, pixelsPerDip);
            var tx = labelX + Math.Max(0, labelW - formatted.Width - 4);
            var ty = Math.Clamp(y - formatted.Height * 0.5, 0, Math.Max(0, height - formatted.Height));
            dc.DrawText(formatted, new Point(tx, ty));
            lastLabelY = y;
        }

        // 0 半音線は ±12 と同じオクターブ線の色・太さ。
        var zeroY = SemitoneToY(0, height);
        dc.DrawLine(OctaveGridPen, new Point(contentX, zeroY), new Point(contentX + contentW, zeroY));
    }

    /// <summary>
    /// 左端鍵盤。半音 0 を C 相当とし、白鍵＝自然音、黒鍵＝シャープ。
    /// </summary>
    private void DrawKeyboard(DrawingContext dc, double gutter, double height)
    {
        if (gutter < 2)
        {
            return;
        }

        var half = VisibleHalfSemitones();
        var maxGuide = (int)Math.Ceiling(half);
        var white = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        var black = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        var border = new SolidColorBrush(Color.FromArgb(0x66, 0x00, 0x00, 0x00));
        white.Freeze();
        black.Freeze();
        border.Freeze();
        var borderPen = new Pen(border, 0.6);
        borderPen.Freeze();

        // 白鍵を先に、黒鍵を上に重ねる。
        for (var pass = 0; pass < 2; pass++)
        {
            for (var semi = -maxGuide; semi <= maxGuide; semi++)
            {
                var degree = ((semi % 12) + 12) % 12;
                var isBlack = degree is 1 or 3 or 6 or 8 or 10;
                if (pass == 0 == isBlack)
                {
                    continue;
                }

                var yTop = SemitoneToY(semi + 0.5f, height);
                var yBot = SemitoneToY(semi - 0.5f, height);
                if (yBot < yTop)
                {
                    (yTop, yBot) = (yBot, yTop);
                }

                yTop = Math.Clamp(yTop, 0, height);
                yBot = Math.Clamp(yBot, 0, height);
                if (yBot - yTop < 0.5)
                {
                    continue;
                }

                var keyW = isBlack ? gutter * 0.62 : gutter;
                var rect = new Rect(0, yTop, keyW, yBot - yTop);
                dc.DrawRectangle(isBlack ? black : white, borderPen, rect);
            }
        }

        dc.DrawLine(
            new Pen(border, 1) { Brush = border },
            new Point(gutter - 0.5, 0),
            new Point(gutter - 0.5, height));
    }

    private void DrawCurve(DrawingContext dc, double contentX, double contentW, double height)
    {
        if (_viewSpan <= 0 || contentW < 1)
        {
            return;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            // 音階スナップ時は階段が見えるようサンプリングを細かくする。
            var samples = _snapToScale ? 512 : 256;
            var started = false;
            float prevSemi = float.NaN;
            for (var i = 0; i <= samples; i++)
            {
                var localX = contentW * i / samples;
                var frame = _viewStart + (localX / contentW) * _viewSpan;
                var t = (_range.Length <= 1)
                    ? 0
                    : (frame - _range.StartFrame) / (double)(_range.Length - 1);
                var semi = _curve.SampleAt(t);
                if (_snapToScale)
                {
                    semi = PitchCurve.SnapToScale(semi);
                }

                var x = contentX + localX;
                var y = SemitoneToY(semi, height);
                if (!started)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    started = true;
                    prevSemi = semi;
                    continue;
                }

                // 階段：半音が変わったら横→縦で角を出す。
                if (_snapToScale && Math.Abs(semi - prevSemi) > 1e-3f)
                {
                    ctx.LineTo(new Point(x, SemitoneToY(prevSemi, height)), true, true);
                }

                ctx.LineTo(new Point(x, y), true, true);
                prevSemi = semi;
            }
        }

        geo.Freeze();
        dc.DrawGeometry(null, CurvePen, geo);

        if (_tool == EditTool.Points)
        {
            for (var i = 0; i < _curve.Points.Count; i++)
            {
                var point = _curve.Points[i];
                var frame = _range.StartFrame + point.T * Math.Max(0, _range.Length - 1);
                var x = contentX + (frame - _viewStart) / _viewSpan * contentW;
                var y = SemitoneToY(point.Semitones, height);
                // 選択時は赤ではなく白い枠で囲んで目立たせる。
                var selected = i == _dragPoint;
                var radius = selected ? 6.5 : 4.5;
                dc.DrawEllipse(
                    PointFill,
                    selected ? SelectedPointPen : CurvePen,
                    new Point(x, y),
                    radius,
                    radius);
            }
        }
    }

    private void MapPoint(Point pos, out double t, out float semitones)
    {
        var contentW = ContentWidth;
        var localX = Math.Clamp(pos.X - LeftChrome, 0, contentW);
        var frame = _viewStart + (localX / contentW) * _viewSpan;
        t = _range.Length <= 1
            ? 0
            : Math.Clamp((frame - _range.StartFrame) / (double)(_range.Length - 1), 0, 1);
        var raw = YToSemitone(pos.Y, ActualHeight);
        semitones = _snapToScale
            ? PitchCurve.SnapToScale(raw)
            : PitchCurve.SoftSnapZero(raw, ZeroSnapRadius);
    }

    private double SemitoneToY(float semitones, double height)
    {
        var half = VisibleHalfSemitones();
        var n = semitones / half;
        var pad = PitchAxisPad;
        return height * 0.5 - n * (height * 0.5 - pad);
    }

    private float YToSemitone(double y, double height)
    {
        var mid = height * 0.5;
        var amp = Math.Max(1, mid - PitchAxisPad);
        var n = (float)((mid - y) / amp);
        return PitchCurve.Snap(n * VisibleHalfSemitones());
    }

    private void ResetView()
    {
        _viewStart = _range.StartFrame;
        _viewSpan = Math.Max(1, _range.Length);
        _pitchZoom = 1;
    }

    private void ClampView()
    {
        if (_range.IsEmpty)
        {
            return;
        }

        // WaveformView / SilenceDetect と同じ: minSpan = length / TimeZoomMax
        var minSpan = Math.Max(1d, _range.Length / WaveformView.TimeZoomMax);
        _viewSpan = Math.Clamp(_viewSpan, minSpan, Math.Max(minSpan, (double)_range.Length));
        var maxStart = Math.Max((double)_range.StartFrame, _range.EndFrame - _viewSpan);
        _viewStart = Math.Clamp(_viewStart, _range.StartFrame, maxStart);
    }

    private void MarkWaveDirty() => _waveDirty = true;

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
            && Math.Abs(_bitmapGain - _displayGain) < 1e-6f)
        {
            return;
        }

        var start = (long)Math.Floor(_viewStart);
        var end = (long)Math.Ceiling(_viewStart + _viewSpan);
        var lastFrame = Math.Max(_range.StartFrame, _range.EndFrame - 1);
        start = Math.Clamp(start, _range.StartFrame, lastFrame);
        end = Math.Clamp(end, start + 1, _range.EndFrame);
        var sampleFrames = end - start;
        // 本体／無音検出と同じ: Polyline 上限以下ならサンプル折れ線＋ドット。
        var traceSamples = WaveformView.IsPolylineZoom(sampleFrames, pxW);
        var count = traceSamples ? 0 : FillVisibleColumns(pxW, start, end);
        var bitmap = new WriteableBitmap(pxW, pxH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
        var wave = ToPbgra(Theme.Get("WaveFillBrush"));
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
            var amp = pxH * 0.5 * _displayGain;
            if (traceSamples)
            {
                PaintSampleTrace(buffer, stride, pxW, pxH, start, end, mid, amp, wave, dpi.DpiScaleX, width);
            }
            else
            {
                PaintEnvelopeColumns(buffer, stride, pxW, pxH, count, mid, amp, wave, sampleFrames);
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
        _bitmapGain = _displayGain;
        _waveDirty = false;
    }

    /// <summary>エディタの RasterPeakEnvelope と同じく、列 min/max を塗り、必要なら隣列と輪郭をつなぐ。</summary>
    private unsafe void PaintEnvelopeColumns(
        uint* buffer,
        int stride,
        int pxW,
        int pxH,
        int count,
        double mid,
        double amp,
        uint wave,
        long rangeFrames)
    {
        if (count <= 0)
        {
            return;
        }

        var connect = WaveformView.ShouldConnectEnvelopeNeighbors(rangeFrames, pxW);
        var tops = new int[count];
        var bottoms = new int[count];
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

            tops[i] = Math.Clamp(top, 0, pxH - 1);
            bottoms[i] = Math.Clamp(bottom, tops[i] + 1, pxH);
        }

        for (var i = 0; i < count; i++)
        {
            var top = tops[i];
            var bottom = bottoms[i];
            if (connect && i + 1 < count)
            {
                top = Math.Min(top, tops[i + 1]);
                bottom = Math.Max(bottom, bottoms[i + 1]);
            }

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

    /// <summary>表示フレームが画素幅×Polyline 上限以下のとき、エディタ本体と同じくサンプルを結ぶ線と点を描く。</summary>
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
            var y = (int)Math.Round(mid - MonoSampleAt(samples, channels, frame, frameCount) * amp);
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

    private float MonoSampleAt(float[] samples, int channels, long frame, int frameCount)
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

    private int FillVisibleColumns(int width, long start, long end)
    {
        width = Math.Max(1, width);
        if (_document is null || end <= start)
        {
            return 0;
        }

        var frames = end - start;
        var peaks = _document.Peaks;
        var peaksUsable = !peaks.IsEmpty;
        var useRaw = WaveformView.ShouldUseRawWaveColumns(
            seekAndSelectOnly: false,
            hasPcm: true,
            polylineZoom: false,
            peaksUsableForEnvelope: peaksUsable,
            rangeFrames: frames,
            width: width);
        if (!useRaw && peaksUsable)
        {
            var peakChannels = Math.Max(1, peaks.Channels);
            EnsureColumns(width * peakChannels);
            var count = peaks.ReadRangePacked(
                start,
                end,
                width,
                _columnMins,
                _columnMaxs,
                WaveformView.EditorEnvelopeLodBias(
                    editorMode: true,
                    frames,
                    width,
                    CurrentTimeZoom));
            if (count > 0)
            {
                FoldSolo(_columnMins, _columnMaxs, count, peakChannels, _channelMask);
                return count;
            }
        }

        EnsureColumns(width);
        FillRawColumns(width, start, end);
        return width;
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

    private void FillRawColumns(int width, long start, long end)
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        var channels = Math.Max(1, document.Channels);
        var samples = document.Interleaved;
        var frameCount = samples.Length / channels;
        var span = Math.Max(1, end - start);
        for (var i = 0; i < width; i++)
        {
            var a = start + span * i / width;
            var b = start + span * (i + 1) / width;
            if (b <= a)
            {
                b = a + 1;
            }

            float min = 0;
            float max = 0;
            var any = false;
            for (var frame = a; frame < b; frame++)
            {
                if (frame < 0 || frame >= frameCount)
                {
                    continue;
                }

                var origin = (int)frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    if (!ChannelSolo.Contains(_channelMask, ch))
                    {
                        continue;
                    }

                    var sample = samples[origin + ch];
                    if (!any)
                    {
                        min = max = sample;
                        any = true;
                    }
                    else
                    {
                        min = Math.Min(min, sample);
                        max = Math.Max(max, sample);
                    }
                }
            }

            _columnMins[i] = min;
            _columnMaxs[i] = max;
        }
    }

    private static uint ToPbgra(Color color) =>
        (uint)((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);

    private static Pen CreateCurvePen()
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0xE2, 0x3B, 0x3B)), 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Brush.Freeze();
        pen.Freeze();
        return pen;
    }

    private static Pen CreateSemitoneGridPen()
    {
        // 半音線は控えめに（少し薄く）。
        var brush = new SolidColorBrush(Color.FromArgb(0x40, 0xD0, 0xD0, 0xD0));
        brush.Freeze();
        var pen = new Pen(brush, 0.9);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateOctaveGridPen()
    {
        // オクターブ線も一段薄く。半音線よりは残す。
        var brush = new SolidColorBrush(Color.FromArgb(0x78, 0xF0, 0xF0, 0xF0));
        brush.Freeze();
        var pen = new Pen(brush, 1.25);
        pen.Freeze();
        return pen;
    }

    private static Pen CreatePlayheadPen()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0));
        brush.Freeze();
        var pen = new Pen(brush, 1.2);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateSelectedPointPen()
    {
        var brush = new SolidColorBrush(Colors.White);
        brush.Freeze();
        var pen = new Pen(brush, 2.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush CreatePointFill()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
        brush.Freeze();
        return brush;
    }
}
