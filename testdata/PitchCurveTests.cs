using System.IO;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PitchCurveTests
{
    [Fact]
    public void Flat_IsConstantAndNoOpAtZero()
    {
        var zero = PitchCurve.Flat(0);
        Assert.True(zero.IsNoOp());
        Assert.True(zero.IsConstant(out var semi));
        Assert.Equal(0f, semi);

        var up = PitchCurve.Flat(5);
        Assert.False(up.IsNoOp());
        Assert.True(up.IsConstant(out semi));
        Assert.Equal(5f, semi);
    }

    [Fact]
    public void SnapSemitone_RoundsToInteger_SoftSnapZero_Attracts()
    {
        Assert.Equal(3f, PitchCurve.SnapSemitone(2.6f));
        Assert.Equal(-2f, PitchCurve.SnapSemitone(-1.6f));
        Assert.Equal(0f, PitchCurve.SoftSnapZero(0.2f));
        Assert.Equal(1.2f, PitchCurve.SoftSnapZero(1.2f), 3);
    }

    [Fact]
    public void QuantizeToScaleSteps_RoundsPointsAndUsesConstantSegments()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 1.4f),
                new PitchCurvePoint(0.5f, -2.6f),
                new PitchCurvePoint(1, 0.1f),
            ],
            [FadeShape.SCurve, FadeShape.Linear]);
        curve.QuantizeToScaleSteps();
        Assert.Equal(1f, curve.Points[0].Semitones);
        Assert.Equal(-3f, curve.Points[1].Semitones);
        Assert.Equal(0f, curve.Points[2].Semitones);
        Assert.All(curve.SegmentShapes, s => Assert.Equal(FadeShape.Constant, s));
        Assert.Equal(1f, curve.SampleAt(0.25), 3);
    }

    /// <summary>
    /// 音階スナップ ON→量子化→再生相当→OFF（連続形状に戻す）→再計算でもピッチが残ること。
    /// </summary>
    [Fact]
    public void SnapOnThenOff_RestoredContinuousShapes_StillPitches()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate / 2;
        var sine = MakeTone(440f, frames, rate);
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(0.5, -12.4f),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.SCurve, FadeShape.SCurve]);

        curve.QuantizeToScaleSteps();
        Assert.Equal(-12f, curve.Points[1].Semitones);
        Assert.All(curve.SegmentShapes, s => Assert.Equal(FadeShape.Constant, s));
        var tableOn = curve.BuildSemitoneTable(frames);
        var onLight = PitchShift.ApplyCurve(sine, channels, rate, tableOn, timeStretch: true, lightweight: true);
        var onHeavy = PitchShift.ApplyCurve(sine, channels, rate, tableOn, timeStretch: true, lightweight: false);

        // OFF 相当: 階段をやめ連続形状へ（点の半音はそのまま）。
        curve.DefaultShape = FadeShape.Linear;
        curve.SetAllSegmentShapes(FadeShape.Linear);
        Assert.False(curve.IsNoOp());
        var tableOff = curve.BuildSemitoneTable(frames);
        var offLight = PitchShift.ApplyCurve(sine, channels, rate, tableOff, timeStretch: true, lightweight: true);
        var offHeavy = PitchShift.ApplyCurve(sine, channels, rate, tableOff, timeStretch: true, lightweight: false);

        // Constant 階段は前半 0・後半 -12。Linear は中央付近が下がる。
        var baseHz = EstimateHzByZeroCrossings(sine, rate);
        var onRegion = frames * 3 / 5;
        var offRegion = frames * 2 / 5;
        var len = Math.Max(64, frames / 5);
        var onL = EstimateHzByZeroCrossings(onLight.AsSpan(onRegion, len).ToArray(), rate);
        var onH = EstimateHzByZeroCrossings(onHeavy.AsSpan(onRegion, len).ToArray(), rate);
        var offL = EstimateHzByZeroCrossings(offLight.AsSpan(offRegion, len).ToArray(), rate);
        var offH = EstimateHzByZeroCrossings(offHeavy.AsSpan(offRegion, len).ToArray(), rate);
        var dump = Path.Combine(Path.GetTempPath(), "pitch-snap-on-off.txt");
        File.WriteAllText(
            dump,
            $"base={baseHz:F1} onL={onL:F1} onH={onH:F1} offL={offL:F1} offH={offH:F1}");

        Assert.True(onL < baseHz * 0.8, $"snap-on light: {File.ReadAllText(dump)}");
        Assert.True(onH < baseHz * 0.8, $"snap-on heavy: {File.ReadAllText(dump)}");
        Assert.True(offL < baseHz * 0.8, $"snap-off light: {File.ReadAllText(dump)}");
        Assert.True(offH < baseHz * 0.8, $"snap-off heavy: {File.ReadAllText(dump)}");
    }

    /// <summary>
    /// 量子化で定数になったあと数値欄が古い 0 のままだと SetConstant(0) で潰れる、という退行を固定する。
    /// </summary>
    [Fact]
    public void QuantizeToConstant_StaleFlatZero_WouldWipe_UnlessSyncedFromCurve()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 4.6f),
                new PitchCurvePoint(0.5f, 5.2f),
                new PitchCurvePoint(1, 4.8f),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        curve.QuantizeToScaleSteps();
        Assert.True(curve.IsConstant(out var semi));
        Assert.Equal(5f, semi);

        // バグ再現: 古い欄 0 をカーブへ適用すると潰れる。
        curve.SetConstant(0f);
        Assert.True(curve.IsNoOp());

        // 正しい同期: 量子化後の定数を欄側へ合わせる前提なら潰さない。
        curve.SetConstant(semi);
        Assert.False(curve.IsNoOp());
        Assert.Equal(5f, curve.Points[0].Semitones);
    }

    [Fact]
    public void SetPointOutgoingShape_ChangesOnlyThatSegment()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(0.5f, 6),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        curve.SetPointOutgoingShape(0, FadeShape.SCurve);
        Assert.Equal(FadeShape.SCurve, curve.SegmentShapes[0]);
        Assert.Equal(FadeShape.Linear, curve.SegmentShapes[1]);
        Assert.Equal(FadeShape.SCurve, curve.OutgoingShapeOfPoint(0));
    }

    [Fact]
    public void LinearInterpolate_BetweenPoints()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(1, 12),
            ],
            [FadeShape.Linear]);
        Assert.Equal(0f, curve.SampleAt(0), 3);
        Assert.Equal(6f, curve.SampleAt(0.5), 3);
        Assert.Equal(12f, curve.SampleAt(1), 3);
    }

    /// <summary>ブラシ相当: 既存が SCurve でも挿入・間引き後は全区間リニア。</summary>
    [Fact]
    public void BrushUpsertAndSimplify_ForcesLinearSegments()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(1, 12),
            ],
            [FadeShape.SCurve],
            FadeShape.SCurve);

        curve.Upsert(0.25, 3f, segmentShape: FadeShape.Linear);
        curve.Upsert(0.5, 6f, segmentShape: FadeShape.Linear);
        curve.Upsert(0.75, 9f, segmentShape: FadeShape.Linear);
        Assert.All(curve.SegmentShapes, s => Assert.Equal(FadeShape.Linear, s));
        Assert.Equal(6f, curve.SampleAt(0.5), 3);

        curve.Simplify(semiTolerance: 0.08f, segmentShape: FadeShape.Linear);
        curve.SetAllSegmentShapes(FadeShape.Linear);
        Assert.Equal(FadeShape.Linear, curve.DefaultShape);
        Assert.All(curve.SegmentShapes, s => Assert.Equal(FadeShape.Linear, s));
        Assert.True(curve.Points.Count <= 3);
        Assert.Equal(6f, curve.SampleAt(0.5), 3);
    }

    [Fact]
    public void PitchCurve_DefaultsToLinear_NotFadeSCurve()
    {
        var curve = new PitchCurve();
        Assert.Equal(FadeShape.Linear, curve.DefaultShape);
        Assert.All(curve.SegmentShapes, s => Assert.Equal(FadeShape.Linear, s));
    }

    [Fact]
    public void BuildSemitoneTable_LengthMatches()
    {
        var curve = PitchCurve.Flat(-3);
        var table = curve.BuildSemitoneTable(100);
        Assert.Equal(100, table.Length);
        Assert.All(table, v => Assert.Equal(-3f, v));
    }

    /// <summary>
    /// タイムストレッチなし: 出力フレーム→ソース位置が速度式／線形リサンプルと一致すること。
    /// 単純な再生進捗の比例だと変化カーブで音とシークがずれる。
    /// </summary>
    [Fact]
    public void BuildNoTimeStretchSourceMap_MatchesRatePlayback()
    {
        const int rate = 48000;
        const int frames = 4000;
        var flat = PitchCurve.Flat(-12).BuildSemitoneTable(frames);
        var flatMap = PitchShift.BuildNoTimeStretchSourceMap(flat, rate, smoothTable: false);
        var flatOut = PitchShift.ApplyCurve(
            new float[frames], 1, rate, flat, timeStretch: false, lightweight: true);
        Assert.Equal(flatOut.Length, flatMap.Length);
        // -12: 出力は約2倍。出力の半分まで進むとソースも半分（各出力で 0.5 フレーム進む）。
        var midOut = flatMap.Length / 2;
        Assert.InRange(flatMap[midOut], frames / 2 - 8, frames / 2 + 8);

        var varying = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(0.5, -12),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear]).BuildSemitoneTable(frames);
        var varMap = PitchShift.BuildNoTimeStretchSourceMap(varying, rate, smoothTable: false);
        var varOut = PitchShift.ApplyCurve(
            new float[frames], 1, rate, varying, timeStretch: false, lightweight: true);
        Assert.Equal(varOut.Length, varMap.Length);

        // 比例マップとの差: 変化カーブでは中央付近でソース位置が比例から外れる。
        var proportional = (int)(midOut * (frames - 1.0) / Math.Max(1, varMap.Length - 1));
        var mappedMid = varMap[Math.Min(midOut, varMap.Length - 1)];
        Assert.True(
            Math.Abs(mappedMid - proportional) > 20,
            $"varying map should differ from linear proportion: map={mappedMid} prop={proportional}");

        // 単調非減少・範囲内。
        Assert.Equal(0, varMap[0]);
        Assert.InRange(varMap[^1], frames - 2, frames - 1);
        for (var i = 1; i < varMap.Length; i++)
        {
            Assert.True(varMap[i] >= varMap[i - 1], $"map not monotonic at {i}");
        }
    }

    [Fact]
    public void PackedRoundTrip_PreservesPointsAndShapes()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, -6),
                new PitchCurvePoint(0.5, 3),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.SCurve, FadeShape.Linear],
            FadeShape.SCurve);
        var restored = PitchCurve.FromPackedSamples(curve.ToPackedSamples(), curve.ToShapeIds(), FadeShape.SCurve);
        Assert.Equal(curve.Points.Count, restored.Points.Count);
        for (var i = 0; i < curve.Points.Count; i++)
        {
            Assert.Equal(curve.Points[i].T, restored.Points[i].T, 5);
            Assert.Equal(curve.Points[i].Semitones, restored.Points[i].Semitones, 3);
        }

        Assert.Equal(curve.SegmentShapes.Count, restored.SegmentShapes.Count);
        Assert.Equal(curve.SegmentShapes[0], restored.SegmentShapes[0]);
        Assert.Equal(curve.SegmentShapes[1], restored.SegmentShapes[1]);
    }

    [Fact]
    public void BlendEdgesWithOriginal_PreservesLengthAndSoftensEnds()
    {
        const int channels = 1;
        const int frames = 100;
        var original = new float[frames];
        var processed = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            original[i] = 0.25f;
            processed[i] = 1f;
        }

        PitchShift.BlendEdgesWithOriginal(original, processed, channels, edgeFrames: 10);
        Assert.Equal(frames, processed.Length);
        Assert.True(processed[0] < 1f);
        Assert.True(processed[0] > 0.25f);
        Assert.True(processed[^1] < 1f);
        Assert.Equal(1f, processed[frames / 2], 3);
    }

    [Fact]
    public void PeakAbs_FindsMaximum()
    {
        float[] samples = [0.1f, -0.8f, 0.4f];
        Assert.Equal(0.8f, PitchShift.PeakAbs(samples), 5);
    }

    [Fact]
    public void ApplyCurve_ConstantTable_PreservesLength()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = 2048;
        var sine = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            sine[i] = (float)Math.Sin(2 * Math.PI * 440 * i / rate) * 0.5f;
        }

        var table = PitchCurve.Flat(2).BuildSemitoneTable(frames);
        var pitched = PitchShift.ApplyCurve(sine, channels, rate, table);
        Assert.Equal(sine.Length, pitched.Length);
        Assert.True(PitchShift.PeakAbs(pitched) > 0.01f);
    }

    [Fact]
    public void ApplyCurve_Lightweight_TimeStretch_ChangesPitchAndKeepsLength()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate;
        const float hz = 440f;
        var sine = MakeTone(hz, frames, rate);
        var downTable = PitchCurve.Flat(-12).BuildSemitoneTable(frames);
        var upTable = PitchCurve.Flat(12).BuildSemitoneTable(frames);
        var down = PitchShift.ApplyCurve(sine, channels, rate, downTable, timeStretch: true, lightweight: true);
        var up = PitchShift.ApplyCurve(sine, channels, rate, upTable, timeStretch: true, lightweight: true);
        Assert.Equal(frames, down.Length);
        Assert.Equal(frames, up.Length);

        var baseHz = EstimateHzByZeroCrossings(sine, rate);
        var downHz = EstimateHzByZeroCrossings(down, rate);
        var upHz = EstimateHzByZeroCrossings(up, rate);
        Assert.InRange(baseHz, hz * 0.95, hz * 1.05);
        Assert.InRange(downHz, 220 * 0.85, 220 * 1.15);
        Assert.InRange(upHz, 880 * 0.85, 880 * 1.15);
    }

    /// <summary>
    /// ポイント／ブラシ相当の時間変化カーブでも軽量経路がピッチを動かすこと。
    /// 定数テーブルだけ通って変化カーブで無変化、という退行を防ぐ。
    /// </summary>
    [Fact]
    public void ApplyCurve_Lightweight_VaryingCurve_ChangesPitchInMiddle()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate;
        const float hz = 440f;
        var sine = MakeTone(hz, frames, rate);
        // 両端 0・中央 -12（ポイント編集の典型。端ブレンド条件も満たす）。
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(0.5, -12),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        var table = curve.BuildSemitoneTable(frames);

        var heavy = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: false);
        var light = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: true);
        Assert.Equal(frames, heavy.Length);
        Assert.Equal(frames, light.Length);

        // 中央 40% 付近だけ測る（両端は 0 半音）。
        var midStart = frames * 2 / 5;
        var midLen = frames / 5;
        var baseMid = EstimateHzByZeroCrossings(sine.AsSpan(midStart, midLen).ToArray(), rate);
        var heavyMid = EstimateHzByZeroCrossings(heavy.AsSpan(midStart, midLen).ToArray(), rate);
        var lightMid = EstimateHzByZeroCrossings(light.AsSpan(midStart, midLen).ToArray(), rate);

        // 入力との相関が高い＝ほぼ無加工。
        var corrLight = Correlation(sine, light);
        var dump = Path.Combine(Path.GetTempPath(), "pitch-lightweight-varying.txt");
        File.WriteAllText(
            dump,
            $"baseMid={baseMid:F1} heavyMid={heavyMid:F1} lightMid={lightMid:F1} corrLight={corrLight:F4}");

        Assert.InRange(baseMid, hz * 0.9, hz * 1.1);
        Assert.True(heavyMid < baseMid * 0.75, $"heavy should drop pitch, base={baseMid:F1} heavy={heavyMid:F1}");
        Assert.True(lightMid < baseMid * 0.75, $"lightweight varying curve must drop pitch, base={baseMid:F1} light={lightMid:F1} corr={corrLight:F3}");
        Assert.True(corrLight < 0.85, $"lightweight output too similar to input (corr={corrLight:F3})");
    }

    /// <summary>短い選択＋両端0の山形でも、軽量経路が中央ピッチを落とし切ること。</summary>
    [Fact]
    public void ApplyCurve_Lightweight_ShortSelection_VaryingCurve_StillPitches()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate / 10; // 100ms。間引き後に平滑半径が効きやすい長さ。
        const float hz = 440f;
        var sine = MakeTone(hz, frames, rate);
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(0.5, -12),
                new PitchCurvePoint(1, 0),
            ],
            [FadeShape.Linear, FadeShape.Linear]);
        var table = curve.BuildSemitoneTable(frames);
        var heavy = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: false);
        var light = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: true);
        var flatLight = PitchShift.ApplyCurve(
            sine, channels, rate, PitchCurve.Flat(-12).BuildSemitoneTable(frames), timeStretch: true, lightweight: true);
        Assert.Equal(frames, light.Length);
        var midStart = frames * 2 / 5;
        var midLen = Math.Max(64, frames / 5);
        var baseMid = EstimateHzByZeroCrossings(sine.AsSpan(midStart, midLen).ToArray(), rate);
        var heavyMid = EstimateHzByZeroCrossings(heavy.AsSpan(midStart, midLen).ToArray(), rate);
        var lightMid = EstimateHzByZeroCrossings(light.AsSpan(midStart, midLen).ToArray(), rate);
        var flatMid = EstimateHzByZeroCrossings(flatLight.AsSpan(midStart, midLen).ToArray(), rate);
        var dump = Path.Combine(Path.GetTempPath(), "pitch-lightweight-short.txt");
        File.WriteAllText(
            dump,
            $"base={baseMid:F1} heavy={heavyMid:F1} light={lightMid:F1} flatL={flatMid:F1} " +
            $"corrH={Correlation(sine, heavy):F3} corrL={Correlation(sine, light):F3}");
        // 短尺の変化カーブは ZC が不安定なことがあるので、無変化（高相関）でないことを主に見る。
        Assert.True(
            Correlation(sine, light) < 0.9 || lightMid < baseMid * 0.85,
            $"short lightweight must change audio or pitch, {File.ReadAllText(dump)}");
        Assert.True(Correlation(sine, flatLight) < 0.5 || flatMid < baseMid * 0.7, $"flat short: {File.ReadAllText(dump)}");
    }

    /// <summary>
    /// 軽量＋長さ据え置き: ピッチが変わり、かつ速度式だけより長さが元に戻っている（タイムストレッチ）。
    /// </summary>
    [Fact]
    public void Lightweight_TimeStretch_KeepsLength_AndDiffersFromRateOnly()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate;
        var sine = MakeTone(440f, frames, rate);
        var table = PitchCurve.Flat(-12).BuildSemitoneTable(frames);
        var rateOnly = PitchShift.LightweightPitchRateOnly(sine, channels, rate, table);
        Assert.True(rateOnly.Length > frames * channels, "rate-only must change length");

        var light = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: true);
        Assert.Equal(frames * channels, light.Length);

        var lightHz = EstimateHzByZeroCrossings(light, rate);
        Assert.InRange(lightHz, 220 * 0.8, 220 * 1.2);

        // 後半にもエネルギー（先頭だけ切った速度式の切り詰めではない）。
        var half = frames / 2;
        var rms1 = Rms(light.AsSpan(0, half));
        var rms2 = Rms(light.AsSpan(half, frames - half));
        var dump = Path.Combine(Path.GetTempPath(), "pitch-lightweight-timestretch.txt");
        File.WriteAllText(
            dump,
            $"lightHz={lightHz:F1} rms1={rms1:F4} rms2={rms2:F4} lightLen={light.Length} rateLen={rateOnly.Length}");
        Assert.True(rms2 > rms1 * 0.25, $"time stretch missing (tail quiet): {File.ReadAllText(dump)}");

        // 速度式のみを同じ長さに線形で揃えるとピッチが相殺される。軽量結果は相殺していない。
        var canceled = PitchShift.ResampleLinearToFrameCountPublic(rateOnly, channels, frames);
        var canceledHz = EstimateHzByZeroCrossings(canceled, rate);
        Assert.InRange(canceledHz, 400, 480);
        Assert.True(Math.Abs(lightHz - canceledHz) > 80, $"light should not equal canceled pitch: {File.ReadAllText(dump)}");
    }

    private static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0)
        {
            return 0;
        }

        double sum = 0;
        for (var i = 0; i < samples.Length; i++)
        {
            sum += samples[i] * samples[i];
        }

        return Math.Sqrt(sum / samples.Length);
    }

    private static double Correlation(float[] a, float[] b)
    {
        var n = Math.Min(a.Length, b.Length);
        double sumA = 0, sumB = 0, sumAA = 0, sumBB = 0, sumAB = 0;
        for (var i = 0; i < n; i++)
        {
            double x = a[i];
            double y = b[i];
            sumA += x;
            sumB += y;
            sumAA += x * x;
            sumBB += y * y;
            sumAB += x * y;
        }

        var meanA = sumA / n;
        var meanB = sumB / n;
        var cov = sumAB / n - meanA * meanB;
        var varA = sumAA / n - meanA * meanA;
        var varB = sumBB / n - meanB * meanB;
        if (varA <= 1e-20 || varB <= 1e-20)
        {
            return 0;
        }

        return cov / Math.Sqrt(varA * varB);
    }

    /// <summary>
    /// 速度変更後の「別ホップOLA尺戻し」が逆レートでピッチを相殺することを実測し、
    /// 現行の間引き+Signalsmith 経路は -12 半音を維持することを示す。
    /// </summary>
    [Fact]
    public void Lightweight_TwoStageOla_CancelsPitch_WhileDecimatedPathKeepsIt()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = rate;
        const float hz = 440f;
        var sine = MakeTone(hz, frames, rate);
        var table = PitchCurve.Flat(-12).BuildSemitoneTable(frames);

        // stage1: ピッチ下げ済み相当（220Hz・長さ2倍）の実データ。
        var stage1 = MakeTone(220f, frames * 2, rate);
        var stage1Hz = EstimateHzByZeroCrossings(stage1, rate);
        Assert.InRange(stage1Hz, 220 * 0.95, 220 * 1.05);

        // 線形で長さを半分に戻す = 2倍速 = ピッチも1オクターブ戻る（相殺の典型）。
        var canceledByLinear = PitchShift.ResampleLinearToFrameCountPublic(stage1, channels, frames);
        var canceledHz = EstimateHzByZeroCrossings(canceledByLinear, rate);
        Assert.Equal(frames, canceledByLinear.Length);
        Assert.InRange(canceledHz, hz * 0.9, hz * 1.1);

        var ola = PitchShift.LightweightTimeStretchOlaCanceling(stage1, channels, rate, frames);
        var olaHz = EstimateHzByZeroCrossings(ola, rate);
        Assert.Equal(frames, ola.Length);
        // 別ホップOLAも速度比でピッチを動かす（220のままではあってはならない）。
        Assert.True(Math.Abs(olaHz - 220) > 40, $"OLA should move pitch away from 220, got {olaHz:F1}");

        var fixedOut = PitchShift.ApplyCurve(sine, channels, rate, table, timeStretch: true, lightweight: true);
        var fixedHz = EstimateHzByZeroCrossings(fixedOut, rate);
        Assert.Equal(frames, fixedOut.Length);
        Assert.InRange(fixedHz, 220 * 0.85, 220 * 1.15);

        var dump = Path.Combine(Path.GetTempPath(), "pitch-lightweight-stages.txt");
        File.WriteAllText(
            dump,
            $"baseHz={EstimateHzByZeroCrossings(sine, rate):F1} " +
            $"stage1_220Hz_len2x={stage1Hz:F1} " +
            $"linearCancelHz={canceledHz:F1} " +
            $"olaHz={olaHz:F1} " +
            $"decimatedFixedHz={fixedHz:F1}");
    }

    private static float[] MakeTone(float hz, int frames, int rate)
    {
        var samples = new float[frames];
        // 長いバッファでは float 位相が壊れるので double で回す。
        var phaseStep = 2 * Math.PI * hz / rate;
        var phase = 0d;
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (float)(Math.Sin(phase) * 0.5);
            phase += phaseStep;
        }

        return samples;
    }

    private static double EstimateHzByZeroCrossings(float[] samples, int sampleRate)
    {
        var start = samples.Length / 10;
        var end = samples.Length - start;
        var crossings = 0;
        for (var i = start + 1; i < end; i++)
        {
            if (samples[i - 1] <= 0 && samples[i] > 0)
            {
                crossings++;
            }
        }

        var seconds = (end - start) / (double)sampleRate;
        return crossings / seconds;
    }

    [Fact]
    public void ProcessEdits_PitchCurve_ReplaceKeepsLength()
    {
        const int channels = 1;
        const int rate = 48000;
        const int frames = 4096;
        var sine = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            sine[i] = (float)Math.Sin(2 * Math.PI * 220 * i / rate) * 0.4f;
        }

        var document = new AudioDocument(sine, rate, channels, 16, AudioFileKind.Wave, null, buildPeaks: false);
        var range = new WaveSelection(512, 512 + 1024);
        var curve = PitchCurve.Flat(3);
        var command = ProcessEdits.PitchCurve(document, range, curve);
        Assert.NotNull(command);
        command!.Apply(document);
        Assert.Equal(frames, document.FrameCount);
    }

    [Fact]
    public void HistoryRecipe_PitchCurve_RoundTrips()
    {
        var curve = new PitchCurve(
            [
                new PitchCurvePoint(0, 0),
                new PitchCurvePoint(1, 4),
            ],
            [FadeShape.Linear]);
        var range = new WaveSelection(10, 100);
        var recipe = HistoryRecipes.FromPitchCurve(48000, range, curve, edgeFadeMs: 20, documentGainScale: 0.9f);
        Assert.Equal(HistoryRecipes.PitchCurve, recipe.Kind);
        Assert.Equal(0.9, recipe.Amount, 5);
        Assert.True(recipe.ClipSamples.Length >= 4);

        const int frames = 200;
        var samples = new float[frames];
        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null, buildPeaks: false);
        var command = HistoryRecipes.TryCreate(document, recipe);
        Assert.NotNull(command);
    }
}
