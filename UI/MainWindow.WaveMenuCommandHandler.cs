using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 波形コンテキストメニューのコマンドディスパッチ（旧 ExecuteWaveMenu）。
/// MainWindow partial として切り出し、呼び出し側は従来どおり ExecuteWaveMenu。
/// </summary>
public partial class MainWindow
{
    private void ExecuteWaveMenu(WaveMenuCommand command)
    {
        if (IsLibraryMaximized && LibraryPlayerMode.BlocksWaveMenu(command))
        {
            return;
        }

        switch (command)
        {
            case WaveMenuCommand.ClearMarkers:
                if (IsLibraryMaximized)
                {
                    Waveform.TryRemovePlayerScratchMarkers(_waveMenuHit.MarkerFrames);
                }
                else
                {
                    ClearMarkers(_waveMenuHit.MarkerFrames);
                }

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
                if (IsLibraryMaximized)
                {
                    SyncPlayerScratchEnabled();
                    Waveform.TryAddPlayerScratchMarker(_waveMenuHit.Frame);
                }
                else
                {
                    AddMarkerAtFrame(_waveMenuHit.Frame);
                }

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
                // タブ選択中は選択タブへ（Ctrl+V・タブメニューと同じ挙動）。
                if (HasTabSelection)
                {
                    PasteHistoryRecipesToTabs(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    PasteHistoryRecipesToTabs([_activeSession]);
                }

                break;
            case WaveMenuCommand.Delete:
                ApplyDelete();
                break;
            case WaveMenuCommand.SilenceSelection:
                PromptSilenceSelection();
                break;
            case WaveMenuCommand.InsertSilence:
                PromptInsertSilence();
                break;
            case WaveMenuCommand.DetectSilence:
                ApplyDetectSilence();
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
            case WaveMenuCommand.CopySelectionTime:
                CopySelectionTimes(SelectionClipboardPathKind.FileName);
                break;
            case WaveMenuCommand.CopySelectionTimePath:
                CopySelectionTimes(SelectionClipboardPathKind.FullPath);
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
            case WaveMenuCommand.CrossfadeEdit:
                TryEnterCrossfadeEditMode();
                break;
            case WaveMenuCommand.Normalize:
                ApplyNormalize();
                break;
            case WaveMenuCommand.NormalizePerRegion:
                ApplyNormalizePerRegion();
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
                if (IsLibraryMaximized)
                {
                    TryAddPlayerScratchMarkerAtPlayhead();
                }
                else
                {
                    TryAddMarkerAtPlayhead();
                }

                break;
            case WaveMenuCommand.RenameMarker:
            case WaveMenuCommand.RenameRegion:
                RenameMarkerAtPosition();
                break;
            case WaveMenuCommand.DeleteMarkers:
                if (IsLibraryMaximized)
                {
                    ApplyPlayerScratchMarkerDelete();
                }
                else
                {
                    ApplyDeleteMarkers();
                }

                break;
            case WaveMenuCommand.DeleteAllMarkers:
                ApplyDeleteAllMarkers();
                break;
            case WaveMenuCommand.SetRegion:
                TrySetRegionFromSelection();
                break;
            case WaveMenuCommand.DeleteRegions:
                ApplyDeleteSelectedRegions();
                break;
            case WaveMenuCommand.DeleteAllRegions:
                ApplyDeleteAllRegions();
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
            case WaveMenuCommand.PlaybackPitchUp:
                ApplyPlaybackPitchStep(1);
                break;
            case WaveMenuCommand.PlaybackPitchDown:
                ApplyPlaybackPitchStep(-1);
                break;
            case WaveMenuCommand.PlaybackPitchReset:
                ApplyPlaybackPitchStep(0);
                break;
            case WaveMenuCommand.TogglePlayback:
                if (IsLibraryMaximized)
                {
                    PlayLibrarySelectionOnceOrToggle();
                }
                else
                {
                    TogglePlayback();
                }

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
            case WaveMenuCommand.Shuffle:
                LibraryBrowser.ToggleShuffle();
                break;
            case WaveMenuCommand.AutoPlay:
                LibraryBrowser.ToggleAutoPlay();
                break;
            case WaveMenuCommand.PreviousTrack:
                TryStepLibraryPlaylist(-1);
                break;
            case WaveMenuCommand.NextTrack:
                TryStepLibraryPlaylist(1);
                break;
            case WaveMenuCommand.RestartTrack:
                RestartLibraryTrack();
                break;
            case WaveMenuCommand.AutoPlayNext:
                LibraryBrowser.ToggleAutoPlayNext();
                break;
            case WaveMenuCommand.RangeClick:
                ToggleRangeClick();
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
            case WaveMenuCommand.TileOff:
                SetWaveformTileArrange(WaveformTileArrange.Off);
                break;
            case WaveMenuCommand.TileHorizontal:
                SetWaveformTileArrange(WaveformTileArrange.Horizontal);
                break;
            case WaveMenuCommand.TileVertical:
                SetWaveformTileArrange(WaveformTileArrange.Vertical);
                break;
            case WaveMenuCommand.TileGrid:
                SetWaveformTileArrange(WaveformTileArrange.Grid);
                break;
            case WaveMenuCommand.MaximizeWaveform:
                ToggleWaveformMaximize();
                break;
            case WaveMenuCommand.MaximizeAnalyzers:
                ToggleAnalyzerMaximize();
                break;
            case WaveMenuCommand.MaximizeLibrary:
                ToggleLibraryMaximize();
                break;
            case WaveMenuCommand.VideoMini:
                ToggleVideoMiniPlayer();
                break;
            case WaveMenuCommand.MinimalChrome:
                ToggleLibraryMinimalChrome();
                break;
            case WaveMenuCommand.VideoFullscreen:
                TryTogglePlaylistVideoFullscreen();
                break;
            case WaveMenuCommand.VideoMiniZoomIn:
                TryScaleVideoLaunchWindow(1, isRepeat: false);
                break;
            case WaveMenuCommand.VideoMiniZoomOut:
                TryScaleVideoLaunchWindow(-1, isRepeat: false);
                break;
            case WaveMenuCommand.VideoHud:
                TryTogglePlaylistVideoHud();
                break;
            case WaveMenuCommand.VideoTimecode:
                TryTogglePlaylistVideoTimecode();
                break;
            case WaveMenuCommand.PdfBackgroundPin:
                TryTogglePdfBackgroundPin();
                break;
            case WaveMenuCommand.ExitRealPlayback:
                if (IsVideoMiniPlayerActive())
                {
                    LeaveVideoMiniToPlaylist(minimalChrome: false);
                }
                else
                {
                    TryExitPlaylistRealPlayback();
                }

                break;
            case WaveMenuCommand.ForceVideoProxy:
                RequestForceVideoProxyEncode();
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
            case WaveMenuCommand.SilentSkip:
                SilentSkipCheck.IsChecked = SilentSkipCheck.IsChecked != true;
                break;
            case WaveMenuCommand.AlwaysOnTop:
                AlwaysOnTopCheck.IsChecked = AlwaysOnTopCheck.IsChecked != true;
                break;
            case WaveMenuCommand.NewDocument:
                NewDocument();
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
            case WaveMenuCommand.RenameFile:
                if (_activeSession is not null)
                {
                    BeginFileNameEdit(_activeSession);
                }

                break;
            case WaveMenuCommand.CopyFileName:
                CopySessionPathTexts(fullPath: false);
                break;
            case WaveMenuCommand.CopyFilePath:
                CopySessionPathTexts(fullPath: true);
                break;
            case WaveMenuCommand.DuplicateFile:
                if (HasTabSelection)
                {
                    DuplicateSessions(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    DuplicateSession(_activeSession);
                }

                break;
            case WaveMenuCommand.MergeTabs:
                MergeSelectedTabs();
                break;
            case WaveMenuCommand.DeleteFile:
                if (HasTabSelection)
                {
                    DeleteSessionFiles(SelectedTabsInOrder());
                }
                else if (_activeSession is not null)
                {
                    DeleteSessionFile(_activeSession);
                }

                break;
            case WaveMenuCommand.CloseTab:
                CloseDocument();
                break;
            case WaveMenuCommand.CloseOthers:
                if (_activeSession is not null)
                {
                    CloseOtherTabs(_activeSession);
                }

                break;
            case WaveMenuCommand.CloseTabsRight:
                if (_activeSession is not null)
                {
                    CloseTabsFrom(_activeSession, rightSide: true);
                }

                break;
            case WaveMenuCommand.CloseTabsLeft:
                if (_activeSession is not null)
                {
                    CloseTabsFrom(_activeSession, rightSide: false);
                }

                break;
            case WaveMenuCommand.CloseAll:
                CloseAllTabs();
                break;
            case WaveMenuCommand.CopyAllTabTimes:
                CopyAllTabTimes();
                break;
            case WaveMenuCommand.ReopenTab:
                ReopenLastClosedTab();
                break;
            case WaveMenuCommand.SelectAllTabs:
                SelectAllTabs();
                break;
            case WaveMenuCommand.NextTab:
                ActivateAdjacentTab(1);
                break;
            case WaveMenuCommand.PrevTab:
                ActivateAdjacentTab(-1);
                break;
            case WaveMenuCommand.FindTabs:
                OpenTileSearch();
                break;
            case WaveMenuCommand.ClearTileSearch:
                CloseTileSearch();
                break;
            case WaveMenuCommand.Settings:
                OpenSettings();
                break;
            case WaveMenuCommand.ColorPanel:
                ShowColorDevPanel();
                break;
            case WaveMenuCommand.WaapiPanel:
                ToggleWaapiPanel();
                break;
            case WaveMenuCommand.KeepTarget:
                WaapiBar.ToggleKeepTarget();
                break;
            case WaveMenuCommand.AutoActive:
                WaapiBar.AutoActiveChecked = !WaapiBar.AutoActiveChecked;
                PersistWaapiSettings();
                break;
            case WaveMenuCommand.OutputFolder:
                BrowseExportOutputFolder();
                break;
            case WaveMenuCommand.LaunchWwise:
                LaunchWwiseProjectFromShortcut();
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
                // タブ／プレイリスト選択中は一括書き出し。
                if (HasBatchEditTargets)
                {
                    ExportBatchWave();
                }
                else
                {
                    ExportActiveTab(AudioFileKind.Wave);
                }

                break;
            case WaveMenuCommand.ExportMp3:
                ExportMp3FromUi();
                break;
            case WaveMenuCommand.ExportByMarkers:
                if (HasBatchEditTargets)
                {
                    ExportBatchSeparated(TabExportSplit.Markers);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Markers);
                }

                break;
            case WaveMenuCommand.ExportByRegions:
                if (HasBatchEditTargets)
                {
                    ExportBatchSeparated(TabExportSplit.Regions);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Regions);
                }

                break;
            case WaveMenuCommand.ExportByChannels:
                if (HasBatchEditTargets)
                {
                    ExportBatchSeparated(TabExportSplit.Channels);
                }
                else
                {
                    ExportActiveTabSeparated(TabExportSplit.Channels);
                }

                break;
            case WaveMenuCommand.Manual:
                ManualViewer.Open(this);
                break;
            case WaveMenuCommand.GitHub:
                TryOpenUrl(AppVersion.RepositoryUrl);
                break;
        }
    }
}
