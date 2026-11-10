using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.Audio;

/// <summary>正規化時間 t∈[0,1] 上の制御点。</summary>
internal readonly record struct VolumeCurvePoint(double T, float GainDb);

/// <summary>
/// 音量カーブ。制御点を FadeShape で補間し、フレーム単位の dB テーブルを作る。
/// </summary>
internal sealed class VolumeCurve
{
    /// <summary>無音（線形ゲイン 0）。縦軸の下端。</summary>
    public const float MinGainDb = float.NegativeInfinity;

    public const float MaxGainDb = (float)WaveformGainAnalyzer.MaxGainDb;

    /// <summary>これ以下の有限値は <see cref="MinGainDb"/>（-∞）へ丸める。</summary>
    public const float FiniteSilenceDb = -200f;

    private readonly List<VolumeCurvePoint> _points = [];
    private readonly List<FadeShape> _shapes = [];

    public VolumeCurve() => SetConstant(0);

    public VolumeCurve(IEnumerable<VolumeCurvePoint> points, IEnumerable<FadeShape>? shapes = null, FadeShape defaultShape = FadeShape.Linear)
    {
        // Clone 経路では Constant（階段）もそのまま残す。勝手に連続形状へ戻さない。
        DefaultShape = defaultShape;
        ReplacePoints(points, shapes);
    }

    /// <summary>ユーザーが形状を選ぶまではリニア。フェード既定の SCurve は使わない。</summary>
    public FadeShape DefaultShape { get; set; } = FadeShape.Linear;

    public IReadOnlyList<VolumeCurvePoint> Points => _points;

    public IReadOnlyList<FadeShape> SegmentShapes => _shapes;

    public static VolumeCurve Flat(float gainDb)
    {
        var curve = new VolumeCurve();
        curve.SetConstant(gainDb);
        return curve;
    }

    /// <summary>無段階。ブラシ用。範囲だけ制限する（下は -∞ まで）。</summary>
    public static float Snap(float gainDb)
    {
        if (float.IsNaN(gainDb))
        {
            return 0f;
        }

        if (float.IsNegativeInfinity(gainDb) || gainDb <= FiniteSilenceDb)
        {
            return MinGainDb;
        }

        if (float.IsPositiveInfinity(gainDb) || gainDb > MaxGainDb)
        {
            return MaxGainDb;
        }

        return gainDb;
    }

    /// <summary>0.1 dB 単位。数値入力用。-∞ も許可。</summary>
    public static float SnapGainStep(float gainDb)
    {
        if (float.IsNegativeInfinity(gainDb) || gainDb <= FiniteSilenceDb)
        {
            return MinGainDb;
        }

        if (!float.IsFinite(gainDb))
        {
            return 0f;
        }

        return Snap(MathF.Round(gainDb, 1, MidpointRounding.AwayFromZero));
    }

    /// <summary>ゼロ付近へ吸着。</summary>
    public static float SoftSnapZero(float gainDb, float radius = 0.35f)
    {
        if (float.IsNegativeInfinity(gainDb))
        {
            return MinGainDb;
        }

        return Math.Abs(gainDb) <= radius ? 0f : Snap(gainDb);
    }

    /// <summary>-∞ を補間用の有限フロアへ。</summary>
    public static float ToFiniteGainDb(float gainDb) =>
        float.IsNegativeInfinity(gainDb) ? FiniteSilenceDb : gainDb;

    /// <summary>有限フロア以下は -∞ に戻す。</summary>
    public static float FromFiniteGainDb(float gainDb) =>
        gainDb <= FiniteSilenceDb ? MinGainDb : gainDb;

    /// <summary>-∞ 同士の差は 0。片方が -∞ なら有限フロア基準。</summary>
    public static float AbsGainDeltaDb(float a, float b)
    {
        if (float.IsNegativeInfinity(a) && float.IsNegativeInfinity(b))
        {
            return 0f;
        }

        return Math.Abs(ToFiniteGainDb(a) - ToFiniteGainDb(b));
    }

