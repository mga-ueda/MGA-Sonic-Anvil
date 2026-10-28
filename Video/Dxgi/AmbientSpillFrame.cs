namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// 光漏れ（ambient spill）用に、デコード済みフレームを縮小 BGRA へ落とす。
/// D3DImage の共有は複数 Image でうまく描けないので、縁へにじませる薄い光は CPU ビットマップで作る。
/// どうせ強くぼかして拡大するので、最近傍の粗い縮小・整数近似の色変換で足りる。
/// </summary>
internal static class AmbientSpillFrame
{
    /// <summary>縮小後の最長辺。これ以下なら等倍。ぼかし前提なので小さくて十分。</summary>
    internal const int MaxEdge = 480;

    /// <summary>最長辺が <see cref="MaxEdge"/> を超えないよう縮めた寸法（アスペクト維持）。</summary>
    internal static (int Width, int Height) DownscaledSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        var longest = Math.Max(width, height);
        if (longest <= MaxEdge)
        {
            return (width, height);
        }

        var scale = MaxEdge / (double)longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    /// <summary>
    /// <paramref name="frame"/>（NV12 か BGRA）を最近傍で <paramref name="dstWidth"/>×<paramref name="dstHeight"/> の
    /// BGRA（トップダウン、stride = dstWidth*4）へ書き出す。<paramref name="dst"/> は dstWidth*dstHeight*4 以上。
    /// </summary>
    internal static void FillBgra(DxgiDecodedFrame frame, byte[] dst, int dstWidth, int dstHeight)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Format == DxgiFramePixelFormat.Nv12)
        {
            FillFromNv12(frame, dst, dstWidth, dstHeight);
        }
        else
        {
            FillFromBgra(frame.Pixels, frame.Width, frame.Height, frame.Stride, dst, dstWidth, dstHeight);
        }
    }

    /// <summary>BGRA ソース（テストパターン等）を縮小 BGRA へ。</summary>
    internal static void FillFromBgra(
        ReadOnlySpan<byte> src,
        int srcWidth,
        int srcHeight,
        int srcStride,
        byte[] dst,
        int dstWidth,
        int dstHeight)
    {
        if (!Validate(srcWidth, srcHeight, dstWidth, dstHeight, dst))
        {
            return;
        }

        for (var y = 0; y < dstHeight; y++)
        {
            var sy = (y * srcHeight) / dstHeight;
            var srcRow = sy * srcStride;
            var dstRow = y * dstWidth * 4;
            for (var x = 0; x < dstWidth; x++)
            {
                var sx = (x * srcWidth) / dstWidth;
                var s = srcRow + (sx * 4);
                var d = dstRow + (x * 4);
                dst[d] = src[s];
                dst[d + 1] = src[s + 1];
                dst[d + 2] = src[s + 2];
                dst[d + 3] = 0xFF;
            }
        }
    }

    private static void FillFromNv12(DxgiDecodedFrame frame, byte[] dst, int dstWidth, int dstHeight)
    {
        var srcWidth = frame.Width;
        var srcHeight = frame.Height;
        if (!Validate(srcWidth, srcHeight, dstWidth, dstHeight, dst))
        {
            return;
        }

        var info = frame.ColorInfo.Normalized(srcHeight);
        var full = info.IsFullRange;
        // 係数（×1024）: 601 と 709 のみ区別。2020 は 709 で近似（ぼかす光漏れなので精度は求めない）。
        var bt601 = info.Matrix == VideoYuvMatrix.Bt601;
        int kr = bt601 ? 1436 : 1613, kgU = bt601 ? 352 : 192, kgV = bt601 ? 731 : 479, kb = bt601 ? 1815 : 1900;
        var yScale = full ? 1024 : 1196; // 255/219
        var yOffset = full ? 0 : 16;
        var cScale = full ? 1024 : 1169; // 255/224

        var src = frame.Pixels;
        var yStride = frame.Stride;
        var uvStride = frame.UvStride;
        var uvBase = frame.UvOffset;
        for (var y = 0; y < dstHeight; y++)
        {
            var sy = (y * srcHeight) / dstHeight;
            var yRow = sy * yStride;
            var uvRow = uvBase + ((sy / 2) * uvStride);
            var dstRow = y * dstWidth * 4;
            for (var x = 0; x < dstWidth; x++)
            {
                var sx = (x * srcWidth) / dstWidth;
                var yy = ((src[yRow + sx] - yOffset) * yScale) >> 10;
                var uvIndex = uvRow + ((sx / 2) * 2);
                var u = ((src[uvIndex] - 128) * cScale) >> 10;
                var v = ((src[uvIndex + 1] - 128) * cScale) >> 10;
                var r = yy + ((kr * v) >> 10);
                var g = yy - ((kgU * u) >> 10) - ((kgV * v) >> 10);
                var b = yy + ((kb * u) >> 10);
                var d = dstRow + (x * 4);
                dst[d] = (byte)Math.Clamp(b, 0, 255);
                dst[d + 1] = (byte)Math.Clamp(g, 0, 255);
                dst[d + 2] = (byte)Math.Clamp(r, 0, 255);
                dst[d + 3] = 0xFF;
            }
        }
    }

    private static bool Validate(int srcWidth, int srcHeight, int dstWidth, int dstHeight, byte[] dst) =>
        srcWidth > 0
        && srcHeight > 0
        && dstWidth > 0
        && dstHeight > 0
        && dst is not null
        && dst.Length >= dstWidth * dstHeight * 4;
}
