using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Editing;

internal readonly record struct PitchCurveRenderResult(float[] AfterRange, float PeakAbs);

internal static partial class ProcessEdits
{
    /// <summary>
    /// ピッチカーブを範囲に適用。既定は長さ据え置き。timeStretch=false なら長さが変わる。
    /// クリップ時は documentGainScale&lt;1 で波形全体を下げる（長さ据え置き時）。
    /// <paramref name="precomputedAfterRange"/> があるときは ApplyCurve を再実行せず再利用する。
    /// </summary>
    public static IEditCommand? PitchCurve(
        AudioDocument document,
        WaveSelection range,
        PitchCurve curve,
        int edgeFadeMs = ClickGuard.DefaultFadeMilliseconds,
        float documentGainScale = 1f,
        IProgress<double>? progress = null,
        int channel = ChannelSolo.Off,
        int channelMask = 0,
        bool timeStretch = true,
        float[]? precomputedAfterRange = null)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, channel, channelMask);
        if (range.IsEmpty || curve.IsNoOp())
        {
            return null;
        }

        documentGainScale = Math.Clamp(documentGainScale, 1e-4f, 1f);
        edgeFadeMs = ClickGuard.ClampFadeMs(edgeFadeMs);
        progress?.Report(0);

        var beforeRange = document.CopyRange(range.StartFrame, range.Length);
        float[] afterRange;
        if (precomputedAfterRange is not null)
        {
            // 呼び出し側キャッシュを壊さないようコピー（ゲイン適用で書き換えるため）。
            afterRange = (float[])precomputedAfterRange.Clone();
            progress?.Report(1);
        }
        else
        {
            afterRange = RenderPitchCurveRange(
                    document,
                    range,
                    curve,
                    mask,
                    timeStretch,
                    edgeFadeMs,
                    progress)
                .AfterRange;
        }

        var curveCopy = curve.Clone();
        var gain = documentGainScale;
        var fade = edgeFadeMs;
        var stretch = timeStretch;
        var extra = UiStrings.FormatPitchCurveExtra(curve, documentGainScale, timeStretch);

        if (documentGainScale < 0.9999f && afterRange.Length == beforeRange.Length)
        {
            ChannelSamples.ApplyGain(afterRange, document.Channels, mask, documentGainScale);
            // 範囲外も含めドキュメント全体を同じ比率で下げる。
            var fullBefore = document.CopyRange(0, document.FrameCount);
            var fullAfter = (float[])fullBefore.Clone();
            ChannelSamples.ApplyGain(fullAfter, document.Channels, mask: 0, documentGainScale);
            var channels = Math.Max(1, document.Channels);
            var destOffset = (int)range.StartFrame * channels;
            Array.Copy(afterRange, 0, fullAfter, destOffset, afterRange.Length);

            progress?.Report(1);
            var command = new ReplaceRangeCommand(
                "Pitch Shift",
                0,
                fullBefore,
                fullAfter,
                document.Selection,
                WaveSelection.Empty,
                document.CursorFrame,
                range.StartFrame,
                UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Pitch Shift"),
                    document.SampleRate,
                    range.StartFrame,
                    range.EndFrame,
                    extra));
            AttachRangeReplay(
                command,
                document.SampleRate,
                range,
                (target, mapped) => PitchCurve(
                    target,
                    mapped,
                    curveCopy,
                    fade,
                    gain,
                    channelMask: mask,
                    timeStretch: stretch));
            command.Persist = HistoryRecipes.FromPitchCurve(
                document.SampleRate,
                range,
                curveCopy,
                fade,
                gain,
                channelMask: mask,
                timeStretch: stretch);
            return command;
        }

        if (documentGainScale < 0.9999f)
        {
            ChannelSamples.ApplyGain(afterRange, document.Channels, mask, documentGainScale);
        }

        progress?.Report(1);
        if (afterRange.Length == beforeRange.Length)
        {
            var replace = new ReplaceRangeCommand(
                "Pitch Shift",
                range.StartFrame,
                beforeRange,
                afterRange,
                document.Selection,
                WaveSelection.Empty,
                document.CursorFrame,
                range.StartFrame,
                UiStrings.EditHistoryRange(
                    UiStrings.EditHistoryName("Pitch Shift"),
                    document.SampleRate,
                    range.StartFrame,
                    range.EndFrame,
                    extra));
            AttachRangeReplay(
                replace,
                document.SampleRate,
                range,
                (target, mapped) => PitchCurve(
                    target,
                    mapped,
                    curveCopy,
                    fade,
                    gain,
                    channelMask: mask,
                    timeStretch: stretch));
            replace.Persist = HistoryRecipes.FromPitchCurve(
                document.SampleRate,
                range,
                curveCopy,
                fade,
                gain,
                channelMask: mask,
                timeStretch: stretch);
            return replace;
        }

        var oldFrames = range.Length;
        var newFrames = afterRange.Length / Math.Max(1, document.Channels);
        var markersAfter = MapMarkersThroughRange(document.SnapshotMarkers(), range.StartFrame, oldFrames, newFrames);
        var regionsAfter = MapRegionsThroughRange(document.SnapshotRegions(), range.StartFrame, oldFrames, newFrames);
        var loopAfter = MapSelectionThroughRange(document.SampleLoop, range.StartFrame, oldFrames, newFrames);
        var splice = new SpliceRangeCommand(
            "Pitch Shift",
            range.StartFrame,
            beforeRange,
            afterRange,
            document.Selection,
            WaveSelection.Empty,
            document.CursorFrame,
            range.StartFrame,
            document.SnapshotMarkers(),
            markersAfter,
            document.SampleLoop,
            loopAfter,
            document.SnapshotRegions(),
            regionsAfter,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Pitch Shift"),
                document.SampleRate,
                range.StartFrame,
                range.EndFrame,
                extra));
        AttachRangeReplay(
            splice,
            document.SampleRate,
            range,
            (target, mapped) => PitchCurve(
                target,
                mapped,
                curveCopy,
                fade,
                gain,
                channelMask: mask,
                timeStretch: stretch));
        splice.Persist = HistoryRecipes.FromPitchCurve(
            document.SampleRate,
            range,
            curveCopy,
            fade,
            gain,
            channelMask: mask,
            timeStretch: stretch);
        return splice;
    }

    /// <summary>
    /// プレビュー／ピーク見積／適用で共有するピッチ処理。ApplyCurve＋端ブレンドまで行い、ピークも返す。
    /// <paramref name="lightweight"/> はプレビュー用。本適用では false。
    /// </summary>
    public static PitchCurveRenderResult RenderPitchCurveRange(
        AudioDocument document,
        WaveSelection range,
        PitchCurve curve,
        int channelMask = 0,
        bool timeStretch = true,
        int edgeFadeMs = ClickGuard.DefaultFadeMilliseconds,
        IProgress<double>? progress = null,
        bool lightweight = false)
    {
        range = range.Clamp(document.FrameCount);
        var mask = EditMask(document, ChannelSolo.Off, channelMask);
        if (range.IsEmpty || curve.IsNoOp())
        {
            progress?.Report(1);
            return new PitchCurveRenderResult([], 0f);
        }

        edgeFadeMs = ClickGuard.ClampFadeMs(edgeFadeMs);
        progress?.Report(0);

        var table = curve.BuildSemitoneTable((int)range.Length);
        var beforeRange = document.CopyRange(range.StartFrame, range.Length);
        var blendEdges = timeStretch
            && table.Length > 0
            && Math.Abs(table[0]) < 0.25f
            && Math.Abs(table[^1]) < 0.25f;
        var edgeFrames = ClickGuard.FadeFrames(document.SampleRate, edgeFadeMs);
        float[] afterRange;
        float peak;
        if (ChannelSamples.IsScoped(mask, document.Channels))
        {
            var extracted = ChannelSamples.ExtractScope(beforeRange, document.Channels, mask);
            var processed = Audio.PitchShift.ApplyCurve(
                extracted.Samples,
                extracted.Channels,
                document.SampleRate,
                table,
                timeStretch,
                progress,
                lightweight);
            if (blendEdges && processed.Length == extracted.Samples.Length)
            {
                Audio.PitchShift.BlendEdgesWithOriginal(
                    extracted.Samples,
                    processed,
                    extracted.Channels,
                    edgeFrames);
            }

            peak = Audio.PitchShift.PeakAbs(processed);
            afterRange = (float[])beforeRange.Clone();
            ChannelSamples.WriteFitted(afterRange, document.Channels, mask, processed, extracted.Channels);
        }
        else
        {
            afterRange = Audio.PitchShift.ApplyCurve(
                beforeRange,
                document.Channels,
                document.SampleRate,
                table,
                timeStretch,
                progress,
                lightweight);
            if (blendEdges && afterRange.Length == beforeRange.Length)
            {
                Audio.PitchShift.BlendEdgesWithOriginal(
                    beforeRange,
                    afterRange,
                    document.Channels,
                    edgeFrames);
            }

            peak = Audio.PitchShift.PeakAbs(afterRange);
        }

        progress?.Report(1);
        return new PitchCurveRenderResult(afterRange, peak);
    }
}
