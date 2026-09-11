using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>設定の録音ポート行用。メインのレベルメーターと同じグラデ。</summary>
internal sealed class ChannelLevelBar : FrameworkElement
{
    private float _display;

    public int Channel { get; set; }

    public ChannelLevelBar()
    {
        Width = DesignMetrics.SettingsLevelBarWidth;
        MinWidth = DesignMetrics.SettingsLevelBarWidth;
        Height = DesignMetrics.AudioInputHeight - 8;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public void ApplyLinearPeak(float peak)
    {
        if (peak > _display)
        {
            _display = peak;
        }
        else
        {
            _display *= 0.82f;
            if (_display < 1e-4f)
            {
                _display = 0;
            }
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("WaveformBackBrush")), null, bounds);
        dc.DrawRectangle(
            null,
            new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")), 1),
            bounds);

        var db = LevelMeterEngine.ToDb(_display);
        var fillWidth = Math.Max(0, (bounds.Width - 2) * LevelMeterEngine.DbToNorm(db));
        if (fillWidth < 0.5)
        {
            return;
        }

        var track = new Rect(1, 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
        var bar = new Rect(track.X, track.Y, fillWidth, track.Height);
        dc.PushClip(new RectangleGeometry(bar));
        dc.PushOpacity(LevelMeterEngine.BarFillOpacity);
        dc.DrawRectangle(LevelMeterBarPaint.Create(vertical: false, Channel), null, track);
        dc.Pop();
        dc.Pop();
    }
}
