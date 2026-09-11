using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal sealed class LevelMeterView : FrameworkElement
{
    private const double ScaleColWidth = LevelMeterSurroundLayout.ScaleColWidth;
    private const double BarWidth = LevelMeterSurroundLayout.MaxBarWidth;
    private const double LampHeight = 8;
    private const double ReadoutLineHeight = 12;
    private const double ReadoutHeight = ReadoutLineHeight * 2;
    private const double HoldLineHeight = 3;

    private static readonly Color TrackBack = Color.FromRgb(0x24, 0x26, 0x29);
    private static readonly Color TrackBorder = Color.FromRgb(0x1A, 0x22, 0x1A);
    private static readonly Color Tick = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF);
    private static readonly Color ClipOff = Color.FromRgb(0x28, 0x08, 0x08);
    private static readonly Color ClipOffBorder = Color.FromRgb(0x3A, 0x15, 0x15);
    private static readonly Color ClipOn = Color.FromRgb(0xFF, 0x00, 0x00);

    private LevelMeterSnapshot _snapshot = LevelMeterSnapshot.Idle;

    public LevelMeterView()
    {
        MinWidth = DesignMetrics.LevelMeterWidth;
        HorizontalAlignment = HorizontalAlignment.Stretch;
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

        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(Theme.Get("TransportBackBrush")), null, bounds);
        if (!_snapshot.ShowRms && _snapshot.Channels.Length > 2)
        {
            DrawSurround(dc, bounds);
            return;
        }

        var inner = bounds;
        var trackTop = inner.Y + LampHeight;
        var trackBottom = inner.Bottom - ReadoutHeight;
        var trackHeight = Math.Max(24, trackBottom - trackTop);
        var barsWidth = BarWidth * 4;
        var barsLeft = inner.X + Math.Max(0, (inner.Width - barsWidth) * 0.5);
        var track = new Rect(barsLeft, trackTop, barsWidth, trackHeight);

        DrawScale(dc, new Rect(track.X - ScaleColWidth, trackTop, ScaleColWidth, trackHeight), rightAlign: true);
        DrawUnit(dc, new Rect(track.X, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: false, _snapshot.Left, clip: false, channel: 0);
        DrawUnit(dc, new Rect(track.X + BarWidth, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: true, _snapshot.Left, _snapshot.ClipLeft, channel: 0);
        DrawUnit(dc, new Rect(track.X + BarWidth * 2, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: true, _snapshot.Right, _snapshot.ClipRight, channel: 1);
        DrawUnit(dc, new Rect(track.X + BarWidth * 3, inner.Y, BarWidth, inner.Height), trackHeight, isPeak: false, _snapshot.Right, clip: false, channel: 1);
        DrawScale(dc, new Rect(track.Right, trackTop, ScaleColWidth, trackHeight), rightAlign: false);
        DrawReadouts(dc, new Rect(inner.X, track.Bottom + 1, inner.Width, ReadoutHeight));
    }

    private void DrawSurround(DrawingContext dc, Rect bounds)
    {
        var channels = Math.Max(1, _snapshot.Channels.Length);
        var block = Math.Max(
            LevelMeterSurroundLayout.BarsBlockWidth,
            bounds.Width - (ScaleColWidth * 2));
        var barW = LevelMeterSurroundLayout.BarWidth(channels, block);
        var barsLeft = LevelMeterSurroundLayout.BarsLeft(bounds.X, bounds.Width, barW, channels);
        var barsRight = barsLeft + (barW * channels);
        var trackTop = bounds.Y + LampHeight;
        var trackHeight = Math.Max(24, bounds.Bottom - trackTop);
        DrawScale(dc, new Rect(barsLeft - ScaleColWidth, trackTop, ScaleColWidth, trackHeight), rightAlign: true);
        DrawScale(dc, new Rect(barsRight, trackTop, ScaleColWidth, trackHeight), rightAlign: false);
        for (var i = 0; i < channels; i++)
        {
            var clip = i < _snapshot.Clips.Length && _snapshot.Clips[i];
            DrawUnit(
                dc,
                new Rect(barsLeft + i * barW, bounds.Y, barW, LampHeight + trackHeight),
                trackHeight,
                isPeak: true,
                _snapshot.Channels[i],
                clip,
                channel: i,
                insetStroke: true,
                includeRmsHold: true);
        }
    }

    private void DrawUnit(
        DrawingContext dc,
        Rect unit,
        double trackHeight,
        bool isPeak,
        ChannelMeter meter,
        bool clip,
        int channel,
        bool insetStroke = false,
        bool includeRmsHold = false)
    {
        var pad = insetStroke ? 0.5 : 0;
        unit = new Rect(unit.X + pad, unit.Y, Math.Max(1, unit.Width - pad * 2), unit.Height);
        var lamp = new Rect(unit.X + 1, unit.Y, Math.Max(1, unit.Width - 2), LampHeight - 1);
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
        if (barH > 0.5)
        {
            var bar = new Rect(track.X + 1, track.Bottom - barH, Math.Max(1, track.Width - 2), barH);
            dc.PushClip(new RectangleGeometry(bar));
            dc.PushOpacity(LevelMeterEngine.BarFillOpacity);
            dc.DrawRectangle(LevelMeterBarPaint.Create(vertical: true, channel, _snapshot.Channels.Length), null, track);
            dc.Pop();
            dc.Pop();
        }

        if (isPeak && meter.ShowPeakHold)
        {
            DrawHoldLine(dc, track, meter.PeakHoldPct, meter.PeakHeldDb, channel);
        }

        if ((!isPeak || includeRmsHold) && meter.ShowRmsHold)
        {
            DrawHoldLine(dc, track, meter.RmsHoldPct, meter.RmsHoldLineDb, channel);
        }
    }

    private void DrawHoldLine(DrawingContext dc, Rect track, double holdPct, double holdDb, int channel)
    {
        var holdBottom = track.Height * Math.Clamp(holdPct, 0, 100) / 100d;
        var y = track.Bottom - holdBottom - HoldLineHeight;
        Brush fill;
        if (ChannelColors.UsesLaneTint(_snapshot.Channels.Length))
        {
            fill = ChannelSwatch.Brush(channel);
        }
        else
        {
            var rgb = LevelMeterEngine.LevelColor(holdDb);
            fill = WpfControlHelpers.FrozenBrush(Color.FromRgb(rgb.R, rgb.G, rgb.B));
        }

        dc.DrawRectangle(
            fill,
            new Pen(Brushes.Black, 1),
            new Rect(track.X + 1, y, Math.Max(1, track.Width - 2), HoldLineHeight));
    }

    private void DrawReadouts(DrawingContext dc, Rect area)
    {
        var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var brush = LabelBrush();
        var labelWidth = Math.Max(MeasureReadout(UiStrings.LabelPeak, pixels).Width, MeasureReadout(UiStrings.LabelRms, pixels).Width);
        var intSlot = MeasureReadout("-60", pixels).Width;
        var fracSlot = MeasureReadout(".0", pixels).Width;
        var numWidth = intSlot + fracSlot;
        const double gap = 3;
        var rowWidth = labelWidth + gap + numWidth + gap + numWidth;
        var left = area.X + Math.Max(0, (area.Width - rowWidth) * 0.5);
        var leftDotX = left + labelWidth + gap + intSlot;
        var rightDotX = leftDotX + fracSlot + gap + intSlot;

        DrawReadoutLabel(dc, UiStrings.LabelPeak, left, area.Y, pixels, brush);
        DrawReadoutAligned(dc, LevelMeterEngine.FormatReadout(_snapshot.Left.PeakHeldDb), leftDotX, area.Y, pixels, brush);
        DrawReadoutAligned(dc, LevelMeterEngine.FormatReadout(_snapshot.Right.PeakHeldDb), rightDotX, area.Y, pixels, brush);

        var y2 = area.Y + ReadoutLineHeight;
        DrawReadoutLabel(dc, UiStrings.LabelRms, left, y2, pixels, brush);
        DrawReadoutAligned(dc, LevelMeterEngine.FormatReadout(_snapshot.Left.RmsHeldDb), leftDotX, y2, pixels, brush);
        DrawReadoutAligned(dc, LevelMeterEngine.FormatReadout(_snapshot.Right.RmsHeldDb), rightDotX, y2, pixels, brush);
    }

    private static void DrawReadoutLabel(
        DrawingContext dc,
        string text,
        double x,
        double y,
        double pixels,
        Brush brush)
    {
        dc.DrawText(MeasureReadout(text, pixels, brush), new Point(x, y));
    }

    private static void DrawReadoutAligned(
        DrawingContext dc,
        string text,
        double dotX,
        double y,
        double pixels,
        Brush brush)
    {
        var dot = text.IndexOf('.');
        if (dot < 0)
        {
            var whole = MeasureReadout(text, pixels, brush);
            dc.DrawText(whole, new Point(dotX - whole.Width, y));
            return;
        }

        var integer = MeasureReadout(text[..dot], pixels, brush);
        var fraction = MeasureReadout(text[dot..], pixels, brush);
        dc.DrawText(integer, new Point(dotX - integer.Width, y));
        dc.DrawText(fraction, new Point(dotX, y));
    }

    private static FormattedText MeasureReadout(string text, double pixels, Brush? brush = null, double font = 8) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            WpfControlHelpers.MonoTypeface,
            font,
            brush ?? LabelBrush(),
            pixels);

    private void DrawScale(DrawingContext dc, Rect col, bool rightAlign)
    {
        var pixels = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var lastBottom = double.NegativeInfinity;
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
                LabelBrush(),
                pixels);
            var x = rightAlign ? col.Right - formatted.Width - 1 : col.X + 1;
            var ty = db <= LevelMeterEngine.DbMin
                ? col.Bottom - formatted.Height
                : db >= LevelMeterEngine.DbMax
                    ? col.Y
                    : y - formatted.Height * 0.5 + 1;
            if (db is not (0 or -60) && ty < lastBottom + formatted.Height * 0.15)
            {
                continue;
            }

            dc.DrawText(formatted, new Point(x, ty));
            lastBottom = ty + formatted.Height;
        }
    }

    private static Brush LabelBrush() =>
        WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));

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
}
