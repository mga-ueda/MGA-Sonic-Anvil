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

    /// <summary><see cref="LevelMeterEngine.LevelColor"/> と同じ停止点。</summary>
    private static LinearGradientBrush BuildCyan(bool vertical)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = vertical ? new Point(0, 1) : new Point(0, 0),
            EndPoint = vertical ? new Point(0, 0) : new Point(1, 0),
            MappingMode = BrushMappingMode.RelativeToBoundingBox,
            GradientStops =
            [
                new GradientStop(Color.FromRgb(16, 62, 86), 0),
                new GradientStop(Color.FromRgb(20, 90, 118), 0.26),
                new GradientStop(Color.FromRgb(58, 184, 232), 0.55),
                new GradientStop(Color.FromRgb(200, 239, 255), 0.82),
                new GradientStop(Color.FromRgb(248, 254, 255), 1),
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
