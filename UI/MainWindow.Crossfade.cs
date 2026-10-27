using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private CrossfadeEditState? _crossfadeEdit;
    private CrossfadeEditWindow? _crossfadeWindow;
    private bool _exitingCrossfade;
    private WaveSelection _crossfadeLoopBefore = WaveSelection.Empty;

    internal bool IsCrossfadeEditMode => _crossfadeEdit is { IsActive: true };

    /// <summary>
    /// フェード調整ツール表示中は、他モード切替と破壊的な波形編集を受け付けない。
    /// 再生・シーク・ズーム、およびツール自身（Escape で閉じる等）は許可する。
    /// </summary>
    internal bool BlocksOutsideFadeToolActions => IsCrossfadeEditMode;

    /// <summary>トランスポートから受け付ける操作か（フェード調整中のフィルタ）。</summary>
    internal static bool IsAllowedTransportDuringFadeTool(TransportCommand command) =>
        command is TransportCommand.TogglePlayback
            or TransportCommand.Stop
            or TransportCommand.JumpToTime
            or TransportCommand.GoToStart
            or TransportCommand.PreviousPage
            or TransportCommand.PreviousMarker
            or TransportCommand.NextMarker
            or TransportCommand.NextPage
            or TransportCommand.GoToEnd
            or TransportCommand.TimeZoomIn
            or TransportCommand.TimeZoomOut
            or TransportCommand.TimeZoomMax
            or TransportCommand.TimeZoomReset
            or TransportCommand.AmpZoomIn
            or TransportCommand.AmpZoomOut
            or TransportCommand.AmpZoomMax
            or TransportCommand.AmpZoomReset
            or TransportCommand.CenterPlayhead
            or TransportCommand.ToggleLoudnessView
            or TransportCommand.OpenManual
            or TransportCommand.OpenSettings
            or TransportCommand.OpenColorPanel
            or TransportCommand.Save
            or TransportCommand.SaveAs
            or TransportCommand.SaveMp3;

    private void HookCrossfadePanel()
    {
        // ウィンドウは初回オープン時に生成する。
    }

    /// <summary>フェード調整中の外部編集・モード切替を拒否する。true なら呼び出し側は何もしない。</summary>
    private bool RejectOutsideFadeToolAction() => BlocksOutsideFadeToolActions;

    private CrossfadeEditWindow EnsureCrossfadeWindow()
    {
        if (_crossfadeWindow is not null)
        {
            return _crossfadeWindow;
        }

        var window = new CrossfadeEditWindow(this);
        window.DraftChanged += (debounceWave, prePostLight) =>
            OnCrossfadeStateChanged(debounceWaveRedraw: debounceWave, prePostLight: prePostLight);
        window.FieldFocusRequested += OnCrossfadeFieldFocusRequested;
        window.LoopMixRequested += () => ExitCrossfadeEditMode(apply: true);
        window.ExitRequested += () => ExitCrossfadeEditMode(apply: false);
        window.AutoSearchRequested += RunCrossfadeAutoSearch;
        window.SnapZeroRequested += SnapCrossfadeZero;
        window.SingleLoopChanged += OnCrossfadeSingleLoopChanged;
        window.SetSingleLoopChecked(LibraryBrowser.AutoLoopEnabled);
        _crossfadeWindow = window;
        return window;
    }

    private void OnCrossfadeSingleLoopChanged(bool enabled)
    {
        if (LibraryBrowser.AutoLoopEnabled == enabled)
        {
            return;
        }

        LibraryBrowser.AutoLoopEnabled = enabled;
    }

    private bool TryEnterCrossfadeEditMode()
    {
        if (IsLibraryMaximized || _document is null || _document.FrameCount <= 0)
        {
            return false;
        }

        if (!_document.AllowsRegionsAndLoops)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorMp3NoRegionLoop,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return true;
        }

        if (IsCrossfadeEditMode)
        {
            EnsureCrossfadeWindow().Present();
            return true;
        }

        _crossfadeLoopBefore = _document.SampleLoop;
        _crossfadeEdit = CrossfadeEditState.CreateInitial(_document);
        _crossfadeEdit.AlignLoopEndsToTwentyEight =
            AppStorage.Settings.FadeToolAlignLoopEndsToTwentyEight;
        if (_crossfadeEdit.AlignLoopEndsToTwentyEight)
        {
            _crossfadeEdit.Clamp(_document.FrameCount);
        }

        ApplySampleLoopFromDraft();
        var window = EnsureCrossfadeWindow();
        window.SetDocument(_document);
        window.State = _crossfadeEdit;
        window.SetSingleLoopChecked(LibraryBrowser.AutoLoopEnabled);
        window.RefreshUiStrings();
        // 前ファイルの重ね／プレビューキャッシュを捨ててからドラフトを載せる。
        Waveform.ClearCrossfadeCaches();
        Waveform.CrossfadeDraft = _crossfadeEdit;
        Waveform.SetPreviewGain(null);
        Waveform.InvalidateVisual();
        SyncPlayWindowFromDocument();
        window.Present();
        return true;
    }

    /// <summary>ドラフトの S–E をサンプルループへ仮反映（履歴なし。キャンセル時に戻す）。</summary>
    /// <param name="refreshWaveform">
    /// false のとき波形ビットマップは汚さず旗／ループ表示だけ更新（0.5 秒デバウンス中用）。
    /// </param>
    private void ApplySampleLoopFromDraft(bool refreshWaveform = true)
    {
        if (_document is null || _crossfadeEdit is null)
        {
            return;
        }

        var range = _crossfadeEdit.LoopRange.Clamp(_document.FrameCount);
        if (range.IsEmpty || range.Length <= 0)
        {
            return;
        }

        if (_document.SampleLoop == range)
        {
            return;
        }

        var loopBefore = _document.SampleLoop;
        _document.SetSampleLoop(range, markDirty: false);
        // S/E でループ端が動いたとき、一致していた波形選択端も追従させる。
        Waveform.FollowSelectionToSampleLoopChange(loopBefore, range);
        if (refreshWaveform)
        {
            Waveform.Refresh();
            Overview.Refresh();
        }
        else
        {
            Waveform.InvalidateCrossfadeChrome();
        }

        SyncViewChrome();
    }

    private void ExitCrossfadeEditMode(bool apply)
    {
        if (_exitingCrossfade)
        {
            return;
        }

        if (!IsCrossfadeEditMode && _crossfadeWindow is not { IsVisible: true })
        {
            return;
        }

        _exitingCrossfade = true;
        try
        {
            if (apply)
            {
                ApplyCrossfadeLoopMix();
            }
            else if (_document is not null)
            {
                // キャンセル: 入場前のサンプルループへ戻し、補助表示を消す
                if (_document.SampleLoop != _crossfadeLoopBefore)
                {
                    _document.SetSampleLoop(_crossfadeLoopBefore, markDirty: false);
                    Waveform.Refresh();
                    Overview.Refresh();
                    SyncViewChrome();
                }
            }

            ClearCrossfadeEditUi();
            Waveform.Focus();
        }
        finally
        {
            _exitingCrossfade = false;
        }
    }

    private void ClearCrossfadeEditUi()
    {
        _crossfadeEdit = null;
        _crossfadeLoopBefore = WaveSelection.Empty;
        if (_crossfadeWindow is not null)
        {
            _crossfadeWindow.State = null;
            _crossfadeWindow.SetDocument(null);
            _crossfadeWindow.HideFromHost();
        }

        if (Waveform.CrossfadeDraft is not null)
        {
            Waveform.CrossfadeDraft = null;
            Waveform.InvalidateVisual();
        }

        Waveform.ClearCrossfadeCaches();
        Waveform.SetPreviewGain(null);
        _player.SetLoopFadePreview(null);
        _player.PlayExitLayer = WaapiBar.PlayPostExitChecked;
        SyncPlayWindowFromDocument();
    }

    /// <summary>タブ切替・プレイヤー遷移などでモードを閉じる。</summary>
    private void CancelCrossfadeEditMode()
    {
        if (IsCrossfadeEditMode || _crossfadeWindow is { IsVisible: true })
        {
            ExitCrossfadeEditMode(apply: false);
        }
    }

    /// <param name="debounceWaveRedraw">本体ピークを 0.5 秒デバウンス。</param>
    /// <param name="prePostLight">
    /// Pre/Post のみの変更。オーバーレイ＋数値だけ更新し、
    /// サンプルループ／再生窓／継ぎ目は触らない（デバウンス後や S/E 変更時に Full）。
    /// </param>
    private void OnCrossfadeStateChanged(bool debounceWaveRedraw = false, bool prePostLight = false)
    {
        if (_crossfadeEdit is null || _document is null)
        {
            return;
        }

        _crossfadeEdit.Clamp(_document.FrameCount);
        Waveform.CrossfadeDraft = _crossfadeEdit;
        Waveform.SetPreviewGain(null);
        Waveform.RequestCrossfadeWaveRedraw(immediate: !debounceWaveRedraw);

        if (prePostLight)
        {
            // 数値だけ。継ぎ目は本体デバウンスに合わせて後で更新。
            _crossfadeWindow?.SyncFromHost(updateSplice: false);
            return;
        }

        // デバウンス中はサンプルループ位置だけ仮反映し、本体 raw/全ラスタは起こさない。
        ApplySampleLoopFromDraft(refreshWaveform: !debounceWaveRedraw);
        SyncPlayWindowFromDocument();
        if (debounceWaveRedraw)
        {
            _crossfadeWindow?.SyncFromHost(updateSplice: false);
            return;
        }

        _crossfadeWindow?.SyncFromHost(updateSplice: true);
        _crossfadeWindow?.NotifyDocumentChanged();
    }

    /// <summary>決定: ループミックスを焼き込み（呼び出し元でモード終了）。</summary>
    private void ApplyCrossfadeLoopMix()
    {
        if (_document is null || _crossfadeEdit is null)
        {
            return;
        }

        _crossfadeEdit.Clamp(_document.FrameCount);
        var command = ProcessEdits.ApplyLoopMix(
            _document,
            _crossfadeEdit,
            lowerClippedCrossfade: false,
            out var clip);
        if (command is null)
        {
            return;
        }

        if (clip.ExceedsFullScale)
        {
            var answer = OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ConfirmCrossfadeClip(TabMerge.FormatOverDb(clip.OverDb)),
                UiStrings.AppName,
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.OK);
            if (answer == MessageBoxResult.OK)
            {
                command = ProcessEdits.ApplyLoopMix(
                    _document,
                    _crossfadeEdit,
                    lowerClippedCrossfade: true,
                    out _);
                if (command is null)
                {
                    return;
                }
            }
        }

        StopPlaybackForEdit();
        _history.Do(_document, command);
        AfterEdit();
    }

    private void OnCrossfadeFieldFocusRequested(CrossfadeEditState.FocusField field)
    {
        if (_crossfadeEdit is null)
        {
            return;
        }

        JumpCrossfade(field switch
        {
            CrossfadeEditState.FocusField.End => CrossfadeEditJump.End,
            CrossfadeEditState.FocusField.PreCrossfade => CrossfadeEditJump.StartPlusX,
            CrossfadeEditState.FocusField.PostCrossfade => CrossfadeEditJump.EndPlusX,
            _ => CrossfadeEditJump.Start,
        });
    }

    private void JumpCrossfade(CrossfadeEditJump jump)
    {
        if (_document is null || _crossfadeEdit is null)
        {
            return;
        }

        long frame = jump switch
        {
            CrossfadeEditJump.Start => _crossfadeEdit.StartFrame,
            CrossfadeEditJump.End => _crossfadeEdit.EndFrame,
            CrossfadeEditJump.StartPlusX => _crossfadeEdit.PreFlagFrame,
            CrossfadeEditJump.EndPlusX => _crossfadeEdit.PostFlagFrame,
            _ => _crossfadeEdit.StartFrame,
        };
        frame = Math.Clamp(frame, 0, _document.FrameCount);
        Waveform.SeekKeepingSelection(frame);
        Waveform.CenterViewOnPlayhead();
    }

    private void RunCrossfadeAutoSearch(CrossfadeAutoSearchKind kind)
    {
        if (_document is null || _crossfadeEdit is null)
        {
            return;
        }

        switch (kind)
        {
            case CrossfadeAutoSearchKind.StartAndEnd:
                LoopAutoSearch.AdjustStartAndEnd(
                    _document.Interleaved,
                    _document.Channels,
                    _document.FrameCount,
                    _crossfadeEdit);
                break;
            case CrossfadeAutoSearchKind.StartOnly:
                LoopAutoSearch.AdjustStart(
                    _document.Interleaved,
                    _document.Channels,
                    _document.FrameCount,
                    _crossfadeEdit);
                break;
            case CrossfadeAutoSearchKind.EndOnly:
                LoopAutoSearch.AdjustEnd(
                    _document.Interleaved,
                    _document.Channels,
                    _document.FrameCount,
                    _crossfadeEdit);
                break;
        }

        // 28 倍数モードなら探索結果も格子へ揃える
        _crossfadeEdit.Clamp(_document.FrameCount);

        if (_crossfadeWindow is not null)
        {
            _crossfadeWindow.State = _crossfadeEdit;
        }

        OnCrossfadeStateChanged();
    }

    private void SnapCrossfadeZero(CrossfadeEditState.FocusField field)
    {
        if (_document is null || _crossfadeEdit is null)
        {
            return;
        }

        var interleaved = _document.Interleaved;
        var channels = _document.Channels;
        var frames = _document.FrameCount;
        var align28 = _crossfadeEdit.AlignLoopEndsToTwentyEight;
        switch (field)
        {
            case CrossfadeEditState.FocusField.Start:
                // SetStart 経由で UI／サンプルループへ反映。28 倍数モード時は格子上のゼロクロスへ。
                _crossfadeEdit.SetStart(
                    LoopCrossfade.SnapToZeroCrossing(
                        interleaved,
                        channels,
                        frames,
                        _crossfadeEdit.StartFrame,
                        alignToTwentyEight: align28),
                    frames);
                break;
            case CrossfadeEditState.FocusField.End:
                // 継ぎ目に出るのは E−1。そこをゼロクロスへ寄せてから半開の E を決める。
                _crossfadeEdit.SetEnd(
                    LoopCrossfade.SnapEndToZeroCrossing(
                        interleaved,
                        channels,
                        frames,
                        _crossfadeEdit.EndFrame,
                        alignToTwentyEight: align28),
                    frames);
                break;
            case CrossfadeEditState.FocusField.PreCrossfade:
                {
                    var flag = LoopCrossfade.SnapToZeroCrossing(
                        interleaved, channels, frames, _crossfadeEdit.PreFlagFrame);
                    _crossfadeEdit.SetPreCrossfade(
                        Math.Max(0, _crossfadeEdit.StartFrame - flag), frames);
                    break;
                }
            case CrossfadeEditState.FocusField.PostCrossfade:
                {
                    // 赤旗は S＋Post（材料側）。プリと同様、旗をゼロクロスへ寄せて長さ＝旗−S。
                    var target = Math.Clamp(_crossfadeEdit.PostFlagFrame, 0, Math.Max(0, frames - 1));
                    var flag = LoopCrossfade.SnapToZeroCrossing(
                        interleaved, channels, frames, target);
                    _crossfadeEdit.SetPostCrossfade(
                        Math.Max(0, flag - _crossfadeEdit.StartFrame), frames);
                    break;
                }
        }

        if (_crossfadeWindow is not null)
        {
            _crossfadeWindow.State = _crossfadeEdit;
        }

        OnCrossfadeStateChanged();
    }

    private bool TryProcessCrossfadeShortcut(Key key, ModifierKeys modifiers)
    {
        if (key == Key.X && modifiers == ModifierKeys.Shift && !IsLibraryMaximized)
        {
            return TryEnterCrossfadeEditMode();
        }

        if (!IsCrossfadeEditMode || _crossfadeEdit is null || _document is null)
        {
            return false;
        }

        if (_crossfadeWindow is { IsActive: true }
            && Keyboard.FocusedElement is TextBox
            && key is not (Key.Escape or Key.Enter))
        {
            return false;
        }

        if (modifiers == ModifierKeys.None && key == Key.Escape)
        {
            ExitCrossfadeEditMode(apply: false);
            return true;
        }

        if (modifiers == ModifierKeys.None && key == Key.Enter)
        {
            ExitCrossfadeEditMode(apply: true);
            return true;
        }

        // Space／停止は本体トランスポートに任せ、サンプルループ無限再生にする。

        if (modifiers == ModifierKeys.Alt)
        {
            switch (key)
            {
                case Key.S:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.Start);
                    return true;
                case Key.E:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.End);
                    return true;
                case Key.P:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PreCrossfade);
                    return true;
                case Key.T:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PostCrossfade);
                    return true;
            }
        }

        if (modifiers == ModifierKeys.None)
        {
            switch (key)
            {
                case Key.S:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.Start);
                    return true;
                case Key.E:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.End);
                    return true;
                case Key.X:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PreCrossfade);
                    return true;
                case Key.F:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PreCrossfade);
                    return true;
                case Key.P:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PostCrossfade);
                    return true;
                case Key.T:
                    EnsureCrossfadeWindow().FocusValueField(CrossfadeEditState.FocusField.PostCrossfade);
                    return true;
                case Key.Left:
                    _crossfadeEdit.Nudge(_crossfadeEdit.Focus, -1, _document.FrameCount);
                    _crossfadeWindow?.SyncFromHost();
                    OnCrossfadeStateChanged();
                    return true;
                case Key.Right:
                    _crossfadeEdit.Nudge(_crossfadeEdit.Focus, 1, _document.FrameCount);
                    _crossfadeWindow?.SyncFromHost();
                    OnCrossfadeStateChanged();
                    return true;
                case Key.OemPlus:
                case Key.Add:
                    _crossfadeEdit.Nudge(_crossfadeEdit.Focus, 1, _document.FrameCount);
                    _crossfadeWindow?.SyncFromHost();
                    OnCrossfadeStateChanged();
                    return true;
                case Key.OemMinus:
                case Key.Subtract:
                    _crossfadeEdit.Nudge(_crossfadeEdit.Focus, -1, _document.FrameCount);
                    _crossfadeWindow?.SyncFromHost();
                    OnCrossfadeStateChanged();
                    return true;
            }
        }

        if (modifiers == ModifierKeys.None
            // C はチャンネル変換へ落ちないよう握るだけ（切り落としトグルは廃止）。
            && key is Key.S or Key.E or Key.C or Key.X or Key.I or Key.O or Key.N or Key.R or Key.V or Key.P or Key.T or Key.B)
        {
            return true;
        }

        return false;
    }

    private void RefreshCrossfadeLocalizedText() => _crossfadeWindow?.RefreshUiStrings();

    private void OnWaveformCrossfadeDraftChanged(object? sender, EventArgs e)
    {
        if (_crossfadeEdit is null)
        {
            return;
        }

        var debounce = Waveform.ConsumeCrossfadeDraftChangeDebounce();
        // Pre/Post のみ light（サンプルループ非更新）。S/E は debounce でもループは仮反映する。
        var light = Waveform.IsCrossfadePrePostFlagDragging;
        OnCrossfadeStateChanged(debounceWaveRedraw: debounce || light, prePostLight: light);
    }
}
