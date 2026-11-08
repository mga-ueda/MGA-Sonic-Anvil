using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

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
        Loaded += (_, _) => ApplyTrackBrush();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        ApplyTrackBrush();
    }

    internal void ApplyTrackBrush()
    {
        if (Template?.FindName("TrackBack", this) is Border track)
        {
            track.Background = CreateTrackBrush();
        }
    }

    /// <summary>スペクトログラム LUT と同じ色調。設定の青系／赤系に追従する。</summary>
    internal static Brush CreateTrackBrush()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 1),
            EndPoint = new Point(0.5, 0),
        };
        brush.GradientStops.Add(new GradientStop(TrackFloor, 0));
        brush.GradientStops.Add(new GradientStop(TrackMid, 0.5));
        brush.GradientStops.Add(new GradientStop(TrackCeil, 1));
        brush.Freeze();
        return brush;
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

    internal static Color TrackFloor => ColorFromBgra(SpectrogramEngine.ColorFromLutByte(0));

    internal static Color TrackMid => ColorFromBgra(SpectrogramEngine.ColorFromLutByte(128));

    internal static Color TrackCeil => ColorFromBgra(SpectrogramEngine.ColorFromLutByte(255));

    internal static Color TrackWhite => TrackCeil;

    private static Color ColorFromBgra(int bgra) =>
        Color.FromArgb(
            (byte)((bgra >> 24) & 0xFF),
            (byte)((bgra >> 16) & 0xFF),
            (byte)((bgra >> 8) & 0xFF),
            (byte)(bgra & 0xFF));
}
