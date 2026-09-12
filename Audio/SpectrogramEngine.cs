namespace MgaSonicAnvil.Audio;

/// <summary>表示用 STFT。フロアは -60 dB。周波数は対数＋低域圧縮。暗部は持ち上げて見せる。</summary>
internal static class SpectrogramEngine
{
    public const int FftSize = 1024;
    public const int Hop = FftSize / 2;
    public const int BinCount = FftSize / 2 + 1;
    public const float FloorDb = -60f;
    public const float CeilingDb = 0f;
    /// <summary>左の縦バー最上段。小さい成分を天井近くまで持ち上げる。</summary>
    public const float DisplayBoostMaxDb = 32f * 2f / 3f;
    private static readonly float[] LinearFromLut = CreateLinearFromLut();
    public const double MinHertz = 80d;
    public const double MaxHertz = 24000d;
    /// <summary>1 より大きいと低域の縦幅を圧縮する。対数軸の上にかける。</summary>
    public const double FrequencyWarp = 1.35;
    public const byte OverlayAlpha = 255;
    private const int ColorLutSize = 256;
    private static readonly int[] ColorLut = CreateColorLut();

    public static readonly double[] FrequencyMarks =
    [
        100, 200, 500, 1000, 2000, 5000, 10000, 20000,
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
        var logUnit = Math.Clamp((Math.Log(Math.Clamp(hertz, minHertz, maxHertz)) - lo) / (hi - lo), 0, 1);
        return Math.Pow(logUnit, FrequencyWarp);
    }

    public static double UnitToHertz(double unit, double minHertz, double maxHertz)
    {
        var logUnit = Math.Pow(Math.Clamp(unit, 0, 1), 1d / FrequencyWarp);
        return minHertz * Math.Pow(maxHertz / minHertz, logUnit);
    }

    /// <summary>表示スケールは常に 24 kHz。実データのナイキストはこれと別。</summary>
    public static double DisplayMaxHertz => MaxHertz;

    public static double ContentNyquist(int sampleRate) =>
        Math.Max(MinHertz, sampleRate * 0.5 * 0.995);

    public static int ColumnCount(long frameCount) =>
        Math.Max(1, (int)((Math.Max(0, frameCount) + Hop - 1) / Hop));

    /// <summary>
    /// 画面列ごとにライブ FFT するか。
    /// ファイル全体が画面に収まっていて（＝スクロールが起きない）、かつ
    /// キャッシュ列（Hop 間隔）が画面より粗いときだけライブにする（短い SE のフィット表示）。
    /// 少しでもズームしていれば従来どおりキャッシュから読み、拡大時の追従スクロールを軽く保つ。
    /// </summary>
    public static bool PreferLiveFft(double framesPerPixel, double viewSpanFrames, long frameCount) =>
        framesPerPixel < Hop && viewSpanFrames >= frameCount;

    public static int ColumnIndex(long frame, int columnCount) =>
        (int)Math.Clamp(frame / Hop, 0, Math.Max(0, columnCount - 1));

    /// <summary>
    /// 表示用ノーマライズのゲイン（dB）。ファイル内のピークが 0 dBFS になる持ち上げ量。
    /// 読み込み・編集の解析タイミングで一度だけ計算し、描画ごとには追従しない。
    /// </summary>
    public static float NormalizeGainDb(float[] samples)
    {
        var peak = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            var abs = Math.Abs(samples[i]);
            if (abs > peak)
            {
                peak = abs;
            }
        }

        if (peak <= 0f)
        {
            return 0f;
        }

