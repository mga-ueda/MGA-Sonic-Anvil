using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private readonly AudioRecorder _recorder = new();
    private readonly DispatcherTimer _recordTimer;
    private DocumentSession? _recordSession;
    private bool _recording;

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

        var channels = Math.Max(1, _recorder.Channels);
        var document = new AudioDocument(
            new float[channels],
            _recorder.SampleRate,
            channels,
            24,
            AudioFileKind.Wave,
            null);
        var session = new DocumentSession(document);
        _sessions.Add(session);
        _recordSession = session;
        _recording = true;
        ActivateSession(session);
        Transport.SetRecording(true);
        _recordTimer.Start();
        FlushRecordedAudio();
    }

    private void StopRecording()
    {
        if (!_recording && !_recorder.IsRecording)
        {
            Transport.SetRecording(false);
            return;
        }

        _recordTimer.Stop();
        float[] samples;
        var rate = _recorder.SampleRate;
        var channels = _recorder.Channels;
        try
        {
            samples = _recorder.StopAndTake();
        }
        catch
        {
            samples = [];
        }

        _recording = false;
        Transport.SetRecording(false);
        var session = _recordSession;
        _recordSession = null;
        if (session is null)
        {
            return;
        }

        if (samples.Length < channels)
        {
            samples = new float[channels];
        }

        session.Document.ReplaceAudio(samples, rate, channels, 24);
        if (ReferenceEquals(_document, session.Document))
        {
            AfterEdit();
        }
        else
        {
            RefreshTabHeaders();
        }
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

        var samples = _recorder.Snapshot();
        var channels = Math.Max(1, _recorder.Channels);
        if (samples.Length < channels)
        {
            samples = new float[channels];
        }

        _recordSession.Document.ReplaceAudio(samples, _recorder.SampleRate, channels, 24);
        if (!ReferenceEquals(_document, _recordSession.Document))
        {
            return;
        }

        Waveform.InvalidateSpectrogramCache();
        Waveform.Refresh();
        Overview.Refresh();
        SyncViewChrome();
        RefreshStatus();
        RefreshTabHeaders();
    }
}