    /// <summary>dB 加算。-∞ からの引き上げは有限フロア起点。</summary>
    public static float AddGainDb(float gainDb, float deltaDb)
    {
        if (Math.Abs(deltaDb) < 1e-8f)
        {
            return Snap(gainDb);
        }

        return Snap(ToFiniteGainDb(gainDb) + deltaDb);
    }

    public void SetConstant(float gainDb)
    {
        gainDb = Snap(gainDb);
        _points.Clear();
        _shapes.Clear();
        _points.Add(new VolumeCurvePoint(0, gainDb));
        _points.Add(new VolumeCurvePoint(1, gainDb));
        _shapes.Add(DefaultShape);
    }

    public bool IsNoOp()
    {
        for (var i = 0; i < _points.Count; i++)
        {
            if (AbsGainDeltaDb(_points[i].GainDb, 0f) > 1e-4f)
            {
                return false;
            }
        }

        return true;
    }

    public bool IsConstant(out float gainDb)
    {
        if (_points.Count == 0)
        {
            gainDb = 0;
            return true;
        }

        gainDb = _points[0].GainDb;
        for (var i = 1; i < _points.Count; i++)
        {
            if (AbsGainDeltaDb(_points[i].GainDb, gainDb) > 1e-3f)
            {
                return false;
            }
        }

        return true;
    }

