using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

internal sealed class LevelMeterView : FrameworkElement
{
    private const double ScaleColWidth = 22;
    private const double BarWidth = 28;
    private const double LampHeight = 8;
    private const double ReadoutHeight = 16;
    private const double LabelHeight = 12;
    private const double ChromePad = 6;

    private static readonly Color SectionBack = Color.FromRgb(0x24, 0x26, 0x29);
    private static readonly Color SectionBorder = Color.FromRgb(0x4A, 0x4D, 0x52);
    private static readonly Color TrackBack = Color.FromRgb(0x24, 0x26, 0x29);
    private static readonly Color TrackBorder = Color.FromRgb(0x1A, 0x22, 0x1A);
    private static readonly Color Tick = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
    private static readonly Color ClipOff = Color.FromRgb(0x28, 0x08, 0x08);
    private static readonly Color ClipOffBorder = Color.FromRgb(0x3A, 0x15, 0x15);
    private static readonly Color ClipOn = Color.FromRgb(0xFF, 0x00, 0x00);
    private static readonly Color LabelFore = Colors.White;

    private LevelMeterSnapshot _snapshot = LevelMeterSnapshot.Idle;
    private LinearGradientBrush? _barGradient;
    private double _gradientTrackHeight;

    public LevelMeterView()
    {
        Width = DesignMetrics.LevelMeterWidth;
        MinWidth = DesignMetrics.LevelMeterWidth;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        UseLayoutRounding = true;
    }

    public void Apply(LevelMeterSnapshot snapshot)
    {
        _snapshot = snapshot;
        InvalidateVisual();
    }

    public void Extinguish()
    {
        _snapshot = LevelMeterSnapshot.Idle;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(RenderSize);
        if (bounds.Width <= 1 || bounds.Height <= 1)
        {
            return;
        }

        dc.DrawRoundedRectangle(
            WpfControlHelpers.FrozenBrush(SectionBack),
            new Pen(WpfControlHelpers.FrozenBrush(SectionBorder), 1),
            bounds,
            4,
            4);

        var inner = new Rect(
            bounds.X + ChromePad,
            bounds.Y + ChromePad,
            Math.Max(1, bounds.Width - ChromePad * 2),
            Math.Max(1, bounds.Height - ChromePad * 2));
        var trackTop = inner.Y + LampHeight;
        var trackBottom = inner.Bottom - ReadoutHeight - LabelHeight;
        var trackHeight = Math.Max(24, trackBottom - trackTop);
        var track = new Rect(inner.X + ScaleColWidth, trackTop, BarWidth * 4, trackHeight);
        EnsureGradient(trackHeight);

        DrawScale(dc, new Rect(inner.X, trackTop, ScaleColWidth, trackHeight), rightAlign: true);
        DrawUnit(dc, new Rect(track.X, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: false, _snapshot.Left, clip: false, "RMS");
        DrawUnit(dc, new Rect(track.X + BarWidth, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: true, _snapshot.Left, _snapshot.ClipLeft, "L");
        DrawUnit(dc, new Rect(track.X + BarWidth * 2, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: true, _snapshot.Right, _snapshot.ClipRight, "R");
        DrawUnit(dc, new Rect(track.X + BarWidth * 3, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: false, _snapshot.Right, clip: false, "RMS");
        DrawScale(dc, new Rect(track.Right, trackTop, ScaleColWidth, trackHeight), rightAlign: false);
    }

    private void DrawUnit(
        DrawingContext dc,
        Rect unit,
        double trackHeight,
        bool isPeak,
        ChannelMeter meter,
        bool clip,
        string label)
    {
        var lamp = new Rect(unit.X + 1, unit.Y, unit.Width - 2, LampHeight - 1);
        if (isPeak)
        {
            var fill = clip ? ClipOn : ClipOff;
            var border = clip ? ClipOn : ClipOffBorder;
            dc.DrawRectangle(WpfControlHelpers.FrozenBrush(fill), new Pen(WpfControlHelpers.FrozenBrush(border), 1), lamp);
            if (clip)
            {
                dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Color.FromArgb(80, 255, 0, 0)), null, lamp);
            }
        }

        var track = new Rect(unit.X, unit.Y + LampHeight, unit.Width, trackHeight);
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(TrackBack),
            new Pen(WpfControlHelpers.FrozenBrush(TrackBorder), 1),
            track);

        DrawTicks(dc, track);

        var pct = isPeak ? meter.PeakPct : meter.RmsPct;
        var barH = track.Height * Math.Clamp(pct, 0, 100) / 100d;
        if (barH > 0.5 && _barGradient is not null)
        {
            var bar = new Rect(track.X + 1, track.Bottom - barH, Math.Max(1, track.Width - 2), barH);
            dc.PushClip(new RectangleGeometry(bar));
            dc.PushOpacity(0.68);
            dc.DrawRectangle(_barGradient, null, track);
            dc.Pop();
            dc.Pop();
        }

        var showHold = isPeak ? meter.ShowPeakHold : meter.ShowRmsHold;
        if (showHold)
        {
            var holdPct = isPeak ? meter.PeakHoldPct : meter.RmsHoldPct;
            var holdDb = isPeak ? meter.PeakHeldDb : meter.RmsHoldLineDb;
            var holdH = Math.Max(1, track.Height / Math.Abs(LevelMeterEngine.DbMax - LevelMeterEngine.DbMin));
            var holdBottom = track.Height * Math.Clamp(holdPct, 0, 100) / 100d;
            var y = track.Bottom - holdBottom - holdH;
            var color = LevelMeterEngine.LevelColor(holdDb);
            var holdRect = new Rect(track.X + 1, y, Math.Max(1, track.Width - 2), holdH);
            dc.DrawRectangle(
                WpfControlHelpers.FrozenBrush(Color.FromRgb(color.R, color.G, color.B)),
                new Pen(Brushes.Black, 1),
                holdRect);
        }

        var readoutDb = isPeak ? meter.PeakHeldDb : meter.RmsHeldDb;
        var readout = LevelMeterEngine.FormatReadout(readoutDb);
        var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dbText = new FormattedText(
            readout,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            8,
            WpfControlHelpers.FrozenBrush(LabelFore),
            pixels);
        dc.DrawText(dbText, new Point(unit.X + (unit.Width - dbText.Width) * 0.5, track.Bottom + 1));

        var labelText = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            8,
            WpfControlHelpers.FrozenBrush(LabelFore),
            pixels);
        dc.DrawText(labelText, new Point(unit.X + (unit.Width - labelText.Width) * 0.5, track.Bottom + ReadoutHeight));
    }

