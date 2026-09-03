using System.Windows;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>IM Importer と同じリージョン下塗り（-A/-L/-E）と -R の重ね。</summary>
internal static class MarkerRolePaint
{
    public static void DrawBackgrounds(
        DrawingContext dc,
        AudioDocument document,
        Rect wave,
        double viewStart,
        double viewSpan)
    {
        Draw(dc, document, wave, viewStart, viewSpan, overlayRemove: false);
    }

    public static void DrawRemoveOverlays(
        DrawingContext dc,
        AudioDocument document,
        Rect wave,
        double viewStart,
        double viewSpan)
    {
        Draw(dc, document, wave, viewStart, viewSpan, overlayRemove: true);
    }

    private static void Draw(
        DrawingContext dc,
        AudioDocument document,
        Rect wave,
        double viewStart,
        double viewSpan,
        bool overlayRemove)
    {
        var markers = document.Markers;
        if (markers.Count == 0 || wave.Width <= 1 || wave.Height <= 1 || viewSpan <= 0)
        {
            return;
        }

        var frames = document.FrameCount;
        for (var i = 0; i < markers.Count; i++)
        {
            var role = MarkerRoles.FromComment(markers[i].Comment);
            if (role == MarkerRole.None)
            {
                continue;
            }

            var isRemove = role == MarkerRole.Remove;
            if (isRemove != overlayRemove)
            {
                continue;
            }

            var start = markers[i].Frame;
            var end = i + 1 < markers.Count ? markers[i + 1].Frame : frames;
            if (end <= start)
            {
                continue;
            }

            var x0 = wave.X + ((start - viewStart) / viewSpan) * wave.Width;
            var x1 = wave.X + ((end - viewStart) / viewSpan) * wave.Width;
            if (x1 < wave.X || x0 > wave.Right)
            {
                continue;
            }

            x0 = Math.Clamp(x0, wave.X, wave.Right);
            x1 = Math.Clamp(x1, wave.X, wave.Right);
            var width = Math.Max(1, x1 - x0);
            dc.DrawRectangle(BrushOf(role), null, new Rect(x0, wave.Y, width, wave.Height));
        }
    }

    public static void DrawSampleLoop(
        DrawingContext dc,
        AudioDocument document,
        Rect lane,
        double viewStart,
        double viewSpan,
        string brushKey)
    {
        DrawRange(dc, document.SampleLoop, lane, viewStart, viewSpan, brushKey);
    }

    public static void DrawRange(
        DrawingContext dc,
        WaveSelection range,
        Rect lane,
        double viewStart,
        double viewSpan,
        string brushKey)
    {
        if (range.IsEmpty || lane.Width <= 1 || lane.Height <= 1 || viewSpan <= 0)
        {
            return;
        }

        var x0 = lane.X + ((range.StartFrame - viewStart) / viewSpan) * lane.Width;
        var x1 = lane.X + ((range.EndFrame - viewStart) / viewSpan) * lane.Width;
        if (x1 < lane.X || x0 > lane.Right)
        {
            return;
        }

        x0 = Math.Clamp(x0, lane.X, lane.Right);
        x1 = Math.Clamp(x1, lane.X, lane.Right);
        dc.DrawRectangle(
            WpfControlHelpers.FrozenBrush(Theme.Get(brushKey)),
            null,
            new Rect(x0, lane.Y, Math.Max(1, x1 - x0), lane.Height));
    }

    private static Brush BrushOf(MarkerRole role) =>
        WpfControlHelpers.FrozenBrush(Theme.Get(role switch
        {
            MarkerRole.Anacrusis => "RegionWaveFillAnacrusisBrush",
            MarkerRole.Loop => "RegionWaveFillLoopBrush",
            MarkerRole.Exit => "RegionWaveFillExitBrush",
            MarkerRole.Remove => "RegionWaveFillExcludedBrush",
            _ => "WaveformBackBrush",
        }));
}
