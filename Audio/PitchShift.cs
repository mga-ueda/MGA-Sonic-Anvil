using System.Runtime.InteropServices;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.Audio;

/// <summary>Signalsmith Stretch によるピッチシフト。既定は長さ据え置き。</summary>
internal static class PitchShift
{
    public const int MinSemitones = -24;
    public const int MaxSemitones = 24;
    public const int OctaveSemitones = 12;

    public static int Snap(int semitones) => Math.Clamp(semitones, MinSemitones, MaxSemitones);

    public static bool IsNoOp(int semitones) => Snap(semitones) == 0;

    public static double Ratio(int semitones) => Math.Pow(2, Snap(semitones) / 12d);

    public static int NudgeStep(bool octave) => octave ? OctaveSemitones : 1;

    public static int DestFrameCount(int sourceFrames, int semitones, bool timeStretch)
    {
        sourceFrames = Math.Max(0, sourceFrames);
        if (timeStretch || IsNoOp(semitones) || sourceFrames == 0)
        {
            return sourceFrames;
        }

        return Math.Max(1, (int)Math.Round(sourceFrames / Ratio(semitones)));
    }

    public static float[] Apply(
        float[] interleaved,
        int channels,
        int sampleRate,
        int semitones,
        bool timeStretch = true,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        semitones = Snap(semitones);
        if (interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (IsNoOp(semitones))
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        var frames = interleaved.Length / channels;
        if (timeStretch)
        {
            return StretchExact(interleaved, frames, frames, channels, sampleRate, semitones, progress);
        }

        var destFrames = DestFrameCount(frames, semitones, timeStretch: false);
        return FormatConvert.ResampleToFrameCount(interleaved, channels, sampleRate, destFrames, progress);
    }

    internal static float[] StretchExact(
        float[] interleaved,
        int inputFrames,
        int outputFrames,
        int channels,
        int sampleRate,
        float semitones,
        IProgress<double>? progress,
        string? failedMessage = null)
    {
        var dest = new float[outputFrames * channels];
        SignalsmithStretchNative.ProgressCallback? callback = progress is null
            ? null
            : (_, value) => progress.Report(value);
        var ok = SignalsmithStretchNative.PitchExact(
            interleaved,
            inputFrames,
            dest,
            outputFrames,
            channels,
            sampleRate,
            semitones,
            callback,
            IntPtr.Zero);
        GC.KeepAlive(callback);
        if (ok == 0)
        {
            throw new InvalidOperationException(failedMessage ?? UiStrings.ErrorPitchShiftFailed);
        }

        return dest;
    }

    /// <summary>
    /// 時間変化ピッチ。既定は長さ据え置き。timeStretch=false なら再生速度式（長さが変わる）。
    /// <paramref name="lightweight"/> 時は間引きプレビュー経路（音質は落ちる）。
    /// </summary>
    public static float[] ApplyCurve(
        float[] interleaved,
        int channels,
        int sampleRate,
        ReadOnlySpan<float> semitonesPerFrame,
        bool timeStretch = true,
        IProgress<double>? progress = null,
        bool lightweight = false) =>
        ApplyCurveCore(
            interleaved,
            channels,
            sampleRate,
            semitonesPerFrame,
            timeStretch,
            progress,
            lightweight,
            smoothTable: true);

    /// <param name="smoothTable">false のとき半音テーブルを均さない（軽量プレビューの間引き後用）。</param>
    private static float[] ApplyCurveCore(
        float[] interleaved,
        int channels,
        int sampleRate,
        ReadOnlySpan<float> semitonesPerFrame,
        bool timeStretch,
        IProgress<double>? progress,
        bool lightweight,
        bool smoothTable)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var frames = interleaved.Length / channels;
        if (frames < 1 || interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (semitonesPerFrame.Length < frames)
        {
            throw new ArgumentException("Semitone table shorter than frame count.", nameof(semitonesPerFrame));
        }

        var table = new float[frames];
        var allZero = true;
        var maxAbs = 0f;
        for (var i = 0; i < frames; i++)
        {
            table[i] = SnapFloat(semitonesPerFrame[i]);
            var abs = Math.Abs(table[i]);
            if (abs > 1e-4f)
            {
                allZero = false;
            }

            if (abs > maxAbs)
            {
                maxAbs = abs;
            }
        }

        if (allZero)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (lightweight)
        {
            // プレビュー用。無変化なら通常品質へフォールバック（相殺・平滑潰れの保険）。
            var light = ApplyCurveLightweight(interleaved, channels, sampleRate, table, timeStretch, progress);
            if (maxAbs >= 0.5f
                && light.Length == interleaved.Length
                && IsNearlyIdentical(interleaved, light))
            {
                return ApplyCurveCore(
                    interleaved,
                    channels,
                    sampleRate,
                    table,
                    timeStretch,
                    progress,
                    lightweight: false,
                    smoothTable: true);
            }

            return light;
        }

        // ブラシの微小ジッタを均し、ほぼ定数なら exact／リサンプル経路へ。
        if (smoothTable)
        {
            SmoothSemitoneTable(table, sampleRate);
        }

        if (IsConstantTable(table, frames, out var constant))
        {
            var semi = (int)Math.Clamp(MathF.Round(constant), MinSemitones, MaxSemitones);
            if (timeStretch)
            {
                return StretchExact(interleaved, frames, frames, channels, sampleRate, constant, progress);
            }

            return Apply(interleaved, channels, sampleRate, semi, timeStretch: false, progress);
        }

        if (!timeStretch)
        {
            return ApplyCurveByRate(interleaved, channels, table, progress);
        }

        var dest = new float[frames * channels];
        SignalsmithStretchNative.ProgressCallback? callback = progress is null
            ? null
            : (_, value) => progress.Report(value);
        var ok = SignalsmithStretchNative.PitchCurve(
            interleaved,
            dest,
            frames,
            channels,
            sampleRate,
            table,
            callback,
            IntPtr.Zero);
        GC.KeepAlive(callback);
        if (ok == 0)
        {
            throw new InvalidOperationException(UiStrings.ErrorPitchShiftFailed);
        }

        progress?.Report(1);
        return dest;
    }

    /// <summary>軽量プレビューの間引き倍率。Signalsmith を短いバッファで回して高速化する。</summary>
    private const int LightweightDecimation = 3;

    /// <summary>
    /// 間引き後の最低フレーム数。これ未満だと Signalsmith PitchCurve が実質無変化になることがある。
    /// 短い選択はそもそも軽いので通常品質のまま処理する。
    /// </summary>
    private const int LightweightMinShortFrames = 8192;

    /// <summary>
    /// プレビュー用の簡易ピッチ。
    /// timeStretch=false: テープ速度式（長さ変化）。
    /// timeStretch=true: 間引き → 通常の長さ据え置きピッチ（ピッチ＋時間伸縮を一発）→ 線形で元サンプル数へ。
    /// ※「速度変更→ピッチ0尺戻し」は尺は揃ってもテンポ補正が実耳で不足しやすい。
    /// ※「速度変更→線形／OLA尺戻し」はピッチ相殺のため使わない。
    /// </summary>
    private static float[] ApplyCurveLightweight(
        float[] interleaved,
        int channels,
        int sampleRate,
        ReadOnlySpan<float> semitonesPerFrame,
        bool timeStretch,
        IProgress<double>? progress)
    {
        var frames = interleaved.Length / channels;
        if (!timeStretch)
        {
            if (IsConstantTable(semitonesPerFrame, frames, out var constant))
            {
                var semi = (int)Math.Clamp(MathF.Round(constant), MinSemitones, MaxSemitones);
                if (IsNoOp(semi))
                {
                    progress?.Report(1);
                    return (float[])interleaved.Clone();
                }

                var destFrames = DestFrameCount(frames, semi, timeStretch: false);
                return ResampleLinearToFrameCount(interleaved, channels, destFrames, progress);
            }

            return ApplyCurveByRate(interleaved, channels, semitonesPerFrame, progress);
        }

        // 長さ据え置き: 本物のピッチ＋時間伸縮を短いバッファで実行する。
        return ApplyCurveLightweightDecimated(
            interleaved,
            channels,
            sampleRate,
            semitonesPerFrame,
            progress);
    }

    /// <summary>
    /// 間引き → 通常 ApplyCurve（長さ据え置き＝ピッチ＋タイムストレッチ）→ 線形で元のフレーム数へ。
    /// 間引き後が短すぎる／間引き余地が無いときはフル品質（短い選択はもともと軽い）。
    /// </summary>
    private static float[] ApplyCurveLightweightDecimated(
        float[] interleaved,
        int channels,
        int sampleRate,
        ReadOnlySpan<float> semitonesPerFrame,
        IProgress<double>? progress)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var frames = interleaved.Length / channels;
        var decim = LightweightDecimation;
        var shortRate = Math.Max(8000, sampleRate / decim);
        var shortFrames = Math.Max(64, (int)Math.Round(frames * (double)shortRate / sampleRate));
        if (shortFrames >= frames - 8 || shortFrames < LightweightMinShortFrames)
        {
            return ApplyCurveCore(
                interleaved,
                channels,
                sampleRate,
                semitonesPerFrame,
                timeStretch: true,
                progress,
                lightweight: false,
                smoothTable: true);
        }

        IProgress<double>? downProgress = progress is null
            ? null
            : new ProgressScale(progress, 0, 0.15);
        var down = ResampleLinearToFrameCount(interleaved, channels, shortFrames, downProgress);
        var shortTable = new float[shortFrames];
        var last = Math.Max(1, shortFrames - 1);
        for (var i = 0; i < shortFrames; i++)
        {
            var srcIndex = (int)Math.Round(i * (frames - 1) / (double)last);
            shortTable[i] = semitonesPerFrame[Math.Clamp(srcIndex, 0, frames - 1)];
        }

        // 間引き後は平滑化しない（短いテーブルでカーブが潰れるのを防ぐ）。
        IProgress<double>? pitchProgress = progress is null
            ? null
            : new ProgressScale(progress, 0.15, 0.85);
        var pitchedShort = ApplyCurveCore(
            down,
            channels,
            shortRate,
            shortTable,
            timeStretch: true,
            pitchProgress,
            lightweight: false,
            smoothTable: false);

        // 長さ据え置きピッチの結果を線形で元フレーム数へ（ここではもうピッチを付けない）。
        IProgress<double>? upProgress = progress is null
            ? null
            : new ProgressScale(progress, 0.85, 1);
        return ResampleLinearToFrameCount(pitchedShort, channels, frames, upProgress);
    }

