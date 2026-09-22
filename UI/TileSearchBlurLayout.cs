using System.Windows;

namespace MgaSonicAnvil.UI;

/// <summary>
/// タイル検索でヒットしなかったタイルのぼかし。レイアウトサイズは変えない。
/// 負のマージンで半径ぶん広げると WaveformView の画素対応とファイル名帯の位置が変わり、
/// ヒットしたタイルと並んだときにずれる。端のぼかしは <c>BlurEffect</c> が描画ではみ出すので、
/// クリップを外して見せる。
/// </summary>
internal static class TileSearchBlurLayout
{
    public const double Radius = 8;

    /// <summary>ヒットしなかったタイルも 0。レイアウトをヒット側と揃える。</summary>
    public static Thickness BodyMargin => new(0);

    public static bool ClipLayersToBounds(bool veiled) => !veiled;
}
