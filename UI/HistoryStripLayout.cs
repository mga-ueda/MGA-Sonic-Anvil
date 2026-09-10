namespace MgaSonicAnvil.UI;

/// <summary>履歴ストリップに収まる行の開始位置。古い行は上側で切る。</summary>
internal static class HistoryStripLayout
{
    public static int VisibleCount(double height, double rowHeight, double padY)
    {
        if (height <= 0 || rowHeight <= 0)
        {
            return 0;
        }

        var inner = height - (padY * 2);
        return inner <= 0 ? 0 : (int)Math.Floor(inner / rowHeight);
    }

    public static int VisibleStart(int itemCount, int currentIndex, int visibleCount)
    {
        if (itemCount <= 0 || visibleCount <= 0)
        {
            return 0;
        }

        currentIndex = Math.Clamp(currentIndex, 0, itemCount - 1);
        var start = currentIndex - visibleCount + 1;
        if (start < 0)
        {
            start = 0;
        }

        if (start + visibleCount > itemCount)
        {
            start = Math.Max(0, itemCount - visibleCount);
        }

        return start;
    }
}
