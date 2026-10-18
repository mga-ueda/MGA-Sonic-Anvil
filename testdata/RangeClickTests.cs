using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class RangeClickTests
{
    private static float Gain(float sample) => sample * RangeClickMixer.OutputGainLinear;

    [Fact]
    public void OutputGain_IsMinusTwentyDb()
    {
        Assert.Equal(-20, RangeClickMixer.OutputGainDb);
        Assert.Equal((float)Math.Pow(10, -20 / 20d), RangeClickMixer.OutputGainLinear);
        Assert.Equal(0.1f, RangeClickMixer.OutputGainLinear, 5);
    }

    [Fact]
    public void Mixer_Advance_AttenuatesBelowUnity()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSample([1f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0]);

        var heard = mixer.Advance(0);
        Assert.NotEqual(1f, heard);
        Assert.Equal(RangeClickMixer.OutputGainLinear, heard, 5);
        Assert.Equal(
            RangeClickMixer.OutputGainDb,
            20d * Math.Log10(heard),
            3);
    }

    [Fact]
    public void Playback_MixesAttenuatedClickNotFullScale()
    {
        var document = new AudioDocument(new float[4 * 2], 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, new WaveSelection(0, 4), loop: false);
        provider.SetRangeClickSample([1f], 48000);
        provider.SetRangeClickFrames([0]);

        var buffer = new float[2];
        Assert.Equal(2, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0.1f, buffer[0], 5);
        Assert.Equal(RangeClickMixer.OutputGainLinear, buffer[0], 5);
        Assert.Equal(buffer[0], buffer[1], 5);
    }

    [Fact]
    public void Mixer_Advance_EmitsSampleWhenSourceHitsTrigger()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSample([1f, 0.5f, 0.25f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([2, 6]);

        Assert.Equal(0f, mixer.Advance(0));
        Assert.Equal(0f, mixer.Advance(1));
        Assert.Equal(Gain(1f), mixer.Advance(2), 5);
        Assert.Equal(Gain(0.5f), mixer.Advance(3), 5);
        Assert.Equal(Gain(0.25f), mixer.Advance(4), 5);
        Assert.Equal(0f, mixer.Advance(5));
        Assert.Equal(Gain(1f), mixer.Advance(6), 5);
        Assert.Equal(Gain(0.5f), mixer.Advance(7), 5);
    }

    [Fact]
    public void Mixer_Advance_DoesNotRetriggerWhileSourceStays()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSample([1f, 0.5f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0]);

        Assert.Equal(Gain(1f), mixer.Advance(0), 5);
        Assert.Equal(Gain(0.5f), mixer.Advance(0), 5);
        Assert.Equal(0f, mixer.Advance(0));
        Assert.Equal(0f, mixer.Advance(0));
    }

    [Fact]
    public void Mixer_Advance_RetriggersAfterLoopWrap()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSample([1f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0, 4]);

        Assert.Equal(Gain(1f), mixer.Advance(0), 5);
        Assert.Equal(0f, mixer.Advance(1));
        Assert.Equal(0f, mixer.Advance(2));
        Assert.Equal(0f, mixer.Advance(3));
        Assert.Equal(Gain(1f), mixer.Advance(4), 5);
        Assert.Equal(Gain(1f), mixer.Advance(0), 5);
    }

    [Fact]
    public void Mixer_ClearTriggers_StopsVoice()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSample([1f, 1f, 1f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0]);
        Assert.Equal(Gain(1f), mixer.Advance(0), 5);

        mixer.SetTriggers([]);
        Assert.False(mixer.Enabled);
        Assert.Equal(0f, mixer.Advance(1));
        Assert.Equal(0f, mixer.Advance(2));
    }

    [Fact]
    public void Playback_MixesClickOntoSilentDocument()
    {
        var document = new AudioDocument(new float[10 * 2], 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, new WaveSelection(0, 10), loop: true);
        provider.SetRangeClickSample([1f, 0.5f], 48000);
        provider.SetRangeClickFrames([2, 6]);

        var buffer = new float[10 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(0f, buffer[0], 5);
        Assert.Equal(0f, buffer[1], 5);
        Assert.Equal(0f, buffer[2], 5);
        Assert.Equal(0f, buffer[3], 5);
        Assert.Equal(Gain(1f), buffer[4], 5);
        Assert.Equal(Gain(1f), buffer[5], 5);
        Assert.Equal(Gain(0.5f), buffer[6], 5);
        Assert.Equal(Gain(0.5f), buffer[7], 5);
        Assert.Equal(0f, buffer[8], 5);
        Assert.Equal(Gain(1f), buffer[12], 5);
        Assert.Equal(Gain(1f), buffer[13], 5);
        Assert.Equal(Gain(0.5f), buffer[14], 5);
        Assert.Equal(Gain(0.5f), buffer[15], 5);
    }

    [Fact]
    public void Playback_LoopWrap_RetriggersStartClick()
    {
        var document = new AudioDocument(new float[8 * 2], 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, new WaveSelection(0, 8), loop: true);
        provider.SetRangeClickSample([1f], 48000);
        provider.SetRangeClickFrames([0, 4]);

        var first = new float[8 * 2];
        Assert.Equal(first.Length, provider.Read(first, 0, first.Length));
        Assert.Equal(Gain(1f), first[0], 5);
        Assert.Equal(Gain(1f), first[8], 5);

        var wrapped = new float[2];
        Assert.Equal(wrapped.Length, provider.Read(wrapped, 0, wrapped.Length));
        Assert.Equal(Gain(1f), wrapped[0], 5);
        Assert.Equal(Gain(1f), wrapped[1], 5);
    }

    [Fact]
    public void Playback_ClearFrames_StopsClick()
    {
        var document = new AudioDocument(new float[8 * 2], 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, new WaveSelection(0, 8), loop: false);
        provider.SetRangeClickSample([1f, 1f, 1f, 1f], 48000);
        provider.SetRangeClickFrames([0]);

        var first = new float[2];
        Assert.Equal(first.Length, provider.Read(first, 0, first.Length));
        Assert.Equal(Gain(1f), first[0], 5);

        provider.SetRangeClickFrames([]);
        var rest = new float[4];
        Assert.Equal(rest.Length, provider.Read(rest, 0, rest.Length));
        Assert.All(rest, value => Assert.Equal(0f, value, 5));
    }

    [Fact]
    public void Mixer_FourBeats_PlaysHighLowLowLow()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSamples(low: [1f], high: [2f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0, 10, 20, 30, 40], groupSize: 4);

        Assert.Equal(Gain(2f), mixer.Advance(0), 5);
        Assert.Equal(Gain(1f), mixer.Advance(10), 5);
        Assert.Equal(Gain(1f), mixer.Advance(20), 5);
        Assert.Equal(Gain(1f), mixer.Advance(30), 5);
        Assert.Equal(Gain(2f), mixer.Advance(40), 5);
        Assert.Equal(Gain(2f), mixer.Advance(0), 5);
    }

    [Fact]
    public void Mixer_ThreeBeats_PlaysHighLowLow()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSamples(low: [1f], high: [2f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0, 10, 20, 30], groupSize: 3);

        Assert.Equal(Gain(2f), mixer.Advance(0), 5);
        Assert.Equal(Gain(1f), mixer.Advance(10), 5);
        Assert.Equal(Gain(1f), mixer.Advance(20), 5);
        Assert.Equal(Gain(2f), mixer.Advance(30), 5);
        Assert.Equal(Gain(2f), mixer.Advance(0), 5);
    }

    [Fact]
    public void Mixer_NoMeter_UsesLowForEveryBeat()
    {
        var mixer = new RangeClickMixer();
        mixer.SetSamples(low: [1f], high: [2f], 48000);
        mixer.SetDeviceRate(48000);
        mixer.SetTriggers([0, 10, 20, 30, 40], groupSize: 0);

        Assert.Equal(Gain(1f), mixer.Advance(0), 5);
        Assert.Equal(Gain(1f), mixer.Advance(10), 5);
        Assert.Equal(Gain(1f), mixer.Advance(20), 5);
        Assert.Equal(Gain(1f), mixer.Advance(30), 5);
        Assert.Equal(Gain(1f), mixer.Advance(40), 5);
    }

    [Fact]
    public void Meter_PrefersFourOverThreeWhenBothDivide()
    {
        Assert.Equal(4, RangeClickMeter.GroupSize(4));
        Assert.Equal(4, RangeClickMeter.GroupSize(8));
        Assert.Equal(4, RangeClickMeter.GroupSize(12));
        Assert.Equal(3, RangeClickMeter.GroupSize(3));
        Assert.Equal(3, RangeClickMeter.GroupSize(6));
        Assert.Equal(3, RangeClickMeter.GroupSize(9));
        Assert.Equal(0, RangeClickMeter.GroupSize(1));
        Assert.Equal(0, RangeClickMeter.GroupSize(2));
        Assert.Equal(0, RangeClickMeter.GroupSize(5));
        Assert.Equal(0, RangeClickMeter.GroupSize(0));
    }

    [Fact]
    public void Meter_BeatCount_DropsExclusiveEnd()
    {
        var range = new WaveSelection(0, 80);
        Assert.Equal(4, RangeClickMeter.BeatCount([0, 20, 40, 60, 80], range));
        Assert.Equal(3, RangeClickMeter.BeatCount([0, 80 / 3, 160 / 3, 80], range));
        Assert.Equal(4, RangeClickMeter.BeatCount([0, 20, 40, 60], range));
        Assert.Equal(0, RangeClickMeter.BeatCount([], range));
    }

    [Fact]
    public void Playback_FourBeats_MixesHighThenLows()
    {
        var document = new AudioDocument(new float[8 * 2], 48000, 2, 16, AudioFileKind.Wave, null);
        var provider = new PlaybackSampleProvider();
        provider.SetDeviceSampleRate(48000);
        provider.Bind(document, 0, new WaveSelection(0, 8), loop: true);
        provider.SetRangeClickSamples(low: [0.3f], high: [0.8f], 48000);
        provider.SetRangeClickFrames([0, 2, 4, 6, 8], groupSize: 4);

        var buffer = new float[8 * 2];
        Assert.Equal(buffer.Length, provider.Read(buffer, 0, buffer.Length));
        Assert.Equal(Gain(0.8f), buffer[0], 5);
        Assert.Equal(Gain(0.8f), buffer[1], 5);
        Assert.Equal(Gain(0.3f), buffer[4], 5);
        Assert.Equal(Gain(0.3f), buffer[8], 5);
        Assert.Equal(Gain(0.3f), buffer[12], 5);
    }

    [Fact]
    public void LoadLow_ReadsEmbeddedWav()
    {
        var (samples, rate) = RangeClickSample.LoadLow();
        Assert.True(samples.Length > 0);
        Assert.InRange(rate, 8000, 192000);
        Assert.Contains(samples, sample => Math.Abs(sample) > 0.01f);
    }

    [Fact]
    public void LoadHigh_ReadsEmbeddedWav()
    {
        var (samples, rate) = RangeClickSample.LoadHigh();
        Assert.True(samples.Length > 0);
        Assert.InRange(rate, 8000, 192000);
        Assert.Contains(samples, sample => Math.Abs(sample) > 0.01f);
    }
}
