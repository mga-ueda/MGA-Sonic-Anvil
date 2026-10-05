namespace MgaSonicAnvil.UI;

/// <summary>
/// プレイリスト背面 PDF の拡縮。100%＝画面に全体が収まる（Uniform）、最大 300%。
/// 幅いっぱい／高さいっぱいのどちらかに達した倍率で一旦止める。
/// </summary>
internal static class LibraryPdfZoom
{
    public const double Min = 1.0;
    public const double Max = 3.0;
    public const double StepAmount = 0.05;

    /// <summary>幅いっぱいと高さいっぱいのうち、全体収まりからの倍率。</summary>
    public static double SnapFactor(double viewportWidth, double viewportHeight, double pageWidth, double pageHeight)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || pageWidth <= 0 || pageHeight <= 0)
        {
            return Min;
        }

        var fit = Math.Min(viewportWidth / pageWidth, viewportHeight / pageHeight);
        var fill = Math.Max(viewportWidth / pageWidth, viewportHeight / pageHeight);
        if (fit <= 0)
        {
            return Min;
        }

        return Math.Clamp(fill / fit, Min, Max);
    }

    /// <summary>
    /// 1 ステップ拡縮。スナップをまたぐ／着地したらそこに止め、押しっぱなし（isRepeat）では進まない。
    /// キーを離して押し直すとスナップを越えて進める。
    /// </summary>
    public static double Step(
        double current,
        int direction,
        double snap,
        bool isRepeat,
        ref bool snapLatched)
    {
        if (direction == 0)
        {
            return Clamp(current);
        }

        snap = Math.Clamp(snap, Min, Max);
        current = Clamp(current);
        var target = Clamp(current + direction * StepAmount);

        if (snapLatched && isRepeat && NearlyEqual(current, snap))
        {
            return current;
        }

        if (direction > 0
            && current < snap - 1e-6
            && target >= snap - 1e-6)
        {
            snapLatched = true;
            return snap;
        }

        if (direction < 0
            && current > snap + 1e-6
            && target <= snap + 1e-6)
        {
            snapLatched = true;
            return snap;
        }

        if (!isRepeat)
        {
            snapLatched = false;
        }

        return target;
    }

    public static double Clamp(double zoom) => Math.Clamp(zoom, Min, Max);

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;
}
