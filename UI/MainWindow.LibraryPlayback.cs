using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>
    /// キー操作による本再生を <see cref="LibraryPlayerMode.InteractivePlayCoalesceMs"/> だけ合流待ちしてから
    /// 1 回だけ実行する。連打しても最後に落ち着いた曲だけが頭から鳴り、曲頭が 2〜3 回重なる
    /// （「ジャジャジャーン」）のを防ぐ。先読みの自動送り（ギャップレス）はこの経路を通らない。
    /// </summary>
    private void QueueInteractiveLibraryPlay(
        DocumentSession? session,
        bool stopAfterTrack = false,
        bool keepAmbientGlow = false,
        bool fromStart = false)
    {
        _interactivePlaySession = session;
        _interactivePlayStopAfterTrack = stopAfterTrack;
        _interactivePlayKeepAmbientGlow = keepAmbientGlow;
        _interactivePlayFromStart = fromStart;
        _interactivePlayScheduledGeneration = ++_interactivePlayGeneration;

        if (_interactivePlayTimer is null)
        {
            _interactivePlayTimer = new DispatcherTimer(DispatcherPriority.Input, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.InteractivePlayCoalesceMs),
            };
            _interactivePlayTimer.Tick += OnInteractiveLibraryPlayTick;
        }

        // 連打は最後の 1 回に畳む（トレーリング）。押すたびに貼り直す。
        _interactivePlayTimer.Stop();
        _interactivePlayTimer.Start();
    }

    /// <summary>予約済みの対話本再生を取り消す（明示の停止・一時停止・別経路の再生が勝ったとき）。</summary>
    private void CancelInteractiveLibraryPlay()
    {
        _interactivePlayTimer?.Stop();
        _interactivePlaySession = null;
        // 世代を進め、万一ディスパッチ済みの Tick が後から来ても最新ではないとして捨てる。
        _interactivePlayGeneration++;
    }

    private void OnInteractiveLibraryPlayTick(object? sender, EventArgs e)
    {
        _interactivePlayTimer?.Stop();
        if (!LibraryPlayerMode.IsLatestInteractivePlay(
                _interactivePlayScheduledGeneration,
                _interactivePlayGeneration))
        {
            return;
        }

        // 合流待ちのあいだにプレイヤーを抜けていたら鳴らさない。
        if (!IsLibraryMaximized)
        {
            _interactivePlaySession = null;
            return;
        }

        var session = _interactivePlaySession;
        var stopAfterTrack = _interactivePlayStopAfterTrack;
        var keepAmbientGlow = _interactivePlayKeepAmbientGlow;
        var fromStart = _interactivePlayFromStart;
        _interactivePlaySession = null;
        _ = PlayLibrarySessionAsync(session, stopAfterTrack, keepAmbientGlow, fromStart);
    }

    private void StepLibraryTrackSelection(int direction, bool wrap)
    {
        // 単発は再生中の曲から（端で回る）。長押しの続きは動いた選択から（端で止まる）。
        var cursor = wrap && IsPlaybackActive()
            ? (_activeSession ?? LibraryBrowser.SelectedSession)
            : (LibraryBrowser.SelectedSession ?? _activeSession);
        LibraryBrowser.SelectPlaylistStep(cursor, direction, wrap);
        _libraryPlayOnTrackKeyRelease = true;
    }

    /// <summary>
    /// 直前と同一の本再生要求（同じセッション・同じパラメータ）がダブルクリック間隔内に
    /// 再度来たら true。ダブルクリックの 2 連・3 連 Play を 1 回へ畳む。
    /// 別曲への切替・停止からの開始・意図的な再開（間隔を越える）は畳まない。
    /// <paramref name="fromStart"/>（テンキー 5 頭出し）は毎回効かせるので畳まない。
    /// </summary>
    private bool IsDuplicateLibraryPlayRequest(
        DocumentSession session,
        bool stopAfterTrack,
        bool keepAmbientGlow,
        bool fromStart)
    {
        var now = Stopwatch.GetTimestamp();
        var windowMs = Math.Max(200u, GetDoubleClickTime());
        var elapsedMs = (now - _lastLibraryPlayRequestTicks) * 1000.0 / Stopwatch.Frequency;
        // fromStart は明示頭出し。ダブルクリック抑制に巻き込むとテンキー 5 連打が効かなくなる。
        var duplicate = !fromStart
            && _lastLibraryPlayRequestSession is not null
            && ReferenceEquals(session, _lastLibraryPlayRequestSession)
            && stopAfterTrack == _lastLibraryPlayRequestStopAfterTrack
            && keepAmbientGlow == _lastLibraryPlayRequestKeepAmbientGlow
            && fromStart == _lastLibraryPlayRequestFromStart
            && elapsedMs <= windowMs;

        _lastLibraryPlayRequestSession = session;
        _lastLibraryPlayRequestStopAfterTrack = stopAfterTrack;
        _lastLibraryPlayRequestKeepAmbientGlow = keepAmbientGlow;
        _lastLibraryPlayRequestFromStart = fromStart;
        _lastLibraryPlayRequestTicks = now;
        return duplicate;
    }

    private void CancelLibraryGapless()
    {
        _gaplessToken++;
        _gaplessTarget = null;
        _gaplessInFlight = false;
        _gaplessFailed = false;
        _player.ClearGaplessNext();
    }

    private void ScheduleLibraryGaplessPrefetch()
    {
        if (!IsLibraryMaximized
            || !IsPlaybackActive()
            || _activeSession is null
            || _libraryStopAfterTrack
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled
            || LibraryPlaylistDocuments.IsVisual(_activeSession.Document))
        {
            return;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null || LibraryPlaylistDocuments.IsVisual(next.Document))
        {
            return;
        }

        if (ReferenceEquals(next, _gaplessTarget)
            && (_gaplessInFlight || _gaplessFailed || _player.HasGaplessArmed))
        {
            return;
        }

        _gaplessToken++;
        _player.ClearGaplessNext();
        _gaplessTarget = next;
        _gaplessInFlight = true;
        _gaplessFailed = false;
        var token = _gaplessToken;
        _ = ArmLibraryGaplessAfterPeaksAsync(next, token);
    }

    /// <summary>
    /// 次曲のピーク走査が終わってから先読みストリームを開く。
    /// 同じ MP3 を Media Foundation で二重に開くと、プレイリスト波形が壊れる。
    /// </summary>
    private async Task ArmLibraryGaplessAfterPeaksAsync(DocumentSession next, int token)
    {
        var startedPrefetch = false;
        try
        {
            if (_activeSession is { } playing)
            {
                await FillLibraryPeaksAsync(playing).ConfigureAwait(true);
            }

            if (token != _gaplessToken)
            {
                return;
            }

            await FillLibraryPeaksAsync(next).ConfigureAwait(true);
            if (token != _gaplessToken)
            {
                return;
            }

            startedPrefetch = true;
            await PrefetchLibraryGaplessAsync(next, token).ConfigureAwait(true);
        }
        catch
        {
            if (token == _gaplessToken)
            {
                _gaplessFailed = true;
            }
        }
        finally
        {
            if (!startedPrefetch && token == _gaplessToken)
            {
                _gaplessInFlight = false;
            }
        }
    }

    private async Task PrefetchLibraryGaplessAsync(DocumentSession next, int token)
    {
        AudioStreamSource? source = null;
        try
        {
            source = await Task.Run(() => OpenLibraryGaplessSource(next)).ConfigureAwait(true);
            if (token != _gaplessToken || source is null)
            {
                source?.Dispose();
                if (token == _gaplessToken && source is null)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            if (!IsLibraryMaximized
                || !IsPlaybackActive()
                || !ReferenceEquals(next, LibraryBrowser.NextPlaylistSession(_activeSession)))
            {
                source.Dispose();
                if (token == _gaplessToken)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            ScanLibraryArtwork(next);
            if (!_player.TryArmGapless(source, next.Document))
            {
                source.Dispose();
                _gaplessFailed = true;
            }
        }
        catch
        {
            source?.Dispose();
            if (token == _gaplessToken)
            {
                _gaplessFailed = true;
            }
        }
        finally
        {
            if (token == _gaplessToken)
            {
                _gaplessInFlight = false;
            }
        }
    }

    private static AudioStreamSource? OpenLibraryGaplessSource(DocumentSession next)
    {
        var document = next.Document;
        var path = document.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !AudioCodec.CanStreamPlay(path))
        {
            return null;
        }

        // 非圧縮はメモリ再生。ギャップレス先読みストリームには載せない。
        if (AudioCodec.IsUncompressedPcm(document.SourceKind)
            || (document.SourcePath is { Length: > 0 } nextPath && AudioCodec.IsUncompressedPcm(nextPath)))
        {
            return null;
        }

        // ActivateStreamPlayback は PCM とピークを捨てる。
        // 1曲のときは次曲も同じ曲なので、エディタで見えていた波形がプレイヤーへ戻すと消える。
        if (!document.IsStreamPlayback
            && document.Interleaved.Length == 0
            && document.Peaks.IsEmpty
            && !AudioCodec.TryActivateStreamPlayback(document)
            && !LibraryPlaylistDocuments.IsVideo(document))
        {
            return null;
        }

        return AudioPlayer.OpenLibraryPlaybackSource(document, prebufferTimeoutMs: 200);
    }

    private bool AdoptLibraryGaplessAdvance()
    {
        if (!IsLibraryMaximized
            || _libraryStopAfterTrack
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled
            || !_player.HasGaplessAdvancePending)
        {
            return false;
        }

        if (!_player.TryTakeGaplessAdvance(out var document) || document is null)
        {
            return false;
        }

        DocumentSession? session = null;
        foreach (var item in _sessions)
        {
            if (ReferenceEquals(item.Document, document))
            {
                session = item;
                break;
            }
        }

        if (session is null)
        {
            return false;
        }

        _gaplessTarget = null;
        _gaplessFailed = false;
        _activeSession = session;
        Waveform.Document = document;
        Overview.Document = document;
        Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
        Waveform.ResetTimeZoom();
        Waveform.ResetAmpZoom();
        Waveform.PlayheadFrame = _player.CursorFrame;
        // Document の差し替えが軌跡の記録を止める。再生は続いているので付け直す。
        Waveform.SetTrailRecording(true);
        LibraryBrowser.SelectSessionQuiet(session);
        LibraryBrowser.UpdateSessionRow(session);
        ShowLibraryArtwork(session);
        _ = FillLibraryPeaksAsync(session);
        Transport.SetPlaying(true);
        RefreshTitle();
        RefreshStatus();
        SyncMonitorLayout();
        SyncVstInsertEngine();
        ScheduleLibraryGaplessPrefetch();
        return true;
    }

    private bool TryAdvanceLibraryPlaylist()
    {
        if (_libraryStopAfterTrack
            || LibraryBrowser.AutoLoopEnabled
            || !LibraryBrowser.AutoPlayNextEnabled)
        {
            return false;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null)
        {
            return false;
        }

        _ = PlayLibrarySessionAsync(next);
        return true;
    }

    private bool TryStepLibraryPlaylist(int direction)
    {
        // 再生中は今の曲、停止中は選択行を基準。先頭の前→末尾、末尾の次→先頭。
        var cursor = IsPlaybackActive()
            ? (_activeSession ?? LibraryBrowser.SelectedSession)
            : (LibraryBrowser.SelectedSession ?? _activeSession);
        var session = direction < 0
            ? LibraryBrowser.PreviousPlaylistSession(cursor)
            : LibraryBrowser.NextPlaylistSession(cursor);
        if (session is null)
        {
            return false;
        }

        QueueInteractiveLibraryPlay(session);
        return true;
    }

    private void ToggleLibraryPlayPause()
    {
        if (IsPlaybackActive())
        {
            // 本再生中の一時停止では暗いプレビューへ移ってもクロームを出さない。
            PausePlaybackHere(immersiveVideoHold: IsPlaylistVideoPlaying());
            return;
        }

        _libraryStopAfterTrack = false;
        if (_document is not null)
        {
            _ = ResumeLibraryDocumentPlaybackAsync();
            return;
        }

        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    /// <summary>
    /// 既に載っているドキュメントをシーク位置から再開する。
    /// 動画でプロキシ未作成なら、自動生成設定に従って先に Ensure する。
    /// </summary>
    private async Task ResumeLibraryDocumentPlaybackAsync()
    {
        if (_document is null)
        {
            return;
        }

        var startFrame = _document.CursorFrame;
        if (_activeSession is { } session
            && !await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        StartPlayback(startFrame, prerollSeconds: 0);
        ScheduleLibraryGaplessPrefetch();
    }

    private void ClearPlaylistVideoImmersivePause(bool sync = true)
    {
        if (!_playlistVideoImmersivePause)
        {
            return;
        }

        _playlistVideoImmersivePause = false;
        if (sync)
        {
            SyncPlaylistVideoChromeFade();
        }
    }

    private void RestartLibraryTrack()
    {
        _libraryStopAfterTrack = false;
        var session = _activeSession ?? LibraryBrowser.SelectedSession;
        if (session is not null
            && IsLibraryMaximized
            && LibraryPlaylistDocuments.IsVideo(session.Document)
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo)
        {
            // 止め直すとクローム（プレイリスト）が一瞬見える。本再生のまま 0 へシークする。
            // プロキシ未作成のまま頭出しした場合も、ここで自動生成する。
            _ = RestartLibraryVideoFromStartAsync(session);
            return;
        }

        if (session is not null)
        {
            // fromStart: true で、直前の本再生（fromStart: false）と同一視して
            // IsDuplicateLibraryPlayRequest に畳まれないようにする（テンキー 5 頭出し）。
            QueueInteractiveLibraryPlay(session, fromStart: true);
            return;
        }

        if (_document is not null)
        {
            StartPlayback(0, prerollSeconds: 0);
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private async Task RestartLibraryVideoFromStartAsync(DocumentSession session)
    {
        session.Document.CursorFrame = 0;
        Waveform.PlayheadFrame = 0;
        if (!await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
        {
            return;
        }

        StartPlayback(0, prerollSeconds: 0);
    }

    private void SeekLibraryBySeconds(double seconds)
    {
        if (_document is null)
        {
            return;
        }

        NotePlaylistVideoSimpleSeekKeyboard();

        var frames = (long)Math.Round(_document.SampleRate * seconds);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        SeekFrame(
            current + frames,
            IsPlaybackActive() ? LibraryPlayerMode.SeekNudgeFadeMilliseconds : 0);
        if (_transportSelectionExtend)
        {
            Waveform.ExtendSelectionTo(Waveform.PlayheadFrame);
        }
    }

    private bool BeginOrContinueSeekNudge(int direction)
    {
        if (direction == 0 || _document is null)
        {
            return false;
        }

        if (_seekNudgeDirection == direction && _seekNudgeTimer.IsEnabled)
        {
            return true;
        }

        SeekLibraryBySeconds(
            direction < 0 ? -LibraryPlayerMode.SeekNudgeSeconds : LibraryPlayerMode.SeekNudgeSeconds);
        _seekNudgeDirection = direction;
        _seekNudgeRepeatStarted = false;
        _seekNudgeLastAt = Environment.TickCount64;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        _seekNudgeTimer.Start();
        return true;
    }

    private void OnSeekNudgeTick()
    {
        if (_seekNudgeDirection == 0 || !IsSeekNudgeHeld(_seekNudgeDirection))
        {
            StopSeekNudge();
            return;
        }

        var now = Environment.TickCount64;
        var seconds = _seekNudgeDirection < 0
            ? -LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted)
            : LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted);
        SeekLibraryBySeconds(seconds);

        _seekNudgeLastAt = now;
        if (_seekNudgeRepeatStarted)
        {
            return;
        }

        _seekNudgeRepeatStarted = true;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: true));
        _seekNudgeTimer.Start();
    }

    private bool IsSeekNudgeHeld(int direction)
    {
        if (_transportSelectionExtend)
        {
            // 選択付きシークは Shift＋J／K のみ（テンキーは割り当てなし）。
            var expected = direction < 0 ? 7 : 9;
            var keyDown = _transportSelectionDigit == expected
                || (direction < 0 ? Keyboard.IsKeyDown(Key.J) : Keyboard.IsKeyDown(Key.K));
            return keyDown
                && (Keyboard.Modifiers & ModifierKeys.Shift) != 0
                && (Keyboard.Modifiers & ~(ModifierKeys.Shift)) == 0;
        }

        var plainKeyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.NumPad7) || Keyboard.IsKeyDown(Key.J)
            : Keyboard.IsKeyDown(Key.NumPad9) || Keyboard.IsKeyDown(Key.K);
        return plainKeyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopSeekNudge()
    {
        var wasExtend = _transportSelectionExtend
            && _transportSelectionDigit is 7 or 9;
        _seekNudgeDirection = 0;
        _seekNudgeRepeatStarted = false;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        if (wasExtend)
        {
            _transportSelectionDigit = -1;
            _transportSelectionExtend = false;
            SyncPlayWindowFromDocument();
        }
    }

    private bool TryHandlePlaylistVideoFrameStep(Key key, ModifierKeys modifiers, bool isRepeat)
    {
        if (!LibraryPlayerMode.IsVideoFrameStepKey(key, modifiers)
            || !LibraryPlayerMode.CanStepPlaylistVideoFrame(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _playlistVideoImmersivePause))
        {
            return false;
        }

        // OS のキーリピートはタイマー側で扱う（最初だけウェイト）。
        if (isRepeat)
        {
            return true;
        }

        var direction = key == Key.Left ? -1 : 1;
        return BeginOrContinueVideoFrameStep(direction);
    }

    private bool BeginOrContinueVideoFrameStep(int direction)
    {
        if (direction == 0 || _document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return false;
        }

        if (_videoFrameStepDirection == direction && _videoFrameStepTimer.IsEnabled)
        {
            return true;
        }

        StopPlaybackShuttle();
        StopSeekNudge();
        StepPlaylistVideoFrame(direction);
        _videoFrameStepDirection = direction;
        _videoFrameStepRepeatStarted = false;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: false));
        _videoFrameStepTimer.Start();
        return true;
    }

    private void OnVideoFrameStepTick()
    {
        if (_videoFrameStepDirection == 0 || !IsVideoFrameStepHeld(_videoFrameStepDirection))
        {
            StopVideoFrameStep();
            return;
        }

        if (!LibraryPlayerMode.CanStepPlaylistVideoFrame(
                IsLibraryMaximized,
                LibraryBrowser.PlaylistVisualShown,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualPlaying,
                _playlistVideoImmersivePause))
        {
            StopVideoFrameStep();
            return;
        }

        StepPlaylistVideoFrame(_videoFrameStepDirection);
        if (_videoFrameStepRepeatStarted)
        {
            return;
        }

        _videoFrameStepRepeatStarted = true;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: true));
        _videoFrameStepTimer.Start();
    }

    private void StepPlaylistVideoFrame(int direction)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return;
        }

        if (IsPlaybackActive())
        {
            PausePlaybackHere(immersiveVideoHold: true);
        }
        else if (LibraryBrowser.PlaylistVisualPlaying || !_playlistVideoImmersivePause)
        {
            _playlistVideoImmersivePause = true;
            LibraryBrowser.PausePlaylistVisualFrame();
            SyncPlaylistVideoChromeFade();
        }

        var step = LibraryPlayerMode.VideoFrameStepSamples(
            _document.SampleRate,
            LibraryPlayerMode.VideoTimecodeFramesPerSecond);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        // 一時停止直後は音声ヘッドが止まるので、映像位置を優先する。
        if (!IsPlaybackActive() && LibraryBrowser.PlaylistVisualShown)
        {
            var rate = Math.Max(1, _document.SampleRate);
            current = LibraryPlayerMode.FrameFromSeconds(
                LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                rate,
                _document.FrameCount);
        }

        SeekFrame(current + direction * step);
        NotePlaylistVideoSimpleSeekKeyboard();
    }

    private static bool IsVideoFrameStepHeld(int direction)
    {
        var keyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.Left)
            : Keyboard.IsKeyDown(Key.Right);
        return keyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopVideoFrameStep()
    {
        _videoFrameStepDirection = 0;
        _videoFrameStepRepeatStarted = false;
        _videoFrameStepTimer.Stop();
        _videoFrameStepTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.VideoFrameStepTimerIntervalMs(repeatStarted: false));
    }

    private async Task PlayLibrarySessionAsync(
        DocumentSession? session,
        bool stopAfterTrack = false,
        bool keepAmbientGlow = false,
        bool fromStart = false)
    {
        // ダブルクリックは選択（SessionActivated）とダブルクリック（SessionPlayRequested）で
        // 同じ曲へ連続して入る。先読み済みで同期完了する経路では _libraryPlayTicket では
        // 潰せず、新しい曲の頭が 2〜3 回鳴る。直前と同一の要求はダブルクリック間隔内で畳む。
        if (session is not null && IsDuplicateLibraryPlayRequest(session, stopAfterTrack, keepAmbientGlow, fromStart))
        {
            return;
        }

        // ここまで来た本再生が勝ち。合流待ち中の対話予約があれば畳む（このまま鳴らす）。
        CancelInteractiveLibraryPlay();

        StopVideoFrameStep();
        CancelLibraryGapless();
        _libraryPlayOnArrowRelease = false;
        _libraryPlayOnTrackKeyRelease = false;
        _libraryStopAfterTrack = stopAfterTrack;
        var playTicket = ++_libraryPlayTicket;
        if (session is null)
        {
            ClearPlaylistVisualChromeBridge();
            if (!keepAmbientGlow)
            {
                LibraryBrowser.SetArtwork(null);
            }

            return;
        }

        // フレーム一時停止からの次曲は本再生へつなぐ（暗いプレビューに落とさない）。
        var immersiveHold = _playlistVideoImmersivePause;
        ClearPlaylistVideoImmersivePause(sync: false);
        // 速度ランプ中にプロキシ準備などを await すると低速のまま待たされるので、
        // 同じ動画の暗いプレビューなら先に 1 倍・明るい本再生映像へ戻す。
        var promoteDimPreview = LibraryPlayerMode.PromotesDimPreviewBeforePlayLoad(
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryBrowser.IsPlaylistVisualSameSource(session.Document.SourcePath),
            LibraryPlaylistDocuments.IsVideo(session.Document));
        if (promoteDimPreview)
        {
            LibraryBrowser.PromotePlaylistVisualToMainPlay();
        }

        var bridgeChrome = LibraryPlayerMode.HoldsPlaylistVisualChromeBridge(
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryPlaylistDocuments.IsVisual(session.Document),
            immersiveHold || promoteDimPreview);
        if (bridgeChrome)
        {
            BeginPlaylistVisualChromeBridge();
        }

        try
        {
            if (!keepAmbientGlow)
            {
                ShowLibraryArtwork(session);
            }

            await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
            if (playTicket != _libraryPlayTicket)
            {
                return;
            }

            if (session.Document.IsDeferredLoad
                && !LibraryPlaylistDocuments.IsVisual(session.Document))
            {
                return;
            }

            if (LibraryPlaylistDocuments.IsVideo(session.Document))
            {
                ActivateVisualLibraryMeta(session);
            }

            if (playTicket != _libraryPlayTicket)
            {
                return;
            }

            if (IsLibraryMaximized)
            {
                LibraryBrowser.SelectSessionQuiet(session);
            }

            if (IsPlaybackActive())
            {
                // 次の本再生へつなぐときは Halt（先頭へ戻す停止）しない。
                // 旧曲を lastStart へ Seek してから次曲頭を鳴らすと、残留と重なって
                // ジャジャーン／切替直前の異音になる。
                // 本再生は直後の StartPlayback(0)／映像開始が先頭を担当する。
                // 動画クロムを跨ぐときだけプレイリスト映像を残して一時停止する。
                PausePlaybackSoft(keepPlaylistVisual: bridgeChrome);
            }

            ApplyLoadedLibrarySession(session);
            if (LibraryPlaylistDocuments.IsVisual(session.Document))
            {
                if (LibraryPlaylistDocuments.IsVideo(session.Document)
                    && !await TryPrepareVideoProxyAsync(session, playTicket).ConfigureAwait(true))
                {
                    return;
                }

                if (playTicket != _libraryPlayTicket)
                {
                    return;
                }

                // プロキシ待ちのあいだに別ロードが _libraryLoadSession を動かしても、
                // この再生チケットが生きていれば本再生へ進む。
                _libraryLoadSession = session;
                StartLibraryVisualPlayback(session, stopAfterTrack, fromStart);
                if (!keepAmbientGlow)
                {
                    ShowLibraryArtwork(session);
                }

                return;
            }

            SetPlaylistVideoFullscreen(false);
            LibraryBrowser.HidePlaylistVisual();
            StartPlayback(0, prerollSeconds: 0);
            if (!keepAmbientGlow)
            {
                ShowLibraryArtwork(session);
            }

            // 再生とは別ハンドルでピーク走査する（停止待ちしない）。
            // 次曲の先読みは、今の曲と次曲の走査が終わってから開く。
            _ = FillLibraryPeaksAsync(session);
            if (!stopAfterTrack)
            {
                ScheduleLibraryGaplessPrefetch();
            }
        }
        finally
        {
            ClearPlaylistVisualChromeBridge();
        }
    }

    private void BeginPlaylistVisualChromeBridge()
    {
        if (_playlistVisualChromeBridge)
        {
            return;
        }

        _playlistVisualChromeBridge = true;
        SyncPlaylistVideoChromeFade();
    }

    private void ClearPlaylistVisualChromeBridge()
    {
        if (!_playlistVisualChromeBridge)
        {
            return;
        }

        _playlistVisualChromeBridge = false;
        SyncPlaylistVideoChromeFade();
    }

    /// <summary>
    /// Space／トランスポート再生。エディタの TogglePlayback 相当。
    /// 再生中は HaltPlaybackToStart（動画本再生中だけその場一時停止）。
    /// 停止中はシーク位置（CursorFrame）から単独再生／再開（曲末で次へ進まない）。
    /// 別曲へ切り替えるときだけ PlayLibrarySessionAsync（その曲は先頭から）。
    /// </summary>
    /// <param name="fromStart">
    /// true なら常に先頭から（プレイヤー Enter）。false なら同じ曲はシーク位置から再開（Space）。
    /// </param>
    private async void PlayLibrarySelectionOnceOrToggle(bool fromStart = false)
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // 再生中：動画本再生は一時停止／再開用にその場停止。それ以外は開始位置へ戻す。
        if (IsPlaybackActive())
        {
            StopVideoFrameStep();
            if (LibraryPlayerMode.PausesVideoRealPlaybackInPlaceOnSpace(IsPlaylistVideoPlaying()))
            {
                PausePlaybackHere(immersiveVideoHold: true);
            }
            else
            {
                HaltPlaybackToStart(keepVideoChromeHidden: false);
            }

            _libraryStopAfterTrack = false;
            return;
        }

        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null)
        {
            return;
        }

        // 同じ曲が既に載っているとき：Space はシークバー位置から、Enter は先頭から。
        if (ReferenceEquals(session, _activeSession) && _document is not null)
        {
            _libraryStopAfterTrack = true;
            if (LibraryPlaylistDocuments.IsPdf(session.Document))
            {
                await PlayLibrarySessionAsync(session, stopAfterTrack: true, fromStart: fromStart)
                    .ConfigureAwait(true);
                return;
            }

            // 選択／ホバープレビューはプロキシを待たない。Space 本再生ではここで作る。
            // Enter（fromStart）は先頭固定。Space は暗いプレビュー中の位置を引き継ぐ。
            var startFrame = fromStart ? 0L : _document.CursorFrame;
            if (!await EnsureVideoProxyBeforePlaybackAsync(session).ConfigureAwait(true))
            {
                return;
            }

            if (_document is null)
            {
                return;
            }

            if (fromStart)
            {
                session.Document.CursorFrame = 0;
                Waveform.PlayheadFrame = 0;
            }
            else if (LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && !LibraryBrowser.PlaylistVisualPlaying
                && LibraryBrowser.IsPlaylistVisualSameSource(session.Document.SourcePath))
            {
                // プロキシ待ちのあいだに進んだプレビュー位置を使う。待ち始めの値へ戻さない。
                startFrame = LibraryPlayerMode.HoverResumeFrame(
                    hoverVideoShown: true,
                    visualPlaying: false,
                    sameClip: true,
                    LibraryBrowser.PlaylistVisualPosition.TotalSeconds,
                    session.Document.SampleRate,
                    session.Document.FrameCount);
                session.Document.CursorFrame = startFrame;
                Waveform.PlayheadFrame = startFrame;
            }

            StartPlayback(startFrame, prerollSeconds: 0);
            return;
        }

        await PlayLibrarySessionAsync(session, stopAfterTrack: true, fromStart: fromStart)
            .ConfigureAwait(true);
    }

    private Task EnsureLibrarySessionLoadedAsync(DocumentSession session, bool bind = true)
    {
        // 別セッションへ載せる前の停止は Pause のみ。HaltPlaybackToStart（先頭へ戻す）は
        // 旧曲を seekedTo=0 にしてから次曲を鳴らすため、切替直前の異音の原因になる。
        // 明示停止（Space など）の Halt 経路は触らない。
        if (bind && IsPlaybackActive() && !ReferenceEquals(session, _activeSession))
        {
            PausePlaybackSoft();
        }

        if (LibraryPlaylistDocuments.IsVisual(session.Document))
        {
            _libraryLoadGeneration++;
            _libraryLoadSession = session;
            ActivateVisualLibraryMeta(session);
            return Task.CompletedTask;
        }

        _libraryLoadGeneration++;
        var gen = _libraryLoadGeneration;
        _libraryLoadSession = session;

        if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
        {
            return Task.CompletedTask;
        }

        // プレイヤー中の圧縮／動画はフル Load しない。WAVE / AIFF はメモリ早戻し用に PCM 化する。
        if (IsLibraryMaximized
            && session.Document.SourcePath is { Length: > 0 }
            && AudioCodec.PrefersPlayerStream(session.Document.SourcePath))
        {
            return ActivateStreamLibrarySessionAsync(session, gen);
        }

        // エディタ復帰、またはプレイヤーの非圧縮ストリーム残りはフル PCM に昇格する。
        if (session.Document.IsStreamPlayback)
        {
            return LoadDeferredLibrarySessionAsync(session, gen, bind);
        }

        if (!session.Document.IsDeferredLoad)
        {
            return Task.CompletedTask;
        }

        return LoadDeferredLibrarySessionAsync(session, gen, bind);
    }

    private async Task ActivateStreamLibrarySessionAsync(DocumentSession session, int generation)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(() =>
            {
                if (session.Document.IsStreamPlayback
                    && session.Document.FrameCount > 0
                    && session.Document.SampleRate > 0)
                {
                    return true;
                }

                return AudioCodec.TryActivateStreamPlayback(session.Document);
            }).ConfigureAwait(true);
            if (!ok)
            {
                // ストリーム不可なら従来どおりフル展開。
                await LoadDeferredLibrarySessionAsync(session, generation, bind: true).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            return;
        }

        ApplyLoadedLibrarySession(session);
        // ピークは再生と同時に走らせない（Activate 直後の再生と MF が競合する）。
        if (!IsPlaybackActive())
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    /// <summary>
    /// 破壊編集用に Wave / AIFF の PCM をメモリへ載せる。遅延／ストリームならフル Load。
    /// </summary>
    private Task<bool> EnsureSessionPcmForEditAsync(DocumentSession session)
    {
        if (!AudioCodec.IsUncompressedPcm(session.Document.SourceKind))
        {
            return Task.FromResult(false);
        }

        return EnsureSessionLoadedForExportAsync(session);
    }

    /// <summary>
    /// MP3 書き出し用に PCM をメモリへ載せる。遅延／ストリームならフル Load（圧縮音源含む）。
    /// </summary>
    private async Task<bool> EnsureSessionLoadedForExportAsync(DocumentSession session)
    {
        var document = session.Document;
        if (LibraryPlaylistDocuments.IsPdf(document)
            || LibraryPlaylistDocuments.IsVideo(document))
        {
            return false;
        }

        if (!document.IsDeferredLoad
            && !document.IsStreamPlayback
            && document.Interleaved.Length > 0
            && document.FrameCount > 0)
        {
            return true;
        }

        if (document.SourcePath is not { Length: > 0 } path || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var playhead = session.PlayheadFrame;
            var tags = document.Tags;
            var previousArt = document.Artwork;
            var loaded = await Task.Run(() =>
            {
                var next = AudioCodec.Load(path, buildPeaks: false);
                next.ReplacePeaks(
                    PeakPyramid.BuildDisplay(next.Interleaved, next.Channels, next.SampleCount));
                return next;
            }).ConfigureAwait(true);

            if (!session.Document.IsDeferredLoad
                && !session.Document.IsStreamPlayback
                && session.Document.Interleaved.Length > 0)
            {
                return session.Document.FrameCount > 0;
            }

            loaded.CursorFrame = Math.Clamp(playhead, 0, loaded.FrameCount);
            session.ReplaceDocument(loaded);
            loaded.ApplyTags(tags);
            if (!loaded.HasArtwork && previousArt is { Length: > 0 })
            {
                loaded.SetArtwork(previousArt);
            }

            session.PlayheadFrame = Math.Clamp(playhead, 0, loaded.FrameCount);
            ApplyLoadedLibrarySession(session, bind: ReferenceEquals(session, _activeSession));
            return loaded.FrameCount > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task LoadDeferredLibrarySessionAsync(DocumentSession session, int generation, bool bind = true)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var loaded = await Task.Run(() =>
            {
                var document = AudioCodec.Load(path, buildPeaks: false);
                document.ReplacePeaks(
                    PeakPyramid.BuildDisplay(document.Interleaved, document.Channels, document.SampleCount));
                return document;
            }).ConfigureAwait(true);
            if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
            {
                return;
            }

            var tags = session.Document.Tags;
            var previousArt = session.Document.Artwork;
            loaded.CursorFrame = Math.Clamp(session.PlayheadFrame, 0, loaded.FrameCount);
            session.ReplaceDocument(loaded);
            loaded.ApplyTags(tags);
            if (!loaded.HasArtwork && previousArt is { Length: > 0 })
            {
                loaded.SetArtwork(previousArt);
            }

            if (loaded.SourcePath is { } openedPath)
            {
                RememberOpenedPath(openedPath);
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            if (bind || !_sessions.Contains(session))
            {
                return;
            }
        }

        ApplyLoadedLibrarySession(session, bind);
        _ = FillLibraryPeaksAsync(session);
    }

    private Task FillLibraryPeaksAsync(DocumentSession session)
    {
        var document = session.Document;
        if (document.IsDeferredLoad || LibraryPlaylistDocuments.IsPdf(document))
        {
            return Task.CompletedTask;
        }

        var display = IsLibraryMaximized;
        TrimUnwantedLibraryPeakJobs();
        // 途中スナップショット（未走査は 0）が残っている間は、完成までやり直す。
        // プレイヤー包絡（1ch）をエディタで使い続けるとレーンが 1 本のまま。
        if (LibraryPlayerMode.CanReusePeaks(display, document))
        {
            // エディタで作り終えたピークは作り直さない。ただし表示中の波形はプレイヤー帯で描き直す。
            if (display && ReferenceEquals(_activeSession, session))
            {
                Waveform.Refresh();
            }

            return Task.CompletedTask;
        }

        if (!display && document.IsStreamPlayback)
        {
            return Task.CompletedTask;
        }

        if (_libraryPeakTasks.TryGetValue(document, out var running)
            && !running.IsCompleted)
        {
            return running;
        }

        if (!_libraryPeakJobs.Add(document))
        {
            return Task.CompletedTask;
        }

        Task task = null!;
        task = FillLibraryPeaksCoreAsync(session, document, display);
        _libraryPeakTasks[document] = task;
        return task;
    }

    private async Task FillLibraryPeaksCoreAsync(
        DocumentSession session,
        AudioDocument document,
        bool display)
    {
        var peakGeneration = _libraryPeakGeneration;
        var pendingPartial = new PeakPyramid?[1];
        var invokePending = 0;
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_libraryPeakCts.Token);
        _libraryPeakJobCts[document] = jobCts;
        var token = jobCts.Token;
        var retry = false;
        try
        {
            PeakPyramid peaks;
            if (document.IsStreamPlayback
                && document.SourcePath is { Length: > 0 } path
                && File.Exists(path))
            {
                var dispatcher = Dispatcher;
                var peakPath = path;
                if (LibraryPlaylistDocuments.IsVideo(path)
                    && VideoProxy.TryGetCached(path, out var proxy))
                {
                    peakPath = proxy;
                }

                peaks = await Task.Run(() => PeakPyramid.BuildPlayerDisplayFromPath(
                        peakPath,
                        onProgress: partial =>
                        {
                            if (peakGeneration != _libraryPeakGeneration)
                            {
                                return;
                            }

                            Volatile.Write(ref pendingPartial[0], partial);
                            if (Interlocked.Exchange(ref invokePending, 1) != 0)
                            {
                                return;
                            }

                            // Normal は Input より先。長いピーク走査中にキーとマウスが飢える。
                            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                            {
                                Interlocked.Exchange(ref invokePending, 0);
                                var snapshot = Volatile.Read(ref pendingPartial[0]);
                                if (snapshot is null || peakGeneration != _libraryPeakGeneration)
                                {
                                    return;
                                }

                                TryApplyLibraryPeaks(session, document, peakGeneration, snapshot, throttlePaint: true);
                            });
                        },
                        token),
                    token).ConfigureAwait(true);
            }
            else
            {
                var interleaved = document.Interleaved;
                var channels = document.Channels;
                var sampleCount = document.SampleCount;
                peaks = await Task.Run(() => display
                        ? PeakPyramid.BuildPlayerDisplay(interleaved, channels, sampleCount)
                        : PeakPyramid.Build(interleaved, channels, sampleCount),
                    token).ConfigureAwait(true);
            }

            var superseded = peakGeneration != _libraryPeakGeneration
                || !ReferenceEquals(session.Document, document);
            if (!superseded && IsLibraryMaximized != display && !document.IsStreamPlayback)
            {
                retry = true;
            }
            else if (!superseded)
            {
                TryApplyLibraryPeaks(session, document, peakGeneration, peaks);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or OperationCanceledException
                                       or System.Runtime.InteropServices.COMException)
        {
            // ピークだけ失敗しても再生は続ける。
            // 今の曲の走査が次曲の先読みで止まったときは、世代を進めずにやり直す。
            if (ex is (OperationCanceledException or System.Runtime.InteropServices.COMException)
                && IsLibraryMaximized
                && _sessions.Contains(session)
                && ReferenceEquals(_activeSession, session)
                && ReferenceEquals(session.Document, document)
                && document.Peaks.IsBuilding)
            {
                retry = true;
            }
        }
        finally
        {
            if (_libraryPeakJobCts.TryGetValue(document, out var tracked)
                && ReferenceEquals(tracked, jobCts))
            {
                _libraryPeakJobCts.Remove(document);
            }

            jobCts.Dispose();
            if (peakGeneration == _libraryPeakGeneration)
            {
                if (_libraryPeakTasks.TryGetValue(document, out var trackedTask)
                    && trackedTask.IsCompleted)
                {
                    _libraryPeakTasks.Remove(document);
                }

                _libraryPeakJobs.Remove(document);
            }
        }

        if (retry)
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    /// <summary>
    /// 同じ曲の、より手前で止まったスナップショットでは置き換えない。
    /// </summary>
    private bool TryApplyLibraryPeaks(
        DocumentSession session,
        AudioDocument document,
        int peakGeneration,
        PeakPyramid peaks,
        bool throttlePaint = false)
    {
        if (peakGeneration != _libraryPeakGeneration
            || !ReferenceEquals(session.Document, document)
            || !LibraryPlayerMode.IsNewerLibraryPeaks(document.Peaks, peaks))
        {
            return false;
        }

        document.ReplacePeaks(peaks);
        if (document.IsStreamPlayback
            && !peaks.IsEmpty
            && !peaks.IsBuilding
            && peaks.FrameCount > 0
            && peaks.FrameCount != document.FrameCount)
        {
            document.SyncStreamPlaybackMeta(
                document.SampleRate,
                document.Channels,
                document.BitsPerSample,
                peaks.FrameCount);
        }

        if (!peaks.IsBuilding && document.SourcePath is { Length: > 0 } path)
        {
            LibraryBrowser.ApplyCompletedPlaylistWaveform(path, peaks);
        }

        RefreshLibrarySessionWaveform(session, throttlePaint);
        return true;
    }

    private void RefreshLibrarySessionWaveform(DocumentSession session, bool throttlePaint)
    {
        WaveformView? view = null;
        var tilePane = FindTilePane(session);
        if (tilePane is not null)
        {
            view = tilePane.View;
        }
        else if (ReferenceEquals(_activeSession, session)
            && LibraryPlayerMode.RefreshesPrimaryForLibraryPeaks(_tileMode, sessionHasTilePane: false))
        {
            // タイル構築中は Primary が Clear 済み。ここに描くと空ビットマップが残り、
            // タブ切替まで波形が出ないことがある。
            view = Waveform;
        }

        if (view is null)
        {
            return;
        }

        if (throttlePaint)
        {
            view.RefreshThrottled();
        }
        else
        {
            view.Refresh();
        }

        if (!IsLibraryMaximized && ReferenceEquals(_activeSession, session))
        {
            Overview.Refresh();
        }
    }
}
