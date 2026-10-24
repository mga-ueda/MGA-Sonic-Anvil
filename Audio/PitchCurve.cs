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

    public PitchCurve(IEnumerable<PitchCurvePoint> points, IEnumerable<FadeShape>? shapes = null, FadeShape defaultShape = FadeCurves.Default)
    {
        // Clone 経路では Constant（音階スナップ）もそのまま残す。勝手に連続形状へ戻さない。
        DefaultShape = defaultShape;
        ReplacePoints(points, shapes);
    }

    public FadeShape DefaultShape { get; set; } = FadeCurves.Default;

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
    public int Upsert(double t, float semitones, double mergeEpsilon = 0.004)
    {
        t = Math.Clamp(t, 0d, 1d);
        semitones = Snap(semitones);
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
            _shapes.Add(DefaultShape);
        }
        else if (insertAt == 0)
        {
            _shapes.Insert(0, DefaultShape);
        }
        else if (insertAt >= _points.Count - 1)
        {
            _shapes.Add(DefaultShape);
        }
        else
        {
            _shapes.Insert(insertAt, DefaultShape);
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
    public void Simplify(float semiTolerance = 0.15f)
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

        ReplacePoints(nextPoints, Enumerable.Repeat(DefaultShape, Math.Max(0, nextPoints.Count - 1)));
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

    public static PitchCurve FromPackedSamples(float[]? packed, IReadOnlyList<long>? shapeIds, FadeShape defaultShape = FadeCurves.Default)
    {
        var curve = new PitchCurve();
        curve.DefaultShape = defaultShape == FadeShape.Constant ? FadeCurves.Default : defaultShape;
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
