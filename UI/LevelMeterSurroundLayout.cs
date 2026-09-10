namespace MgaSonicAnvil.UI;

/// <summary>
/// サラウンド時のバー配置とチャンネル名の座標。ステレオと同じ額縁
/// （中央のバー群 + 左右の目盛り列）になるよう、バー群は
/// <see cref="BarsBlockWidth"/> 以内に収めて中央へ置く。
/// </summary>
internal static class LevelMeterSurroundLayout
{
    /// <summary>ラベル同士のすき間。</summary>
    public const double LabelGap = 1;

    /// <summary>右端に必ず残す余白。ここを割ると見切れる。</summary>
    public const double RightMargin = 1;

    /// <summary>バー群の最大幅。ステレオの 14px × 4 本と同じ。</summary>
    public const double BarsBlockWidth = 56;

    /// <summary>1 本の最大幅（ステレオと同じ）と最小幅。</summary>
    public const double MaxBarWidth = 14;
    public const double MinBarWidth = 2;

    public const double MaxFont = 6;
    public const double MinFont = 5;
    public const double FontStep = 0.5;

    /// <summary>バー群 56px を等分。端数は切り捨ててバーを細くする。</summary>
    public static double BarWidth(int channels)
    {
        if (channels <= 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Floor(BarsBlockWidth / channels), MinBarWidth, MaxBarWidth);
    }

    /// <summary>バー群を列の中央へ。左右に目盛り列ぶんの空きが必ず残る。</summary>
    public static double BarsLeft(double x, double width, double barWidth, int channels) =>
        x + Math.Floor((width - (barWidth * channels)) * 0.5);

    /// <summary>
    /// 各名前をバー中央に置いてから右端から詰め直す。1 段で入らなければ font を縮め、
    /// それでも入らなければ 2 段（偶数=下段 / 奇数=上段）に振る。どれも無理なら
    /// <see cref="SurroundLabelLayout.None"/>。
    /// </summary>
    /// <param name="measure">フォントサイズを受け取り、各名前の実インク幅を返す。</param>
    /// <param name="leftEdge">ラベルが越えてはいけない左端。省略時はバー列の左端。</param>
    public static SurroundLabelLayout SolveLabels(
        double rightEdge,
        double barsLeft,
        double barW,
        int channels,
        Func<double, double[]> measure,
        double? leftEdge = null)
    {
        if (channels <= 0 || barW <= 0 || rightEdge - barsLeft <= 0)
        {
            return SurroundLabelLayout.None;
        }

        var limit = leftEdge ?? barsLeft;
        for (var rows = 1; rows <= 2; rows++)
        {
            for (var font = MaxFont; font > MinFont - (FontStep * 0.5); font -= FontStep)
            {
                var widths = measure(font);
                if (widths.Length < channels)
                {
                    return SurroundLabelLayout.None;
                }

                var x = TryPlace(rightEdge, limit, barsLeft, barW, channels, widths, rows);
                if (x is null)
                {
                    continue;
                }

                var row = new int[channels];
                if (rows == 2)
                {
                    for (var i = 0; i < channels; i++)
                    {
                        row[i] = i % 2;
                    }
                }

                return new SurroundLabelLayout(font, rows, x, row);
            }
        }

        return SurroundLabelLayout.None;
    }

    /// <summary>右端から左へ順に押し込む。先頭が <paramref name="leftEdge"/> より左へ出たら失敗。</summary>
    private static double[]? TryPlace(
        double rightEdge,
        double leftEdge,
        double barsLeft,
        double barW,
        int channels,
        IReadOnlyList<double> widths,
        int rows)
    {
        var x = new double[channels];
        for (var row = 0; row < rows; row++)
        {
            var right = rightEdge - RightMargin;
            var first = -1;
            for (var i = channels - 1; i >= 0; i--)
            {
                if (rows == 2 && i % 2 != row)
                {
                    continue;
                }

                var w = Math.Max(0, widths[i]);
                var placed = barsLeft + ((i + 0.5) * barW) - (w * 0.5);
                if (placed + w > right)
                {
                    placed = right - w;
                }

                x[i] = placed;
                right = placed - LabelGap;
                first = i;
            }

            if (first >= 0 && x[first] < leftEdge)
            {
                return null;
            }
        }

        return x;
    }
}

/// <summary>チャンネル名の解。<c>Row</c> は 0 が下段。</summary>
internal readonly record struct SurroundLabelLayout(double FontSize, int Rows, double[] X, int[] Row)
{
    public static readonly SurroundLabelLayout None = new(0, 0, [], []);

    public bool HasLabels => Rows > 0 && X.Length > 0;
}
