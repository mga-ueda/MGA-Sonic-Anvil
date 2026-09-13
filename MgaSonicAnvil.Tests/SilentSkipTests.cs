using System.Text.Json;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SilentSkipTests
{
    [Fact]
    public void ConfirmRecord_MentionsThresholdAndSilentSkipToggle()
    {
        var text = UiStrings.ConfirmRecordSilentSkip(-60, 500);
        Assert.Contains("-60", text);
        Assert.Contains("500", text);
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Contains("録音を開始", UiStrings.ConfirmRecord);
            Assert.Contains("設定", UiStrings.ConfirmRecordSilentSkip(-60, 500));
            Assert.Contains("Silent Skip をオンにする", UiStrings.LabelConfirmRecordSilentSkip);
            Assert.Contains("リージョン", UiStrings.LabelSilentSkipRecordAddRegion);
            Assert.Contains("録音部分", UiStrings.LabelSilentSkipRecordAddRegion);
            Assert.Contains("無音部分", UiStrings.LabelSilentSkipRecordAddRegion);
            Assert.Contains("谷の平均", UiStrings.LabelSilentSkipFloorNote);
            Assert.DoesNotContain("ピーク", UiStrings.LabelSilentSkipFloorNote);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Contains("recording", UiStrings.ConfirmRecord, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Settings", UiStrings.ConfirmRecordSilentSkip(-60, 500));
            Assert.Contains("Turn on Silent Skip", UiStrings.LabelConfirmRecordSilentSkip);
            Assert.Contains("region", UiStrings.LabelSilentSkipRecordAddRegion, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("recorded", UiStrings.LabelSilentSkipRecordAddRegion, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("silent", UiStrings.LabelSilentSkipRecordAddRegion, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("floor", UiStrings.LabelSilentSkipFloorNote, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("peak", UiStrings.LabelSilentSkipFloorNote, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void FormatPeakDb_FloorsAtMinus120()
    {
        Assert.Equal("-120.0", SilentSkip.FormatPeakDb(double.NegativeInfinity));
        Assert.Equal("-120.0", SilentSkip.FormatPeakDb(-160));
        Assert.Equal("-120.0", SilentSkip.FormatPeakDb(-120));
        Assert.Equal("-60.0", SilentSkip.FormatPeakDb(-60));
        Assert.Equal("-6.0", SilentSkip.FormatPeakDb(-6.02));
        Assert.Equal("0.0", SilentSkip.FormatPeakDb(0));
        Assert.Equal("0.0", SilentSkip.FormatPeakDb(3));
    }

    [Fact]
    public void FloorBlocks_AverageUsesIntervalMinimum()
    {
        var blockPeak = 0f;
        var blockFrames = 0;
        var intervalMin = 0f;
        var hasMin = false;
        for (var i = 0; i < SilentSkip.FloorBlockFrames; i++)
        {
            SilentSkip.NoteFloorAbs(0.8f, ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin);
        }

        for (var i = 0; i < SilentSkip.FloorBlockFrames; i++)
        {
            SilentSkip.NoteFloorAbs(0.2f, ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin);
        }

        Assert.Equal(0.2f, SilentSkip.TakeFloor(ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin), 5);
        Assert.Equal(0f, SilentSkip.TakeFloor(ref blockPeak, ref blockFrames, ref intervalMin, ref hasMin), 5);
    }

    [Fact]
    public void RecordAddRegion_DefaultsOff()
    {
        Assert.False(new AppSettings().SilentSkipRecordAddRegion);
        Assert.False(AppSettings.CreateDefault().SilentSkipRecordAddRegion);
    }

    [Fact]
    public void RecordAddRegion_RoundTripsInSettingsJson()
    {
        var settings = new AppSettings { SilentSkipRecordAddRegion = true };
        var json = JsonSerializer.Serialize(settings, AppSettingsJsonContext.Default.AppSettings);
        var back = JsonSerializer.Deserialize(json, AppSettingsJsonContext.Default.AppSettings);
        Assert.True(back!.SilentSkipRecordAddRegion);
    }

    [Fact]
    public void SplitInsertedSpans_UsesCompletedPadsNotBriefDips()
    {
        var floor = SilentSkip.LinearFromDb(-60);
        var dips = new float[] { 0.5f, 0f, 0.5f, 0f, 0.4f };
        Assert.Equal(
            [new RecordedSpan(0, 5, false)],
            SilentSkip.SplitInsertedSpans(dips, 1, floor, padFrames: 2));

        var paused = new float[] { 0.5f, 0f, 0f, 0f, 0.4f };
        var spans = SilentSkip.SplitInsertedSpans(paused, 1, floor, padFrames: 2);
        Assert.Equal(3, spans.Length);
        Assert.Equal(new RecordedSpan(0, 1, false), spans[0]);
        Assert.Equal(new RecordedSpan(1, 3, true), spans[1]);
        Assert.Equal(new RecordedSpan(3, 4, false), spans[2]);
    }

    [Fact]
    public void RecordPartRegions_NamesInsertedAudioAndSilence()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var regions = SilentSkip.RecordPartRegions(
                [
                    new RecordedSpan(0, 1, false),
                    new RecordedSpan(1, 3, true),
                    new RecordedSpan(3, 4, false),
                ],
                10);
            Assert.Equal(3, regions.Length);
            Assert.Equal(new WaveSelection(10, 11), regions[0].Range);
            Assert.Equal(UiStrings.RegionNameRecordAudio, regions[0].Name);
            Assert.Equal(new WaveSelection(11, 13), regions[1].Range);
            Assert.Equal(UiStrings.RegionNameRecordSilence, regions[1].Name);
            Assert.Equal(new WaveSelection(13, 14), regions[2].Range);
            Assert.Equal(UiStrings.RegionNameRecordAudio, regions[2].Name);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    [Fact]
    public void RecordGate_DropsTrailingPadFromSpans()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 1);
        gate.Reset(hasWritten: false);
        var dest = new float[8];
        var written = gate.ProcessFrame([0.5f], dest);
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written = Math.Max(0, written - gate.CloseTake());

        Assert.Equal(1, written);
        Assert.Equal([new RecordedSpan(0, 1, false)], gate.SnapshotWrittenSpans());
    }

    [Fact]
    public void RecordGate_ThreePadsMakeThreeSilenceSpans()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 1);
        gate.Reset(hasWritten: false);
        var dest = new float[32];
        var written = 0;
        written += gate.ProcessFrame([0.5f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0.4f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0.3f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0.2f], dest.AsSpan(written));
        written = Math.Max(0, written - gate.CloseTake());

        var spans = gate.SnapshotWrittenSpans();
        Assert.Equal(7, spans.Length);
        Assert.Equal(3, spans.Count(span => span.Silent));
        Assert.Equal(4, spans.Count(span => !span.Silent));
        Assert.Equal(written, spans[^1].EndFrame);
    }

    [Fact]
    public void RecordTakeRegion_CoversWrittenSpanOnly()
    {
        Assert.Null(SilentSkip.RecordTakeRegion(0, 0));
        Assert.Null(SilentSkip.RecordTakeRegion(12, 12));
        Assert.Null(SilentSkip.RecordTakeRegion(20, 8));
        Assert.Equal(new WaveSelection(0, 48), SilentSkip.RecordTakeRegion(0, 48));
        Assert.Equal(new WaveSelection(12, 40), SilentSkip.RecordTakeRegion(12, 40));
    }

    [Fact]
    public void Threshold_DefaultsAndRejectsOutOfRange()
    {
        Assert.Equal(-60, SilentSkip.DefaultThresholdDb);
        Assert.Equal(-60, SilentSkip.ClampThresholdDb(-60));
        Assert.Equal(-120, SilentSkip.ClampThresholdDb(-200));
        Assert.Equal(0, SilentSkip.ClampThresholdDb(3));
        Assert.True(SilentSkip.TryParseThresholdDb("-60", out var db));
        Assert.Equal(-60, db);
        Assert.True(SilentSkip.TryParseThresholdDb("-12.5", out db));
        Assert.Equal(-12.5, db);
        Assert.False(SilentSkip.TryParseThresholdDb("", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("x", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("1", out _));
        Assert.False(SilentSkip.TryParseThresholdDb("-130", out _));
    }

    [Fact]
    public void RecordPad_DefaultsAndRejectsOutOfRange()
    {
        Assert.Equal(500, SilentSkip.DefaultRecordPadMs);
        Assert.Equal(500, SilentSkip.ClampRecordPadMs(500));
        Assert.Equal(0, SilentSkip.ClampRecordPadMs(-3));
        Assert.Equal(10000, SilentSkip.ClampRecordPadMs(20000));
        Assert.Equal(24000, SilentSkip.RecordPadFrames(48000, 500));
        Assert.Equal(0, SilentSkip.RecordPadFrames(48000, 0));
        Assert.True(SilentSkip.TryParseRecordPadMs("500", out var ms));
        Assert.Equal(500, ms);
        Assert.True(SilentSkip.TryParseRecordPadMs("0", out ms));
        Assert.Equal(0, ms);
        Assert.False(SilentSkip.TryParseRecordPadMs("", out _));
        Assert.False(SilentSkip.TryParseRecordPadMs("x", out _));
        Assert.False(SilentSkip.TryParseRecordPadMs("-1", out _));
        Assert.False(SilentSkip.TryParseRecordPadMs("10001", out _));
    }

    [Fact]
    public void CopyAudibleFrames_KeepsSilenceShorterThanPad()
    {
        var samples = new float[] { 0f, 0f, 0.5f, 0.5f, 0f, 0f, 0.4f, 0.4f };
        var dest = new float[16];
        var hasWritten = false;
        var written = SilentSkip.CopyAudibleFrames(
            samples,
            dest,
            2,
            SilentSkip.LinearFromDb(-60),
            padFrames: 2,
            ref hasWritten);

        Assert.Equal(6, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(0.5f, dest[1]);
        Assert.Equal(0f, dest[2]);
        Assert.Equal(0f, dest[3]);
        Assert.Equal(0.4f, dest[4]);
        Assert.Equal(0.4f, dest[5]);
        Assert.True(hasWritten);
    }

    [Fact]
    public void CopyAudibleFrames_ReplacesSilenceLongerThanPad()
    {
        var samples = new float[] { 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0.4f, 0.4f };
        var dest = new float[16];
        var hasWritten = false;
        var written = SilentSkip.CopyAudibleFrames(
            samples,
            dest,
            2,
            SilentSkip.LinearFromDb(-60),
            padFrames: 2,
            ref hasWritten);

        Assert.Equal(8, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(0.5f, dest[1]);
        Assert.Equal(0f, dest[2]);
        Assert.Equal(0f, dest[3]);
        Assert.Equal(0f, dest[4]);
        Assert.Equal(0f, dest[5]);
        Assert.Equal(0.4f, dest[6]);
        Assert.Equal(0.4f, dest[7]);
    }

    [Fact]
    public void CopyAudibleFrames_KeepsLeadingShortSilenceWhenTakeAlreadyHasAudio()
    {
        var samples = new float[] { 0f, 0f, 0.5f, 0.5f };
        var dest = new float[8];
        var hasWritten = true;
        var written = SilentSkip.CopyAudibleFrames(
            samples,
            dest,
            2,
            SilentSkip.LinearFromDb(-60),
            padFrames: 2,
            ref hasWritten);

        Assert.Equal(4, written);
        Assert.Equal(0f, dest[0]);
        Assert.Equal(0f, dest[1]);
        Assert.Equal(0.5f, dest[2]);
        Assert.Equal(0.5f, dest[3]);
    }

    [Fact]
    public void RecordGate_WritesSilenceWhenThresholdStops()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 2);
        gate.Reset(hasWritten: false);
        var dest = new float[16];
        var written = gate.ProcessFrame([0.5f, 0.5f], dest);
        var pad = gate.ProcessFrame([0f, 0f], dest.AsSpan(written));

        Assert.Equal(2, written);
        Assert.Equal(2, pad);
        Assert.Equal(0f, dest[2]);
        Assert.Equal(0f, dest[3]);
    }

    [Fact]
    public void RecordGate_DoesNotDumpPadWhenSoundReturns()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 2);
        gate.Reset(hasWritten: false);
        var dest = new float[16];
        var written = gate.ProcessFrame([0.5f, 0.5f], dest);
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        Assert.Equal(6, written);

        var added = gate.ProcessFrame([0.4f, 0.4f], dest.AsSpan(written));
        Assert.Equal(2, added);
        Assert.Equal(0.4f, dest[6]);
        Assert.Equal(0.4f, dest[7]);
    }

    [Fact]
    public void RecordGate_StaysClosedUntilClearlyAudible()
    {
        var gate = new SilentSkipRecordGate();
        var floor = SilentSkip.LinearFromDb(-60);
        gate.Configure(true, floor, padFrames: 0, channels: 2);
        gate.Reset(hasWritten: false);
        var dest = new float[16];
        var written = gate.ProcessFrame([0.5f, 0.5f], dest);
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([floor * 1.5f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0.4f, 0.4f], dest.AsSpan(written));

        Assert.Equal(4, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(0.4f, dest[2]);
    }

    [Fact]
    public void RecordGate_DropsLongTrailingSilence()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 2);
        gate.Reset(hasWritten: false);
        var dest = new float[16];
        var written = gate.ProcessFrame([0.5f, 0.5f], dest);
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written = Math.Max(0, written - gate.CloseTake());

        Assert.Equal(2, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(0.5f, dest[1]);
    }

    [Fact]
    public void RecordGate_KeepsShortTrailingSilence()
    {
        var gate = new SilentSkipRecordGate();
        gate.Configure(true, SilentSkip.LinearFromDb(-60), padFrames: 2, channels: 2);
        gate.Reset(hasWritten: false);
        var dest = new float[16];
        var written = gate.ProcessFrame([0.5f, 0.5f], dest);
        written += gate.ProcessFrame([0f, 0f], dest.AsSpan(written));
        written = Math.Max(0, written - gate.CloseTake());

        Assert.Equal(4, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(0.5f, dest[1]);
        Assert.Equal(0f, dest[2]);
        Assert.Equal(0f, dest[3]);
    }

    [Fact]
    public void LinearFromDb_MatchesMinusSixty()
    {
        Assert.Equal(0.001f, SilentSkip.LinearFromDb(-60), 6);
    }

    [Fact]
    public void FindNextAudible_SkipsLeadingSilence()
    {
        var samples = new float[20];
        samples[10] = 0.5f;
        samples[11] = 0.5f;
        var threshold = SilentSkip.LinearFromDb(-60);
        Assert.True(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, 0));
        Assert.Equal(5, SilentSkip.FindNextAudible(samples, 2, 0, 10, threshold, 0));
        Assert.Equal(10, SilentSkip.FindNextAudible(samples, 2, 6, 10, threshold, 0));
    }

    [Fact]
    public void CopyAudibleFrames_KeepsOnlyFramesAboveThreshold()
    {
        var samples = new float[] { 0f, 0f, 0.5f, -0.4f, 0.0001f, 0.0001f, 0.002f, 0f };
        var dest = new float[8];
        var written = SilentSkip.CopyAudibleFrames(samples, dest, 2, SilentSkip.LinearFromDb(-60));

        Assert.Equal(4, written);
        Assert.Equal(0.5f, dest[0]);
        Assert.Equal(-0.4f, dest[1]);
        Assert.Equal(0.002f, dest[2]);
        Assert.Equal(0f, dest[3]);
    }

    [Fact]
    public void IsSpanSilent_TreatsAnyChannelAboveFloorAsAudible()
    {
        var silent = new float[] { 0.0001f, -0.0002f };
        var audible = new float[] { 0.0001f, 0.5f };
        var floor = SilentSkip.LinearFromDb(-60);
        Assert.True(SilentSkip.IsSpanSilent(silent, floor));
        Assert.False(SilentSkip.IsSpanSilent(audible, floor));
    }

    [Fact]
    public void FindNextAudible_HonorsSoloMask()
    {
        var samples = new float[8];
        samples[1] = 0.5f;
        samples[4] = 0.5f;
        var threshold = SilentSkip.LinearFromDb(-60);
        Assert.False(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, 0));
        Assert.True(SilentSkip.IsFrameSilent(samples, 2, 0, threshold, ChannelSolo.MaskOf(0)));
        Assert.Equal(2, SilentSkip.FindNextAudible(samples, 2, 0, 4, threshold, ChannelSolo.MaskOf(0)));
    }

    [Fact]
    public void Provider_JumpsOverSilenceWhenEnabled()
    {
        var samples = new float[200];
        for (var i = 80; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0.25f, value, 3));
        Assert.Equal(50, provider.CursorFrame);
    }

    [Fact]
    public void Provider_PlaysSilenceWhenDisabled()
    {
        var samples = new float[200];
        for (var i = 80; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(false, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0f, value, 3));
        Assert.Equal(10, provider.CursorFrame);
    }

    [Fact]
    public void Provider_SkipsSilenceInTheMiddleOfARead()
    {
        var samples = new float[80];
        for (var frame = 0; frame < 5; frame++)
        {
            samples[frame * 2] = 0.25f;
            samples[frame * 2 + 1] = 0.25f;
        }

        for (var frame = 30; frame < 40; frame++)
        {
            samples[frame * 2] = 0.5f;
            samples[frame * 2 + 1] = 0.5f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[20];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(0.25f, buffer[i], 3);
        }

        for (var i = 10; i < 20; i++)
        {
            Assert.Equal(0.5f, buffer[i], 3);
        }
    }

    [Fact]
    public void Provider_StopsAtUsedSampleCountNotCapacity()
    {
        var document = new AudioDocument([0.5f, 0.5f], 48000, 2, 16, AudioFileKind.Wave, null);
        document.AppendLiveSamples([0.25f, 0.25f]);
        Assert.True(document.Interleaved.Length > document.SampleCount);

        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(false, -60);

        var buffer = new float[20];
        Assert.Equal(4, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.5f, buffer[0], 3);
        Assert.Equal(0.5f, buffer[1], 3);
        Assert.Equal(0.25f, buffer[2], 3);
        Assert.Equal(0.25f, buffer[3], 3);
        Assert.Equal(2, provider.CursorFrame);
        Assert.True(provider.Ended);
    }

    [Fact]
    public void Provider_SkipDoesNotJumpThroughSpareCapacity()
    {
        var document = new AudioDocument([0.5f, 0.5f], 48000, 2, 16, AudioFileKind.Wave, null);
        document.AppendLiveSamples([0.25f, 0.25f]);

        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[20];
        Assert.Equal(4, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(2, provider.CursorFrame);
        Assert.True(provider.Ended);
    }

    [Fact]
    public void Provider_LoopWrapsToNextAudible()
    {
        var samples = new float[40];
        for (var frame = 0; frame < 5; frame++)
        {
            samples[frame * 2] = 0.4f;
            samples[frame * 2 + 1] = 0.4f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 8, new WaveSelection(0, 20), loop: true);
        provider.SetSilentSkip(true, -60);

        var buffer = new float[8];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0.4f, value, 3));
        Assert.Equal(4, provider.CursorFrame);
    }

    [Fact]
    public void Provider_ShuttleDoesNotSkipSilence()
    {
        var samples = new float[200];
        for (var i = 160; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);
        provider.SetPlaybackSpeed(PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.All(buffer, value => Assert.Equal(0f, value, 3));
        Assert.Equal(60, provider.CursorFrame);
    }

    [Fact]
    public void Provider_RewindShuttleDoesNotJumpForwardOverSilence()
    {
        var samples = new float[200];
        for (var i = 160; i < 200; i++)
        {
            samples[i] = 0.25f;
        }

        var document = new AudioDocument(samples, 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 90, playRange: null, loop: false);
        provider.SetSilentSkip(true, -60);
        provider.SetPlaybackSpeed(-PlaybackSampleProvider.FastSpeed);

        var buffer = new float[20 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(30, provider.CursorFrame);

        provider.SetPlaybackSpeed(1);
        var after = new float[4];
        Assert.Equal(after.Length, provider.Read(after, 0, after.Length));
        Assert.All(after, value => Assert.Equal(0.25f, value, 3));
        Assert.Equal(82, provider.CursorFrame);
    }
}
