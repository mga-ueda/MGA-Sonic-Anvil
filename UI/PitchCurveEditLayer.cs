using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>
/// メイン波形（<see cref="WaveformView"/>）の手前に重ねるピッチカーブ編集オーバーレイ。
/// 波形そのものは描かない。赤いカーブ・制御点・半音ガイド・（音階スナップ時）左端ピアノ鍵盤を描画し、
/// マウス編集（ポイント／ブラシ／セグメント平行リフト／線・ポイント上の Ctrl＋ドラッグ＝全体上下、
/// 線の既定＝自由／Shift＝水平／Alt＝垂直、ポイントの Alt＝垂直／Shift＝水平固定）を扱う。
/// <see cref="VolumeCurveEditLayer"/> と同じ編集ジェスチャーを半音の線形軸に合わせて移植したもの
/// （旧 <c>PitchCurveEditorDialog.PitchCurveWavePreview</c> のマッピング／スナップ／鍵盤を踏襲し、
/// 波形ビットマップ描画は持たない）。
/// </summary>
internal sealed class PitchCurveEditLayer : Control
{
    internal enum EditTool
    {
        Brush,
        Points,
        /// <summary>編集せず波形へクリックを通し、シークできるようにする。</summary>
        SeekBar,
    }

    /// <summary>表示ピッチ範囲は上下 2 オクターブ（±24）固定。ダイアログ版のようなピッチズームは持たない。</summary>
    private const float PitchAxisSemitones = 24f;

    /// <summary>左端ピアノ鍵盤の幅（音階スナップ時のみ。波形左の dB レーン上に重ねる）。</summary>
    private const double KeyboardWidthDip = 40;

    /// <summary>半音目盛り（"+24" 等）用ガター。</summary>
    private const double PitchLabelWidthDip = 40;

    /// <summary>上下の目盛りが ClipToBounds で切れない余白。</summary>
    private const double PitchAxisPadDip = 20;

    private const float ZeroSnapRadius = 0.35f;
    private const double TimelineSnapPx = 8;
    private const int TimeNudgeRepeatDelayMs = 400;
    private const int TimeNudgeRepeatIntervalMs = 50;

    private WaveformView? _target;
    private PitchCurve _curve = PitchCurve.Flat(0);
    private WaveSelection _range;
    private EditTool _tool = EditTool.Points;
    private bool _snapToScale;
    private bool _dragging;
    private int _dragPoint = -1;
    /// <summary>ポイントドラッグ開始時の t / 半音。Alt・Shift 軸固定の基準（途中押下でもここへ戻す）。</summary>
    private double _dragOriginT;
    private float _dragOriginSemitones;
    /// <summary>点と点のあいだを掴んでいるセグメント。未使用時は -1。</summary>
    private int _dragSegment = -1;
    private float _segStartSemi0;
    private float _segStartSemi1;
    private double _segStartT0;
    private double _segStartT1;
    private double _segOriginX;
    private double _segOriginY;
    /// <summary>Ctrl＋ドラッグの全体シフト。</summary>
    private bool _shiftAll;
    private PitchCurve? _shiftStartCurve;
    private float _shiftRefSemitones;
    private double _shiftOriginY;
    private readonly DispatcherTimer _timeNudgeTimer;
    private int _timeNudgeDirection;
    private bool _timeNudgeRepeatStarted;
    /// <summary>ドラッグ／押し続け移動で、1 ジェスチャにつき undo を 1 回だけ積む。</summary>
    private bool _pointEditCapturedForGesture;
    /// <summary>ブラシ一筆の直前サンプル t。区間内の旧点削除に使う。未開始は null。</summary>
    private double? _brushLastT;

    private Pen _curvePen = null!;
    private Pen _pitchGridPen = null!;
    private Pen _pitchOctaveGridPen = null!;
    private Pen _selectedPointPen = null!;
    private Pen _timeGuidePen = null!;
    private Brush _pointFill = null!;
    private Brush _pointLabelBrush = null!;

    public event Action? CurveChanged;

    public event Action? SelectionChanged;

    /// <summary>端点ドラッグで編集範囲が広がったとき。呼び出し側の <c>_pitchCurveRange</c> と同期する。</summary>
    public event Action? RangeChanged;

    /// <summary>ポイント／線のドラッグやキー移動が一段落したとき。</summary>
    public event Action? CurveEditCompleted;

    /// <summary>ポイント追加・移動・半音変更の直前。呼び出し側が undo スナップショットを積む。</summary>
    public event Action? BeforePointEdit;

    /// <summary>キャプチャしたが実際のピッチ変更が無かったときの取り消し。</summary>
    public event Action? DiscardPointEditCapture;

    public int SelectedPointIndex => _dragPoint;

    /// <summary>ホイールズーム等を委譲する対象波形（MainWindow 側のオリジン探索用）。</summary>
    public WaveformView? TargetWaveform => _target;

    public EditTool Tool
    {
        get => _tool;
        set
        {
            if (_tool == value)
            {
                return;
            }

            _tool = value;
            Cursor = value switch
            {
                EditTool.Brush => Cursors.Pen,
                EditTool.SeekBar => Cursors.IBeam,
                _ => Cursors.Cross,
            };
            if ((value == EditTool.Brush || value == EditTool.SeekBar) && _dragPoint >= 0)
            {
                _dragPoint = -1;
                SelectionChanged?.Invoke();
            }

            InvalidateVisual();
        }
    }

    /// <summary>音階スナップ中は MapPoint・ガイド描画の挙動が変わる（整数半音へスナップ）。</summary>
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

    public WaveSelection Range
    {
        get => _range;
        set
        {
            _range = value;
            _dragPoint = -1;
            _dragSegment = -1;
            InvalidateVisual();
        }
    }

