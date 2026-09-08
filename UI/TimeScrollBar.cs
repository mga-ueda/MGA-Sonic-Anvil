using System.Windows.Controls.Primitives;

namespace MgaSonicAnvil.UI;

internal sealed class TimeScrollBar : ScrollBar
{
    public TimeScrollBar()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        Height = DesignMetrics.WaveformScrollBarHeight;
        Minimum = 0;
        Maximum = 0;
        ViewportSize = 1;
        SmallChange = 1;
        LargeChange = 1;
        Focusable = false;
        // 派生型には App.xaml の ScrollBar 暗黙スタイルが当たらない。
        SetResourceReference(StyleProperty, "DarkScrollBarStyle");
        SetResourceReference(BackgroundProperty, "TimelineWellBackBrush");
    }

    public void Sync(double viewStart, double viewSpan, long totalFrames)
    {
        if (totalFrames <= 0)
        {
            Minimum = 0;
            Maximum = 1;
            ViewportSize = 1;
            Value = 0;
            IsEnabled = false;
            return;
        }

        var span = Math.Max(1, viewSpan);
        var max = Math.Max(0, totalFrames - span);
        if (max < 1)
        {
            // Track は Maximum==0（つまみ＝全幅）だと Thumb を Hidden にする。
            max = 1;
            span = Math.Max(1, totalFrames);
        }

        IsEnabled = true;
        ViewportSize = span;
        Minimum = 0;
        Maximum = max;
        SmallChange = Math.Max(1, span * 0.05);
        LargeChange = Math.Max(1, span * 0.9);
        Value = Math.Clamp(viewStart, 0, Math.Max(0, totalFrames - viewSpan));
    }
}
