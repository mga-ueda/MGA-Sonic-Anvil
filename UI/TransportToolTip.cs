using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MgaSonicAnvil.UI;

/// <summary>トランスポートボタン専用。Tips 枠とは別に、短いツールチップを出す。</summary>
internal static class TransportToolTip
{
    public static void Attach(FrameworkElement target, string text)
    {
        ToolTipService.SetIsEnabled(target, true);
        ToolTipService.SetShowOnDisabled(target, true);
        ToolTipService.SetInitialShowDelay(target, 400);
        ToolTipService.SetBetweenShowDelay(target, 0);
        ToolTipService.SetShowDuration(target, 20000);
        ToolTipService.SetPlacement(target, PlacementMode.Top);
        ToolTipService.SetVerticalOffset(target, -2);
        target.ToolTip = text;
    }
}
