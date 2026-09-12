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
}
