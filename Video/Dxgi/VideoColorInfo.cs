namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>MFVideoPrimaries と同じ値。</summary>
internal enum VideoColorPrimaries
{
    Unknown = 0,
    Bt709 = 2,
    Bt470SysM = 3,
    Bt470SysBG = 4,
    Smpte170M = 5,
    Smpte240M = 6,
    Ebu3213 = 7,
    SmpteC = 8,
    Bt2020 = 9,
    Xyz = 10,
    DciP3 = 11,
    Aces = 12,
}

/// <summary>MFVideoTransferFunction と同じ値。</summary>
internal enum VideoTransferFunction
{
    Unknown = 0,
    Linear = 1,
    Gamma18 = 2,
    Gamma20 = 3,
    Gamma22 = 4,
    Bt709 = 5,
    Smpte240M = 6,
    Srgb = 7,
    Gamma28 = 8,
    Log100 = 9,
    Log316 = 10,
    Bt709Sym = 11,
    Bt2020Const = 12,
    Bt2020 = 13,
    Gamma26 = 14,
    Pq = 15,
    Hlg = 16,
    LinearRel = 17,
}

/// <summary>MFVideoTransferMatrix と同じ値。</summary>
internal enum VideoYuvMatrix
{
    Unknown = 0,
    Bt709 = 1,
    Bt601 = 2,
    Smpte240M = 3,
    Bt2020_10 = 4,
    Bt2020_12 = 5,
}

/// <summary>MFNominalRange と同じ値。</summary>
internal enum VideoNominalRange
{
    Unknown = 0,
    Full = 1,
    Limited = 2,
    Range48_208 = 3,
    Range64_127 = 4,
}

/// <summary>
/// ソース映像の色情報（MF のメディアタイプから読む）。不明なら BT.709／リミテッドレンジとみなす。
/// </summary>
internal readonly record struct VideoColorInfo(
    VideoColorPrimaries Primaries,
    VideoTransferFunction Transfer,
    VideoYuvMatrix Matrix,
    VideoNominalRange Range)
{
    public static readonly VideoColorInfo Default = new(
        VideoColorPrimaries.Bt709, VideoTransferFunction.Bt709, VideoYuvMatrix.Bt709, VideoNominalRange.Limited);

    public bool IsFullRange => Range == VideoNominalRange.Full;

    public bool IsHdr => Transfer is VideoTransferFunction.Pq or VideoTransferFunction.Hlg;

    public bool IsWideGamut => Primaries is VideoColorPrimaries.Bt2020 or VideoColorPrimaries.DciP3;

    /// <summary>Unknown を既定値に置き換える。SD（高さ 576 以下）で行列不明なら BT.601 とみなす。</summary>
    public VideoColorInfo Normalized(int height = 0)
    {
        var matrix = Matrix;
        if (matrix == VideoYuvMatrix.Unknown)
        {
            matrix = Primaries is VideoColorPrimaries.Bt2020 ? VideoYuvMatrix.Bt2020_10
                : height is > 0 and <= 576 ? VideoYuvMatrix.Bt601
                : VideoYuvMatrix.Bt709;
        }

        return new VideoColorInfo(
            Primaries == VideoColorPrimaries.Unknown ? VideoColorPrimaries.Bt709 : Primaries,
            Transfer == VideoTransferFunction.Unknown ? VideoTransferFunction.Bt709 : Transfer,
            matrix,
            Range == VideoNominalRange.Unknown ? VideoNominalRange.Limited : Range);
    }

    /// <summary>MF_MT_VIDEO_PRIMARIES／MF_MT_TRANSFER_FUNCTION／MF_MT_YUV_MATRIX／MF_MT_VIDEO_NOMINAL_RANGE の生値から。</summary>
    public static VideoColorInfo FromMediaFoundation(uint primaries, uint transfer, uint matrix, uint range) => new(
        Enum.IsDefined(typeof(VideoColorPrimaries), (int)primaries) ? (VideoColorPrimaries)primaries : VideoColorPrimaries.Unknown,
        Enum.IsDefined(typeof(VideoTransferFunction), (int)transfer) ? (VideoTransferFunction)transfer : VideoTransferFunction.Unknown,
        Enum.IsDefined(typeof(VideoYuvMatrix), (int)matrix) ? (VideoYuvMatrix)matrix : VideoYuvMatrix.Unknown,
        Enum.IsDefined(typeof(VideoNominalRange), (int)range) ? (VideoNominalRange)range : VideoNominalRange.Unknown);
}

/// <summary>シェーダ定数にする色変換行列。3x3 は行優先 9 要素。</summary>
internal static class VideoColorMath
{
    public static readonly float[] Identity = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    /// <summary>sRGB（BT.709）線形 → XYZ（D50、Bradford 順応済み）。ICC の colorant と同じ白色点で比べるため。</summary>
    public static readonly double[] SrgbToXyzD50 =
    [
        0.4360747, 0.3850649, 0.1430804,
        0.2225045, 0.7168786, 0.0606169,
        0.0139322, 0.0971045, 0.7141733,
    ];