    /// <summary>既存 IProgress の区間へ 0..1 を写す。背景スレッドでも SyncContext を汚さない。</summary>
    private sealed class ProgressScale(IProgress<double> inner, double start, double end) : IProgress<double>
    {
        public void Report(double value)
        {
            var t = Math.Clamp(value, 0, 1);
            inner.Report(start + (end - start) * t);
        }
    }

    /// <summary>診断用: 速度変更のみ（ピッチ付き・長さ変化）。</summary>
    internal static float[] LightweightPitchRateOnly(
        float[] interleaved,
        int channels,
        int sampleRate,
        ReadOnlySpan<float> semitonesPerFrame,
        IProgress<double>? progress = null)
    {
        _ = sampleRate;
        var frames = interleaved.Length / channels;
        if (IsConstantTable(semitonesPerFrame, frames, out var constant))
        {
            var semi = (int)Math.Clamp(MathF.Round(constant), MinSemitones, MaxSemitones);
            if (IsNoOp(semi))
            {
                return (float[])interleaved.Clone();
            }

            return ResampleLinearToFrameCount(
                interleaved,
                channels,
                DestFrameCount(frames, semi, timeStretch: false),
                progress);
        }

        return ApplyCurveByRate(interleaved, channels, semitonesPerFrame, progress);
    }

