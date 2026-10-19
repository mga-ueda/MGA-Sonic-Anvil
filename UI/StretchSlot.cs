using System.Windows;
using System.Windows.Controls;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 子の高さだけを要求する。幅は並べるときに親から受け取り、パス文字列で親を広げない。
/// </summary>
internal sealed class StretchSlot : Decorator
{
    protected override Size MeasureOverride(Size constraint)
    {
        Child?.Measure(new Size(constraint.Width, constraint.Height));
        return new Size(0, Child?.DesiredSize.Height ?? 0);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        Child?.Arrange(new Rect(arrangeSize));
        return arrangeSize;
    }
}
