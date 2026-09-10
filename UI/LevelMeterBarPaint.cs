using System.Windows;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>メインのレベルメーターと同じ塗りグラデ。</summary>
internal static class LevelMeterBarPaint
{
    public static LinearGradientBrush Create(bool vertical)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new Point(0, 1) : new Point(0, 0),
            EndPoint = vertical ? new Point(0, 0) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            [
                new GradientStop(Color.FromRgb(0x0A, 0x30, 0x44), 0),
                new GradientStop(Color.FromRgb(0x0D, 0x4A, 0x62), 0.26),
                new GradientStop(Color.FromRgb(0x3A, 0xB8, 0xE8), 0.55),
                new GradientStop(Color.FromRgb(0xC8, 0xEF, 0xFF), 0.82),
                new GradientStop(Color.FromRgb(0xF8, 0xFE, 0xFF), 1),
            ],
        };
        brush.Freeze();
        return brush;
    }
}
