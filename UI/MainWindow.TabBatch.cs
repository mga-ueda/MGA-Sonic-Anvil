using System.Windows;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>選択タブ／プレイリスト一括編集中。IsUiBusy に含める。</summary>
    private bool _tabBatchEditBusy;

    /// <summary>
    /// 一括編集の対象範囲。複数ファイルへのコマンドは選択があっても常にファイル全体。
    /// </summary>
    private static WaveSelection TabBatchRange(AudioDocument document) => new(0, document.FrameCount);

    /// <summary>各ファイル自身のソロ設定を尊重した編集マスク。</summary>
    private static int TabBatchEditMask(DocumentSession session) =>
        ChannelSolo.ClampMask(session.SoloMask, Math.Max(1, session.Document.Channels));

    /// <summary>
    /// プレイヤーはプレイリスト選択（無ければアクティブ）、エディタはタブ選択。
    /// どちらも無いときは空（単体パスへ落とす呼び出し側用）。
    /// </summary>
    private DocumentSession[] ResolveBatchEditTargets()
    {
        if (IsLibraryMaximized)
        {
            return ResolvePlaylistLoudnessMatchTargets();
        }

        if (HasTabSelection)
        {
            return SelectedTabsInOrder();
        }

        return [];
    }

    /// <summary>タブ選択またはプレイヤーで一括対象があるか。</summary>
    private bool HasBatchEditTargets =>
        IsLibraryMaximized
            ? ResolvePlaylistLoudnessMatchTargets().Length > 0
            : HasTabSelection;

    /// <summary>既存 IProgress の区間へ 0..1 を写す。背景スレッドでも SyncContext を汚さない。</summary>
    private static IProgress<double>? ScaleBusyProgress(
        IProgress<double>? inner,
        double start,
        double end) =>
        inner is null ? null : new BusyProgressScale(inner, start, end);

    private sealed class BusyProgressScale(IProgress<double> inner, double start, double end)
        : IProgress<double>
    {
        public void Report(double value)
        {
            var t = Math.Clamp(value, 0, 1);
            inner.Report(start + (end - start) * t);
        }
    }

    /// <summary>
    /// タブ選択があれば、編集コマンドを選択タブ全部に対して実行する。実行に入ったら true。
    /// 履歴レシピ貼り付けと同じすりガラス（タブ別進捗）を重ねて操作を抑制する。
    /// </summary>
    private bool TryRunEditOnSelectedTabs(
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build) =>
        TryRunBatchEdit(overlayMessage, build, requireTabSelection: true);

    /// <summary>
    /// タブ選択またはプレイヤー選択へ一括編集。実行に入ったら true。
    /// </summary>
    private bool TryRunBatchEdit(
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build,
        bool requireTabSelection = false)
    {
        if (requireTabSelection)
        {
            if (!HasTabSelection)
            {
                return false;
            }

            _ = RunBatchEditAsync(
                SelectedTabsInOrder(),
                overlayMessage,
                build,
                saveAfter: false,
                ensurePcm: false,
                warnIfSkipped: true);
            return true;
        }

        if (!HasBatchEditTargets)
        {
            return false;
        }

        var targets = ResolveBatchEditTargets();
        if (targets.Length == 0)
        {
            return false;
        }

        _ = RunBatchEditAsync(
            targets,
            overlayMessage,
            build,
            saveAfter: IsLibraryMaximized,
            ensurePcm: IsLibraryMaximized,
            warnIfSkipped: true);
        return true;
    }

    /// <summary>
    /// 選択タブへの一括編集（後方互換）。コマンド生成はバックグラウンド、適用は UI スレッド。
    /// </summary>
    private Task RunSelectedTabsEditAsync(
        IReadOnlyList<DocumentSession> targets,
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build) =>
        RunBatchEditAsync(targets, overlayMessage, build, saveAfter: false, ensurePcm: false, warnIfSkipped: true);

    /// <summary>
    /// 一括編集。PCM 確保（任意）→ コマンド生成を ExportParallelism で並列 → 履歴適用は UI 直列 → 任意で保存。
    /// build が null を返した対象はスキップする。
    /// </summary>
    private async Task RunBatchEditAsync(
        IReadOnlyList<DocumentSession> targets,
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build,
        bool saveAfter,
        bool ensurePcm,
        bool warnIfSkipped = true)
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
        var commands = new IEditCommand?[sessions.Length];
        var ready = new bool[sessions.Length];
        Exception? error = null;
        var errorLock = new object();
        try
        {
            StopPlaybackForEdit();
            // 選択クリアの Prepare はコマンド生成のあと（適用直前）に回す。

            ShowBusyGlass(overlayMessage);
            ReportHistoryPasteBusy(names, progress, weights);

            if (ensurePcm)
            {
                for (var i = 0; i < sessions.Length; i++)
                {
                    if (!IsLoaded)
                    {
                        return;
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
                }
            }
            else
            {
                Array.Fill(ready, true);
            }

            if (!IsLoaded)
            {
                return;
            }

            var buildIndices = new List<int>(sessions.Length);
            for (var i = 0; i < sessions.Length; i++)
            {
                if (ready[i])
                {
                    buildIndices.Add(i);
                }
            }

            if (buildIndices.Count > 0)
            {
                var parallelism = AudioExport.WorkerCount(
                    buildIndices.Count,
                    AppStorage.Settings.ExportParallelism);
                // System.Progress の SyncContext 経由だと UI Post → 再 BeginInvoke になり、
                // 計測ループ中の％更新が一度しか見えないことがある。ワーカー直書き＋スナップショット送る。
                var busyProgress = new BatchEditBusyProgress(
                    names,
                    progress,
                    weights,
                    Dispatcher,
                    ApplyHistoryPasteBusySnapshot);
                var perTabProgress = new IProgress<double>?[sessions.Length];
                foreach (var i in buildIndices)
                {
                    perTabProgress[i] = busyProgress.ForTab(i);
                }

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
                                commands[i] = build(session, perTabProgress[i]);
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

            if (!IsLoaded)
            {
                return;
            }

            Exception? buildError;
            lock (errorLock)
            {
                buildError = error;
            }

            if (buildError is not null)
            {
                error = buildError;
                for (var i = 0; i < sessions.Length; i++)
                {
                    progress[i] = 1;
                }

                ReportHistoryPasteBusy(names, progress, weights);
            }
            else
            {
                for (var i = 0; i < sessions.Length; i++)
                {
                    if (!ready[i])
                    {
                        continue;
                    }

                    if (commands[i] is { } command)
                    {
                        PrepareSessionForHistoryPaste(sessions[i]);
                        sessions[i].History.Do(sessions[i].Document, command);
                        if (IsLibraryMaximized)
                        {
                            LibraryBrowser.UpdateSessionRow(sessions[i]);
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
            if (warnIfSkipped)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.TabBatchEditSkippedAll,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        if (!willSave)
        {
            return;
        }

        try
        {
            _tabBatchEditBusy = false;
            RefreshExportEnabled();
            await SaveSessionsInPlaceQuietAsync(saveTargets).ConfigureAwait(true);
        }
        finally
        {
            RefreshExportEnabled();
        }
    }
}