    /// <summary>
    /// 診断用: 旧・誤りの第2段。入力ホップ≠出力ホップの OLA は実質レート変更でピッチを戻す。
    /// </summary>
    internal static float[] LightweightTimeStretchOlaCanceling(
        float[] interleaved,
        int channels,
        int sampleRate,
        int destFrames,
        IProgress<double>? progress = null)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var srcFrames = interleaved.Length / channels;
        destFrames = Math.Max(1, destFrames);
        if (srcFrames < 4 || srcFrames == destFrames)
        {
            return ResampleLinearToFrameCount(interleaved, channels, destFrames, progress);
        }

        var grain = Math.Clamp(sampleRate / 80, 256, 2048);
        if ((grain & 1) != 0)
        {
            grain++;
        }

        var hopOut = Math.Max(64, grain / 4);
        var half = grain / 2;
        var hopIn = hopOut * (srcFrames / (double)destFrames);
        var window = new float[grain];
        var denom = Math.Max(1, grain - 1);
        for (var i = 0; i < grain; i++)
        {
            window[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / denom));
        }

        var dest = new float[destFrames * channels];
        var weight = new float[destFrames];
        double srcCenter = half;
        for (var outCenter = 0; outCenter < destFrames; outCenter += hopOut)
        {
            for (var j = 0; j < grain; j++)
            {
                var outPos = outCenter - half + j;
                if ((uint)outPos >= (uint)destFrames)
                {
                    continue;
                }

                var w = window[j];
                if (w <= 1e-8f)
                {
                    continue;
                }

                AccumulateLinearFrame(
                    dest,
                    weight,
                    interleaved,
                    channels,
                    srcFrames,
                    outPos,
                    srcCenter + (j - half),
                    w);
            }

            srcCenter += hopIn;
        }

        for (var i = 0; i < destFrames; i++)
        {
            var w = weight[i];
            if (w <= 1e-6f)
            {
                var srcIndex = (int)Math.Clamp(
                    Math.Round(i * (srcFrames - 1) / (double)Math.Max(1, destFrames - 1)),
                    0,
                    srcFrames - 1);
                var s = srcIndex * channels;
                var d = i * channels;
                for (var c = 0; c < channels; c++)
                {
                    dest[d + c] = interleaved[s + c];
                }

                continue;
            }

            var inv = 1f / w;
            var o = i * channels;
            for (var c = 0; c < channels; c++)
            {
                dest[o + c] *= inv;
            }
        }

        progress?.Report(1);
        return dest;
    }

    private static void AccumulateLinearFrame(
        float[] dest,
        float[] weight,
        float[] src,
        int channels,
        int srcFrames,
        int outPos,
        double srcPos,
        float gain)
    {
        weight[outPos] += gain;
        var d = outPos * channels;
        if (srcPos <= 0)
        {
            for (var c = 0; c < channels; c++)
            {
                dest[d + c] += src[c] * gain;
            }

            return;
        }

        if (srcPos >= srcFrames - 1)
        {
            var last = (srcFrames - 1) * channels;
            for (var c = 0; c < channels; c++)
            {
                dest[d + c] += src[last + c] * gain;
            }

            return;
        }

        var i0 = (int)srcPos;
        var frac = (float)(srcPos - i0);
        var s0 = i0 * channels;
        var s1 = (i0 + 1) * channels;
        for (var c = 0; c < channels; c++)
        {
            dest[d + c] += (src[s0 + c] + (src[s1 + c] - src[s0 + c]) * frac) * gain;
        }
    }

    /// <summary>テスト用ラッパ。</summary>
    internal static float[] ResampleLinearToFrameCountPublic(
        float[] interleaved,
        int channels,
        int destFrames) =>
        ResampleLinearToFrameCount(interleaved, channels, destFrames, progress: null);

    /// <summary>線形補間だけの長さ合わせ。プレビュー用（sinc より大幅に速い）。</summary>
    private static float[] ResampleLinearToFrameCount(
        float[] interleaved,
        int channels,
        int destFrames,
        IProgress<double>? progress)
    {
        channels = Math.Max(1, channels);
        var srcFrames = interleaved.Length / channels;
        destFrames = Math.Max(1, destFrames);
        if (srcFrames <= 0 || interleaved.Length < channels)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        if (srcFrames == destFrames)
        {
            progress?.Report(1);
            return (float[])interleaved.Clone();
        }

        var dest = new float[destFrames * channels];
        var step = srcFrames / (double)destFrames;
        var reportEvery = Math.Max(1, destFrames / 100);
        for (var o = 0; o < destFrames; o++)
        {
            var src = Math.Min(srcFrames - 1, o * step);
            var i0 = (int)src;
            var frac = (float)(src - i0);
            var i1 = Math.Min(i0 + 1, srcFrames - 1);
            var s0 = i0 * channels;
            var s1 = i1 * channels;
            var d = o * channels;
            for (var c = 0; c < channels; c++)
            {
                dest[d + c] = interleaved[s0 + c] + (interleaved[s1 + c] - interleaved[s0 + c]) * frac;
            }

            if (progress is not null && ((o + 1) % reportEvery == 0 || o + 1 == destFrames))
            {
                progress.Report((o + 1) / (double)destFrames);
            }
        }

        progress?.Report(1);
        return dest;
    }

    /// <summary>
    /// タイムストレッチなし再生のシーク用。出力フレーム → 元ソースフレーム（範囲内オフセット）。
    /// ApplyCurve(..., timeStretch:false) と同じ前処理・読み進め方にする。
    /// </summary>
    /// <param name="smoothTable">通常品質は true、軽量プレビューの非ストレッチは false。</param>
    internal static int[] BuildNoTimeStretchSourceMap(
        ReadOnlySpan<float> semitonesPerFrame,
        int sampleRate,
        bool smoothTable = true)
    {
        var frames = semitonesPerFrame.Length;
        if (frames < 1)
        {
            return [0];
        }

        var table = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            table[i] = SnapFloat(semitonesPerFrame[i]);
        }

        if (smoothTable)
        {
            SmoothSemitoneTable(table, Math.Max(1, sampleRate));
        }

        if (IsConstantTable(table, frames, out var constant))
        {
            var semi = (int)Math.Clamp(MathF.Round(constant), MinSemitones, MaxSemitones);
            var destFrames = DestFrameCount(frames, semi, timeStretch: false);
            return BuildLinearResampleSourceMap(frames, destFrames);
        }

        return BuildRateOutputSourceMap(table);
    }

    /// <summary>ResampleLinearToFrameCount と同じソース位置。</summary>
    private static int[] BuildLinearResampleSourceMap(int sourceFrames, int destFrames)
    {
        sourceFrames = Math.Max(1, sourceFrames);
        destFrames = Math.Max(1, destFrames);
        var map = new int[destFrames];
        var step = sourceFrames / (double)destFrames;
        for (var o = 0; o < destFrames; o++)
        {
            map[o] = Math.Clamp((int)Math.Min(sourceFrames - 1, o * step), 0, sourceFrames - 1);
        }

        return map;
    }

    /// <summary>ApplyCurveByRate と同じ読み進めでの出力→ソース対応。</summary>
    private static int[] BuildRateOutputSourceMap(ReadOnlySpan<float> semitonesPerFrame)
    {
        var frames = semitonesPerFrame.Length;
        double outAcc = 0;
        for (var i = 0; i < frames; i++)
        {
            outAcc += 1d / Math.Pow(2, semitonesPerFrame[i] / 12d);
        }

        var destFrames = Math.Max(1, (int)Math.Round(outAcc));
        var map = new int[destFrames];
        double src = 0;
        for (var o = 0; o < destFrames; o++)
        {
            var i0 = src >= frames - 1 ? frames - 1 : (int)src;
            map[o] = Math.Clamp(i0, 0, frames - 1);
            if (src < frames - 1)
            {
                var semi = semitonesPerFrame[map[o]];
                src += Math.Pow(2, semi / 12d);
            }
        }

        return map;
    }

    /// <summary>
    /// タイムストレッチなしの時間変化ピッチ。テープ速度と同じく、半音に応じた速度で読み進める。
    /// </summary>
    private static float[] ApplyCurveByRate(
        float[] interleaved,
        int channels,
        ReadOnlySpan<float> semitonesPerFrame,
        IProgress<double>? progress)
    {
        channels = Math.Max(1, channels);
        var frames = interleaved.Length / channels;
        double outAcc = 0;
        for (var i = 0; i < frames; i++)
        {
            outAcc += 1d / Math.Pow(2, semitonesPerFrame[i] / 12d);
        }

        var destFrames = Math.Max(1, (int)Math.Round(outAcc));
        var dest = new float[destFrames * channels];
        double src = 0;
        for (var o = 0; o < destFrames; o++)
        {
            if (src >= frames - 1)
            {
                var last = (frames - 1) * channels;
                for (var c = 0; c < channels; c++)
                {
                    dest[o * channels + c] = interleaved[last + c];
                }

                continue;
            }

            var i0 = (int)src;
            var frac = (float)(src - i0);
            var i1 = Math.Min(i0 + 1, frames - 1);
            var s0 = i0 * channels;
            var s1 = i1 * channels;
            var d = o * channels;
            for (var c = 0; c < channels; c++)
            {
                dest[d + c] = interleaved[s0 + c] + (interleaved[s1 + c] - interleaved[s0 + c]) * frac;
            }

            var semi = semitonesPerFrame[Math.Clamp(i0, 0, frames - 1)];
            src += Math.Pow(2, semi / 12d);
            if ((o & 4095) == 0)
            {
                progress?.Report(o / (double)destFrames);
            }
        }

        progress?.Report(1);
        return dest;
    }

    /// <summary>~8ms 移動平均で階段・ブラシノイズを抑える。テーブルが短いときは半径を抑える。</summary>
    private static void SmoothSemitoneTable(float[] table, int sampleRate)
    {
        if (table.Length < 3)
        {
            return;
        }

        // sampleRate 基準の ~4ms 半径。短いバッファでは全体平均＝カーブ潰れになるので上限を掛ける。
        var radius = Math.Clamp(sampleRate / 250, 2, 512);
        radius = Math.Min(radius, Math.Max(2, table.Length / 32));
        var copy = (float[])table.Clone();
        for (var i = 0; i < table.Length; i++)
        {
            var i0 = Math.Max(0, i - radius);
            var i1 = Math.Min(table.Length - 1, i + radius);
            double sum = 0;
            var count = i1 - i0 + 1;
            for (var j = i0; j <= i1; j++)
            {
                sum += copy[j];
            }

            table[i] = SnapFloat((float)(sum / count));
        }
    }

    /// <summary>ピッチ未適用の検出用。全体の相関が高く振幅差も小さければ「ほぼ同一」。</summary>
    private static bool IsNearlyIdentical(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
        {
            return false;
        }

        var n = a.Length;
        var step = Math.Max(1, n / 8000);
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0, sumDiff2 = 0;
        var count = 0;
        for (var i = 0; i < n; i += step)
        {
            double x = a[i];
            double y = b[i];
            var d = x - y;
            sumA += x;
            sumB += y;
            sumAA += x * x;
            sumBB += y * y;
            sumAB += x * y;
            sumDiff2 += d * d;
            count++;
        }

        if (count < 8)
        {
            return false;
        }

        var meanA = sumA / count;
        var meanB = sumB / count;
        var cov = sumAB / count - meanA * meanB;
        var varA = sumAA / count - meanA * meanA;
        var varB = sumBB / count - meanB * meanB;
        if (varA <= 1e-12 || varB <= 1e-12)
        {
            return sumDiff2 / count < 1e-8;
        }

        var corr = cov / Math.Sqrt(varA * varB);
        var rmsDiff = Math.Sqrt(sumDiff2 / count);
        return corr > 0.97 && rmsDiff < 0.02;
    }

    private static float SnapFloat(float semitones) =>
        Math.Clamp(semitones, MinSemitones, MaxSemitones);

    public static float PeakAbs(ReadOnlySpan<float> interleaved)
    {
        var peak = 0f;
        for (var i = 0; i < interleaved.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(interleaved[i]));
        }

        return peak;
    }

    /// <summary>
    /// 処理結果と元波形を両端でクロスフェードし、継ぎ目ノイズを抑える。
    /// </summary>
    public static void BlendEdgesWithOriginal(
        ReadOnlySpan<float> original,
        Span<float> processed,
        int channels,
        int edgeFrames)
    {
        channels = Math.Max(1, channels);
        if (original.Length != processed.Length || original.Length < channels || edgeFrames <= 0)
        {
            return;
        }

        var frames = original.Length / channels;
        var edge = Math.Min(Math.Max(1, edgeFrames), Math.Max(1, frames / 2));
        for (var frame = 0; frame < edge; frame++)
        {
            var t = (frame + 1) / (float)(edge + 1);
            var wet = t;
            var dry = 1f - wet;
            var index = frame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                var i = index + ch;
                processed[i] = original[i] * dry + processed[i] * wet;
            }
        }

        for (var frame = 0; frame < edge; frame++)
        {
            var t = (frame + 1) / (float)(edge + 1);
            var wet = 1f - t;
            var dry = t;
            var srcFrame = frames - 1 - frame;
            var index = srcFrame * channels;
            for (var ch = 0; ch < channels; ch++)
            {
                var i = index + ch;
                processed[i] = original[i] * dry + processed[i] * wet;
            }
        }
    }

    private static bool IsConstantTable(ReadOnlySpan<float> table, int frames, out float value)
    {
        value = SnapFloat(table[0]);
        for (var i = 1; i < frames; i++)
        {
            // 滑らか化後の微小差は定数扱い（exact 経路）。
            if (Math.Abs(SnapFloat(table[i]) - value) > 0.05f)
            {
                return false;
            }
        }

        return true;
    }
}

