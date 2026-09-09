using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>Short Term LKFS を波形の下に描く。解析は裏で一度積む。</summary>
internal sealed class LoudnessOverlayRenderer
{
    public const double TopLufs = 0;
    public const double FloorLu = 21;
    public const double BandLu = 6;

    private static readonly double[] MarkOffsets = [6, 0, -3, -12, -21];
    private static readonly Pen SafePen = CreatePen(Colors.Cyan, 1.6);
    private static readonly Pen CautionPen = CreatePen(Color.FromRgb(255, 128, 0), 1.6);
    private static readonly Pen DangerPen = CreatePen(Colors.Red, 2.4);

    private readonly object _gate = new();
    private int _generation;
    private object? _samples;
    private int _sampleRate;
    private int _channels;
    private LoudnessProfile? _profile;

    public LoudnessProfile? Current
    {
        get
        {
            lock (_gate)
            {
                return _profile;
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            _generation++;
            _samples = null;
            _profile = null;
        }
    }

    public void Dispose() => Invalidate();

    public void Ensure(AudioDocument document, Action onUpdated)
    {
        int gen;
        float[] samples;
        int rate;
        int channels;
        lock (_gate)
        {
            if (ReferenceEquals(_samples, document.Interleaved)
                && _sampleRate == document.SampleRate
                && _channels == document.Channels)
            {
                return;
            }

            gen = ++_generation;
            samples = document.Interleaved;
            rate = document.SampleRate;
            channels = document.Channels;
            _samples = samples;
            _sampleRate = rate;
            _channels = channels;
            _profile = null;
        }

        Task.Run(() =>
        {
            var profile = LoudnessMeterEngine.BuildShortTermProfile(samples, channels, rate);
            lock (_gate)
            {
                if (gen != _generation)
                {
                    return;
                }

                _profile = profile;
            }

            onUpdated();
        });
    }

    public void DrawUnderlay(DrawingContext dc, Rect wave, double target)
    {
        if (wave.Width <= 1 || wave.Height <= 1)
        {
            return;
        }

        target = LoudnessMeterEngine.ClampTargetLufs(target);
        dc.PushClip(new RectangleGeometry(wave));
        DrawBands(dc, wave, target);
        DrawGrid(dc, wave, target);
        dc.Pop();
    }

    public void DrawOverlay(
        DrawingContext dc,
        Rect wave,
        LoudnessProfile profile,
        double target,
        double viewStart,
        double viewSpan)
    {
        if (wave.Width <= 1 || wave.Height <= 1 || viewSpan <= 0)
        {
            return;
        }

        target = LoudnessMeterEngine.ClampTargetLufs(target);
        dc.PushClip(new RectangleGeometry(wave));
        DrawLuWidth(dc, wave, profile, target, viewStart, viewSpan);
        DrawCurve(dc, wave, profile, target, viewStart, viewSpan, danger: false);
        DrawTarget(dc, wave, target);
        DrawCurve(dc, wave, profile, target, viewStart, viewSpan, danger: true);
        dc.Pop();
    }

    public void DrawScale(DrawingContext dc, Rect well, Rect wave, double target, double pixelsPerDip)
    {
        if (well.Width <= 8 || wave.Height <= 1)
        {
            return;
        }

        target = LoudnessMeterEngine.ClampTargetLufs(target);
        dc.PushClip(new RectangleGeometry(new Rect(well.X, wave.Y, well.Width, wave.Height)));
        DrawScaleLabels(dc, well, wave, target, pixelsPerDip);
        dc.Pop();
    }

    public static double LufsToY(double lufs, Rect wave, double target)
    {
        var top = TopLufs;
        var bottom = target - FloorLu;
        if (bottom >= top)
        {
            bottom = top - FloorLu;
        }

        var t = (top - lufs) / (top - bottom);
        return wave.Y + Math.Clamp(t, 0, 1) * wave.Height;
    }

    private static void DrawBands(DrawingContext dc, Rect wave, double target)
    {
        var yTop = LufsToY(TopLufs, wave, target);
        var yTarget = LufsToY(target, wave, target);
        var yNear = LufsToY(target - BandLu, wave, target);
        var yFloor = LufsToY(target - FloorLu, wave, target);
        FillBand(dc, wave, yTop, yTarget, BandBrush(LoudnessTraffic.Danger));
        FillBand(dc, wave, yTarget, yNear, BandBrush(LoudnessTraffic.Caution));
        FillBand(dc, wave, yNear, yFloor, BandBrush(LoudnessTraffic.Safe));
    }

    private static void FillBand(DrawingContext dc, Rect wave, double y0, double y1, Brush brush)
    {
        var top = Math.Min(y0, y1);
        var height = Math.Abs(y1 - y0);
        if (height < 0.5)
        {
            return;
        }

        dc.DrawRectangle(brush, null, new Rect(wave.X, top, wave.Width, height));
    }

    private static Brush BandBrush(LoudnessTraffic traffic)
    {
        LoudnessTrafficLight.Rgb(traffic, out var r, out var g, out var b);
        const double shade = 0.28;
        return WpfControlHelpers.FrozenBrush(Color.FromRgb(
            (byte)Math.Round(r * shade),
            (byte)Math.Round(g * shade),
            (byte)Math.Round(b * shade)));
    }

    private static void DrawGrid(DrawingContext dc, Rect wave, double target)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(36, 255, 255, 255)), 1);
        pen.Freeze();
        foreach (var lufs in ScaleMarks(target))
        {
            var y = LufsToY(lufs, wave, target);
            dc.DrawLine(pen, new Point(wave.X, y), new Point(wave.Right, y));
        }
    }

    private static void DrawTarget(DrawingContext dc, Rect wave, double target)
    {
        var y = LufsToY(target, wave, target);
        var pen = new Pen(WpfControlHelpers.FrozenBrush(Color.FromArgb(200, 255, 255, 255)), 1.2);
        pen.Freeze();
        dc.DrawLine(pen, new Point(wave.X, y), new Point(wave.Right, y));
    }

    private static void DrawLuWidth(
        DrawingContext dc,
        Rect wave,
        LoudnessProfile profile,
        double target,
        double viewStart,
        double viewSpan)
    {
        var width = Math.Max(1, (int)Math.Ceiling(wave.Width));
        var lu = LoudnessTrafficLight.LufsApproachLu;
        var fill = WpfControlHelpers.FrozenBrush(Color.FromArgb(56, 255, 255, 255));
        var upper = new List<Point>(width + 1);
        var lower = new List<Point>(width + 1);

        void Flush()
        {
            if (upper.Count < 2)
            {
                upper.Clear();
                lower.Clear();
                return;
            }

            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(upper[0], isFilled: true, isClosed: true);
                for (var i = 1; i < upper.Count; i++)
                {
                    ctx.LineTo(upper[i], isStroked: false, isSmoothJoin: true);
                }

                for (var i = lower.Count - 1; i >= 0; i--)
                {
                    ctx.LineTo(lower[i], isStroked: false, isSmoothJoin: true);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(fill, null, geometry);
            upper.Clear();
            lower.Clear();
        }

        for (var i = 0; i <= width; i++)
        {
            var frame = viewStart + viewSpan * (i / (double)width);
            var lufs = profile.AtFrame(frame);
            if (float.IsInfinity(lufs) || float.IsNaN(lufs))
            {
                Flush();
                continue;
            }

            var x = wave.X + i;
            upper.Add(new Point(x, LufsToY(lufs + lu, wave, target)));
            lower.Add(new Point(x, LufsToY(lufs - lu, wave, target)));
        }

        Flush();
    }

    private static void DrawProfilePath(
        DrawingContext dc,
        Rect wave,
        LoudnessProfile profile,
        double target,
        double viewStart,
        double viewSpan,
        Func<LoudnessTraffic, LoudnessTraffic, Pen?> penAt)
    {
        var width = Math.Max(1, (int)Math.Ceiling(wave.Width));
        Point? prev = null;
        var prevTraffic = LoudnessTraffic.Idle;
        for (var i = 0; i <= width; i++)
        {
            var frame = viewStart + viewSpan * (i / (double)width);
            var lufs = profile.AtFrame(frame);
            if (float.IsInfinity(lufs) || float.IsNaN(lufs))
            {
                prev = null;
                continue;
            }

            var point = new Point(wave.X + i, LufsToY(lufs, wave, target));
            var traffic = LoudnessTrafficLight.ForLufs(lufs, target);
            if (prev is { } from && penAt(traffic, prevTraffic) is { } pen)
            {
                dc.DrawLine(pen, from, point);
            }

            prev = point;
            prevTraffic = traffic;
        }
    }

    private static void DrawCurve(
        DrawingContext dc,
        Rect wave,
        LoudnessProfile profile,
        double target,
        double viewStart,
        double viewSpan,
        bool danger)
    {
        DrawProfilePath(
            dc,
            wave,
            profile,
            target,
            viewStart,
            viewSpan,
            (traffic, prev) =>
            {
                var tone = traffic == LoudnessTraffic.Idle ? prev : traffic;
                var isDanger = tone == LoudnessTraffic.Danger;
                return danger == isDanger ? CurvePen(tone) : null;
            });
    }

    private static Pen CurvePen(LoudnessTraffic traffic) => traffic switch
    {
        LoudnessTraffic.Caution => CautionPen,
        LoudnessTraffic.Danger => DangerPen,
        _ => SafePen,
    };

    private static Pen CreatePen(Color color, double thickness)
    {
        var pen = new Pen(WpfControlHelpers.FrozenBrush(color), thickness);
        pen.StartLineCap = PenLineCap.Round;
        pen.EndLineCap = PenLineCap.Round;
        pen.Freeze();
        return pen;
    }

    private static void DrawScaleLabels(
        DrawingContext dc,
        Rect well,
        Rect wave,
        double target,
        double pixelsPerDip)
    {
        var fore = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        var tick = new Pen(fore, 1);
        tick.Freeze();
        foreach (var lufs in ScaleMarks(target))
        {
            var y = LufsToY(lufs, wave, target);
            if (y < wave.Y - 2 || y > wave.Bottom + 2)
            {
                continue;
            }

            y = Math.Clamp(y, wave.Y + 0.5, wave.Bottom - 0.5);
            var label = lufs.ToString("0", CultureInfo.InvariantCulture);
            var text = new FormattedText(
                label,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                WpfControlHelpers.MonoTypeface,
                9,
                fore,
                pixelsPerDip);
            var ty = Math.Clamp(y - text.Height * 0.5, wave.Y, wave.Bottom - text.Height);
            var tx = well.Right - 7 - text.Width;
            dc.DrawLine(tick, new Point(well.Right - 5, y), new Point(well.Right - 1, y));
            dc.DrawText(text, new Point(Math.Max(well.X + 2, tx), ty));
        }
    }

    private static IEnumerable<double> ScaleMarks(double target)
    {
        yield return TopLufs;
        foreach (var offset in MarkOffsets)
        {
            var lufs = target + offset;
            if (Math.Abs(lufs - TopLufs) < 0.05)
            {
                continue;
            }

            yield return lufs;
        }
    }
}
