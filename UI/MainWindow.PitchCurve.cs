using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>
/// P キーのピッチカーブ編集モード。旧 <c>PitchCurveEditorDialog</c>（モーダル）を廃止し、
/// 波形描画エリア直下に <see cref="PitchCurveBar"/> を出し、
/// カーブ編集はメイン波形（<see cref="PitchCurveEditLayer"/> オーバーレイ）上で行う。
/// 音量カーブと違い、ピッチは単純な係数を毎フレーム掛けるだけでは成立しないため、
/// ライブプレビューはできない。Space は <see cref="EnsurePitchCurveRenderAsync"/> で
/// レンダリング済みのピッチ音声を本体プレイヤーへ一時差し替えて鳴らす。
/// 「ピッチを無視して再生」だけバーに残し、シークバー／再生ヘッドは本体 UI を動かす。
/// </summary>
public partial class MainWindow
{
    private readonly record struct PitchCurveEditSnapshot(PitchCurve Curve, int SelectedIndex);

    private bool _pitchCurveMode;
    private PitchCurve _pitchCurve = PitchCurve.Flat(0);
    private WaveSelection _pitchCurveRange = WaveSelection.Empty;
    /// <summary>
    /// ピッチ(_N) 欄の表示値。定数カーブ時はピッチそのもの、
    /// ポイント済み（非定数）時は全体シフトの基準オフセット。
    /// </summary>
    private float _pitchCurveSemitoneOffset;
    /// <summary>音階スナップ解除時に戻す連続形状。Constant 自体は記憶しない。</summary>
    private FadeShape _pitchCurveContinuousShape = FadeShape.Linear;
    private readonly List<PitchCurveEditSnapshot> _pitchCurveUndo = [];
    private readonly List<PitchCurveEditSnapshot> _pitchCurveRedo = [];
    private const int MaxPitchCurveUndo = 64;

    /// <summary>再生プレビューと OK 適用で共有するピッチ計算キャッシュ。</summary>
    private float[]? _pitchCurveCachedAfterRange;
    private float _pitchCurveCachedPeak;
    private string? _pitchCurveCacheKey;
    private CancellationTokenSource? _pitchCurveRenderCts;
    private bool _pitchCurveRendering;
    private bool _pitchCurveIgnoreCurve;
    /// <summary>プレビュー／スナップ変更の割り込みで古い非同期処理を捨てるための世代。</summary>
    private int _pitchCurveOpEpoch;
    /// <summary>タイムストレッチなしプレビュー: 出力フレーム → 元レンジ内オフセット。</summary>
    private int[]? _pitchCurvePreviewToSourceMap;
    private long _pitchCurvePreviewPlaybackFrames;
    private const int PitchCurveLoudnessDebounceMs = 280;
    private DispatcherTimer? _pitchCurveLoudnessTimer;
    private int _pitchCurveLoudnessEpoch;

    /// <summary>ピッチカーブ編集モード中か。音量カーブと同様、他モード切替・破壊編集をロックする。</summary>
    internal bool IsPitchCurveMode => _pitchCurveMode;

    private void InitializePitchCurveBar()
    {
        PitchCurveBar.Visibility = Visibility.Collapsed;
        PitchCurveBar.ToolChanged += OnPitchCurveToolChanged;
        PitchCurveBar.PitchCommitted += OnPitchCurvePitchCommitted;
        PitchCurveBar.ShapeSelected += OnPitchCurveShapeSelected;
        PitchCurveBar.AddPoint += PitchCurveAddPointAtPlayhead;
        PitchCurveBar.Reset += OnPitchCurveReset;
        PitchCurveBar.Accepted += () => _ = AcceptPitchCurveAsync();
        PitchCurveBar.Cancelled += CancelPitchCurveMode;
        PitchCurveBar.PreviewBypass += TogglePitchCurveBypassPreview;
        PitchCurveBar.SnapToScaleChanged += OnPitchCurveSnapToScaleChanged;
        PitchCurveBar.NoTimeStretchChanged += OnPitchCurveNoTimeStretchChanged;
        PitchCurveBar.LightweightModeChanged += OnPitchCurveLightweightModeChanged;

        PitchCurveEditLayer.Visibility = Visibility.Collapsed;
        PitchCurveEditLayer.IsHitTestVisible = false;
        PitchCurveEditLayer.CurveChanged += OnPitchCurveCurveChanged;
        PitchCurveEditLayer.SelectionChanged += SyncPitchCurveShapeButtons;
        PitchCurveEditLayer.RangeChanged += OnPitchCurveRangeChanged;
        PitchCurveEditLayer.CurveEditCompleted += OnPitchCurveEditCompleted;
        PitchCurveEditLayer.BeforePointEdit += CapturePitchCurveUndo;
        PitchCurveEditLayer.DiscardPointEditCapture += DiscardLastPitchCurveUndoCapture;
    }

