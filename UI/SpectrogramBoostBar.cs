using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>スペクトログラム左。下端がオフ、上へ動かすと小さい成分を持ち上げる。</summary>
internal sealed class SpectrogramBoostBar : Slider
{
    internal const double UnitStep = 0.05;

    public SpectrogramBoostBar()
    {
        Orientation = Orientation.Vertical;
        Minimum = 0;
        Maximum = 1;
        SmallChange = UnitStep;
        LargeChange = 0.2;
        Value = 0;
        Focusable = false;
        IsMoveToPointEnabled = true;
        IsDirectionReversed = false;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        Width = DesignMetrics.SpectrogramBoostThumbSize;
        MinWidth = DesignMetrics.SpectrogramBoostThumbSize;
        MaxWidth = DesignMetrics.SpectrogramBoostThumbSize;
        Cursor = Cursors.Hand;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        SetResourceReference(StyleProperty, "SpectrogramBoostBarStyle");
        IsEnabledChanged += (_, _) => Cursor = IsEnabled ? Cursors.Hand : Cursors.Arrow;
    }

    public double BoostUnit => Math.Clamp(Value, Minimum, Maximum);

    public static double NudgeUnit(double unit, int direction, double step = UnitStep)
    {
        if (direction == 0)
        {
            return Math.Clamp(unit, 0, 1);
        }

        return Math.Clamp(unit + Math.Sign(direction) * step, 0, 1);
    }

    public void Nudge(int direction) =>
        Value = NudgeUnit(Value, direction, SmallChange);

    internal static Color TrackOrange => Color.FromRgb(0x9C, 0x2E, 0x00);

    internal static Color TrackWhite => Colors.White;
}
