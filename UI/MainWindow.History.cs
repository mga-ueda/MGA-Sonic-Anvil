using System.Windows;
using System.Windows.Input;
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

    private bool HistoryOpen => HistoryOverlay.Visibility == Visibility.Visible;

    private void OpenEditHistory()
    {
        if (_document is null)
        {
            return;
        }

        CloseFadeCurvePicker();
        CloseFormatConvertPicker();
        CloseVolumeGainPicker();
        ClosePitchShiftPicker();
        CloseTimeStretchPicker();
        CommitTimelineNudgeSession();
        StopPlaceRepeat();
        StopPlaybackForEdit();
        _historyAnchorIndex = _history.CurrentIndex;
        _historySelectedIndex = _historyAnchorIndex;
        _historyCopySelection.Clear();
        _historyCopyAnchor = -1;
        _historyShiftBase = null;
        RefreshHistoryOverlay();
        HistoryOverlay.Visibility = Visibility.Visible;
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

        HistoryOverlay.Visibility = Visibility.Collapsed;
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

        if (CloseFadeCurvePicker()
            || CloseFormatConvertPicker()
            || CloseVolumeGainPicker()
            || ClosePitchShiftPicker()
            || CloseTimeStretchPicker())
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
        if (_document is null)
        {
            HistoryStrip.SetItems([], 0);
            return;
        }

        var items = HistoryOpen ? _historyEntries : _history.Snapshot();
        var current = HistoryOpen ? _historySelectedIndex : _history.CurrentIndex;
        HistoryStrip.SetItems(items, current);
    }

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

        StopPlaybackForEdit();
        var applied = ApplyHistoryRecipeClipboardCore();
        if (applied == 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.EditHistoryPasteSkippedAll,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

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

        StopPlaybackForEdit();
        var appliedAny = false;
        var activeApplied = 0;
        foreach (var session in targets)
        {
            var applied = ApplyHistoryRecipesToSession(session);
            appliedAny |= applied > 0;
            if (ReferenceEquals(session, _activeSession))
            {
                activeApplied = applied;
            }
        }

        if (activeApplied > 0)
        {
            AfterEdit();
        }

        // 非アクティブタブのダーティ表示（* とアクセント色）を更新する。
        RefreshTabHeaders();
        if (!appliedAny)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.EditHistoryPasteSkippedAll,
                UiStrings.AppName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
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
