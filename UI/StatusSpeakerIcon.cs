using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>ステータスバーのスピーカーコンボ左。旧 SPEAKER 文字の代わり。</summary>
internal sealed class StatusSpeakerIcon : FrameworkElement
{
    public StatusSpeakerIcon()
    {
        Width = DesignMetrics.StatusSpeakerIconSide;
        Height = DesignMetrics.StatusSpeakerIconSide;
        Margin = new Thickness(0, 0, 3, 0);
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
        AutomationProperties.SetName(this, UiStrings.LabelSpeaker);
        UiThemeService.Changed += OnThemeChanged;
        Unloaded += (_, _) => UiThemeService.Changed -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    public void ApplyLocalizedText() =>
        AutomationProperties.SetName(this, UiStrings.LabelSpeaker);

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        TransportIconDrawing.Draw(
            dc,
            TransportIcon.StudioSpeaker,
            bounds,
            Theme.Get("PrimaryForeBrush"),
            isPlaying: false,
            UiThemeService.Current,
            accent: true);
    }
}
