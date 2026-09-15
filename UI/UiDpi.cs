using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Config;

namespace MgaSonicAnvil.UI;

/// <summary>OS の DPI に、設定の表示倍率を掛けた実効スケール。</summary>
internal static class UiDpi
{
    public static DpiScale Get(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var extra = ExtraFactor;
        if (extra <= 1.0001)
        {
            return dpi;
        }

        return new DpiScale(dpi.DpiScaleX * extra, dpi.DpiScaleY * extra);
    }

    public static double PixelsPerDip(Visual visual) => Get(visual).PixelsPerDip;

    public static double ExtraFactor => AppStorage.Settings.ResolvedUiScale();
}
