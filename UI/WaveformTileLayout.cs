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

    /// <summary>タイル1枚の最小幅。dB 目盛りと波形の切れ端が見える程度。</summary>
    public static double MinTileWidth =>
        DesignMetrics.DbScaleWidth + DesignMetrics.From96(96);

    /// <summary>タイル1枚の最小高さ。見出し＋時間軸＋波形の切れ端。</summary>
    public static double MinTileHeight =>
        DesignMetrics.DocumentTabBarHeight
        + DesignMetrics.RulerHeight
        + DesignMetrics.From96(48);

    /// <summary>
    /// 枚数によっては格子が横並びと同じ形になる。今まで出した配置と同じ見た目は飛ばす。
    /// ホスト寸法を渡すと、1枚が下限を下回る配置も飛ばす。左右・上下が収まらなければ格子へ進む。
    /// </summary>
    public static WaveformTileArrange Next(WaveformTileArrange current, int count) =>
        Next(current, count, double.PositiveInfinity, double.PositiveInfinity);

    public static WaveformTileArrange Next(
        WaveformTileArrange current,
        int count,
        double hostWidth,
        double hostHeight)
    {
        var next = Next(current);
        while (next != WaveformTileArrange.Off && ShouldSkip(current, next, count, hostWidth, hostHeight))
        {
            if ((next == WaveformTileArrange.Horizontal || next == WaveformTileArrange.Vertical)
                && !Fits(next, count, hostWidth, hostHeight)
                && !ShouldSkip(current, WaveformTileArrange.Grid, count, hostWidth, hostHeight))
            {
                return WaveformTileArrange.Grid;
            }

            next = Next(next);
        }

        return next;
    }

    /// <summary>
    /// 指定の並べ方が収まらなければ格子を試し、それも無理なら解除。
    /// </summary>
    public static WaveformTileArrange Fallback(
        WaveformTileArrange arrange,
        int count,
        double hostWidth,
        double hostHeight)
    {
        if (arrange == WaveformTileArrange.Off)
        {
            return WaveformTileArrange.Off;
        }

        if (Fits(arrange, count, hostWidth, hostHeight))
        {
            return arrange;
        }

        if (arrange != WaveformTileArrange.Grid
            && Fits(WaveformTileArrange.Grid, count, hostWidth, hostHeight))
        {
            return WaveformTileArrange.Grid;
        }

        return WaveformTileArrange.Off;
    }

    /// <summary>
    /// その配置で一番小さいマスが下限以上か。解除は常に可。寸法が未確定（1 以下）のときは判定しない。
    /// </summary>
    public static bool Fits(WaveformTileArrange arrange, int count, double hostWidth, double hostHeight)
    {
        if (arrange == WaveformTileArrange.Off)
        {
            return true;
        }

        if (count < 2)
        {
            return false;
        }

        if (hostWidth <= 1 || hostHeight <= 1)
        {
            return true;
        }

        ChooseGrid(arrange, count, out var rows, out var cols);
        if (rows <= 0 || cols <= 0)
        {
            return false;
        }

        return hostWidth / cols >= MinTileWidth && hostHeight / rows >= MinTileHeight;
    }

    private static bool ShouldSkip(
        WaveformTileArrange current,
        WaveformTileArrange candidate,
        int count,
        double hostWidth,
        double hostHeight)
    {
        if (!Fits(candidate, count, hostWidth, hostHeight))
        {
            return true;
        }

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

    /// <summary>最終行が 1 枚なら横断するので空きは出ない。11 枚の 3×4 のように余りがあるときだけ空きマス。</summary>
    public static IReadOnlyList<WaveformTileCell> UnusedCells(int count, int rows, int cols)
    {
        if (count <= 0 || rows <= 0 || cols <= 0)
        {
            return [];
        }

        var used = new bool[rows, cols];
        for (var i = 0; i < count; i++)
        {
            var cell = Cell(i, count, rows, cols);
            for (var r = 0; r < cell.RowSpan; r++)
            {
                for (var c = 0; c < cell.ColumnSpan; c++)
                {
                    var row = cell.Row + r;
                    var col = cell.Column + c;
                    if ((uint)row < (uint)rows && (uint)col < (uint)cols)
                    {
                        used[row, col] = true;
                    }
                }
            }
        }

        var unused = new List<WaveformTileCell>();
        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                if (!used[row, col])
                {
                    unused.Add(new WaveformTileCell(row, col, 1, 1));
                }
            }
        }

        return unused;
    }

    public static WaveformAnalysisView ResolveAnalysis(
        WaveformAnalysisView? stored,
        WaveformAnalysisView fallback) =>
        stored ?? fallback;
}