    public PitchCurveEditLayer()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        Focusable = true;
        ClipToBounds = true;
        // Control は Background が null だと空きクリックが下へ抜ける。
        Background = Brushes.Transparent;
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Cursor = Cursors.Cross;
        _timeNudgeTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatDelayMs),
        };
        _timeNudgeTimer.Tick += (_, _) => OnTimeNudgeTick();
        Unloaded += (_, _) => StopTimeNudge();
        RebuildPens();
    }

    /// <summary>色設定変更時にペン／ブラシを Theme から作り直す。</summary>
    public void RefreshAppearance()
    {
        RebuildPens();
        InvalidateVisual();
    }

    private void RebuildPens()
    {
        _curvePen = CreateCurvePen();
        _pitchGridPen = CreatePitchGridPen();
        _pitchOctaveGridPen = CreatePitchOctaveGridPen();
        _selectedPointPen = CreateSelectedPointPen();
        _timeGuidePen = CreateTimeGuidePen();
        _pointFill = CreatePointFill();
        _pointLabelBrush = CreatePointLabelBrush();
    }

    /// <summary>重ねる対象波形。ビューポート（ズーム／パン）を共有する。</summary>
    public void Attach(WaveformView target)
    {
        if (ReferenceEquals(_target, target))
        {
            return;
        }

        if (_target is not null)
        {
            _target.ViewChanged -= OnTargetViewChanged;
            _target.PreviewMouseLeftButtonDown -= OnTargetPreviewMouseLeftButtonDown;
        }

        _target = target;
        _target.ViewChanged += OnTargetViewChanged;
        _target.PreviewMouseLeftButtonDown += OnTargetPreviewMouseLeftButtonDown;
    }

    private void OnTargetViewChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <summary>
    /// オーバーレイが空きクリックを波形へ透過したとき、波形側のクリックでもポイント選択を解除する。
    /// </summary>
    private void OnTargetPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsHitTestVisible || Visibility != Visibility.Visible || _dragPoint < 0)
        {
            return;
        }

        ClearPointSelection();
    }

    /// <summary>画面上おおよそ 14px 相当の時間ヒット半径（カーブ正規化 t）。</summary>
    private double PointHitTRadius()
    {
        var rangeLen = Math.Max(1d, _range.Length - 1);
        var framesPerPixel = ViewSpanFrames / Math.Max(1d, ContentWidth);
        return Math.Max(1e-9, 14d * framesPerPixel / rangeLen);
    }

    /// <summary>
    /// ブラシの同一点マージ幅。ファイル全長の固定比率だとズームイン時に
    /// 画面全体が 1 点に潰れ、自由曲線が描けなくなる。
    /// </summary>
    private double BrushMergeEpsilonT()
    {
        var rangeLen = Math.Max(1d, _range.Length - 1);
        var framesPerPixel = ViewSpanFrames / Math.Max(1d, ContentWidth);
        return Math.Max(1e-9, 2.5 * framesPerPixel / rangeLen);
    }

    /// <summary>画面上おおよそ 10px 相当の半音ヒット半径。</summary>
    private float PointHitSemitoneRadius() =>
        (float)Math.Max(0.3, 10d * SemitonesPerPixel());

    /// <summary>画面上おおよそ 14px 相当の半音ヒット半径（セグメント用）。</summary>
    private float SegmentHitSemitoneRadius() =>
        (float)Math.Max(0.5, 14d * SemitonesPerPixel());

    private double SemitonesPerPixel()
    {
        var amp = Math.Max(1d, ContentHeight * 0.5 - PitchAxisPad);
        return PitchAxisSemitones / amp;
    }

    /// <summary>
    /// ポイント／セグメント／ブラシ以外はヒットしない → 下の波形へクリックが届く。
    /// シークバーモードは常に透過（カーブ上でもシークできる）。
    /// </summary>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (!IsHitTestVisible || Visibility != Visibility.Visible || _target is null || _range.IsEmpty)
        {
            return null;
        }

        if (_tool == EditTool.SeekBar)
        {
            return null;
        }

        var pos = hitTestParameters.HitPoint;
        if (pos.X < ContentX || pos.Y < 0 || pos.X > ActualWidth || pos.Y > ActualHeight)
        {
            return null;
        }

        if (_tool == EditTool.Brush)
        {
            return new PointHitTestResult(this, pos);
        }

        MapPoint(pos, out var t, out var semitones);
        if (_curve.HitTestPoint(t, semitones, PointHitTRadius(), PointHitSemitoneRadius()) >= 0)
        {
            return new PointHitTestResult(this, pos);
        }

        var segRadius = SegmentHitSemitoneRadius();
        if (_curve.HitTestSegment(t, semitones, segRadius) >= 0)
        {
            return new PointHitTestResult(this, pos);
        }

        return null;
    }

    public void InvalidateCurve() => InvalidateVisual();

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

    /// <summary>
    /// Esc 用。選択中ポイント／進行中のポイントドラッグがあれば解除して true。
    /// </summary>
    public bool TryClearPointFocus()
    {
        var hadPointFocus = _dragPoint >= 0;
        if (_dragging && _dragPoint >= 0)
        {
            var edited = _pointEditCapturedForGesture;
            _dragging = false;
            _dragSegment = -1;
            _shiftAll = false;
            _shiftStartCurve = null;
            _pointEditCapturedForGesture = false;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            if (edited)
            {
                CurveEditCompleted?.Invoke();
            }
        }

        if (!hadPointFocus)
        {
            return false;
        }

        ClearPointSelection();
        return true;
    }

    public bool TryDeleteSelectedPoint()
    {
        if (_dragPoint < 0 || !_curve.RemovePoint(_dragPoint))
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
    /// 本体のシアン再生ヘッド（シーク位置）にポイントを追加して選択する。
    /// 編集範囲外のときは false。
    /// </summary>
    public bool TryAddPointAtPlayhead(long playheadFrame, out int newIndex)
    {
        newIndex = -1;
        if (_curve.Points.Count < 2 || _range.IsEmpty)
        {
            return false;
        }

        if (playheadFrame < _range.StartFrame || playheadFrame > _range.EndFrame)
        {
            return false;
        }

        var t = FrameToCurveT(playheadFrame);
        // 端点は常時ポイントがあるので、内側へわずかに寄せて新規挿入できるようにする。
        const double edgePad = 1e-4;
        if (t <= edgePad)
        {
            t = edgePad;
        }
        else if (t >= 1d - edgePad)
        {
            t = 1d - edgePad;
        }

        Tool = EditTool.Points;
        var semitones = _curve.SampleAt(t);
        if (_snapToScale)
        {
            semitones = PitchCurve.SnapToScale(semitones);
        }

        // Alt+A 系の追加区間は、音階スナップ中は階段（Constant）、それ以外はリニア。
        var segmentShape = _snapToScale ? FadeShape.Constant : FadeShape.Linear;
        newIndex = _curve.Upsert(t, semitones, mergeEpsilon: 0.5 / Math.Max(1L, _range.Length - 1), segmentShape: segmentShape);
        SelectPoint(newIndex);
        CurveChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 選択範囲を台形の上辺にし、外側 2 点を選択の前後に少し離して打つ（計 4 点）。
    /// 内側 = 選択の始点・終点。外側へ出る余地が無い辺は、その辺だけ内側へわずかに退避する。
    /// 編集範囲と交差しない／狭すぎるときは false。
    /// </summary>
    public bool TryAddPointsForSelection(WaveSelection selection, out int selectedIndex)
    {
        selectedIndex = -1;
        if (_curve.Points.Count < 2 || _range.IsEmpty || selection.IsEmpty)
        {
            return false;
        }

        var start = Math.Max(selection.StartFrame, _range.StartFrame);
        var endExclusive = Math.Min(selection.EndFrame, _range.EndFrame);
        if (endExclusive <= start + 1)
        {
            return false;
        }

        var endInclusive = endExclusive - 1;
        var selLen = endExclusive - start;
        var pad = PlateauPadFrames(selLen, _target?.Document?.SampleRate ?? 0);
        var leftRoom = start - _range.StartFrame;
        var rightRoom = _range.EndFrame - endExclusive;
        var leftPad = Math.Min(pad, leftRoom);
        var rightPad = Math.Min(pad, rightRoom);

        // 半開区間。外側は選択の前後、内側は選択端。
        var outer0 = start - leftPad;
        var inner0 = start;
        var inner1 = endInclusive;
        var outer1 = endInclusive + rightPad;

        // 外側へ出る余地が無い辺は、内側点を選択端に残しつつ外側点だけわずかに内側へ。
        var minGap = Math.Max(1L, Math.Min(pad, Math.Max(1L, selLen / 6)));
        if (outer0 >= inner0)
        {
            outer0 = Math.Max(_range.StartFrame, inner0);
            inner0 = Math.Min(inner1 - minGap, outer0 + minGap);
        }

        if (outer1 <= inner1)
        {
            outer1 = Math.Min(Math.Max(_range.StartFrame, _range.EndFrame - 1), inner1);
            inner1 = Math.Max(inner0 + minGap, outer1 - minGap);
        }

        if (!(outer0 < inner0 && inner0 < inner1 && inner1 < outer1))
        {
            return false;
        }

        var length = Math.Max(1L, _range.Length - 1);
        var t0 = FrameToCurveT(outer0);
        var t1 = FrameToCurveT(inner0);
        var t2 = FrameToCurveT(inner1);
        var t3 = FrameToCurveT(outer1);
        var minStep = 1.0 / length;
        if (!(t0 + minStep * 0.5 < t1 && t1 + minStep * 0.5 < t2 && t2 + minStep * 0.5 < t3))
        {
            return false;
        }

        Tool = EditTool.Points;
        // 正規化 t の固定 epsilon だと長い編集範囲で離れた選択まで既存点へ吸い込まれる。
        // 半フレーム分だけ同一扱いし、意図したフレームへ正確に打つ。
        var eps = 0.5 / length;
        UpsertSnapped(t0, eps);
        UpsertSnapped(t1, eps);
        UpsertSnapped(t2, eps);
        UpsertSnapped(t3, eps);
        if (!_snapToScale)
        {
            // 音階スナップ中は階段（Constant）を保つ。OFF 時だけ台形区間をリニアに揃える。
            EnsureLinearSegmentsBetween(t0, t3);
        }

        if (_curve.Points.Count < 4)
        {
            return false;
        }

        // 内側始点の index を返す（呼び出し側が選択するかどうかは任せる）。
        selectedIndex = IndexOfNearestPoint(t1);
        CurveChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    private void UpsertSnapped(double t, double eps)
    {
        var semitones = _curve.SampleAt(t);
        if (_snapToScale)
        {
            semitones = PitchCurve.SnapToScale(semitones);
        }

        var segmentShape = _snapToScale ? FadeShape.Constant : FadeShape.Linear;
        _curve.Upsert(t, semitones, mergeEpsilon: eps, segmentShape: segmentShape);
    }

    /// <summary>指定 t 範囲に重なる区間をリニアにする（Alt+A 台形用）。</summary>
    private void EnsureLinearSegmentsBetween(double t0, double t3)
    {
        var lo = Math.Min(t0, t3);
        var hi = Math.Max(t0, t3);
        for (var i = 0; i < _curve.Points.Count - 1; i++)
        {
            var a = _curve.Points[i].T;
            var b = _curve.Points[i + 1].T;
            if (b < lo || a > hi)
            {
                continue;
            }

            _curve.SetSegmentShape(i, FadeShape.Linear);
        }
    }

    /// <summary>台形の外側足を選択端から離すフレーム数。編集範囲の拡張側と共有する。</summary>
    internal static long PlateauPadFrames(long selectionLength, int sampleRate = 0) =>
        VolumeCurveEditLayer.PlateauPadFrames(selectionLength, sampleRate);

    private int IndexOfNearestPoint(double t)
    {
        var best = 0;
        var bestDist = double.MaxValue;
        for (var i = 0; i < _curve.Points.Count; i++)
        {
            var dist = Math.Abs(_curve.Points[i].T - t);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = i;
            }
        }

        return best;
    }

    private double FrameToCurveT(long frame)
    {
        var length = Math.Max(1L, _range.Length - 1);
        return Math.Clamp((frame - _range.StartFrame) / (double)length, 0d, 1d);
    }

    private long TToFrame(double t)
    {
        var length = Math.Max(1L, _range.Length - 1);
        return _range.StartFrame + (long)Math.Round(Math.Clamp(t, 0d, 1d) * length);
    }

    /// <summary>
    /// 端点を含むポイント移動。波形途中の最終端でも時間方向に動かせるよう、
    /// 内側へは端点切り離し、外側へは編集範囲を拡張する。
    /// </summary>
    private void MoveDraggedPoint(double t, float semitones)
    {
        if (_dragPoint < 0 || _dragPoint >= _curve.Points.Count)
        {
            return;
        }

        if (_dragPoint == 0)
        {
            _dragPoint = MoveFirstPoint(t, semitones);
            return;
        }

        if (_dragPoint == _curve.Points.Count - 1)
        {
            _dragPoint = MoveLastPoint(t, semitones);
            return;
        }

        _curve.MovePoint(_dragPoint, t, semitones);
    }

    private int MoveLastPoint(double t, float semitones)
    {
        var frame = TToFrame(t);
        var prevFrame = TToFrame(_curve.Points[^2].T);
        frame = Math.Max(frame, prevFrame + 1);
        var docFrames = _target?.Document?.FrameCount ?? _range.EndFrame;
        if (docFrames > 0)
        {
            frame = Math.Min(frame, docFrames - 1);
        }

        var endInclusive = _range.EndFrame - 1;
        if (frame < endInclusive)
        {
            return _curve.DetachLastEndpoint(FrameToCurveT(frame), semitones);
        }

        if (frame > endInclusive)
        {
            RemapRangeKeepingAbsoluteFrames(
                new WaveSelection(_range.StartFrame, frame + 1),
                moveLastToFrame: frame,
                moveFirstToFrame: null,
                semitones);
            return _curve.Points.Count - 1;
        }

        _curve.MovePoint(_curve.Points.Count - 1, 1, semitones);
        return _curve.Points.Count - 1;
    }

    private int MoveFirstPoint(double t, float semitones)
    {
        var frame = TToFrame(t);
        var nextFrame = TToFrame(_curve.Points[1].T);
        frame = Math.Min(frame, nextFrame - 1);
        frame = Math.Max(0, frame);

        var start = _range.StartFrame;
        if (frame > start)
        {
            return _curve.DetachFirstEndpoint(FrameToCurveT(frame), semitones);
        }

        if (frame < start)
        {
            var docFrames = _target?.Document?.FrameCount ?? _range.EndFrame;
            var end = Math.Min(docFrames, _range.EndFrame);
            RemapRangeKeepingAbsoluteFrames(
                new WaveSelection(frame, end),
                moveLastToFrame: null,
                moveFirstToFrame: frame,
                semitones);
            return 0;
        }

        _curve.MovePoint(0, 0, semitones);
        return 0;
    }

    private void RemapRangeKeepingAbsoluteFrames(
        WaveSelection newRange,
        long? moveLastToFrame,
        long? moveFirstToFrame,
        float semitones)
    {
        var docFrames = _target?.Document?.FrameCount ?? newRange.EndFrame;
        newRange = newRange.Clamp(docFrames);
        if (newRange.IsEmpty || newRange.Length <= 1)
        {
            return;
        }

        var old = _range;
        var oldLen = Math.Max(1L, old.Length - 1);
        var newLen = Math.Max(1L, newRange.Length - 1);
        var points = new List<PitchCurvePoint>(_curve.Points.Count);
        for (var i = 0; i < _curve.Points.Count; i++)
        {
            var point = _curve.Points[i];
            long frame;
            float semi;
            if (moveFirstToFrame is long first && i == 0)
            {
                frame = first;
                semi = semitones;
            }
            else if (moveLastToFrame is long last && i == _curve.Points.Count - 1)
            {
                frame = last;
                semi = semitones;
            }
            else
            {
                frame = old.StartFrame + (long)Math.Round(point.T * oldLen);
                semi = point.Semitones;
            }

            frame = Math.Clamp(frame, newRange.StartFrame, newRange.EndFrame - 1);
            var nt = (frame - newRange.StartFrame) / (double)newLen;
            points.Add(new PitchCurvePoint(nt, semi));
        }

        var shapes = _curve.SegmentShapes.ToList();
        _range = newRange;
        _curve.ReplacePoints(points, shapes);
        RangeChanged?.Invoke();
    }

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

        InvalidateVisual();
    }

    /// <summary>選択中ポイントを半音単位で上下。↑↓＝±1 半音、Shift＝±12（1 オクターブ）。</summary>
    public bool TryNudgeSelectedSemitones(float deltaSemitones)
    {
        if (_tool != EditTool.Points || _dragPoint < 0 || Math.Abs(deltaSemitones) < 1e-6f)
        {
            return false;
        }

        NotifyBeforePointEditOnce();
        var point = _curve.Points[_dragPoint];
        var next = PitchCurve.SnapSemitone(point.Semitones + deltaSemitones);
        if (_snapToScale)
        {
            next = PitchCurve.SnapToScale(next);
        }

        _curve.MovePoint(_dragPoint, point.T, next);
        CurveChanged?.Invoke();
        InvalidateVisual();
        CurveEditCompleted?.Invoke();
        return true;
    }

    public bool TrySelectAdjacentPoint(int direction)
    {
        if (_tool != EditTool.Points || direction == 0 || _curve.Points.Count == 0)
        {
            return false;
        }

        if (_dragPoint < 0)
        {
            _dragPoint = direction < 0 ? _curve.Points.Count - 1 : 0;
            SelectionChanged?.Invoke();
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
        InvalidateVisual();
        return true;
    }

    public bool BeginOrContinueTimeNudge(int direction)
    {
        if (direction == 0 || _target is null || _tool != EditTool.Points || _dragPoint < 0)
        {
            return false;
        }

        if (_timeNudgeDirection == direction && _timeNudgeTimer.IsEnabled)
        {
            return true;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        // 端点で時間移動できないときだけ表示パンに逃がす（未選択時のシーク代用にはしない）。
        if (!TryNudgeSelectedTimeWithUndo(direction, fast) && !TryPanTimeAxis(direction, fast))
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

    public void StopTimeNudge()
    {
        var edited = _pointEditCapturedForGesture;
        _timeNudgeDirection = 0;
        _timeNudgeRepeatStarted = false;
        _pointEditCapturedForGesture = false;
        _timeNudgeTimer.Stop();
        _timeNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimeNudgeRepeatDelayMs);
        if (edited)
        {
            CurveEditCompleted?.Invoke();
        }
    }

    public void ResetPointEditCapture() => _pointEditCapturedForGesture = false;

    private void OnTimeNudgeTick()
    {
        if (_timeNudgeDirection == 0 || !IsTimeNudgeHeld(_timeNudgeDirection))
        {
            StopTimeNudge();
            return;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (_tool != EditTool.Points || _dragPoint < 0)
        {
            StopTimeNudge();
            return;
        }

        if (!TryNudgeSelectedTime(_timeNudgeDirection, fast))
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

    private static bool IsTimeNudgeHeld(int direction)
    {
        var arrowDown = direction < 0 ? Keyboard.IsKeyDown(Key.Left) : Keyboard.IsKeyDown(Key.Right);
        return arrowDown && (Keyboard.Modifiers & ModifierKeys.Alt) == 0;
    }

    private bool TryNudgeSelectedTimeWithUndo(int direction, bool fast)
    {
        var alreadyCaptured = _pointEditCapturedForGesture;
        NotifyBeforePointEditOnce();
        if (TryNudgeSelectedTime(direction, fast))
        {
            return true;
        }

        if (!alreadyCaptured)
        {
            _pointEditCapturedForGesture = false;
            DiscardPointEditCapture?.Invoke();
        }

        return false;
    }

    private bool TryNudgeSelectedTime(int direction, bool fast)
    {
        if (_tool != EditTool.Points || direction == 0 || _range.IsEmpty || _dragPoint < 0 || _target is null)
        {
            return false;
        }

        var contentW = Math.Max(1, _target.ContentAreaWidth);
        var span = Math.Max(1d, _target.ViewSpanFrames);
        var length = Math.Max(1d, _range.Length - 1);
        var pixels = fast ? 3d : 1d;
        var deltaFrames = direction * pixels * (span / contentW);
        var deltaT = deltaFrames / length;
        var point = _curve.Points[_dragPoint];
        var before = point.T;
        var beforeFrame = TToFrame(before);
        var nextT = before + deltaT;
        MoveDraggedPoint(nextT, point.Semitones);
        if (_dragPoint < 0 || _dragPoint >= _curve.Points.Count)
        {
            return false;
        }

        if (Math.Abs(TToFrame(_curve.Points[_dragPoint].T) - beforeFrame) <= 0)
        {
            return false;
        }

        CurveChanged?.Invoke();
        InvalidateVisual();
        return true;
    }

    private bool TryPanTimeAxis(int direction, bool fast)
    {
        if (_target is null || _range.IsEmpty)
        {
            return false;
        }

        var fraction = fast ? 0.15 : 0.05;
        _target.PanByVisibleFraction(direction * fraction);
        InvalidateVisual();
        return true;
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

    // --- マッピング（対象波形のビューポートへ委譲） ---

    private double ContentX => _target?.ContentOffsetX ?? 0;

    private double ContentWidth => Math.Max(1, _target?.ContentAreaWidth ?? Math.Max(1, ActualWidth));

    private double ContentTop => _target?.ContentAreaTop ?? 0;

    private double ContentHeight => Math.Max(1, _target?.ContentAreaHeight ?? Math.Max(1, ActualHeight));

    private double ViewStartFrames => _target?.ViewStart ?? _range.StartFrame;

    private double ViewSpanFrames => Math.Max(1, _target?.ViewSpanFrames ?? Math.Max(1, _range.Length));

    private static double PitchAxisPad => DesignMetrics.From96(PitchAxisPadDip);

    /// <summary>音階スナップ時だけ鍵盤を出す。左レーン（dB 列の位置）いっぱいに描く。</summary>
    private double KeyboardGutter =>
        _snapToScale
            ? Math.Max(ContentX, DesignMetrics.From96(KeyboardWidthDip))
            : 0;

    private double PitchLabelGutter => DesignMetrics.From96(PitchLabelWidthDip);

    private double FrameToX(double frame) =>
        ContentX + (frame - ViewStartFrames) / ViewSpanFrames * ContentWidth;

    /// <summary>0 半音は軸の中央。±<see cref="PitchAxisSemitones"/> を上下いっぱいに線形表示する。</summary>
    private double SemitoneToY(float semitones)
    {
        var top = ContentTop;
        var height = ContentHeight;
        var mid = top + height * 0.5;
        var amp = Math.Max(1d, height * 0.5 - PitchAxisPad);
        var n = semitones / PitchAxisSemitones;
        return mid - n * amp;
    }

    private float YToSemitoneRaw(double y)
    {
        var top = ContentTop;
        var height = ContentHeight;
        var mid = top + height * 0.5;
        var amp = Math.Max(1d, height * 0.5 - PitchAxisPad);
        var n = (mid - y) / amp;
        return PitchCurve.Snap((float)(n * PitchAxisSemitones));
    }

    private static string FormatPointSemitoneLabel(float semitones) =>
        Math.Abs(semitones - MathF.Round(semitones)) < 1e-3f
            ? semitones.ToString("+0;-0;0", CultureInfo.InvariantCulture)
            : semitones.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);

    private void MapPoint(Point pos, out double t, out float semitones, bool snapTimeline = false, double? escapeFromT = null)
    {
        var contentW = ContentWidth;
        var localX = Math.Clamp(pos.X - ContentX, 0, contentW);
        var frame = ViewStartFrames + (localX / contentW) * ViewSpanFrames;
        t = _range.Length <= 1
            ? 0
            : Math.Clamp((frame - _range.StartFrame) / (double)(_range.Length - 1), 0, 1);
        if (snapTimeline)
        {
            t = SnapTToTimeline(t, escapeFromT);
        }

        var raw = YToSemitoneRaw(pos.Y);
        if (_snapToScale)
        {
            semitones = PitchCurve.SnapToScale(raw);
        }
        else
        {
            semitones = _tool == EditTool.Brush
                ? PitchCurve.Snap(raw)
                : PitchCurve.SoftSnapZero(raw, ZeroSnapRadius);
        }
    }

    /// <summary>
    /// ドキュメントにマーカー／リージョンがあるとき、近傍フレームへ時間軸スナップする。
    /// <paramref name="escapeFromT"/> を渡すと、現在位置への引き戻しだけは無視して離れられる。
    /// </summary>
    private double SnapTToTimeline(double t, double? escapeFromT = null)
    {
        if (_target?.Document is not { } doc || _range.IsEmpty || _range.Length <= 1)
        {
            return t;
        }

        var markers = doc.Markers;
        var regions = doc.Regions;
        if (markers.Count == 0 && regions.Count == 0)
        {
            return t;
        }

        var length = _range.Length - 1;
        var frame = _range.StartFrame + Math.Clamp(t, 0, 1) * length;
        var snapFrames = ViewSpanFrames * (TimelineSnapPx / Math.Max(1d, ContentWidth));
        var bestDist = snapFrames;
        long? best = null;

        void Consider(long candidate)
        {
            if (candidate < _range.StartFrame || candidate > _range.EndFrame)
            {
                return;
            }

            var dist = Math.Abs(candidate - frame);
            if (dist > bestDist)
            {
                return;
            }

            bestDist = dist;
            best = candidate;
        }

        for (var i = 0; i < markers.Count; i++)
        {
            Consider(markers[i].Frame);
        }

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            Consider(region.StartFrame);
            Consider(region.EndFrame);
        }

        if (best is not long snapped)
        {
            return t;
        }

        var snappedT = Math.Clamp((snapped - _range.StartFrame) / (double)length, 0, 1);
        // いま吸着している点からキー／ドラッグで離れようとしているときは引き戻さない。
        if (escapeFromT is double from
            && Math.Abs(snappedT - from) <= 1e-12
            && Math.Abs(t - from) > 1e-12)
        {
            return t;
        }

        return snappedT;
    }

    // --- 描画 ---

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_target is null || ActualWidth < 2 || ActualHeight < 2 || _range.IsEmpty)
        {
            return;
        }

        var keyboard = KeyboardGutter;
        var labelW = PitchLabelGutter;
        // 鍵盤は dB レーン上。ラベルは鍵盤の右（スナップ時）またはレーン右寄せ（非スナップ時）。
        var labelX = _snapToScale ? ContentX : Math.Max(0, ContentX - labelW);
        DrawPitchGuides(dc, ContentX, ContentWidth, labelX, labelW);
        if (_snapToScale)
        {
            DrawKeyboard(dc, keyboard);
        }

        DrawCurve(dc);
    }

    private void DrawPitchGuides(
        DrawingContext dc,
        double contentX,
        double contentW,
        double labelX,
        double labelW)
    {
        var maxGuide = (int)PitchAxisSemitones;
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var labelBrush = _pointLabelBrush;
        var minLabelGap = DesignMetrics.From96(13);
        double? lastLabelY = null;
        var top = ContentTop;
        var bottom = ContentTop + ContentHeight;
        for (var semi = -maxGuide; semi <= maxGuide; semi++)
        {
            var y = SemitoneToY(semi);
            if (y < top - 1 || y > bottom + 1)
            {
                continue;
            }

            if (semi != 0)
            {
                var pen = semi % 12 == 0 ? _pitchOctaveGridPen : _pitchGridPen;
                dc.DrawLine(pen, new Point(contentX, y), new Point(contentX + contentW, y));
            }

            // オクターブは常にラベル。半音ごとだと密集するので 12 刻み。
            if (semi % 12 != 0)
            {
                continue;
            }

            if (lastLabelY is double prev && Math.Abs(y - prev) < minLabelGap)
            {
                continue;
            }

            var label = semi.ToString("+0;-0;0", CultureInfo.InvariantCulture);
            var fontSize = DesignMetrics.From96(10);
            var text = WpfControlHelpers.MonoRegularText(label, fontSize, labelBrush, pixelsPerDip);
            var tx = labelX + Math.Max(0, labelW - text.Width - 4);
            var ty = Math.Clamp(y - text.Height * 0.5, top, Math.Max(top, bottom - text.Height));
            DrawPitchLabelWithBackdrop(dc, label, fontSize, labelBrush, pixelsPerDip, tx, ty);
            lastLabelY = y;
        }

        // 0 半音線は ±12 と同じオクターブ線の色・太さ。
        var zeroY = SemitoneToY(0);
        dc.DrawLine(_pitchOctaveGridPen, new Point(contentX, zeroY), new Point(contentX + contentW, zeroY));
    }

    /// <summary>ピッチ数値ラベル向けの黒半透明背景（軸目盛／ポイント値）。</summary>
    private static void DrawPitchLabelWithBackdrop(
        DrawingContext dc,
        string label,
        double fontSize,
        Brush fillBrush,
        double pixelsPerDip,
        double x,
        double y)
    {
        var fill = WpfControlHelpers.MonoRegularText(label, fontSize, fillBrush, pixelsPerDip);
        var padX = DesignMetrics.From96(3);
        var padY = DesignMetrics.From96(1);
        var radius = DesignMetrics.From96(2);
        var bg = new SolidColorBrush(Color.FromArgb(0xB0, 0x00, 0x00, 0x00));
        bg.Freeze();
        dc.DrawRoundedRectangle(
            bg,
            null,
            new Rect(
                x - padX,
                y - padY,
                fill.Width + padX * 2,
                fill.Height + padY * 2),
            radius,
            radius);
        dc.DrawText(fill, new Point(x, y));
    }

    /// <summary>
    /// 左端鍵盤。半音 0 を C 相当とし、白鍵＝自然音、黒鍵＝シャープ。
    /// 波形の dB レーン上に重ねる（カーブ座標はずらさない）。
    /// 鍵同士の隙間と軽い角丸で区切りを見せる。
    /// </summary>
    private void DrawKeyboard(DrawingContext dc, double gutter)
    {
        if (gutter < 2)
        {
            return;
        }

        var maxGuide = (int)Math.Ceiling(PitchAxisSemitones);
        var top = ContentTop;
        var height = ContentHeight;
        var bottom = top + height;
        // 純白／純黒より一段落ち着かせる（背景や枠とのコントラストを保ちつつ眩しさを抑える）。
        var white = new SolidColorBrush(Color.FromRgb(0xC4, 0xC4, 0xC4));
        var black = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));
        var border = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
        white.Freeze();
        black.Freeze();
        border.Freeze();
        var borderPen = new Pen(border, 0.6);
        borderPen.Freeze();

        var keyGap = DesignMetrics.From96(1.4);
        var keyRadius = DesignMetrics.From96(2.2);
        // 鍵の外側／隙間は塗らない（下の波形背景をそのまま見せる）。

        // 各半音スロットは白鍵か黒鍵のどちらか一方だけ（黒鍵位置に白を混ぜない）。
        for (var semi = -maxGuide; semi <= maxGuide; semi++)
        {
            var degree = ((semi % 12) + 12) % 12;
            var isBlack = degree is 1 or 3 or 6 or 8 or 10;

            var yTop = SemitoneToY(semi + 0.5f);
            var yBot = SemitoneToY(semi - 0.5f);
            if (yBot < yTop)
            {
                (yTop, yBot) = (yBot, yTop);
            }

            yTop = Math.Clamp(yTop, top, bottom);
            yBot = Math.Clamp(yBot, top, bottom);
            // 上下に隙間を空けて隣鍵と分離する。
            yTop += keyGap * 0.5;
            yBot -= keyGap * 0.5;
            if (yBot - yTop < 0.75)
            {
                continue;
            }

            var keyW = gutter - keyGap * 0.35;
            var rect = new Rect(0, yTop, keyW, yBot - yTop);
            var radius = Math.Min(keyRadius, Math.Min(rect.Width, rect.Height) * 0.45);
            dc.DrawRoundedRectangle(isBlack ? black : white, borderPen, rect, radius, radius);
        }

        dc.DrawLine(
            new Pen(border, 1),
            new Point(gutter - 0.5, top),
            new Point(gutter - 0.5, bottom));
    }

    private float SemitonesAtDocumentFrame(long frame)
    {
        if (_range.IsEmpty)
        {
            return 0f;
        }

        // 表示端まで伸ばすときは端点の半音を保持する。
        if (frame < _range.StartFrame)
        {
            return _curve.SampleAt(0);
        }

        if (frame >= _range.EndFrame)
        {
            return _curve.SampleAt(1);
        }

        return _curve.SampleAt(FrameToCurveT(frame));
    }

    private void DrawCurve(DrawingContext dc)
    {
        var contentW = ContentWidth;
        if (contentW < 1 || _curve.Points.Count == 0)
        {
            return;
        }

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            var started = false;
            void EmitXy(double x, double y)
            {
                if (!started)
                {
                    ctx.BeginFigure(new Point(x, y), false, false);
                    started = true;
                    return;
                }

                ctx.LineTo(new Point(x, y), true, false);
            }

            void EmitFrame(long frame, float semitones)
            {
                EmitXy(FrameToX(frame), SemitoneToY(semitones));
            }

            // 表示左端 → 全制御点（ファイル先頭/終端の点を含む）→ 表示右端。
            var viewLeft = (long)Math.Floor(ViewStartFrames);
            var viewRight = (long)Math.Ceiling(ViewStartFrames + ViewSpanFrames);
            EmitFrame(viewLeft, SemitonesAtDocumentFrame(viewLeft));

            var length = Math.Max(0, _range.Length - 1);
            for (var i = 0; i < _curve.Points.Count; i++)
            {
                var point = _curve.Points[i];
                var frame = _range.StartFrame + (long)Math.Round(point.T * length);
                EmitFrame(frame, point.Semitones);
                if (i >= _curve.Points.Count - 1)
                {
                    continue;
                }

                var next = _curve.Points[i + 1];
                var shape = i < _curve.SegmentShapes.Count ? _curve.SegmentShapes[i] : _curve.DefaultShape;
                if (shape == FadeShape.Linear)
                {
                    continue;
                }

                if (shape == FadeShape.Constant)
                {
                    // 階段: 横→縦で角を出す。
                    var cx = FrameToX(_range.StartFrame + next.T * length);
                    EmitXy(cx, SemitoneToY(point.Semitones));
                    continue;
                }

                var spanT = next.T - point.T;
                if (spanT <= 1e-12)
                {
                    continue;
                }

                var x0 = FrameToX(frame);
                var x1 = FrameToX(_range.StartFrame + next.T * length);
                var steps = Math.Clamp((int)Math.Ceiling(Math.Abs(x1 - x0) / 4.0), 4, 64);
                for (var s = 1; s < steps; s++)
                {
                    var t = point.T + spanT * (s / (double)steps);
                    EmitFrame(
                        _range.StartFrame + (long)Math.Round(t * length),
                        _curve.SampleAt(t));
                }
            }

            EmitFrame(viewRight, SemitonesAtDocumentFrame(viewRight));
        }

        geo.Freeze();
        dc.DrawGeometry(null, _curvePen, geo);

        if (_tool != EditTool.Points)
        {
            return;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // 選択中ポイントの時間ガイド（ドラッグ中でなくても表示）。
        if (_dragPoint >= 0 && _dragPoint < _curve.Points.Count)
        {
            var guidePoint = _curve.Points[_dragPoint];
            var guideFrame = _range.StartFrame + guidePoint.T * Math.Max(0, _range.Length - 1);
            var guideX = FrameToX(guideFrame);
            var snappedX = WpfControlHelpers.SnapDeviceCenter(guideX, pixelsPerDip);
            var top = ContentTop;
            var bottom = ContentTop + ContentHeight;
            if (bottom <= top + 1)
            {
                top = 0;
                bottom = ActualHeight;
            }

            dc.DrawLine(_timeGuidePen, new Point(snappedX, top), new Point(snappedX, bottom));
        }

        for (var i = 0; i < _curve.Points.Count; i++)
        {
            var point = _curve.Points[i];
            var frame = _range.StartFrame + point.T * Math.Max(0, _range.Length - 1);
            var x = FrameToX(frame);
            var y = SemitoneToY(point.Semitones);
            var selected = i == _dragPoint;
            var radius = selected ? 6.5 : 4.5;
            dc.DrawEllipse(_pointFill, selected ? _selectedPointPen : _curvePen, new Point(x, y), radius, radius);

            var label = FormatPointSemitoneLabel(point.Semitones);
            var fontSize = DesignMetrics.From96(10);
            var text = WpfControlHelpers.MonoRegularText(
                label,
                fontSize,
                _pointLabelBrush,
                pixelsPerDip);
            var labelX = x + radius + DesignMetrics.From96(3);
            var labelY = y - text.Height * 0.5;
            var maxX = ContentX + ContentWidth - text.Width - 1;
            if (labelX > maxX)
            {
                labelX = x - radius - DesignMetrics.From96(3) - text.Width;
            }

            labelY = Math.Clamp(labelY, ContentTop, ContentTop + ContentHeight - text.Height);
            DrawPitchLabelWithBackdrop(dc, label, fontSize, _pointLabelBrush, pixelsPerDip, labelX, labelY);
        }
    }

    // --- マウス編集 ---

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_target is null || _range.IsEmpty || ActualWidth < 2 || ActualHeight < 2)
        {
            return;
        }

        var pos = e.GetPosition(this);
        if (pos.X < ContentX)
        {
            e.Handled = true;
            return;
        }

        MapPoint(pos, out var t, out var semitones);
        if (_tool == EditTool.Points)
        {
            var prev = _dragPoint;
            var hitPoint = _curve.HitTestPoint(t, semitones, PointHitTRadius(), PointHitSemitoneRadius());
            _dragSegment = -1;
            if (hitPoint >= 0)
            {
                // ポイント掴み。Alt=垂直 / Shift=水平 / Ctrl=全体上下（途中押下も原点基準で有効）。
                _dragPoint = hitPoint;
                var origin = _curve.Points[_dragPoint];
                _dragOriginT = origin.T;
                _dragOriginSemitones = origin.Semitones;
                // 途中で Ctrl を足した全体上下用の基準（開始時カーブ）。
                _shiftStartCurve = _curve.Clone();
                _shiftRefSemitones = origin.Semitones;
                _shiftOriginY = pos.Y;
                _shiftAll = false;
                if (_dragPoint != prev)
                {
                    SelectionChanged?.Invoke();
                }

                _dragging = true;
                CaptureMouse();
                Cursor = PointDragCursor(Keyboard.Modifiers);
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            // ポイント以外: 選択解除。線上の Ctrl＝全体シフト、線＝自由（Shift＝左右／Alt＝上下）。
            if (prev >= 0)
            {
                _dragPoint = -1;
                SelectionChanged?.Invoke();
                InvalidateVisual();
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                BeginShiftAll(pos, t);
                e.Handled = true;
                return;
            }

            var segRadius = SegmentHitSemitoneRadius();
            _dragSegment = _curve.HitTestSegment(t, semitones, segRadius);
            if (_dragSegment >= 0)
            {
                var a = _curve.Points[_dragSegment];
                var b = _curve.Points[_dragSegment + 1];
                _segStartSemi0 = a.Semitones;
                _segStartSemi1 = b.Semitones;
                _segStartT0 = a.T;
                _segStartT1 = b.T;
                _segOriginX = pos.X;
                _segOriginY = pos.Y;
                // 途中で Ctrl を足した全体上下用の基準（開始時カーブ）。
                _shiftStartCurve = _curve.Clone();
                _shiftRefSemitones = Math.Max(_segStartSemi0, _segStartSemi1);
                _shiftOriginY = pos.Y;
                _shiftAll = false;
                _dragging = true;
                CaptureMouse();
                Cursor = SegmentDragCursor(Keyboard.Modifiers);
                e.Handled = true;
                return;
            }

            e.Handled = false;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 && _tool != EditTool.Brush)
        {
            BeginShiftAll(pos, t);
            e.Handled = true;
            return;
        }

        _dragging = true;
        CaptureMouse();
        if (_tool == EditTool.Brush)
        {
            _brushLastT = null;
            NotifyBeforePointEditOnce();
        }

        UpsertDrawn(t, semitones);
        CurveChanged?.Invoke();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var pos = e.GetPosition(this);
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateHoverCursor(pos);
            return;
        }

        // ブラシはマーカー吸着なし（自由描画）。ポイント移動だけタイムラインスナップする。
        double? escapeFromT = _tool == EditTool.Points && _dragPoint >= 0 && _dragPoint < _curve.Points.Count
            ? _curve.Points[_dragPoint].T
            : null;
        MapPoint(
            pos,
            out var t,
            out var semitones,
            snapTimeline: _tool == EditTool.Points,
            escapeFromT: escapeFromT);
        if (_shiftAll && _shiftStartCurve is not null)
        {
            NotifyBeforePointEditOnce();
            var deltaSemi = DeltaSemitoneFromDrag(_shiftRefSemitones, _shiftOriginY, pos.Y);
            var snappedDelta = SnapDragDeltaSemitones(_shiftRefSemitones, deltaSemi);
            _curve.RestoreShiftedFrom(_shiftStartCurve, snappedDelta);
            CurveChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        if (_tool == EditTool.Points && _dragSegment >= 0)
        {
            ApplySegmentDrag(pos);
            e.Handled = true;
            return;
        }

        if (_tool == EditTool.Points && _dragPoint >= 0 && _dragPoint < _curve.Points.Count)
        {
            ApplyPointDrag(pos);
            e.Handled = true;
            return;
        }

        if (_tool == EditTool.Brush)
        {
            UpsertDrawn(t, semitones);
            CurveChanged?.Invoke();
            InvalidateVisual();
        }

        e.Handled = true;
    }

    /// <summary>
    /// ポイントドラッグ中の移動。Alt/Shift はドラッグ開始時点の座標を軸固定の基準にする
    /// （途中押下でも「いまの位置」ではなく元ポイントへ制限する）。
    /// Ctrl を押しているあいだは開始時カーブ全体を同じ Δ半音で上下する。
    /// </summary>
    private void ApplyPointDrag(Point pos)
    {
        if (_dragPoint < 0 || _dragPoint >= _curve.Points.Count)
        {
            return;
        }

        NotifyBeforePointEditOnce();
        var mods = Keyboard.Modifiers;
        if ((mods & ModifierKeys.Control) != 0 && _shiftStartCurve is not null)
        {
            var deltaSemi = DeltaSemitoneFromDrag(_shiftRefSemitones, _shiftOriginY, pos.Y);
            var snappedDelta = SnapDragDeltaSemitones(_shiftRefSemitones, deltaSemi);
            _curve.RestoreShiftedFrom(_shiftStartCurve, snappedDelta);
            Cursor = Cursors.SizeNS;
            CurveChanged?.Invoke();
            InvalidateVisual();
            return;
        }

        if (_shiftStartCurve is not null)
        {
            // Ctrl 解除後は全体シフトを巻き戻してから、ポイント移動を再適用する。
            _curve.CopyFrom(_shiftStartCurve);
        }

        MapPoint(pos, out var t, out var semitones, snapTimeline: true, escapeFromT: _dragOriginT);
        // Alt: 時間固定（垂直のみ） / Shift: 半音固定（水平のみ）
        // 音階スナップ中は Alt でも半音は整数音階へスナップしたまま（MapPoint 側）。
        if ((mods & ModifierKeys.Alt) != 0 && (mods & ModifierKeys.Shift) == 0)
        {
            t = _dragOriginT;
            Cursor = Cursors.SizeNS;
        }
        else if ((mods & ModifierKeys.Shift) != 0 && (mods & ModifierKeys.Alt) == 0)
        {
            semitones = _dragOriginSemitones;
            Cursor = Cursors.SizeWE;
        }
        else
        {
            Cursor = Cursors.SizeAll;
        }

        MoveDraggedPoint(t, semitones);
        CurveChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>
    /// 線（セグメント）ドラッグ。既定は自由移動、Shift＝左右、Alt＝上下。
    /// Ctrl を押しているあいだは開始時カーブ全体を同じ Δ半音で上下する。
    /// </summary>
    private void ApplySegmentDrag(Point pos)
    {
        if (_dragSegment < 0 || _shiftStartCurve is null)
        {
            return;
        }

        NotifyBeforePointEditOnce();
        var mods = Keyboard.Modifiers;
        var refSemi = _shiftRefSemitones;
        var deltaSemi = DeltaSemitoneFromDrag(refSemi, _segOriginY, pos.Y);
        var snappedDelta = SnapDragDeltaSemitones(refSemi, deltaSemi);
        if ((mods & ModifierKeys.Control) != 0)
        {
            _curve.RestoreShiftedFrom(_shiftStartCurve, snappedDelta);
            Cursor = Cursors.SizeNS;
        }
        else
        {
            // Ctrl 解除後は全体シフトを巻き戻し、線だけ再適用する。
            _curve.CopyFrom(_shiftStartCurve);
            var t0 = _segStartT0;
            var t1 = _segStartT1;
            var semi0 = _segStartSemi0 + snappedDelta;
            var semi1 = _segStartSemi1 + snappedDelta;
            if (_snapToScale)
            {
                semi0 = PitchCurve.SnapToScale(semi0);
                semi1 = PitchCurve.SnapToScale(semi1);
            }
            var shiftOnly = (mods & ModifierKeys.Shift) != 0 && (mods & ModifierKeys.Alt) == 0;
            var altOnly = (mods & ModifierKeys.Alt) != 0 && (mods & ModifierKeys.Shift) == 0;
            if (!altOnly)
            {
                var deltaT = DeltaTFromDrag(_segOriginX, pos.X);
                (t0, t1) = ClampSegmentTranslate(_dragSegment, _segStartT0, _segStartT1, deltaT);
            }

            if (shiftOnly)
            {
                semi0 = _segStartSemi0;
                semi1 = _segStartSemi1;
                Cursor = Cursors.SizeWE;
            }
            else if (altOnly)
            {
                Cursor = Cursors.SizeNS;
            }
            else
            {
                Cursor = Cursors.SizeAll;
            }

            _curve.SetSegmentEnds(_dragSegment, t0, semi0, t1, semi1);
        }

        CurveChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>ドラッグ中に修飾キーが変わったら、マウスを動かさなくても即反映する。</summary>
    private void RefreshDragFromModifiers()
    {
        if (!_dragging || _tool != EditTool.Points || Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var pos = Mouse.GetPosition(this);
        if (_dragSegment >= 0)
        {
            ApplySegmentDrag(pos);
            return;
        }

        if (_dragPoint < 0 || _dragPoint >= _curve.Points.Count)
        {
            return;
        }

        ApplyPointDrag(pos);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl or Key.System)
        {
            RefreshDragFromModifiers();
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Key is Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl or Key.System)
        {
            RefreshDragFromModifiers();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        var edited = _pointEditCapturedForGesture;
        _dragging = false;
        _dragSegment = -1;
        _shiftAll = false;
        _shiftStartCurve = null;
        _brushLastT = null;
        _pointEditCapturedForGesture = false;
        ReleaseMouseCapture();
        if (_tool == EditTool.Brush)
        {
            // 音量カーブと同様、間引き後の規定形状はリニア（音階スナップ時のみ階段）。
            // DefaultShape が SCurve のままだと Simplify が S 字を残してしまう。
            if (_snapToScale)
            {
                _curve.Simplify(semiTolerance: 0.08f, segmentShape: FadeShape.Constant);
                _curve.SetAllSegmentShapes(FadeShape.Constant);
            }
            else
            {
                _curve.Simplify(semiTolerance: 0.08f, segmentShape: FadeShape.Linear);
                _curve.SetAllSegmentShapes(FadeShape.Linear);
            }

            CurveChanged?.Invoke();
            InvalidateVisual();
            edited = true;
        }

        UpdateHoverCursor(e.GetPosition(this));
        e.Handled = true;
        if (edited)
        {
            CurveEditCompleted?.Invoke();
        }
    }

    private void BeginShiftAll(Point pos, double t)
    {
        _shiftAll = true;
        _shiftOriginY = pos.Y;
        _shiftStartCurve = _curve.Clone();
        _shiftRefSemitones = _curve.SampleAt(t);
        _dragPoint = -1;
        _dragSegment = -1;
        _dragging = true;
        CaptureMouse();
        Cursor = Cursors.SizeNS;
        InvalidateVisual();
    }

    private static Cursor PointDragCursor(ModifierKeys mods)
    {
        if ((mods & ModifierKeys.Control) != 0)
        {
            return Cursors.SizeNS;
        }

        if ((mods & ModifierKeys.Alt) != 0 && (mods & ModifierKeys.Shift) == 0)
        {
            return Cursors.SizeNS;
        }

        if ((mods & ModifierKeys.Shift) != 0 && (mods & ModifierKeys.Alt) == 0)
        {
            return Cursors.SizeWE;
        }

        return Cursors.SizeAll;
    }

    private static Cursor SegmentDragCursor(ModifierKeys mods)
    {
        if ((mods & ModifierKeys.Control) != 0)
        {
            return Cursors.SizeNS;
        }

        if ((mods & ModifierKeys.Shift) != 0 && (mods & ModifierKeys.Alt) == 0)
        {
            return Cursors.SizeWE;
        }

        if ((mods & ModifierKeys.Alt) != 0 && (mods & ModifierKeys.Shift) == 0)
        {
            return Cursors.SizeNS;
        }

        return Cursors.SizeAll;
    }

    private float DeltaSemitoneFromDrag(float startRefSemitones, double originY, double currentY)
    {
        var startY = SemitoneToY(startRefSemitones);
        var movedY = startY + (currentY - originY);
        var currentSemi = YToSemitoneRaw(movedY);
        var delta = currentSemi - startRefSemitones;
        // 音階スナップ中は Alt の 0.1 半音細かい移動も使わず、整数半音へ揃える。
        var step = !_snapToScale && (Keyboard.Modifiers & ModifierKeys.Alt) != 0 ? 0.1f : 1f;
        return (float)(Math.Round(delta / step) * step);
    }

    /// <summary>ドラッグ Δ 半音を SoftSnap／音階スナップで整える。</summary>
    private float SnapDragDeltaSemitones(float startRefSemitones, float deltaSemitones)
    {
        if (_snapToScale)
        {
            return PitchCurve.SnapToScale(startRefSemitones + deltaSemitones) - startRefSemitones;
        }

        return PitchCurve.SoftSnapZero(startRefSemitones + deltaSemitones, ZeroSnapRadius) - startRefSemitones;
    }

    private double DeltaTFromDrag(double originX, double currentX)
    {
        var contentW = Math.Max(1d, ContentWidth);
        var frameDelta = ((currentX - originX) / contentW) * ViewSpanFrames;
        var length = Math.Max(1L, _range.Length - 1);
        return frameDelta / length;
    }

    /// <summary>
    /// セグメント両端を Δt だけ動かす。内側セグメントは幅を保った平行移動。
    /// 端点（t=0/1）は固定し、端セグメントは内側の点だけ時間移動する。
    /// </summary>
    private (double T0, double T1) ClampSegmentTranslate(int segmentIndex, double t0, double t1, double deltaT)
    {
        if (_shiftStartCurve is null || segmentIndex < 0 || segmentIndex >= _shiftStartCurve.Points.Count - 1)
        {
            return (t0, t1);
        }

        var points = _shiftStartCurve.Points;
        const double eps = 1e-9;
        var leftLocked = segmentIndex == 0;
        var rightLocked = segmentIndex + 1 == points.Count - 1;
        if (leftLocked && rightLocked)
        {
            return (t0, t1);
        }

        if (!leftLocked && !rightLocked)
        {
            var width = t1 - t0;
            if (width <= 1e-12)
            {
                return (t0, t1);
            }

            var minT = points[segmentIndex - 1].T + eps;
            var maxT = points[segmentIndex + 2].T - eps;
            if (maxT - minT <= width)
            {
                var mid = (minT + maxT) * 0.5;
                return (mid - width * 0.5, mid + width * 0.5);
            }

            var nextT0 = Math.Clamp(t0 + deltaT, minT, maxT - width);
            return (nextT0, nextT0 + width);
        }

        if (leftLocked)
        {
            var maxT = points[segmentIndex + 2].T - eps;
            return (0, Math.Clamp(t1 + deltaT, eps, maxT));
        }

        var minTRight = points[segmentIndex - 1].T + eps;
        return (Math.Clamp(t0 + deltaT, minTRight, 1d - eps), 1);
    }

    private void UpdateHoverCursor(Point pos)
    {
        if (_tool == EditTool.SeekBar)
        {
            Cursor = Cursors.IBeam;
            return;
        }

        if (_tool == EditTool.Brush || pos.X < ContentX || _range.IsEmpty)
        {
            Cursor = _tool == EditTool.Brush ? Cursors.Pen : Cursors.Cross;
            return;
        }

        MapPoint(pos, out var t, out var semitones);
        if (_curve.HitTestPoint(t, semitones, PointHitTRadius(), PointHitSemitoneRadius()) >= 0)
        {
            Cursor = PointDragCursor(Keyboard.Modifiers);
            return;
        }

        var segRadius = SegmentHitSemitoneRadius();
        if (_curve.HitTestSegment(t, semitones, segRadius) >= 0)
        {
            Cursor = SegmentDragCursor(Keyboard.Modifiers);
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            Cursor = Cursors.SizeNS;
            return;
        }

        Cursor = Cursors.Cross;
    }

    private void UpsertDrawn(double t, float semitones)
    {
        var merge = BrushMergeEpsilonT();
        // 直前サンプルとのあいだに残る旧点を消す（書き直し時のとげとげ防止）。
        if (_brushLastT is double lastT)
        {
            var lo = Math.Min(lastT, t);
            var hi = Math.Max(lastT, t);
            if (hi - lo > 1e-12)
            {
                _curve.RemovePointsBetween(lo, hi);
            }
        }
        else
        {
            // 一筆目: ニブ幅ぶんの近傍旧点も掃く。
            _curve.RemovePointsBetween(t - merge, t + merge);
        }

        if (_snapToScale)
        {
            if (_curve.DefaultShape != FadeShape.Constant)
            {
                _curve.DefaultShape = FadeShape.Constant;
            }

            _curve.Upsert(t, semitones, mergeEpsilon: merge);
        }
        else
        {
            _curve.Upsert(t, semitones, mergeEpsilon: merge, segmentShape: FadeShape.Linear);
            EnsureBrushLinearSegments();
        }

        _brushLastT = t;
    }

    private void EnsureBrushLinearSegments()
    {
        for (var i = 0; i < _curve.SegmentShapes.Count; i++)
        {
            if (_curve.SegmentShapes[i] != FadeShape.Linear)
            {
                _curve.SetAllSegmentShapes(FadeShape.Linear);
                return;
            }
        }

        if (_curve.DefaultShape != FadeShape.Linear)
        {
            _curve.DefaultShape = FadeShape.Linear;
        }
    }

    private static Pen CreateCurvePen()
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("VolumeCurveLineBrush")), 1.8)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            // Round だと折れ点付近が弧に見え、リニアでも「ぐにゃっ」と誤認される。
            LineJoin = PenLineJoin.Miter,
            MiterLimit = 2.5,
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CreatePitchGridPen()
    {
        // 音量カーブの dB グリッドより一段暗く（半音線）。
        var brush = new SolidColorBrush(Color.FromArgb(0x40, 0x90, 0x90, 0x90));
        brush.Freeze();
        var pen = new Pen(brush, 1.0);
        pen.Freeze();
        return pen;
    }

    private static Pen CreatePitchOctaveGridPen()
    {
        // オクターブ線も同様に抑えめ（ゼロ線含む）。
        var brush = new SolidColorBrush(Color.FromArgb(0x58, 0xA8, 0xA8, 0xA8));
        brush.Freeze();
        var pen = new Pen(brush, 1.4);
        pen.Freeze();
        return pen;
    }

    private static Pen CreateSelectedPointPen()
    {
        var pen = new Pen(
            WpfControlHelpers.FrozenBrush(Theme.Get("VolumeCurveSelectedPointStrokeBrush")),
            2.4)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        return pen;
    }

    private static Pen CreateTimeGuidePen()
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("VolumeCurveTimeGuideBrush")), 1.0)
        {
            StartLineCap = PenLineCap.Flat,
            EndLineCap = PenLineCap.Flat,
        };
        pen.Freeze();
        return pen;
    }

    private static Brush CreatePointFill() =>
        WpfControlHelpers.FrozenBrush(Theme.Get("VolumeCurvePointFillBrush"));

    private static Brush CreatePointLabelBrush() =>
        WpfControlHelpers.FrozenBrush(Theme.Get("VolumeCurvePointLabelBrush"));
}