internal static class SignalsmithStretchNative
{
    private const string Dll = "SignalsmithStretch";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ProgressCallback(IntPtr user, float progress);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_pitch_exact_interleaved")]
    public static extern int PitchExact(
        float[] input,
        int inputFrames,
        float[] output,
        int outputFrames,
        int channels,
        int sampleRate,
        float semitones,
        ProgressCallback? progress,
        IntPtr user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_pitch_curve_interleaved")]
    public static extern int PitchCurve(
        float[] input,
        float[] output,
        int frames,
        int channels,
        int sampleRate,
        float[] semitonesPerFrame,
        ProgressCallback? progress,
        IntPtr user);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_create")]
    public static extern IntPtr RtCreate();

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_destroy")]
    public static extern void RtDestroy(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_set_semitones")]
    public static extern void RtSetSemitones(IntPtr handle, float semitones);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_reset")]
    public static extern void RtReset(IntPtr handle);

    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories)]
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, EntryPoint = "ss_rt_process")]
    public static extern int RtProcess(
        IntPtr handle,
        float[] interleaved,
        int frameOffset,
        int frames,
        int channels,
        int sampleRate);
}

/// <summary>
/// 再生出力に掛ける時間長据え置きのピッチ。半音 0 のときはバッファを触らない。
/// </summary>
internal sealed class PlaybackPitchShift : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public void SetSemitones(int semitones)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Ensure();
        SignalsmithStretchNative.RtSetSemitones(_handle, semitones);
    }

    /// <summary>未使用ならネイティブを作らない。トラック切替の 0 戻し用。</summary>
    public void ClearSemitones()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        SignalsmithStretchNative.RtSetSemitones(_handle, 0);
        SignalsmithStretchNative.RtReset(_handle);
    }

    public void ResetEngine()
    {
        if (_disposed || _handle == IntPtr.Zero)
        {
            return;
        }

        SignalsmithStretchNative.RtReset(_handle);
    }

    public void Process(float[] interleaved, int frameOffset, int frames, int channels, int sampleRate, int semitones)
    {
        if (_disposed || semitones == 0 || frames < 1 || channels < 1)
        {
            return;
        }

        Ensure();
        SignalsmithStretchNative.RtSetSemitones(_handle, semitones);
        SignalsmithStretchNative.RtProcess(_handle, interleaved, frameOffset, frames, channels, sampleRate);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            SignalsmithStretchNative.RtDestroy(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private void Ensure()
    {
        if (_handle == IntPtr.Zero)
        {
            _handle = SignalsmithStretchNative.RtCreate();
        }
    }
}
