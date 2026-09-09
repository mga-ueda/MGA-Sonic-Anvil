namespace MgaSonicAnvil.Audio;

/// <summary>表示用 STFT。フロアは -60 dB。周波数は対数。</summary>
internal static class SpectrogramEngine
{
    public const int FftSize = 1024;
    public const int Hop = FftSize / 2;
    public const int BinCount = FftSize / 2 + 1;
    public const float FloorDb = -60f;
    public const float CeilingDb = 0f;
    public const double MinHertz = 20d;
    public const double MaxHertz = 20000d;
    public const byte OverlayAlpha = 255;
    private const int ColorLutSize = 256;
    private static readonly int[] ColorLut = CreateColorLut();

    public static readonly double[] FrequencyMarks =
    [
        20, 30, 50, 70, 100, 200, 300, 500, 700,
        1000, 2000, 3000, 5000, 7000, 10000, 20000,
    ];

    public static string FormatHertz(double hertz)
    {
        if (hertz >= 950)
        {
            var kilo = hertz / 1000d;
            var rounded = Math.Round(kilo, 1);
            return Math.Abs(rounded - Math.Round(rounded)) < 1e-6
                ? $"{(int)Math.Round(rounded)}k"
                : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{rounded:0.#}k");
        }

        return ((int)Math.Round(hertz)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public static double HertzToUnit(double hertz, double minHertz, double maxHertz)
    {
        var lo = Math.Log(Math.Max(1e-6, minHertz));
        var hi = Math.Log(Math.Max(minHertz * 1.0001, maxHertz));
        return Math.Clamp((Math.Log(Math.Clamp(hertz, minHertz, maxHertz)) - lo) / (hi - lo), 0, 1);
    }

    public static double UnitToHertz(double unit, double minHertz, double maxHertz) =>
        minHertz * Math.Pow(maxHertz / minHertz, Math.Clamp(unit, 0, 1));

    /// <summary>表示スケールは常に 20 kHz。実データのナイキストはこれと別。</summary>
    public static double DisplayMaxHertz => MaxHertz;

    public static double ContentNyquist(int sampleRate) =>
        Math.Max(MinHertz, sampleRate * 0.5 * 0.995);

    public static int ColumnCount(long frameCount) =>
        Math.Max(1, (int)((Math.Max(0, frameCount) + Hop - 1) / Hop));

    public static int ColumnIndex(long frame, int columnCount) =>
        (int)Math.Clamp(frame / Hop, 0, Math.Max(0, columnCount - 1));

    public static byte DbToLutByte(float db)
    {
        var t = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
        return (byte)Math.Round(t * (ColorLutSize - 1));
    }

    public static int ColorFromLutByte(byte value) => ColorLut[value];

    public static int ColorBgra(float db) => ColorFromLutByte(DbToLutByte(db));

    public static void WriteColumnLut(ReadOnlySpan<double> magnitude, Span<byte> dest)
    {
        var bins = Math.Min(dest.Length, magnitude.Length);
        for (var i = 0; i < bins; i++)
        {
            var mag = magnitude[i];
            dest[i] = mag <= 1e-12
                ? (byte)0
                : DbToLutByte((float)(20d * Math.Log10(mag)));
        }
    }

    public static void FillMonoMix(
        float[] interleaved,
        int channels,
        long origin,
        long frames,
        float[] dest)
    {
        if (channels <= 1)
        {
            for (var i = 0; i < dest.Length; i++)
            {
                var frame = origin + i;
                dest[i] = frame >= 0 && frame < frames ? interleaved[frame] : 0;
            }

            return;
        }

        for (var i = 0; i < dest.Length; i++)
        {
            MixFrame(interleaved, channels, origin + i, frames, out dest[i]);
        }
    }

    private static int[] CreateColorLut()
    {
        var lut = new int[ColorLutSize];
        for (var i = 0; i < ColorLutSize; i++)
        {
            lut[i] = SampleGradient(i / (float)(ColorLutSize - 1));
        }

        return lut;
    }

    private static int SampleGradient(float t)
    {
        t = MathF.Pow(Math.Clamp(t, 0f, 1f), 0.78f);
        ReadOnlySpan<(float P, byte R, byte G, byte B)> stops =
        [
            (0.00f, 4, 0, 20),
            (0.10f, 55, 12, 95),
            (0.22f, 140, 18, 115),
            (0.36f, 215, 28, 55),
            (0.50f, 250, 85, 22),
            (0.62f, 255, 175, 18),
            (0.74f, 255, 235, 45),
            (0.86f, 255, 252, 175),
            (1.00f, 255, 255, 255),
        ];
        var i = 0;
        for (; i < stops.Length - 2; i++)
        {
            if (t <= stops[i + 1].P)
            {
                break;
            }
        }

        var a = stops[i];
        var b = stops[i + 1];
        var span = b.P - a.P;
        var w = span < 1e-6f ? 1f : (t - a.P) / span;
        var r = (byte)Math.Round(a.R + (b.R - a.R) * w);
        var g = (byte)Math.Round(a.G + (b.G - a.G) * w);
        var bl = (byte)Math.Round(a.B + (b.B - a.B) * w);
        (r, g, bl) = BoostSaturation(r, g, bl, 1.18f);
        return PackBgra(bl, g, r, OverlayAlpha);
    }

    public static void FillHann(Span<float> window, out float sum)
    {
        sum = 0;
        var n = window.Length;
        for (var i = 0; i < n; i++)
        {
            window[i] = 0.5f - 0.5f * (float)Math.Cos(2d * Math.PI * i / (n - 1));
            sum += window[i];
        }
    }

    public static void AnalyzeWindow(
        ReadOnlySpan<float> samples,
        ReadOnlySpan<float> window,
        float windowSum,
        Span<double> re,
        Span<double> im)
    {
        var n = samples.Length;
        for (var i = 0; i < n; i++)
        {
            re[i] = samples[i] * window[i];
            im[i] = 0;
        }

        Fft(re, im);
        var norm = windowSum > 1e-8f ? windowSum : 1f;
        var bins = (n / 2) + 1;
        for (var i = 0; i < bins; i++)
        {
            re[i] = 2d * Math.Sqrt(re[i] * re[i] + im[i] * im[i]) / norm;
        }
    }

    public static float BinDb(ReadOnlySpan<double> magnitude, double bin)
    {
        var last = magnitude.Length - 1;
        if (last <= 0)
        {
            return FloorDb;
        }

        var i = Math.Clamp(bin, 0, last);
        var lo = (int)Math.Floor(i);
        var hi = Math.Min(last, lo + 1);
        var t = i - lo;
        var mag = magnitude[lo] * (1 - t) + magnitude[hi] * t;
        return mag <= 1e-12 ? FloorDb : (float)(20d * Math.Log10(mag));
    }

    public static void MixFrame(
        float[] interleaved,
        int channels,
        long frame,
        long frameCount,
        out float sample)
    {
        if (!ChannelMix.TryFrameOffset(interleaved, channels, frame, frameCount, out var offset))
        {
            sample = 0;
            return;
        }

        ChannelMix.Downmix(interleaved, offset, channels, out var left, out var right);
        sample = 0.5f * (left + right);
    }

    private static (byte R, byte G, byte B) BoostSaturation(byte r, byte g, byte b, float amount)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        if (max == 0 || max == min)
        {
            return (r, g, b);
        }

        var gray = (r + g + b) / 3f;
        byte Push(byte channel) =>
            (byte)Math.Clamp(Math.Round(gray + (channel - gray) * amount), 0, 255);
        return (Push(r), Push(g), Push(b));
    }

    private static int PackBgra(byte b, byte g, byte r, byte a) =>
        b | (g << 8) | (r << 16) | (a << 24);

    private static void Fft(Span<double> re, Span<double> im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }

            j |= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var start = 0; start < n; start += length)
            {
                var curRe = 1d;
                var curIm = 0d;
                for (var k = 0; k < length / 2; k++)
                {
                    var evenIndex = start + k;
                    var oddIndex = start + k + length / 2;
                    var oddRe = re[oddIndex] * curRe - im[oddIndex] * curIm;
                    var oddIm = re[oddIndex] * curIm + im[oddIndex] * curRe;
                    re[oddIndex] = re[evenIndex] - oddRe;
                    im[oddIndex] = im[evenIndex] - oddIm;
                    re[evenIndex] += oddRe;
                    im[evenIndex] += oddIm;
                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
