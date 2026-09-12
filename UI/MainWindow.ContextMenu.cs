using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private WaveformContextHit _waveMenuHit = new();

    private void OpenWaveformContextMenu(WaveformContextHit hit)
    {
        if (IsUiBusy)
        {
            return;
        }

        _waveMenuHit = hit;
        var menu = WaveformContextMenuBuilder.Create(
            WaveformContextMenuBuilder.Build(CaptureWaveMenuModel(hit)),
            ExecuteWaveMenu,
            Waveform);
        menu.IsOpen = true;
    }

    private void Overview_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (IsUiBusy)
        {
            return;
        }

        e.Handled = true;
        var x = e.GetPosition(Overview).X;
        OpenWaveformContextMenu(new WaveformContextHit
        {
            Frame = (long)Math.Round(Overview.FrameAt(x)),
        });
    }

    private WaveformContextMenuModel CaptureWaveMenuModel(WaveformContextHit hit)
    {
        var document = _document;
        var hasDoc = document is not null;
        var recording = IsRecording;
        var busy = IsUiBusy;
        var canEdit = hasDoc && !busy && !recording;
        var canNavigate = hasDoc && !busy && !recording;
        var hasSelection = hasDoc && !document!.Selection.IsEmpty;
        return new WaveformContextMenuModel
        {
            HasDocument = hasDoc,
            CanEdit = canEdit,
            CanNavigate = canNavigate,
            IsBusy = busy,
            IsPlaying = IsPlaybackActive(),
            IsRecording = recording,
            HasSelection = hasSelection,
            HasAnySelection = hasSelection
                || Waveform.HasSelectedMarkers
                || Waveform.HasSelectedRegions
                || Waveform.HasSelectedTimelineItems,
            CanUndo = hasDoc && _history.CanUndo,
            CanRedo = hasDoc && _history.CanRedo,
            CanPasteAudio = _clipboard is { IsEmpty: false },
            CanPasteHistory = _historyRecipeClipboard.Count > 0,
            HasMarkers = hasDoc && document!.Markers.Count > 0,
            HasRegions = hasDoc && document!.Regions.Count > 0,
            HasSelectedMarkers = Waveform.HasSelectedMarkers,
            HasSelectedRegions = Waveform.HasSelectedRegions,
            HasSampleLoop = hasDoc && !document!.SampleLoop.IsEmpty,
            AllowsRegionsAndLoops = hasDoc && document!.AllowsRegionsAndLoops,
            CanRenameMarker = hasDoc && (Waveform.HasSelectedMarkers || document!.HasMarkerAt(Waveform.PlayheadFrame)),
            CanRenameRegion = hasDoc
                && document!.AllowsRegionsAndLoops
                && (Waveform.HasSelectedRegions || document.Regions.Count > 0),
            HasSolo = (_activeSession?.SoloMask ?? 0) != 0,
            CenterLocked = Waveform.CenterLocked,
            AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true,
            TipsVisible = AppStorage.Settings.ShowTips,
            WaapiVisible = _waapiPanelVisible,
            PlayExit = WaapiBar.PlayPostExitChecked,
            WaapiExportEnabled = WaapiBar.ExportEnabled,
            CanReopenTab = _workspace.ClosedTabs.Count > 0,
            HasMultipleTabs = _sessions.Count > 1,
            AnalysisView = Waveform.AnalysisView,
            Hit = hit,
        };
    }

    private void ExecuteWaveMenu(WaveMenuCommand command)
    {
        switch (command)
        {
            case WaveMenuCommand.ClearMarkers:
                ClearMarkers(_waveMenuHit.MarkerFrames);
                break;
            case WaveMenuCommand.ClearRegion:
                ClearRegion(_waveMenuHit.Region);
                break;
            case WaveMenuCommand.ClearLoop:
            case WaveMenuCommand.ClearLoopItem:
                ClearSampleLoop();
                break;
            case WaveMenuCommand.PlayFromHere:
                PlayFromFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.SeekHere:
                SeekFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.AddMarkerHere:
                AddMarkerAtFrame(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.SelectSpanHere:
                Waveform.SelectSpanAt(_waveMenuHit.Frame);
                break;
            case WaveMenuCommand.Undo:
                UndoEdit();
                break;
            case WaveMenuCommand.Redo:
                RedoEdit();
                break;
            case WaveMenuCommand.Cut:
                ApplyCut();
                break;
            case WaveMenuCommand.Copy:
                ApplyCopy();
                break;
            case WaveMenuCommand.Paste:
                ApplyPaste();
                break;
            case WaveMenuCommand.PasteHistory:
                if (_activeSession is not null)
                {
                    PasteHistoryRecipesToTabs([_activeSession]);
                }

                break;
            case WaveMenuCommand.Delete:
                ApplyDelete();
                break;
            case WaveMenuCommand.SelectAll:
                Waveform.SelectAll();
                break;
            case WaveMenuCommand.ClearSelection:
                Waveform.ClearSelection();
                break;
            case WaveMenuCommand.SelectToStart:
                Waveform.SelectToDocumentEdge(-1);
                break;
            case WaveMenuCommand.SelectToEnd:
                Waveform.SelectToDocumentEdge(1);
                break;
            case WaveMenuCommand.History:
                OpenEditHistory();
                break;
            case WaveMenuCommand.FadeIn:
                PromptFade(fadeIn: true, Waveform);
                break;
            case WaveMenuCommand.FadeOut:
                PromptFade(fadeIn: false, Waveform);
                break;
            case WaveMenuCommand.FadeAround:
                ApplyFadeAroundPlayhead();
                break;
            case WaveMenuCommand.Normalize:
                ApplyNormalize();
                break;
            case WaveMenuCommand.Volume:
                PromptVolume();
                break;
            case WaveMenuCommand.Pitch:
                PromptPitch();
                break;
            case WaveMenuCommand.TimeStretch:
                PromptTimeStretch();
                break;
            case WaveMenuCommand.Reverse:
                ApplyReverse();
                break;
            case WaveMenuCommand.SampleRate:
                PromptFormatConvert(FormatConvertKind.SampleRate);
                break;
            case WaveMenuCommand.BitDepth:
                PromptFormatConvert(FormatConvertKind.BitDepth);
                break;
            case WaveMenuCommand.Channels:
                PromptFormatConvert(FormatConvertKind.Channels);
                break;
            case WaveMenuCommand.AddMarker:
                TryAddMarkerAtPlayhead();
                break;
            case WaveMenuCommand.RenameMarker:
            case WaveMenuCommand.RenameRegion:
                RenameMarkerAtPosition();
                break;
            case WaveMenuCommand.DeleteMarkers:
                ApplyDeleteMarkers();
                break;
            case WaveMenuCommand.SetRegion:
                TrySetRegionFromSelection();
                break;
            case WaveMenuCommand.DeleteRegions:
                ApplyDeleteSelectedRegions();
                break;
            case WaveMenuCommand.SetLoop:
                SetSampleLoopFromSelection();
                break;
            case WaveMenuCommand.PrevMarker:
                Waveform.SeekToMarker(-1);
                break;
            case WaveMenuCommand.NextMarker:
                Waveform.SeekToMarker(1);
                break;
            case WaveMenuCommand.TogglePlayback:
                TogglePlayback();
                break;
            case WaveMenuCommand.Stop:
                ExecuteTransport(TransportCommand.Stop);
                break;
            case WaveMenuCommand.PauseHere:
                PausePlaybackHere();
                break;
            case WaveMenuCommand.Preroll:
                StartPrerollPlayback();
                break;
            case WaveMenuCommand.Restart:
                RestartFromLastPlaybackStart();
                break;
            case WaveMenuCommand.LoopPlay:
                JumpToLoopPrerollAndPlay();
                break;
            case WaveMenuCommand.PlayExit:
                TogglePlayPostExit();
                break;
            case WaveMenuCommand.Record:
                ToggleRecording();
                break;
            case WaveMenuCommand.GoStart:
                ExecuteTransport(TransportCommand.GoToStart);
                break;
            case WaveMenuCommand.GoEnd:
                ExecuteTransport(TransportCommand.GoToEnd);
                break;
            case WaveMenuCommand.ViewLeft:
                Waveform.SeekToViewEdge(-1);
                break;
            case WaveMenuCommand.ViewRight:
                Waveform.SeekToViewEdge(1);
                break;
            case WaveMenuCommand.TimeZoomIn:
                Waveform.ZoomTimeIn();
                break;
            case WaveMenuCommand.TimeZoomOut:
                Waveform.ZoomTimeOut();
                break;
            case WaveMenuCommand.TimeZoomMax:
                Waveform.ZoomTimeToMax();
                break;
            case WaveMenuCommand.TimeZoomFit:
                Waveform.ResetTimeZoom();
                break;
            case WaveMenuCommand.AmpZoomIn:
                Waveform.ZoomAmpIn();
                break;
            case WaveMenuCommand.AmpZoomOut:
                Waveform.ZoomAmpOut();
                break;
            case WaveMenuCommand.AmpZoomMax:
                Waveform.ZoomAmpToMax();
                break;
            case WaveMenuCommand.AmpZoomReset:
                Waveform.ResetAmpZoom();
                break;
            case WaveMenuCommand.CenterPlayhead:
                Waveform.CenterViewOnPlayhead();
                break;
            case WaveMenuCommand.CenterLock:
                if (Waveform.CenterLocked)
                {
                    Waveform.UnlockCenter();
                }
                else
                {
                    Waveform.LockCenterToPlayhead();
                }

                break;
            case WaveMenuCommand.ViewWaveform:
                Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
                break;
            case WaveMenuCommand.ViewSpectrogram:
                Waveform.SetAnalysisView(WaveformAnalysisView.Spectrogram);
                break;
            case WaveMenuCommand.ViewOverlay:
                Waveform.SetAnalysisView(WaveformAnalysisView.Overlay);
                break;
            case WaveMenuCommand.ViewLoudness:
                Waveform.SetAnalysisView(WaveformAnalysisView.Loudness);
                break;
            case WaveMenuCommand.SoloNext:
                CycleChannelSolo(1);
                break;
            case WaveMenuCommand.SoloPrev:
                CycleChannelSolo(-1);
                break;
            case WaveMenuCommand.SoloClear:
                ClearChannelSolo();
                break;
            case WaveMenuCommand.FocusTime:
                StatusTimes.FocusCurrentTime();
                break;
            case WaveMenuCommand.AlwaysOnTop:
                AlwaysOnTopCheck.IsChecked = AlwaysOnTopCheck.IsChecked != true;
                break;
            case WaveMenuCommand.Open:
                OpenFromDialog();
                break;
            case WaveMenuCommand.Save:
                Save(saveAs: false);
                break;
            case WaveMenuCommand.SaveAs:
                Save(saveAs: true);
                break;
            case WaveMenuCommand.SaveMp3:
                SaveAsMp3();
                break;
            case WaveMenuCommand.CloseTab:
                CloseDocument();
                break;
            case WaveMenuCommand.CloseAll:
                CloseAllTabs();
                break;
            case WaveMenuCommand.ReopenTab:
                ReopenLastClosedTab();
                break;
            case WaveMenuCommand.NextTab:
                ActivateAdjacentTab(1);
                break;
            case WaveMenuCommand.PrevTab:
                ActivateAdjacentTab(-1);
                break;
            case WaveMenuCommand.Settings:
                OpenSettings();
                break;
            case WaveMenuCommand.WaapiPanel:
                ToggleWaapiPanel();
                break;
            case WaveMenuCommand.WwiseExport:
                if (WaapiBar.ExportEnabled)
                {
                    _ = ExportToWwiseAsync();
                }

                break;
            case WaveMenuCommand.Quit:
                Close();
                break;
            case WaveMenuCommand.ExportWave:
                ExportActiveTab(AudioFileKind.Wave);
                break;
            case WaveMenuCommand.ExportMp3:
                ExportActiveTab(AudioFileKind.Mp3);
                break;
            case WaveMenuCommand.ExportByMarkers:
                ExportActiveTabSeparated(TabExportSplit.Markers);
                break;
            case WaveMenuCommand.ExportByRegions:
                ExportActiveTabSeparated(TabExportSplit.Regions);
                break;
            case WaveMenuCommand.ExportAllMp3:
                ExportAllTabsMp3();
                break;
            case WaveMenuCommand.Tips:
                ToggleTips();
                break;
            case WaveMenuCommand.Manual:
                ManualViewer.Open(this);
                break;
            case WaveMenuCommand.GitHub:
                TryOpenUrl(AppVersion.RepositoryUrl);
                break;
        }
    }

    private void PlayFromFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        SeekFrame(frame);
        if (!IsPlaybackActive())
        {
            StartPlayback(frame, prerollSeconds: 0);
        }
    }

    private void AddMarkerAtFrame(long frame)
    {
        if (_document is null)
        {
            return;
        }

        frame = Math.Clamp(frame, 0, _document.FrameCount);
        SeekFrame(frame);
        if (_document.HasMarkerAt(frame))
        {
            return;
        }

        ApplyPlaceLive(ProcessEdits.AddMarker(_document, frame));
        AfterMarkerEdit();
    }

    private void ClearChannelSolo()
    {
        if (_activeSession is null)
        {
            return;
        }

        _activeSession.SoloMask = 0;
        ApplyChannelSolo();
    }

    private void ExportActiveTab(AudioFileKind kind)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabs([_activeSession], kind);
    }

    private void ExportActiveTabSeparated(TabExportSplit split)
    {
        if (_activeSession is null)
        {
            return;
        }

        ExportTabsSeparated([_activeSession], split);
    }
}
