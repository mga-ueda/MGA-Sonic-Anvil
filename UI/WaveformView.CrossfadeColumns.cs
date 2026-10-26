using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal sealed partial class WaveformView
{
    private void PatchColumnsWithCrossfadeWavePreview(
        AudioDocument document,
        long startFrame,
        long endFrame,
        int count,
        int channels)
    {
        var draft = _crossfadeDraft;
        if (draft is not { IsActive: true } || count <= 0 || channels < 1)
        {
            return;
        }

        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return;
        }

        static bool Overlaps(long a0, long a1, long b0, long b1) => a1 > b0 && a0 < b1;

        var pre0 = draft.PreDrawStartFrame;
        var pre1 = draft.EndFrame;
        var post0 = draft.PostDrawRange.StartFrame;
        var post1 = draft.PostDrawRange.EndFrame;
        var hasPre = draft.PreCrossfadeFrames > 0;
        var hasPost = draft.PostCrossfadeFrames > 0;
        if ((!hasPre || !Overlaps(startFrame, endFrame, pre0, pre1))
            && (!hasPost || !Overlaps(startFrame, endFrame, post0, post1)))
        {
            return;
        }

        // ピクセル列インデックスをフェード帯に厳密クリップ（全列走査しない）。
        var colLo = count;
        var colHi = -1;
        void AccrueColumns(long b0, long b1)
        {
            if (!Overlaps(startFrame, endFrame, b0, b1))
            {
                return;
            }

            var i0 = (int)Math.Clamp(
                (Math.Max(b0, startFrame) - startFrame) * count / rangeFrames,
                0,
                count - 1);
            var i1 = (int)Math.Clamp(
                (Math.Min(b1, endFrame) - startFrame) * count / rangeFrames,
                0,
                count);
            if (i1 <= i0)
            {
                i1 = Math.Min(count, i0 + 1);
            }

            colLo = Math.Min(colLo, i0);
            colHi = Math.Max(colHi, i1);
        }

        if (hasPre)
        {
            AccrueColumns(pre0, pre1);
        }

        if (hasPost)
        {
            AccrueColumns(post0, post1);
        }

        if (colHi <= colLo)
        {
            return;
        }

        var samples = document.Interleaved;
        var frameCount = document.FrameCount;
        var fade = LoopFadePreview.From(draft);
        var peaks = document.Peaks;
        var usePeaks = !peaks.IsEmpty && peaks.Channels == channels;
        if (usePeaks)
        {
            EnsureCrossfadePatchScratch(channels);
        }

        for (var i = colLo; i < colHi; i++)
        {
            var f0 = startFrame + i * rangeFrames / count;
            var f1 = startFrame + (i + 1) * rangeFrames / count;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            // 列フレームをプリ／ポスト帯との積集合にクリップ。
            if (!TryClipFramesToCrossfadeBands(
                    f0,
                    f1,
                    hasPre,
                    pre0,
                    pre1,
                    hasPost,
                    post0,
                    post1,
                    out f0,
                    out f1))
            {
                continue;
            }

            f0 = Math.Clamp(f0, 0, frameCount);
            f1 = Math.Clamp(f1, f0, frameCount);
            if (f1 <= f0)
            {
                continue;
            }

            // 列中央で fade／preview gain を 1 回だけ先計算。
            var mid = f0 + (f1 - f0) / 2;
            var fadeGain = CrossfadeBaseFadeGainAt(mid, fade);
            var previewGain = PreviewGain(mid);
            var dest = i * channels;

            if (usePeaks
                && peaks.ReadRangePacked(f0, f1, 1, _crossfadePatchScratchMins, _crossfadePatchScratchMaxs) >= 1)
            {
                for (var ch = 0; ch < channels; ch++)
                {
                    var g = IsLaneMuted(ch) ? fadeGain : fadeGain * previewGain;
                    _columnMins[dest + ch] = _crossfadePatchScratchMins[ch] * g;
                    _columnMaxs[dest + ch] = _crossfadePatchScratchMaxs[ch] * g;
                }

                continue;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                _columnMins[dest + ch] = float.MaxValue;
                _columnMaxs[dest + ch] = float.MinValue;
            }

            var span = f1 - f0;
            var n = (int)Math.Min(CrossfadePatchSamplesPerColumn, span);
            for (var s = 0; s < n; s++)
            {
                var frame = n == 1
                    ? f0
                    : f0 + s * (span - 1) / (n - 1);
                if (frame < 0 || frame >= frameCount)
                {
                    continue;
                }

                var src = (int)frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    var g = IsLaneMuted(ch) ? fadeGain : fadeGain * previewGain;
                    var sample = samples[src + ch] * g;
                    if (sample < _columnMins[dest + ch])
                    {
                        _columnMins[dest + ch] = sample;
                    }

                    if (sample > _columnMaxs[dest + ch])
                    {
                        _columnMaxs[dest + ch] = sample;
                    }
                }
            }

            for (var ch = 0; ch < channels; ch++)
            {
                if (_columnMins[dest + ch] > _columnMaxs[dest + ch])
                {
                    _columnMins[dest + ch] = 0;
                    _columnMaxs[dest + ch] = 0;
                }
            }
        }
    }

    private void EnsureCrossfadePatchScratch(int channels)
    {
        if (_crossfadePatchScratchMins.Length >= channels
            && _crossfadePatchScratchMaxs.Length >= channels)
        {
            return;
        }

        _crossfadePatchScratchMins = new float[channels];
        _crossfadePatchScratchMaxs = new float[channels];
    }

    /// <summary>列フレーム範囲をプリ／ポスト帯との積集合にクリップ。</summary>
    private static bool TryClipFramesToCrossfadeBands(
        long f0,
        long f1,
        bool hasPre,
        long pre0,
        long pre1,
        bool hasPost,
        long post0,
        long post1,
        out long clipped0,
        out long clipped1)
    {
        clipped0 = long.MaxValue;
        clipped1 = long.MinValue;
        static bool Overlaps(long a0, long a1, long b0, long b1) => a1 > b0 && a0 < b1;

        if (hasPre && Overlaps(f0, f1, pre0, pre1))
        {
            clipped0 = Math.Min(clipped0, Math.Max(f0, pre0));
            clipped1 = Math.Max(clipped1, Math.Min(f1, pre1));
        }

        if (hasPost && Overlaps(f0, f1, post0, post1))
        {
            clipped0 = Math.Min(clipped0, Math.Max(f0, post0));
            clipped1 = Math.Max(clipped1, Math.Min(f1, post1));
        }

        return clipped1 > clipped0;
    }

    /// <summary>本体表示用フェードゲイン。元波形側（プリ＝out、ポスト＝in）。</summary>
    private static float CrossfadeBaseFadeGainAt(long frame, in LoopFadePreview fade)
    {
        if (!fade.TryCrossfadeMix(frame, out _, out var inSide, out var outGain, out var inGain))
        {
            return 1f;
        }

        return inSide == frame ? inGain : outGain;
    }

    private int FillLiveColumnPeaks(
        AudioDocument document,
        long startFrame,
        long endFrame,
        int width,
        int channels)
    {
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0 || width <= 0)
        {
            return 0;
        }

        if (document.Peaks.IsEmpty || document.Peaks.Channels != channels)
        {
            return FillRawColumnPeaks(document, startFrame, endFrame, width, channels);
        }

        var settledFrames = _liveSettledFrames >= 0
            ? _liveSettledFrames
            : Math.Min(document.Peaks.FrameCount, document.FrameCount);
        var settledCols = RecordContinue.CountPrefixColumns(
            startFrame,
            rangeFrames,
            width,
            settledFrames);
        var peakCols = 0;
        if (settledCols > 0)
        {
            var settledEnd = startFrame + settledCols * rangeFrames / width;
            peakCols = document.Peaks.ReadRangePacked(
                startFrame,
                settledEnd,
                settledCols,
                _columnMins,
                _columnMaxs);
            if (peakCols > 0 && _previewGainAtFrame is not null)
            {
                ApplyPreviewGainToColumns(startFrame, settledEnd, peakCols, channels);
            }
        }

        if (peakCols >= width)
        {
            return peakCols;
        }

        var rawStart = startFrame + peakCols * rangeFrames / width;
        return peakCols + FillRawColumnPeaks(
            document,
            rawStart,
            endFrame,
            width - peakCols,
            channels,
            peakCols);
    }

    private int FillRawColumnPeaks(
        AudioDocument document,
        long startFrame,
        long endFrame,
        int width,
        int channels,
        int destColumn = 0)
    {
        var rangeFrames = endFrame - startFrame;
        var buckets = (int)Math.Min(width, rangeFrames);
        if (buckets <= 0)
        {
            return 0;
        }

        var samples = document.Interleaved;
        var frameCount = document.FrameCount;
        var destBase = destColumn * channels;
        var destEnd = destBase + buckets * channels;
        var draft = _crossfadeDraft is { IsActive: true } active ? active : null;
        var fade = draft is not null ? LoopFadePreview.From(draft) : default;
        for (var i = destBase; i < destEnd; i++)
        {
            _columnMins[i] = float.MaxValue;
            _columnMaxs[i] = float.MinValue;
        }

        for (var i = 0; i < buckets; i++)
        {
            var f0 = startFrame + i * rangeFrames / buckets;
            var f1 = startFrame + (i + 1) * rangeFrames / buckets;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            f0 = Math.Clamp(f0, 0, frameCount);
            f1 = Math.Clamp(f1, f0, frameCount);
            var dest = destBase + i * channels;
            for (var frame = f0; frame < f1; frame++)
            {
                var src = (int)frame * channels;
                var gain = PreviewGain(frame);
                for (var ch = 0; ch < channels; ch++)
                {
                    float sample;
                    if (draft is not null
                        && TryGetCrossfadeBaseDisplaySample(frame, ch, fade, out var preview))
                    {
                        sample = preview;
                    }
                    else
                    {
                        sample = samples[src + ch];
                    }

                    sample *= IsLaneMuted(ch) ? 1f : gain;
                    if (sample < _columnMins[dest + ch])
                    {
                        _columnMins[dest + ch] = sample;
                    }

                    if (sample > _columnMaxs[dest + ch])
                    {
                        _columnMaxs[dest + ch] = sample;
                    }
                }
            }
        }

        for (var i = destBase; i < destEnd; i++)
        {
            if (_columnMins[i] > _columnMaxs[i])
            {
                _columnMins[i] = 0;
                _columnMaxs[i] = 0;
            }
        }

        return buckets;
    }

    private float PreviewGain(long frame) =>
        _previewGainAtFrame is { } gain ? gain(frame) : 1f;

    private void ApplyPreviewGainToColumns(long startFrame, long endFrame, int count, int channels)
    {
        if (_previewGainAtFrame is null || count <= 0)
        {
            return;
        }

        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var f0 = startFrame + i * rangeFrames / count;
            var f1 = startFrame + (i + 1) * rangeFrames / count;
            var mid = f0 + Math.Max(0L, (f1 - f0) / 2);
            var gain = PreviewGain(mid);
            if (Math.Abs(gain - 1f) < 1e-6f)
            {
                continue;
            }

            var dest = i * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                if (IsLaneMuted(ch))
                {
                    continue;
                }

                _columnMins[dest + ch] *= gain;
                _columnMaxs[dest + ch] *= gain;
            }
        }
    }

    private unsafe void RasterSamplePolyline(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        AudioDocument document,
        int channel,
        double start,
        double span,
        double top,
        double ampHeight,
        double mid,
        double scaleX)
    {
        var channels = document.Channels;
        var drawable = DrawableFrameCount(document);
        var first = (long)Math.Floor(start);
        var last = Math.Min(drawable - 1, (long)Math.Ceiling(start + span));
        if (last < first)
        {
            return;
        }

        var count = (int)(last - first + 1);
        var samples = document.Interleaved;
        var amp = ampHeight * 0.5 * _ampZoom;
        var draft = _crossfadeDraft is { IsActive: true } active ? active : null;
        var fade = draft is not null ? LoopFadePreview.From(draft) : default;
        int SampleY(float sample)
        {
            var y = (int)Math.Round(mid - sample * amp);
            return Math.Clamp(y, clipTop, clipBottom - 1);
        }

        float SampleAt(int index)
        {
            var frame = first + index;
            var gain = channel >= 0 && IsLaneMuted(channel) ? 1f : PreviewGain(frame);
            if (channel < 0)
            {
                if (draft is not null
                    && TryMixCrossfadeBaseDisplayFrame(frame, channels, fade, out var previewMixed))
                {
                    return previewMixed * gain;
                }

                SpectrogramEngine.MixFrame(samples, channels, frame, drawable, out var mixed);
                return mixed * gain;
            }

            if (draft is not null
                && TryGetCrossfadeBaseDisplaySample(frame, channel, fade, out var preview))
            {
                return preview * gain;
            }

            return samples[(int)frame * channels + channel] * gain;
        }

        var collectDots = ShouldDrawSamplePoints(count, width / scaleX);
        var dotRadius = Math.Max(1, (int)Math.Round(SamplePointRadius * scaleX));
        var color = WavePaintBgraFor(channel);

        if (count == 1)
        {
            var x = (int)Math.Round(FrameToX(first, start, span, width));
            var y = SampleY(SampleAt(0));
            FillVLine(buffer, stride, width, clipTop, clipBottom, x, y - 3, y + 3, color);
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, color);
            }

            return;
        }

        var prevX = 0;
        var prevY = 0;
        var havePrev = false;
        for (var i = 0; i < count; i++)
        {
            var x = (int)Math.Round(FrameToX(first + i, start, span, width));
            var y = SampleY(SampleAt(i));
            if (havePrev)
            {
                DrawThickLine(buffer, stride, width, clipTop, clipBottom, prevX, prevY, x, y, color);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
            if (collectDots)
            {
                FillDot(buffer, stride, width, clipTop, clipBottom, x, y, dotRadius, color);
            }
        }
    }

    private static unsafe void FillVLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x,
        int y1,
        int y2,
        int color)
    {
        if ((uint)x >= (uint)width)
        {
            return;
        }

        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        if (y1 < clipTop)
        {
            y1 = clipTop;
        }

        if (y2 >= clipBottom)
        {
            y2 = clipBottom - 1;
        }

        if (y1 > y2)
        {
            return;
        }

        var p = buffer + y1 * stride + x;
        for (var y = y1; y <= y2; y++)
        {
            *p = color;
            p += stride;
        }
    }

    private static unsafe void FillVLineGradient(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x,
        int y1,
        int y2,
        int color,
        double mid,
        double halfHeight)
    {
        if ((uint)x >= (uint)width)
        {
            return;
        }

        if (y1 > y2)
        {
            (y1, y2) = (y2, y1);
        }

        if (y1 < clipTop)
        {
            y1 = clipTop;
        }

        if (y2 >= clipBottom)
        {
            y2 = clipBottom - 1;
        }

        if (y1 > y2)
        {
            return;
        }

        var p = buffer + y1 * stride + x;
        for (var y = y1; y <= y2; y++)
        {
            *p = WaveformLaneGradient.Shade(color, y + 0.5, mid, halfHeight, UiThemeService.Painted);
            p += stride;
        }
    }

    private static unsafe void FillDot(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int cx,
        int cy,
        int radius,
        int color)
    {
        var r2 = radius * radius;
        var y0 = Math.Max(clipTop, cy - radius);
        var y1 = Math.Min(clipBottom - 1, cy + radius);
        var x0 = Math.Max(0, cx - radius);
        var x1 = Math.Min(width - 1, cx + radius);
        for (var y = y0; y <= y1; y++)
        {
            var dy = y - cy;
            var row = buffer + y * stride;
            for (var x = x0; x <= x1; x++)
            {
                var dx = x - cx;
                if (dx * dx + dy * dy <= r2)
                {
                    row[x] = color;
                }
            }
        }
    }

    private static unsafe void FillHLine(int* buffer, int stride, int width, int x0, int x1, int y, int color)
    {
        var p = buffer + y * stride;
        var left = Math.Clamp(x0, 0, width);
        var right = Math.Clamp(x1, left, width);
        for (var x = left; x < right; x++)
        {
            p[x] = color;
        }
    }

    private static unsafe void DrawThickLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x0,
        int y0,
        int x1,
        int y1,
        int color)
    {
        DrawLine(buffer, stride, width, clipTop, clipBottom, x0, y0, x1, y1, color);
        DrawLine(buffer, stride, width, clipTop, clipBottom, x0, y0 + 1, x1, y1 + 1, color);
    }

    /// <summary>ラウドネス波形の輪郭からビュー下端までを塗る。急斜線は列ごとの最上点だけ塗る。</summary>
    private static unsafe void FillBelowLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x0,
        int y0,
        int x1,
        int y1,
        int color)
    {
        if (clipBottom <= clipTop || (color >>> 24) == 0)
        {
            return;
        }

        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        var colX = x0;
        var colTop = y0;
        while (true)
        {
            if (x0 != colX)
            {
                FillVLine(buffer, stride, width, clipTop, clipBottom, colX, colTop, clipBottom - 1, color);
                colX = x0;
                colTop = y0;
            }
            else if (y0 < colTop)
            {
                colTop = y0;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        FillVLine(buffer, stride, width, clipTop, clipBottom, colX, colTop, clipBottom - 1, color);
    }

    private static unsafe void DrawLine(
        int* buffer,
        int stride,
        int width,
        int clipTop,
        int clipBottom,
        int x0,
        int y0,
        int x1,
        int y1,
        int color)
    {
        var dx = Math.Abs(x1 - x0);
        var sx = x0 < x1 ? 1 : -1;
        var dy = -Math.Abs(y1 - y0);
        var sy = y0 < y1 ? 1 : -1;
        var err = dx + dy;
        while (true)
        {
            if ((uint)x0 < (uint)width && y0 >= clipTop && y0 < clipBottom)
            {
                buffer[y0 * stride + x0] = color;
            }

            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy;
                x0 += sx;
            }

            if (e2 <= dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }


}
