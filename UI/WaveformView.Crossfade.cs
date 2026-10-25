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
    // プリ／ポストの原色（旗・バー・重ね波形）。
    private static Color CrossfadePrePrimary => Theme.Get("CrossfadePreBrush");
    private static Color CrossfadePostPrimary => Theme.Get("CrossfadePostBrush");

    /// <summary>旗側の長さバーを波形ビットマップの下（静的層）に塗る。</summary>
    private void DrawCrossfadeBarBackgrounds(DrawingContext dc, Rect wave, double viewStart, double viewSpan)
    {
        var draft = CrossfadeDraft;
        if (draft is not { IsActive: true } || draft.LoopLength <= 0 || viewSpan <= 0)
        {
            return;
        }

        if (draft.PreCrossfadeFrames > 0)
        {
            DrawCrossfadeBackgroundWash(
                dc, wave, viewStart, viewSpan, draft.PreBarRange, CrossfadePrePrimary);
        }

        if (draft.PostCrossfadeFrames > 0)
        {
            DrawCrossfadeBackgroundWash(
                dc, wave, viewStart, viewSpan, draft.PostBarRange, CrossfadePostPrimary);
        }
    }

    private static void DrawCrossfadeBackgroundWash(
        DrawingContext dc,
        Rect wave,
        double viewStart,
        double viewSpan,
        WaveSelection range,
        Color primary)
    {
        if (!MarkerRolePaint.TryGetVisibleRangeRect(range, wave, viewStart, viewSpan, out var bar))
        {
            return;
        }

        // 原色のまま、半透明で波形の下に置く（暗く潰さない）。
        var wash = Color.FromArgb(0x55, primary.R, primary.G, primary.B);
        dc.DrawRectangle(WpfControlHelpers.FrozenBrush(wash), null, bar);
    }

    private void DrawCrossfadeDraft(DrawingContext dc, Rect bounds, double start, double span)
    {
        var draft = CrossfadeDraft;
        if (draft is not { IsActive: true } || _document is null || draft.LoopLength <= 0)
        {
            return;
        }

        var wave = WaveformBounds(bounds);

        // 範囲塗りは静的層のみ。重ね色はゾーン一色（赤緑の同時描画はしない）。
        // 元波形ピークは局所フェード（プリ=out→0 / ポスト=in←0）。重ねは相手側のみ。
        // プリ区間＝S 手前×in（緑）、ポスト区間＝E 以降×out（赤）。
        if (draft.PreCrossfadeFrames > 0)
        {
            DrawCrossfadeOverlapWaves(
                dc,
                wave,
                start,
                span,
                draft.PreDrawStartFrame,
                draft.PreCrossfadeFrames,
                sourceStart: draft.PreFlagFrame,
                color: CrossfadePrePrimary,
                outShape: draft.PreOutShape,
                inShape: draft.PreInShape,
                fadeIn: true);
        }

        if (draft.PostCrossfadeFrames > 0)
        {
            DrawCrossfadeOverlapWaves(
                dc,
                wave,
                start,
                span,
                draft.PostDrawStartFrame,
                draft.PostCrossfadeFrames,
                sourceStart: draft.EndFrame,
                color: CrossfadePostPrimary,
                outShape: draft.PostOutShape,
                inShape: draft.PostInShape,
                fadeIn: false);
        }

        var sePen = new Pen(WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")), 1.0);
        sePen.Freeze();

        void StemOnly(long frame)
        {
            var x = FrameToViewX(frame, start, span, bounds);
            if (x < wave.Left - 2 || x > wave.Right + 2)
            {
                return;
            }

            dc.DrawLine(sePen, new Point(x, wave.Top), new Point(x, wave.Bottom));
        }

        // Pre/Post 旗は DrawCrossfadeFlags で最前面に描く。
        StemOnly(draft.StartFrame);
        StemOnly(draft.EndFrame);
    }

    /// <summary>Pre/Post 旗をタイムレーン（サンプルループ／タイムコード側）に描き、当たり判定用に覚える。</summary>
    private void DrawCrossfadeFlags(DrawingContext dc, Rect bounds, double start, double span)
    {
        _crossfadeFlags.Clear();
        var draft = CrossfadeDraft;
        if (draft is not { IsActive: true } || _document is null || draft.LoopLength <= 0)
        {
            return;
        }

        var dip = UiDpi.Get(this).PixelsPerDip;
        var typeface = new Typeface("Segoe UI");
        // マーカー／リージョンレーンではなく、サンプルループがあるタイムレーンに置く。
        var lane = TimeLaneBounds(bounds);
        if (lane.Height <= 1)
        {
            return;
        }

        var whiteText = WpfControlHelpers.FrozenBrush(Colors.White);

        void Flag(long frame, string label, bool growLeft, Color flagColor, CrossfadeDragPart part)
        {
            var x = FrameToViewX(frame, start, span, bounds);
            if (x < ScaleLeft(bounds) - 24 || x > bounds.Width + 24)
            {
                return;
            }

            var boxW = MeasureFlagWidth(label, dip);
            var left = growLeft ? x - boxW : x;
            // FullFlagRect は Y=1 固定（マーカーレーン用）なので、タイムレーン上端に合わせて置く。
            var box = new Rect(
                left,
                lane.Y + 1,
                boxW,
                Math.Max(2, lane.Height - 2));
            var xs = WpfControlHelpers.SnapDeviceCenter(x, dip);
            var inset = WpfControlHelpers.DeviceHairline(dip) * 0.5;
            box = AlignFlagToStem(box, xs, growLeft, inset);
            _crossfadeFlags.Add((part, box));
            var geo = WpfControlHelpers.FlagGeometry(box, FlagCornerRadius, stemOnLeft: !growLeft);
            var fill = WpfControlHelpers.FrozenBrush(flagColor);
            var stem = WpfControlHelpers.FrozenHairline(flagColor, dip);
            dc.DrawGeometry(fill, null, geo);
            var idText = new FormattedText(
                label,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                ChromeFontSize,
                whiteText,
                dip);
            dc.PushClip(geo);
            dc.DrawText(
                idText,
                new Point(box.X + FlagPadX, box.Y + Math.Max(0, (box.Height - idText.Height) * 0.5)));
            dc.Pop();
            dc.DrawLine(stem, new Point(xs, box.Bottom), new Point(xs, bounds.Height));
        }

        Flag(draft.PreFlagFrame, UiStrings.CrossfadeMarkerPre, growLeft: true, CrossfadePrePrimary, CrossfadeDragPart.PreCrossfade);
        Flag(draft.PostFlagFrame, UiStrings.CrossfadeMarkerPost, growLeft: false, CrossfadePostPrimary, CrossfadeDragPart.PostCrossfade);
    }

    /// <summary>
    /// クロス区間に相手側材料を、ゾーン色一色＋フェード曲線ゲインで色付けする。
    /// 本体と同じピーク経路で列を取り gain を乗算し、WriteableBitmap へ一度描いて転送する。
    /// </summary>
    private void DrawCrossfadeOverlapWaves(
        DrawingContext dc,
        Rect wave,
        double viewStart,
        double viewSpan,
        long viewStartFrame,
        long length,
        long sourceStart,
        Color color,
        FadeShape outShape,
        FadeShape inShape,
        bool fadeIn)
    {
        if (_document is null || length <= 0 || viewSpan <= 0 || wave.Height < 4)
        {
            return;
        }

        var channels = Math.Max(1, _document.Channels);
        var bounds = new Rect(RenderSize);
        var x0Full = FrameToViewX(viewStartFrame, viewStart, viewSpan, bounds);
        var x1Full = FrameToViewX(viewStartFrame + length, viewStart, viewSpan, bounds);
        if (x1Full <= x0Full || x1Full < wave.Left || x0Full > wave.Right)
        {
            return;
        }

        var x0 = Math.Max(wave.Left, x0Full);
        var x1 = Math.Min(wave.Right, x1Full);
        if (x1 <= x0)
        {
            return;
        }

        // 画面外に切れたぶんは offset も切る。全長を見える幅へ押し込むと拡大が止まる。
        var spanX = x1Full - x0Full;
        var offset0 = (long)Math.Floor((x0 - x0Full) / spanX * length);
        var offset1 = (long)Math.Ceiling((x1 - x0Full) / spanX * length);
        offset0 = Math.Clamp(offset0, 0, length);
        offset1 = Math.Clamp(offset1, offset0, length);
        var visibleLen = offset1 - offset0;
        if (visibleLen <= 0)
        {
            return;
        }

        var dpi = UiDpi.Get(this);
        var scaleX = Math.Max(1e-6, dpi.DpiScaleX);
        var scaleY = Math.Max(1e-6, dpi.DpiScaleY);
        var destW = x1 - x0;
        var destH = wave.Height;
        var pixelH = Math.Max(1, (int)Math.Ceiling(destH * scaleY));
        // 本体と同じく表示ピクセル幅＝列数（低解像度ビットマップの引き伸ばしをしない）。
        var pixelW = Math.Max(1, (int)Math.Ceiling(destW * scaleX));
        var light = UseLightCrossfadeOverlap;
        var hasPcm = !_document.IsDeferredLoad
            && !_document.IsStreamPlayback
            && _document.SampleCount >= channels;
        var usePolyline = !SeekAndSelectOnly && hasPcm && IsPolylineZoom(visibleLen, pixelW);

        var drawChannels = SeekAndSelectOnly ? 1 : channels;
        var laneGapPx = drawChannels > 1 ? 4d * scaleY : 0d;
        ResolveWaveLane(
            overlay: false,
            pixelH,
            drawChannels,
            laneGapPx,
            _ampZoom,
            out var laneHeightPx,
            out var laneOriginPx,
            out var ampHeightPx);
        if (laneHeightPx < 2)
        {
            return;
        }

        var key = HashCode.Combine(
            viewStartFrame,
            length,
            sourceStart,
            offset0,
            offset1,
            pixelW,
            drawChannels,
            HashCode.Combine(
                HashCode.Combine(
                    BitConverter.DoubleToInt64Bits(viewStart),
                    BitConverter.DoubleToInt64Bits(viewSpan),
                    BitConverter.DoubleToInt64Bits(x0),
                    BitConverter.DoubleToInt64Bits(x1),
                    BitConverter.DoubleToInt64Bits(_ampZoom),
                    BitConverter.DoubleToInt64Bits(wave.Height),
                    pixelH),
                HashCode.Combine(
                    (int)outShape,
                    (int)inShape,
                    usePolyline ? 1 : 0,
                    light ? 0 : 1)));

        var cacheKey = fadeIn ? _crossfadePreCacheKey : _crossfadePostCacheKey;
        var hasBitmap = fadeIn ? _crossfadePreHasBitmap : _crossfadePostHasBitmap;
        var bitmap = fadeIn ? _crossfadePreBitmap : _crossfadePostBitmap;

        // スロットル待ち中はキーが古くても既存ビットマップを転送（常時表示を維持）。
        var rebuild = cacheKey != key
            || !hasBitmap
            || bitmap is null
            || bitmap.PixelWidth != pixelW
            || bitmap.PixelHeight != pixelH;
        if (rebuild && IsCrossfadeDraftDragging && _crossfadeOverlayRebuildPending && hasBitmap)
        {
            rebuild = false;
        }

        if (rebuild)
        {
            EnsureCrossfadeOverlapFill(fadeIn, color);
            var fillBgra = fadeIn ? _crossfadePreFillBgra : _crossfadePostFillBgra;
            var pixels = fadeIn ? _crossfadePrePixels : _crossfadePostPixels;
            if (pixels.Length < pixelW * pixelH)
            {
                pixels = new int[pixelW * pixelH];
            }
            else
            {
                Array.Clear(pixels, 0, pixelW * pixelH);
            }

            RasterizeCrossfadeOverlapBitmap(
                pixels,
                pixelW,
                pixelH,
                channels,
                drawChannels,
                sourceStart,
                length,
                offset0,
                visibleLen,
                laneOriginPx,
                laneHeightPx,
                laneGapPx,
                ampHeightPx,
                fillBgra,
                outShape,
                inShape,
                fadeIn,
                usePolyline,
                scaleX);

            bitmap = EnsureCrossfadeOverlapBitmap(fadeIn, pixelW, pixelH, dpi);
            bitmap.WritePixels(new Int32Rect(0, 0, pixelW, pixelH), pixels, pixelW * 4, 0);
            var dest = new Rect(x0, wave.Top, destW, destH);
            if (fadeIn)
            {
                _crossfadePreCacheKey = key;
                _crossfadePrePixels = pixels;
                _crossfadePreBitmap = bitmap;
                _crossfadePreDest = dest;
                _crossfadePreHasBitmap = true;
            }
            else
            {
                _crossfadePostCacheKey = key;
                _crossfadePostPixels = pixels;
                _crossfadePostBitmap = bitmap;
                _crossfadePostDest = dest;
                _crossfadePostHasBitmap = true;
            }

            hasBitmap = true;
        }

        if (!hasBitmap)
        {
            return;
        }

        bitmap = fadeIn ? _crossfadePreBitmap : _crossfadePostBitmap;
        if (bitmap is null)
        {
            return;
        }

        // 再構築時は最新 dest、スロットル保留中は前回 dest のまま転送。
        var blitDest = rebuild
            ? new Rect(x0, wave.Top, destW, destH)
            : (fadeIn ? _crossfadePreDest : _crossfadePostDest);
        if (blitDest.Width <= 0 || blitDest.Height <= 0)
        {
            return;
        }

        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        var context = group.Open();
        context.DrawImage(bitmap, blitDest);
        context.Close();
        dc.DrawDrawing(group);
    }

    private void EnsureCrossfadeOverlapFill(bool fadeIn, Color color)
    {
        // 本体 WaveFill と同様に不透明寄り（半透明の縁取り感を避ける）。
        var fill = ToBgra(Color.FromArgb(220, color.R, color.G, color.B));
        if (fadeIn)
        {
            _crossfadePreFillBgra = fill;
        }
        else
        {
            _crossfadePostFillBgra = fill;
        }
    }

    private WriteableBitmap EnsureCrossfadeOverlapBitmap(bool fadeIn, int width, int height, DpiScale dpi)
    {
        var existing = fadeIn ? _crossfadePreBitmap : _crossfadePostBitmap;
        if (existing is not null
            && existing.PixelWidth == width
            && existing.PixelHeight == height)
        {
            return existing;
        }

        var bmp = new WriteableBitmap(
            width,
            height,
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Bgra32,
            null);
        if (fadeIn)
        {
            _crossfadePreBitmap = bmp;
        }
        else
        {
            _crossfadePostBitmap = bmp;
        }

        return bmp;
    }

    /// <summary>
    /// 本体と同じ分岐: 拡大時は折れ線、それ以外はピーク包絡。ビットマップは表示ピクセル幅と 1:1。
    /// </summary>
    private unsafe void RasterizeCrossfadeOverlapBitmap(
        int[] pixels,
        int columns,
        int height,
        int channels,
        int drawChannels,
        long sourceStart,
        long length,
        long offset0,
        long visibleLen,
        double laneOrigin,
        double laneHeight,
        double laneGap,
        double ampHeight,
        int fillBgra,
        FadeShape outShape,
        FadeShape inShape,
        bool fadeIn,
        bool usePolyline,
        double scaleX)
    {
        fixed (int* buffer = pixels)
        {
            if (usePolyline)
            {
                for (var ch = 0; ch < drawChannels; ch++)
                {
                    var top = laneOrigin + ch * (laneHeight + laneGap);
                    var mid = top + laneHeight * 0.5;
                    if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
                    {
                        continue;
                    }

                    RasterCrossfadeOverlapPolyline(
                        buffer,
                        columns,
                        clipTop,
                        clipBottom,
                        channels,
                        drawChannels,
                        ch,
                        sourceStart,
                        length,
                        offset0,
                        visibleLen,
                        top,
                        ampHeight,
                        mid,
                        fillBgra,
                        outShape,
                        inShape,
                        fadeIn,
                        scaleX);
                }

                return;
            }

            // 列中央の offset で gain を 1 回だけ先計算。
            var gains = new float[columns];
            for (var i = 0; i < columns; i++)
            {
                var midOffset = offset0 + (2 * i + 1) * visibleLen / (2 * columns);
                midOffset = Math.Clamp(midOffset, 0L, Math.Max(0L, length - 1));
                FadeCurves.CrossfadeGainsAtFrame(
                    outShape, inShape, midOffset, 0, length, out var outGain, out var inGain);
                gains[i] = fadeIn ? inGain : outGain;
            }

            if (!TryFillCrossfadeOverlapPeaks(
                    sourceStart,
                    offset0,
                    visibleLen,
                    columns,
                    channels,
                    drawChannels,
                    gains,
                    out var packedChannels))
            {
                return;
            }

            for (var ch = 0; ch < drawChannels; ch++)
            {
                var top = laneOrigin + ch * (laneHeight + laneGap);
                var mid = top + laneHeight * 0.5;
                if (!TryLaneClip(top, laneHeight, height, out var clipTop, out var clipBottom))
                {
                    continue;
                }

                RasterPeakEnvelope(
                    buffer,
                    columns,
                    columns,
                    clipTop,
                    clipBottom,
                    ch,
                    packedChannels,
                    columns,
                    px0: 0,
                    top,
                    laneHeight,
                    ampHeight,
                    mid,
                    connectNeighbors: ShouldConnectEnvelopeNeighbors(visibleLen, columns),
                    colorOverride: fillBgra);
            }
        }
    }

    /// <summary>本体 <see cref="RasterSamplePolyline"/> と同型。ソース＋クロスフェード gain で折れ線を引く。</summary>
    private unsafe void RasterCrossfadeOverlapPolyline(
        int* buffer,
        int width,
        int clipTop,
        int clipBottom,
        int channels,
        int drawChannels,
        int channel,
        long sourceStart,
        long length,
        long offset0,
        long visibleLen,
        double top,
        double ampHeight,
        double mid,
        int color,
        FadeShape outShape,
        FadeShape inShape,
        bool fadeIn,
        double scaleX)
    {
        if (_document is null || visibleLen <= 0 || width <= 0)
        {
            return;
        }

        var samples = _document.Interleaved;
        var frameCount = _document.FrameCount;
        var amp = ampHeight * 0.5 * _ampZoom;
        // 本体 RasterSamplePolyline と同じ点々条件・半径。
        var count = (int)Math.Min(int.MaxValue, visibleLen);
        var collectDots = ShouldDrawSamplePoints(count, width / scaleX);
        var dotRadius = Math.Max(1, (int)Math.Round(SamplePointRadius * scaleX));
        int SampleY(float sample)
        {
            var y = (int)Math.Round(mid - sample * amp);
            return Math.Clamp(y, clipTop, clipBottom - 1);
        }

        float SampleAt(long offset)
        {
            FadeCurves.CrossfadeGainsAtFrame(
                outShape, inShape, offset, 0, length, out var outGain, out var inGain);
            var gain = fadeIn ? inGain : outGain;
            var frame = sourceStart + offset;
            if (frame < 0 || frame >= frameCount)
            {
                return 0f;
            }

            var off = (int)frame * channels;
            if (drawChannels == 1 && channels > 1)
            {
                double sum = 0;
                for (var ch = 0; ch < channels; ch++)
                {
                    sum += samples[off + ch];
                }

                return (float)(sum / channels) * gain;
            }

            return samples[off + Math.Clamp(channel, 0, channels - 1)] * gain;
        }

        double OffsetToX(long offset)
        {
            if (visibleLen <= 1)
            {
                return 0;
            }

            return (offset - offset0) * (width - 1) / (double)(visibleLen - 1);
        }

        var first = offset0;
        var last = offset0 + visibleLen - 1;
        if (last < first)
        {
            return;
        }

        if (last == first)
        {
            var x = (int)Math.Round(OffsetToX(first));
            var y = SampleY(SampleAt(first));
            FillVLine(buffer, width, width, clipTop, clipBottom, x, y - 3, y + 3, color);
            if (collectDots)
            {
                FillDot(buffer, width, width, clipTop, clipBottom, x, y, dotRadius, color);
            }

            return;
        }

        var prevX = 0;
        var prevY = 0;
        var havePrev = false;
        for (var offset = first; offset <= last; offset++)
        {
            var x = (int)Math.Round(OffsetToX(offset));
            var y = SampleY(SampleAt(offset));
            if (havePrev)
            {
                DrawThickLine(buffer, width, width, clipTop, clipBottom, prevX, prevY, x, y, color);
            }

            prevX = x;
            prevY = y;
            havePrev = true;
            if (collectDots)
            {
                FillDot(buffer, width, width, clipTop, clipBottom, x, y, dotRadius, color);
            }
        }
    }

    /// <summary>
    /// ピークピラミッド（無ければ生 PCM の列バケット）で列 min/max を取り、gains を乗算する。
    /// 本体波形のピーク経路と同じ集計。
    /// </summary>
    private bool TryFillCrossfadeOverlapPeaks(
        long sourceStart,
        long offset0,
        long visibleLen,
        int columns,
        int channels,
        int drawChannels,
        ReadOnlySpan<float> gains,
        out int packedChannels)
    {
        packedChannels = drawChannels;
        if (_document is null || columns <= 0 || visibleLen <= 0)
        {
            return false;
        }

        var f0 = sourceStart + offset0;
        var f1 = sourceStart + offset0 + visibleLen;
        if (f1 <= f0)
        {
            return false;
        }

        EnsureColumnBuffers(columns * channels);
        var peaks = _document.Peaks;
        var count = 0;
        if (!peaks.IsEmpty && peaks.Channels == channels)
        {
            count = peaks.ReadRangePacked(f0, f1, columns, _columnMins, _columnMaxs);
        }

        if (count < columns)
        {
            // ピークが無い／足りないときだけ、本体と同型の生カラム集計（ドラフト混じり無し）。
            count = FillRawOverlapColumnPeaks(f0, f1, columns, channels);
            if (count < columns)
            {
                return false;
            }
        }

        packedChannels = channels;
        if (drawChannels == 1 && channels > 1)
        {
            ChannelMix.FoldPackedPeaksToUnion(_columnMins, _columnMaxs, columns, channels);
            packedChannels = 1;
        }

        for (var i = 0; i < columns; i++)
        {
            var gain = gains[i];
            var dest = i * packedChannels;
            for (var ch = 0; ch < packedChannels; ch++)
            {
                _columnMins[dest + ch] *= gain;
                _columnMaxs[dest + ch] *= gain;
            }
        }

        return true;
    }

    /// <summary>クロス重ね用: ドラフト非適用の生 PCM 列ピーク（FillRawColumnPeaks と同型）。</summary>
    private int FillRawOverlapColumnPeaks(long startFrame, long endFrame, int width, int channels)
    {
        if (_document is null || width <= 0 || channels < 1)
        {
            return 0;
        }

        var rangeFrames = endFrame - startFrame;
        var buckets = (int)Math.Min(width, Math.Max(1L, rangeFrames));
        if (buckets <= 0)
        {
            return 0;
        }

        var samples = _document.Interleaved;
        var frameCount = _document.FrameCount;
        EnsureColumnBuffers(buckets * channels);
        var destEnd = buckets * channels;
        for (var i = 0; i < destEnd; i++)
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
            var dest = i * channels;
            for (var frame = f0; frame < f1; frame++)
            {
                var src = (int)frame * channels;
                for (var ch = 0; ch < channels; ch++)
                {
                    var sample = samples[src + ch];
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

        for (var i = 0; i < destEnd; i++)
        {
            if (_columnMins[i] > _columnMaxs[i])
            {
                _columnMins[i] = 0;
                _columnMaxs[i] = 0;
            }
        }

        return buckets;
    }

    private enum CrossfadeDragPart
    {
        None,
        Start,
        End,
        PreCrossfade,
        PostCrossfade,
    }

    /// <summary>マーカー旗と同じく、旗矩形 Contains で Pre/Post を掴む。</summary>
    private bool TryHitCrossfadeFlag(Point point, out CrossfadeDragPart part)
    {
        for (var i = _crossfadeFlags.Count - 1; i >= 0; i--)
        {
            var hit = _crossfadeFlags[i];
            if (hit.Flag.Contains(point))
            {
                part = hit.Part;
                return true;
            }
        }

        part = CrossfadeDragPart.None;
        return false;
    }

    private bool TryHitCrossfadeDraft(Point pos, out CrossfadeDragPart part)
    {
        part = CrossfadeDragPart.None;
        var draft = CrossfadeDraft;
        if (draft is not { IsActive: true } || _document is null || draft.LoopLength <= 0)
        {
            return false;
        }

        // 旗は TryHitCrossfadeFlag。ここでは S/E 茎と長さバー本体。
        var bounds = new Rect(RenderSize);
        var start = _viewStart;
        var span = ViewSpanFrames;
        if (span <= 0)
        {
            return false;
        }

        var hitPx = Math.Max(6d, MarkerSnapPx);
        var bestDist = hitPx;
        var hit = CrossfadeDragPart.None;
        void ConsiderStem(long frame, CrossfadeDragPart candidate)
        {
            var x = FrameToViewX(frame, start, span, bounds);
            var dist = Math.Abs(pos.X - x);
            if (dist <= bestDist)
            {
                bestDist = dist;
                hit = candidate;
            }
        }

        ConsiderStem(draft.StartFrame, CrossfadeDragPart.Start);
        ConsiderStem(draft.EndFrame, CrossfadeDragPart.End);

        // 長さバー上クリックでも旗と同じパーツを掴む。
        if (draft.PreCrossfadeFrames > 0
            && IsPointInCrossfadeBar(pos, draft.PreBarRange, start, span, bounds))
        {
            part = CrossfadeDragPart.PreCrossfade;
            return true;
        }

        if (draft.PostCrossfadeFrames > 0
            && IsPointInCrossfadeBar(pos, draft.PostBarRange, start, span, bounds))
        {
            part = CrossfadeDragPart.PostCrossfade;
            return true;
        }

        part = hit;
        return hit != CrossfadeDragPart.None;
    }

    private bool IsPointInCrossfadeBar(
        Point pos,
        WaveSelection range,
        double viewStart,
        double viewSpan,
        Rect bounds)
    {
        var wave = WaveformBounds(bounds);
        if (!wave.Contains(pos)
            || !MarkerRolePaint.TryGetVisibleRangeRect(range, wave, viewStart, viewSpan, out var bar))
        {
            return false;
        }

        return bar.Contains(pos);
    }

    private void BeginCrossfadeDraftDrag(CrossfadeDragPart part, Point start)
    {
        _crossfadeDrag = part;
        _crossfadeDragMoved = false;
        _crossfadeDragSnapHold = null;
        _dragStart = start;
        // 操作開始で軽い重ねへ。詳細はデバウンス後に一度だけ。
        _crossfadeOverlapQuality = CrossfadeOverlapQuality.Light;
        InvalidateCrossfadeOverlapCache();
        CaptureMouse();
        Cursor = Cursors.SizeWE;
        Focus();
    }

    private long ResolveCrossfadeDragFrame(double mouseX)
    {
        var draft = CrossfadeDraft;
        if (draft is null || _document is null)
        {
            return RawFrameAt(mouseX);
        }

        // ドラッグ中、近いバー／旗／S/E に吸着（マーカーと同じ距離感）。
        var targets = new List<long>(4);
        switch (_crossfadeDrag)
        {
            case CrossfadeDragPart.Start:
            case CrossfadeDragPart.End:
                if (draft.PreCrossfadeFrames > 0)
                {
                    targets.Add(draft.PreFlagFrame);
                }

                if (draft.PostCrossfadeFrames > 0)
                {
                    targets.Add(draft.PostFlagFrame);
                }

                break;
            case CrossfadeDragPart.PreCrossfade:
                targets.Add(draft.StartFrame);
                targets.Add(draft.EndFrame);
                if (draft.PostCrossfadeFrames > 0)
                {
                    targets.Add(draft.PostFlagFrame);
                }

                break;
            case CrossfadeDragPart.PostCrossfade:
                targets.Add(draft.StartFrame);
                targets.Add(draft.EndFrame);
                if (draft.PreCrossfadeFrames > 0)
                {
                    targets.Add(draft.PreFlagFrame);
                }

                break;
        }

        if (targets.Count > 0
            && TryPickNearestSnap(
                targets,
                mouseX,
                MarkerSnapPx,
                _viewStart,
                _viewStart + ViewSpanFrames,
                frame => FrameToViewX(frame, _viewStart, ViewSpanFrames, new Rect(0, 0, ActualWidth, ActualHeight)),
                out _,
                out var snapped))
        {
            _crossfadeDragSnapHold = snapped;
            return snapped;
        }

        if (_crossfadeDragSnapHold is long held
            && TryGetSnapHoldDistance(mouseX, held, out var holdDist)
            && holdDist <= MarkerSnapPx * 1.5)
        {
            return held;
        }

        _crossfadeDragSnapHold = null;
        return RawFrameAt(mouseX);
    }

    private void ApplyCrossfadeDraftDrag(long frame)
    {
        var draft = CrossfadeDraft;
        if (draft is null || _document is null)
        {
            return;
        }

        var frames = _document.FrameCount;
        // 上限はファイル余白とループ長。相手フェードの自動削減／100%連動は SetPre/SetPost 側。
        if (_crossfadeDrag == CrossfadeDragPart.PostCrossfade)
        {
            var maxPost = Math.Min(Math.Max(0, frames - draft.EndFrame), draft.LoopLength);
            frame = Math.Clamp(frame, draft.EndFrame, draft.EndFrame + maxPost);
        }
        else if (_crossfadeDrag == CrossfadeDragPart.PreCrossfade)
        {
            var maxPre = Math.Min(Math.Max(0, draft.StartFrame), draft.LoopLength);
            frame = Math.Clamp(frame, draft.StartFrame - maxPre, draft.StartFrame);
        }
        else
        {
            frame = Math.Clamp(frame, 0, frames);
            if (draft.AlignLoopEndsToTwentyEight
                && _crossfadeDrag is CrossfadeDragPart.Start or CrossfadeDragPart.End)
            {
                frame = CrossfadeEditState.RoundToAlignStep(frame);
            }
        }

        switch (_crossfadeDrag)
        {
            case CrossfadeDragPart.Start:
                draft.SetStart(frame, frames);
                break;
            case CrossfadeDragPart.End:
                draft.SetEnd(frame, frames);
                break;
            case CrossfadeDragPart.PreCrossfade:
                // 緑旗は S 手前 → 長さ = S − 旗
                draft.SetPreCrossfade(Math.Max(0, draft.StartFrame - frame), frames);
                break;
            case CrossfadeDragPart.PostCrossfade:
                // 赤旗は E 後ろ → 長さ = 旗 − E
                draft.SetPostCrossfade(Math.Max(0, frame - draft.EndFrame), frames);
                break;
            default:
                return;
        }

        // S/E・Pre/Post とも重ねは 40ms スロットル、本体は 0.5 秒デバウンス（raw 全走査を避ける）。
        _crossfadeDraftChangeDebounce = true;
        RequestThrottledCrossfadeOverlayRebuild();
        ScheduleCrossfadeWavePreview(immediate: false);

        CrossfadeDraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void FinishCrossfadeDraftDrag()
    {
        if (_crossfadeDrag == CrossfadeDragPart.None)
        {
            return;
        }

        var moved = _crossfadeDragMoved;
        _crossfadeDrag = CrossfadeDragPart.None;
        _crossfadeDragMoved = false;
        _crossfadeDragSnapHold = null;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        RestoreIdleCursor();
        _crossfadeDraftChangeDebounce = moved;
        // 離したら重ねを最新に揃え、本体は 0.5 秒デバウンス（クロス帯差分ラスタ）。
        _crossfadeOverlayThrottleTimer.Stop();
        _crossfadeOverlayRebuildPending = false;
        InvalidateCrossfadeOverlapCache();
        InvalidatePlayheadLayer();
        if (moved)
        {
            ScheduleCrossfadeWavePreview(immediate: false);
        }

        CrossfadeDraftChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RequestThrottledCrossfadeOverlayRebuild()
    {
        _crossfadeOverlayRebuildPending = true;
        // 旗位置は毎回描き直し（安い）。重ねジオメトリはタイマーで再構築。
        InvalidatePlayheadLayer();
        if (!_crossfadeOverlayThrottleTimer.IsEnabled)
        {
            _crossfadeOverlayThrottleTimer.Start();
        }
    }

    /// <summary>キーだけ無効化。ドラッグ中スロットルではビットマップを残して常時表示を維持。</summary>
    private void InvalidateCrossfadeOverlapCache()
    {
        _crossfadePreCacheKey = 0;
        _crossfadePostCacheKey = 0;
    }

    /// <summary>緑／赤重ねビットマップだけ破棄（デバウンス用 0.5 秒タイマーは維持）。</summary>
    private void DiscardCrossfadeOverlapBitmaps()
    {
        InvalidateCrossfadeOverlapCache();
        _crossfadePreHasBitmap = false;
        _crossfadePostHasBitmap = false;
        _crossfadePreBitmap = null;
        _crossfadePostBitmap = null;
        _crossfadePrePixels = [];
        _crossfadePostPixels = [];
        _crossfadePreDest = default;
        _crossfadePostDest = default;
        _crossfadePreFillBgra = 0;
        _crossfadePostFillBgra = 0;
        _crossfadeFlags.Clear();
    }

    /// <summary>
    /// 前ファイル／前回セッションの重ね・帯差分・パッチ用バッファをすべて破棄する。
    /// </summary>
    private void DiscardCrossfadeRenderCaches()
    {
        DiscardCrossfadeOverlapBitmaps();
        _crossfadeOverlayThrottleTimer.Stop();
        _crossfadeOverlayRebuildPending = false;
        _crossfadeWavePreviewTimer.Stop();
        _crossfadeWaveBandPrevValid = false;
        _crossfadeWaveBandPrevHasPre = false;
        _crossfadeWaveBandPrevHasPost = false;
        _crossfadePatchScratchMins = [];
        _crossfadePatchScratchMaxs = [];
    }

    /// <summary>ホストからファイル切替・クロスフェード開始時に呼べる完全クリア。</summary>
    public void ClearCrossfadeCaches() => DiscardCrossfadeRenderCaches();

    private void HoldCrossfadeWaveRedraw(bool hold)
    {
        _crossfadeWaveRedrawHeld = hold;
        if (hold)
        {
            _crossfadeWavePreviewTimer.Stop();
        }
    }

    private void ScheduleCrossfadeWavePreview(bool immediate = false)
    {
        if (_crossfadeWaveRedrawHeld && !immediate)
        {
            return;
        }

        _crossfadeWavePreviewTimer.Stop();
        if (immediate || _crossfadeDraft is not { IsActive: true })
        {
            RebuildCrossfadeWavePreview();
            return;
        }

        // デバウンス待ち中は重ねをライトのまま。
        _crossfadeOverlapQuality = CrossfadeOverlapQuality.Light;
        _crossfadeWavePreviewTimer.Start();
    }

    private void RebuildCrossfadeWavePreview()
    {
        // 操作終了後：重ねを詳細品質で一度描き直し、本体はクロス帯差分（または全体）。
        _crossfadeOverlapQuality = CrossfadeOverlapQuality.Detailed;
        InvalidateCrossfadeOverlapCache();
        InvalidatePlayheadLayer();

        if (!TryRasterizeCrossfadeWaveBandsIncremental())
        {
            InvalidateWaveform();
        }

        InvalidateVisual();
        CrossfadeWavePreviewRebuilt?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>旗ドラッグ中、または 0.5 秒デバウンス待ち、または品質が Light のとき軽い重ね。</summary>
    private bool UseLightCrossfadeOverlap =>
        IsCrossfadeDraftDragging
        || _crossfadeWavePreviewTimer.IsEnabled
        || _crossfadeOverlapQuality == CrossfadeOverlapQuality.Light;

    /// <summary>
    /// 既存ビットマップ上で、前回と今回のプリ／ポスト描画帯の和集合だけ再ラスタする。
    /// </summary>
    private bool TryRasterizeCrossfadeWaveBandsIncremental()
    {
        if (_document is null
            || _waveBitmap is null
            || _waveDirty
            || _wavePixelWidth <= 1
            || _wavePixelHeight <= 0
            || SpectrogramVisible
            || LoudnessVisible
            || SeekAndSelectOnly
            || _liveRecording)
        {
            return false;
        }

        if (_crossfadeDraft is not { IsActive: true } draft)
        {
            _crossfadeWaveBandPrevValid = false;
            return false;
        }

        var quantStart = _waveViewStart;
        var bmpSpan = _waveViewSpan;
        var width = _wavePixelWidth;
        var height = _wavePixelHeight;
        if (bmpSpan <= 0 || width <= 1)
        {
            return false;
        }

        var any = false;
        var px0 = 0;
        var px1 = 0;
        void Accrue(long a0, long a1)
        {
            if (a1 <= a0)
            {
                return;
            }

            var x0 = (int)Math.Floor((a0 - quantStart) / bmpSpan * width);
            var x1 = (int)Math.Ceiling((a1 - quantStart) / bmpSpan * width) + 1;
            x0 = Math.Clamp(x0, 0, width);
            x1 = Math.Clamp(x1, 0, width);
            if (x1 <= x0)
            {
                return;
            }

            if (!any)
            {
                px0 = x0;
                px1 = x1;
                any = true;
            }
            else
            {
                px0 = Math.Min(px0, x0);
                px1 = Math.Max(px1, x1);
            }
        }

        if (_crossfadeWaveBandPrevValid)
        {
            if (_crossfadeWaveBandPrevHasPre)
            {
                Accrue(_crossfadeWaveBandPrevPre0, _crossfadeWaveBandPrevPre1);
            }

            if (_crossfadeWaveBandPrevHasPost)
            {
                Accrue(_crossfadeWaveBandPrevPost0, _crossfadeWaveBandPrevPost1);
            }
        }

        if (draft.PreCrossfadeFrames > 0)
        {
            Accrue(draft.PreDrawStartFrame, draft.EndFrame);
        }

        if (draft.PostCrossfadeFrames > 0)
        {
            Accrue(draft.PostDrawRange.StartFrame, draft.PostDrawRange.EndFrame);
        }

        if (!any)
        {
            RememberCrossfadeWaveBands(draft);
            return true;
        }

        px0 = Math.Max(0, px0 - 1);
        px1 = Math.Min(width, px1 + 1);
        if (px1 <= px0)
        {
            RememberCrossfadeWaveBands(draft);
            return true;
        }

        var dpi = UiDpi.Get(this);
        var scaleX = Math.Max(1e-6, dpi.DpiScaleX);
        var scaleY = Math.Max(1e-6, dpi.DpiScaleY);
        ClearWavePixelColumns(px0, px1, width, height);
        RasterizeWaveformPixels(px0, px1, width, height, scaleX, scaleY, quantStart, bmpSpan);
        var bandW = px1 - px0;
        // WritePixels(Array) の offset は要素数（内部で ×4 される）。バイト数で渡すと四倍ずれてバッファ不足になる。
        _waveBitmap.WritePixels(
            new Int32Rect(px0, 0, bandW, height),
            _wavePixels,
            width * 4,
            px0);
        MarkInvertDirty();
        RememberCrossfadeWaveBands(draft);
        InvalidateStaticLayer();
        return true;
    }

    private void ClearWavePixelColumns(int x0, int x1, int width, int height)
    {
        if (x1 <= x0 || width <= 0 || height <= 0 || _wavePixels.Length < width * height)
        {
            return;
        }

        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            Array.Clear(_wavePixels, row + x0, x1 - x0);
        }
    }

    private void RememberCrossfadeWaveBands(CrossfadeEditState? draft)
    {
        if (draft is not { IsActive: true })
        {
            _crossfadeWaveBandPrevValid = false;
            return;
        }

        _crossfadeWaveBandPrevHasPre = draft.PreCrossfadeFrames > 0;
        _crossfadeWaveBandPrevHasPost = draft.PostCrossfadeFrames > 0;
        _crossfadeWaveBandPrevPre0 = draft.PreDrawStartFrame;
        _crossfadeWaveBandPrevPre1 = draft.EndFrame;
        _crossfadeWaveBandPrevPost0 = draft.PostDrawRange.StartFrame;
        _crossfadeWaveBandPrevPost1 = draft.PostDrawRange.EndFrame;
        _crossfadeWaveBandPrevValid = true;
    }

    /// <summary>
    /// ピーク用: 元波形だけにフェード（プリ＝out→0、ポスト＝in←0）。重ね側は描画レイヤ任せ。
    /// </summary>
    private bool TryGetCrossfadeBaseDisplaySample(long frame, int channel, out float sample)
    {
        if (_crossfadeDraft is not { IsActive: true } draft)
        {
            sample = 0;
            return false;
        }

        return TryGetCrossfadeBaseDisplaySample(frame, channel, LoopFadePreview.From(draft), draft, out sample);
    }

    private bool TryGetCrossfadeBaseDisplaySample(
        long frame,
        int channel,
        in LoopFadePreview preview,
        CrossfadeEditState draft,
        out float sample)
    {
        sample = 0;
        if (_document is null
            || channel < 0
            || channel >= _document.Channels
            || frame < 0
            || frame >= _document.FrameCount)
        {
            return false;
        }

        if (!preview.TryCrossfadeMix(frame, out _, out _, out var outGain, out var inGain))
        {
            return false;
        }

        var local = _document.Interleaved[(int)frame * _document.Channels + channel];
        if (draft.PostCrossfadeFrames > 0
            && frame >= draft.StartFrame
            && frame < draft.StartFrame + draft.PostCrossfadeFrames)
        {
            sample = local * inGain;
            return true;
        }

        sample = local * outGain;
        return true;
    }

    private bool TryMixCrossfadeBaseDisplayFrame(long frame, int channels, out float mixed) =>
        TryMixCrossfadeBaseDisplayFrame(frame, channels, null, out mixed);

    private bool TryMixCrossfadeBaseDisplayFrame(
        long frame,
        int channels,
        LoopFadePreview? preview,
        out float mixed)
    {
        mixed = 0;
        if (_crossfadeDraft is not { IsActive: true } draft)
        {
            return false;
        }

        var fade = preview ?? LoopFadePreview.From(draft);
        double sum = 0;
        for (var ch = 0; ch < channels; ch++)
        {
            if (!TryGetCrossfadeBaseDisplaySample(frame, ch, fade, draft, out var sample))
            {
                return false;
            }

            sum += sample;
        }

        mixed = (float)(sum / Math.Max(1, channels));
        return true;
    }

}
