using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void PromptFade(bool fadeIn, FrameworkElement? placementTarget = null)
    {
        var batch = HasBatchEditTargets;
        var document = _document ?? ResolveBatchEditTargets().FirstOrDefault()?.Document;
        if (document is null)
        {
            return;
        }

        if (!batch)
        {
            var range = ActiveRange();
            if (range.IsEmpty)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        if (_fadeMenu is { IsOpen: true })
        {
            _fadeMenu.IsOpen = false;
        }

        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _fadePromptIsIn = fadeIn;
        _fadePreview.ResumeFrame = document.CursorFrame;
        _fadeReplayOnHighlight = false;
        var initial = fadeIn
            ? AppStorage.Settings.ResolvedFadeInCurve()
            : AppStorage.Settings.ResolvedFadeOutCurve();
        // 複数ファイル編集ではお試し視聴しない（単体のみ）。
        var menu = FadeCurvePicker.Show(
            this,
            PlacementMode.Center,
            fadeIn,
            shape => ApplyFade(fadeIn, shape),
            batch ? static _ => { } : shape => PreviewFade(fadeIn, shape),
            batch ? static _ => { } : shape => OnFadeCurveHighlighted(fadeIn, shape),
            initial);
        _fadeMenu = menu;
        menu.Closed += (_, _) =>
        {
            _fadeReplayOnHighlight = false;
            StopFadePreview(restoreCursor: true);
            ClearFadeCurveVisualPreview();
            if (ReferenceEquals(_fadeMenu, menu))
            {
                _fadeMenu = null;
            }
        };
        Dispatcher.BeginInvoke(() => _fadeReplayOnHighlight = true, DispatcherPriority.ApplicationIdle);
    }

    private bool CloseFadeCurvePicker()
    {
        if (_fadeMenu is not { IsOpen: true })
        {
            return false;
        }

        _fadeMenu.IsOpen = false;
        return true;
    }

    private void PromptVolume()
    {
        // プレイヤー：プレイリスト選択の Wave / AIFF をターゲットへ揃える（確認のみ）。
        if (IsLibraryMaximized)
        {
            PromptMatchLoudnessTarget(ResolvePlaylistLoudnessMatchTargets());
            return;
        }

        // エディタのタブ複数選択：無音検出と同じく確認ダイアログのあと即適用。
        if (HasTabSelection)
        {
            PromptMatchLoudnessTarget(SelectedTabsInOrder());
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_volumeMenu is { IsOpen: true })
        {
            return;
        }

        if (Waveform.AnalysisView != WaveformAnalysisView.Loudness)
        {
            Waveform.SetAnalysisView(WaveformAnalysisView.Loudness);
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _volumePreview.ResumeFrame = _document.CursorFrame;
        var menu = VolumeGainPicker.Show(
            this,
            AppStorage.Settings.ResolvedLoudnessTargetLufs(),
            ApplyVolume,
            PreviewVolume,
            OnVolumeGainChanged);
        _volumeMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopVolumePreview(restoreCursor: true);
            ClearVolumeVisualPreview();
            if (ReferenceEquals(_volumeMenu, menu))
            {
                _volumeMenu = null;
            }
        };

        var interleaved = _document.Interleaved;
        var channels = _document.Channels;
        var sampleRate = _document.SampleRate;
        _ = Task.Run(() => WaveformGainAnalyzer.Build(interleaved, channels, sampleRate, range))
            .ContinueWith(
                task =>
                {
                    if (!task.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    menu.Dispatcher.BeginInvoke(() =>
                    {
                        if (!ReferenceEquals(_volumeMenu, menu) || menu is not { IsOpen: true })
                        {
                            return;
                        }

                        VolumeGainPicker.SetAnalyzer(menu, task.Result);
                    });
                },
                TaskScheduler.Default);
    }

    private DocumentSession[] ResolvePlaylistLoudnessMatchTargets()
    {
        var selected = LibraryBrowser.SelectedSessions;
        if (selected.Length > 0)
        {
            return selected;
        }

        return _activeSession is { } active ? [active] : [];
    }

    private void PromptMatchLoudnessTarget(IReadOnlyList<DocumentSession> sessions)
    {
        if (IsUiBusy)
        {
            return;
        }

        CloseVolumeGainPicker();
        var eligible = new List<DocumentSession>(sessions.Count);
        foreach (var session in sessions)
        {
            if (AudioCodec.IsUncompressedPcm(session.Document.SourceKind))
            {
                eligible.Add(session);
            }
        }

        if (eligible.Count == 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorNoWaveAiffForLoudnessMatch,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var saveAfter = IsLibraryMaximized;
        var result = MatchLoudnessTargetWindow.Show(
            this,
            eligible.Count,
            AppStorage.Settings.ResolvedLoudnessTargetLufs(),
            saveAfter);
        if (!result.Accepted)
        {
            return;
        }

        _ = MatchLoudnessTargetAsync(eligible, result.TargetLufs, saveAfter);
    }

    /// <summary>
    /// Wave / AIFF の Integrated を設定のラウドネスターゲットへ揃える（ファイル全体の破壊ゲイン）。
    /// プレイヤー（saveAfter）では続けて既存パスへ確認なし保存する。
    /// </summary>
    private async Task MatchLoudnessTargetAsync(
        IReadOnlyList<DocumentSession> targets,
        double targetLufs,
        bool saveAfter)
    {
        if (targets.Count == 0 || IsUiBusy)
        {
            return;
        }

        var sessions = targets.ToArray();
        _tabBatchEditBusy = true;
        RefreshExportEnabled();
        var names = sessions.Select(session => session.DisplayName).ToArray();
        var weights = sessions.Select(session => Math.Max(1L, session.Document.FrameCount)).ToArray();
        var progress = new double[sessions.Length];
        var applied = new int[sessions.Length];
        var ready = new bool[sessions.Length];
        var commands = new IEditCommand?[sessions.Length];
        var integratedBefore = new float[sessions.Length];
        var gainDb = new double[sessions.Length];
        Exception? error = null;
        var errorLock = new object();
        try
        {
            StopPlaybackForEdit();
            foreach (var session in sessions)
            {
                PrepareSessionForHistoryPaste(session);
            }

            ShowBusyGlass(UiStrings.OverlayApplySelected);
            ReportHistoryPasteBusy(names, progress, weights);
            for (var i = 0; i < sessions.Length; i++)
            {
                if (!IsLoaded)
                {
                    break;
                }

                progress[i] = Math.Max(progress[i], 0.02);
                ReportHistoryPasteBusy(names, progress, weights);
                ready[i] = await EnsureSessionPcmForEditAsync(sessions[i]).ConfigureAwait(true);
                if (!ready[i])
                {
                    progress[i] = 1;
                    ReportHistoryPasteBusy(names, progress, weights);
                    continue;
                }

                weights[i] = Math.Max(1L, sessions[i].Document.FrameCount);
                integratedBefore[i] = float.NaN;
            }

            var buildIndices = new List<int>(sessions.Length);
            for (var i = 0; i < sessions.Length; i++)
            {
                if (ready[i])
                {
                    buildIndices.Add(i);
                }
            }

            if (buildIndices.Count > 0 && IsLoaded)
            {
                var parallelism = AudioExport.WorkerCount(
                    buildIndices.Count,
                    AppStorage.Settings.ExportParallelism);
                await Task.Run(() =>
                {
                    Parallel.For(
                        0,
                        buildIndices.Count,
                        new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                        bi =>
                        {
                            var i = buildIndices[bi];
                            var session = sessions[i];
                            try
                            {
                                var document = session.Document;
                                var mask = TabBatchEditMask(session);
                                if (!LoudnessMeterEngine.TryGainDbToMatchTarget(
                                        document,
                                        targetLufs,
                                        out var db,
                                        out var integrated))
                                {
                                    return;
                                }

                                commands[i] = ProcessEdits.Gain(
                                    document,
                                    TabBatchRange(document),
                                    db,
                                    channelMask: mask);
                                integratedBefore[i] = integrated;
                                gainDb[i] = db;
                            }
                            catch (Exception ex)
                            {
                                lock (errorLock)
                                {
                                    error ??= ex;
                                }
                            }
                        });
                }).ConfigureAwait(true);
            }

            Exception? buildError;
            lock (errorLock)
            {
                buildError = error;
            }

            if (buildError is not null)
            {
                error = buildError;
            }
            else if (IsLoaded)
            {
                for (var i = 0; i < sessions.Length; i++)
                {
                    if (!ready[i])
                    {
                        continue;
                    }

                    if (commands[i] is { } command)
                    {
                        var session = sessions[i];
                        session.History.Do(session.Document, command);
                        if (float.IsFinite(integratedBefore[i]))
                        {
                            var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb[i]);
                            var after = LoudnessMeterEngine.ApplyLinearGainToLufs(integratedBefore[i], linear);
                            var tags = session.Document.Tags;
                            session.Document.ApplyTags(
                                tags.HasFileLoudness
                                    ? tags.WithFileLoudness(tags.ToFileLoudness().WithLinearGain(linear) with
                                    {
                                        IntegratedLufs = after,
                                    })
                                    : tags.WithIntegratedLufs(after));
                        }

                        if (IsLibraryMaximized)
                        {
                            LibraryBrowser.UpdateSessionRow(session);
                        }

                        applied[i]++;
                    }

                    progress[i] = 1;
                    ReportHistoryPasteBusy(names, progress, weights);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        var saveTargets = new List<DocumentSession>();
        if (saveAfter && error is null && IsLoaded)
        {
            for (var i = 0; i < sessions.Length; i++)
            {
                if (applied[i] > 0)
                {
                    saveTargets.Add(sessions[i]);
                }
            }
        }

        var willSave = saveTargets.Count > 0;
        if (!willSave)
        {
            _tabBatchEditBusy = false;
            RefreshExportEnabled();
            if (error is not null || !IsLoaded)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (!IsLoaded)
        {
            if (willSave)
            {
                _tabBatchEditBusy = false;
                RefreshExportEnabled();
                _busyGlass.HideOverlay();
            }

            return;
        }

        RefreshViewsAfterHistoryPaste(sessions, applied);
        if (error is not null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (applied.All(count => count == 0))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.TabBatchEditSkippedAll,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!willSave)
        {
            return;
        }

        try
        {
            // 保存中は Export 側のビジーに切り替え（すりガラスは Save が張り替える）。
            _tabBatchEditBusy = false;
            RefreshExportEnabled();
            await SaveSessionsInPlaceQuietAsync(saveTargets).ConfigureAwait(true);
        }
        finally
        {
            RefreshExportEnabled();
        }
    }

    private bool CloseVolumeGainPicker()
    {
        if (_volumeMenu is not { IsOpen: true })
        {
            return false;
        }

        _volumeMenu.IsOpen = false;
        return true;
    }

    private void PromptPitch()
    {
        if (IsUiBusy)
        {
            return;
        }

        var batch = HasBatchEditTargets;
        if (_document is null && !batch)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!batch)
        {
            var range = ActiveRange();
            if (range.IsEmpty)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (_pitchMenu is { IsOpen: true })
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        var document = _document ?? ResolveBatchEditTargets().FirstOrDefault()?.Document;
        if (document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _pitchPreview.ResumeFrame = document.CursorFrame;
        var menu = PitchShiftPicker.Show(
            this,
            ApplyPitch,
            batch ? static (_, _) => { } : PreviewPitch,
            batch ? static (_, _) => { } : OnPitchChanged);
        _pitchMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopPitchPreview(restoreCursor: true);
            if (ReferenceEquals(_pitchMenu, menu))
            {
                _pitchMenu = null;
            }
        };
    }

    private bool ClosePitchShiftPicker()
    {
        if (_pitchMenu is not { IsOpen: true })
        {
            return false;
        }

        _pitchMenu.IsOpen = false;
        return true;
    }

    private void OnPitchChanged(int semitones, bool timeStretch)
    {
        if (!_pitchPreview.Previewing || _document is null || _pitchPreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartPitchPreviewPlayback(range, semitones, timeStretch);
    }

    private void PreviewPitch(int semitones, bool timeStretch)
    {
        // 複数ファイル編集ではお試し視聴しない（単体のみ）。
        if (HasBatchEditTargets || _document is null || _pitchPreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _pitchPreview.SpaceTick < 120)
        {
            return;
        }

        _pitchPreview.SpaceTick = now;
        _pitchPreview.Toggling = true;
        try
        {
            if (_pitchPreview.Previewing && _player.IsPlaying)
            {
                StopPitchPreview(restoreCursor: true);
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartPitchPreviewPlayback(range, semitones, timeStretch);
        }
        finally
        {
            _pitchPreview.Toggling = false;
        }
    }

    private void StartPitchPreviewPlayback(WaveSelection range, int semitones, bool timeStretch)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var source = _document.CopyRange(range.StartFrame, range.Length);
            var pitched = Audio.PitchShift.IsNoOp(semitones)
                ? source
                : Audio.PitchShift.Apply(
                    source,
                    _document.Channels,
                    _document.SampleRate,
                    semitones,
                    timeStretch);
            var preview = new AudioDocument(
                pitched,
                _document.SampleRate,
                _document.Channels,
                _document.BitsPerSample,
                _document.SourceKind,
                null);
            var previewRange = new WaveSelection(0, preview.FrameCount);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(preview, 0, previewRange, loop: false);
            }
            else
            {
                _player.Prepare(preview, 0, previewRange, loop: false);
            }

            _pitchPreview.Origin = range.StartFrame;
            _pitchPreview.Previewing = true;
            _pitchPreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _pitchPreview.Previewing = false;
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopPitchPreview(bool restoreCursor)
    {
        if (!_pitchPreview.Previewing)
        {
            return;
        }

        var resume = _pitchPreview.ResumeFrame;
        _pitchPreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }
    }

    private void ApplyPitch(int semitones, bool timeStretch) =>
        _ = ApplyPitchAsync(semitones, timeStretch);

    private async Task ApplyPitchAsync(int semitones, bool timeStretch)
    {
        if (IsUiBusy)
        {
            return;
        }

        // タブ／プレイリスト選択中はファイル全体へ同じピッチシフトを一括適用する。
        if (HasBatchEditTargets)
        {
            _pitchPreview.Previewing = false;
            ClosePitchShiftPicker();
            if (Audio.PitchShift.IsNoOp(semitones))
            {
                return;
            }

            var targets = ResolveBatchEditTargets();
            await RunBatchEditAsync(
                targets,
                UiStrings.OverlayPitchShift,
                (session, progress) => session.Document.FrameCount == 0
                    ? null
                    : ProcessEdits.PitchShift(
                        session.Document,
                        TabBatchRange(session.Document),
                        semitones,
                        timeStretch,
                        progress,
                        channelMask: TabBatchEditMask(session)),
                saveAfter: IsLibraryMaximized,
                ensurePcm: IsLibraryMaximized).ConfigureAwait(true);
            return;
        }

        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _pitchPreview.Previewing = false;
        ClosePitchShiftPicker();
        if (Audio.PitchShift.IsNoOp(semitones))
        {
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        _pitchShiftBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowPitchShiftBusyGlass();
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            command = await Task.Run(() => ProcessEdits.PitchShift(document, range, semitones, timeStretch, progress, channelMask: EditMask()))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            AfterTransform();
            PausePlaybackSoft();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _pitchShiftBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (error is not null && IsLoaded)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void PromptTimeStretch()
    {
        if (IsUiBusy)
        {
            return;
        }

        var batch = HasBatchEditTargets;
        if (_document is null && !batch)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        WaveSelection range;
        AudioDocument document;
        if (batch)
        {
            document = _document ?? ResolveBatchEditTargets().FirstOrDefault()?.Document!;
            if (document is null)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            range = TabBatchRange(document);
        }
        else
        {
            document = _document!;
            range = ActiveRange();
            if (range.IsEmpty)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        if (_timeStretchMenu is { IsOpen: true })
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _timeStretchPreview.ResumeFrame = document.CursorFrame;
        var menu = TimeStretchPicker.Show(
            this,
            (int)Math.Min(int.MaxValue, range.Length),
            document.SampleRate,
            ApplyTimeStretch,
            batch ? static _ => { } : PreviewTimeStretch,
            batch ? static _ => { } : OnTimeStretchChanged);
        _timeStretchMenu = menu;
        menu.Closed += (_, _) =>
        {
            StopTimeStretchPreview(restoreCursor: true);
            if (ReferenceEquals(_timeStretchMenu, menu))
            {
                _timeStretchMenu = null;
            }
        };
    }

    private bool CloseTimeStretchPicker()
    {
        if (_timeStretchMenu is not { IsOpen: true })
        {
            return false;
        }

        _timeStretchMenu.IsOpen = false;
        return true;
    }

    private void PromptInsertSilence() =>
        PromptInsertSilenceAt(_waveMenuHit.Frame);

    private void PromptInsertSilenceAt(long frame)
    {
        if (IsUiBusy)
        {
            return;
        }

        var document = _document ?? ResolveBatchEditTargets().FirstOrDefault()?.Document;
        if (document is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_silenceMenu is { IsOpen: true })
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CloseSilenceSelectionPicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        _silenceInsertFrame = Math.Clamp(frame, 0, document.FrameCount);
        var menu = SilenceInsertPicker.Show(this, ApplyInsertSilence);
        _silenceMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_silenceMenu, menu))
            {
                _silenceMenu = null;
            }
        };
    }

    private bool CloseInsertSilencePicker()
    {
        if (_silenceMenu is not { IsOpen: true })
        {
            return false;
        }

        _silenceMenu.IsOpen = false;
        return true;
    }

    private void ApplyInsertSilence(double seconds)
    {
        if (IsUiBusy)
        {
            return;
        }

        var edgeFade = _silenceMenu is { } menu && SilenceInsertPicker.ReadEdgeFade(menu);
        var fadeMs = edgeFade ? AppStorage.Settings.ResolvedClickGuardFadeMs() : 0;
        CloseInsertSilencePicker();
        var insertFrame = _silenceInsertFrame;
        var active = _activeSession;
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) =>
                {
                    var at = ReferenceEquals(session, active)
                        ? insertFrame
                        : session.Document.CursorFrame;
                    return ProcessEdits.InsertSilence(session.Document, at, seconds, edgeFade, fadeMs);
                }))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        var command = ProcessEdits.InsertSilence(_document, insertFrame, seconds, edgeFade, fadeMs);
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void OnTimeStretchChanged(int destFrames)
    {
        if (!_timeStretchPreview.Previewing || _document is null || _timeStretchPreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartTimeStretchPreviewPlayback(range, destFrames);
    }

    private void PreviewTimeStretch(int destFrames)
    {
        // 複数ファイル編集ではお試し視聴しない（単体のみ）。
        if (HasBatchEditTargets || _document is null || _timeStretchPreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _timeStretchPreview.SpaceTick < 120)
        {
            return;
        }

        _timeStretchPreview.SpaceTick = now;
        _timeStretchPreview.Toggling = true;
        try
        {
            if (_timeStretchPreview.Previewing && _player.IsPlaying)
            {
                StopTimeStretchPreview(restoreCursor: true);
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartTimeStretchPreviewPlayback(range, destFrames);
        }
        finally
        {
            _timeStretchPreview.Toggling = false;
        }
    }

    private void StartTimeStretchPreviewPlayback(WaveSelection range, int destFrames)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var source = _document.CopyRange(range.StartFrame, range.Length);
            var stretched = Audio.TimeStretch.IsNoOp((int)range.Length, destFrames)
                ? source
                : Audio.TimeStretch.Apply(
                    source,
                    _document.Channels,
                    _document.SampleRate,
                    destFrames);
            var preview = new AudioDocument(
                stretched,
                _document.SampleRate,
                _document.Channels,
                _document.BitsPerSample,
                _document.SourceKind,
                null);
            var previewRange = new WaveSelection(0, preview.FrameCount);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(preview, 0, previewRange, loop: false);
            }
            else
            {
                _player.Prepare(preview, 0, previewRange, loop: false);
            }

            _timeStretchPreview.Origin = range.StartFrame;
            _timeStretchPreview.Previewing = true;
            _timeStretchPreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _timeStretchPreview.Previewing = false;
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopTimeStretchPreview(bool restoreCursor)
    {
        if (!_timeStretchPreview.Previewing)
        {
            return;
        }

        var resume = _timeStretchPreview.ResumeFrame;
        _timeStretchPreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }
    }

    private void ApplyTimeStretch(int destFrames) =>
        _ = ApplyTimeStretchAsync(destFrames);

    private async Task ApplyTimeStretchAsync(int destFrames)
    {
        if (IsUiBusy)
        {
            return;
        }

        // タブ／プレイリスト選択中は、基準長に対する倍率へ換算してファイル全体を一括ストレッチする。
        if (HasBatchEditTargets)
        {
            _timeStretchPreview.Previewing = false;
            CloseTimeStretchPicker();
            CloseInsertSilencePicker();
            var reference = _document ?? ResolveBatchEditTargets().FirstOrDefault()?.Document;
            if (reference is null || reference.FrameCount == 0)
            {
                return;
            }

            var sourceFrames = (int)Math.Min(int.MaxValue, reference.FrameCount);
            var clamped = Audio.TimeStretch.ClampDestFrames(sourceFrames, destFrames);
            if (Audio.TimeStretch.IsNoOp(sourceFrames, clamped))
            {
                return;
            }

            var ratio = clamped / (double)sourceFrames;
            await RunBatchEditAsync(
                ResolveBatchEditTargets(),
                UiStrings.OverlayTimeStretch,
                (session, progress) =>
                {
                    var document = session.Document;
                    if (document.FrameCount == 0)
                    {
                        return null;
                    }

                    var frames = (int)Math.Min(int.MaxValue, document.FrameCount);
                    var dest = Audio.TimeStretch.ClampDestFrames(
                        frames,
                        (int)Math.Min(int.MaxValue, Math.Round(frames * ratio)));
                    if (Audio.TimeStretch.IsNoOp(frames, dest))
                    {
                        return null;
                    }

                    return ProcessEdits.TimeStretch(
                        document,
                        TabBatchRange(document),
                        dest,
                        progress,
                        channelMask: TabBatchEditMask(session));
                },
                saveAfter: IsLibraryMaximized,
                ensurePcm: IsLibraryMaximized).ConfigureAwait(true);
            return;
        }

        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _timeStretchPreview.Previewing = false;
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        destFrames = Audio.TimeStretch.ClampDestFrames((int)range.Length, destFrames);
        if (Audio.TimeStretch.IsNoOp((int)range.Length, destFrames))
        {
            return;
        }

        PausePlaybackSoft();
        var document = _document;
        var viewStart = Waveform.ViewStart;
        _timeStretchBusy = true;
        Exception? error = null;
        IEditCommand? command = null;
        try
        {
            ShowTimeStretchBusyGlass();
            var progress = new Progress<double>(p => _busyGlass.SetProgress(p));
            command = await Task.Run(() => ProcessEdits.TimeStretch(document, range, destFrames, progress, channelMask: EditMask()))
                .ConfigureAwait(true);
            if (!IsLoaded || !ReferenceEquals(_document, document) || command is null)
            {
                return;
            }

            _history.Do(document, command);
            var mappedDest = ChannelSamples.IsScoped(EditMask(), document.Channels)
                ? range.Length
                : destFrames;
            Waveform.SetViewStartExternal(
                AudioDocument.MapFrameThroughRangeStretch(
                    (long)Math.Round(viewStart),
                    range.StartFrame,
                    range.Length,
                    mappedDest));
            AfterTransform();
            PausePlaybackSoft();
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _timeStretchBusy = false;
            if (error is not null)
            {
                _busyGlass.HideOverlay();
            }
            else
            {
                _busyGlass.BeginFadeOut();
            }
        }

        if (error is not null && IsLoaded)
        {
            OwnerCenteredMessageBox.Show(
                this,
                error.Message,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnVolumeGainChanged(double gainDb)
    {
        ApplyVolumeVisualPreview(gainDb);
        if (!_volumePreview.Previewing || _document is null || _volumePreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartVolumePreviewPlayback(range, gainDb);
    }

    private void ApplyVolumeVisualPreview(double gainDb)
    {
        if (_document is null)
        {
            ClearVolumeVisualPreview();
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            ClearVolumeVisualPreview();
            return;
        }

        var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
        Waveform.SetPreviewGain(frame =>
            frame < range.StartFrame || frame >= range.EndFrame
                ? 1f
                : linear);
        LoudnessMeter.SetPreviewLinearGain(
            VolumePreviewShiftsWholeFile(range) ? linear : 1f);
    }

    private void ClearVolumeVisualPreview()
    {
        Waveform.SetPreviewGain(null);
        LoudnessMeter.SetPreviewLinearGain(1f);
    }

    private bool VolumePreviewShiftsWholeFile(WaveSelection range) =>
        _document is not null
        && range.StartFrame <= 0
        && range.EndFrame >= _document.FrameCount
        && !ChannelSamples.IsScoped(EditMask(), _document.Channels);

    private void RestoreVolumeVisualIfMenuOpen()
    {
        if (_document is null || _volumeMenu is not { IsOpen: true })
        {
            return;
        }

        ApplyVolumeVisualPreview(VolumeGainPicker.ReadGain(_volumeMenu));
    }

    private void PreviewVolume(double gainDb)
    {
        // 複数ファイル編集ではお試し視聴しない（単体のみ）。
        if (HasBatchEditTargets || _document is null || _volumePreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _volumePreview.SpaceTick < 120)
        {
            return;
        }

        _volumePreview.SpaceTick = now;
        _volumePreview.Toggling = true;
        try
        {
            if (_volumePreview.Previewing && _player.IsPlaying)
            {
                StopVolumePreview(restoreCursor: true);
                RestoreVolumeVisualIfMenuOpen();
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartVolumePreviewPlayback(range, gainDb);
        }
        finally
        {
            _volumePreview.Toggling = false;
        }
    }

    private void StartVolumePreviewPlayback(WaveSelection range, double gainDb)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var linear = (float)WaveformGainAnalyzer.LinearFromDb(gainDb);
            float GainAt(long frame) =>
                frame < range.StartFrame || frame >= range.EndFrame
                    ? 1f
                    : linear;
            if (_player.HasOutputDevice)
            {
                _player.Rebind(_document, range.StartFrame, range, loop: false, GainAt);
            }
            else
            {
                _player.Prepare(_document, range.StartFrame, range, loop: false, GainAt);
            }

            ApplyVolumeVisualPreview(gainDb);
            _volumePreview.Previewing = true;
            _volumePreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(range.StartFrame);
        }
        catch (Exception ex)
        {
            _volumePreview.Previewing = false;
            ClearVolumeVisualPreview();
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopVolumePreview(bool restoreCursor)
    {
        if (!_volumePreview.Previewing)
        {
            return;
        }

        var resume = _volumePreview.ResumeFrame;
        _volumePreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }

        RestoreVolumeVisualIfMenuOpen();
    }

    private void OnFadeCurveHighlighted(bool fadeIn, FadeShape shape)
    {
        ApplyFadeCurveVisualPreview(fadeIn, shape);
        if (!_fadeReplayOnHighlight || _document is null || _fadePreview.Toggling)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            return;
        }

        StartFadePreviewPlayback(range, fadeIn, shape);
    }

    private void ApplyFadeCurveVisualPreview(bool fadeIn, FadeShape shape)
    {
        if (_document is null)
        {
            ClearFadeCurveVisualPreview();
            return;
        }

        var range = FadeCurves.InclusiveSampleRange(ActiveRange(), _document.FrameCount);
        if (range.IsEmpty)
        {
            ClearFadeCurveVisualPreview();
            return;
        }

        Waveform.SetPreviewGain(frame =>
            frame < range.StartFrame || frame >= range.EndFrame
                ? 1f
                : FadeCurves.GainAtFrame(shape, fadeIn, frame, range.StartFrame, range.Length));
    }

    private void ClearFadeCurveVisualPreview() => Waveform.SetPreviewGain(null);

    private void RestoreFadeVisualIfMenuOpen()
    {
        if (_document is null || _fadeMenu is not { IsOpen: true })
        {
            return;
        }

        ApplyFadeCurveVisualPreview(_fadePromptIsIn, FadeCurvePicker.HighlightedShape(_fadeMenu));
    }

    private void PreviewFade(bool fadeIn, FadeShape shape)
    {
        // 複数ファイル編集ではお試し視聴しない（単体のみ）。
        if (HasBatchEditTargets || _document is null || _fadePreview.Toggling)
        {
            return;
        }

        var now = Environment.TickCount64;
        if (now - _fadePreview.SpaceTick < 120)
        {
            return;
        }

        _fadePreview.SpaceTick = now;
        _fadePreview.Toggling = true;
        try
        {
            if (_fadePreview.Previewing && _player.IsPlaying)
            {
                StopFadePreview(restoreCursor: true);
                RestoreFadeVisualIfMenuOpen();
                return;
            }

            var range = ActiveRange();
            if (range.IsEmpty)
            {
                return;
            }

            StartFadePreviewPlayback(range, fadeIn, shape);
        }
        finally
        {
            _fadePreview.Toggling = false;
        }
    }

    private void StartFadePreviewPlayback(WaveSelection range, bool fadeIn, FadeShape shape)
    {
        if (_document is null)
        {
            return;
        }

        try
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
            }

            _playTimer.Stop();
            _meter.Reset();
            LoudnessMeter.Reset();
            var previewRange = FadeCurves.InclusiveSampleRange(range, _document.FrameCount);
            var gain = (long frame) =>
                FadeCurves.GainAtFrame(shape, fadeIn, frame, previewRange.StartFrame, previewRange.Length);
            if (_player.HasOutputDevice)
            {
                _player.Rebind(_document, previewRange.StartFrame, previewRange, loop: false, gain);
            }
            else
            {
                _player.Prepare(_document, previewRange.StartFrame, previewRange, loop: false, gain);
            }

            ApplyFadeCurveVisualPreview(fadeIn, shape);
            _fadePreview.Previewing = true;
            _fadePreview.StartedAt = Environment.TickCount64;
            _player.Play();
            _playbackGeneration = _player.Generation;
            _playTimer.Start();
            StartMeterRendering();
            Waveform.SetTrailRecording(true);
            Transport.SetPlaying(true);
            Waveform.UnlockCenter();
            Waveform.SetPlayheadFromPlayback(previewRange.StartFrame);
        }
        catch (Exception ex)
        {
            _fadePreview.Previewing = false;
            ClearFadeCurveVisualPreview();
            PausePlaybackSoft();
            OwnerCenteredMessageBox.Show(this, ex.Message, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void StopFadePreview(bool restoreCursor)
    {
        if (!_fadePreview.Previewing)
        {
            return;
        }

        var resume = _fadePreview.ResumeFrame;
        _fadePreview.Previewing = false;
        PausePlaybackSoft();
        if (restoreCursor && _document is not null)
        {
            SeekFrame(resume);
        }

        RestoreFadeVisualIfMenuOpen();
    }

    private void ReleaseStuckScrub()
    {
        Overview.CancelDrag();
        ApplyOverviewViewToWaveform();
        // グローバル Capture 解除はフェード / 変換メニューを閉じ、Closed で試聴まで止める。
        Waveform.AbandonScrub();
        if (_player.IsScrubbing)
        {
            _player.EndScrub();
        }

        _resumeAfterScrub = false;
    }

    /// <param name="keepPlaylistVisual">
    /// true なら映像／PDF の本再生表示はそのまま（暗いプレビューへ落とさない）。
    /// 上下キーで次の本再生へつなぐとき用。
    /// </param>
    /// <param name="freezePlaylistVisual">
    /// true なら映像を現在フレームで一時停止する（暗い 1/3 速プレビューへ落とさない）。
    /// テンキー 0 の一時停止用。
    /// </param>
    private void PausePlaybackSoft(bool keepPlaylistVisual = false, bool freezePlaylistVisual = false)
    {
        StopPlaybackShuttle();
        ReleaseStuckScrub();
        _player.Pause();
        if (IsLibraryMaximized)
        {
            if (freezePlaylistVisual)
            {
                LibraryBrowser.PausePlaylistVisualFrame();
            }
            else if (!keepPlaylistVisual)
            {
                LibraryBrowser.SetPlaylistVisualPlaying(false);
                EnsurePlaylistVideoWindowRestored();
            }
        }
        else
        {
            LibraryBrowser.HidePlaylistVisual();
        }

        _playTimer.Stop();
        StopMeterRendering();
        Waveform.ExitPlayheadFrame = -1;
        Waveform.SetTrailRecording(false);
        Transport.SetPlaying(false);
        BeginMeterRelease();
        // 再生中 Tick は止まっているので、停止時に波形全体のオフライン計測へ戻す。
        LoudnessMeter.RefreshOffline();
        Waveform.UnlockCenter();
    }

    private void ApplyFade(bool fadeIn, FadeShape shape)
    {
        _fadePreview.Previewing = false;
        ClearFadeCurveVisualPreview();
        CloseFadeCurvePicker();

        // タブ／プレイリスト選択中は、同じカーブで選択ファイル全体を一括フェードする。
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) =>
                {
                    var document = session.Document;
                    if (document.FrameCount == 0)
                    {
                        return null;
                    }

                    return fadeIn
                        ? ProcessEdits.FadeIn(document, TabBatchRange(document), shape, channelMask: TabBatchEditMask(session))
                        : ProcessEdits.FadeOut(document, TabBatchRange(document), shape, channelMask: TabBatchEditMask(session));
                }))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        PausePlaybackSoft();
        var command = fadeIn
            ? ProcessEdits.FadeIn(_document, range, shape, channelMask: EditMask())
            : ProcessEdits.FadeOut(_document, range, shape, channelMask: EditMask());
        _history.Do(_document, command);
        AfterTransform();
    }

    private void ApplyFadeAroundPlayhead()
    {
        if (_document is null)
        {
            return;
        }

        var visible = new WaveSelection(Waveform.ViewLeftFrame, Waveform.ViewRightFrame);
        var command = ProcessEdits.FadeAroundPlayhead(_document, visible, Waveform.PlayheadFrame, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyNormalize()
    {
        // タブ／プレイリスト選択中はファイル全体を一括ノーマライズする（選択範囲は見ない）。
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) => session.Document.FrameCount == 0
                    ? null
                    : ProcessEdits.Normalize(
                        session.Document,
                        TabBatchRange(session.Document),
                        channelMask: TabBatchEditMask(session))))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Normalize(_document, range, channelMask: EditMask()));
        AfterTransform();
    }

    private void ApplyReverse()
    {
        // タブ／プレイリスト選択中はファイル全体を一括リバースする。
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) => session.Document.FrameCount == 0
                    ? null
                    : ProcessEdits.Reverse(
                        session.Document,
                        TabBatchRange(session.Document),
                        channelMask: TabBatchEditMask(session))))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.Reverse(_document, range, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterTransform();
    }

    private void ApplyNormalizePerRegion()
    {
        // タブ／プレイリスト選択中は一括適用。リージョンの無いファイルはスキップする。
        var fadeMs = AppStorage.Settings.ResolvedClickGuardFadeMs();
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) =>
                {
                    var document = session.Document;
                    if (!document.AllowsRegionsAndLoops || document.Regions.Count == 0)
                    {
                        return null;
                    }

                    return ProcessEdits.NormalizePerRegion(
                        document,
                        channelMask: TabBatchEditMask(session),
                        fadeMs: fadeMs);
                }))
        {
            return;
        }

        if (_document is null)
        {
            return;
        }

        if (!_document.AllowsRegionsAndLoops || _document.Regions.Count == 0)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoRegions, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.NormalizePerRegion(
            _document,
            channelMask: EditMask(),
            fadeMs: fadeMs);
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterTransform();
    }

    private void ApplyVolume(double gainDb)
    {
        if (_document is null)
        {
            return;
        }

        // タブ選択中は選択ファイル全体へ同じゲインを一括適用する。
        if (HasTabSelection)
        {
            _volumePreview.Previewing = false;
            LoudnessMeter.CommitPreview();
            ClearVolumeVisualPreview();
            CloseVolumeGainPicker();
            if (WaveformGainAnalyzer.IsNoOp(WaveformGainAnalyzer.SnapGainDb(gainDb)))
            {
                return;
            }

            TryRunEditOnSelectedTabs(UiStrings.OverlayApplySelected, (session, _) =>
                session.Document.FrameCount == 0
                    ? null
                    : ProcessEdits.Gain(session.Document, TabBatchRange(session.Document), gainDb, channelMask: TabBatchEditMask(session)));
            return;
        }

        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _volumePreview.Previewing = false;
        LoudnessMeter.CommitPreview();
        ClearVolumeVisualPreview();
        CloseVolumeGainPicker();
        if (WaveformGainAnalyzer.IsNoOp(WaveformGainAnalyzer.SnapGainDb(gainDb)))
        {
            return;
        }

        PausePlaybackSoft();
        var command = ProcessEdits.Gain(_document, range, gainDb, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterTransform();
    }

    private void CommitMarkerComment(long frame, string comment)
    {
        if (_document is null)
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            if (Waveform.ScratchCues.TrySetMarkerComment(frame, comment))
            {
                Waveform.Refresh();
            }

            return;
        }

        var command = ProcessEdits.SetMarkerComment(_document, frame, comment);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.Refresh();
        Overview.Refresh();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private void CommitRegionName(WaveSelection region, string name)
    {
        if (_document is null)
        {
            return;
        }

        var command = ProcessEdits.SetRegionName(_document, region, name);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.Refresh();
        Overview.Refresh();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private bool TryAddMarkerAtPlayhead()
    {
        if (_document is null || LibraryPlayerMode.BlocksEditorMarkerPlacement(IsLibraryMaximized))
        {
            return false;
        }

        var range = _document.Selection;
        if (!range.IsEmpty)
        {
            var previous = DocumentRangeDivide.ResolvePreviousParts(_markerDivide, _document, range, regions: false);
            var next = RangeDivide.NextParts(previous);
            var command = ProcessEdits.DivideMarkers(_document, range, previous, next);
            if (command is not null)
            {
                ApplyPlaceLive(command);
            }

            _markerDivide = new RangeDivideState(_document, range.StartFrame, range.EndFrame, next);
            AfterMarkerEdit();
            SyncRangeClicks();
            return true;
        }

        var frame = Waveform.PlayheadFrame;
        if (_document.HasMarkerAt(frame))
        {
            return true;
        }

        ApplyPlaceLive(ProcessEdits.AddMarker(_document, frame));
        AfterMarkerEdit();
        return true;
    }

    private void RenameMarkerAtPosition()
    {
        if (_document is null)
        {
            return;
        }

        Waveform.TryBeginRenameMarker();
    }

    private void ApplyDeleteMarkers()
    {
        if (_document is null)
        {
            return;
        }

        if (!Waveform.HasSelectedMarkers && _document.HasMarkerAt(Waveform.PlayheadFrame))
        {
            Waveform.SelectMarkerFrames([Waveform.PlayheadFrame]);
        }

        ApplyDeleteSelectedMarkers();
    }

    private void ApplyCopy()
    {
        if (_document is null)
        {
            return;
        }

        var clip = ProcessEdits.Copy(_document, _document.Selection, channelMask: EditMask());
        if (clip is null)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _clipboard = clip;
        _historyClipboardIsLatest = false;
    }

    private static void RestoreKeyboardFocus(UIElement? focus)
    {
        if (focus is null || !focus.IsVisible || focus.IsKeyboardFocusWithin)
        {
            return;
        }

        focus.Focus();
    }

    private void ApplyCut()
    {
        var focus = Keyboard.FocusedElement as UIElement;
        try
        {
            ApplyCutCore();
        }
        finally
        {
            RestoreKeyboardFocus(focus);
        }
    }

    private void ApplyCutCore()
    {
        if (_document is null)
        {
            return;
        }

        var range = _document.Selection.Clamp(_document.FrameCount);
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (DeletesWholeFile(range))
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var clip = ProcessEdits.Copy(_document, range, channelMask: EditMask());
        if (clip is null)
        {
            return;
        }

        _clipboard = clip;
        _historyClipboardIsLatest = false;
        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range, channelMask: EditMask()));
        AfterEdit();
    }

    private void ApplyPaste()
    {
        var focus = Keyboard.FocusedElement as UIElement;
        try
        {
            ApplyPasteCore();
        }
        finally
        {
            RestoreKeyboardFocus(focus);
        }
    }

    private void ApplyPasteCore()
    {
        if (_document is null)
        {
            return;
        }

        // タブ選択中の Ctrl+V は選択タブへの履歴レシピ適用。
        if (HasTabSelection)
        {
            PasteHistoryRecipesToTabs(SelectedTabsInOrder());
            return;
        }

        // 直近のコピーが履歴レシピなら、履歴ウィンドウを開かなくても
        // Ctrl+V で同じ処理を適用する（複数ファイルへの反映用）。
        if (TryPasteHistoryRecipesDirect())
        {
            return;
        }

        if (_clipboard is null || _clipboard.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorClipboardEmpty, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.Paste(_document, _clipboard, Waveform.PlayheadFrame, channelMask: EditMask());
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyDelete()
    {
        if (_document is null)
        {
            return;
        }

        if (Waveform.HasSelectedMarkers || Waveform.HasSelectedRegions)
        {
            ApplyDeleteSelectedMarkers();
            ApplyDeleteSelectedRegions();
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (DeletesWholeFile(range))
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorEmptyAfterDelete, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, ProcessEdits.Delete(_document, range, channelMask: EditMask()));
        AfterEdit();
    }

    private void PromptSilenceSelection()
    {
        if (IsUiBusy || _document is null)
        {
            return;
        }

        if (_silenceSelectionMenu is { IsOpen: true })
        {
            return;
        }

        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        if (_player.IsPlaying)
        {
            PausePlaybackSoft();
        }

        var seconds = _document.SampleRate > 0 ? range.Length / (double)_document.SampleRate : 0;
        var menu = SilenceSelectionPicker.Show(this, seconds, ApplySilenceSelection);
        _silenceSelectionMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_silenceSelectionMenu, menu))
            {
                _silenceSelectionMenu = null;
            }
        };
    }

    private bool CloseSilenceSelectionPicker()
    {
        if (_silenceSelectionMenu is not { IsOpen: true })
        {
            return false;
        }

        _silenceSelectionMenu.IsOpen = false;
        return true;
    }

    private void ApplySilenceSelection()
    {
        if (_document is null || IsUiBusy)
        {
            return;
        }

        var edgeFade = _silenceSelectionMenu is { } menu && SilenceSelectionPicker.ReadEdgeFade(menu);
        var fadeMs = edgeFade ? AppStorage.Settings.ResolvedClickGuardFadeMs() : 0;
        CloseSilenceSelectionPicker();
        var range = _document.Selection;
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoSelection, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var command = ProcessEdits.SilenceSelection(_document, range, channelMask: EditMask(), edgeFade: edgeFade, fadeMs: fadeMs);
        if (command is null)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void ApplyDetectSilence()
    {
        var savedAction = ClampSilenceDetectAction(AppStorage.Settings.SilenceDetectAction);
        var savedOptions = AppStorage.Settings.ResolvedSilenceDetectOptions(
            AppStorage.Settings.SilenceDetectEdgesOnly);

        // タブ／プレイリスト複数選択時はしきい値 UI を出さず、保存済み設定で一括適用するか確認だけする。
        if (HasBatchEditTargets)
        {
            var targets = ResolveBatchEditTargets();
            if (targets.Length == 0)
            {
                return;
            }

            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ConfirmSilenceDetectSelectedFiles(targets.Length),
                UiStrings.DialogSilenceDetectTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            TryRunBatchEdit(UiStrings.OverlayApplySelected, (session, _) =>
            {
                if (session.Document.FrameCount == 0)
                {
                    return null;
                }

                var action = savedAction;
                if (action == SilenceDetectAction.CreateRegions
                    && !session.Document.AllowsRegionsAndLoops)
                {
                    return null;
                }

                return BuildSilenceDetectCommand(
                    session.Document,
                    TabBatchRange(session.Document),
                    savedOptions,
                    TabBatchEditMask(session),
                    action);
            });
            return;
        }

        if (_document is null)
        {
            return;
        }

        // 波形選択があればその範囲、なければファイル全体（他の編集コマンドと同じ ActiveRange）。
        var range = ActiveRange();
        if (range.IsEmpty)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = SilenceDetectDialog.Show(
            this,
            _document,
            range,
            EditMask(),
            savedOptions,
            savedAction,
            allowsRegions: _document.AllowsRegionsAndLoops,
            selectionScoped: !_document.Selection.IsEmpty);
        if (!dialog.Accepted)
        {
            return;
        }

        var options = dialog.Options.Normalized();
        var action = dialog.Action;
        AppStorage.Settings.RememberSilenceDetectOptions(options);
        AppStorage.Settings.SilenceDetectAction = (int)action;
        AppStorage.Save();

        // ダイアログ表示中に選択が変わっても、開いたときの範囲で実行する。
        if (action == SilenceDetectAction.CreateRegions && !_document.AllowsRegionsAndLoops)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorMp3NoRegionLoop,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var command = BuildSilenceDetectCommand(
            _document,
            range,
            options,
            EditMask(),
            action);
        if (command is null)
        {
            if (action is SilenceDetectAction.CreateRegions
                or SilenceDetectAction.CreateMarkers
                or SilenceDetectAction.CreateHitPointMarkers)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    action == SilenceDetectAction.CreateHitPointMarkers
                        ? UiStrings.ErrorNoHitPoints
                        : UiStrings.ErrorNoSilenceDetectSpans,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var keeps = ProcessEdits.CollectDeleteSilenceKeepSpans(
                _document,
                range,
                EditMask(),
                options);
            var wouldEmpty = keeps.Count == 0 && DeletesWholeFile(range);
            OwnerCenteredMessageBox.Show(
                this,
                wouldEmpty
                    ? UiStrings.ErrorEmptyAfterDelete
                    : UiStrings.ErrorNoSilenceToDelete,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private static SilenceDetectAction ClampSilenceDetectAction(int value) =>
        value switch
        {
            (int)SilenceDetectAction.CreateRegions => SilenceDetectAction.CreateRegions,
            (int)SilenceDetectAction.CreateMarkers => SilenceDetectAction.CreateMarkers,
            (int)SilenceDetectAction.CreateHitPointMarkers => SilenceDetectAction.CreateHitPointMarkers,
            _ => SilenceDetectAction.DeleteSilence,
        };

    private static IEditCommand? BuildSilenceDetectCommand(
        AudioDocument document,
        WaveSelection range,
        SilenceDetectOptions options,
        int channelMask,
        SilenceDetectAction action) =>
        action switch
        {
            SilenceDetectAction.CreateRegions => ProcessEdits.CreateRegionsFromSilenceDetection(
                document,
                range,
                options,
                channelMask: channelMask),
            SilenceDetectAction.CreateMarkers => ProcessEdits.CreateMarkersFromSilenceDetection(
                document,
                range,
                options,
                channelMask: channelMask),
            SilenceDetectAction.CreateHitPointMarkers => ProcessEdits.CreateMarkersFromHitPointDetection(
                document,
                range,
                options,
                channelMask: channelMask),
            _ => ProcessEdits.DeleteSilence(
                document,
                range,
                options,
                channelMask: channelMask),
        };

    private void ApplyDeleteSelectedMarkers()
    {
        if (_document is null || !Waveform.HasSelectedMarkers)
        {
            return;
        }

        var command = ProcessEdits.RemoveMarkers(_document, Waveform.SelectedMarkerFrames.ToArray());
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ApplyDeleteSelectedRegions()
    {
        if (_document is null || !Waveform.HasSelectedRegions)
        {
            return;
        }

        var command = ProcessEdits.RemoveRegions(_document, Waveform.SelectedRegions);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ApplyDeleteAllMarkers()
    {
        if (IsLibraryMaximized)
        {
            Waveform.TryClearPlayerScratchMarkers();
        }

        if (HasBatchEditTargets)
        {
            _ = RunBatchEditAsync(
                ResolveBatchEditTargets(),
                UiStrings.OverlayApplySelected,
                (session, _) => ProcessEdits.ClearAllMarkers(session.Document),
                saveAfter: IsLibraryMaximized,
                ensurePcm: IsLibraryMaximized,
                warnIfSkipped: !IsLibraryMaximized);
            return;
        }

        if (_document is null || _document.Markers.Count == 0)
        {
            return;
        }

        var command = ProcessEdits.ClearAllMarkers(_document);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ApplyDeleteAllRegions()
    {
        if (TryRunBatchEdit(
                UiStrings.OverlayApplySelected,
                (session, _) =>
                {
                    var document = session.Document;
                    if (!document.AllowsRegionsAndLoops || document.Regions.Count == 0)
                    {
                        return null;
                    }

                    return ProcessEdits.ClearAllRegions(document);
                }))
        {
            return;
        }

        if (_document is null || !_document.AllowsRegionsAndLoops || _document.Regions.Count == 0)
        {
            return;
        }

        var command = ProcessEdits.ClearAllRegions(_document);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void ClearMarkers(IReadOnlyList<long> frames)
    {
        if (_document is null || frames is null || frames.Count == 0)
        {
            return;
        }

        var command = ProcessEdits.RemoveMarkers(_document, frames);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        Waveform.ClearMarkerSelection();
        AfterMarkerEdit();
    }

    private void BeginTimelineNudgeSession()
    {
        if (_timelineNudgeOpen || _document is null)
        {
            return;
        }

        _timelineNudgeOpen = true;
        if (IsLibraryMaximized)
        {
            _timelineNudgeScratchBefore = PlayerScratchSnapshot.Capture(Waveform.ScratchCues);
            _timelineNudgeMarkersBefore = Waveform.ScratchCues.SnapshotMarkers();
            _timelineNudgeRegionsBefore = [];
            _timelineNudgeLoopBefore = WaveSelection.Empty;
            return;
        }

        _timelineNudgeScratchBefore = null;
        _timelineNudgeMarkersBefore = _document.SnapshotMarkers();
        _timelineNudgeRegionsBefore = _document.SnapshotRegions();
        _timelineNudgeLoopBefore = _document.SampleLoop;
    }

    private void CommitTimelineNudgeSession()
    {
        if (!_timelineNudgeOpen)
        {
            return;
        }

        _timelineNudgeOpen = false;
        if (_document is null)
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            if (_timelineNudgeScratchBefore is PlayerScratchSnapshot scratchBefore)
            {
                Waveform.PushPlayerScratchUndo(scratchBefore);
            }

            _timelineNudgeScratchBefore = null;
            SyncPlayWindowFromDocument();
            return;
        }

        CommitTimelineLayout(_timelineNudgeMarkersBefore, _timelineNudgeRegionsBefore, _timelineNudgeLoopBefore);
    }

    private void CommitTimelineLayout(
        MarkerSnapshot[] markersBefore,
        WaveRegion[] regionsBefore,
        WaveSelection loopBefore)
    {
        if (_document is null || IsLibraryMaximized)
        {
            return;
        }

        if (_timelineNudgeOpen)
        {
            CommitTimelineNudgeSession();
        }

        var command = ProcessEdits.MoveTimelineItems(_document, markersBefore, regionsBefore, loopBefore);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    /// <summary>
    /// プレイヤーの一時マーカーをアクティブ文書へマージ。履歴に載せ dirty のまま（即保存しない）。
    /// </summary>
    private void CommitPlayerScratchMarkersToEditor(MarkerSnapshot[] scratchMarkers)
    {
        if (_document is null || scratchMarkers.Length == 0)
        {
            return;
        }

        if (LibraryPlaylistDocuments.BlocksEditor(_document))
        {
            return;
        }

        var before = _document.SnapshotMarkers();
        var after = PlayerScratchCues.MergeMarkers(before, scratchMarkers);
        var command = ProcessEdits.ApplyMarkers(_document, before, after, "Merge Markers");
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    /// <summary>
    /// 揃った捨て A-B をサンプルループへ。履歴に載せ dirty のまま（即保存しない）。
    /// </summary>
    private void CommitPlayerScratchAbToEditor(WaveSelection scratchAb)
    {
        if (_document is null
            || scratchAb.IsEmpty
            || LibraryPlaylistDocuments.BlocksEditor(_document)
            || !_document.AllowsRegionsAndLoops)
        {
            return;
        }

        var command = ProcessEdits.ApplySampleLoop(_document, scratchAb);
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private void RestorePlayerScratchIfCleared(
        MarkerSnapshot[] scratchMarkers,
        long? abIn,
        long? abOut)
    {
        var restored = false;
        if (scratchMarkers.Length > 0 && !Waveform.ScratchCues.HasMarkers)
        {
            Waveform.ScratchCues.ReplaceMarkers(scratchMarkers);
            restored = true;
        }

        if ((abIn is not null || abOut is not null) && !Waveform.ScratchCues.HasAbPoint)
        {
            Waveform.ScratchCues.ReplaceAb(abIn, abOut);
            restored = true;
            SyncPlayWindowFromDocument();
        }

        if (restored)
        {
            Waveform.Refresh();
        }
    }

    private bool TrySetPlayerAbPoint(bool isIn)
    {
        if (!EnsurePlayerScratchReady())
        {
            return false;
        }

        var frame = Waveform.PlayheadFrame;
        var ok = isIn ? Waveform.TrySetPlayerAbIn(frame) : Waveform.TrySetPlayerAbOut(frame);
        if (ok)
        {
            SyncPlayWindowFromDocument();
        }

        return true;
    }

    private bool TryAddPlayerScratchMarkerAtPlayhead()
    {
        if (!EnsurePlayerScratchReady())
        {
            return false;
        }

        return Waveform.TryAddPlayerScratchMarker(Waveform.PlayheadFrame);
    }

    /// <summary>
    /// 捨てキュー操作前にコンテキスト・描画フラグ・波形ドキュメントを揃える。
    /// </summary>
    private bool EnsurePlayerScratchReady()
    {
        if (!IsPlayerScratchContext())
        {
            return false;
        }

        SyncPlayerScratchEnabled();
        if (_document is not null && Waveform.Document is null)
        {
            Waveform.Document = _document;
        }

        if (_document is null || Waveform.Document is null || !Waveform.PlayerScratchEnabled)
        {
            return false;
        }

        return true;
    }

    private void ApplyPlayerScratchMarkerDelete()
    {
        if (!IsPlayerScratchContext() || _document is null)
        {
            return;
        }

        // エディタの Ctrl+Delete と同じ：選択が無ければ再生ヘッド上のマーカーを対象にする。
        // どちらも無ければ何もしない（メニューの「すべて削除」とは別）。
        if (!Waveform.HasSelectedMarkers && Waveform.PlayerScratchHasMarkerAt(Waveform.PlayheadFrame))
        {
            Waveform.SelectMarkerFrames([Waveform.PlayheadFrame]);
        }

        if (Waveform.HasSelectedMarkers)
        {
            Waveform.TryRemovePlayerScratchMarkers(Waveform.SelectedMarkerFrames.ToArray());
        }
    }

    private const int TimelineNudgeRepeatDelayMs = 400;
    private const int TimelineNudgeRepeatIntervalMs = 50;

    private bool BeginOrContinueMarkerNudge(int direction) =>
        BeginOrContinueTimelineNudge(direction, atPlayhead: true);

    private bool BeginOrContinueTimelineNudge(int direction, bool atPlayhead)
    {
        if (direction == 0)
        {
            return false;
        }

        if (_markerNudgeDirection == direction && _markerNudgeTimer.IsEnabled && _nudgeAtPlayhead == atPlayhead)
        {
            return true;
        }

        _nudgeAtPlayhead = atPlayhead;
        if (!ApplyNudgeStep(direction))
        {
            return true;
        }

        _markerNudgeDirection = direction;
        _nudgeRepeatStarted = false;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _markerNudgeTimer.Start();
        return true;
    }

    private void OnMarkerNudgeTick()
    {
        if (_markerNudgeDirection == 0 || !IsNudgeHeld(_markerNudgeDirection))
        {
            StopMarkerNudge();
            return;
        }

        ApplyNudgeStep(_markerNudgeDirection);
        if (_nudgeRepeatStarted)
        {
            return;
        }

        _nudgeRepeatStarted = true;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _markerNudgeTimer.Start();
    }

    private bool ApplyNudgeStep(int direction)
    {
        if (_nudgeAtPlayhead)
        {
            return ApplyHeldMarkerNudge(direction);
        }

        NudgeSelectedTimeline(direction);
        return true;
    }

    private bool ApplyHeldMarkerNudge(int? direction = null)
    {
        var step = direction ?? _markerNudgeDirection;
        if (step == 0)
        {
            return false;
        }

        var includePrevious = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        return NudgeMarkersAtPlayhead(step, includePrevious, fast);
    }

    private bool IsNudgeHeld(int direction)
    {
        var keyDown = direction < 0 ? Keyboard.IsKeyDown(Key.Left) : Keyboard.IsKeyDown(Key.Right);
        if (!keyDown)
        {
            return false;
        }

        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        return _nudgeAtPlayhead ? alt : !alt;
    }

    private void StopMarkerNudge()
    {
        _markerNudgeDirection = 0;
        _nudgeRepeatStarted = false;
        _markerNudgeTimer.Stop();
        _markerNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        CommitTimelineNudgeSession();
    }

    private enum PlaceRepeatKind
    {
        None,
        Marker,
        Region,
    }

    private bool BeginOrContinuePlaceRepeat(PlaceRepeatKind kind)
    {
        if (kind == PlaceRepeatKind.None)
        {
            return false;
        }

        if (_placeRepeatKind == kind && _placeRepeatTimer.IsEnabled)
        {
            return true;
        }

        BeginPlaceSession();
        if (!ApplyPlace(kind, quiet: false))
        {
            StopPlaceRepeat();
            return true;
        }

        _placeRepeatKind = kind;
        _placeRepeatStarted = false;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _placeRepeatTimer.Start();
        return true;
    }

    private void OnPlaceRepeatTick()
    {
        if (_placeRepeatKind == PlaceRepeatKind.None || !IsPlaceHeld(_placeRepeatKind))
        {
            StopPlaceRepeat();
            return;
        }

        ApplyPlace(_placeRepeatKind, quiet: true);
        if (_placeRepeatStarted)
        {
            return;
        }

        _placeRepeatStarted = true;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _placeRepeatTimer.Start();
    }

    private bool ApplyPlace(PlaceRepeatKind kind, bool quiet) =>
        kind switch
        {
            PlaceRepeatKind.Marker => TryAddMarkerAtPlayhead(),
            PlaceRepeatKind.Region => TrySetRegionFromSelection(quiet),
            _ => false,
        };

    private bool IsPlaceHeld(PlaceRepeatKind kind) =>
        kind switch
        {
            PlaceRepeatKind.Marker =>
                (Keyboard.IsKeyDown(Key.M) || Keyboard.IsKeyDown(Key.Insert))
                && Keyboard.Modifiers == ModifierKeys.None,
            PlaceRepeatKind.Region =>
                Keyboard.IsKeyDown(Key.R) && Keyboard.Modifiers == ModifierKeys.Shift,
            _ => false,
        };

    private void StopPlaceRepeat()
    {
        CommitPlaceSession();
        _placeRepeatKind = PlaceRepeatKind.None;
        _placeRepeatStarted = false;
        _placeRepeatTimer.Stop();
        _placeRepeatTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
    }

    private bool BeginOrContinueSpectrogramBoostNudge(int direction)
    {
        if (!Waveform.SpectrogramVisible || direction == 0)
        {
            return false;
        }

        if (_boostNudgeDirection == direction && _boostNudgeTimer.IsEnabled)
        {
            return true;
        }

        if (!Waveform.NudgeSpectrogramBoost(direction))
        {
            return false;
        }

        _boostNudgeDirection = direction;
        _boostRepeatStarted = false;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
        _boostNudgeTimer.Start();
        return true;
    }

    private void OnSpectrogramBoostNudgeTick()
    {
        if (_boostNudgeDirection == 0 || !IsSpectrogramBoostHeld(_boostNudgeDirection))
        {
            StopSpectrogramBoostNudge();
            return;
        }

        Waveform.NudgeSpectrogramBoost(_boostNudgeDirection);
        if (_boostRepeatStarted)
        {
            return;
        }

        _boostRepeatStarted = true;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatIntervalMs);
        _boostNudgeTimer.Start();
    }

    private bool IsSpectrogramBoostHeld(int direction)
    {
        if (IsUiBusy || !Waveform.SpectrogramVisible)
        {
            return false;
        }

        var keyDown = direction > 0 ? Keyboard.IsKeyDown(Key.Up) : Keyboard.IsKeyDown(Key.Down);
        return keyDown && Keyboard.Modifiers == ModifierKeys.Alt;
    }

    private void StopSpectrogramBoostNudge()
    {
        _boostNudgeDirection = 0;
        _boostRepeatStarted = false;
        _boostNudgeTimer.Stop();
        _boostNudgeTimer.Interval = TimeSpan.FromMilliseconds(TimelineNudgeRepeatDelayMs);
    }

    private void BeginPlaceSession()
    {
        if (_placeSessionOpen || _document is null)
        {
            return;
        }

        _placeSessionOpen = true;
        _placeMarkersBefore = _document.SnapshotMarkers();
        _placeRegionsBefore = _document.SnapshotRegions();
    }

    private void ApplyPlaceLive(IEditCommand command)
    {
        if (_document is null)
        {
            return;
        }

        if (_placeSessionOpen)
        {
            command.Apply(_document);
            return;
        }

        _history.Do(_document, command);
    }

    private void CommitPlaceSession()
    {
        if (!_placeSessionOpen)
        {
            return;
        }

        _placeSessionOpen = false;
        if (_document is null)
        {
            return;
        }

        var command = _placeRepeatKind switch
        {
            PlaceRepeatKind.Marker => ProcessEdits.ApplyMarkers(
                _document,
                _placeMarkersBefore,
                _document.SnapshotMarkers()),
            PlaceRepeatKind.Region => ProcessEdits.ApplyRegions(
                _document,
                _placeRegionsBefore,
                _document.SnapshotRegions(),
                _document.Selection),
            _ => null,
        };
        if (command is null)
        {
            return;
        }

        _history.Do(_document, command);
        AfterMarkerEdit();
    }

    private bool IsContinuingPlaceKey(Key key, ModifierKeys modifiers) =>
        _placeRepeatKind switch
        {
            PlaceRepeatKind.Marker => key is Key.M or Key.Insert && modifiers == ModifierKeys.None,
            PlaceRepeatKind.Region => key == Key.R && modifiers == ModifierKeys.Shift,
            _ => false,
        };

    private bool NudgeMarkersAtPlayhead(int direction, bool includePrevious, bool fast)
    {
        if (_document is null || direction == 0)
        {
            return false;
        }

        if (!Waveform.TrySelectTimelineAtPlayhead(includePrevious))
        {
            return false;
        }

        var delta = Waveform.NudgeStepFrames * (fast ? 3 : 1) * direction;
        BeginTimelineNudgeSession();
        if (!Waveform.TryNudgeSelectedTimeline(delta, out _))
        {
            return false;
        }

        Waveform.SeekKeepingSelection(Waveform.PlayheadFrame);
        AfterMarkerEdit();
        return true;
    }

    private bool NudgePlayheadOrSelection(int direction)
    {
        if (direction == 0)
        {
            return false;
        }

        if (Waveform.HasSelectedTimelineItems)
        {
            StopPlaybackShuttle();
            return BeginOrContinueTimelineNudge(direction, atPlayhead: false);
        }

        if (CanPlaybackShuttle())
        {
            return BeginOrContinuePlaybackShuttle(direction);
        }

        Waveform.NudgePlayhead(direction);
        return true;
    }

    private void NudgeSelectedTimeline(int direction)
    {
        if (_document is null || direction == 0)
        {
            return;
        }

        var fast = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        BeginTimelineNudgeSession();
        if (!Waveform.TryNudgeSelectedTimeline(Waveform.NudgeStepFrames * (fast ? 3 : 1) * direction, out _))
        {
            return;
        }

        AfterMarkerEdit();
    }

    private void AfterMarkerEdit()
    {
        if (_document is null)
        {
            return;
        }

        SyncTileFlagLaneRows();
        Waveform.PruneMarkerSelection();
        Waveform.Refresh();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private void UndoOrRedoPlayerScratch(bool redo)
    {
        CommitTimelineNudgeSession();
        var ok = redo ? Waveform.TryRedoPlayerScratch() : Waveform.TryUndoPlayerScratch();
        if (ok)
        {
            SyncPlayWindowFromDocument();
        }
    }

    private WaveformView? _penSource;

    private void OnPenStrokeUpdated(object? sender, WaveformView.PenStrokeEventArgs e)
    {
        if (sender is not WaveformView view || _document is null || !ReferenceEquals(view.Document, _document))
        {
            return;
        }

        if (ReferenceEquals(_penSource, view))
        {
            return;
        }

        // 音量と曲線の更新はマウスを離してから。描いているあいだは再生だけ止める。
        _penSource = view;
        PausePlaybackSoft();
    }

    private void OnPenStrokeCompleted(object? sender, WaveformView.PenStrokeEventArgs e)
    {
        _penSource = null;
        if (sender is not WaveformView view || _document is null || !ReferenceEquals(view.Document, _document))
        {
            return;
        }

        var baseline = (float[])_document.Interleaved.Clone();
        if (!TryRenderPen(baseline, e.Points, out var rendered))
        {
            return;
        }

        if (LoudnessPenGain.WouldClip(rendered.Peak))
        {
            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ConfirmPenClip,
                UiStrings.AppName,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            LoudnessPenGain.LowerToHeadroom(rendered, _document.Channels, EditMask(), out rendered);
        }

        _document.ReplaceRange(rendered.StartFrame, rendered.Samples);
        var channels = Math.Max(1, _document.Channels);
        var before = new float[rendered.Samples.Length];
        Array.Copy(baseline, (int)rendered.StartFrame * channels, before, 0, before.Length);
        var command = new ReplaceRangeCommand(
            "Pen",
            rendered.StartFrame,
            before,
            rendered.Samples,
            _document.Selection,
            _document.Selection,
            _document.CursorFrame,
            _document.CursorFrame,
            UiStrings.EditHistoryRange(
                UiStrings.EditHistoryName("Pen"),
                _document.SampleRate,
                rendered.StartFrame,
                rendered.StartFrame + rendered.Gains.Length));
        var gains = rendered.Gains;
        var start = rendered.StartFrame;
        var mask = EditMask();
        command.Replay = target => ProcessEdits.PenGain(target, start, gains, mask);
        command.Persist = HistoryRecipes.FromPenGain(
            _document.SampleRate,
            start,
            start + gains.Length,
            gains,
            mask);
        _history.AcceptApplied(_document, command);
        view.RefreshAfterPenEdit(forceLoudness: true);
        LoudnessMeter.RefreshOffline();
        RefreshStatus();
        RefreshHistoryStrip();
    }

    private bool TryRenderPen(
        float[] baseline,
        IReadOnlyList<LoudnessPenGain.Point> points,
        out LoudnessPenGain.Rendered rendered)
    {
        rendered = default;
        return _document is not null
        && LoudnessPenGain.TryRender(
            baseline,
            _document.Channels,
            EditMask(),
            points,
            Waveform.LoudnessTargetLufs,
            ClickGuard.FadeFrames(_document.SampleRate, AppStorage.Settings.ResolvedClickGuardFadeMs()),
            out rendered,
            _document.SampleRate);
    }

    private bool TryRevertOpenPenStroke()
    {
        if (_penSource is not { HasOpenPenStroke: true } view)
        {
            return false;
        }

        _penSource = null;
        view.AbortOpenPenStroke();
        return true;
    }

    private void UndoEdit()
    {
        if (TryRevertOpenPenStroke())
        {
            return;
        }

        CommitTimelineNudgeSession();
        StopPlaceRepeat();

        // 選択タブ／プレイリスト：各ファイルの履歴を等しく 1 段戻す（一括編集の対）。
        if (HasBatchEditTargets)
        {
            var targets = ResolveBatchEditTargets();
            if (AnySessionCanUndo(targets))
            {
                UndoOrRedoSelectedSessions(targets, redo: false);
                return;
            }
        }

        if (IsLibraryMaximized)
        {
            if (IsPlayerScratchContext() && Waveform.CanUndoPlayerScratch)
            {
                UndoOrRedoPlayerScratch(redo: false);
            }

            return;
        }

        if (_document is null || !_history.CanUndo)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Undo(_document);
        AfterEdit();
    }

    private void RedoEdit()
    {
        CommitTimelineNudgeSession();
        StopPlaceRepeat();

        if (HasBatchEditTargets)
        {
            var targets = ResolveBatchEditTargets();
            if (AnySessionCanRedo(targets))
            {
                UndoOrRedoSelectedSessions(targets, redo: true);
                return;
            }
        }

        if (IsLibraryMaximized)
        {
            if (IsPlayerScratchContext())
            {
                UndoOrRedoPlayerScratch(redo: true);
            }

            return;
        }

        if (_document is null || !_history.CanRedo)
        {
            return;
        }

        StopPlaybackForEdit();
        _history.Redo(_document);
        AfterEdit();
    }

    private bool ResolveMenuCanUndo(bool canScratch)
    {
        if (HasBatchEditTargets && AnySessionCanUndo(ResolveBatchEditTargets()))
        {
            return true;
        }

        return IsLibraryMaximized
            ? canScratch && Waveform.CanUndoPlayerScratch
            : _document is not null && _history.CanUndo;
    }

    private bool ResolveMenuCanRedo(bool canScratch)
    {
        if (HasBatchEditTargets && AnySessionCanRedo(ResolveBatchEditTargets()))
        {
            return true;
        }

        return IsLibraryMaximized
            ? canScratch && Waveform.CanRedoPlayerScratch
            : _document is not null && _history.CanRedo;
    }

    private static bool AnySessionCanUndo(IReadOnlyList<DocumentSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.History.CanUndo)
            {
                return true;
            }
        }

        return false;
    }

    private static bool AnySessionCanRedo(IReadOnlyList<DocumentSession> sessions)
    {
        foreach (var session in sessions)
        {
            if (session.History.CanRedo)
            {
                return true;
            }
        }

        return false;
    }

    private void UndoOrRedoSelectedSessions(IReadOnlyList<DocumentSession> sessions, bool redo)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        StopPlaybackForEdit();
        var applied = new int[sessions.Count];
        var any = false;
        for (var i = 0; i < sessions.Count; i++)
        {
            var session = sessions[i];
            var history = session.History;
            if (redo)
            {
                if (!history.CanRedo)
                {
                    continue;
                }

                history.Redo(session.Document);
            }
            else
            {
                if (!history.CanUndo)
                {
                    continue;
                }

                history.Undo(session.Document);
            }

            applied[i] = 1;
            any = true;
            if (IsLibraryMaximized)
            {
                LibraryBrowser.UpdateSessionRow(session);
            }
        }

        if (!any)
        {
            return;
        }

        RefreshViewsAfterHistoryPaste(sessions, applied);
    }

    private void StopPlaybackForEdit()
    {
        PausePlaybackSoft();
    }

    /// <summary>編集コマンドの直前。再生ピッチはどの編集でも 0 に戻し、変わったときだけリセットのトーストを出す。</summary>
    private void ResetPlaybackPitchForEdit()
    {
        if (_player.SetPlaybackSemitones(0))
        {
            ShowPlaybackPitchToast(returnedToOriginal: true);
        }
    }

    private void AfterTransform()
    {
        Waveform.ClearSelection();
        AfterEdit();
    }

    private void AfterEdit()
    {
        RestoreRangeDivideAfterEdit();
        if (_document is null)
        {
            return;
        }

        SyncTileFlagLaneRows();
        Waveform.PlayheadFrame = _document.CursorFrame;
        Waveform.PruneMarkerSelection();
        Waveform.InvalidateSpectrogramCache();
        Waveform.Refresh();
        LoudnessMeter.RefreshOffline();
        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
        SyncViewChrome();
        SyncMonitorLayout();
        ApplyChannelSolo();
        RefreshStatus();
        RefreshHistoryStrip();
        RefreshLibraryBrowser();
        if (_document.Channels != _autoSpeakerSeenChannels)
        {
            TryApplyAutoSpeaker();
        }
    }

    /// <summary>
    /// Undo／編集後も選択が残っていれば、等分マーカー／リージョンからクリック枠を復元する。
    /// オン／オフ状態は触らない（ユーザー設定のまま）。
    /// </summary>
    private void RestoreRangeDivideAfterEdit()
    {
        _markerDivide = null;
        _regionDivide = null;
        if (_document is not null && !_document.Selection.IsEmpty)
        {
            DocumentRangeDivide.RestoreStates(_document, out _markerDivide, out _regionDivide);
        }

        SyncRangeClicks();
    }
}
