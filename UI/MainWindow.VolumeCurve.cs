using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>
/// V キーの音量カーブ編集モード。旧 <c>VolumeCurveEditorDialog</c>（モーダル）を廃止し、
/// 波形描画エリア直下に <see cref="VolumeCurveBar"/> を出し、
/// カーブ編集とライブ波形プレビューはメイン波形（<see cref="VolumeCurveEditLayer"/> オーバーレイ＋
/// <see cref="WaveformView.SetPreviewGain"/>）上で行う。
/// </summary>
public partial class MainWindow
{
    private readonly record struct VolumeCurveEditSnapshot(VolumeCurve Curve, int SelectedIndex);

    private bool _volumeCurveMode;
    private VolumeCurve _volumeCurve = VolumeCurve.Flat(0);
    private WaveSelection _volumeCurveRange = WaveSelection.Empty;
    /// <summary>
    /// 音量(_N) 欄の表示値。定数カーブ時はゲインそのもの、
    /// ポイント済み（非定数）時は全体シフトの基準オフセット。
    /// </summary>
    private float _volumeCurveGainOffsetDb;
    private readonly List<VolumeCurveEditSnapshot> _volumeCurveUndo = [];
    private readonly List<VolumeCurveEditSnapshot> _volumeCurveRedo = [];
    private const int MaxVolumeCurveUndo = 64;
    private const int VolumeCurveLoudnessDebounceMs = 280;
    private DispatcherTimer? _volumeCurveLoudnessTimer;

    /// <summary>音量カーブ編集モード中か。フェード調整中と同様、他モード切替・破壊編集をロックする。</summary>
    internal bool IsVolumeCurveMode => _volumeCurveMode;

    private void InitializeVolumeCurveBar()
    {
        VolumeCurveBar.Visibility = Visibility.Collapsed;
        VolumeCurveBar.ToolChanged += OnVolumeCurveToolChanged;
        VolumeCurveBar.GainCommitted += OnVolumeCurveGainCommitted;
        VolumeCurveBar.ShapeSelected += OnVolumeCurveShapeSelected;
        VolumeCurveBar.AddPoint += VolumeCurveAddPointAtPlayhead;
        VolumeCurveBar.Reset += OnVolumeCurveReset;
        VolumeCurveBar.Accepted += () => _ = AcceptVolumeCurveAsync();
        VolumeCurveBar.Cancelled += CancelVolumeCurveMode;

        VolumeCurveEditLayer.Visibility = Visibility.Collapsed;
        VolumeCurveEditLayer.IsHitTestVisible = false;
        VolumeCurveEditLayer.CurveChanged += OnVolumeCurveCurveChanged;
        VolumeCurveEditLayer.SelectionChanged += SyncVolumeCurveShapeButtons;
        VolumeCurveEditLayer.RangeChanged += OnVolumeCurveRangeChanged;
        VolumeCurveEditLayer.CurveEditCompleted += OnVolumeCurveEditCompleted;
        VolumeCurveEditLayer.BeforePointEdit += CaptureVolumeCurveUndo;
        VolumeCurveEditLayer.DiscardPointEditCapture += DiscardLastVolumeCurveUndoCapture;
    }

