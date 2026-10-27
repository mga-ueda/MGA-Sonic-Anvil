namespace MgaSonicAnvil.Video.Dxgi;

/// <summary>
/// DXGI 経路の「保持」判定。直前に出したフレームが今の表示位置をまだ覆っているなら、
/// ポンプへ位置を要求し直さず（ワーカーを起こさず）、Present もしない（RT に残っている絵をそのまま使う）。
/// 一時停止・スクラブ静止中に 33ms ごとの起床とアップロードを無くすため。
/// <para>
/// 窓は「そのフレームの 1 枚ぶん [pts, pts+dur)」だけ。前後に 1 枚ぶん広げると、1 倍再生（tick 33ms・フレーム 33.3ms）で
/// 次のフレームが窓に飲まれて 1 枚おきに落ちたり、1 コマ戻しが前の絵のまま止まるので広げない。
/// </para>
/// </summary>
internal static class DxgiFrameHold
{
    /// <summary>
    /// 直前フレーム（<paramref name="lastPts"/>／<paramref name="lastDur"/>）が <paramref name="target"/> を覆っているか。
    /// 長さが取れていない（≦1 tick）フレームは <paramref name="frameDur"/>（ソースのフレーム間隔）1 枚ぶんとみなす。
    /// </summary>
    public static bool ShouldHold(long lastPts, long lastDur, long target, long frameDur)
    {
        if (lastPts < 0 || target < 0)
        {
            return false;
        }

        var span = lastDur > 1 ? lastDur : Math.Max(1, frameDur);
        return target >= lastPts && target < lastPts + span;
    }
}
