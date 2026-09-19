using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>設定の録音ポート行用。メインのレベルメーターと同じグラデ。</summary>
internal sealed class ChannelLevelBar : FrameworkElement
{
    private static Color FloorLine => Theme.Get("LevelMeterClipOnBrush");
    private const float FloorMix = 0.22f;
    private const double FloorLineWidth = 2;

    private float _display;
    private float _floor;
    private bool _showFloor;

    public int Channel { get; set; }

    /// <summary>スピーカー本数。2 以下はシアングラデ、3 以上はチャンネル色。</summary>
    public int Channels { get; set; } = 2;

    public double DisplayFloorDb => LevelMeterEngine.ToDb(_floor);

    public ChannelLevelBar()
    {
        Width = DesignMetrics.SettingsLevelBarWidth;
        MinWidth = DesignMetrics.SettingsLevelBarWidth;
        Height = DesignMetrics.AudioInputHeight - 8;
        VerticalAlignment = VerticalAlignment.Center;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public void Reset()
    {
        _display = 0;
        _floor = 0;
        _showFloor = false;
        InvalidateVisual();
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

    public void ApplyFloor(float minAbs)
    {
        _showFloor = true;
        if (_floor <= 0)
        {
            _floor = minAbs;
        }
        else
        {
            _floor += (minAbs - _floor) * FloorMix;
        }

        if (_floor < 1e-6f)
        {
            _floor = 0;
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

        var track = new Rect(1, 1, Math.Max(1, bounds.Width - 2), Math.Max(1, bounds.Height - 2));
        var db = LevelMeterEngine.ToDb(_display);
        var fillWidth = Math.Max(0, track.Width * LevelMeterEngine.DbToNorm(db));
        if (fillWidth >= 0.5)
        {
            var bar = new Rect(track.X, track.Y, fillWidth, track.Height);
            dc.PushClip(new RectangleGeometry(bar));
            dc.PushOpacity(LevelMeterEngine.BarFillOpacity);
            dc.DrawRectangle(LevelMeterBarPaint.Create(vertical: false, Channel, Channels), null, track);
            dc.Pop();
            dc.Pop();
        }

        if (!_showFloor)
        {
            return;
        }

        var floorNorm = LevelMeterEngine.DbToNorm(DisplayFloorDb);
        var x = track.X + (track.Width * floorNorm) - (FloorLineWidth * 0.5);
        x = Math.Clamp(x, track.X, track.Right - FloorLineWidth);
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(FloorLine),
            null,
            new Rect(x, track.Y, FloorLineWidth, track.Height));
    }
}
