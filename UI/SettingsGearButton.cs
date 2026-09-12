using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>Wwise IM Importer と同じ歯車。押すと設定を開く。</summary>
internal sealed class SettingsGearButton : Button
{
    public SettingsGearButton()
    {
        Width = DesignMetrics.ToolbarButtonSide;
        Height = DesignMetrics.ToolbarButtonSide;
        MinWidth = DesignMetrics.ToolbarButtonSide;
        MinHeight = DesignMetrics.ToolbarButtonSide;
        Padding = new Thickness(0);
        BorderThickness = new Thickness(0);
        Background = Brushes.Transparent;
        Focusable = false;
        FocusVisualStyle = null;
        Cursor = System.Windows.Input.Cursors.Hand;
        OverridesDefaultStyle = true;
        Template = new ControlTemplate(typeof(Button));
        SnapsToDevicePixels = true;
        SetValue(AutomationProperties.NameProperty, Domain.UiStrings.AccessibleAudioSettingsButton);
        TransportHover.Attach(this);
    }

    public void RefreshAppearance() => InvalidateVisual();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint)
            ? new PointHitTestResult(this, hitTestParameters.HitPoint)
            : null;

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TransportChrome.Paint(dc, bounds, IsEnabled, IsMouseOver, IsPressed);
        TransportIconDrawing.DrawSettingsGear(dc, bounds, TransportChrome.Fore(IsEnabled), ChromeHole());
    }

    private Color ChromeHole()
    {
        if (IsEnabled && (IsMouseOver || IsPressed))
        {
            return Theme.Get(IsPressed ? "TransportPressedBackBrush" : "TransportHoverBackBrush");
        }

        return Theme.Get("TransportBackBrush");
    }
}
