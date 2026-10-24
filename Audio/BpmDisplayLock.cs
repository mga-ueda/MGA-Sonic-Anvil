namespace MgaSonicAnvil.Audio;

/// <summary>
/// 直近の BPM 推定が狭い範囲に落ち着いたらロックし、
/// 倍／半や外れ値を表示に混ぜない。連続して外れたら解除する。
/// </summary>
internal sealed class BpmDisplayLock
{
    /// <summary>この個数が揃い、かつ狭い範囲ならロック。</summary>
    public const int StableCount = 6;

    /// <summary>ロック判定・維持の相対幅（±）。</summary>
    public const double LockRatio = 0.08;

    /// <summary>この回数連続で範囲外ならロック解除。</summary>
    public const int UnlockStreak = 4;

    private readonly float[] _recent = new float[StableCount];
    private int _recentCount;
    private int _recentWrite;
    private float? _lockBpm;
    private int _outStreak;

    /// <summary>表示用。ロック中はその値。未ロックなら直近の中央値（あれば）。</summary>
    public float? DisplayBpm
    {
        get
        {
            if (_lockBpm is { } locked)
            {
                return locked;
            }

            return _recentCount > 0 ? MedianRecent() : null;
        }
    }

    public bool IsLocked => _lockBpm is not null;

    public void Reset()
    {
        _recentCount = 0;
        _recentWrite = 0;
        _lockBpm = null;
        _outStreak = 0;
        Array.Clear(_recent);
    }

    public void Push(float bpm)
    {
        if (!float.IsFinite(bpm) || bpm < EffeTuneBpmEstimator.MinBpm || bpm > EffeTuneBpmEstimator.MaxBpm)
        {
            return;
        }

        if (_lockBpm is { } locked)
        {
            var folded = (float)FoldOctave(bpm, locked);
            if (WithinRatio(folded, locked, LockRatio))
            {
                _outStreak = 0;
                // ロック値はゆっくり追従（誤って半分へ引っ張られない程度）。
                _lockBpm = locked * 0.92f + folded * 0.08f;
                Remember(folded);
                return;
            }

            _outStreak++;
            if (_outStreak < UnlockStreak)
            {
                // 外れ値は無視。表示はロックのまま。
                return;
            }

            // 連続外れ → 解除して新しい候補から集め直す。
            _lockBpm = null;
            _outStreak = 0;
            _recentCount = 0;
            _recentWrite = 0;
            Remember(bpm);
            return;
        }

        Remember(bpm);
        if (_recentCount < StableCount)
        {
            return;
        }

        var median = MedianRecent();
        if (!AllWithinRatio(median, LockRatio))
        {
            return;
        }

        _lockBpm = median;
        _outStreak = 0;
    }

    private void Remember(float bpm)
    {
        _recent[_recentWrite] = bpm;
        _recentWrite = (_recentWrite + 1) % _recent.Length;
        if (_recentCount < _recent.Length)
        {
            _recentCount++;
        }
    }

    private bool AllWithinRatio(float center, double ratio)
    {
        for (var i = 0; i < _recentCount; i++)
        {
            var v = (float)FoldOctave(_recent[i], center);
            if (!WithinRatio(v, center, ratio))
            {
                return false;
            }
        }

        return true;
    }

    private float MedianRecent()
    {
        Span<float> tmp = stackalloc float[_recentCount];
        for (var i = 0; i < _recentCount; i++)
        {
            tmp[i] = _recent[i];
        }

        tmp.Sort();
        var mid = _recentCount / 2;
        return _recentCount % 2 == 0
            ? 0.5f * (tmp[mid - 1] + tmp[mid])
            : tmp[mid];
    }

    private static bool WithinRatio(float value, float center, double ratio)
    {
        if (center <= 0)
        {
            return false;
        }

        var lo = center * (1.0 - ratio);
        var hi = center * (1.0 + ratio);
        return value >= lo && value <= hi;
    }

    private static double FoldOctave(double bpm, double anchor)
    {
        if (anchor <= 0 || !double.IsFinite(bpm) || !double.IsFinite(anchor))
        {
            return bpm;
        }

        while (bpm > anchor * Math.Sqrt(2) && bpm * 0.5 >= EffeTuneBpmEstimator.MinBpm)
        {
            bpm *= 0.5;
        }

        while (bpm < anchor / Math.Sqrt(2) && bpm * 2 <= EffeTuneBpmEstimator.MaxBpm)
        {
            bpm *= 2;
        }

        return bpm;
    }
}
