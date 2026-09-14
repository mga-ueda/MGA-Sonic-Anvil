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
    private float[] _recordBefore = [];
    private long _recordStartFrame;
    private int _recordTakeFrames;
    private int _recordSettledTakeFrames;
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
        if (IsUiBusy || _recording || _activeSession is not { } session)
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

        var document = session.Document;
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
                speaker.FileChannelMap,
                document.Channels,
                document.SampleRate);
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

        _recordStartFrame = Math.Clamp(Waveform.PlayheadFrame, 0, document.FrameCount);
        var tailFrames = document.FrameCount - _recordStartFrame;
        _recordBefore = tailFrames > 0 ? document.CopyRange(_recordStartFrame, tailFrames) : [];
        _recordTakeFrames = 0;
        _recordSettledTakeFrames = 0;
        _recordChromeTick = 0;
        _recordSession = session;
        _recordUsedSilentSkip = SilentSkipCheck.IsChecked == true;
        _recording = true;
        ActivateSession(session);
        document.RefreshPeaks();
        Waveform.SetLiveRecording(true);
        Waveform.SetLiveRecordSettledFrame(_recordStartFrame);
        Waveform.PlayheadFrame = _recordStartFrame;
        Waveform.RevealFrame(_recordStartFrame);
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
        var start = _recordStartFrame;
        var before = _recordBefore;
        _recordSession = null;
        _recordBefore = [];
        _recordTakeFrames = 0;
        _recordSettledTakeFrames = 0;
        _recordChromeTick = 0;
        if (session is null)
        {
            _recordUsedSilentSkip = false;
            Waveform.SetLiveRecording(false);
            return;
        }

        var document = session.Document;
        var take = PrepareRecordTake(samples, document, spans, fadeOpenTail: true);
        var command = ProcessEdits.RecordOverwrite(document, start, before, take);
        if (command is null)
        {
            document.WriteLiveFrom(start, before);
            document.CommitLiveSamples(rebuildPeaks: true);
            Waveform.SetLiveRecording(false);
            _recordUsedSilentSkip = false;
            Waveform.PlayheadFrame = start;
            session.PlayheadFrame = start;
            if (ReferenceEquals(_document, document))
            {
                AfterEdit();
            }
            else
            {
                RefreshTabHeaders();
            }

            return;
        }

        var after = ProcessEdits.CombineTakeAndLeftover(take, before, document.Channels);
        document.WriteLiveFrom(start, after);
        document.CommitLiveSamples(rebuildPeaks: true);
        session.History.AcceptApplied(document, command);
        document.CaptureFormatOrigin();
        var takeEnd = start + (take.Length / Math.Max(1, document.Channels));
        TryAddSilentSkipRecordRegion(session, start, takeEnd, spans);
        document.CursorFrame = takeEnd;
        session.PlayheadFrame = takeEnd;
        Waveform.PlayheadFrame = takeEnd;
        _recordUsedSilentSkip = false;
        Waveform.SetLiveRecording(false);
        if (ReferenceEquals(_document, document))
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
        long takeEnd,
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
            SilentSkip.RecordTakeRegion(takeStart, takeEnd));
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
        var take = PrepareRecordTake(
            _recorder.Snapshot(),
            document,
            _recorder.TakeWrittenSpans(),
            fadeOpenTail: false);
        var after = ProcessEdits.CombineTakeAndLeftover(take, _recordBefore, document.Channels);
        document.WriteLiveFrom(_recordStartFrame, after);
        _recordTakeFrames = take.Length / Math.Max(1, document.Channels);
        var settle = RecordContinue.DrawSettleFrames(document.SampleRate);
        var rebuildPeaks = _recordTakeFrames - _recordSettledTakeFrames >= settle;
        if (rebuildPeaks)
        {
            document.RefreshPeaks();
            _recordSettledTakeFrames = _recordTakeFrames;
        }

        document.CursorFrame = _recordStartFrame + _recordTakeFrames;
        RefreshRecordChrome(rebuildWaveform: true, rebuildOverview: rebuildPeaks);
    }

    private float[] PrepareRecordTake(
        float[] samples,
        AudioDocument document,
        RecordedSpan[] spans,
        bool fadeOpenTail)
    {
        var take = MatchRecordTake(samples, document);
        if (_recordUsedSilentSkip && take.Length > 0)
        {
            ClickGuard.FadeRecordedAudio(
                take,
                document.Channels,
                spans,
                document.SampleRate,
                _recorder.SampleRate,
                fadeOpenTail,
                AppStorage.Settings.ResolvedClickGuardFadeMs());
        }

        return take;
    }

    private float[] MatchRecordTake(float[] take, AudioDocument document)
    {
        var channels = Math.Max(1, document.Channels);
        if (take.Length < channels)
        {
            return [];
        }

        var aligned = take.Length - (take.Length % channels);
        if (aligned != take.Length)
        {
            if (aligned <= 0)
            {
                return [];
            }

            var trimmed = new float[aligned];
            Array.Copy(take, trimmed, aligned);
            take = trimmed;
        }

        var sourceRate = _recorder.SampleRate;
        if (sourceRate >= 1000 && sourceRate != document.SampleRate)
        {
            take = FormatConvert.Resample(take, channels, sourceRate, document.SampleRate);
        }

        return take;
    }

    private void RefreshRecordChrome(bool rebuildWaveform, bool rebuildOverview)
    {
        if (_recordSession is null || !ReferenceEquals(_document, _recordSession.Document))
        {
            return;
        }

        var playhead = _recordStartFrame + _recordTakeFrames;
        Waveform.SetLiveRecording(true);
        Waveform.SetLiveRecordSettledFrame(_recordStartFrame + _recordSettledTakeFrames);
        Waveform.PlayheadFrame = playhead;
        if (rebuildWaveform)
        {
            Waveform.RevealFrame(playhead);
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
}
