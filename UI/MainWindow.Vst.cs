using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private int _vstApplyEpoch;
    private readonly Dictionary<int, VstPluginEditorWindow> _vstEditors = new();
    /// <summary>差し替えで Orphan した窓。プロセッサ Dispose（removed）後に Close する。</summary>
    private readonly List<VstPluginEditorWindow> _orphanedVstEditors = new();
    /// <summary>スロットごとの GUI 左上（DIP）。アプリ終了で破棄（永続化しない）。</summary>
    private readonly Dictionary<int, Point> _vstEditorPositions = new();
    private int _vstAssignGate;

    /// <summary>
    /// アクティブ文書のインサートをエンジンへ反映。プレイヤー時はハードバイパス。
    /// VST3 は STA（UI スレッド）で Load／initialize する。
    /// エディタ復帰中（波形優先）は LeaveLibraryMaximizeAsync が後で呼ぶまで抑止する。
    /// </summary>
    private void SyncVstInsertEngine()
    {
        if (_deferVstInsertSyncForEditorEnter)
        {
            return;
        }

        _ = SyncVstInsertEngineAsync(openEditorSlot: null, preferPlayback: false);
    }

    private Task SyncVstInsertEngineAsync(int? openEditorSlot) =>
        SyncVstInsertEngineAsync(openEditorSlot, preferPlayback: false);

    /// <param name="preferPlayback">
    /// true のとき Wait カーソルを出さず、入力／再生を先に通してからプラグインを載せる。
    /// </param>
    private async Task SyncVstInsertEngineAsync(int? openEditorSlot, bool preferPlayback)
    {
        if (_deferVstInsertSyncForEditorEnter)
        {
            return;
        }

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

        // 差し替え中は CaptureState しない（閉じかけの IPlugView 上で GetState すると固まる）。
        // プレイヤー入りでもライブ破棄前にパラメータを書き戻す。
        if (openEditorSlot is null)
        {
            var captured = _player.VstInserts.CaptureAppliedStates();
            if (captured is not null)
            {
                FlushPersistedInsertParameters(captured);
            }
        }

        CloseEditorsForPendingReload(session?.VstInserts);

        var showWait = !preferPlayback;
        if (showWait)
        {
            Mouse.OverrideCursor = Cursors.Wait;
        }

        try
        {
            if (preferPlayback)
            {
                // スペース再生などを先に捌き、プラグイン create は後回し。
                await Dispatcher.Yield(DispatcherPriority.Input);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            else
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

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
            if (showWait)
            {
                Mouse.OverrideCursor = null;
            }
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
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorVstInsertEditorFailed,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    /// <summary>
    /// プラグイン割り当て。既に別プラグインがある差し替えは
    /// 「アンロード → 待機 → ロード → エディタ」の二段階にし、同一 Apply 内の破棄＋生成を避ける。
    /// </summary>
    private void OnVstInsertAssignRequested(int slotIndex, VstPickerEntry entry, bool allTabs) =>
        _ = AssignVstPluginAsync(slotIndex, entry, allTabs);

    private async Task AssignVstPluginAsync(int slotIndex, VstPickerEntry entry, bool allTabs)
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
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorVstInsertAltiverbNeedsExeLaunch,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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

                if (allTabs && _sessions.Count > 1)
                {
                    PropagateVstInsertChainToAllTabs();
                }

                // 永続化中スロットなら、今刺さったプラグインで記憶を更新し全タブへ載せる。
                _player.VstInserts.CaptureAppliedStates();
                if (DocumentSessionStore.TrySyncDefaultInsertFromSlot(
                        AppStorage.Settings,
                        slotIndex,
                        session.VstInserts.Slots[slotIndex]))
                {
                    AppStorage.Save();
                    SyncPersistedVstInsertsAcrossTabs();
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
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.ErrorVstInsertLoadFailed,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
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
                OrphanVstEditorForReplace(i);
                continue;
            }

            var same = string.Equals(live.PluginPath, want.PluginPath, StringComparison.OrdinalIgnoreCase)
                && string.Equals(live.ClassId ?? string.Empty, want.ClassId ?? string.Empty, StringComparison.Ordinal);
            if (!same)
            {
                OrphanVstEditorForReplace(i);
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
                OrphanVstEditorForReplace(i);
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
            OnVstInsertAssignRequested,
            OnVstInsertMoveRequested,
            () => _player.VstInserts.CaptureAppliedStates(),
            () => _sessions.Count > 1,
            SyncPersistedVstInsertsAcrossTabs,
            ClearMatchingVstInsertSlotAcrossTabs);
        if (show)
        {
            VstInsertRack.ApplyTheme();
        }
    }

    private static bool IsVstInsertUiEnabled() => VstFeature.IsEnabled;

    private void OnVstInsertChainChanged()
    {
        SyncVstInsertEngine();
        RefreshTransportCommandsEnabled();
    }

    /// <summary>
    /// 永続インサート（プラグイン＋パラメータ＋バイパス）を開いている全タブへ載せる。
    /// </summary>
    private void SyncPersistedVstInsertsAcrossTabs()
    {
        PropagatePersistedInsertSnapshotsToAllSessions();
        SyncVstInsertEngine();
        VstInsertRack.Refresh();
    }

    /// <summary>
    /// 設定上の永続スナップショットを、開いている全タブのチェーンへコピーする（エンジンは触らない）。
    /// </summary>
    private void PropagatePersistedInsertSnapshotsToAllSessions(bool applyBypass = true)
    {
        var defaults = AppStorage.Settings.DefaultVstInserts;
        if (!DocumentSessionStore.SnapshotHasAnyPlugin(defaults))
        {
            return;
        }

        foreach (var session in _sessions)
        {
            DocumentSessionStore.ApplyPersistedInserts(session.VstInserts, defaults, applyBypass);
        }
    }

    /// <summary>
    /// 同じスロット番号で、表示名（なければパス／ClassId）が一致するプラグインだけ全タブから外す。
    /// </summary>
    private void ClearMatchingVstInsertSlotAcrossTabs(
        int index,
        string? displayName,
        string? pluginPath,
        string? classId)
    {
        if ((uint)index >= VstInsertChain.MaxSlots)
        {
            return;
        }

        foreach (var session in _sessions)
        {
            var slot = session.VstInserts.Slots[index];
            if (VstInsertRackView.SlotMatchesPluginForClear(slot, displayName, pluginPath, classId))
            {
                session.VstInserts.ClearSlot(index);
            }
        }

        OrphanVstEditorForReplace(index);
        SyncVstInsertEngine();
        VstInsertRack.Refresh();
    }

    /// <summary>アクティブタブのインサート構成を他タブへコピーする（全タブへインサート時）。</summary>
    private void PropagateVstInsertChainToAllTabs()
    {
        if (_activeSession is null)
        {
            return;
        }

        var source = _activeSession.VstInserts;
        foreach (var session in _sessions)
        {
            if (ReferenceEquals(session, _activeSession))
            {
                continue;
            }

            session.VstInserts.CopyFrom(source);
        }
    }

    /// <summary>
    /// ドラッグでスロット移動。空き先なら移動、使用中どうしなら入れ替え。
    /// ライブプロセッサは破棄せず付け替える。
    /// </summary>
    private void OnVstInsertMoveRequested(int from, int to)
    {
        if (_activeSession is null
            || (uint)from >= VstInsertChain.MaxSlots
            || (uint)to >= VstInsertChain.MaxSlots
            || from == to
            || _activeSession.VstInserts.Slots[from].IsEmpty)
        {
            return;
        }

        var session = _activeSession;
        // 移動前にライブパラメータをスロットへ書き戻す（チェーン／永続の両方で受け継ぐ）。
        _player.VstInserts.CaptureAppliedStates();
        FlushPersistedInsertParameters(session.VstInserts);
        RemapVstEditorsForMove(from, to);
        session.VstInserts.MoveSlot(from, to);
        _player.VstInserts.MoveSlot(from, to, session.VstInserts);
        // 永続化も同じスロット番号へ追従（入れ替え／空きへの移動）。StateBase64 もスナップショットごと移る。
        var defaults = AppStorage.Settings.DefaultVstInserts;
        var persistMoved = DocumentSessionStore.SnapshotHasPluginAt(defaults, from)
            || DocumentSessionStore.SnapshotHasPluginAt(defaults, to);
        DocumentSessionStore.MoveDefaultInsertSlot(AppStorage.Settings, from, to);
        AppStorage.Save();
        // 永続スロットの移動は全タブ共通（構造＋パラメータ）。
        if (persistMoved)
        {
            foreach (var other in _sessions)
            {
                if (ReferenceEquals(other, session))
                {
                    continue;
                }

                other.VstInserts.MoveSlot(from, to);
            }

            PropagatePersistedInsertSnapshotsToAllSessions();
        }

        VstInsertRack.Refresh();
    }

    private void RemapVstEditorsForMove(int from, int to)
    {
        _vstEditors.TryGetValue(from, out var a);
        _vstEditors.TryGetValue(to, out var b);
        _vstEditors.Remove(from);
        _vstEditors.Remove(to);
        if (b is not null)
        {
            b.SlotIndex = from;
            _vstEditors[from] = b;
        }

        if (a is not null)
        {
            a.SlotIndex = to;
            _vstEditors[to] = a;
        }

        _vstEditorPositions.TryGetValue(from, out var posA);
        _vstEditorPositions.TryGetValue(to, out var posB);
        var hadA = _vstEditorPositions.Remove(from);
        var hadB = _vstEditorPositions.Remove(to);
        if (hadB)
        {
            _vstEditorPositions[from] = posB;
        }

        if (hadA)
        {
            _vstEditorPositions[to] = posA;
        }
    }

    private void RememberVstEditorPosition(int slotIndex, Window window)
    {
        if ((uint)slotIndex >= VstInsertChain.MaxSlots)
        {
            return;
        }

        try
        {
            var bounds = window.WindowState == WindowState.Normal
                ? new Rect(window.Left, window.Top, window.Width, window.Height)
                : window.RestoreBounds;
            _vstEditorPositions[slotIndex] = new Point(bounds.X, bounds.Y);
        }
        catch
        {
        }
    }

    /// <summary>選択タブ（無ければアクティブ）のうち、バウンス可能なもの。</summary>
    private DocumentSession[] ResolveBounceTargets()
    {
        IEnumerable<DocumentSession> candidates = HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is not null ? [_activeSession] : [];
        return candidates.Where(VstInsertRackView.SessionCanBake).ToArray();
    }

    private bool CanBounceSelectedVstInserts() =>
        !IsUiBusy
        && !IsRecording
        && !IsLibraryMaximized
        && VstFeature.IsEnabled
        && ResolveBounceTargets().Length > 0;

    /// <summary>ミックスダウン可能か（選択が 2 本以上。メニュー／ツールバー共通）。</summary>
    private bool CanMergeSelectedTabs() =>
        !IsUiBusy
        && !IsRecording
        && ResolveBatchEditTargets().Length >= 2;

    private async Task BakeVstInsertsAsync()
    {
        if (IsUiBusy || RejectOutsideFadeToolAction())
        {
            return;
        }

        // 共有インサートエンジンのため、必ず 1 ファイルずつ順に焼く。
        var targets = ResolveBounceTargets();
        if (targets.Length == 0)
        {
            return;
        }

        // チェックボックスは出さず、実行時にテイルを残すか問う（既定は残す）。
        var includeTailChoice = OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmVstBounceIncludeTail,
            UiStrings.TitleVstBounceIncludeTail,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes,
            UiStrings.ButtonVstBounceKeepTail,
            UiStrings.ButtonVstBounceCutTail,
            orangeNo: true);
        if (includeTailChoice == MessageBoxResult.Cancel)
        {
            return;
        }

        var includeTail = includeTailChoice != MessageBoxResult.No;

        var activeBefore = _activeSession;
        StopPlaybackForEdit();
        _player.VstInserts.CaptureAppliedStates();

        _vstBakeBusy = true;
        RefreshTransportCommandsEnabled();
        Exception? error = null;
        var bakedActive = false;
        var jobProgress = new double[targets.Length];
        try
        {
            ShowBusyGlass(UiStrings.OverlayVstInsertBake);
            ReportBakeGlassProgress(targets, jobProgress, currentIndex: 0, fileProgress: 0);

            for (var t = 0; t < targets.Length; t++)
            {
                var session = targets[t];
                if (!IsLoaded)
                {
                    return;
                }

                // すりガラスを先に更新してから重い処理へ。
                ReportBakeGlassProgress(targets, jobProgress, t, fileProgress: 0);
                await Dispatcher.Yield(DispatcherPriority.Render);

                var index = t;
                var progress = new Progress<double>(p =>
                {
                    jobProgress[index] = Math.Clamp(p, 0, 1);
                    ReportBakeGlassProgress(targets, jobProgress, index, jobProgress[index]);
                });

                if (!await BakeOneSessionAsync(session, progress, includeTail).ConfigureAwait(true))
                {
                    return;
                }

                jobProgress[t] = 1;
                ReportBakeGlassProgress(targets, jobProgress, t, fileProgress: 1);

                if (ReferenceEquals(session, activeBefore))
                {
                    bakedActive = true;
                }

                // 次ファイルへ切り替える前に UI／エンジンを一息入れる（共有インサートの付け替え）。
                await Dispatcher.Yield(DispatcherPriority.Background);
            }

            // 全ターゲット焼き終わり後に、永続スロットのバイパスを記憶へ載せて全タブへ揃える。
            // 途中で載せると未焼成タブまでバイパスされ、オフラインが dry になる。
            SyncBakeBypassIntoPersistedDefaults(targets);

            if (activeBefore is not null && ReferenceEquals(_activeSession, activeBefore))
            {
                ApplyVstInsertsOnUiThread(activeBefore, activeBefore.VstInserts, playerBypass: IsLibraryMaximized);
                _player.VstInserts.UnloadBypassedLiveSlots();
                VstInsertRack.Refresh();
            }

            if (bakedActive && ReferenceEquals(_activeSession, activeBefore))
            {
                AfterTransform();
            }
            else
            {
                LoudnessMeter.RefreshOffline();
                RefreshStatus();
                RefreshHistoryStrip();
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _vstBakeBusy = false;
            RefreshTransportCommandsEnabled();
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
            return;
        }

        if (IsLoaded)
        {
            ShowEditAppliedToast("Bounce");
        }
    }

    /// <summary>
    /// すりガラスへ全体％と、各ファイル行（書き出しと同様）を出す。
    /// </summary>
    private void ReportBakeGlassProgress(
        IReadOnlyList<DocumentSession> targets,
        double[] jobProgress,
        int currentIndex,
        double fileProgress)
    {
        var total = targets.Count;
        if (total <= 0)
        {
            return;
        }

        var finished = 0;
        for (var i = 0; i < jobProgress.Length; i++)
        {
            if (jobProgress[i] >= 1)
            {
                finished++;
            }
        }

        var overall = 0d;
        for (var i = 0; i < jobProgress.Length; i++)
        {
            overall += Math.Clamp(jobProgress[i], 0, 1);
        }

        overall /= total;

        var running = new List<ExportJobProgress>(Math.Min(total, 8));
        for (var i = 0; i < total; i++)
        {
            var p = Math.Clamp(jobProgress[i], 0, 1);
            if (p >= 1 && i != currentIndex)
            {
                continue;
            }

            if (p <= 0 && i != currentIndex)
            {
                continue;
            }

            running.Add(new ExportJobProgress(targets[i].DisplayName, i == currentIndex ? fileProgress : p));
        }

        if (running.Count == 0 && (uint)currentIndex < (uint)total)
        {
            running.Add(new ExportJobProgress(targets[currentIndex].DisplayName, fileProgress));
        }

        var status = total == 1
            ? UiStrings.OverlayVstInsertBakeFile(targets[0].DisplayName)
            : UiStrings.OverlayExportCount(finished, total);
        _busyGlass.SetMessage(UiStrings.OverlayVstInsertBake);
        _busyGlass.SetExportView(overall, status, running);
    }

    /// <summary>
    /// 1 ファイルだけオフライン処理して履歴へ載せる。共有エンジンのため呼び出し側で直列化する。
    /// </summary>
    private async Task<bool> BakeOneSessionAsync(
        DocumentSession session,
        IProgress<double> progress,
        bool includeTail)
    {
        var document = session.Document;
        var restoreOnUndo = new bool[VstInsertChain.MaxSlots];
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            var slot = session.VstInserts.Slots[i];
            restoreOnUndo[i] = !slot.IsEmpty && !slot.Bypassed && !slot.Faulted;
        }

        // 直前ファイルの state を退避してから、このファイルのチェーンへ付け替える。
        _player.VstInserts.CaptureAppliedStates();
        ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: false);
        await Dispatcher.Yield(DispatcherPriority.Background);

        var channels = Math.Max(1, document.Channels);
        var source = includeTail
            ? document.Interleaved
            : (float[])document.Interleaved.Clone();
        var after = await _player.VstInserts
            .ProcessOfflineAsync(source, channels, progress, includeTail)
            .ConfigureAwait(true);
        if (!IsLoaded)
        {
            return false;
        }

        void ApplyBypass()
        {
            for (var i = 0; i < VstInsertChain.MaxSlots; i++)
            {
                if (!session.VstInserts.Slots[i].IsEmpty)
                {
                    session.VstInserts.Slots[i].Bypassed = true;
                }
            }

            SyncVstBypassAfterBake(session);
        }

        void RevertBypass()
        {
            for (var i = 0; i < VstInsertChain.MaxSlots; i++)
            {
                if (restoreOnUndo[i] && !session.VstInserts.Slots[i].IsEmpty)
                {
                    session.VstInserts.Slots[i].Bypassed = false;
                }
            }

            SyncVstBypassAfterBake(session);
        }

        var command = ProcessEdits.BakeVstInserts(document, after, ApplyBypass, RevertBypass);
        if (command is null)
        {
            // 波形変化なしでもバイパスは適用する
            ApplyBypass();
            return true;
        }

        session.History.Do(document, command);
        return true;
    }

    private void SyncVstBypassAfterBake(DocumentSession session)
    {
        // オフライン後のライブをバイパスのまま残すと、タブ閉じの Capture／Dispose で
        // 落ちることがある。破棄ではなく owner へ park し、Undo のバイパス解除で復帰する
        // （再ロードしない）。UnloadBypassedLiveSlots 内で ResetProcessing も行う。
        _player.VstInserts.CaptureAppliedStates();
        ApplyVstInsertsOnUiThread(session, session.VstInserts, playerBypass: IsLibraryMaximized);
        _player.VstInserts.UnloadBypassedLiveSlots();
        if (ReferenceEquals(_activeSession, session))
        {
            VstInsertRack.Refresh();
        }
    }

    /// <summary>
    /// バウンスで付けたバイパスを永続スロットへ書き、開いている全タブへ揃える。
    /// 手動バイパス（SyncPersistedBypass）と同じ「永続＝全タブ共通」に合わせる。
    /// </summary>
    private void SyncBakeBypassIntoPersistedDefaults(IReadOnlyList<DocumentSession> baked)
    {
        var settings = AppStorage.Settings;
        if (!DocumentSessionStore.SnapshotHasAnyPlugin(settings.DefaultVstInserts) || baked.Count == 0)
        {
            return;
        }

        var changed = false;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (!DocumentSessionStore.SnapshotHasPluginAt(settings.DefaultVstInserts, i))
            {
                continue;
            }

            var anyBypassed = false;
            for (var t = 0; t < baked.Count; t++)
            {
                var slot = baked[t].VstInserts.Slots[i];
                if (!slot.IsEmpty && slot.Bypassed)
                {
                    anyBypassed = true;
                    break;
                }
            }

            if (anyBypassed
                && DocumentSessionStore.TrySyncDefaultInsertBypass(settings, i, bypassed: true))
            {
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        AppStorage.Save();
        PropagatePersistedInsertSnapshotsToAllSessions(applyBypass: true);
        if (_activeSession is not null)
        {
            ApplyVstInsertsOnUiThread(_activeSession, _activeSession.VstInserts, playerBypass: IsLibraryMaximized);
            _player.VstInserts.UnloadBypassedLiveSlots();
            VstInsertRack.Refresh();
        }
    }

    private void OnVstInsertOpenEditorRequested(int slotIndex)
    {
        // ロード済みなら Apply を再実行せず UI だけ開く（差し替え直後の再 Apply で失敗しやすい）
        var live = _player.VstInserts.TryGetLive(slotIndex);
        if (live is not null && !live.Faulted)
        {
            OpenVstEditorForSlot(slotIndex);
            return;
        }

        // バイパス deferred（起動時未ロード）なら、エディタ用にここで実体化する。
        if (_activeSession is not null
            && (uint)slotIndex < VstInsertChain.MaxSlots
            && !_activeSession.VstInserts.Slots[slotIndex].IsEmpty)
        {
            _player.VstInserts.EnsureSlotLoaded(slotIndex);
            live = _player.VstInserts.TryGetLive(slotIndex);
            if (live is not null && !live.Faulted)
            {
                OpenVstEditorForSlot(slotIndex);
                return;
            }
        }

        _ = SyncVstInsertEngineAsync(openEditorSlot: slotIndex);
    }

    /// <summary>
    /// Ctrl+Shift+1–8。空き＝選択。使用中＝バイパス解除／GUI 表示／GUI 表示中なら閉じる。
    /// </summary>
    private bool TryToggleVstInsertSlotShortcut(int slotIndex)
    {
        if (_activeSession is null || (uint)slotIndex >= VstInsertChain.MaxSlots)
        {
            return false;
        }

        var slot = _activeSession.VstInserts.Slots[slotIndex];
        if (slot.IsEmpty)
        {
            return VstInsertRack.ActivateSlot(slotIndex);
        }

        if (slot.Bypassed)
        {
            return VstInsertRack.ClearBypassIfSet(slotIndex);
        }

        if (_vstEditors.TryGetValue(slotIndex, out var existing) && existing.IsVisible)
        {
            OrphanVstEditorForReplace(slotIndex);
            return true;
        }

        OnVstInsertOpenEditorRequested(slotIndex);
        return true;
    }

    private void OpenVstEditorForSlot(int slotIndex)
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
                ForegroundActivation.RaiseInTopmostBand(existing);
                return;
            }

            OrphanVstEditorForReplace(slotIndex);
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

        // createView／SetFrame／attach は process の _gate 外なので、開くだけなら
        // サスペンドしない（dry／無音を出さない）。競合 AV は Process 側でスロット fault。
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
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorVstInsertEditorFailed,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Owner 無し（所有関係は子ダイアログとデッドロックしやすい）。前面には Topmost で出す。
        // ホストのフルスクリーン Topmost と帯が競合するので、Show 後と ApplyWindowTopmost 後に RaiseInTopmostBand する。
        // 初回はアプリ中央、再オープンはセッション内の前回位置。見切れは PlaceFloating 側で押す。
        var hasRemembered = _vstEditorPositions.TryGetValue(slotIndex, out var remembered);
        var window = new VstPluginEditorWindow(null, session)
        {
            SlotIndex = slotIndex,
            PlacementOwner = this,
            RememberedTopLeft = hasRemembered ? remembered : null,
            RecenterUntilUserMoves = !hasRemembered,
            Topmost = true,
        };
        window.Closed += (_, _) =>
        {
            RememberVstEditorPosition(slotIndex, window);
            if (_vstEditors.TryGetValue(slotIndex, out var current) && ReferenceEquals(current, window))
            {
                _vstEditors.Remove(slotIndex);
            }

            // エディタで触ったパラメータを永続化し、他タブの同じスロットにも揃える。
            try
            {
                var captured = _player.VstInserts.CaptureAppliedStates();
                if (captured is not null)
                {
                    FlushPersistedInsertParameters(captured);
                }
            }
            catch
            {
            }
        };
        _vstEditors[slotIndex] = window;
        try
        {
            window.Show();
            window.ApplyPlacement();
            window.Activate();
            ForegroundActivation.RaiseInTopmostBand(window);
        }
        catch
        {
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
    /// 開いている VST エディタをホストより手前の Topmost 帯へ押し上げる（フォーカスは奪わない）。
    /// </summary>
    private void RaiseOpenVstEditorsAboveShell()
    {
        if (_vstEditors.Count == 0)
        {
            return;
        }

        foreach (var window in _vstEditors.Values)
        {
            try
            {
                if (!window.IsVisible)
                {
                    continue;
                }

                ForegroundActivation.RaiseInTopmostBand(window);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// ExclusiveLoad 後の差し替え用。removed/DestroyWindow は呼ばず Hide のみ。
    /// Soft Limit 等は DetachPluginNow（removed/DestroyWindow）で固まることがあるので常に Orphan。
    /// </summary>
    private void OrphanVstEditorForReplace(int slotIndex)
    {
        if (!_vstEditors.Remove(slotIndex, out var window))
        {
            return;
        }

        try
        {
            RememberVstEditorPosition(slotIndex, window);
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

    private void CloseAllVstEditors(bool resumeAudioOnClose = true)
    {
        foreach (var key in _vstEditors.Keys.ToArray())
        {
            if (!resumeAudioOnClose)
            {
                OrphanVstEditorForReplace(key);
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
            OrphanVstEditorForReplace(slotIndex);
            return;
        }

        if (_vstEditors.Remove(slotIndex, out var window))
        {
            try
            {
                RememberVstEditorPosition(slotIndex, window);
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
        // アンロード前にパラメータを永続スロットへ保存（次に別波形を開いたとき再現する）。
        _player.VstInserts.CaptureStatesInto(session.VstInserts);
        FlushPersistedInsertParameters(session.VstInserts);
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

    /// <summary>
    /// 永続化済みスロットについて、今刺さっているプラグインのパラメータ（state）を設定へ保存し、
    /// 開いている全タブのチェーンにも同じ内容を載せる。
    /// </summary>
    private void FlushPersistedInsertParameters(VstInsertChain chain)
    {
        var settings = AppStorage.Settings;
        if (!DocumentSessionStore.SnapshotHasAnyPlugin(settings.DefaultVstInserts))
        {
            return;
        }

        var changed = false;
        for (var i = 0; i < VstInsertChain.MaxSlots; i++)
        {
            if (!DocumentSessionStore.SnapshotHasPluginAt(settings.DefaultVstInserts, i))
            {
                continue;
            }

            var slot = chain.Slots[i];
            if (slot.IsEmpty)
            {
                continue;
            }

            var snap = settings.DefaultVstInserts[i];
            if (!string.Equals(snap.PluginPath, slot.PluginPath, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(snap.ClassId ?? string.Empty, slot.ClassId ?? string.Empty, StringComparison.Ordinal))
            {
                continue;
            }

            // パラメータだけ更新。バウンス後の自動バイパスを永続既定へ書かない。
            var captured = DocumentSessionStore.CaptureSlot(slot);
            captured.Bypassed = snap.Bypassed;
            DocumentSessionStore.SetDefaultInsertSlot(settings, i, captured);
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        AppStorage.Save();
        // バイパスは各タブ（バウンス結果）を残し、実体／state だけ揃える。
        PropagatePersistedInsertSnapshotsToAllSessions(applyBypass: false);
    }
}