    private void OnVolumeCurveRangeChanged()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        _volumeCurveRange = VolumeCurveEditLayer.Range;
    }

    /// <summary>V キー。未入場なら入る。入場中の 2 回目は Cancel 扱いで出る。</summary>
    private void PromptVolumeCurve()
    {
        if (_volumeCurveMode)
        {
            CancelVolumeCurveMode();
            return;
        }

        EnterVolumeCurveMode();
    }

    /// <summary>
    /// Esc／キャンセル／V トグルオフ。ポイント選択があれば先に解除し、
    /// 未確定編集があるときだけ破棄確認してからモードを抜ける。
    /// </summary>
    private void CancelVolumeCurveMode()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        if (VolumeCurveEditLayer.TryClearPointFocus())
        {
            return;
        }

        if (HasUnconfirmedVolumeCurveEdits())
        {
            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ConfirmVolumeCurveDiscard,
                UiStrings.AppName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        ExitVolumeCurveMode();
    }

    private bool HasUnconfirmedVolumeCurveEdits() =>
        !_volumeCurve.IsNoOp() || _volumeCurveUndo.Count > 0;

    private void EnterVolumeCurveMode()
    {
        // フェード調整中の拒否は無言のまま。音量カーブ自身の入場では警告ダイアログを出さない。
        if (_volumeCurveMode || IsCrossfadeEditMode || IsPitchCurveMode || IsUiBusy || IsLibraryMaximized)
        {
            return;
        }

        if (Waveform.LoudnessPenActive)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorLoudnessPenBlocksVolumeCurve,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // タイル表示からの入場は許容するが、シングルへ戻してから入る。
        if (_tileMode)
        {
            ExitWaveformTileMode();
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        // ActiveRange は選択が空だとファイル全体になる。入場時の自動 4 点は「先に選択があった」場合だけ。
        var hadSelection = !_document.Selection.IsEmpty;
        var selection = ActiveRange().Clamp(_document.FrameCount);
        if (selection.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 選択がある入場でも編集範囲はファイル全体。
        // 選択±pad だけを範囲にすると台形の外側点が t=0/1 端点になり、
        // 波形途中なのにファイル端点扱いで時間移動できなくなる。
        _volumeCurveRange = hadSelection
            ? new WaveSelection(0, _document.FrameCount)
            : selection;
        _volumeCurve = VolumeCurve.Flat(0);
        _volumeCurve.DefaultShape = FadeCurves.ParseStored(AppStorage.Settings.VolumeCurveDefaultShape, FadeShape.Linear);
        _volumeCurve.SetAllSegmentShapes(_volumeCurve.DefaultShape);
        _volumeCurveUndo.Clear();
        _volumeCurveRedo.Clear();

        _volumeCurveMode = true;

        VolumeCurveBar.Visibility = Visibility.Visible;
        VolumeCurveBar.Tool = VolumeCurveBarTool.Points;
        VolumeCurveBar.SetPointsModeEnabled(true);
        _volumeCurveGainOffsetDb = 0;
        VolumeCurveBar.WriteGainDb(0);
        VolumeCurveBar.SyncShapeSelection(_volumeCurve.DefaultShape, rising: true);

        VolumeCurveEditLayer.Attach(PrimaryWaveform);
        VolumeCurveEditLayer.Curve = _volumeCurve;
        VolumeCurveEditLayer.Range = _volumeCurveRange;
        VolumeCurveEditLayer.Tool = VolumeCurveEditLayer.EditTool.Points;
        VolumeCurveEditLayer.Visibility = Visibility.Visible;
        VolumeCurveEditLayer.IsHitTestVisible = true;

        // 先に選択範囲がある入場では、上げ下げ用の台形 4 点を初期状態として打つ（undo の起点）。
        // 入場直後はポイントをフォーカスしない（縦ガイドも出さない）。
        if (hadSelection)
        {
            VolumeCurveEditLayer.TryAddPointsForSelection(selection, out _);
            VolumeCurveEditLayer.ClearPointSelection();
        }

        PrimaryWaveform.SetPreviewGain(VolumeCurveGainAt);
        SyncVolumeCurvePlaybackGain();
        OnVolumeCurveCurveChanged();
        RefreshTransportCommandsEnabled();
        VolumeCurveEditLayer.Focus();
    }

    private void ExitVolumeCurveMode()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        // 適用は AcceptVolumeCurveAsync（クリップガード付き）側。ここは UI 解除のみ。
        _volumeCurveMode = false;
        StopVolumeCurveLoudnessRefresh();
        VolumeCurveBar.Visibility = Visibility.Collapsed;
        VolumeCurveEditLayer.StopTimeNudge();
        VolumeCurveEditLayer.Visibility = Visibility.Collapsed;
        VolumeCurveEditLayer.IsHitTestVisible = false;
        VolumeCurveEditLayer.ClearPointSelection();
        PrimaryWaveform.SetPreviewGain(null);
        PrimaryWaveform.RefreshLoudnessWithPreviewGain(null);
        LoudnessMeter.ClearFrameGainPreview();
        _player.SetFrameGain(null);
        _volumeCurveUndo.Clear();
        _volumeCurveRedo.Clear();
        RefreshTransportCommandsEnabled();
        RestoreDefaultContentFocusAfterOverlay();
    }

    private void OnVolumeCurveEditCompleted()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        _volumeCurveLoudnessTimer ??= CreateVolumeCurveLoudnessTimer();
        _volumeCurveLoudnessTimer.Stop();
        _volumeCurveLoudnessTimer.Start();
    }

    private DispatcherTimer CreateVolumeCurveLoudnessTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(VolumeCurveLoudnessDebounceMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            RefreshVolumeCurveLoudnessPreview();
        };
        return timer;
    }

    private void StopVolumeCurveLoudnessRefresh()
    {
        _volumeCurveLoudnessTimer?.Stop();
    }

    /// <summary>ドラッグ完了後、プレビューゲイン込みでメーター／曲線のラウドネスを取り直す。</summary>
    private void RefreshVolumeCurveLoudnessPreview()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        PrimaryWaveform.RefreshLoudnessWithPreviewGain(VolumeCurveGainAt);
        LoudnessMeter.RefreshOfflineWithFrameGain(VolumeCurveGainAt);
    }

    /// <summary>V モード中の本体再生に現在カーブのゲインを載せる／外す。</summary>
    private void SyncVolumeCurvePlaybackGain()
    {
        if (!_volumeCurveMode)
        {
            _player.SetFrameGain(null);
            return;
        }

        _player.SetFrameGain(VolumeCurveGainAt);
    }

    /// <summary>範囲内フレームでの現在カーブのリニアゲイン（波形ライブプレビュー／再生プレビュー共有）。</summary>
    private float VolumeCurveGainAt(long frame)
    {
        var range = _volumeCurveRange;
        if (frame < range.StartFrame || frame >= range.EndFrame)
        {
            return 1f;
        }

        var length = Math.Max(1L, range.Length - 1);
        var t = Math.Clamp((frame - range.StartFrame) / (double)length, 0d, 1d);
        return (float)WaveformGainAnalyzer.LinearFromDb(_volumeCurve.SampleAt(t));
    }

    private void OnVolumeCurveCurveChanged()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        VolumeCurveEditLayer.InvalidateCurve();
        PrimaryWaveform.SetPreviewGain(VolumeCurveGainAt);
        SyncVolumeCurvePlaybackGain();
        if (_volumeCurve.IsConstant(out var gainDb))
        {
            _volumeCurveGainOffsetDb = gainDb;
            VolumeCurveBar.WriteGainDb(gainDb);
        }
        else
        {
            // 非定数: 欄は全体上げ下げオフセットのまま（形を定数表示で上書きしない）。
            VolumeCurveBar.WriteGainDb(_volumeCurveGainOffsetDb);
        }

        SyncVolumeCurveShapeButtons();
    }

    private void SyncVolumeCurveShapeButtons()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        var index = VolumeCurveEditLayer.SelectedPointIndex;
        var shape = index >= 0 ? _volumeCurve.OutgoingShapeOfPoint(index) : _volumeCurve.DefaultShape;
        VolumeCurveBar.SyncShapeSelection(shape, ResolveVolumeCurveShapeIconRising(index));
    }

    private bool ResolveVolumeCurveShapeIconRising(int index)
    {
        var points = _volumeCurve.Points;
        if (index >= 0 && index + 1 < points.Count)
        {
            return points[index + 1].GainDb >= points[index].GainDb;
        }

        if (index > 0 && index < points.Count)
        {
            return points[index].GainDb >= points[index - 1].GainDb;
        }

        return true;
    }

    private void OnVolumeCurveToolChanged(VolumeCurveBarTool tool)
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        VolumeCurveEditLayer.Tool = tool == VolumeCurveBarTool.Brush
            ? VolumeCurveEditLayer.EditTool.Brush
            : VolumeCurveEditLayer.EditTool.Points;
        VolumeCurveBar.SetPointsModeEnabled(tool == VolumeCurveBarTool.Points);
    }

    private void OnVolumeCurveShapeSelected(FadeShape shape)
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        if (shape == FadeShape.Constant)
        {
            shape = FadeShape.Linear;
        }

        var selectedIndex = VolumeCurveEditLayer.SelectedPointIndex;
        if (selectedIndex >= 0)
        {
            if (_volumeCurve.OutgoingShapeOfPoint(selectedIndex) == shape)
            {
                return;
            }

            CaptureVolumeCurveUndo();
            _volumeCurve.SetPointOutgoingShape(selectedIndex, shape);
        }
        else
        {
            if (_volumeCurve.DefaultShape == shape)
            {
                return;
            }

            CaptureVolumeCurveUndo();
            _volumeCurve.DefaultShape = shape;
            AppStorage.Settings.VolumeCurveDefaultShape = _volumeCurve.DefaultShape.ToString();
        }

        OnVolumeCurveCurveChanged();
    }

    private bool TryApplyVolumeCurveShapeFromDigitKey(Key key)
    {
        if (!_volumeCurveMode || !FadeCurveIcons.TryShapeFromDigitKey(key, isFadeIn: true, out var shape))
        {
            return false;
        }

        OnVolumeCurveShapeSelected(shape);
        return true;
    }

    private void VolumeCurveAddPointAtPlayhead()
    {
        if (!_volumeCurveMode || _document is null)
        {
            return;
        }

        // ブラシ中でも Alt+A／追加ボタンはポイントツールへ切り替えてから打つ。
        if (VolumeCurveEditLayer.Tool != VolumeCurveEditLayer.EditTool.Points)
        {
            VolumeCurveBar.Tool = VolumeCurveBarTool.Points;
            VolumeCurveEditLayer.Tool = VolumeCurveEditLayer.EditTool.Points;
        }

        CaptureVolumeCurveUndo();
        var selection = ResolveVolumeCurvePointSelection();
        bool added;
        if (!selection.IsEmpty)
        {
            // 範囲選択あり → 台形 4 点。短すぎて組めないときだけシーク位置へ 1 点。
            added = VolumeCurveEditLayer.TryAddPointsForSelection(selection, out var selectedIndex);
            if (added)
            {
                if (selectedIndex >= 0)
                {
                    VolumeCurveEditLayer.SelectPoint(selectedIndex);
                }
            }
            else
            {
                var playhead = _player.IsPlaying ? _player.SmoothCursorFrame : _document.CursorFrame;
                added = VolumeCurveEditLayer.TryAddPointAtPlayhead(playhead, out _);
            }
        }
        else
        {
            // 範囲選択なし → シークバー直下に 1 点（編集範囲全体を台形にしない）。
            var playhead = _player.IsPlaying ? _player.SmoothCursorFrame : _document.CursorFrame;
            added = VolumeCurveEditLayer.TryAddPointAtPlayhead(playhead, out _);
        }

        if (!added)
        {
            DiscardLastVolumeCurveUndoCapture();
            return;
        }

        VolumeCurveBar.Tool = VolumeCurveBarTool.Points;
        OnVolumeCurveCurveChanged();
    }

    /// <summary>
    /// Alt+A 用の波形選択。未選択なら Empty（呼び出し側がシーク位置へ 1 点を打つ）。
    /// </summary>
    private WaveSelection ResolveVolumeCurvePointSelection()
    {
        if (_document is null || _volumeCurveRange.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var selection = _document.Selection.Clamp(_document.FrameCount);
        if (selection.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var start = Math.Max(selection.StartFrame, _volumeCurveRange.StartFrame);
        var end = Math.Min(selection.EndFrame, _volumeCurveRange.EndFrame);
        return end <= start ? WaveSelection.Empty : new WaveSelection(start, end);
    }

    private void OnVolumeCurveReset()
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        CaptureVolumeCurveUndo();
        _volumeCurve.SetConstant(0);
        _volumeCurveGainOffsetDb = 0;
        VolumeCurveEditLayer.ClearPointSelection();
        OnVolumeCurveCurveChanged();
    }

    private void OnVolumeCurveGainCommitted(float value, VolumeCurveGainCommitKind _)
    {
        if (!_volumeCurveMode)
        {
            return;
        }

        value = VolumeCurve.SnapGainStep(value);

        // 定数カーブ: 欄の値＝全体ゲイン。ポイント済み: 欄の差分で全体を平行移動。
        if (_volumeCurve.IsConstant(out var currentConst))
        {
            if (VolumeCurve.AbsGainDeltaDb(currentConst, value) < 1e-6f)
            {
                _volumeCurveGainOffsetDb = value;
                VolumeCurveBar.WriteGainDb(value);
                return;
            }

            CaptureVolumeCurveUndo();
            _volumeCurve.SetConstant(value);
            _volumeCurveGainOffsetDb = value;
            OnVolumeCurveCurveChanged();
            OnVolumeCurveEditCompleted();
            return;
        }

        var from = _volumeCurveGainOffsetDb;
        if (VolumeCurve.AbsGainDeltaDb(from, value) < 1e-6f)
        {
            VolumeCurveBar.WriteGainDb(from);
            return;
        }

        // -∞ 同士や片側 -∞ は差分シフトにならないので定数化はしない（形を残す）。
        if (float.IsNegativeInfinity(from) || float.IsNegativeInfinity(value))
        {
            VolumeCurveBar.WriteGainDb(from);
            return;
        }

        var delta = value - from;
        CaptureVolumeCurveUndo();
        _volumeCurve.ShiftAll(delta);
        _volumeCurveGainOffsetDb = value;
        VolumeCurveBar.WriteGainDb(value);
        OnVolumeCurveCurveChanged();
        OnVolumeCurveEditCompleted();
    }

    // --- undo / redo（モード内だけの軽量履歴。本体の EditHistory とは独立） ---

    private void CaptureVolumeCurveUndo()
    {
        _volumeCurveUndo.Add(new VolumeCurveEditSnapshot(_volumeCurve.Clone(), VolumeCurveEditLayer.SelectedPointIndex));
        if (_volumeCurveUndo.Count > MaxVolumeCurveUndo)
        {
            _volumeCurveUndo.RemoveAt(0);
        }

        _volumeCurveRedo.Clear();
    }

    private void DiscardLastVolumeCurveUndoCapture()
    {
        if (_volumeCurveUndo.Count > 0)
        {
            _volumeCurveUndo.RemoveAt(_volumeCurveUndo.Count - 1);
        }
    }

    private void UndoVolumeCurveEdit()
    {
        if (!_volumeCurveMode || _volumeCurveUndo.Count == 0)
        {
            return;
        }

        var current = new VolumeCurveEditSnapshot(_volumeCurve.Clone(), VolumeCurveEditLayer.SelectedPointIndex);
        var previous = _volumeCurveUndo[^1];
        _volumeCurveUndo.RemoveAt(_volumeCurveUndo.Count - 1);
        _volumeCurveRedo.Add(current);
        ApplyVolumeCurveSnapshot(previous);
    }

    private void RedoVolumeCurveEdit()
    {
        if (!_volumeCurveMode || _volumeCurveRedo.Count == 0)
        {
            return;
        }

        var current = new VolumeCurveEditSnapshot(_volumeCurve.Clone(), VolumeCurveEditLayer.SelectedPointIndex);
        var next = _volumeCurveRedo[^1];
        _volumeCurveRedo.RemoveAt(_volumeCurveRedo.Count - 1);
        _volumeCurveUndo.Add(current);
        ApplyVolumeCurveSnapshot(next);
    }

    private void ApplyVolumeCurveSnapshot(VolumeCurveEditSnapshot snapshot)
    {
        _volumeCurve.CopyFrom(snapshot.Curve);
        if (snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < _volumeCurve.Points.Count)
        {
            VolumeCurveEditLayer.SelectPoint(snapshot.SelectedIndex);
        }
        else
        {
            VolumeCurveEditLayer.ClearPointSelection();
        }

        OnVolumeCurveCurveChanged();
    }

    /// <summary>
    /// OK。ピークが 0 dBFS を超えるときは確認してから適用する（Cancel なら編集継続）。
    /// </summary>
    private async Task AcceptVolumeCurveAsync()
    {
        if (!_volumeCurveMode || IsUiBusy || _document is null)
        {
            return;
        }

        var curve = _volumeCurve.Clone();
        var range = _volumeCurveRange.Clamp(_document.FrameCount);
        if (curve.IsNoOp())
        {
            ExitVolumeCurveMode();
            return;
        }

        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorNoSelection,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        var mask = EditMask();
        _pitchShiftBusy = true;
        Exception? error = null;
        VolumeCurveRenderResult rendered = default;
        var keepEditing = false;
        var aborted = false;
        try
        {
            ShowBusyGlass(UiStrings.LabelVolume);
            IProgress<double> progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            rendered = await Task.Run(() => ProcessEdits.RenderVolumeCurveRange(
                    document,
                    range,
                    curve,
                    mask,
                    progress))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document))
            {
                aborted = true;
                return;
            }

            if (LoudnessPenGain.WouldClip(rendered.PeakAbs))
            {
                var peak = Math.Max(rendered.PeakAbs, 1e-8f);
                var factor = LoudnessPenGain.HeadroomLinear / peak;
                var deltaDb = (float)(20d * Math.Log10(factor));
                var lowerDbText = TabMerge.FormatOverDb(-deltaDb);
                _busyGlass.HideOverlay();
                var answer = OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ConfirmVolumeCurveClip(lowerDbText),
                    UiStrings.AppName,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.OK)
                {
                    keepEditing = true;
                    return;
                }

                curve.ShiftAll(deltaDb);
                ShowBusyGlass(UiStrings.LabelVolume);
                progress = new Progress<double>(p => _busyGlass.SetProgress(p));
                rendered = await Task.Run(() => ProcessEdits.RenderVolumeCurveRange(
                        document,
                        range,
                        curve,
                        mask,
                        progress))
                    .ConfigureAwait(true);
                if (!IsLoaded || !ReferenceEquals(_document, document))
                {
                    aborted = true;
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            if (error is not null || keepEditing || aborted)
            {
                _pitchShiftBusy = false;
                _busyGlass.HideOverlay();
                RefreshTransportCommandsEnabled();
            }
        }

        if (error is not null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (keepEditing || aborted)
        {
            if (keepEditing)
            {
                VolumeCurveEditLayer.Focus();
            }

            return;
        }

        // ピーク検査用の busy をいったん下ろし、適用側で改めて立てる。
        _pitchShiftBusy = false;
        ExitVolumeCurveMode();
        await ApplyVolumeCurveAsync(curve, range, rendered.AfterRange).ConfigureAwait(true);
    }

    private async Task ApplyVolumeCurveAsync(
        VolumeCurve curve,
        WaveSelection range,
        float[]? precomputedAfterRange = null)
    {
        if (IsUiBusy || _document is null || curve.IsNoOp())
        {
            return;
        }

        range = range.Clamp(_document.FrameCount);
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        var mask = EditMask();
        _pitchShiftBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowBusyGlass(UiStrings.LabelVolume);
            IProgress<double> progress = new Progress<double>(p => _busyGlass.SetProgress(p));

            var precomputed = precomputedAfterRange;
            command = await Task.Run(() => ProcessEdits.VolumeCurve(
                    document,
                    range,
                    curve,
                    progress,
                    channelMask: mask,
                    precomputedAfterRange: precomputed))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            // AfterTransform 内の Refresh は busy 中に走るので、メニュー／ツールバーは
            // finally で busy 解除後にもう一度有効化する。
            AfterTransform();
            PausePlaybackSoft();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _pitchShiftBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }

            RefreshTransportCommandsEnabled();
            RestoreEditorKeyboardFocusAfterBusy();
        }

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

    /// <summary>
    /// 音量カーブ編集モード中のキー処理。モード外は false を返し、通常のショートカット処理へ委ねる
    /// （V キー自体は呼び出し元で別途処理する）。
    /// </summary>
    private bool TryProcessVolumeCurveShortcut(Key key, ModifierKeys modifiers)
    {
        if (!_volumeCurveMode)
        {
            return false;
        }

        // Esc は TryProcessEscapeShortcut 側（ポイントフォーカス解除のみ。モード退出はしない）。
        if (key == Key.Escape)
        {
            return false;
        }

        if (key == Key.V && modifiers == ModifierKeys.None)
        {
            CancelVolumeCurveMode();
            return true;
        }

        // Alt+O: OK で編集確定（数値欄フォーカス中も有効）。クリップ時は確認。
        if (key == Key.O
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            _ = AcceptVolumeCurveAsync();
            return true;
        }

        // Alt+C: Cancel（ラベル _Cancel）。
        if (key == Key.C
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            CancelVolumeCurveMode();
            return true;
        }

        var focusedTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            UndoVolumeCurveEdit();
            return true;
        }

        if ((key == Key.Y && modifiers == ModifierKeys.Control)
            || (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            RedoVolumeCurveEdit();
            return true;
        }

        // Space は本体の再生トグルへ渡す（V モード中も許可）。
        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            return false;
        }

        if (key == Key.N && (modifiers & ModifierKeys.Alt) != 0 && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            VolumeCurveBar.FocusGainBox();
            return true;
        }

        if (key == Key.A && (modifiers & ModifierKeys.Alt) != 0 && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            VolumeCurveAddPointAtPlayhead();
            return true;
        }

        if (focusedTextBox)
        {
            // 数字・矢印キー等はバーの数値欄側へ渡す。
            return false;
        }

        if (key == Key.Delete && VolumeCurveEditLayer.Tool == VolumeCurveEditLayer.EditTool.Points)
        {
            VolumeCurveEditLayer.TryDeleteSelectedPoint();
            return true;
        }

        if (modifiers == ModifierKeys.None
            && VolumeCurveEditLayer.Tool == VolumeCurveEditLayer.EditTool.Points
            && VolumeCurveEditLayer.SelectedPointIndex >= 0
            && TryApplyVolumeCurveShapeFromDigitKey(key))
        {
            return true;
        }

        if (key is Key.Up or Key.Down && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
        {
            var step = modifiers == ModifierKeys.Shift ? 6f : 1f;
            if (VolumeCurveEditLayer.TryNudgeSelectedGainDb(key == Key.Up ? step : -step))
            {
                return true;
            }
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.Alt)
        {
            if (VolumeCurveEditLayer.TrySelectAdjacentPoint(key == Key.Left ? -1 : 1))
            {
                return true;
            }
        }

        // ポイント選択中のみ ←→ で制御点を時間移動。未選択時は通常のシークへ渡す。
        if (key is Key.Left or Key.Right
            && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)
            && VolumeCurveEditLayer.Tool == VolumeCurveEditLayer.EditTool.Points
            && VolumeCurveEditLayer.SelectedPointIndex >= 0)
        {
            if (VolumeCurveEditLayer.BeginOrContinueTimeNudge(key == Key.Left ? -1 : 1))
            {
                return true;
            }
        }

        // Ctrl+L: 既にラウドネス表示中なら同じモードのトグル解除を許容。
        if (key == Key.L && modifiers == ModifierKeys.Control)
        {
            if (Waveform.AnalysisView == WaveformAnalysisView.Loudness)
            {
                CloseVolumeGainPicker();
                Waveform.ExitAnalysisView();
                return true;
            }

            WarnVolumeCurveModeBlocked();
            return true;
        }

        // A: スペクトログラム／重ねの相互トグルは許容。未表示からの入場は拒否。
        if (key == Key.A && modifiers == ModifierKeys.None)
        {
            if (Waveform.AnalysisView is WaveformAnalysisView.Spectrogram or WaveformAnalysisView.Overlay)
            {
                Waveform.ToggleSpectrogramView();
                return true;
            }

            WarnVolumeCurveModeBlocked();
            return true;
        }

        // マーカー追加（M／Ins）、マーカークリック（Q）、ループへ（L）、時刻フォーカス（G）は通す。
        if (modifiers == ModifierKeys.None && key is Key.M or Key.Insert or Key.Q or Key.L or Key.G)
        {
            return false;
        }

        // 他の破壊編集・モード切替キーはモード中ブロック（警告付き）。V 自身のトグルは上で許可済み。
        if (modifiers == ModifierKeys.None
            && key is Key.S or Key.E or Key.C or Key.X or Key.I or Key.O or Key.N or Key.R
                or Key.P or Key.T or Key.B or Key.U)
        {
            WarnVolumeCurveModeBlocked();
            return true;
        }

        return false;
    }
}
