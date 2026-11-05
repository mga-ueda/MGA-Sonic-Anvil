namespace MgaSonicAnvil.Audio;

/// <summary>
/// process に載せる再生位置。PositionValid が false のときはオフライン用の自走時計。
/// </summary>
internal readonly record struct VstTransport(bool Playing, long ProjectTimeSamples, bool PositionValid)
{
    public static VstTransport FreeRunning => new(true, 0, false);

    public static VstTransport Live(bool playing, long projectTimeSamples) =>
        new(playing, Math.Max(0, projectTimeSamples), true);

    /// <summary>
    /// プラグイン出力は getLatencySamples だけ遅れる。再生中に送る時刻は、今読んでいる位置からその分戻した聞こえる位置。
    /// 停止中はシークした位置のまま（クランプで頭に張り付かない）。
    /// </summary>
    public static long AudibleProjectSamples(long projectSamples, int latencySamples, bool playing)
    {
        if (projectSamples < 0)
        {
            projectSamples = 0;
        }

        if (!playing || latencySamples <= 0)
        {
            return projectSamples;
        }

        return projectSamples > latencySamples ? projectSamples - latencySamples : 0;
    }

    /// <summary>描画用カーソル。デバイス側の遅延をソースフレームへ換算して戻す。</summary>
    public static long AudibleSourceFrame(
        long sourceFrame,
        int latencyDeviceSamples,
        int sourceRate,
        int deviceRate,
        bool playing)
    {
        if (!playing || latencyDeviceSamples <= 0 || sourceFrame <= 0)
        {
            return Math.Max(0, sourceFrame);
        }

        var shift = (long)Math.Round(
            latencyDeviceSamples * (double)Math.Max(1, sourceRate) / Math.Max(1, deviceRate));
        return sourceFrame > shift ? sourceFrame - shift : 0;
    }
}

/// <summary>
/// VST3 ProcessContext の再生フラグと時刻。停止中はプロジェクト時刻を進めない。
/// </summary>
internal static class VstTransportClock
{
    public const double Tempo = 120;

    public readonly record struct Step(
        bool Playing,
        long ProjectTimeSamples,
        long ContinuousTimeSamples,
        long NextFreeProjectTime,
        long NextContinuousTime,
        int NextFrameCursor);

    public static Step Advance(
        VstTransport transport,
        long freeProjectTime,
        long continuousTime,
        int frameCursor,
        int frames)
    {
        frames = Math.Max(0, frames);
        if (!transport.PositionValid)
        {
            return new Step(
                true,
                freeProjectTime,
                continuousTime,
                freeProjectTime + frames,
                continuousTime + frames,
                frameCursor + frames);
        }

        var playing = transport.Playing;
        var project = playing
            ? transport.ProjectTimeSamples + frameCursor
            : transport.ProjectTimeSamples;
        return new Step(
            playing,
            project,
            continuousTime,
            freeProjectTime,
            playing ? continuousTime + frames : continuousTime,
            frameCursor + frames);
    }

    public static Vst3HostNative.ProcessContext Context(Step step, double sampleRate, long systemTimeNs)
    {
        var rate = sampleRate > 0 ? sampleRate : 48000;
        var state = Vst3HostNative.ProcessContextSystemTimeValid
            | Vst3HostNative.ProcessContextContTimeValid
            | Vst3HostNative.ProcessContextTempoValid
            | Vst3HostNative.ProcessContextTimeSigValid
            | Vst3HostNative.ProcessContextProjectTimeMusicValid;
        if (step.Playing)
        {
            state |= Vst3HostNative.ProcessContextPlaying;
        }

        return new Vst3HostNative.ProcessContext
        {
            State = unchecked((uint)state),
            SampleRate = rate,
            ProjectTimeSamples = step.ProjectTimeSamples,
            SystemTime = systemTimeNs,
            ContinousTimeSamples = step.ContinuousTimeSamples,
            ProjectTimeMusic = step.ProjectTimeSamples / rate * (Tempo / 60.0),
            Tempo = Tempo,
            TimeSigNumerator = 4,
            TimeSigDenominator = 4,
        };
    }
}
