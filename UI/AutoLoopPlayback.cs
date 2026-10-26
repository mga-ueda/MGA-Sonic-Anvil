using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// Single Loop の再生仕様（エディタ／プレイヤー共通の判定）。
/// <list type="bullet">
/// <item>
/// <description>
/// エディタ＋サンプルループあり: 再生窓は [S, E)。S より前（イントロ）からの開始を許可し、
/// 一度通したあと E 到達で S へ折り返して無限ループする。E 以降の開始だけ S へスナップする。
/// StartPlayback の吸い寄せ解除に加え、Bind／Mp3Gapless.ClampStart にも
/// allowBeforePlayWindowStart を渡さないと再生直後に S へ再ジャンプする。
/// </description>
/// </item>
/// <item>
/// <description>
/// プレイヤー: サンプルループは無視し、再生窓は全長 [0, N)。曲末で先頭へ戻す
/// （割合ジャンプが S–E 窓に吸われないようにする）。
/// </description>
/// </item>
/// <item>
/// <description>
/// 選択範囲やプレイヤー A-B があるときはそちらが再生窓になり、このイントロ許容は使わない
/// （窓の外からの開始は窓の始点へスナップする）。
/// </description>
/// </item>
/// </list>
/// </summary>
internal static class AutoLoopPlayback
{
    /// <summary>
    /// Single Loop がサンプルループを再生窓にしているか（エディタのみ。選択／A-B より下位）。
    /// プレイヤーモードでは常に false（サンプルループ無視）。
    /// </summary>
    public static bool UsesSampleLoopWindow(
        bool autoLoopEnabled,
        bool sampleLoopEmpty,
        bool hasSelection,
        bool hasPlayerAbLoop,
        bool playerMode = false) =>
        !playerMode
        && autoLoopEnabled
        && !sampleLoopEmpty
        && !hasSelection
        && !hasPlayerAbLoop;

    /// <summary>
    /// 再生開始フレームを再生窓に合わせる。
    /// <paramref name="allowBeforeStart"/> が true のとき（Single Loop＋サンプルループ／フェード調整）は
    /// 窓の始点より前をそのまま許し、終端以降だけ始点へ戻す。
    /// </summary>
    public static long ClampStartFrame(long startFrame, WaveSelection range, bool allowBeforeStart)
    {
        if (range.IsEmpty)
        {
            return startFrame;
        }

        if (allowBeforeStart)
        {
            return startFrame >= range.EndFrame ? range.StartFrame : startFrame;
        }

        if (startFrame < range.StartFrame || startFrame >= range.EndFrame)
        {
            return range.StartFrame;
        }

        return startFrame;
    }
}
