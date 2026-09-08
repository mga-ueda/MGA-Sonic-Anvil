namespace MgaSonicAnvil.UI;

/// <summary>波形／スペクトログラム共通。表示開始を 1px 格子に載せ、スクロール差分を列シフトにする。</summary>
internal static class WaveScroll
{
    public static void Quantize(
        double viewStart,
        double viewSpan,
        int width,
        out double quantStart,
        out double bmpSpan,
        out int bmpWidth)
    {
        width = Math.Max(1, width);
        viewSpan = Math.Max(1e-9, viewSpan);
        var framesPerPx = viewSpan / width;
        quantStart = Math.Floor(viewStart / framesPerPx) * framesPerPx;
        bmpWidth = width + 1;
        bmpSpan = bmpWidth * framesPerPx;
    }

    public static bool TryPixelShift(
        double oldStart,
        double newStart,
        double span,
        int width,
        out int shiftPx)
    {
        shiftPx = 0;
        if (span <= 1 || width <= 1)
        {
            return false;
        }

        var shift = (newStart - oldStart) / span * width;
        shiftPx = (int)Math.Round(shift);
        return shiftPx != 0
            && Math.Abs(shift - shiftPx) <= 0.2
            && Math.Abs(shiftPx) < width;
    }

    public static void ShiftPacked(int[] pixels, int width, int height, int shiftPx)
    {
        if (width <= 0 || height <= 0 || shiftPx == 0 || Math.Abs(shiftPx) >= width)
        {
            return;
        }

        if (shiftPx > 0)
        {
            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                Array.Copy(pixels, row + shiftPx, pixels, row, width - shiftPx);
                Array.Clear(pixels, row + width - shiftPx, shiftPx);
            }

            return;
        }

        var left = -shiftPx;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            Array.Copy(pixels, row, pixels, row + left, width - left);
            Array.Clear(pixels, row, left);
        }
    }
}
