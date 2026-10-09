namespace MgaSonicAnvil.UI;

/// <summary>
/// F8／引数起動の動画ミニ窓。原寸（100%）を基準に拡縮する。
/// キーだけ常に 25% 刻み（原寸で一旦停止）。枠ドラッグは連続（25% 刻みではない）。
/// 枠ドラッグ後の次のキーは、今の大きさに近い 25% 倍率へ着地する。
/// 倍率はモードを抜けるまで覚え、抜けると忘れる。
/// </summary>
internal static class LibraryVideoWindowScale
{
    public const double Min = 0.25;
    public const double Max = 3.0;
    public const double Native = 1.0;
    /// <summary>キー／テンキーの 1 ステップ（25%）。</summary>
    public const double KeyStepAmount = 0.25;

    /// <summary>
    /// キー 1 ステップ。常に 25% で割り切れる倍率へ着地する。
    /// 枠ドラッグで中途半端な倍率でも、+ は上側の次の 25%、- は下側の次の 25% へ。
    /// 原寸をまたぐ／着地したらそこに止め、押しっぱなしでは進まない。
    /// </summary>
    public static double StepKey(
        double current,
        int direction,
        bool isRepeat,
        ref bool nativeLatched)
    {
        if (direction == 0)
        {
            return Clamp(current);
        }

        current = Clamp(current);
        var target = Clamp(NextKeyStep(current, direction));
        const double snap = Native;

        if (nativeLatched && isRepeat && NearlyEqual(current, snap))
        {
            return current;
        }

        if (direction > 0
            && current < snap - 1e-6
            && target >= snap - 1e-6)
        {
            nativeLatched = true;
            return snap;
        }

        if (direction < 0
            && current > snap + 1e-6
            && target <= snap + 1e-6)
        {
            nativeLatched = true;
            return snap;
        }

        if (!isRepeat)
        {
            nativeLatched = false;
        }

        return target;
    }

    /// <summary>
    /// 現在値から方向側の次の 25% 刻み。格子上なら ±25%。中間なら近い側の次の格子。
    /// </summary>
    public static double NextKeyStep(double current, int direction)
    {
        current = Clamp(current);
        if (direction > 0)
        {
            // 1.13 → 1.25、1.25 → 1.50
            return Math.Floor((current + 1e-9) / KeyStepAmount) * KeyStepAmount + KeyStepAmount;
        }

        if (direction < 0)
        {
            // 1.13 → 1.00、1.25 → 1.00、1.00 → 0.75
            return Math.Floor((current - 1e-9) / KeyStepAmount) * KeyStepAmount;
        }

        return current;
    }

    public static double Clamp(double scale) => Math.Clamp(scale, Min, Max);

    /// <summary>25% 刻みか。</summary>
    public static bool IsKeyStep(double scale)
    {
        var clamped = Clamp(scale);
        var grid = Math.Round(clamped / KeyStepAmount) * KeyStepAmount;
        return NearlyEqual(clamped, grid);
    }

    /// <summary>現在のクライアント DIP と映像原寸 DIP から倍率を求める（縦横比は映像側を正とする）。</summary>
    public static double FromClientSize(
        double clientWidth,
        double clientHeight,
        double naturalDipWidth,
        double naturalDipHeight)
    {
        if (clientWidth <= 0
            || clientHeight <= 0
            || naturalDipWidth <= 0
            || naturalDipHeight <= 0)
        {
            return Native;
        }

        // 縦横どちらで測っても同じになるよう、幅基準（Apply は常に原寸比を保つ）。
        return Clamp(clientWidth / naturalDipWidth);
    }

    /// <summary>
    /// 窓の実寸から求めた倍率と、意図した倍率を突き合わせる。
    /// 作業領域で大きく縮められたときだけ実効へ合わせ、丸め／下限のわずかな差では意図側を残す
    /// （キーの 25% 格子が 47% などに化けないようにする）。
    /// </summary>
    public static double RememberAfterApply(double requested, double effectiveFromWindow)
    {
        requested = Clamp(requested);
        effectiveFromWindow = Clamp(effectiveFromWindow);
        // 作業領域などで意図より明らかに小さいときだけ実効を覚える。
        if (effectiveFromWindow < requested - 0.02)
        {
            return effectiveFromWindow;
        }

        return requested;
    }

    /// <summary>トースト用。キー格子ならちょうど %、それ以外は四捨五入。</summary>
    public static int FormatPercent(double scale)
    {
        scale = Clamp(scale);
        if (IsKeyStep(scale))
        {
            return (int)Math.Round(scale / KeyStepAmount) * (int)Math.Round(KeyStepAmount * 100);
        }

        return (int)Math.Round(scale * 100);
    }

    private static bool NearlyEqual(double a, double b) => Math.Abs(a - b) <= 1e-6;
}