    private void DrawScale(DrawingContext dc, Rect col, bool rightAlign)
    {
        var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var db in LevelMeterEngine.ScaleLabels)
        {
            var y = col.Y + (1 - LevelMeterEngine.DbToNorm(db)) * col.Height;
            var text = db == 0 ? "0" : db.ToString(CultureInfo.InvariantCulture);
            var formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.MonoTypeface,
                8,
                WpfControlHelpers.FrozenBrush(LabelFore),
                pixels);
            var x = rightAlign ? col.Right - formatted.Width - 1 : col.X + 1;
            var ty = db <= LevelMeterEngine.DbMin
                ? col.Bottom - formatted.Height
                : db >= LevelMeterEngine.DbMax
                    ? col.Y
                    : y - formatted.Height * 0.5 + 1;
            dc.DrawText(formatted, new Point(x, ty));
        }
    }

    private static void DrawTicks(DrawingContext dc, Rect track)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Tick), 1);
        pen.Freeze();
        foreach (var db in LevelMeterEngine.ScaleLabels)
        {
            var y = track.Y + (1 - LevelMeterEngine.DbToNorm(db)) * track.Height;
            dc.DrawLine(pen, new Point(track.X + 1, y), new Point(track.Right - 1, y));
        }
    }

    private void EnsureGradient(double trackHeight)
    {
        if (_barGradient is not null && Math.Abs(_gradientTrackHeight - trackHeight) < 0.5)
        {
            return;
        }

        _gradientTrackHeight = trackHeight;
        _barGradient = new LinearGradientBrush
        {
            StartPoint = new Point(0, 1),
            EndPoint = new Point(0, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            [
                new GradientStop(Color.FromRgb(0x02, 0x18, 0x20), 0),
                new GradientStop(Color.FromRgb(0x0D, 0x4A, 0x62), 0.26),
                new GradientStop(Color.FromRgb(0x3A, 0xB8, 0xE8), 0.55),
                new GradientStop(Color.FromRgb(0xC8, 0xEF, 0xFF), 0.82),
                new GradientStop(Color.FromRgb(0xF8, 0xFE, 0xFF), 1),
            ],
        };
        _barGradient.Freeze();
    }
}
