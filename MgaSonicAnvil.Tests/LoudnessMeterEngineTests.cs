using System.IO;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LoudnessMeterEngineTests
{
    [Fact]
    public void Silence_IsNegativeInfinity()
    {
        var engine = new LoudnessMeterEngine();
        var left = new float[4800];
        var right = new float[4800];
        var snap = engine.Process(left, right, left.Length, 48000);
        Assert.True(float.IsNegativeInfinity(snap.ShortTermLufs));
        Assert.True(float.IsNegativeInfinity(snap.IntegratedLufs));
    }

    [Fact]
    public void StereoSineAtMinusTwenty_IsNearMinusTwentyPointSeven()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var left = new float[frames];
        var right = new float[frames];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            left[i] = s;
            right[i] = s;
        }

        var engine = new LoudnessMeterEngine();
        var snap = engine.Process(left, right, frames, rate);
        Assert.InRange(snap.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(snap.IntegratedLufs, -22.5f, -19.5f);
        Assert.True(snap.TruePeakDb < -19f);

        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }

        var profile = LoudnessMeterEngine.BuildShortTermProfile(interleaved, 2, rate);
        Assert.True(profile.Values.Length >= 10);
        Assert.InRange(profile.AtFrame(frames - 1), -22.5f, -19.5f);

        var measured = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(measured.IntegratedLufs, -22.5f, -19.5f);
        Assert.InRange(measured.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(measured.TruePeakDb, -22.5f, -19f);
    }

    [Fact]
    public void TryMeasureIntegratedLufs_UsesInMemoryPcmForWave()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var interleaved = new float[frames * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        var document = new AudioDocument(interleaved, rate, 2, 16, AudioFileKind.Wave, null, buildPeaks: false);
        Assert.True(LoudnessMeterEngine.TryMeasureIntegratedLufs(document, out var lufs));
        Assert.InRange(lufs, -22.5f, -19.5f);
    }

    [Fact]
    public void TryMeasureIntegratedFromPath_ReadsAiff()
    {
        var path = Path.Combine(Path.GetTempPath(), $"mga-aiff-lufs-{Guid.NewGuid():N}.aiff");
        try
        {
            WriteMonoToneAiff(path, sampleRate: 48000, frames: 48000 * 2, frequency: 1000, amp: 0.1f);
            Assert.Equal(AudioFileKind.Aiff, AudioCodec.DetectKind(path));
            Assert.True(LoudnessMeterEngine.TryMeasureIntegratedFromPath(path, out var lufs));
            Assert.True(float.IsFinite(lufs));

            var deferred = AudioDocument.CreateDeferred(path);
            Assert.Equal(AudioFileKind.Aiff, deferred.SourceKind);
            Assert.True(LoudnessMeterEngine.TryMeasureIntegratedLufs(deferred, out var deferredLufs));
            Assert.Equal(lufs, deferredLufs, precision: 2);
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static void WriteMonoToneAiff(
        string path,
        int sampleRate,
        int frames,
        double frequency,
        float amp)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: false);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("FORM"));
        var sizePos = stream.Position;
        WriteU32Be(writer, 0);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("AIFF"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("COMM"));
        WriteU32Be(writer, 18);
        WriteU16Be(writer, 1);
        WriteU32Be(writer, (uint)frames);
        WriteU16Be(writer, 16);
        // 48000 Hz as 80-bit IEEE extended
        writer.Write(new byte[] { 0x40, 0x0E, 0xBB, 0x80, 0, 0, 0, 0, 0, 0 });
        writer.Write(System.Text.Encoding.ASCII.GetBytes("SSND"));
        WriteU32Be(writer, (uint)(8 + (frames * 2)));
        WriteU32Be(writer, 0);
        WriteU32Be(writer, 0);
        for (var i = 0; i < frames; i++)
        {
            var sample = (short)Math.Clamp(
                Math.Round(amp * short.MaxValue * Math.Sin(2 * Math.PI * frequency * i / sampleRate)),
                short.MinValue,
                short.MaxValue);
            writer.Write((byte)(sample >> 8));
            writer.Write((byte)sample);
        }

        var end = stream.Position;
        stream.Position = sizePos;
        WriteU32Be(writer, (uint)(end - 8));
    }

    private static void WriteU16Be(BinaryWriter writer, int value)
    {
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteU32Be(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    [Fact]
    public void MeasureFile_ShortTermIsFileMaximum()
    {
        const int rate = 48000;
        var loudFrames = rate * 4;
        var quietFrames = rate * 4;
        var frames = loudFrames + quietFrames;
        var interleaved = new float[frames * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < loudFrames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        var engine = new LoudnessMeterEngine();
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = interleaved[i * 2];
            right[i] = interleaved[i * 2 + 1];
        }

        var live = engine.Process(left, right, frames, rate);
        Assert.True(live.ShortTermLufs < -40f);
        Assert.InRange(engine.MaxShortTermLufs, -22.5f, -19.5f);

        var measured = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(measured.ShortTermLufs, -22.5f, -19.5f);
        Assert.InRange(measured.IntegratedLufs, live.IntegratedLufs - 0.2f, live.IntegratedLufs + 0.2f);
        Assert.InRange(measured.MomentaryMaxLufs, -22.5f, -19.5f);
    }

    [Fact]
    public void Traffic_Lufs_SafeThroughTarget_CautionForThreeLu_DangerAbove()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-26f, -24));
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-24f, -24));
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLufs(-23.96f, -24));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLufs(-23.9f, -24));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLufs(-21f, -24));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForLufs(-20.9f, -24));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForLufs(float.NegativeInfinity, -24));
    }

    [Fact]
    public void Traffic_TruePeak_DangerAtOrOverZero()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForTruePeak(-2f));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForTruePeak(-0.5f));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForTruePeak(0f));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForTruePeak(float.NegativeInfinity));
    }

    [Fact]
    public void ParseTargetLufs_AcceptsRange()
    {
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("-24", out var v));
        Assert.Equal(-24, v);
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("-16.0", out v));
        Assert.Equal(-16, v);
        Assert.True(LoudnessMeterEngine.TryParseTargetLufs("0", out v));
        Assert.Equal(0, v);
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("x", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("1", out _));
        Assert.False(LoudnessMeterEngine.TryParseTargetLufs("-80", out _));
    }

    [Fact]
    public void ApplyLinearGainToLufs_AddsTwentyLog()
    {
        Assert.InRange(LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 2f), -18.05f, -17.95f);
        Assert.Equal(-24f, LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 1f), 5);
        Assert.True(float.IsNegativeInfinity(LoudnessMeterEngine.ApplyLinearGainToLufs(-24f, 0f)));
        Assert.True(float.IsNegativeInfinity(
            LoudnessMeterEngine.ApplyLinearGainToLufs(float.NegativeInfinity, 2f)));
    }

    [Fact]
    public void TryGainDbToMatchTarget_UsesTargetMinusIntegrated()
    {
        Assert.True(LoudnessMeterEngine.TryGainDbToMatchTarget(-18f, -24, out var gainDb));
        Assert.Equal(-6, gainDb, 5);
        Assert.True(LoudnessMeterEngine.TryGainDbToMatchTarget(-30f, -24, out gainDb));
        Assert.Equal(6, gainDb, 5);
        Assert.False(LoudnessMeterEngine.TryGainDbToMatchTarget(-24f, -24, out _));
        Assert.False(LoudnessMeterEngine.TryGainDbToMatchTarget(float.NegativeInfinity, -24, out _));
        Assert.False(LoudnessMeterEngine.TryGainDbToMatchTarget(float.NaN, -24, out _));
    }

    [Fact]
    public void TryGainDbToMatchTarget_Document_MeasuresAndReturnsGain()
    {
        // 短いトーンでも Integrated が有限ならゲインが求まる。
        var rate = 48000;
        var frames = rate; // 1 秒
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = 0.25f * MathF.Sin(2f * MathF.PI * 440f * i / rate);
        }

        var document = new AudioDocument(samples, rate, 1, 16, AudioFileKind.Wave, null);
        Assert.True(LoudnessMeterEngine.TryMeasureIntegratedLufs(document, out var integrated));
        Assert.True(float.IsFinite(integrated));
        var target = integrated - 3f;
        Assert.True(LoudnessMeterEngine.TryGainDbToMatchTarget(
            document,
            target,
            out var gainDb,
            out var measured));
        Assert.Equal(integrated, measured, 3);
        Assert.Equal(-3, gainDb, 1);
    }

    [Fact]
    public void ApplyLinearGain_ShiftsLufsAndTruePeakButNotLra()
    {
        var snap = new LoudnessSnapshot(-20f, -18f, -19f, -16f, 7.5f, -3f, -24);
        var gained = LoudnessMeterEngine.ApplyLinearGain(snap, 0.5f);
        Assert.InRange(gained.ShortTermLufs - snap.ShortTermLufs, -6.1f, -5.9f);
        Assert.InRange(gained.IntegratedLufs - snap.IntegratedLufs, -6.1f, -5.9f);
        Assert.InRange(gained.MomentaryMaxLufs - snap.MomentaryMaxLufs, -6.1f, -5.9f);
        Assert.InRange(gained.TruePeakDb - snap.TruePeakDb, -6.1f, -5.9f);
        Assert.Equal(snap.LoudnessRangeLu, gained.LoudnessRangeLu, 5);
        Assert.Equal(snap, LoudnessMeterEngine.ApplyLinearGain(snap, 1f));
    }

    [Fact]
    public void MeasureFile_IgnoresSpareCapacityPastSampleCount()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var used = frames * 2;
        var interleaved = new float[used * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        for (var i = used; i < interleaved.Length; i++)
        {
            interleaved[i] = 0.9f;
        }

        var exact = LoudnessMeterEngine.MeasureFile(interleaved[..used], 2, rate);
        var padded = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate, sampleCount: used);
        Assert.InRange(padded.IntegratedLufs, exact.IntegratedLufs - 0.15f, exact.IntegratedLufs + 0.15f);
        Assert.InRange(padded.TruePeakDb, exact.TruePeakDb - 0.15f, exact.TruePeakDb + 0.15f);
        Assert.True(padded.TruePeakDb < -6f);
    }

    [Fact]
    public void MeasureFile_InPlaceGain_ShiftsIntegrated()
    {
        const int rate = 48000;
        var frames = rate * 4;
        var interleaved = new float[frames * 2];
        var amp = (float)Math.Pow(10, -20d / 20d);
        for (var i = 0; i < frames; i++)
        {
            var s = amp * (float)Math.Sin(2 * Math.PI * 1000 * i / rate);
            interleaved[i * 2] = s;
            interleaved[i * 2 + 1] = s;
        }

        var before = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        for (var i = 0; i < interleaved.Length; i++)
        {
            interleaved[i] *= 0.5f;
        }

        var after = LoudnessMeterEngine.MeasureFile(interleaved, 2, rate);
        Assert.InRange(before.IntegratedLufs - after.IntegratedLufs, 5.8f, 6.2f);
        Assert.InRange(before.ShortTermLufs - after.ShortTermLufs, 5.8f, 6.2f);
        Assert.InRange(before.TruePeakDb - after.TruePeakDb, 5.8f, 6.2f);
    }

    [Fact]
    public void TryAverageIntegratedLufs_MeansFiniteReadingsOnly()
    {
        Assert.False(LoudnessMeterEngine.TryAverageIntegratedLufs([null, float.NaN], out _));
        Assert.True(LoudnessMeterEngine.TryAverageIntegratedLufs(
            [-14f, null, -20f, float.NegativeInfinity],
            out var average));
        Assert.Equal(-17f, average, 3);
        var averaged = FileLoudness.Average(
        [
            new FileLoudness(-10f, -14f, -8f, 6f, -1f),
            new FileLoudness(-12f, -18f, -10f, 8f, -3f),
        ]);
        Assert.Equal(-11f, averaged.ShortTermMaxLufs, 3);
        Assert.Equal(-16f, averaged.IntegratedLufs, 3);
        Assert.Equal(-9f, averaged.MomentaryMaxLufs, 3);
        Assert.Equal(7f, averaged.LoudnessRangeLu, 3);
        Assert.Equal(-2f, averaged.TruePeakDb, 3);
    }

    [Fact]
    public void OfflineCache_InvalidatesWhenRevisionChangesOnSameBuffer()
    {
        var samples = new float[8];
        Assert.True(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 1, 8));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 2, 8));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, samples, 1, 4));
        Assert.False(LoudnessMeterView.CanReuseOffline(samples, 1, 8, new float[8], 1, 8));
        Assert.Equal("-23.5 LKFS", UiStrings.FormatIntegratedLufs(-23.5f));
        Assert.Equal("--.- LKFS", UiStrings.FormatIntegratedLufs(float.NegativeInfinity));
        Assert.Equal(string.Empty, UiStrings.FormatIntegratedLufs(null));
    }

    [Fact]
    public void Profile_AtFrame_AppliesPreviewGain()
    {
        var values = new float[] { -20f, -18f };
        var profile = new LoudnessProfile(48000, 4800, values);
        Assert.Equal(-20f, profile.AtFrame(0), 4);
        Assert.InRange(profile.AtFrame(0, _ => 2f), -14.05f, -13.95f);
        Assert.Equal(-20f, profile.AtFrame(0, _ => 1f), 4);
    }

    [Fact]
    public void Traffic_Lra_WarnsWhenWide()
    {
        Assert.Equal(LoudnessTraffic.Safe, LoudnessTrafficLight.ForLra(12f));
        Assert.Equal(LoudnessTraffic.Caution, LoudnessTrafficLight.ForLra(22f));
        Assert.Equal(LoudnessTraffic.Danger, LoudnessTrafficLight.ForLra(26f));
        Assert.Equal(LoudnessTraffic.Idle, LoudnessTrafficLight.ForLra(float.NaN));
    }

    [Fact]
    public void FillChip_OnlyWhenOfflineAndActive()
    {
        Assert.True(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Safe));
        Assert.True(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Caution));
        Assert.False(LoudnessMeterView.UsesFillChip(offline: true, LoudnessTraffic.Idle));
        Assert.False(LoudnessMeterView.UsesFillChip(offline: false, LoudnessTraffic.Safe));
    }

    [Fact]
    public void ChipText_IsWhiteOnDarkAndDarkOnLight()
    {
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Dark, "LoudnessChipForeBrush"),
            LoudnessMeterView.ChipTextColor(UiTheme.Dark));
        Assert.Equal(
            UiThemePalette.ColorFor(UiTheme.Light, "LoudnessChipForeBrush"),
            LoudnessMeterView.ChipTextColor(UiTheme.Light));
        Assert.True(Luma(LoudnessMeterView.ChipTextColor(UiTheme.Dark)) > 0.8);
        Assert.True(Luma(LoudnessMeterView.ChipTextColor(UiTheme.Light)) < 0.2);
        var cyan = Color.FromRgb(0x3A, 0xB8, 0xE8);
        var darkChip = LoudnessMeterView.ShadeChipFill(cyan, UiTheme.Dark);
        var lightChip = LoudnessMeterView.ShadeChipFill(cyan, UiTheme.Light);
        Assert.True(Luma(darkChip) < Luma(cyan));
        Assert.True(Luma(lightChip) > Luma(cyan));
        // 0.55 混ぜのほうが 0.22 より暗い。
        var milder = MixTowardBlack(cyan, 0.22);
        Assert.True(Luma(darkChip) < Luma(milder));
        Assert.Equal(0xFF, darkChip.A);
        Assert.Equal(0xFF, lightChip.A);

        // 注意（琥珀）と超過（赤）は黒へ混ぜても緑成分で判別できる。
        var caution = LoudnessMeterView.ShadeChipFill(
            Color.FromRgb(
                LoudnessTrafficLight.CautionR,
                LoudnessTrafficLight.CautionG,
                LoudnessTrafficLight.CautionB),
            UiTheme.Dark);
        var danger = LoudnessMeterView.ShadeChipFill(
            Color.FromRgb(
                LoudnessTrafficLight.DangerR,
                LoudnessTrafficLight.DangerG,
                LoudnessTrafficLight.DangerB),
            UiTheme.Dark);
        Assert.True(caution.G - danger.G >= 40);
        Assert.True(danger.G < 40);
        Assert.True(caution.B < 24);
        Assert.True(Luma(caution) < 0.35);
        Assert.True(Luma(danger) < 0.35);
    }

    private static Color MixTowardBlack(Color color, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(color.R * (1 - amount)),
            (byte)Math.Round(color.G * (1 - amount)),
            (byte)Math.Round(color.B * (1 - amount)));
    }

    private static double Luma(Color color) =>
        (0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B) / 255d;
}
