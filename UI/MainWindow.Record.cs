using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private readonly AudioRecorder _recorder = new();
    private readonly DispatcherTimer _recordTimer;
    private DocumentSession? _recordSession;
    private float[] _recordPrefix = [];
    private int _recordSettledTakeSamples;
    private int _recordChromeTick;
    private bool _recording;
    private bool _recordUsedSilentSkip;

    public bool IsRecording => _recording;

    private void ToggleRecording()
    {
        if (_recording)
        {
            StopRecording();
            return;
        }

        StartRecording();
    }

    private void StartRecording()
    {
        if (IsUiBusy || _recording)
        {
            return;
        }

        if (!ConfirmRecordOptions())
        {
            return;
        }

        StopPlaybackForEdit();
        if (_outputSettings.Api == AudioOutputApi.Asio)
        {
            _player.ReleaseOutput();
        }

        var settings = AppStorage.Settings;
        var speaker = settings.ResolvedSpeaker();
        var layout = settings.ResolvedRecordLayout();
        try
        {
            _recorder.Start(
                _outputSettings,
                settings.ResolvedRecordDeviceId(),
                layout,
                speaker.RecordInputMap,
                speaker.FileChannelMap);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(ex.Message) ? UiStrings.ErrorRecordFailed : ex.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var session = FindContinuableRecording(_recorder.SampleRate, _recorder.Channels);
        if (session is null)
        {
            var channels = Math.Max(1, _recorder.Channels);
            var document = new AudioDocument(
                [],
                _recorder.SampleRate,
                channels,
                24,
                AudioFileKind.Wave,
                null)
            {
                CanContinueRecording = true,
            };
            document.SetDirty(true);
            session = new DocumentSession(document);
            _sessions.Add(session);
        }

        _recordPrefix = CopyUsedSamples(session.Document);
        if (_recordPrefix.Length > 0)
        {
            _recorder.MarkTakeHasAudio();
        }

        _recordSettledTakeSamples = 0;
        _recordChromeTick = 0;
        _recordSession = session;
        _recordUsedSilentSkip = SilentSkipCheck.IsChecked == true;
        _recording = true;
        ActivateSession(session);
        Waveform.SetLiveRecording(true);
        Waveform.RevealFrame(session.Document.FrameCount);
        Overview.Refresh();
        Transport.SetRecording(true);
        _recordTimer.Start();
        FlushRecordedAudio();
    }

    private bool ConfirmRecordOptions()
    {
        var result = ConfirmRecordSilentSkipWindow.Show(
            this,
            AppStorage.Settings.ResolvedSilentSkipThresholdDb(),
            AppStorage.Settings.ResolvedSilentSkipRecordPadMs(),
            SilentSkipCheck.IsChecked == true,
            AppStorage.Settings.SilentSkipRecordAddRegion);
        if (result.Choice == ConfirmRecordSilentSkipChoice.Cancel)
        {
            return false;
        }

        AppStorage.Settings.SilentSkipRecordAddRegion = result.AddRegion;
        SilentSkipCheck.IsChecked = result.UseSilentSkip;
        AppStorage.Settings.SilentSkip = result.UseSilentSkip;
        AppStorage.Save();
        ApplySilentSkipFromSettings();
        return true;
    }

    private void StopRecording()
    {
        if (!_recording && !_recorder.IsRecording)
        {
            Transport.SetRecording(false);
            Waveform.SetLiveRecording(false);
            return;
        }

        _recordTimer.Stop();
        float[] samples;
        RecordedSpan[] spans;
        var rate = _recorder.SampleRate;
        var channels = _recorder.Channels;
        try
        {
            samples = _recorder.StopAndTake();
            spans = _recorder.TakeWrittenSpans();
        }
        catch
        {
            samples = [];
            spans = [];
        }

        _recording = false;
        Transport.SetRecording(false);
        var session = _recordSession;
        _recordSession = null;
        if (session is null)
        {
            _recordPrefix = [];
            _recordUsedSilentSkip = false;
            Waveform.SetLiveRecording(false);
            return;
        }

        var takeStart = PrefixFrameCount(_recordPrefix.Length, channels);
        ApplyRecordedTake(session.Document, samples, rate, channels, rebuildPeaks: false);
        session.Document.CommitLiveSamples(rebuildPeaks: true);
        TryAddSilentSkipRecordRegion(session, takeStart, spans);
        session.Document.CursorFrame = takeStart;
        session.PlayheadFrame = takeStart;
        _recordPrefix = [];
        _recordSettledTakeSamples = 0;
        _recordChromeTick = 0;
        _recordUsedSilentSkip = false;
        Waveform.SetLiveRecording(false);
        if (ReferenceEquals(_document, session.Document))
        {
            AfterEdit();
        }
        else
        {
            RefreshTabHeaders();
        }
    }

    private void TryAddSilentSkipRecordRegion(
        DocumentSession session,
        long takeStart,
        RecordedSpan[] spans)
    {
        if (!_recordUsedSilentSkip || !AppStorage.Settings.SilentSkipRecordAddRegion)
        {
            return;
        }

        var document = session.Document;
        if (!document.AllowsRegionsAndLoops)
        {
            return;
        }

        var added = SilentSkip.RecordPartRegions(spans, takeStart);
        if (added.Length == 0)
        {
            return;
        }

        var before = document.SnapshotRegions();
        var after = new List<WaveRegion>(before);
        var any = false;
        foreach (var region in added)
        {
            if (document.RegionNumber(region.Range) > 0)
            {
                continue;
            }

            after.Add(region);
            any = true;
        }

        if (!any)
        {
            return;
        }

        var command = ProcessEdits.ApplyRegions(
            document,
            before,
            after.ToArray(),
            SilentSkip.RecordTakeRegion(takeStart, document.FrameCount));
        if (command is null)
        {
            return;
        }

        session.History.Do(document, command);
    }

    private void OnRecordTimerTick()
    {
        if (!_recording)
        {
            _recordTimer.Stop();
            return;
        }

        FlushRecordedAudio();
    }

    private void FlushRecordedAudio()
    {
        if (_recordSession is null)
        {
            return;
        }

        var document = _recordSession.Document;
        var channels = Math.Max(1, _recorder.Channels);
        var prefix = _recordPrefix.Length;
        var have = document.SampleCount;
        if (have < prefix)
        {
            ApplyRecordedTake(document, _recorder.Snapshot(), _recorder.SampleRate, channels, rebuildPeaks: true);
            _recordSettledTakeSamples = Math.Max(0, document.SampleCount - prefix);
            RefreshRecordChrome(rebuildWaveform: true, rebuildOverview: true);
            return;
        }

        var delta = _recorder.SnapshotFrom(have - prefix);
        if (delta.Length < channels)
        {
            RefreshRecordChrome(rebuildWaveform: false, rebuildOverview: false);
            return;
        }

        document.CanContinueRecording = true;
        document.AppendLiveSamples(delta);
        var takeSamples = document.SampleCount - prefix;
        var settle = RecordContinue.DrawSettleFrames(_recorder.SampleRate) * channels;
        var rebuildPeaks = takeSamples - _recordSettledTakeSamples >= settle;
        if (rebuildPeaks)
        {
            document.RefreshPeaks();
            _recordSettledTakeSamples = takeSamples;
        }

        document.CursorFrame = document.FrameCount;
        RefreshRecordChrome(rebuildWaveform: true, rebuildOverview: rebuildPeaks);
    }

    private void ApplyRecordedTake(
        AudioDocument document,
        float[] take,
        int sampleRate,
        int channels,
        bool rebuildPeaks)
    {
        channels = Math.Max(1, channels);
        if (take.Length < channels)
        {
            take = [];
        }

        document.CanContinueRecording = true;
        var prefixLength = _recordPrefix.Length;
        var target = prefixLength + take.Length;
        var have = document.SampleCount;
        if (have < prefixLength || target < have)
        {
            document.ReplaceAudio(CombinePrefixAndTake(_recordPrefix, take), sampleRate, channels, 24, rebuildPeaks: true);
        }
        else if (target > have)
        {
            document.AppendLiveSamples(take.AsSpan(have - prefixLength));
        }

        if (rebuildPeaks)
        {
            document.RefreshPeaks();
        }
    }

    private static int PrefixFrameCount(int prefixSamples, int channels)
    {
        channels = Math.Max(1, channels);
        return Math.Max(0, prefixSamples / channels);
    }

    private static float[] CopyUsedSamples(AudioDocument document)
    {
        var used = document.SampleCount;
        if (used == 0)
        {
            return [];
        }

        var copy = new float[used];
        Array.Copy(document.Interleaved, copy, used);
        return copy;
    }

    private static float[] CombinePrefixAndTake(float[] prefix, float[] take)
    {
        var combined = new float[prefix.Length + take.Length];
        if (prefix.Length > 0)
        {
            Array.Copy(prefix, combined, prefix.Length);
        }

        if (take.Length > 0)
        {
            Array.Copy(take, 0, combined, prefix.Length, take.Length);
        }

        return combined;
    }

    private void RefreshRecordChrome(bool rebuildWaveform, bool rebuildOverview)
    {
        if (_recordSession is null || !ReferenceEquals(_document, _recordSession.Document))
        {
            return;
        }

        Waveform.SetLiveRecording(true);
        Waveform.PlayheadFrame = _recordSession.Document.FrameCount;
        if (rebuildWaveform)
        {
            Waveform.RevealFrame(_recordSession.Document.FrameCount);
            Waveform.RefreshLive(rebuildOverview);
        }

        if (rebuildOverview)
        {
            Overview.Refresh();
        }

        _recordChromeTick++;
        if ((_recordChromeTick & 3) == 0)
        {
            SyncViewChrome();
            RefreshStatus();
            RefreshTabHeaders();
        }
    }

    private DocumentSession? FindContinuableRecording(int sampleRate, int channels)
    {
        if (_activeSession is not null
            && RecordContinue.Matches(_activeSession.Document, sampleRate, channels))
        {
            return _activeSession;
        }

        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            if (RecordContinue.Matches(_sessions[i].Document, sampleRate, channels))
            {
                return _sessions[i];
            }
        }

        return null;
    }
}
