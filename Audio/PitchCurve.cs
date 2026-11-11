using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.Audio;

/// <summary>正規化時間 t∈[0,1] 上の制御点。</summary>
internal readonly record struct PitchCurvePoint(double T, float Semitones);

/// <summary>
/// ピッチカーブ。制御点を FadeShape で補間し、フレーム単位の半音テーブルを作る。
/// </summary>
internal sealed class PitchCurve
{
    public const float MinSemitones = PitchShift.MinSemitones;
    public const float MaxSemitones = PitchShift.MaxSemitones;

    private readonly List<PitchCurvePoint> _points = [];
    private readonly List<FadeShape> _shapes = [];

    public PitchCurve() => SetConstant(0);

    public PitchCurve(IEnumerable<PitchCurvePoint> points, IEnumerable<FadeShape>? shapes = null, FadeShape defaultShape = FadeShape.Linear)
    {
        // Clone 経路では Constant（音階スナップ）もそのまま残す。勝手に連続形状へ戻さない。
        DefaultShape = defaultShape;
        ReplacePoints(points, shapes);
    }

    /// <summary>ユーザーが形状を選ぶまではリニア。フェード既定の SCurve は使わない。</summary>
    public FadeShape DefaultShape { get; set; } = FadeShape.Linear;

    public IReadOnlyList<PitchCurvePoint> Points => _points;

    public IReadOnlyList<FadeShape> SegmentShapes => _shapes;

    public static PitchCurve Flat(float semitones)
    {
        var curve = new PitchCurve();
        curve.SetConstant(semitones);
        return curve;
    }

    /// <summary>無段階。ブラシ用。範囲だけ制限する。</summary>
    public static float Snap(float semitones) =>
        Math.Clamp(semitones, MinSemitones, MaxSemitones);

    /// <summary>半音整数。数値入力用。</summary>
    public static float SnapSemitone(float semitones) =>
        Math.Clamp(MathF.Round(semitones), MinSemitones, MaxSemitones);

    /// <summary>ゼロ付近へ吸着。</summary>
    public static float SoftSnapZero(float semitones, float radius = 0.35f) =>
        Math.Abs(semitones) <= radius ? 0f : Snap(semitones);

    /// <summary>音階スナップ用。半音整数へ丸め、ゼロ付近は 0。</summary>
    public static float SnapToScale(float semitones) =>
        SoftSnapZero(SnapSemitone(semitones), radius: 0.01f);

    public void SetConstant(float semitones)
    {
        semitones = Snap(semitones);
        _points.Clear();
        _shapes.Clear();
        _points.Add(new PitchCurvePoint(0, semitones));
        _points.Add(new PitchCurvePoint(1, semitones));
        _shapes.Add(DefaultShape);
    }

    /// <summary>全制御点を半音整数化し、区間を階段（Constant）にする。</summary>
    public void QuantizeToScaleSteps()
    {
        for (var i = 0; i < _points.Count; i++)
        {
            _points[i] = new PitchCurvePoint(_points[i].T, SnapToScale(_points[i].Semitones));
        }

        DefaultShape = FadeShape.Constant;
        SetAllSegmentShapes(FadeShape.Constant);
    }

