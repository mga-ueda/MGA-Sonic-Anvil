using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Wwise;

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
        // 画素ごとのレーン判定は width×height 回で高くつくため、行の色を先に引く。
        var waveColorByY = BuildWaveColorRows(height, laneWaveColors, laneGapPx, fallbackWave);
        if (document is null || document.FrameCount <= 0 || viewSpan <= 0)
        {
            for (var y = 0; y < height; y++)
            {
                var waveFill = waveColorByY[y];
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    pixels[row + x] = InvertPixel(pixels[row + x], waveformBack, waveFill);
                }
            }

            return;
        }

        var regionFill = ToBgra(Theme.Get("RegionWaveFillBrush"));
        var sampleLoop = ToBgra(Theme.Get("SampleLoopWaveFillBrush"));
        var anacrusis = ToBgra(Theme.Get("RegionWaveFillAnacrusisBrush"));
        var loop = ToBgra(Theme.Get("RegionWaveFillLoopBrush"));
        var exit = ToBgra(Theme.Get("RegionWaveFillExitBrush"));
        var regions = document.Regions;
        var sample = document.SampleLoop;
        var hasSampleLoop = !sample.IsEmpty;
        var frameCount = document.FrameCount;
        // マーカー役割（コメントの文字列解析）は列×マーカー数で繰り返さず、一度だけ解決する。
        var underSpans = CollectUnderWaveRoleSpans(document, anacrusis, loop, exit);

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

            var underRole = SpanBgraAt(underSpans, frame);
            if (underRole != 0)
            {
                columnBack = BlendOver(columnBack, underRole);
            }

            for (var y = 0; y < height; y++)
            {
                var index = y * width + x;
                pixels[index] = InvertPixel(pixels[index], columnBack, waveColorByY[y]);
            }
        }
    }

    private static int[] BuildWaveColorRows(
        int height,
        IReadOnlyList<int>? laneWaveColors,
        double laneGapPx,
        int fallback)
    {
        var rows = new int[height];
        for (var y = 0; y < height; y++)
        {
            rows[y] = WaveColorAt(y, height, laneWaveColors, laneGapPx, fallback);
        }

        return rows;
    }

    private readonly record struct RoleSpan(long Start, long End, int Bgra);

    private static List<RoleSpan> CollectUnderWaveRoleSpans(
        AudioDocument document,
        int anacrusis,
        int loop,
        int exit)
    {
        var spans = new List<RoleSpan>();
        var markers = document.Markers;
        var frameCount = document.FrameCount;
        for (var i = 0; i < markers.Count; i++)
        {
            var role = MarkerRoles.FromComment(markers[i].Comment);
            if (role is not (MarkerRole.Anacrusis or MarkerRole.Loop or MarkerRole.Exit))
            {
                continue;
            }

            var bgra = role switch
            {
                MarkerRole.Anacrusis => anacrusis,
                MarkerRole.Loop => loop,
                MarkerRole.Exit => exit,
                _ => 0,
            };
            var end = i + 1 < markers.Count ? markers[i + 1].Frame : frameCount;
            spans.Add(new RoleSpan(markers[i].Frame, end, bgra));
        }

        foreach (var range in WaveOnlyPlanBuilder.ImplicitExitRanges(document))
        {
            spans.Add(new RoleSpan(range.StartFrame, range.EndFrame, exit));
        }

        return spans;
    }

    /// <summary>マーカー順で最初に当たったスパンの色。無ければ 0。</summary>
    private static int SpanBgraAt(List<RoleSpan> spans, long frame)
    {
        for (var i = 0; i < spans.Count; i++)
        {
            if (frame >= spans[i].Start && frame < spans[i].End)
            {
                return spans[i].Bgra;
            }
        }

        return 0;
    }

    /// <summary>波形ピクセルは背景色へ、背景ピクセルは波形色へ。白黒反転ではない。</summary>
    internal static int InvertPixel(int pixel, int back, int waveFill) =>
        ((pixel >> 24) & 0xFF) > 0 ? back : waveFill;

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

    private static long FrameAtColumn(int x, int width, double viewStart, double viewSpan, long frameCount)
    {
        var frame = (long)Math.Floor(viewStart + ((x + 0.5) / width) * viewSpan);
        return Math.Clamp(frame, 0, Math.Max(0, frameCount - 1));
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
