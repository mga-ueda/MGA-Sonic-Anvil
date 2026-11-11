using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>コピーした履歴レシピ。タブをまたいで使うためドキュメントには依存しない。</summary>
    private sealed record HistoryRecipeClipboardItem(string Title, Func<AudioDocument, IEditCommand?> Replay);

    private readonly List<HistoryRecipeClipboardItem> _historyRecipeClipboard = [];
    private readonly HashSet<int> _historyCopySelection = [];

    /// <summary>ビッグタイムコード／編集履歴／VST ラックの論理表示。初回 Sync までは null。</summary>
    private bool? _editorChromeShown;

    /// <summary>直前 Sync 時にプレイヤーだったか。復帰時はメーター同様即出し。</summary>
    private bool _editorChromeWasPlayer;

    /// <summary>フェードアウト後の Collapse を世代で無効化する。</summary>
    private int _editorChromeFadeTicket;
    /// <summary>
    /// 直近のコピーが履歴レシピかどうか。true の間はメインウィンドウの
    /// Ctrl+V が（履歴ウィンドウを開かなくても）レシピ適用になる。
    /// 音声のコピー／カットで false に戻る。レシピ自体は消さない。
    /// </summary>
    private bool _historyClipboardIsLatest;
    private IReadOnlyList<EditHistoryEntry> _historyEntries = [];
    private int _historyAnchorIndex;
    private int _historySelectedIndex;
    private int _historyCopyAnchor = -1;

    /// <summary>Shift+↑↓ セッション開始時の選択。範囲の伸縮でここへ戻せるようにする。</summary>
    private HashSet<int>? _historyShiftBase;

    private bool HistoryOpen => HistoryOverlay.IsPanelOpen;

    private void OpenEditHistory()
    {
        if (RejectOutsideFadeToolAction() || _document is null || IsLibraryMaximized)
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        CloseTimeStretchPicker();
        CloseInsertSilencePicker();
        CommitTimelineNudgeSession();
        StopPlaceRepeat();
        StopPlaybackForEdit();
        _historyAnchorIndex = _history.CurrentIndex;
        _historySelectedIndex = _historyAnchorIndex;
        _historyCopySelection.Clear();
        _historyCopyAnchor = -1;
        _historyShiftBase = null;
        RefreshHistoryOverlay();
        HistoryOverlay.Open(this);
    }

    private void CloseEditHistory(bool commit)
    {
        if (!HistoryOpen)
        {
            return;
        }

        if (!commit && _document is not null && _history.JumpTo(_document, _historyAnchorIndex))
        {
            AfterEdit();
        }

        HistoryOverlay.Close();
        RestoreDefaultContentFocusAfterOverlay();
    }

    private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HistoryOpen)
        {
            if (HistoryOverlay.IsMouseOver)
            {
                return;
            }

            // 履歴ストリップは MouseUp でトグルする。ここでは閉じない。
            if (HistoryStrip.IsMouseOver)
            {
                return;
            }

            CloseEditHistory(commit: true);
            e.Handled = true;
            return;
        }

        if (Keyboard.FocusedElement is TextBoxBase editorField
            && (CloseFadeCurvePicker()
                || CloseFormatConvertPicker()
                || CloseVolumeGainPicker()
                || CloseTimeStretchPicker()
                || CloseInsertSilencePicker()))
        {
            editorField.Focusable = false;
            FocusManager.SetFocusedElement(this, Waveform);
            Waveform.Focus();
            Keyboard.Focus(Waveform);
            editorField.Focusable = true;
            e.Handled = true;
            return;
        }

        if (CloseFadeCurvePicker()
            || CloseFormatConvertPicker()
            || CloseVolumeGainPicker()
            || CloseTimeStretchPicker()
            || CloseInsertSilencePicker())
        {
            e.Handled = true;
        }
    }

    private void HistoryOverlay_CloseRequested(object sender, EventArgs e) =>
        CloseEditHistory(commit: true);

    private bool TryProcessHistoryShortcut(Key key, ModifierKeys modifiers)
    {
        if (!HistoryOpen)
        {
            return false;
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (key)
            {
                case Key.A:
                    SelectAllHistoryRecipes();
                    return true;
                case Key.C:
                    CopyHistoryRecipes();
                    return true;
                case Key.V:
                    PasteHistoryRecipes();
                    return true;
                case Key.Q:
                    return false;
            }
        }

        if (modifiers == ModifierKeys.Shift)
        {
            switch (key)
            {
                case Key.Up:
                    ExtendHistoryCopySelection(-1);
                    return true;
                case Key.Down:
                    ExtendHistoryCopySelection(1);
                    return true;
            }
        }

        if (modifiers != ModifierKeys.None)
        {
            return true;
        }

        // Shift+↑↓ の範囲セッションは Shift 以外の操作で確定する。
        _historyShiftBase = null;

        switch (key)
        {
            case Key.Escape:
                CloseEditHistory(commit: false);
                return true;
            case Key.Enter:
            case Key.U:
                CloseEditHistory(commit: true);
                return true;
            case Key.Up:
                MoveHistorySelection(-1);
                return true;
            case Key.Down:
                MoveHistorySelection(1);
                return true;
            case Key.PageUp:
                MoveHistorySelection(-8);
                return true;
            case Key.PageDown:
                MoveHistorySelection(8);
                return true;
            case Key.Home:
                PreviewHistoryIndex(0);
                return true;
            case Key.End:
                PreviewHistoryIndex(_history.TotalCount);
                return true;
            default:
                return true;
        }
    }

    private void MoveHistorySelection(int delta)
    {
        PreviewHistoryIndex(_historySelectedIndex + delta);
    }

    private void PreviewHistoryIndex(int index)
    {
        if (_document is null)
        {
            return;
        }

        var next = Math.Clamp(index, 0, _history.TotalCount);
        if (next == _historySelectedIndex && _history.CurrentIndex == next)
        {
            RefreshHistoryOverlay();
            return;
        }

        _historySelectedIndex = next;
        if (_history.JumpTo(_document, next))
        {
            AfterEdit();
        }

        RefreshHistoryOverlay();
    }

    private void RefreshHistoryOverlay()
    {
        _historyEntries = _history.Snapshot();
        HistoryOverlay.SetItems(_historyEntries, _historySelectedIndex, _historyCopySelection);
        RefreshHistoryStrip();
    }

    private void RefreshHistoryStrip()
    {
        RefreshHistoryStripItems();
        SyncEditorChromeFade();
    }

    private void RefreshHistoryStripItems()
    {
        if (_document is null)
        {
            // フェードアウト中は題を残し、Collapse 完了時に空にする。
            return;
        }

        var items = HistoryOpen ? _historyEntries : _history.Snapshot();
        var current = HistoryOpen ? _historySelectedIndex : _history.CurrentIndex;
        HistoryStrip.SetItems(items, current);
    }

    /// <summary>
    /// ビッグタイムコード／編集履歴／VST ラック。
    /// エディタで波形があるときだけ。出し入れはメーターと同じ 1 秒フェード
    /// （再生中のインとプレイヤー復帰は即）。アウト後に Collapse して列幅を返す。
    /// </summary>
    private void SyncHistoryStripVisibility() => SyncEditorChromeFade();

    private void SyncEditorChromeFade()
    {
        var player = IsLibraryMaximized;
        var show = !player
            && !_videoLaunchPlacement
            && _document is not null
            && TransportChromeHost.Visibility == Visibility.Visible;
        var showTimecode = show && PlayerWaveformSlot.Visibility != Visibility.Visible;
        var showVst = show && IsVstInsertUiEnabled();

        if (!show)
        {
            CloseEditHistory(commit: true);
        }

        var leavingPlayer = !player && _editorChromeWasPlayer;
        _editorChromeWasPlayer = player;

        var first = _editorChromeShown is null;
        var instantReveal = LibraryPlayerMode.InstantPlayerMeterReveal(show, IsPlaybackActive());
        var wantInstant = first
            || leavingPlayer
            || LibraryPlayerMode.InstantPlayerMeterFade(show, instantReveal);

        if (_editorChromeShown == show)
        {
            // フェードアウト中／非表示中に SyncVstInsertEngine などから再入すると、
            // VST を instant Collapse してメーター系だけ残る。触らない。
            if (!show)
            {
                return;
            }

            RefreshHistoryStripItems();
            BindVstInsertRackSession(showVst);

            if (wantInstant && EditorChromeNeedsOpaqueSnap(show, showTimecode, showVst))
            {
                ApplyEditorChromeFade(show, showTimecode, showVst, instant: true);
                return;
            }

            // ドック切替や VST 有効化など、論理表示は同じで子だけ変わる場合。
            ApplyEditorChromeTarget(HistoryStrip, show, instant: false);
            ApplyEditorChromeTarget(EditorPositionTimecodeHost, showTimecode, instant: false);
            ApplyEditorChromeTarget(VstInsertRack, showVst, instant: false);
            return;
        }

        ApplyEditorChromeFade(show, showTimecode, showVst, wantInstant);
        _editorChromeShown = show;
    }

    private bool EditorChromeNeedsOpaqueSnap(bool show, bool showTimecode, bool showVst)
    {
        if (!show)
        {
            return false;
        }

        if (LibraryPlayerMode.NeedsInstantOpaqueSnap(HistoryStrip))
        {
            return true;
        }

        if (showTimecode && LibraryPlayerMode.NeedsInstantOpaqueSnap(EditorPositionTimecodeHost))
        {
            return true;
        }

        return showVst && LibraryPlayerMode.NeedsInstantOpaqueSnap(VstInsertRack);
    }

    private void ApplyEditorChromeFade(bool show, bool showTimecode, bool showVst, bool instant)
    {
        var ticket = ++_editorChromeFadeTicket;
        if (show)
        {
            RefreshHistoryStripItems();
        }

        ApplyEditorChromeTarget(HistoryStrip, show, instant);
        ApplyEditorChromeTarget(EditorPositionTimecodeHost, showTimecode, instant);

        if (IsVstInsertUiEnabled())
        {
            if (showVst)
            {
                BindVstInsertRackSession(true);
            }

            ApplyEditorChromeTarget(VstInsertRack, showVst, instant);
            if (!showVst && instant)
            {
                BindVstInsertRackSession(false);
            }
        }
        else
        {
            ApplyEditorChromeTarget(VstInsertRack, visible: false, instant: true);
            BindVstInsertRackSession(false);
        }

        if (!show)
        {
            if (instant)
            {
                FinishEditorChromeCollapse(ticket);
            }
            else
            {
                ScheduleEditorChromeCollapse(ticket);
            }
        }
    }

    private static void ApplyEditorChromeTarget(UIElement target, bool visible, bool instant)
    {
        if (visible)
        {
            if (target.Visibility != Visibility.Visible)
            {
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Opacity = 0;
                target.Visibility = Visibility.Visible;
            }

            LibraryPlayerMode.FadeElementOpacity(target, 1, instant, hitTestVisible: true);
            return;
        }

        LibraryPlayerMode.FadeElementOpacity(target, 0, instant, hitTestVisible: false);
        if (instant)
        {
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
        }
    }

    private void ScheduleEditorChromeCollapse(int ticket)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.MeterFadeSeconds + 0.05),
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            FinishEditorChromeCollapse(ticket);
        };
        timer.Start();
    }

    private void FinishEditorChromeCollapse(int ticket)
    {
        if (ticket != _editorChromeFadeTicket || _editorChromeShown == true)
        {
            return;
        }

        foreach (var target in EditorChromeFadeTargets())
        {
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = 0;
            target.Visibility = Visibility.Collapsed;
            target.IsHitTestVisible = false;
        }

        HistoryStrip.SetItems([], 0);
        EditorPositionTimecode.Clear();
        BindVstInsertRackSession(false);
    }

    private UIElement[] EditorChromeFadeTargets() =>
        [HistoryStrip, EditorPositionTimecodeHost, VstInsertRack];

    private void HistoryStrip_OpenRequested(object sender, EventArgs e)
    {
        if (HistoryOpen)
        {
            CloseEditHistory(commit: true);
            return;
        }

        OpenEditHistory();
    }

    private void HistoryOverlay_ItemClicked(object sender, HistoryItemClick e)
    {
        if ((e.Modifiers & ModifierKeys.Control) != 0)
        {
            ToggleHistoryCopySelection(e.Index);
        }
        else if ((e.Modifiers & ModifierKeys.Shift) != 0)
        {
            SelectHistoryCopyRange(e.Index);
        }
        else
        {
            PreviewHistoryIndex(e.Index);
        }
    }

    private bool CanReplayHistoryIndex(int index) =>
        index >= 1
        && index < _historyEntries.Count
        && _historyEntries[index].CanReplay;

    private void ToggleHistoryCopySelection(int index)
    {
        if (!CanReplayHistoryIndex(index))
        {
            HistoryOverlay.FlashStatus(UiStrings.EditHistoryNotCopyable);
            return;
        }

        if (!_historyCopySelection.Add(index))
        {
            _historyCopySelection.Remove(index);
        }

        _historyCopyAnchor = index;
        _historyShiftBase = null;
        RefreshHistoryOverlay();
    }

    /// <summary>
    /// Shift+↑↓。押し始めの位置をアンカーにしてカーソルを動かし、
    /// アンカー〜カーソル間を選択に加える（戻れば縮む）。
    /// </summary>
    private void ExtendHistoryCopySelection(int delta)
    {
        if (_historyShiftBase is null)
        {
            _historyShiftBase = [.. _historyCopySelection];
            _historyCopyAnchor = Math.Max(1, _historySelectedIndex);
        }

        var caret = Math.Clamp(_historySelectedIndex + delta, 0, _history.TotalCount);
        _historyCopySelection.Clear();
        _historyCopySelection.UnionWith(_historyShiftBase);
        var start = Math.Min(_historyCopyAnchor, Math.Max(1, caret));
        var end = Math.Max(_historyCopyAnchor, caret);
        for (var i = start; i <= end; i++)
        {
            if (CanReplayHistoryIndex(i))
            {
                _historyCopySelection.Add(i);
            }
        }

        PreviewHistoryIndex(caret);
    }

    private void SelectHistoryCopyRange(int index)
    {
        var anchor = _historyCopyAnchor >= 1 ? _historyCopyAnchor : Math.Max(1, _historySelectedIndex);
        var start = Math.Min(anchor, index);
        var end = Math.Max(anchor, index);
        for (var i = start; i <= end; i++)
        {
            if (CanReplayHistoryIndex(i))
            {
                _historyCopySelection.Add(i);
            }
        }

        _historyShiftBase = null;
        RefreshHistoryOverlay();
    }

    private void SelectAllHistoryRecipes()
    {
        _historyCopySelection.Clear();
        foreach (var entry in _historyEntries)
        {
            if (entry.CanReplay)
            {
                _historyCopySelection.Add(entry.Index);
            }
        }

        RefreshHistoryOverlay();
    }

    private void CopyHistoryRecipes()
    {
        // 選択が無ければ「現在位置までの全操作」をコピーする。
        var indices = _historyCopySelection.Count > 0
            ? _historyCopySelection.Order().ToList()
            : Enumerable.Range(1, Math.Max(0, _historySelectedIndex))
                .Where(CanReplayHistoryIndex)
                .ToList();
        var items = new List<HistoryRecipeClipboardItem>(indices.Count);
        foreach (var index in indices)
        {
            var replay = _history.ReplayAt(index);
            if (replay is not null && index < _historyEntries.Count)
            {
                items.Add(new HistoryRecipeClipboardItem(_historyEntries[index].Title, replay));
            }
        }

        if (items.Count == 0)
        {
            HistoryOverlay.FlashStatus(UiStrings.EditHistoryCopyEmpty);
            return;
        }

        _historyRecipeClipboard.Clear();
        _historyRecipeClipboard.AddRange(items);
        _historyClipboardIsLatest = true;
        HistoryOverlay.FlashStatus(UiStrings.EditHistoryCopied(items.Count));
    }

    /// <summary>履歴ウィンドウ内の Ctrl+V。</summary>
    private void PasteHistoryRecipes()
    {
        if (_document is null)
        {
            return;
        }

        if (_historyRecipeClipboard.Count == 0)
        {
            HistoryOverlay.FlashStatus(UiStrings.EditHistoryPasteEmpty);
            return;
        }

        var applied = ApplyHistoryRecipeClipboardCore();

        // Esc で貼り付けが巻き戻らないよう基準位置を更新する。
        _historyAnchorIndex = _history.CurrentIndex;
        _historySelectedIndex = _history.CurrentIndex;
        _historyCopySelection.Clear();
        RefreshHistoryOverlay();
        HistoryOverlay.FlashStatus(
            UiStrings.EditHistoryPasted(applied, _historyRecipeClipboard.Count));
    }

    /// <summary>
    /// メインウィンドウの Ctrl+V から呼ぶ直接適用。履歴ウィンドウを開かずに
    /// 複数ファイルへ順番に反映できる。適用できるかどうかを返す。
    /// </summary>
    private bool TryPasteHistoryRecipesDirect()
    {
        if (_document is null || !_historyClipboardIsLatest || _historyRecipeClipboard.Count == 0)
        {
            return false;
        }

        if (_activeSession is null)
        {
            return false;
        }

        _ = ApplyHistoryRecipesWithBusyGlassAsync([_activeSession]);
        return true;
    }

    /// <summary>
    /// レシピを順に適用する。貼り付け結果はコピー元の範囲・パラメータで
    /// 決まるため、適用先の選択は先にクリアする。クリップボードは消さない
    /// （次のコピーまで何度でも別ファイルへ貼れる）。
    /// </summary>
    private int ApplyHistoryRecipeClipboardCore()
    {
        if (_activeSession is null)
        {
            return 0;
        }

        var applied = ApplyHistoryRecipesToSession(_activeSession);
        if (applied > 0)
        {
            AfterEdit();
        }

        return applied;
    }

    /// <summary>タブ選択中の Ctrl+V／タブメニューから。選択したタブへレシピを適用する。</summary>
    private void PasteHistoryRecipesToTabs(IReadOnlyList<DocumentSession> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        if (_historyRecipeClipboard.Count == 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.EditHistoryPasteEmpty,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _ = ApplyHistoryRecipesWithBusyGlassAsync(targets);
    }

    /// <summary>
    /// 選択タブ／別ファイルへの履歴貼り付け。重いレシピはバックグラウンドで作り、
    /// すりガラスでタブごとの進捗を出す。
    /// </summary>
    private async Task ApplyHistoryRecipesWithBusyGlassAsync(IReadOnlyList<DocumentSession> targets)
    {
        if (targets.Count == 0 || IsUiBusy)
        {
            return;
        }

        var sessions = targets.ToArray();
        var recipes = _historyRecipeClipboard.ToArray();
        if (recipes.Length == 0)
        {
            return;
        }

        _historyPasteBusy = true;
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

            ShowHistoryPasteBusyGlass();
            ReportHistoryPasteBusy(names, progress, weights);
            var recipeCount = recipes.Length;
            for (var i = 0; i < sessions.Length; i++)
            {
                if (!IsLoaded)
                {
                    return;
                }

                var session = sessions[i];
                progress[i] = Math.Max(progress[i], 0.08);
                ReportHistoryPasteBusy(names, progress, weights);
                for (var r = 0; r < recipes.Length; r++)
                {
                    var item = recipes[r];
                    var document = session.Document;
                    IEditCommand? command;
                    try
                    {
                        command = await Task.Run(() => item.Replay(document)).ConfigureAwait(true);
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
                        session.History.Do(document, command);
                        applied[i]++;
                    }

                    progress[i] = (r + 1) / (double)recipeCount;
                    ReportHistoryPasteBusy(names, progress, weights);
                }

                if (error is not null)
                {
                    break;
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
            _historyPasteBusy = false;
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
                UiStrings.EditHistoryPasteSkippedAll,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ShowEditAppliedToast("History Paste");
    }

    private void ReportHistoryPasteBusy(string[] names, double[] progress, long[] weights) =>
        ApplyHistoryPasteBusySnapshot(HistoryPasteBusyProgress.Capture(names, progress, weights));

    private void ApplyHistoryPasteBusySnapshot(HistoryPasteBusySnapshot snapshot) =>
        _busyGlass.SetExportView(
            snapshot.Overall,
            UiStrings.OverlayExportCount(snapshot.Finished, snapshot.Jobs.Count),
            snapshot.Jobs);

    private void PrepareSessionForHistoryPaste(DocumentSession session)
    {
        session.Document.Selection = WaveSelection.Empty;
        ForEachWaveform(view =>
        {
            if (ReferenceEquals(view.Document, session.Document))
            {
                view.ClearSelection();
            }
        });
    }

    private void RefreshViewsAfterHistoryPaste(IReadOnlyList<DocumentSession> sessions, IReadOnlyList<int> applied)
    {
        var activeApplied = 0;
        for (var i = 0; i < sessions.Count; i++)
        {
            if (ReferenceEquals(sessions[i], _activeSession))
            {
                activeApplied = applied[i];
                break;
            }
        }

        if (activeApplied > 0)
        {
            AfterEdit();
            if (HistoryOpen)
            {
                _historySelectedIndex = _history.CurrentIndex;
                _historyAnchorIndex = _history.CurrentIndex;
                _historyCopySelection.Clear();
                RefreshHistoryOverlay();
            }
        }

        if (_tileMode)
        {
            foreach (var pane in _tilePanes)
            {
                for (var i = 0; i < sessions.Count; i++)
                {
                    if (applied[i] > 0 && ReferenceEquals(pane.Session, sessions[i]))
                    {
                        pane.View.InvalidateSpectrogramCache();
                        pane.View.Refresh();
                        break;
                    }
                }
            }
        }

        RefreshTabHeaders();
    }

    /// <summary>1 セッションへレシピを順に適用する（画面の切替はしない）。</summary>
    private int ApplyHistoryRecipesToSession(DocumentSession session)
    {
        var document = session.Document;

        // Paste レシピが適用先の選択に反応しないよう先にクリアする。
        if (ReferenceEquals(session, _activeSession))
        {
            Waveform.ClearSelection();
        }
        else
        {
            document.Selection = WaveSelection.Empty;
        }

        var applied = 0;
        foreach (var item in _historyRecipeClipboard)
        {
            var command = item.Replay(document);
            if (command is null)
            {
                continue;
            }

            session.History.Do(document, command);
            applied++;
        }

        return applied;
    }
}