    private void OnPitchCurveRangeChanged()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        _pitchCurveRange = PitchCurveEditLayer.Range;
        InvalidatePitchCurveRenderCache();
    }

    /// <summary>P キー。未入場なら入る。入場中は何もしない（退出は Cancel／Alt+C／OK のみ）。</summary>
    private void PromptPitchCurve()
    {
        if (_pitchCurveMode)
        {
            return;
        }

        EnterPitchCurveMode();
    }

    /// <summary>
    /// Esc／キャンセル。ポイント選択があれば先に解除し、
    /// 未確定編集があるときだけ破棄確認してからモードを抜ける。
    /// </summary>
    private void CancelPitchCurveMode()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        if (PitchCurveEditLayer.TryClearPointFocus())
        {
            return;
        }

        if (HasUnconfirmedPitchCurveEdits())
        {
            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ConfirmPitchCurveDiscard,
                UiStrings.AppName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (answer != MessageBoxResult.OK)
            {
                return;
            }
        }

        ExitPitchCurveMode();
    }

    private bool HasUnconfirmedPitchCurveEdits() =>
        !_pitchCurve.IsNoOp() || _pitchCurveUndo.Count > 0;

    private void EnterPitchCurveMode()
    {
        // 他モード調整中の拒否は無言のまま。ピッチカーブ自身の入場では警告ダイアログを出さない。
        if (_pitchCurveMode || IsCrossfadeEditMode || IsVolumeCurveMode || IsUiBusy || IsLibraryMaximized)
        {
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

        // 選択がある入場でも編集範囲はファイル全体（音量カーブと同じ理由: 台形の外側点が
        // ファイル端点扱いになって時間移動できなくなるのを防ぐ）。
        _pitchCurveRange = hadSelection
            ? new WaveSelection(0, _document.FrameCount)
            : selection;
        _pitchCurve = PitchCurve.Flat(0);
        // ピッチの規定はリニア。設定やフェード既定の S 字が残っていたらリニアへ直す。
        _pitchCurveContinuousShape = ResolvePitchCurveContinuousShape(
            FadeCurves.ParseStored(AppStorage.Settings.PitchCurveDefaultShape, FadeShape.Linear));
        if (AppStorage.Settings.PitchCurveDefaultShape != _pitchCurveContinuousShape.ToString())
        {
            AppStorage.Settings.PitchCurveDefaultShape = _pitchCurveContinuousShape.ToString();
        }

        _pitchCurve.DefaultShape = _pitchCurveContinuousShape;
        _pitchCurve.SetAllSegmentShapes(_pitchCurve.DefaultShape);
        _pitchCurveUndo.Clear();
        _pitchCurveRedo.Clear();
        InvalidatePitchCurveRenderCache();
        _pitchCurveIgnoreCurve = false;

        _pitchCurveMode = true;
        // 鍵盤と被る dB 目盛りだけ消す（レーン幅は残して鍵盤を載せる）。
        PrimaryWaveform.PaintDbScale = false;

        PitchCurveBar.Visibility = Visibility.Visible;
        PitchCurveBar.Tool = PitchCurveBarTool.Points;
        PitchCurveBar.SetPointsModeEnabled(true);
        PitchCurveBar.SetBypassActive(false);
        _pitchCurveSemitoneOffset = 0;
        PitchCurveBar.WriteSemitones(0);
        PitchCurveBar.SyncShapeSelection(_pitchCurve.DefaultShape, rising: true);

        PitchCurveEditLayer.Attach(PrimaryWaveform);
        PitchCurveEditLayer.SnapToScale = PitchCurveBar.SnapToScale;
        PitchCurveEditLayer.Curve = _pitchCurve;
        PitchCurveEditLayer.Range = _pitchCurveRange;
        PitchCurveEditLayer.Tool = PitchCurveEditLayer.EditTool.Points;
        PitchCurveEditLayer.Visibility = Visibility.Visible;
        PitchCurveEditLayer.IsHitTestVisible = true;

        if (PitchCurveBar.SnapToScale)
        {
            _pitchCurve.QuantizeToScaleSteps();
        }

        // 先に選択範囲がある入場では、上げ下げ用の台形 4 点を初期状態として打つ（undo の起点）。
        if (hadSelection)
        {
            PitchCurveEditLayer.TryAddPointsForSelection(selection, out _);
            PitchCurveEditLayer.ClearPointSelection();
        }

        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
        RefreshTransportCommandsEnabled();
        PitchCurveEditLayer.Focus();
    }

    private void ExitPitchCurveMode()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        // 適用は AcceptPitchCurveAsync（クリップガード付き）側。ここは UI 解除のみ。
        _pitchCurveMode = false;
        StopPitchCurveLoudnessRefresh();
        StopPitchCurveModePreview(cancelRender: true);
        PrimaryWaveform.PaintDbScale = true;
        PitchCurveBar.Visibility = Visibility.Collapsed;
        PitchCurveEditLayer.StopTimeNudge();
        PitchCurveEditLayer.Visibility = Visibility.Collapsed;
        PitchCurveEditLayer.IsHitTestVisible = false;
        PitchCurveEditLayer.ClearPointSelection();
        PrimaryWaveform.SetPreviewSamples(null, 0);
        PrimaryWaveform.RefreshLoudnessWithPreviewGain(null);
        LoudnessMeter.ClearFrameGainPreview();
        _pitchCurveUndo.Clear();
        _pitchCurveRedo.Clear();
        InvalidatePitchCurveRenderCache();
        RefreshTransportCommandsEnabled();
        RestoreDefaultContentFocusAfterOverlay();
    }

    private void OnPitchCurveEditCompleted()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        _pitchCurveLoudnessTimer ??= CreatePitchCurveLoudnessTimer();
        _pitchCurveLoudnessTimer.Stop();
        _pitchCurveLoudnessTimer.Start();
    }

    private DispatcherTimer CreatePitchCurveLoudnessTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(PitchCurveLoudnessDebounceMs),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = RefreshPitchCurveLoudnessPreviewAsync();
        };
        return timer;
    }

    private void StopPitchCurveLoudnessRefresh()
    {
        _pitchCurveLoudnessTimer?.Stop();
        _pitchCurveLoudnessEpoch++;
    }

    /// <summary>
    /// 編集完了後、ピッチ適用後の PCM を仮組みしてメーター／ラウドネス曲線を取り直す。
    /// </summary>
    private async Task RefreshPitchCurveLoudnessPreviewAsync()
    {
        if (!_pitchCurveMode || _document is null || IsUiBusy)
        {
            return;
        }

        var epoch = ++_pitchCurveLoudnessEpoch;
        try
        {
            if (_pitchCurve.IsNoOp())
            {
                PrimaryWaveform.SetPreviewSamples(null, 0);
                PrimaryWaveform.RefreshLoudnessWithPreviewGain(null);
                LoudnessMeter.ClearFrameGainPreview();
                return;
            }

            var timeStretch = WantsPitchCurveTimeStretch();
            var lightweight = WantsPitchCurveLightweightPreview();
            var key = BuildPitchCurveRenderCacheKey(timeStretch, lightweight);
            (float[] AfterRange, float Peak)? rendered = null;
            if (_pitchCurveCachedAfterRange is not null && _pitchCurveCacheKey == key)
            {
                rendered = (_pitchCurveCachedAfterRange, _pitchCurveCachedPeak);
            }
            else if (_pitchShiftBusy || _pitchPreview.Previewing || _pitchCurveRendering)
            {
                // Space プレビュー／本適用のレンダを奪わない。キャッシュができたあとに再スケジュールする。
                return;
            }
            else
            {
                _pitchCurveRenderCts?.Cancel();
                _pitchCurveRenderCts?.Dispose();
                _pitchCurveRenderCts = new CancellationTokenSource();
                var token = _pitchCurveRenderCts.Token;
                rendered = await EnsurePitchCurveRenderAsync(
                        token,
                        forPreview: true,
                        showProgress: false)
                    .ConfigureAwait(true);
                if (token.IsCancellationRequested)
                {
                    return;
                }
            }

            if (epoch != _pitchCurveLoudnessEpoch
                || rendered is null
                || !_pitchCurveMode
                || !IsLoaded
                || _document is null)
            {
                return;
            }

            ApplyPitchCurveLoudnessFromPitchedRange(rendered.Value.AfterRange);
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch (Exception)
        {
            // プレビュー計測失敗は編集を止めない。
        }
    }

    /// <summary>0 dBFS 以内に収めるために下げる dB（V モードと同じ計算）。</summary>
    private static string FormatPitchCurveLowerDb(float peakAbs)
    {
        var peak = Math.Max(peakAbs, 1e-8f);
        var factor = LoudnessPenGain.HeadroomLinear / peak;
        var deltaDb = 20d * Math.Log10(factor);
        return TabMerge.FormatOverDb(-deltaDb);
    }

    private void ApplyPitchCurveLoudnessFromPitchedRange(float[] pitchedRange)
    {
        if (_document is null)
        {
            return;
        }

        var spliced = BuildPitchCurvePreviewDocumentSamples(pitchedRange);
        if (spliced is null || spliced.Length < _document.Channels)
        {
            return;
        }

        LoudnessMeter.RefreshOfflineWithPreviewSamples(
            spliced,
            _document.Channels,
            _document.SampleRate);

        // 尺が変わると曲線の時間軸がずれるので、長さ据え置き時だけ波形／赤カプセルも更新する。
        var sourceFrames = Math.Max(1, _pitchCurveRange.Length);
        var pitchedFrames = pitchedRange.Length / Math.Max(1, _document.Channels);
        if (pitchedFrames == sourceFrames)
        {
            PrimaryWaveform.SetPreviewSamples(spliced, _document.Channels);
            PrimaryWaveform.RefreshLoudnessWithPreviewSamples(
                spliced,
                _document.Channels,
                _document.SampleRate);
        }
        else
        {
            PrimaryWaveform.SetPreviewSamples(null, 0);
        }
    }

    /// <summary>編集範囲をピッチ結果で差し替えた仮ドキュメント PCM。失敗時は null。</summary>
    private float[]? BuildPitchCurvePreviewDocumentSamples(float[] pitchedRange)
    {
        if (_document is null || pitchedRange.Length == 0)
        {
            return null;
        }

        var channels = Math.Max(1, _document.Channels);
        if (pitchedRange.Length % channels != 0)
        {
            return null;
        }

        var src = _document.Interleaved;
        var used = Math.Clamp(_document.SampleCount, 0, src.Length);
        var range = _pitchCurveRange.Clamp(_document.FrameCount);
        if (range.IsEmpty)
        {
            return null;
        }

        var before = (int)Math.Min(int.MaxValue, range.StartFrame * channels);
        var afterStart = (int)Math.Min(int.MaxValue, range.EndFrame * channels);
        before = Math.Clamp(before, 0, used);
        afterStart = Math.Clamp(afterStart, before, used);
        var afterLen = used - afterStart;
        var result = new float[before + pitchedRange.Length + afterLen];
        if (before > 0)
        {
            Array.Copy(src, 0, result, 0, before);
        }

        Array.Copy(pitchedRange, 0, result, before, pitchedRange.Length);
        if (afterLen > 0)
        {
            Array.Copy(src, afterStart, result, before + pitchedRange.Length, afterLen);
        }

        return result;
    }

    private void OnPitchCurveCurveChanged()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        InvalidatePitchCurveRenderCache();
        PitchCurveEditLayer.InvalidateCurve();
        if (_pitchCurve.IsConstant(out var semitones))
        {
            _pitchCurveSemitoneOffset = semitones;
            PitchCurveBar.WriteSemitonesRaw(semitones);
        }
        else
        {
            // 非定数: 欄は全体上げ下げオフセットのまま（形を定数表示で上書きしない）。
            PitchCurveBar.WriteSemitonesRaw(_pitchCurveSemitoneOffset);
        }

        SyncPitchCurveShapeButtons();
    }

    private void SyncPitchCurveShapeButtons()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        var index = PitchCurveEditLayer.SelectedPointIndex;
        var shape = index >= 0 ? _pitchCurve.OutgoingShapeOfPoint(index) : _pitchCurve.DefaultShape;
        PitchCurveBar.SyncShapeSelection(shape, ResolvePitchCurveShapeIconRising(index));
    }

    private bool ResolvePitchCurveShapeIconRising(int index)
    {
        var points = _pitchCurve.Points;
        if (index >= 0 && index + 1 < points.Count)
        {
            return points[index + 1].Semitones >= points[index].Semitones;
        }

        if (index > 0 && index < points.Count)
        {
            return points[index].Semitones >= points[index - 1].Semitones;
        }

        return true;
    }

    private void OnPitchCurveToolChanged(PitchCurveBarTool tool)
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        var next = tool switch
        {
            PitchCurveBarTool.Brush => PitchCurveEditLayer.EditTool.Brush,
            PitchCurveBarTool.SeekBar => PitchCurveEditLayer.EditTool.SeekBar,
            _ => PitchCurveEditLayer.EditTool.Points,
        };
        PitchCurveEditLayer.Tool = next;
        PitchCurveBar.SetPointsModeEnabled(next == PitchCurveEditLayer.EditTool.Points && !PitchCurveBar.SnapToScale);

        // ブラシ→ポイントへ戻すとき、規定が S 字のまま残っていればリニアへ揃える。
        if (next == PitchCurveEditLayer.EditTool.Points
            && !PitchCurveBar.SnapToScale
            && PitchCurveHasSCurveShape(_pitchCurve))
        {
            CapturePitchCurveUndo();
            _pitchCurve.SetAllSegmentShapes(FadeShape.Linear);
            _pitchCurveContinuousShape = FadeShape.Linear;
            AppStorage.Settings.PitchCurveDefaultShape = FadeShape.Linear.ToString();
            OnPitchCurveCurveChanged();
        }
    }

    private static bool PitchCurveHasSCurveShape(PitchCurve curve)
    {
        if (curve.DefaultShape == FadeShape.SCurve)
        {
            return true;
        }

        for (var i = 0; i < curve.SegmentShapes.Count; i++)
        {
            if (curve.SegmentShapes[i] == FadeShape.SCurve)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPitchCurveShapeSelected(FadeShape shape)
    {
        if (!_pitchCurveMode || PitchCurveBar.SnapToScale)
        {
            return;
        }

        if (shape == FadeShape.Constant)
        {
            shape = FadeShape.Linear;
        }

        var selectedIndex = PitchCurveEditLayer.SelectedPointIndex;
        if (selectedIndex >= 0)
        {
            if (_pitchCurve.OutgoingShapeOfPoint(selectedIndex) == shape)
            {
                return;
            }

            CapturePitchCurveUndo();
            _pitchCurve.SetPointOutgoingShape(selectedIndex, shape);
        }
        else
        {
            if (_pitchCurve.DefaultShape == shape)
            {
                return;
            }

            CapturePitchCurveUndo();
            _pitchCurve.DefaultShape = shape;
            _pitchCurveContinuousShape = shape;
            AppStorage.Settings.PitchCurveDefaultShape = _pitchCurve.DefaultShape.ToString();
        }

        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
    }

    private bool TryApplyPitchCurveShapeFromDigitKey(Key key)
    {
        if (!_pitchCurveMode || PitchCurveBar.SnapToScale || !FadeCurveIcons.TryShapeFromDigitKey(key, isFadeIn: true, out var shape))
        {
            return false;
        }

        OnPitchCurveShapeSelected(shape);
        return true;
    }

    private void PitchCurveAddPointAtPlayhead()
    {
        if (!_pitchCurveMode || _document is null)
        {
            return;
        }

        // ブラシ中でも Alt+A／追加ボタンはポイントツールへ切り替えてから打つ。
        if (PitchCurveEditLayer.Tool != PitchCurveEditLayer.EditTool.Points)
        {
            PitchCurveBar.Tool = PitchCurveBarTool.Points;
            PitchCurveEditLayer.Tool = PitchCurveEditLayer.EditTool.Points;
        }

        CapturePitchCurveUndo();
        var selection = ResolvePitchCurvePointSelection();
        bool added;
        if (!selection.IsEmpty)
        {
            added = PitchCurveEditLayer.TryAddPointsForSelection(selection, out var selectedIndex);
            if (added)
            {
                if (selectedIndex >= 0)
                {
                    PitchCurveEditLayer.SelectPoint(selectedIndex);
                }
            }
            else
            {
                var playhead = _player.IsPlaying ? _player.SmoothCursorFrame : _document.CursorFrame;
                added = PitchCurveEditLayer.TryAddPointAtPlayhead(playhead, out _);
            }
        }
        else
        {
            var playhead = _player.IsPlaying ? _player.SmoothCursorFrame : _document.CursorFrame;
            added = PitchCurveEditLayer.TryAddPointAtPlayhead(playhead, out _);
        }

        if (!added)
        {
            DiscardLastPitchCurveUndoCapture();
            return;
        }

        PitchCurveBar.Tool = PitchCurveBarTool.Points;
        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
    }

    /// <summary>Alt+A 用の波形選択。未選択なら Empty（呼び出し側がシーク位置へ 1 点を打つ）。</summary>
    private WaveSelection ResolvePitchCurvePointSelection()
    {
        if (_document is null || _pitchCurveRange.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var selection = _document.Selection.Clamp(_document.FrameCount);
        if (selection.IsEmpty)
        {
            return WaveSelection.Empty;
        }

        var start = Math.Max(selection.StartFrame, _pitchCurveRange.StartFrame);
        var end = Math.Min(selection.EndFrame, _pitchCurveRange.EndFrame);
        return end <= start ? WaveSelection.Empty : new WaveSelection(start, end);
    }

    private void OnPitchCurveReset()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        CapturePitchCurveUndo();
        _pitchCurve.SetConstant(0);
        _pitchCurveSemitoneOffset = 0;
        PitchCurveEditLayer.ClearPointSelection();
        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
    }

    private void OnPitchCurvePitchCommitted(float value, PitchCurveCommitKind _)
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        value = PitchCurve.SnapSemitone(value);

        // 定数カーブ: 欄の値＝全体ピッチ。ポイント済み: 欄の差分で全体を平行移動。
        if (_pitchCurve.IsConstant(out var currentConst))
        {
            if (Math.Abs(currentConst - value) < 1e-6f)
            {
                _pitchCurveSemitoneOffset = value;
                PitchCurveBar.WriteSemitones(value);
                return;
            }

            CapturePitchCurveUndo();
            _pitchCurve.SetConstant(value);
            _pitchCurveSemitoneOffset = value;
            OnPitchCurveCurveChanged();
            OnPitchCurveEditCompleted();
            return;
        }

        var from = _pitchCurveSemitoneOffset;
        if (Math.Abs(from - value) < 1e-6f)
        {
            PitchCurveBar.WriteSemitones(from);
            return;
        }

        var delta = value - from;
        CapturePitchCurveUndo();
        _pitchCurve.ShiftAll(delta);
        _pitchCurveSemitoneOffset = value;
        PitchCurveBar.WriteSemitones(value);
        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
    }

    private void OnPitchCurveSnapToScaleChanged(bool enabled)
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        AppStorage.Settings.PitchCurveSnapToScale = enabled;
        var wasPreviewing = _pitchPreview.Previewing;
        var ignoreCurve = _pitchCurveIgnoreCurve;
        StopPitchCurveModePreview(cancelRender: true);
        CapturePitchCurveUndo();
        PitchCurveEditLayer.SnapToScale = enabled;
        PitchCurveBar.SetPointsModeEnabled(PitchCurveEditLayer.Tool == PitchCurveEditLayer.EditTool.Points && !enabled);
        if (enabled)
        {
            _pitchCurve.QuantizeToScaleSteps();
        }
        else
        {
            RestorePitchCurveContinuousShapes();
        }

        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
        if (wasPreviewing)
        {
            _ = StartPitchCurveModePreviewAsync(ignoreCurve);
        }
    }

    /// <summary>
    /// 音階スナップ解除時: 階段補間をやめる。設定ファイルの古い形状ではなく、
    /// 解除前のユーザー選択（無ければリニア）へ戻す。S 字はピッチ規定として使わない。
    /// </summary>
    private void RestorePitchCurveContinuousShapes()
    {
        var restored = ResolvePitchCurveContinuousShape(_pitchCurveContinuousShape);
        _pitchCurveContinuousShape = restored;
        _pitchCurve.DefaultShape = restored;
        _pitchCurve.SetAllSegmentShapes(restored);
    }

    /// <summary>ピッチカーブの連続形状。S 字／無効値はリニアへ寄せる。</summary>
    private static FadeShape ResolvePitchCurveContinuousShape(FadeShape shape)
    {
        if (shape == FadeShape.SCurve
            || shape == FadeShape.Constant
            || !FadeCurves.MenuOrderFadeIn.Contains(shape))
        {
            return FadeShape.Linear;
        }

        return shape;
    }

    private void OnPitchCurveNoTimeStretchChanged(bool enabled)
    {
        AppStorage.Settings.PitchCurveNoTimeStretch = enabled;
        InvalidatePitchCurveRenderCache();
        OnPitchCurveEditCompleted();
    }

    private void OnPitchCurveLightweightModeChanged(bool enabled)
    {
        AppStorage.Settings.PitchCurveLightweightMode = enabled;
        InvalidatePitchCurveRenderCache();
        OnPitchCurveEditCompleted();
    }

    private bool WantsPitchCurveTimeStretch() => !PitchCurveBar.NoTimeStretch;

    private bool WantsPitchCurveLightweightPreview() => PitchCurveBar.LightweightMode;

    // --- undo / redo（モード内だけの軽量履歴。本体の EditHistory とは独立） ---

    private void CapturePitchCurveUndo()
    {
        _pitchCurveUndo.Add(new PitchCurveEditSnapshot(_pitchCurve.Clone(), PitchCurveEditLayer.SelectedPointIndex));
        if (_pitchCurveUndo.Count > MaxPitchCurveUndo)
        {
            _pitchCurveUndo.RemoveAt(0);
        }

        _pitchCurveRedo.Clear();
    }

    private void DiscardLastPitchCurveUndoCapture()
    {
        if (_pitchCurveUndo.Count > 0)
        {
            _pitchCurveUndo.RemoveAt(_pitchCurveUndo.Count - 1);
        }
    }

    private void UndoPitchCurveEdit()
    {
        if (!_pitchCurveMode || _pitchCurveUndo.Count == 0)
        {
            return;
        }

        var current = new PitchCurveEditSnapshot(_pitchCurve.Clone(), PitchCurveEditLayer.SelectedPointIndex);
        var previous = _pitchCurveUndo[^1];
        _pitchCurveUndo.RemoveAt(_pitchCurveUndo.Count - 1);
        _pitchCurveRedo.Add(current);
        ApplyPitchCurveSnapshot(previous);
    }

    private void RedoPitchCurveEdit()
    {
        if (!_pitchCurveMode || _pitchCurveRedo.Count == 0)
        {
            return;
        }

        var current = new PitchCurveEditSnapshot(_pitchCurve.Clone(), PitchCurveEditLayer.SelectedPointIndex);
        var next = _pitchCurveRedo[^1];
        _pitchCurveRedo.RemoveAt(_pitchCurveRedo.Count - 1);
        _pitchCurveUndo.Add(current);
        ApplyPitchCurveSnapshot(next);
    }

    private void ApplyPitchCurveSnapshot(PitchCurveEditSnapshot snapshot)
    {
        _pitchCurve.CopyFrom(snapshot.Curve);
        if (snapshot.SelectedIndex >= 0 && snapshot.SelectedIndex < _pitchCurve.Points.Count)
        {
            PitchCurveEditLayer.SelectPoint(snapshot.SelectedIndex);
        }
        else
        {
            PitchCurveEditLayer.ClearPointSelection();
        }

        OnPitchCurveCurveChanged();
        OnPitchCurveEditCompleted();
    }

    // --- プレビュー（本体 AudioPlayer へレンダリング済み音声を一時差し替え） ---

    private void InvalidatePitchCurveRenderCache()
    {
        _pitchCurveCachedAfterRange = null;
        _pitchCurveCachedPeak = 0f;
        _pitchCurveCacheKey = null;
        if (_pitchCurveMode)
        {
            PrimaryWaveform.SetPreviewSamples(null, 0);
        }
    }

    private string BuildPitchCurveRenderCacheKey(bool timeStretch, bool lightweight)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append(timeStretch ? '1' : '0').Append('|')
            .Append(lightweight ? 'L' : 'Q').Append('|')
            .Append(PitchCurveBar.SnapToScale ? 'S' : 'C').Append('|')
            .Append(EditMask()).Append('|')
            .Append(_pitchCurveRange.StartFrame).Append('-').Append(_pitchCurveRange.Length).Append('|')
            .Append(_document?.SampleRate ?? 0).Append('|')
            .Append(ClickGuard.DefaultFadeMilliseconds).Append('|')
            .Append((int)_pitchCurve.DefaultShape).Append('|');
        foreach (var value in _pitchCurve.ToPackedSamples())
        {
            sb.Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        }

        sb.Append('|');
        foreach (var id in _pitchCurve.ToShapeIds())
        {
            sb.Append(id).Append(',');
        }

        return sb.ToString();
    }

    private async Task WaitUntilPitchCurveNotRenderingAsync()
    {
        for (var i = 0; i < 6000 && _pitchCurveRendering; i++)
        {
            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 長い波形でも、変換待ちの硬直より先にすりガラスを描画する。
    /// 既に出ているときは再キャプチャせず、0% のまま進捗更新へつなぐ。
    /// </summary>
    private async Task ShowPitchShiftBusyGlassAndYieldAsync()
    {
        if (_busyGlass.IsShowingBusy)
        {
            _busyGlass.SetProgress(0);
            return;
        }

        ShowPitchShiftBusyGlass();
        _busyGlass.SetProgress(0);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    /// <summary>
    /// ピッチ処理結果を取得。キャッシュが効けば再利用、なければ計算してキャッシュする。
    /// <paramref name="forPreview"/> が true かつ軽量モード ON のときだけ軽量演算。
    /// OK 本適用は forPreview=false で常に通常品質（軽量キャッシュは使わない）。
    /// </summary>
    private async Task<(float[] AfterRange, float Peak)?> EnsurePitchCurveRenderAsync(
        CancellationToken cancellationToken,
        bool forPreview,
        bool showProgress = true)
    {
        if (_document is null || _pitchCurveRange.IsEmpty)
        {
            return null;
        }

        var timeStretch = WantsPitchCurveTimeStretch();
        var lightweight = forPreview && WantsPitchCurveLightweightPreview();
        var key = BuildPitchCurveRenderCacheKey(timeStretch, lightweight);
        if (_pitchCurveCachedAfterRange is not null && _pitchCurveCacheKey == key)
        {
            return (_pitchCurveCachedAfterRange, _pitchCurveCachedPeak);
        }

        // 先にすりガラスを描いてから重い変換へ（Yield しないと表示が遅れる）。
        if (showProgress)
        {
            await ShowPitchShiftBusyGlassAndYieldAsync().ConfigureAwait(true);
            _pitchShiftBusy = true;
        }

        _pitchCurveRendering = true;

        Exception? error = null;
        PitchCurveRenderResult rendered = default;
        try
        {
            var curve = _pitchCurve.Clone();
            var document = _document;
            var range = _pitchCurveRange;
            var mask = EditMask();
            var edgeFadeMs = ClickGuard.DefaultFadeMilliseconds;
            IProgress<double>? progress = showProgress
                ? new Progress<double>(p => _busyGlass.SetProgress(p))
                : null;
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
            if (!IsLoaded || !ReferenceEquals(_document, document))
            {
                return null;
            }

            _pitchCurveCachedAfterRange = rendered.AfterRange;
            _pitchCurveCachedPeak = rendered.PeakAbs;
            _pitchCurveCacheKey = key;
            return (_pitchCurveCachedAfterRange, _pitchCurveCachedPeak);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            error = ex;
            InvalidatePitchCurveRenderCache();
            return null;
        }
        finally
        {
            _pitchCurveRendering = false;
            if (showProgress)
            {
                _pitchShiftBusy = false;
                if (error is null)
                {
                    _busyGlass.BeginFadeOut();
                }
                else
                {
                    _busyGlass.HideOverlay();
                }

                RefreshTransportCommandsEnabled();
            }

            if (error is not null && IsLoaded && showProgress)
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

    /// <summary>Space: 現在カーブを反映したプレビュー再生／停止。</summary>
    private void TogglePitchCurvePreview()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        if (_pitchPreview.Previewing)
        {
            StopPitchCurveModePreview(cancelRender: true);
            return;
        }

        // 通常再生と同じく、選択してから Space したときだけ一通。L はループ。
        _selectionPlayOneShot = IsSelectionPlayWindow();
        _ = StartPitchCurveModePreviewAsync(ignoreCurve: false);
    }

    /// <summary>「ピッチカーブを無視して再生」: モード切替＋押した時点で元波形再生（または停止）。</summary>
    private void TogglePitchCurveBypassPreview()
    {
        if (!_pitchCurveMode)
        {
            return;
        }

        if (_pitchPreview.Previewing)
        {
            if (_pitchCurveIgnoreCurve)
            {
                _pitchCurveIgnoreCurve = false;
                PitchCurveBar.SetBypassActive(false);
                StopPitchCurveModePreview(cancelRender: true);
                return;
            }

            _pitchCurveIgnoreCurve = true;
            PitchCurveBar.SetBypassActive(true);
            _ = StartPitchCurveModePreviewAsync(ignoreCurve: true);
            return;
        }

        _pitchCurveIgnoreCurve = !_pitchCurveIgnoreCurve;
        PitchCurveBar.SetBypassActive(_pitchCurveIgnoreCurve);
        if (_pitchCurveIgnoreCurve)
        {
            _ = StartPitchCurveModePreviewAsync(ignoreCurve: true);
        }
    }

    private async Task StartPitchCurveModePreviewAsync(bool ignoreCurve)
    {
        if (!_pitchCurveMode || _document is null)
        {
            return;
        }

        var op = ++_pitchCurveOpEpoch;
        var needsPitchRender = !ignoreCurve && !_pitchCurve.IsNoOp();
        // バックグラウンド計測の完了待ちや変換前に、先にすりガラスを出す。
        if (needsPitchRender)
        {
            await ShowPitchShiftBusyGlassAndYieldAsync().ConfigureAwait(true);
        }

        try
        {
            StopPitchCurveModePreview(cancelRender: true);
            await WaitUntilPitchCurveNotRenderingAsync().ConfigureAwait(true);
            if (op != _pitchCurveOpEpoch || !_pitchCurveMode || !IsLoaded)
            {
                if (needsPitchRender)
                {
                    _busyGlass.BeginFadeOut();
                }

                return;
            }

            _pitchCurveIgnoreCurve = ignoreCurve;
            PitchCurveBar.SetBypassActive(ignoreCurve);

            float[] pitched;
            if (!needsPitchRender)
            {
                pitched = _document.CopyRange(_pitchCurveRange.StartFrame, _pitchCurveRange.Length);
            }
            else
            {
                _pitchCurveRenderCts?.Cancel();
                _pitchCurveRenderCts?.Dispose();
                _pitchCurveRenderCts = new CancellationTokenSource();
                var token = _pitchCurveRenderCts.Token;
                var rendered = await EnsurePitchCurveRenderAsync(token, forPreview: true).ConfigureAwait(true);
                if (op != _pitchCurveOpEpoch || rendered is null || token.IsCancellationRequested || !_pitchCurveMode || !IsLoaded)
                {
                    // Ensure がキャッシュ命中で finally を通らない／キャンセル時はここで消す。
                    if (_busyGlass.IsShowingBusy)
                    {
                        _busyGlass.BeginFadeOut();
                    }

                    return;
                }

                pitched = rendered.Value.AfterRange;
                if (pitched.Length < _document.Channels)
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorPitchShiftFailed,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                // キャッシュ命中時は Ensure がすりガラスを閉じないので、再生前に消す。
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }

                // プレビュー用レンダ結果でラウドネスも揃える（キャッシュ命中）。
                OnPitchCurveEditCompleted();
            }

            if (op != _pitchCurveOpEpoch || !_pitchCurveMode || !IsLoaded)
            {
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
            _pitchCurvePreviewPlaybackFrames = Math.Max(1, previewDoc.FrameCount);
            _pitchCurvePreviewToSourceMap = BuildPitchCurvePreviewToSourceMap(pitched, ignoreCurve);
            _pitchPreview.ResumeFrame = _document.CursorFrame;
            StartPitchCurvePreviewPlayback(previewDoc, previewRange, _pitchCurveRange.StartFrame);
        }
        catch (Exception ex)
        {
            StopPitchCurveModePreview(cancelRender: true);
            if (_busyGlass.IsShowingBusy)
            {
                _busyGlass.HideOverlay();
            }

            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// プレビュー再生カーソルを本体タイムライン上のフレームへ写す。
    /// 長さ据え置き時は Origin+preview。タイムストレッチなしは読み位置マップ（無ければ比例）。
    /// </summary>
    private long MapPitchCurvePreviewCursorToHost(long previewCursorFrame)
    {
        var range = _pitchCurveRange;
        var rangeLength = Math.Max(1L, range.Length);
        if (_pitchCurvePreviewToSourceMap is { Length: > 0 } map)
        {
            var index = (int)Math.Clamp(previewCursorFrame, 0, map.Length - 1);
            var offset = Math.Clamp(map[index], 0, (int)Math.Min(int.MaxValue, rangeLength - 1));
            return range.StartFrame + offset;
        }

        var previewFrames = Math.Max(1L, _pitchCurvePreviewPlaybackFrames);
        if (previewFrames == rangeLength)
        {
            return range.StartFrame + Math.Clamp(previewCursorFrame, 0, rangeLength);
        }

        var t = Math.Clamp(previewCursorFrame / (double)previewFrames, 0d, 1d);
        return range.StartFrame + (long)Math.Floor(t * rangeLength);
    }

    /// <summary>
    /// 本体タイムライン上の位置をプレビュー PCM 上のフレームへ写す（シーク／再生窓用）。
    /// </summary>
    private long MapPitchCurveHostCursorToPreview(long hostFrame)
    {
        var range = _pitchCurveRange;
        var rangeLength = Math.Max(1L, range.Length);
        var previewFrames = Math.Max(1L, _pitchCurvePreviewPlaybackFrames);
        var sourceOffset = Math.Clamp(hostFrame - range.StartFrame, 0L, rangeLength);
        if (sourceOffset >= rangeLength)
        {
            return previewFrames;
        }

        if (_pitchCurvePreviewToSourceMap is { Length: > 0 } map)
        {
            return FindPreviewFrameForSourceOffset(map, (int)sourceOffset);
        }

        if (previewFrames == rangeLength)
        {
            return sourceOffset;
        }

        return (long)Math.Floor(sourceOffset / (double)rangeLength * previewFrames);
    }

    /// <summary>ソースオフセット以上を初めて指すプレビューフレーム（半開終端は map.Length）。</summary>
    private static int FindPreviewFrameForSourceOffset(int[] map, int sourceOffset)
    {
        if (map.Length == 0)
        {
            return 0;
        }

        var lo = 0;
        var hi = map.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >> 1;
            if (map[mid] < sourceOffset)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return Math.Clamp(lo, 0, map.Length);
    }

    /// <summary>本体の再生窓をプレビュー PCM 座標へ。交差が無ければ null。</summary>
    private WaveSelection? MapPitchCurveHostRangeToPreview(WaveSelection host)
    {
        if (host.IsEmpty || _pitchCurvePreviewPlaybackFrames <= 0 || _pitchCurveRange.IsEmpty)
        {
            return null;
        }

        var range = _pitchCurveRange;
        var start = Math.Max(host.StartFrame, range.StartFrame);
        var end = Math.Min(host.EndFrame, range.EndFrame);
        if (end <= start)
        {
            return null;
        }

        var previewStart = MapPitchCurveHostCursorToPreview(start);
        var previewEnd = MapPitchCurveHostCursorToPreview(end);
        previewStart = Math.Clamp(previewStart, 0, _pitchCurvePreviewPlaybackFrames);
        previewEnd = Math.Clamp(previewEnd, 0, _pitchCurvePreviewPlaybackFrames);
        if (previewEnd <= previewStart)
        {
            previewEnd = Math.Min(_pitchCurvePreviewPlaybackFrames, previewStart + 1);
        }

        return new WaveSelection(previewStart, previewEnd);
    }

    /// <summary>タイムストレッチなしのときだけ、ApplyCurve と同じ読み進めの対応表を作る。</summary>
    private int[]? BuildPitchCurvePreviewToSourceMap(float[] pitched, bool ignoreCurve)
    {
        if (_document is null
            || ignoreCurve
            || WantsPitchCurveTimeStretch()
            || _pitchCurve.IsNoOp()
            || _pitchCurveRange.IsEmpty)
        {
            return null;
        }

        var sourceFrames = (int)Math.Min(int.MaxValue, Math.Max(1, _pitchCurveRange.Length));
        var previewFrames = pitched.Length / Math.Max(1, _document.Channels);
        if (previewFrames <= 0 || previewFrames == sourceFrames)
        {
            return null;
        }

        var table = _pitchCurve.BuildSemitoneTable(sourceFrames);
        var smooth = !WantsPitchCurveLightweightPreview();
        var map = PitchShift.BuildNoTimeStretchSourceMap(table, _document.SampleRate, smooth);
        return map.Length == previewFrames ? map : null;
    }

    private void StopPitchCurveModePreview(bool cancelRender = false)
    {
        if (cancelRender)
        {
            _pitchCurveRenderCts?.Cancel();
        }

        if (_pitchPreview.Previewing)
        {
            StopPitchCurvePreview(restoreCursor: true);
        }

        _pitchCurvePreviewToSourceMap = null;
        _pitchCurvePreviewPlaybackFrames = 0;
        _pitchCurveIgnoreCurve = false;
        PitchCurveBar.SetBypassActive(false);
    }

    /// <summary>
    /// OK。EnsurePitchRender で本品質レンダリングし、ピークが 0 dBFS を超えるときは確認してから適用する
    /// （Cancel なら編集継続）。旧ダイアログの Accept と同じクリップガード。
    /// </summary>
    private async Task AcceptPitchCurveAsync()
    {
        if (!_pitchCurveMode || IsUiBusy || _document is null)
        {
            return;
        }

        StopPitchCurveLoudnessRefresh();
        var curve = _pitchCurve.Clone();
        var range = _pitchCurveRange.Clamp(_document.FrameCount);
        if (curve.IsNoOp())
        {
            ExitPitchCurveMode();
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

        // 変換待ちに入る前にすりガラスを出す（長い波形の硬直対策）。
        await ShowPitchShiftBusyGlassAndYieldAsync().ConfigureAwait(true);

        StopPitchCurveModePreview(cancelRender: true);
        var timeStretch = WantsPitchCurveTimeStretch();
        var gain = 1f;
        float[]? precomputed;
        try
        {
            _pitchCurveRenderCts?.Cancel();
            _pitchCurveRenderCts?.Dispose();
            _pitchCurveRenderCts = new CancellationTokenSource();
            // OK 本適用・ピーク判定は常に通常品質（軽量プレビューキャッシュは使わない）。
            var rendered = await EnsurePitchCurveRenderAsync(_pitchCurveRenderCts.Token, forPreview: false).ConfigureAwait(true);
            if (rendered is null || !IsLoaded || !_pitchCurveMode)
            {
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }

                return;
            }

            // キャッシュ命中時は Ensure が閉じないので、確認ダイアログの前に消す。
            if (_busyGlass.IsShowingBusy)
            {
                _busyGlass.BeginFadeOut();
            }

            precomputed = rendered.Value.AfterRange;
            var peak = rendered.Value.Peak;
            if (LoudnessPenGain.WouldClip(peak))
            {
                var lowerDbText = FormatPitchCurveLowerDb(peak);
                var answer = OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ConfirmPitchCurveClip(lowerDbText),
                    UiStrings.AppName,
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);
                if (answer == MessageBoxResult.Cancel)
                {
                    PitchCurveEditLayer.Focus();
                    return;
                }

                if (answer == MessageBoxResult.Yes)
                {
                    gain = Math.Clamp(LoudnessPenGain.HeadroomLinear / Math.Max(peak, 1e-8f), 1e-4f, 1f);
                }
            }
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ExitPitchCurveMode();
        await ApplyPitchCurveAsync(
            curve,
            range,
            gain,
            ClickGuard.DefaultFadeMilliseconds,
            timeStretch,
            precomputed).ConfigureAwait(true);
    }

    /// <summary>
    /// ピッチカーブ編集モード中のキー処理。モード外は false を返し、通常のショートカット処理へ委ねる。
    /// </summary>
    private bool TryProcessPitchCurveShortcut(Key key, ModifierKeys modifiers)
    {
        if (!_pitchCurveMode)
        {
            return false;
        }

        // Esc は TryProcessEscapeShortcut 側（ポイントフォーカス解除のみ。モード退出はしない）。
        if (key == Key.Escape)
        {
            return false;
        }

        // P では退出しない（再入場も不要）。警告も出さず飲み込む。
        if (key == Key.P && modifiers == ModifierKeys.None)
        {
            return true;
        }

        // Alt+O: OK で編集確定（数値欄フォーカス中も有効）。クリップ時は確認。
        if (key == Key.O
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            _ = AcceptPitchCurveAsync();
            return true;
        }

        // Alt+C: Cancel。
        if (key == Key.C
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            CancelPitchCurveMode();
            return true;
        }

        var focusedTextBox = Keyboard.FocusedElement is System.Windows.Controls.TextBox;

        if (key == Key.Z && modifiers == ModifierKeys.Control)
        {
            UndoPitchCurveEdit();
            return true;
        }

        if ((key == Key.Y && modifiers == ModifierKeys.Control)
            || (key == Key.Z && modifiers == (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            RedoPitchCurveEdit();
            return true;
        }

        // Space: ピッチ反映プレビューの再生／停止（原音の通常再生には渡さない）。
        if (key == Key.Space && modifiers == ModifierKeys.None)
        {
            TogglePitchCurvePreview();
            return true;
        }

        if (key == Key.N && (modifiers & ModifierKeys.Alt) != 0 && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            PitchCurveBar.FocusPitchBox();
            return true;
        }

        if (key == Key.A && (modifiers & ModifierKeys.Alt) != 0 && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            PitchCurveAddPointAtPlayhead();
            return true;
        }

        // Alt+F: シークバーモード（波形クリックでシーク）。
        if (key == Key.F
            && (modifiers & ModifierKeys.Alt) != 0
            && (modifiers & (ModifierKeys.Control | ModifierKeys.Windows)) == 0)
        {
            PitchCurveBar.Tool = PitchCurveBarTool.SeekBar;
            OnPitchCurveToolChanged(PitchCurveBarTool.SeekBar);
            return true;
        }

        if (focusedTextBox)
        {
            // 数字・矢印キー等はバーの数値欄側へ渡す。
            return false;
        }

        if (key == Key.Delete && PitchCurveEditLayer.Tool == PitchCurveEditLayer.EditTool.Points)
        {
            PitchCurveEditLayer.TryDeleteSelectedPoint();
            return true;
        }

        if (modifiers == ModifierKeys.None
            && PitchCurveEditLayer.Tool == PitchCurveEditLayer.EditTool.Points
            && PitchCurveEditLayer.SelectedPointIndex >= 0
            && TryApplyPitchCurveShapeFromDigitKey(key))
        {
            return true;
        }

        if (key is Key.Up or Key.Down && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift))
        {
            var step = modifiers == ModifierKeys.Shift ? 12f : 1f;
            if (PitchCurveEditLayer.TryNudgeSelectedSemitones(key == Key.Up ? step : -step))
            {
                return true;
            }
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.Alt)
        {
            if (PitchCurveEditLayer.TrySelectAdjacentPoint(key == Key.Left ? -1 : 1))
            {
                return true;
            }
        }

        // ポイント選択中のみ ←→ で制御点を時間移動。未選択時は通常のシークへ渡す。
        if (key is Key.Left or Key.Right
            && (modifiers == ModifierKeys.None || modifiers == ModifierKeys.Shift)
            && PitchCurveEditLayer.Tool == PitchCurveEditLayer.EditTool.Points
            && PitchCurveEditLayer.SelectedPointIndex >= 0)
        {
            if (PitchCurveEditLayer.BeginOrContinueTimeNudge(key == Key.Left ? -1 : 1))
            {
                return true;
            }
        }

        // マーカー追加（M／Ins）、マーカークリック（Q）、ループへ（L）、時刻フォーカス（G）は通す。
        if (modifiers == ModifierKeys.None && key is Key.M or Key.Insert or Key.Q or Key.L or Key.G)
        {
            return false;
        }

        // 他の破壊編集・モード切替キーはモード中ブロック（警告付き）。V は音量カーブとの相互排他。
        if (modifiers == ModifierKeys.None
            && key is Key.S or Key.E or Key.C or Key.X or Key.I or Key.O or Key.N or Key.R
                or Key.V or Key.T or Key.B or Key.U)
        {
            WarnPitchCurveModeBlocked();
            return true;
        }

        return false;
    }
}