    /// <summary>Y'CbCr（Y 0..1、Cb/Cr -0.5..0.5 に正規化済み）→ R'G'B'。</summary>
    public static float[] YuvToRgb(VideoYuvMatrix matrix) => matrix switch
    {
        VideoYuvMatrix.Bt601 => [1, 0, 1.402f, 1, -0.3441f, -0.7141f, 1, 1.772f, 0],
        VideoYuvMatrix.Smpte240M => [1, 0, 1.5756f, 1, -0.2266f, -0.4766f, 1, 1.8260f, 0],
        VideoYuvMatrix.Bt2020_10 or VideoYuvMatrix.Bt2020_12 => [1, 0, 1.4746f, 1, -0.1646f, -0.5714f, 1, 1.8814f, 0],
        _ => [1, 0, 1.5748f, 1, -0.1873f, -0.4681f, 1, 1.8556f, 0],
    };

    /// <summary>
    /// UNORM で読んだ Y／C を正規化する係数。y' = (y - YOffset) * YScale、c' = (c - COffset) * CScale。
    /// 10bit（P010）は上位詰めなので 8bit と同じ比率で良い。
    /// </summary>
    public static (float YOffset, float YScale, float COffset, float CScale) RangeParams(VideoNominalRange range) => range switch
    {
        VideoNominalRange.Full => (0f, 1f, 128f / 255f, 1f),
        VideoNominalRange.Range48_208 => (48f / 255f, 255f / 160f, 128f / 255f, 255f / 160f),
        _ => (16f / 255f, 255f / 219f, 128f / 255f, 255f / 224f),
    };

    /// <summary>ソース原色（線形光）→ BT.709 線形。似た原色は恒等にしておく。</summary>
    public static float[] PrimariesToBt709(VideoColorPrimaries primaries) => primaries switch
    {
        VideoColorPrimaries.Bt2020 =>
        [
            1.6605f, -0.5876f, -0.0728f,
            -0.1246f, 1.1329f, -0.0083f,
            -0.0182f, -0.1006f, 1.1187f,
        ],
        VideoColorPrimaries.DciP3 =>
        [
            1.2249f, -0.2247f, 0f,
            -0.0420f, 1.0419f, 0f,
            -0.0197f, -0.0786f, 1.0979f,
        ],
        VideoColorPrimaries.Smpte170M or VideoColorPrimaries.SmpteC =>
        [
            0.9395f, 0.0502f, 0.0103f,
            0.0178f, 0.9658f, 0.0164f,
            -0.0016f, -0.0044f, 1.0060f,
        ],
        VideoColorPrimaries.Bt470SysBG or VideoColorPrimaries.Ebu3213 =>
        [
            1.0440f, -0.0440f, 0f,
            0f, 1f, 0f,
            0f, 0.0118f, 0.9882f,
        ],
        _ => (float[])Identity.Clone(),
    };

    public static float[] Multiply(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var r = new float[9];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                r[(i * 3) + j] = (a[i * 3] * b[j]) + (a[(i * 3) + 1] * b[3 + j]) + (a[(i * 3) + 2] * b[6 + j]);
            }
        }

        return r;
    }

    public static double[] Multiply(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        var r = new double[9];
        for (var i = 0; i < 3; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                r[(i * 3) + j] = (a[i * 3] * b[j]) + (a[(i * 3) + 1] * b[3 + j]) + (a[(i * 3) + 2] * b[6 + j]);
            }
        }

        return r;
    }

    public static bool IsIdentity(ReadOnlySpan<float> m, float epsilon = 0.002f)
    {
        for (var i = 0; i < 9; i++)
        {
            var expect = i % 4 == 0 ? 1f : 0f;
            if (Math.Abs(m[i] - expect) > epsilon)
            {
                return false;
            }
        }

        return true;
    }

    public static bool TryInvert(ReadOnlySpan<double> m, out double[] inverse)
    {
        inverse = new double[9];
        var det = (m[0] * ((m[4] * m[8]) - (m[5] * m[7])))
                  - (m[1] * ((m[3] * m[8]) - (m[5] * m[6])))
                  + (m[2] * ((m[3] * m[7]) - (m[4] * m[6])));
        if (Math.Abs(det) < 1e-9 || double.IsNaN(det))
        {
            return false;
        }

        var inv = 1.0 / det;
        inverse[0] = ((m[4] * m[8]) - (m[5] * m[7])) * inv;
        inverse[1] = ((m[2] * m[7]) - (m[1] * m[8])) * inv;
        inverse[2] = ((m[1] * m[5]) - (m[2] * m[4])) * inv;
        inverse[3] = ((m[5] * m[6]) - (m[3] * m[8])) * inv;
        inverse[4] = ((m[0] * m[8]) - (m[2] * m[6])) * inv;
        inverse[5] = ((m[2] * m[3]) - (m[0] * m[5])) * inv;
        inverse[6] = ((m[3] * m[7]) - (m[4] * m[6])) * inv;
        inverse[7] = ((m[1] * m[6]) - (m[0] * m[7])) * inv;
        inverse[8] = ((m[0] * m[4]) - (m[1] * m[3])) * inv;
        return true;
    }
}
