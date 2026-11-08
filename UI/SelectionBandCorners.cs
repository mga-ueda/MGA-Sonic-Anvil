using System.Windows;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>連続選択帯の角丸。<see cref="DesignMetrics.ControlCornerRadius"/> と組み合わせる。</summary>
internal static class SelectionBandCorners
{
    public static SelectionBandEdge EdgeAt(int index, int count)
    {
        if (count <= 1)
        {
            return SelectionBandEdge.Solo;
        }

        if (index <= 0)
        {
            return SelectionBandEdge.First;
        }

        if (index >= count - 1)
        {
            return SelectionBandEdge.Last;
        }

        return SelectionBandEdge.Middle;
    }

    public static CornerRadius Of(SelectionBandEdge edge, double radius)
    {
        if (!double.IsFinite(radius) || radius <= 0)
        {
            return default;
        }

        return edge switch
        {
            SelectionBandEdge.Solo => new CornerRadius(radius),
            SelectionBandEdge.First => new CornerRadius(radius, radius, 0, 0),
            SelectionBandEdge.Last => new CornerRadius(0, 0, radius, radius),
            _ => default,
        };
    }
}