    public void ReplacePoints(IEnumerable<VolumeCurvePoint> points, IEnumerable<FadeShape>? shapes = null)
    {
        _points.Clear();
        _shapes.Clear();
        foreach (var point in points)
        {
            _points.Add(new VolumeCurvePoint(Math.Clamp(point.T, 0d, 1d), Snap(point.GainDb)));
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
            _points.Insert(0, new VolumeCurvePoint(0, _points[0].GainDb));
        }

        if (_points[^1].T < 1)
        {
            _points.Add(new VolumeCurvePoint(1, _points[^1].GainDb));
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
    public int Upsert(double t, float gainDb, double mergeEpsilon = 0.004, FadeShape? segmentShape = null)
    {
        t = Math.Clamp(t, 0d, 1d);
        gainDb = Snap(gainDb);
        var shape = segmentShape ?? DefaultShape;
        for (var i = 0; i < _points.Count; i++)
        {
            if (Math.Abs(_points[i].T - t) <= mergeEpsilon)
            {
                // ブラシは既存 t を維持。意図配置（segmentShape 指定）は目標 t へ寄せる。
                var mergedT = _points[i].T;
                if (segmentShape.HasValue)
                {
                    mergedT = t;
                    if (i > 0)
                    {
                        mergedT = Math.Max(mergedT, _points[i - 1].T + 1e-9);
                    }

                    if (i < _points.Count - 1)
                    {
                        mergedT = Math.Min(mergedT, _points[i + 1].T - 1e-9);
                    }

                    if (i < _shapes.Count)
                    {
                        _shapes[i] = shape;
                    }

                    if (i > 0 && i - 1 < _shapes.Count)
                    {
                        _shapes[i - 1] = shape;
                    }
                }

                _points[i] = new VolumeCurvePoint(mergedT, gainDb);
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

        _points.Insert(insertAt, new VolumeCurvePoint(t, gainDb));
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

    public void MovePoint(int index, double t, float gainDb)
    {
        if (index < 0 || index >= _points.Count)
        {
            return;
        }

        t = Math.Clamp(t, 0d, 1d);
        // 端点は時間固定（内側へ動かしたい場合は Detach*Endpoint を使う）。
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
            // 順序だけ守る。以前の 1e-4 パディングは短い区間で可動域が潰れて動けなくなっていた。
            var prevT = _points[index - 1].T;
            var nextT = _points[index + 1].T;
            if (nextT <= prevT)
            {
                t = (prevT + nextT) * 0.5;
            }
            else
            {
                const double eps = 1e-9;
                var minT = prevT + eps;
                var maxT = nextT - eps;
                t = maxT <= minT ? (prevT + nextT) * 0.5 : Math.Clamp(t, minT, maxT);
            }
        }

        _points[index] = new VolumeCurvePoint(t, Snap(gainDb));
    }

    /// <summary>
    /// 最終端を内側へ切り離す。端点は t=1 に残し、移動点の新しいインデックスを返す。
    /// </summary>
    public int DetachLastEndpoint(double t, float gainDb)
    {
        if (_points.Count < 2)
        {
            return -1;
        }

        var last = _points.Count - 1;
        var edgeGain = _points[last].GainDb;
        var prevT = _points[last - 1].T;
        const double eps = 1e-9;
        if (prevT >= 1d - eps * 2)
        {
            return last;
        }

        t = Math.Clamp(t, prevT + eps, 1d - eps);
        var shape = last - 1 < _shapes.Count ? _shapes[last - 1] : DefaultShape;
        _points[last] = new VolumeCurvePoint(t, Snap(gainDb));
        _points.Add(new VolumeCurvePoint(1, edgeGain));
        _shapes.Add(shape == FadeShape.Constant ? FadeShape.Linear : shape);
        EnsureShapeCount();
        return last;
    }

    /// <summary>
    /// 始端を内側へ切り離す。端点は t=0 に残し、移動点の新しいインデックスを返す。
    /// </summary>
    public int DetachFirstEndpoint(double t, float gainDb)
    {
        if (_points.Count < 2)
        {
            return -1;
        }

        var edgeGain = _points[0].GainDb;
        var nextT = _points[1].T;
        const double eps = 1e-9;
        if (nextT <= eps * 2)
        {
            return 0;
        }

        t = Math.Clamp(t, eps, nextT - eps);
        var shape = _shapes.Count > 0 ? _shapes[0] : DefaultShape;
        _points[0] = new VolumeCurvePoint(0, edgeGain);
        _points.Insert(1, new VolumeCurvePoint(t, Snap(gainDb)));
        _shapes.Insert(0, shape == FadeShape.Constant ? FadeShape.Linear : shape);
        EnsureShapeCount();
        return 1;
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

    public int HitTestPoint(double t, float gainDb, double tRadius, float dbRadius)
    {
        var best = -1;
        var bestScore = double.MaxValue;
        for (var i = 0; i < _points.Count; i++)
        {
            var dt = Math.Abs(_points[i].T - t) / Math.Max(1e-6, tRadius);
            var ds = AbsGainDeltaDb(_points[i].GainDb, gainDb) / Math.Max(1e-6f, dbRadius);
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
    /// 点と点のあいだ（曲線近傍）を掴む。t が区間内で、サンプル値との dB 差が半径以内ならセグメント index。
    /// </summary>
    public int HitTestSegment(double t, float gainDb, float dbRadius)
    {
        t = Math.Clamp(t, 0d, 1d);
        if (_points.Count < 2 || dbRadius <= 0)
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
            var dev = AbsGainDeltaDb(expected, gainDb);
            if (dev <= dbRadius && dev < bestDev)
            {
                bestDev = dev;
                best = i;
            }
        }

        return best;
    }

    /// <summary>セグメント両端の dB を時間固定のまま書き換える（平行リフト用）。</summary>
    public bool SetSegmentGainDb(int segmentIndex, float gainDb0, float gainDb1)
    {
        if (segmentIndex < 0 || segmentIndex >= _points.Count - 1)
        {
            return false;
        }

        var a = _points[segmentIndex];
        var b = _points[segmentIndex + 1];
        _points[segmentIndex] = new VolumeCurvePoint(a.T, Snap(gainDb0));
        _points[segmentIndex + 1] = new VolumeCurvePoint(b.T, Snap(gainDb1));
        return true;
    }

    /// <summary>セグメント両端の時間と dB を書き換える（Alt 付き平行移動用）。端点は t=0/1 固定。</summary>
    public bool SetSegmentEnds(int segmentIndex, double t0, float gainDb0, double t1, float gainDb1)
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

        _points[segmentIndex] = new VolumeCurvePoint(t0, Snap(gainDb0));
        _points[segmentIndex + 1] = new VolumeCurvePoint(t1, Snap(gainDb1));
        return true;
    }

    /// <summary>全制御点の dB に同じ量を加算（全体シフト）。</summary>
    public void ShiftAll(float deltaDb)
    {
        if (Math.Abs(deltaDb) < 1e-6f)
        {
            return;
        }

        for (var i = 0; i < _points.Count; i++)
        {
            var point = _points[i];
            _points[i] = new VolumeCurvePoint(point.T, AddGainDb(point.GainDb, deltaDb));
        }
    }

    /// <summary>開始スナップショットからの差分で全体を置き換える（ドラッグ中の丸め累積を避ける）。</summary>
    public void RestoreShiftedFrom(VolumeCurve start, float deltaDb)
    {
        ArgumentNullException.ThrowIfNull(start);
        DefaultShape = start.DefaultShape;
        var points = new List<VolumeCurvePoint>(start.Points.Count);
        foreach (var point in start.Points)
        {
            points.Add(new VolumeCurvePoint(point.T, AddGainDb(point.GainDb, deltaDb)));
        }

        ReplacePoints(points, start.SegmentShapes);
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
            return _points[0].GainDb;
        }

        if (t >= _points[^1].T)
        {
            return _points[^1].GainDb;
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
                return b.GainDb;
            }

            var local = (t - a.T) / span;
            var shape = i < _shapes.Count ? _shapes[i] : DefaultShape;
            var rising = FadeCurves.Apply01(shape, local);
            // -∞ を含む区間は有限フロアで補間し、フロア以下なら -∞ に戻す。
            var fa = ToFiniteGainDb(a.GainDb);
            var fb = ToFiniteGainDb(b.GainDb);
            return FromFiniteGainDb(fa + rising * (fb - fa));
        }

        return _points[^1].GainDb;
    }

    public float[] BuildGainDbTable(int frameCount)
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
    public void Simplify(float dbTolerance = 0.15f, FadeShape? segmentShape = null)
    {
        if (_points.Count <= 2)
        {
            return;
        }

        var keep = new bool[_points.Count];
        keep[0] = true;
        keep[^1] = true;
        SimplifyRange(0, _points.Count - 1, dbTolerance, keep);
        var nextPoints = new List<VolumeCurvePoint>();
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

    public VolumeCurve Clone() => new(_points, _shapes, DefaultShape);

    /// <summary>別カーブの点・形状をこのインスタンスへコピー（undo/redo 復元用）。</summary>
    public void CopyFrom(VolumeCurve other)
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
            packed[i * 2 + 1] = _points[i].GainDb;
        }

        return packed;
    }

    public static VolumeCurve FromPackedSamples(float[]? packed, IReadOnlyList<long>? shapeIds, FadeShape defaultShape = FadeShape.Linear)
    {
        var curve = new VolumeCurve();
        curve.DefaultShape = defaultShape == FadeShape.Constant ? FadeShape.Linear : defaultShape;
        if (packed is null || packed.Length < 2)
        {
            curve.SetConstant(0);
            return curve;
        }

        var points = new List<VolumeCurvePoint>(packed.Length / 2);
        for (var i = 0; i + 1 < packed.Length; i += 2)
        {
            points.Add(new VolumeCurvePoint(packed[i], packed[i + 1]));
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
                ? a.GainDb
                : a.GainDb + (float)((p.T - a.T) / span) * (b.GainDb - a.GainDb);
            var dev = Math.Abs(p.GainDb - expected);
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
