using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;

namespace MgaSonicAnvil.UI;

/// <summary>2ch 以下はシアングラデ。3ch 以上は <see cref="ChannelColors"/>。</summary>
internal static class LevelMeterBarPaint
{
    private static readonly LinearGradientBrush?[] Vertical = new LinearGradientBrush[ChannelColors.Count];
    private static readonly LinearGradientBrush?[] Horizontal = new LinearGradientBrush[ChannelColors.Count];
    private static LinearGradientBrush? _cyanVertical;
    private static LinearGradientBrush? _cyanHorizontal;

    public static void Invalidate()
    {
        _cyanVertical = null;
        _cyanHorizontal = null;
        Array.Clear(Vertical);
        Array.Clear(Horizontal);
    }

    public static LinearGradientBrush Create(bool vertical, int channel = 0, int channels = ChannelLayout.MaxChannels)
    {
        if (!ChannelColors.UsesLaneTint(channels))
        {
            return vertical
                ? _cyanVertical ??= BuildCyan(vertical: true)
                : _cyanHorizontal ??= BuildCyan(vertical: false);
        }

        var index = ChannelColors.Index(channel);
        var cache = vertical ? Vertical : Horizontal;
        return cache[index] ??= Build(vertical, ChannelSwatch.Of(index));
    }

    /// <summary><see cref="LevelColorTheme"/> と同じ停止点（色設定の LevelGrad*）。</summary>
    private static LinearGradientBrush BuildCyan(bool vertical)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new Point(0, 1) : new Point(0, 0),
            EndPoint = vertical ? new Point(0, 0) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            [
                new GradientStop(LevelColorTheme.StopBrush(0), LevelColorTheme.DefaultStops[0].P),
                new GradientStop(LevelColorTheme.StopBrush(1), LevelColorTheme.DefaultStops[1].P),
                new GradientStop(LevelColorTheme.StopBrush(2), LevelColorTheme.DefaultStops[2].P),
                new GradientStop(LevelColorTheme.StopBrush(3), LevelColorTheme.DefaultStops[3].P),
                new GradientStop(LevelColorTheme.StopBrush(4), LevelColorTheme.DefaultStops[4].P),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Build(bool vertical, Color mid)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new Point(0, 1) : new Point(0, 0),
            EndPoint = vertical ? new Point(0, 0) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            [
                new GradientStop(Mix(mid, 0.58), 0),
                new GradientStop(Mix(mid, 0.78), 0.26),
                new GradientStop(mid, 0.55),
                new GradientStop(Blend(mid, Colors.White, 0.72), 0.82),
                new GradientStop(Blend(mid, Colors.White, 0.94), 1),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static Color Mix(Color color, double amount) =>
        Color.FromRgb(Scale(color.R, amount), Scale(color.G, amount), Scale(color.B, amount));

    private static Color Blend(Color a, Color b, double t) =>
        Color.FromRgb(
            (byte)Math.Clamp(a.R + (b.R - a.R) * t, 0, 255),
            (byte)Math.Clamp(a.G + (b.G - a.G) * t, 0, 255),
            (byte)Math.Clamp(a.B + (b.B - a.B) * t, 0, 255));

    private static byte Scale(byte value, double amount) =>
        (byte)Math.Clamp(value * amount, 0, 255);
}
