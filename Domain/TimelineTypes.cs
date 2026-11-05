namespace MgaSonicAnvil.Domain;

internal readonly record struct WaveMarker(int Id, long Frame, string Comment)
{
    public WaveMarker(int id, long frame)
        : this(id, frame, string.Empty)
    {
    }
}

internal readonly record struct MarkerSnapshot(long Frame, string Comment);

internal readonly record struct WaveRegion(long StartFrame, long EndFrame, string Name)
{
    public WaveRegion(WaveSelection range, string? name = null)
        : this(range.StartFrame, range.EndFrame, name ?? string.Empty)
    {
    }

    public static WaveRegion Empty { get; } = new(0, 0, string.Empty);

    public WaveSelection Range => new(StartFrame, EndFrame);

    public bool IsEmpty => EndFrame <= StartFrame;

    public WaveRegion Clamp(long frameCount)
    {
        var range = Range.Clamp(frameCount);
        return range.IsEmpty ? Empty : new WaveRegion(range, Name);
    }

    public WaveRegion WithRange(WaveSelection range) =>
        range.IsEmpty ? Empty : new WaveRegion(range, Name);
}

internal readonly record struct WaveSelection(long StartFrame, long EndFrame)
{
    public static WaveSelection Empty { get; } = new(0, 0);

    public bool IsEmpty => EndFrame <= StartFrame;

    public bool ContainsFrame(long frame) => !IsEmpty && frame >= StartFrame && frame < EndFrame;

    public long Length => Math.Max(0, EndFrame - StartFrame);

    /// <summary>
    /// 描画用ヘッドを半開区間の終端未満に収める。外挿や折り返し直前の
    /// 生カーソルが End 以上になっても、聞こえる最終フレーム（End-1）へ止める。
    /// 開始より前はそのまま（Single Loop のイントロ許容）。空なら負値だけ 0 へ。
    /// </summary>
    public long ClampVisualPlayhead(long frame)
    {
        if (frame < 0)
        {
            frame = 0;
        }

        if (IsEmpty)
        {
            return frame;
        }

        if (frame >= EndFrame)
        {
            return Math.Max(StartFrame, EndFrame - 1);
        }

        return frame;
    }

    /// <summary>再生窓の終端だけ分かるときの描画用クランプ（開始は 0 扱い）。</summary>
    public static long ClampVisualPlayhead(long frame, long exclusiveEndFrame)
    {
        if (frame < 0)
        {
            return 0;
        }

        if (exclusiveEndFrame > 0 && frame >= exclusiveEndFrame)
        {
            return exclusiveEndFrame - 1;
        }

        return frame;
    }

    public WaveSelection Clamp(long frameCount)
    {
        if (frameCount <= 0 || IsEmpty)
        {
            return Empty;
        }

        var start = Math.Clamp(StartFrame, 0, frameCount);
        var end = Math.Clamp(EndFrame, 0, frameCount);
        return end <= start ? Empty : new WaveSelection(start, end);
    }

    public static WaveSelection FromPoints(long a, long b)
    {
        return a <= b ? new WaveSelection(a, b) : new WaveSelection(b, a);
    }

    public WaveSelection Union(WaveSelection other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new WaveSelection(
            Math.Min(StartFrame, other.StartFrame),
            Math.Max(EndFrame, other.EndFrame));
    }

    /// <summary>進行方向に、まだ通過していない選択端がある。</summary>
    public bool HasBoundInDirection(long from, int direction)
    {
        if (IsEmpty || direction == 0)
        {
            return false;
        }

        return direction > 0
            ? StartFrame > from || EndFrame > from
            : StartFrame < from || EndFrame < from;
    }

    /// <summary>
    /// Ctrl+左右の着地。選択の開始・終了が進行方向にあり、
    /// <paramref name="cueFrame"/> より手前ならその端を返す。
    /// キューが進行方向に無いときは選択端を優先し、端も無ければキューを返す。
    /// </summary>
    public long JumpFrame(long from, long cueFrame, int direction)
    {
        if (IsEmpty || direction == 0)
        {
            return cueFrame;
        }

        long? bound = null;
        Consider(StartFrame);
        Consider(EndFrame);
        if (bound is long edge
            && (direction > 0
                ? cueFrame <= from || edge < cueFrame
                : cueFrame >= from || edge > cueFrame))
        {
            return edge;
        }

        return cueFrame;

        void Consider(long frame)
        {
            if (direction > 0)
            {
                if (frame > from && (bound is null || frame < bound))
                {
                    bound = frame;
                }
            }
            else if (frame < from && (bound is null || frame > bound))
            {
                bound = frame;
            }
        }
    }
}
