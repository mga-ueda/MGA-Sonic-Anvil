using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>選択範囲で波形色と背景（リージョン下塗り含む）を入れ替える。</summary>
internal static class WaveformInvertPaint
{
    public static void RebuildInPlace(
        int[] pixels,
        int width,
        int height,
        AudioDocument? document,
        double viewStart,
        double viewSpan,
        IReadOnlyList<int>? laneWaveColors = null,
        double laneGapPx = 0)
    {
        if (width <= 0 || height <= 0 || pixels.Length < width * height)
        {
            return;
        }

        var waveformBack = ToBgra(Theme.Get("WaveformBackBrush"));
        var fallbackWave = ToBgra(Theme.Get("WaveFillBrush"));
        var count = width * height;
        if (document is null || document.FrameCount <= 0 || viewSpan <= 0)
        {
            for (var i = 0; i < count; i++)
            {
                pixels[i] = IsWavePixel(pixels[i])
                    ? waveformBack
                    : WaveColorAt(i / width, height, laneWaveColors, laneGapPx, fallbackWave);
            }

            return;
        }

        var regionFill = ToBgra(Theme.Get("RegionWaveFillBrush"));
        var sampleLoop = ToBgra(Theme.Get("SampleLoopWaveFillBrush"));
        var anacrusis = ToBgra(Theme.Get("RegionWaveFillAnacrusisBrush"));
        var loop = ToBgra(Theme.Get("RegionWaveFillLoopBrush"));
        var exit = ToBgra(Theme.Get("RegionWaveFillExitBrush"));
        var remove = ToBgra(Theme.Get("RegionWaveFillExcludedBrush"));
        var regions = document.Regions;
        var sample = document.SampleLoop;
        var hasSampleLoop = !sample.IsEmpty;
        var markers = document.Markers;
        var frameCount = document.FrameCount;

        for (var x = 0; x < width; x++)
        {
            var frame = FrameAtColumn(x, width, viewStart, viewSpan, frameCount);
            var columnBack = waveformBack;
            foreach (var region in regions)
            {
                if (region.ContainsFrame(frame))
                {
                    columnBack = BlendOver(columnBack, regionFill);
                    break;
                }
            }

            if (hasSampleLoop && frame >= sample.StartFrame && frame < sample.EndFrame)
            {
                columnBack = BlendOver(columnBack, sampleLoop);
            }

            var underRole = UnderWaveRoleBgra(markers, frame, frameCount, anacrusis, loop, exit);
            if (underRole != 0)
            {
                columnBack = BlendOver(columnBack, underRole);
            }

            var removeOverlay = RemoveOverlayBgra(markers, frame, frameCount, remove);
            var swappedBack = removeOverlay != 0 ? BlendOver(columnBack, removeOverlay) : columnBack;
            for (var y = 0; y < height; y++)
            {
                var index = y * width + x;
                pixels[index] = IsWavePixel(pixels[index])
                    ? swappedBack
                    : SoftenSelectionFill(
                        WaveColorAt(y, height, laneWaveColors, laneGapPx, fallbackWave),
                        waveformBack);
            }
        }
    }

    internal const byte LightFillAlpha = 176;

    internal static int SoftenSelectionFill(int fill, int back) =>
        SoftenSelectionFill(fill, back, UiThemeService.Current == UiTheme.Light);

    internal static int SoftenSelectionFill(int fill, int back, bool light)
    {
        if (!light)
        {
            return fill;
        }

        return BlendOver(back, (fill & 0x00FFFFFF) | (LightFillAlpha << 24));
    }

    private static int WaveColorAt(
        int y,
        int height,
        IReadOnlyList<int>? laneWaveColors,
        double laneGapPx,
        int fallback)
    {
        if (laneWaveColors is null || laneWaveColors.Count == 0)
        {
            return fallback;
        }

        if (laneWaveColors.Count == 1)
        {
            return laneWaveColors[0];
        }

        return laneWaveColors[ChannelWavePaint.LaneAt(y, height, laneWaveColors.Count, laneGapPx)];
    }

    private static bool IsWavePixel(int pixel) => ((pixel >> 24) & 0xFF) > 0;

    private static long FrameAtColumn(int x, int width, double viewStart, double viewSpan, long frameCount)
    {
        var frame = (long)Math.Floor(viewStart + ((x + 0.5) / width) * viewSpan);
        return Math.Clamp(frame, 0, Math.Max(0, frameCount - 1));
    }

    private static int UnderWaveRoleBgra(
        IReadOnlyList<WaveMarker> markers,
        long frame,
        long frameCount,
        int anacrusis,
        int loop,
        int exit)
    {
        for (var i = 0; i < markers.Count; i++)
        {
            var role = MarkerRoles.FromComment(markers[i].Comment);
            if (role is not (MarkerRole.Anacrusis or MarkerRole.Loop or MarkerRole.Exit))
            {
                continue;
            }

            var start = markers[i].Frame;
            var end = i + 1 < markers.Count ? markers[i + 1].Frame : frameCount;
            if (frame < start || frame >= end)
            {
                continue;
            }

            return role switch
            {
                MarkerRole.Anacrusis => anacrusis,
                MarkerRole.Loop => loop,
                MarkerRole.Exit => exit,
                _ => 0,
            };
        }

        return 0;
    }

    private static int RemoveOverlayBgra(
        IReadOnlyList<WaveMarker> markers,
        long frame,
        long frameCount,
        int remove)
    {
        for (var i = 0; i < markers.Count; i++)
        {
            if (MarkerRoles.FromComment(markers[i].Comment) != MarkerRole.Remove)
            {
                continue;
            }

            var start = markers[i].Frame;
            var end = i + 1 < markers.Count ? markers[i + 1].Frame : frameCount;
            if (frame >= start && frame < end)
            {
                return remove;
            }
        }

        return 0;
    }

    private static int BlendOver(int destination, int source)
    {
        var sa = (source >> 24) & 0xFF;
        if (sa == 0)
        {
            return destination;
        }

        if (sa == 255)
        {
            return source;
        }

        var inv = 255 - sa;
        var b = (((source & 0xFF) * sa) + ((destination & 0xFF) * inv)) / 255;
        var g = ((((source >> 8) & 0xFF) * sa) + (((destination >> 8) & 0xFF) * inv)) / 255;
        var r = ((((source >> 16) & 0xFF) * sa) + (((destination >> 16) & 0xFF) * inv)) / 255;
        return b | (g << 8) | (r << 16) | unchecked((int)0xFF000000);
    }

    private static int ToBgra(Color color) =>
        color.B | (color.G << 8) | (color.R << 16) | (color.A << 24);
}
