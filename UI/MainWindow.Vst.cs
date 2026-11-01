using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private int _vstApplyEpoch;
    private readonly Dictionary<int, VstPluginEditorWindow> _vstEditors = new();
    /// <summary>差し替えで Orphan した窓。プロセッサ Dispose（removed）後に Close する。</summary>
    private readonly List<VstPluginEditorWindow> _orphanedVstEditors = new();
    private int _vstAssignGate;

    /// <summary>
    /// アクティブ文書のインサートをエンジンへ反映。プレイヤー時はハードバイパス。
    /// VST3 は STA（UI スレッド）で Load／initialize する。
    /// </summary>
    private void SyncVstInsertEngine() => _ = SyncVstInsertEngineAsync(openEditorSlot: null);

    private Task SyncVstInsertEngineAsync() => SyncVstInsertEngineAsync(openEditorSlot: null);

    private async Task SyncVstInsertEngineAsync(int? openEditorSlot)
    {
        var epoch = Interlocked.Increment(ref _vstApplyEpoch);
        if (!IsVstInsertUiEnabled())
        {
            ApplyVstInsertsOnUiThread(null, null, playerBypass: true);
            if (epoch != _vstApplyEpoch)
            {
                return;
            }

            CloseAllVstEditors(resumeAudioOnClose: false);
            SyncVstInsertRackUi();
            return;
        }

        var session = _activeSession;
        var player = IsLibraryMaximized;

        // 差し替え中は CaptureState しない（閉じかけの IPlugView 上で GetState すると固まる）
        if (!player && openEditorSlot is null)
        {
            _player.VstInserts.CaptureAppliedStates();
        }

        CloseEditorsForPendingReload(session?.VstInserts);

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (epoch != _vstApplyEpoch)
            {
                return;
            }

            if (session is null)
            {
                ApplyVstInsertsOnUiThread(null, null, playerBypass: true);
            }
            else
            {
                ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: player);
            }

            // ScheduleDispose が UI に載るのを待ち、その後 Orphan 窓を Close
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SweepOrphanedVstEditors();
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        if (epoch != _vstApplyEpoch)
        {
            return;
        }

        SyncVstInsertRackUi();
        CloseEditorsForEmptySlots(session?.VstInserts);
        SweepOrphanedVstEditors();
        if (openEditorSlot is int slot && !player)
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await Task.Delay(200);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            if (epoch != _vstApplyEpoch)
            {
                return;
            }

            try
            {
                OpenVstEditorForSlot(slot);
            }
            catch
            {
                if (!VstAppReplaceSmokeActive)
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorVstInsertEditorFailed,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
    }

    /// <summary>
    /// プラグイン割り当て。既に別プラグインがある差し替えは
    /// 「アンロード → 待機 → ロード → エディタ」の二段階にし、同一 Apply 内の破棄＋生成を避ける。
    /// </summary>
    private void OnVstInsertAssignRequested(int slotIndex, VstPickerEntry entry) =>
        _ = AssignVstPluginAsync(slotIndex, entry);

    private async Task AssignVstPluginAsync(int slotIndex, VstPickerEntry entry)
    {
        if ((uint)slotIndex >= VstInsertChain.MaxSlots || _activeSession is null)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _vstAssignGate, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var session = _activeSession;
            var slot = session.VstInserts.Slots[slotIndex];
            var replacing = !slot.IsEmpty
                && (!string.Equals(slot.PluginPath, entry.Path, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(slot.ClassId ?? string.Empty, entry.ClassId ?? string.Empty, StringComparison.Ordinal));

            // Altiverb はデバッガ付きプロセスでは Init が戻らない（ネイティブでも 30s タイムアウト）。
            // 試行せず即拒否。F5 は「デバッガなし」構成で exe 手渡し起動すること。
            if (Vst3DebuggerLoadGuard.ShouldRefuseUnderDebugger(entry.Path))
            {
                if (!VstAppReplaceSmokeActive)
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorVstInsertAltiverbNeedsExeLaunch,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }

                return;
            }

            // Soft Limit（UI STA で create）を MTA で Dispose しながら Altiverb を UI で
            // create するとハングする。Process 停止後、同じ UI STA で破棄を完了させてから作る。
            _player.VstInserts.BeginExclusiveLoad();
            try
            {
                await _player.VstInserts.WaitForProcessUsersAsync(2000);
                await Dispatcher.Yield(DispatcherPriority.Background);

                // Soft Limit は DetachPluginNow（removed/Dispose）や DestroyWindow で固まることがある。
                // 窓は Orphan（Hide のみ）し、IPlugView teardown はプロセッサ同期 Dispose に任せる。
                OrphanVstEditorForReplace(slotIndex);
                await Dispatcher.Yield(DispatcherPriority.Background);
                await Task.Delay(30);

                if (replacing)
                {
                    session.VstInserts.ClearSlot(slotIndex);
                    slot = session.VstInserts.Slots[slotIndex];
                    VstInsertRack.Refresh();
                    // Exclusive 中の Apply → Soft Limit を UI STA で同期 Dispose（removed 含む）
                    ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: IsLibraryMaximized);
                    SweepOrphanedVstEditors();
                    await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    await Task.Delay(50);
                }

                slot = session.VstInserts.Slots[slotIndex];
                slot.PluginPath = entry.Path;
                slot.DisplayName = entry.Name;
                slot.ClassId = entry.ClassId;
                slot.StateBlob = null;
                slot.Bypassed = false;
                slot.Faulted = false;
                VstInsertRack.Refresh();

                // Soft Limit 破棄済みの同一 STA で Altiverb を create
                ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: IsLibraryMaximized);

                var live = _player.VstInserts.TryGetLive(slotIndex);
                if (live is null || live.Faulted)
                {
                    slot.ClassId = null;
                    slot.Faulted = false;
                    ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: IsLibraryMaximized);
                    live = _player.VstInserts.TryGetLive(slotIndex);
                }

                SyncVstInsertRackUi();
            }
            finally
            {
                SweepOrphanedVstEditors();
                _player.VstInserts.EndExclusiveLoad();
                _player.VstInserts.SetSlotSuspended(slotIndex, false);
            }

            var loaded = _player.VstInserts.TryGetLive(slotIndex);
            if (!IsLibraryMaximized && loaded is not null && !loaded.Faulted)
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                await Task.Delay(150);
                OpenVstEditorForSlot(slotIndex);
            }
            else if (!IsLibraryMaximized)
            {
                if (!VstAppReplaceSmokeActive)
                {
                    OwnerCenteredMessageBox.Show(
                        this,
                        UiStrings.ErrorVstInsertLoadFailed,
                        UiStrings.AppName,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _vstAssignGate, 0);
        }
    }

    private void CloseEditorsForPendingReload(VstInsertChain? chain)
    {
        if (chain is null)
        {
            CloseAllVstEditors(resumeAudioOnClose: false);
            return;
        }

        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (!_vstEditors.ContainsKey(i))
            {
                continue;
            }

            var want = chain.Slots[i];
            var live = _player.VstInserts.TryGetLive(i);
            if (want.IsEmpty || live is null)
            {
                DetachAndCloseVstEditor(i);
                continue;
            }

            var same = string.Equals(live.PluginPath, want.PluginPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(live.ClassId ?? string.Empty, want.ClassId ?? string.Empty, StringComparison.Ordinal);
            if (!same)
            {
                DetachAndCloseVstEditor(i);
            }
        }
    }

    private void ApplyVstInsertsOnUiThread(object? owner, VstInsertChain? chain, bool playerBypass) =>
        _player.VstInserts.Apply(owner, chain, playerBypass);

    private void CloseEditorsForEmptySlots(VstInsertChain? chain)
    {
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (chain is null || chain.Slots[i].IsEmpty)
            {
                DetachAndCloseVstEditor(i);
            }
        }
    }

    private void SyncVstInsertRackUi()
    {
        if (!IsVstInsertUiEnabled())
        {
            VstInsertRack.Visibility = Visibility.Collapsed;
            return;
        }

        var show = !IsLibraryMaximized
            && TransportChromeHost.Visibility == Visibility.Visible
            && _activeSession is not null;
        VstInsertRack.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        VstInsertRack.IsHitTestVisible = show;
        VstInsertRack.Opacity = show ? 1 : 0;
        VstInsertRack.SetSession(
            show ? _activeSession : null,
            OnVstInsertChainChanged,
            OnVstInsertOpenEditorRequested,
            OnVstInsertAssignRequested);
        if (show)
        {
            VstInsertRack.ApplyTheme();
        }
    }

    private static bool IsVstInsertUiEnabled() => VstFeature.IsEnabled;

    private void OnVstInsertChainChanged() => SyncVstInsertEngine();

    private void OnVstInsertOpenEditorRequested(int slotIndex)
    {
        // ロード済みなら Apply を再実行せず UI だけ開く（差し替え直後の再 Apply で失敗しやすい）
        var live = _player.VstInserts.TryGetLive(slotIndex);
        if (live is not null && !live.Faulted)
        {
            OpenVstEditorForSlot(slotIndex);
            return;
        }

        _ = SyncVstInsertEngineAsync(openEditorSlot: slotIndex);
    }

    private void OpenVstEditorForSlot(int slotIndex, bool keepSuspended = false)
    {
        if ((uint)slotIndex >= VstInsertChain.MaxSlots)
        {
            return;
        }

        if (_vstEditors.TryGetValue(slotIndex, out var existing))
        {
            var liveExisting = _player.VstInserts.TryGetLive(slotIndex);
            var titleMatches = liveExisting is not null
                && string.Equals(existing.Title, liveExisting.DisplayName, StringComparison.Ordinal);
            if (existing.IsVisible && titleMatches)
            {
                existing.Activate();
                return;
            }

            DetachAndCloseVstEditor(slotIndex);
        }

        var live = _player.VstInserts.TryGetLive(slotIndex);
        if (live is null || live.Faulted)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorVstInsertEditorFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _player.VstInserts.SetSlotSuspended(slotIndex, true);
        live.ResetEditor();

        Vst3EditorSession? session = null;
        for (var attempt = 0; attempt < 3 && session is null; attempt++)
        {
            try
            {
                session = live.TryOpenEditor();
            }
            catch
            {
                session = null;
            }

            if (session is null)
            {
                live.ResetEditor();
            }
        }

        if (session is null)
        {
            if (!keepSuspended)
            {
                _player.VstInserts.SetSlotSuspended(slotIndex, false);
            }

            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorVstInsertEditorFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Owner 無し（所有関係は子ダイアログとデッドロックしやすい）。前面には Topmost で出す。
        var window = new VstPluginEditorWindow(null, session)
        {
            SlotIndex = slotIndex,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
        };
        window.Closed += (_, _) =>
        {
            if (_vstEditors.TryGetValue(slotIndex, out var current) && ReferenceEquals(current, window))
            {
                _vstEditors.Remove(slotIndex);
            }

            if (window.ResumeAudioOnClose)
            {
                _player.VstInserts.SetSlotSuspended(slotIndex, false);
            }
        };
        _vstEditors[slotIndex] = window;
        try
        {
            window.Show();
            window.Activate();
            if (!keepSuspended)
            {
                _player.VstInserts.SetSlotSuspended(slotIndex, false);
            }
        }
        catch
        {
            if (!keepSuspended)
            {
                _player.VstInserts.SetSlotSuspended(slotIndex, false);
            }

            try
            {
                session.Dispose();
            }
            catch
            {
            }

            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorVstInsertEditorFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// ExclusiveLoad 後の差し替え用。removed/DestroyWindow は呼ばず Hide のみ。
    /// </summary>
    private void OrphanVstEditorForReplace(int slotIndex)
    {
        _player.VstInserts.SetSlotSuspended(slotIndex, true);
        if (!_vstEditors.Remove(slotIndex, out var window))
        {
            return;
        }

        try
        {
            window.ResumeAudioOnClose = false;
            window.OrphanCloseForReplace();
            _orphanedVstEditors.Add(window);
        }
        catch
        {
        }
    }

    /// <summary>
    /// プロセッサ Dispose（removed）後に Orphan 窓の HWND を捨てる。
    /// </summary>
    private void SweepOrphanedVstEditors()
    {
        if (_orphanedVstEditors.Count == 0)
        {
            return;
        }

        var list = _orphanedVstEditors.ToArray();
        _orphanedVstEditors.Clear();
        foreach (var window in list)
        {
            try
            {
                window.Close();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// Soft Limit 等は DetachPluginNow（removed/DestroyWindow）で固まることがあるので常に Orphan。
    /// </summary>
    private void DetachAndCloseVstEditor(int slotIndex) => OrphanVstEditorForReplace(slotIndex);

    private void CloseAllVstEditors(bool resumeAudioOnClose = true)
    {
        foreach (var key in _vstEditors.Keys.ToArray())
        {
            if (!resumeAudioOnClose)
            {
                DetachAndCloseVstEditor(key);
            }
            else
            {
                CloseVstEditor(key, resumeAudioOnClose: true);
            }
        }

        _vstEditors.Clear();
    }

    /// <summary>アプリ終了時。Owner 無しのプラグイン窓／Orphan を確実に閉じる。</summary>
    internal void CloseAllVstEditorsForAppExit()
    {
        foreach (var key in _vstEditors.Keys.ToArray())
        {
            if (!_vstEditors.Remove(key, out var window))
            {
                continue;
            }

            try
            {
                window.ForceCloseForAppExit();
            }
            catch
            {
            }
        }

        var orphans = _orphanedVstEditors.ToArray();
        _orphanedVstEditors.Clear();
        foreach (var window in orphans)
        {
            try
            {
                window.ForceCloseForAppExit();
            }
            catch
            {
            }
        }
    }

    private void CloseVstEditor(int slotIndex, bool resumeAudioOnClose = true)
    {
        if (!resumeAudioOnClose)
        {
            DetachAndCloseVstEditor(slotIndex);
            return;
        }

        if (_vstEditors.Remove(slotIndex, out var window))
        {
            try
            {
                window.ResumeAudioOnClose = true;
                window.Close();
            }
            catch
            {
            }
        }
    }

    private void ForgetVstInserts(DocumentSession session)
    {
        CloseAllVstEditors(resumeAudioOnClose: false);
        session.VstInserts.Clear();
        _player.VstInserts.Forget(session);
    }

    private void ForgetVstInserts(IEnumerable<DocumentSession> sessions)
    {
        foreach (var session in sessions)
        {
            ForgetVstInserts(session);
        }
    }
}
