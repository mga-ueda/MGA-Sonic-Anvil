using System.Windows;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>選択タブへの一括編集中。IsUiBusy に含める。</summary>
    private bool _tabBatchEditBusy;

    /// <summary>一括編集の対象範囲。ファイル単位の操作なので常に全体。</summary>
    private static WaveSelection TabBatchRange(AudioDocument document) => new(0, document.FrameCount);

    /// <summary>各ファイル自身のソロ設定を尊重した編集マスク。</summary>
    private static int TabBatchEditMask(DocumentSession session) =>
        ChannelSolo.ClampMask(session.SoloMask, Math.Max(1, session.Document.Channels));

    /// <summary>
    /// タブ選択があれば、編集コマンドを選択タブ全部に対して実行する。実行に入ったら true。
    /// 履歴レシピ貼り付けと同じすりガラス（タブ別進捗）を重ねて操作を抑制する。
    /// タイル検索フィルターのすりガラスはそのまま維持され、上に重なる。
    /// </summary>
    private bool TryRunEditOnSelectedTabs(
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build)
    {
        if (!HasTabSelection)
        {
            return false;
        }

        _ = RunSelectedTabsEditAsync(SelectedTabsInOrder(), overlayMessage, build);
        return true;
    }

    /// <summary>
    /// 選択タブへの一括編集。コマンド生成はバックグラウンド、適用（履歴登録）は UI スレッド。
    /// build が null を返したタブはスキップする（適用できないものは飛ばす）。
    /// </summary>
    private async Task RunSelectedTabsEditAsync(
        IReadOnlyList<DocumentSession> targets,
        string overlayMessage,
        Func<DocumentSession, IProgress<double>?, IEditCommand?> build)
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
        Exception? error = null;
        try
        {
            StopPlaybackForEdit();
            foreach (var session in sessions)
            {
                PrepareSessionForHistoryPaste(session);
            }

            ShowBusyGlass(overlayMessage);
            ReportHistoryPasteBusy(names, progress, weights);
            for (var i = 0; i < sessions.Length; i++)
            {
                if (!IsLoaded)
                {
                    return;
                }

                var session = sessions[i];
                var index = i;
                progress[i] = Math.Max(progress[i], 0.02);
                ReportHistoryPasteBusy(names, progress, weights);
                var perTab = new Progress<double>(p =>
                {
                    progress[index] = Math.Clamp(p, 0d, 1d);
                    ReportHistoryPasteBusy(names, progress, weights);
                });
                IEditCommand? command;
                try
                {
                    command = await Task.Run(() => build(session, perTab)).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    error = ex;
                    progress[i] = 1;
                    ReportHistoryPasteBusy(names, progress, weights);
                    break;
                }

                if (!IsLoaded)
                {
                    return;
                }

                if (command is not null)
                {
                    session.History.Do(session.Document, command);
                    applied[i]++;
                }

                progress[i] = 1;
                ReportHistoryPasteBusy(names, progress, weights);
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }
        finally
        {
            _tabBatchEditBusy = false;
            RefreshExportEnabled();
            if (error is not null)
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
        }
    }
}