        return Math.Clamp((float)(-20d * Math.Log10(peak)), -24f, 96f);
    }

    /// <summary>
    /// 正規化レベル（0=フロア、1=天井）の暗部を持ち上げる。
    /// ハイライトはほぼ線形のまま残し、ピークの色は潰さない。
    /// </summary>
    public static float LiftDisplayUnit(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var root = MathF.Sqrt(t);
        return root + (t - root) * t;
    }

    public static byte DbToLutByte(float db)
    {
        var t = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
        return (byte)Math.Round(LiftDisplayUnit(t) * (ColorLutSize - 1));
    }

    public static float InvertLiftDisplayUnit(float lifted)
    {
        lifted = Math.Clamp(lifted, 0f, 1f);
        if (lifted <= 0f)
        {
            return 0f;
        }

        if (lifted >= 1f)
        {
            return 1f;
        }

        var lo = 0f;
        var hi = 1f;
        for (var i = 0; i < 24; i++)
        {
            var mid = (lo + hi) * 0.5f;
            if (LiftDisplayUnit(mid) < lifted)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        return (lo + hi) * 0.5f;
    }

    public static float ClampDisplayBoostDb(float boostDb) =>
        Math.Clamp(boostDb, 0f, DisplayBoostMaxDb);

    public static float DisplayBoostDbFromUnit(double unit) =>
        ClampDisplayBoostDb((float)(Math.Clamp(unit, 0d, 1d) * DisplayBoostMaxDb));

    public static double DisplayBoostUnitFromDb(float boostDb) =>
        Math.Clamp(ClampDisplayBoostDb(boostDb) / DisplayBoostMaxDb, 0d, 1d);

    /// <summary>
    /// キャッシュ済み LUT に表示ブーストを足す。0 は無音のまま（フロアを黄にしない）。
    /// </summary>
    public static byte ApplyDisplayBoostToLut(byte lut, float boostDb)
    {
        if (lut == 0 || boostDb <= 0.0001f)
        {
            return lut;
        }

        var db = FloorDb + LinearFromLut[lut] * (CeilingDb - FloorDb);
        return DbToLutByte(db + boostDb);
    }

    public static int ColorFromLutByte(byte value) => ColorLut[value];

    /// <summary>表示ブースト用の 256 色パレット。ビットマップの LUT 番号だけ差し替える。</summary>
    public static void FillBoostColorMap(Span<int> map, float boostDb)
    {
        var n = Math.Min(map.Length, ColorLutSize);
        for (var i = 0; i < n; i++)
        {
            map[i] = ColorFromLutByte(ApplyDisplayBoostToLut((byte)i, boostDb));
        }
    }

    public const int LinearUnitScale = 65535;

    public static float LinearUnitFromDb(float db) =>
        Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);

    public static ushort PackLinearUnit(float t) =>
        (ushort)Math.Round(Math.Clamp(t, 0f, 1f) * LinearUnitScale);

    public static float UnpackLinearUnit(ushort packed) =>
        packed / (float)LinearUnitScale;

    /// <summary>持ち上げ済み LUT（0–255、小数のまま）を線形ユニットへ。0 は無音。</summary>
    public static ushort LinearUnitFromLifted(float liftedByte)
    {
        if (liftedByte <= 0.5f)
        {
            return 0;
        }

        return PackLinearUnit(InvertLiftDisplayUnit(liftedByte / (ColorLutSize - 1)));
    }

    public static ushort LinearUnitFromMagnitude(double mag, float gainDb = 0f)
    {
        if (mag <= 1e-12)
        {
            return 0;
        }

        return PackLinearUnit(LinearUnitFromDb((float)(20d * Math.Log10(mag)) + gainDb));
    }

    public static int ColorFromLinearUnit(ushort packed, float boostDb = 0f)
    {
        if (packed == 0)
        {
            return ColorFromLutByte(0);
        }

        var db = FloorDb + UnpackLinearUnit(packed) * (CeilingDb - FloorDb);
        return ColorBgra(db + boostDb);
    }

    /// <summary>16 bit 線形ユニット用パレット。暗部ブーストでも段が目立たない。</summary>
    public static void FillBoostLinearMap(Span<int> map, float boostDb)
    {
        var n = Math.Min(map.Length, LinearUnitScale + 1);
        if (n <= 0)
        {
            return;
        }

        map[0] = ColorFromLutByte(0);
        for (var i = 1; i < n; i++)
        {
            map[i] = ColorFromLinearUnit((ushort)i, boostDb);
        }
    }

    public static int ColorBgra(float db) => ColorFromLutByte(DbToLutByte(db));

    /// <summary>
    /// 振幅を色にする。無音は表示ゲインを足さない（フロアのまま）。
    /// フィットのライブ FFT とキャッシュ列で同じ式を使う。
    /// </summary>
    public static byte LutByteFromMagnitude(double mag, float gainDb = 0f) =>
        mag <= 1e-12
            ? (byte)0
            : DbToLutByte((float)(20d * Math.Log10(mag)) + gainDb);

    public static int ColorBgraFromMagnitude(double mag, float gainDb = 0f) =>
        ColorFromLutByte(LutByteFromMagnitude(mag, gainDb));

    public static void WriteColumnLut(ReadOnlySpan<double> magnitude, Span<byte> dest, float gainDb = 0f)
    {
        var bins = Math.Min(dest.Length, magnitude.Length);
        for (var i = 0; i < bins; i++)
        {
            dest[i] = LutByteFromMagnitude(magnitude[i], gainDb);
        }
    }

    public static void FillMonoMix(
        float[] interleaved,
        int channels,
        long origin,
        long frames,
        float[] dest)
    {
        if (frames <= 0)
        {
            Array.Clear(dest);
            return;
        }

        if (channels <= 1)
        {
            for (var i = 0; i < dest.Length; i++)
            {
                dest[i] = interleaved[ReflectFrame(origin + i, frames)];
            }

            return;
        }

        for (var i = 0; i < dest.Length; i++)
        {
            MixFrame(interleaved, channels, ReflectFrame(origin + i, frames), frames, out dest[i]);
        }
    }

    /// <summary>ファイル端の外側を鏡で折り返す。ゼロ埋めの段差で全帯域に漏れないようにする。</summary>
    public static long ReflectFrame(long frame, long frames)
    {
        if (frames <= 1)
        {
            return 0;
        }

        var last = frames - 1;
        var period = last * 2;
        var x = frame % period;
        if (x < 0)
        {
            x += period;
        }

        return x <= last ? x : period - x;
    }

    private static float[] CreateLinearFromLut()
    {
        var table = new float[ColorLutSize];
        table[0] = 0f;
        table[ColorLutSize - 1] = 1f;
        for (var i = 1; i < ColorLutSize - 1; i++)
        {
            table[i] = InvertLiftDisplayUnit(i / (float)(ColorLutSize - 1));
        }

        return table;
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
        t = Math.Clamp(t, 0f, 1f);
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

    public static double BinMagnitude(ReadOnlySpan<double> magnitude, double bin)
    {
        var last = magnitude.Length - 1;
        if (last <= 0)
        {
            return 0;
        }

        var i = Math.Clamp(bin, 0, last);
        var lo = (int)Math.Floor(i);
        var hi = Math.Min(last, lo + 1);
        var t = i - lo;
        return magnitude[lo] * (1 - t) + magnitude[hi] * t;
    }

    public static float BinDb(ReadOnlySpan<double> magnitude, double bin)
    {
        var mag = BinMagnitude(magnitude, bin);
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
