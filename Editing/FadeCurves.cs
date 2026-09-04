using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.Editing;

/// <summary>
/// Wwise のカーブ形状。TimeCaster の <c>WwiseFadeShape</c> と同じ ID。
/// </summary>
internal enum FadeShape
{
    Log3 = 0,
    Sine = 1,
    Log1 = 2,
    InvSCurve = 3,
    Linear = 4,
    Constant = 5,
    SCurve = 6,
    Exp1 = 7,
    ReciprocalSine = 8,
    Exp3 = 9,
}

/// <summary>
/// TimeCaster の MusicClip フェードと同じ立ち上がり式。並びも In / Out で同じ。
/// </summary>
internal static class FadeCurves
{
    public const FadeShape Default = FadeShape.SCurve;

    /// <summary>TimeCaster フェードイン右クリックと同じ順。Constant は除く。</summary>
    public static IReadOnlyList<FadeShape> MenuOrderFadeIn { get; } =
    [
        FadeShape.Log3,
        FadeShape.Sine,
        FadeShape.Log1,
        FadeShape.InvSCurve,
        FadeShape.Linear,
        FadeShape.SCurve,
        FadeShape.Exp1,
        FadeShape.ReciprocalSine,
        FadeShape.Exp3,
    ];

    /// <summary>TimeCaster フェードアウト右クリックと同じ順。Constant は除く。</summary>
    public static IReadOnlyList<FadeShape> MenuOrderFadeOut { get; } =
    [
        FadeShape.Exp3,
        FadeShape.ReciprocalSine,
        FadeShape.Exp1,
        FadeShape.InvSCurve,
        FadeShape.Linear,
        FadeShape.SCurve,
        FadeShape.Log1,
        FadeShape.Sine,
        FadeShape.Log3,
    ];

    public static IReadOnlyList<FadeShape> MenuOrder(bool fadeIn) =>
        fadeIn ? MenuOrderFadeIn : MenuOrderFadeOut;

    /// <summary>
    /// 0→1 の立ち上がり。Wwise Authoring / TimeCaster と同じ（Base N は冪。decade 対数ではない）。
    /// </summary>
    public static float Apply01(FadeShape shape, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return shape switch
        {
            FadeShape.Log3 => (float)(1d - Math.Pow(1d - t, 3d)),
            FadeShape.Sine => (float)Math.Sin(t * Math.PI * 0.5),
            FadeShape.Log1 => (float)(1d - Math.Pow(1d - t, 1.41d)),
            FadeShape.InvSCurve => InvSCurve(t),
            FadeShape.Linear => (float)t,
            FadeShape.Constant => t >= 1d ? 1f : 0f,
            FadeShape.SCurve => SCurve(t),
            FadeShape.Exp1 => (float)Math.Pow(t, 1.41d),
            FadeShape.ReciprocalSine => (float)(1d - Math.Cos(t * Math.PI * 0.5)),
            FadeShape.Exp3 => (float)Math.Pow(t, 3d),
            _ => SCurve(t),
        };
    }

    /// <summary>フェードインは立ち上がり、フェードアウトは 1 - 立ち上がり。</summary>
    public static float Gain(FadeShape shape, bool fadeIn, double t)
    {
        var rising = Apply01(shape, t);
        return fadeIn ? rising : 1f - rising;
    }

    public static float GainAtFrame(FadeShape shape, bool fadeIn, long frame, long startFrame, long length)
    {
        if (length <= 1)
        {
            // 1 サンプルでも端点の意味を持たせる（イン=無音、アウト=無音）。
            return 0f;
        }

        var t = (frame - startFrame) / (double)(length - 1);
        return Gain(shape, fadeIn, t);
    }

    /// <summary>
    /// 選択は [start, end) だが、終端線上のサンプルもフェード対象にする。
    /// （終端がマーカーのとき、そのサンプルが残って崖になるのを防ぐ）
    /// </summary>
    public static WaveSelection InclusiveSampleRange(WaveSelection selection, long frameCount)
    {
        if (selection.IsEmpty || frameCount <= 0)
        {
            return WaveSelection.Empty;
        }

        var start = selection.StartFrame;
        var end = selection.EndFrame;
        if (end < frameCount)
        {
            end += 1;
        }

        return new WaveSelection(start, end).Clamp(frameCount);
    }

    private static float SCurve(double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return (float)(t * t * (3d - 2d * t));
    }

    private static float InvSCurve(double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return (float)(t * (2d - 3d * t + 2d * t * t));
    }
}