    public bool IsNoOp()
    {
        for (var i = 0; i < _points.Count; i++)
        {
            if (Math.Abs(_points[i].Semitones) > 1e-4f)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsConstant(out float semitones)
    {
        if (_points.Count == 0)
        {
            semitones = 0;
            return true;
        }

        semitones = _points[0].Semitones;
        for (var i = 1; i < _points.Count; i++)
        {
            if (Math.Abs(_points[i].Semitones - semitones) > 1e-3f)
            {
                return false;
            }
        }

        return true;
    }

    public void ReplacePoints(IEnumerable<PitchCurvePoint> points, IEnumerable<FadeShape>? shapes = null)
    {
        _points.Clear();
        _shapes.Clear();
        foreach (var point in points)
        {
            _points.Add(new PitchCurvePoint(Math.Clamp(point.T, 0d, 1d), Snap(point.Semitones)));
        }

        _points.Sort(static (a, b) => a.T.CompareTo(b.T));
        DeduplicateByT();
        if (_points.Count == 0)
        {
            SetConstant(0);
            return;
        }

        if (_points[0].T > 0)
        {
            _points.Insert(0, new PitchCurvePoint(0, _points[0].Semitones));
        }

        if (_points[^1].T < 1)
        {
            _points.Add(new PitchCurvePoint(1, _points[^1].Semitones));
        }

        var shapeList = shapes?.ToList() ?? [];
        EnsureShapeCount(shapeList);
    }

    public void SetAllSegmentShapes(FadeShape shape)
    {
        // Constant は音階スナップの階段用。通常 UI では使わないが保持する。
        DefaultShape = shape;
        for (var i = 0; i < _shapes.Count; i++)
        {
            _shapes[i] = shape;
        }
    }

    public void SetSegmentShape(int segmentIndex, FadeShape shape)
    {
        if (segmentIndex < 0 || segmentIndex >= _shapes.Count)
        {
            return;
        }

        _shapes[segmentIndex] = shape;
    }

    /// <summary>
    /// ポイントから次点へのセグメント形状。最終点は直前セグメント。
    /// </summary>
    public void SetPointOutgoingShape(int pointIndex, FadeShape shape)
    {
        if (_shapes.Count == 0 || pointIndex < 0 || pointIndex >= _points.Count)
        {
            DefaultShape = shape;
            return;
        }

        var segment = pointIndex >= _shapes.Count ? _shapes.Count - 1 : pointIndex;
        SetSegmentShape(segment, shape);
    }

    public FadeShape OutgoingShapeOfPoint(int pointIndex)
    {
        if (_shapes.Count == 0)
        {
            return DefaultShape;
        }

        if (pointIndex < 0)
        {
            return DefaultShape;
        }

        var segment = pointIndex >= _shapes.Count ? _shapes.Count - 1 : pointIndex;
        return _shapes[segment];
    }

    /// <summary>ブラシ／ポイント編集。近い t があれば更新、なければ挿入。対象インデックスを返す。</summary>
    /// <param name="segmentShape">新規区間に使う形状。省略時は DefaultShape。</param>
    public int Upsert(double t, float semitones, double mergeEpsilon = 0.004, FadeShape? segmentShape = null)
    {
        t = Math.Clamp(t, 0d, 1d);
        semitones = Snap(semitones);
        var shape = segmentShape ?? DefaultShape;
        for (var i = 0; i < _points.Count; i++)
        {
            if (Math.Abs(_points[i].T - t) <= mergeEpsilon)
            {
                _points[i] = new PitchCurvePoint(_points[i].T, semitones);
                return i;
            }
        }

        var insertAt = _points.Count;
        for (var i = 0; i < _points.Count; i++)
        {
            if (t < _points[i].T)
            {
                insertAt = i;
                break;
            }
        }

        _points.Insert(insertAt, new PitchCurvePoint(t, semitones));
        if (_shapes.Count == 0)
        {
            _shapes.Add(shape);
        }
        else if (insertAt == 0)
        {
            _shapes.Insert(0, shape);
        }
        else if (insertAt >= _points.Count - 1)
        {
            _shapes.Add(shape);
        }
        else
        {
            // 分割した左区間も指定形状に（ブラシのリニア接続用）。
            if (segmentShape.HasValue)
            {
                _shapes[insertAt - 1] = shape;
            }

            _shapes.Insert(insertAt, shape);
        }

        EnsureShapeCount();
        return insertAt;
    }

    public void MovePoint(int index, double t, float semitones)
    {
        if (index < 0 || index >= _points.Count)
        {
            return;
        }

        t = Math.Clamp(t, 0d, 1d);
        // 端点は時間固定。
        if (index == 0)
        {
            t = 0;
        }
        else if (index == _points.Count - 1)
        {
            t = 1;
        }
        else
        {
            var minT = _points[index - 1].T + 1e-4;
            var maxT = _points[index + 1].T - 1e-4;
            if (maxT <= minT)
            {
                t = (_points[index - 1].T + _points[index + 1].T) * 0.5;
            }
            else
            {
                t = Math.Clamp(t, minT, maxT);
            }
        }

        _points[index] = new PitchCurvePoint(t, Snap(semitones));
    }

    public bool RemovePoint(int index)
    {
        if (_points.Count <= 2 || index <= 0 || index >= _points.Count - 1)
        {
            return false;
        }

        _points.RemoveAt(index);
        if (index - 1 < _shapes.Count)
        {
            _shapes.RemoveAt(Math.Min(index - 1, _shapes.Count - 1));
        }

        EnsureShapeCount();
        return true;
    }

    public int HitTestPoint(double t, float semitones, double tRadius, float semiRadius)
    {
        var best = -1;
        var bestScore = double.MaxValue;
        for (var i = 0; i < _points.Count; i++)
        {
            var dt = Math.Abs(_points[i].T - t) / Math.Max(1e-6, tRadius);
            var ds = Math.Abs(_points[i].Semitones - semitones) / Math.Max(1e-6, semiRadius);
            var score = dt * dt + ds * ds;
            if (score < bestScore && dt <= 1 && ds <= 1)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>
    /// 点と点のあいだ（曲線近傍）を掴む。t が区間内で、サンプル値との半音差が半径以内ならセグメント index。
    /// </summary>
    public int HitTestSegment(double t, float semitones, float semiRadius)
    {
        t = Math.Clamp(t, 0d, 1d);
        if (_points.Count < 2 || semiRadius <= 0)
        {
            return -1;
        }

        var best = -1;
        var bestDev = float.MaxValue;
        for (var i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i];
            var b = _points[i + 1];
            if (t < a.T - 1e-9 || t > b.T + 1e-9)
            {
                continue;
            }

            var expected = SampleAt(t);
            var dev = Math.Abs(expected - semitones);
            if (dev <= semiRadius && dev < bestDev)
            {
                bestDev = dev;
                best = i;
            }
        }

        return best;
    }

    /// <summary>セグメント両端の半音を時間固定のまま書き換える（平行リフト用）。</summary>
    public bool SetSegmentSemitones(int segmentIndex, float semi0, float semi1)
    {
        if (segmentIndex < 0 || segmentIndex >= _points.Count - 1)
        {
            return false;
        }

        var a = _points[segmentIndex];
        var b = _points[segmentIndex + 1];
        _points[segmentIndex] = new PitchCurvePoint(a.T, Snap(semi0));
        _points[segmentIndex + 1] = new PitchCurvePoint(b.T, Snap(semi1));
        return true;
    }

    /// <summary>セグメント両端の時間と半音を書き換える（Alt 付き平行移動用）。端点は t=0/1 固定。</summary>
    public bool SetSegmentEnds(int segmentIndex, double t0, float semi0, double t1, float semi1)
    {
        if (segmentIndex < 0 || segmentIndex >= _points.Count - 1)
        {
            return false;
        }

        const double eps = 1e-9;
        if (segmentIndex == 0)
        {
            t0 = 0;
        }
        else
        {
            var minT = _points[segmentIndex - 1].T + eps;
            var maxT = Math.Max(minT, t1 - eps);
            t0 = Math.Clamp(t0, minT, maxT);
        }

        if (segmentIndex + 1 == _points.Count - 1)
        {
            t1 = 1;
        }
        else
        {
            var maxT = _points[segmentIndex + 2].T - eps;
            var minT = Math.Min(maxT, t0 + eps);
            t1 = Math.Clamp(t1, minT, maxT);
        }

        if (t1 <= t0)
        {
            t1 = Math.Min(1d, t0 + eps);
        }

        _points[segmentIndex] = new PitchCurvePoint(t0, Snap(semi0));
        _points[segmentIndex + 1] = new PitchCurvePoint(t1, Snap(semi1));
        return true;
    }

    /// <summary>全制御点の半音に同じ量を加算（全体シフト）。</summary>
    public void ShiftAll(float deltaSemitones)
    {
        if (Math.Abs(deltaSemitones) < 1e-6f)
        {
            return;
        }

        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            _points[i] = new PitchCurvePoint(point.T, Snap(point.Semitones + deltaSemitones));
        }
    }

    /// <summary>開始スナップショットからの差分で全体を置き換える（ドラッグ中の丸め累積を避ける）。</summary>
    public void RestoreShiftedFrom(PitchCurve start, float deltaSemitones)
    {
        ArgumentNullException.ThrowIfNull(start);
        DefaultShape = start.DefaultShape;
        var points = new List<PitchCurvePoint>(start.Points.Count);
        foreach (var point in start.Points)
        {
            points.Add(new PitchCurvePoint(point.T, Snap(point.Semitones + deltaSemitones)));
        }

        ReplacePoints(points, start.SegmentShapes);
    }

    /// <summary>
    /// 最終端を内側へ切り離す。端点は t=1 に残し、移動点の新しいインデックスを返す。
    /// </summary>
    public int DetachLastEndpoint(double t, float semitones)
    {
        if (_points.Count < 2)
        {
            return -1;
        }

        var last = _points.Count - 1;
        var edgeSemitones = _points[last].Semitones;
        var prevT = _points[last - 1].T;
        const double eps = 1e-9;
        if (prevT >= 1d - eps * 2)
        {
            return last;
        }

        t = Math.Clamp(t, prevT + eps, 1d - eps);
        var shape = last - 1 < _shapes.Count ? _shapes[last - 1] : DefaultShape;
        _points[last] = new PitchCurvePoint(t, Snap(semitones));
        _points.Add(new PitchCurvePoint(1, edgeSemitones));
        _shapes.Add(shape == FadeShape.Constant ? FadeShape.Linear : shape);
        EnsureShapeCount();
        return last;
    }

    /// <summary>
    /// 始端を内側へ切り離す。端点は t=0 に残し、移動点の新しいインデックスを返す。
    /// </summary>
    public int DetachFirstEndpoint(double t, float semitones)
    {
        if (_points.Count < 2)
        {
            return -1;
        }

        var edgeSemitones = _points[0].Semitones;
        var nextT = _points[1].T;
        const double eps = 1e-9;
        if (nextT <= eps * 2)
        {
            return 0;
        }

        t = Math.Clamp(t, eps, nextT - eps);
        var shape = _shapes.Count > 0 ? _shapes[0] : DefaultShape;
        _points[0] = new PitchCurvePoint(0, edgeSemitones);
        _points.Insert(1, new PitchCurvePoint(t, Snap(semitones)));
        _shapes.Insert(0, shape == FadeShape.Constant ? FadeShape.Linear : shape);
        EnsureShapeCount();
        return 1;
    }

    /// <summary>
    /// 開区間 (t0, t1) 内の内側点を削除する。ブラシ書き直しで旧点を残さないため。
    /// 端点（t=0 / t=1）は消さない。
    /// </summary>
    public int RemovePointsBetween(double t0, double t1)
    {
        if (t1 < t0)
        {
            (t0, t1) = (t1, t0);
        }

        if (t1 - t0 <= 1e-12 || _points.Count <= 2)
        {
            return 0;
        }

        var removed = 0;
        for (var i = _points.Count - 2; i >= 1; i--)
        {
            var t = _points[i].T;
            if (t > t0 && t < t1 && RemovePoint(i))
            {
                removed++;
            }
        }

        return removed;
    }

    public float SampleAt(double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        if (_points.Count == 0)
        {
            return 0;
        }

        if (_points.Count == 1 || t <= _points[0].T)
        {
            return _points[0].Semitones;
        }

        if (t >= _points[^1].T)
        {
            return _points[^1].Semitones;
        }

        for (var i = 0; i < _points.Count - 1; i++)
        {
            var a = _points[i];
            var b = _points[i + 1];
            if (t > b.T)
            {
                continue;
            }

            var span = b.T - a.T;
            if (span <= 1e-12)
            {
                return b.Semitones;
            }

            var local = (t - a.T) / span;
            var shape = i < _shapes.Count ? _shapes[i] : DefaultShape;
            var rising = FadeCurves.Apply01(shape, local);
            return a.Semitones + rising * (b.Semitones - a.Semitones);
        }

        return _points[^1].Semitones;
    }

    public float[] BuildSemitoneTable(int frameCount)
    {
        frameCount = Math.Max(0, frameCount);
        var table = new float[frameCount];
        if (frameCount == 0)
        {
            return table;
        }

        if (frameCount == 1)
        {
            table[0] = SampleAt(0);
            return table;
        }

        for (var i = 0; i < frameCount; i++)
        {
            table[i] = SampleAt(i / (double)(frameCount - 1));
        }

        return table;
    }

    /// <summary>ブラシ後にほぼ共線の中間点を間引く。</summary>
    /// <param name="segmentShape">残した点の区間形状。省略時は DefaultShape。</param>
    public void Simplify(float semiTolerance = 0.15f, FadeShape? segmentShape = null)
    {
        if (_points.Count <= 2)
        {
            return;
        }

        var keep = new bool[_points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(0, _points.Count - 1, semiTolerance, keep);
        var nextPoints = new List<PitchCurvePoint>();
        for (var i = 0; i < _points.Count; i++)
        {
            if (keep[i])
            {
                nextPoints.Add(_points[i]);
            }
        }

        var shape = segmentShape ?? DefaultShape;
        ReplacePoints(nextPoints, Enumerable.Repeat(shape, Math.Max(0, nextPoints.Count - 1)));
    }

    public PitchCurve Clone() => new(_points, _shapes, DefaultShape);

    /// <summary>別カーブの点・形状をこのインスタンスへコピー（undo/redo 復元用）。</summary>
    public void CopyFrom(PitchCurve other)
    {
        ArgumentNullException.ThrowIfNull(other);
        DefaultShape = other.DefaultShape;
        ReplacePoints(other.Points, other.SegmentShapes);
    }

    /// <summary>履歴用: [t0,s0,t1,s1,...]</summary>
    public float[] ToPackedSamples()
    {
        var packed = new float[_points.Count * 2];
        for (var i = 0; i < _points.Count; i++)
        {
            packed[i * 2] = (float)_points[i].T;
            packed[i * 2 + 1] = _points[i].Semitones;
        }

        return packed;
    }

    public static PitchCurve FromPackedSamples(float[]? packed, IReadOnlyList<long>? shapeIds, FadeShape defaultShape = FadeShape.Linear)
    {
        var curve = new PitchCurve();
        curve.DefaultShape = defaultShape == FadeShape.Constant ? FadeShape.Linear : defaultShape;
        if (packed is null || packed.Length < 2)
        {
            curve.SetConstant(0);
            return curve;
        }

        var points = new List<PitchCurvePoint>(packed.Length / 2);
        for (var i = 0; i + 1 < packed.Length; i += 2)
        {
            points.Add(new PitchCurvePoint(packed[i], packed[i + 1]));
        }

        var shapes = new List<FadeShape>();
        if (shapeIds is not null)
        {
            foreach (var id in shapeIds)
            {
                var shape = (FadeShape)(int)id;
                if (!Enum.IsDefined(shape))
                {
                    shape = curve.DefaultShape;
                }

                shapes.Add(shape);
            }
        }

        curve.ReplacePoints(points, shapes);
        return curve;
    }

    public long[] ToShapeIds()
    {
        var ids = new long[_shapes.Count];
        for (var i = 0; i < _shapes.Count; i++)
        {
            ids[i] = (long)_shapes[i];
        }

        return ids;
    }

    private void SimplifyRange(int first, int last, float tolerance, bool[] keep)
    {
        if (last <= first + 1)
        {
            return;
        }

        var a = _points[first];
        var b = _points[last];
        var span = b.T - a.T;
        var maxDev = 0f;
        var maxIndex = -1;
        for (var i = first + 1; i < last; i++)
        {
            var p = _points[i];
            var expected = span <= 1e-12
                ? a.Semitones
                : a.Semitones + (float)((p.T - a.T) / span) * (b.Semitones - a.Semitones);
            var dev = Math.Abs(p.Semitones - expected);
            if (dev > maxDev)
            {
                maxDev = dev;
                maxIndex = i;
            }
        }

        if (maxIndex < 0 || maxDev < tolerance)
        {
            return;
        }

        keep[maxIndex] = true;
        SimplifyRange(first, maxIndex, tolerance, keep);
        SimplifyRange(maxIndex, last, tolerance, keep);
    }

    private void DeduplicateByT()
    {
        for (var i = _points.Count - 1; i > 0; i--)
        {
            if (Math.Abs(_points[i].T - _points[i - 1].T) < 1e-9)
            {
                _points.RemoveAt(i - 1);
            }
        }
    }

    private void EnsureShapeCount(List<FadeShape>? preferred = null)
    {
        var need = Math.Max(0, _points.Count - 1);
        while (_shapes.Count > need)
        {
            _shapes.RemoveAt(_shapes.Count - 1);
        }

        while (_shapes.Count < need)
        {
            var shape = preferred is { Count: > 0 } && _shapes.Count < preferred.Count
                ? preferred[_shapes.Count]
                : DefaultShape;
            _shapes.Add(shape);
        }

        if (preferred is null)
        {
            return;
        }

        for (var i = 0; i < _shapes.Count && i < preferred.Count; i++)
        {
            _shapes[i] = preferred[i];
        }
    }
}
