namespace MgaSonicAnvil.UI;

internal enum WaveformTileArrange
{
    Off,
    Vertical,
    Horizontal,
    Grid,
}

internal readonly record struct WaveformTileCell(int Row, int Column, int RowSpan, int ColumnSpan);

/// <summary>
/// 複数波形のタイル配置。横並び / 縦並び / 格子。格子はほぼ正方形で、最終行が 1 枚だけなら横断する。
/// </summary>
internal static class WaveformTileLayout
{
    public const string StoredOff = "off";
    public const string StoredVertical = "vertical";
    public const string StoredHorizontal = "horizontal";
    public const string StoredGrid = "grid";

    public static string Format(WaveformTileArrange arrange) =>
        arrange switch
        {
            WaveformTileArrange.Vertical => StoredVertical,
            WaveformTileArrange.Horizontal => StoredHorizontal,
            WaveformTileArrange.Grid => StoredGrid,
            _ => StoredOff,
        };

    public static WaveformTileArrange Parse(string? text) =>
        (text ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            StoredVertical => WaveformTileArrange.Vertical,
            StoredHorizontal => WaveformTileArrange.Horizontal,
            StoredGrid => WaveformTileArrange.Grid,
            _ => WaveformTileArrange.Off,
        };

    public static WaveformTileArrange Next(WaveformTileArrange current) =>
        current switch
        {
            WaveformTileArrange.Off => WaveformTileArrange.Horizontal,
            WaveformTileArrange.Horizontal => WaveformTileArrange.Vertical,
            WaveformTileArrange.Vertical => WaveformTileArrange.Grid,
            _ => WaveformTileArrange.Off,
        };

    /// <summary>
    /// 枚数によっては格子が横並びと同じ形になる。今まで出した配置と同じ見た目は飛ばす。
    /// </summary>
    public static WaveformTileArrange Next(WaveformTileArrange current, int count)
    {
        var next = Next(current);
        while (next != WaveformTileArrange.Off && ShouldSkip(current, next, count))
        {
            next = Next(next);
        }

        return next;
    }

    private static bool ShouldSkip(
        WaveformTileArrange current,
        WaveformTileArrange candidate,
        int count)
    {
        if (SameShape(current, candidate, count))
        {
            return true;
        }

        var walk = WaveformTileArrange.Off;
        while (walk != current)
        {
            walk = Next(walk);
            if (walk == WaveformTileArrange.Off || walk == current)
            {
                break;
            }

            if (SameShape(walk, candidate, count))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SameShape(WaveformTileArrange left, WaveformTileArrange right, int count)
    {
        if (left == right)
        {
            return true;
        }

        ChooseGrid(left, count, out var leftRows, out var leftCols);
        ChooseGrid(right, count, out var rightRows, out var rightCols);
        return leftRows > 0 && leftRows == rightRows && leftCols == rightCols;
    }

    public static void ChooseGrid(WaveformTileArrange arrange, int count, out int rows, out int cols)
    {
        if (count <= 0 || arrange == WaveformTileArrange.Off)
        {
            rows = 0;
            cols = 0;
            return;
        }

        switch (arrange)
        {
            case WaveformTileArrange.Vertical:
                rows = count;
                cols = 1;
                return;
            case WaveformTileArrange.Horizontal:
                rows = 1;
                cols = count;
                return;
            default:
                cols = (int)Math.Ceiling(Math.Sqrt(count));
                rows = (int)Math.Ceiling(count / (double)cols);
                return;
        }
    }

    public static WaveformTileCell Cell(int index, int count, int rows, int cols)
    {
        if (count <= 0 || rows <= 0 || cols <= 0 || index < 0 || index >= count)
        {
            return new WaveformTileCell(0, 0, 1, 1);
        }

        var row = index / cols;
        var col = index % cols;
        var span = 1;
        if (row == rows - 1)
        {
            var lastCount = count - row * cols;
            if (lastCount == 1)
            {
                col = 0;
                span = cols;
            }
        }

        return new WaveformTileCell(row, col, 1, span);
    }

    public static WaveformAnalysisView ResolveAnalysis(
        WaveformAnalysisView? stored,
        WaveformAnalysisView fallback) =>
        stored ?? fallback;
}
