namespace MgaSonicAnvil.Audio;

/// <summary>ファイル全体の Short Term LKFS。0.1 秒 hop。</summary>
internal sealed class LoudnessProfile
{
    public LoudnessProfile(int sampleRate, int hopFrames, float[] values)
    {
        SampleRate = sampleRate;
        HopFrames = Math.Max(1, hopFrames);
        Values = values;
    }

    public int SampleRate { get; }

    public int HopFrames { get; }

    public float[] Values { get; }

    public float AtFrame(double frame)
    {
        var values = Values;
        if (values.Length == 0)
        {
            return float.NegativeInfinity;
        }

        var x = frame / HopFrames;
        if (x <= 0)
        {
            return values[0];
        }

        var last = values.Length - 1;
        if (x >= last)
        {
            return values[last];
        }

        var i = (int)Math.Floor(x);
        var a = values[i];
        var b = values[i + 1];
        if (float.IsInfinity(a) || float.IsNaN(a))
        {
            return b;
        }

        if (float.IsInfinity(b) || float.IsNaN(b))
        {
            return a;
        }

        return a + (b - a) * (float)(x - i);
    }
}
