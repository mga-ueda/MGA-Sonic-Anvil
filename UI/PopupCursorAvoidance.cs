using System.Windows;
using System.Windows.Controls.Primitives;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 中央寄せを基本に、カーソルが項目に乗らないよう最短だけずらす。
/// </summary>
internal static class PopupCursorAvoidance
{
    public const double Gap = 8;

    public static Point Place(Size popup, Size target, Point cursor)
    {
        var preferred = new Point(
            (target.Width - popup.Width) / 2,
            (target.Height - popup.Height) / 2);
        if (!ContainsInclusive(new Rect(preferred, popup), cursor))
        {
            return Clamp(preferred, popup, target);
        }

        foreach (var candidate in Candidates(preferred, popup, cursor))
        {
            var clamped = Clamp(candidate, popup, target);
            if (!ContainsInclusive(new Rect(clamped, popup), cursor))
            {
                return clamped;
            }
        }

        return Clamp(preferred, popup, target);
    }

    public static CustomPopupPlacement[] Callback(
        Size popupSize,
        Size targetSize,
        Point cursor) =>
        [new CustomPopupPlacement(Place(popupSize, targetSize, cursor), PopupPrimaryAxis.None)];

    private static IEnumerable<Point> Candidates(Point preferred, Size popup, Point cursor)
    {
        var rect = new Rect(preferred, popup);
        var options = new (double Distance, Point Origin)[]
        {
            (cursor.Y - rect.Top, new Point(preferred.X, cursor.Y + Gap)),
            (rect.Bottom - cursor.Y, new Point(preferred.X, cursor.Y - Gap - popup.Height)),
            (cursor.X - rect.Left, new Point(cursor.X + Gap, preferred.Y)),
            (rect.Right - cursor.X, new Point(cursor.X - Gap - popup.Width, preferred.Y)),
        };
        Array.Sort(options, (left, right) => left.Distance.CompareTo(right.Distance));
        foreach (var option in options)
        {
            yield return option.Origin;
        }
    }

    private static Point Clamp(Point origin, Size popup, Size target)
    {
        var maxX = Math.Max(0, target.Width - popup.Width);
        var maxY = Math.Max(0, target.Height - popup.Height);
        return new Point(Math.Clamp(origin.X, 0, maxX), Math.Clamp(origin.Y, 0, maxY));
    }

    /// <summary>WPF の Rect.Contains は右・下辺を含まないので、カーソル回避では四辺とも内側とみなす。</summary>
    private static bool ContainsInclusive(Rect rect, Point point) =>
        point.X >= rect.Left
        && point.X <= rect.Right
        && point.Y >= rect.Top
        && point.Y <= rect.Bottom;
}
