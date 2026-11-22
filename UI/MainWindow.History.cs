using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
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
    /// ビッグタイムコード／ヘッダチャンク／編集履歴／VST ラック。
    /// エディタで波形があるときだけ。出すときは即（停止中も。信号待ちしない）。
    /// 消すときだけメーターと同じ 1 秒フェード。アウト後に Collapse して列幅を返す。
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
        var showChunks = show && AppStorage.Settings.EditorShowHeaderStructure;

        if (!show)
        {
            CloseEditHistory(commit: true);
        }

        var enteringPlayer = player && !_editorChromeWasPlayer;
        var leavingPlayer = !player && _editorChromeWasPlayer;
        _editorChromeWasPlayer = player;

        // 表示は常に即。エディタ↔プレイヤー切替の非表示も即（全部タブ閉じの 1 秒フェードと分ける）。
        // 同モード内で文書が消えたときだけフェードアウト。
        var wantInstant = show
            || enteringPlayer
            || leavingPlayer
            || _editorChromeShown is null;

        if (_editorChromeShown == show)
        {
            // フェードアウト中／非表示中に SyncVstInsertEngine などから再入すると、
            // VST を instant Collapse してメーター系だけ残る。触らない。
            if (!show)
            {
                return;
            }

            RefreshHistoryStripItems();
            RefreshEditorHeaderChunks(showChunks);
            BindVstInsertRackSession(showVst);

            if (EditorChromeNeedsOpaqueSnap(show, showTimecode, showVst, showChunks))
            {
                ApplyEditorChromeFade(show, showTimecode, showVst, showChunks, instant: true);
                RefreshEditorPositionTimecodeText(showTimecode);
                return;
            }

            // ドック切替や VST 有効化など、子の出し分けだけ。既に不透明なら触らない。
            // チャンク帯をオフにするときは即 Collapse し、タイムコード側に幅を返す。
            ApplyEditorChromeTarget(HistoryStrip, show, instant: true);
            ApplyEditorChromeTarget(HeaderChunkStrip, showChunks, instant: true);
            ApplyEditorChromeTarget(EditorPositionTimecodeHost, showTimecode, instant: true);
            SyncEditorPositionSlotMinWidth(showTimecode);
            ApplyEditorChromeTarget(VstInsertRack, showVst, instant: true);
            // H 再表示は帯を Visible にしたあとで詳細配置。Collapse 中の Sync だと詳細が消える。
            if (showChunks)
            {
                SyncEditorHeaderDetailPlacement();
            }

            RefreshEditorPositionTimecodeText(showTimecode);
            return;
        }

        ApplyEditorChromeFade(show, showTimecode, showVst, showChunks, wantInstant);
        _editorChromeShown = show;
        RefreshEditorPositionTimecodeText(showTimecode);
    }

    /// <summary>
    /// Collapse で Clear したあと、停止中の F12 最大化などでもステータスと同じ現在位置（0 秒含む）を載せる。
    /// </summary>
    private void RefreshEditorPositionTimecodeText(bool showTimecode)
    {
        if (!showTimecode)
        {
            return;
        }

        SyncTransportPosition();
    }

    private bool EditorChromeNeedsOpaqueSnap(bool show, bool showTimecode, bool showVst, bool showChunks)
    {
        if (!show)
        {
            return false;
        }

        if (LibraryPlayerMode.NeedsInstantOpaqueSnap(HistoryStrip))
        {
            return true;
        }

        if (showChunks && LibraryPlayerMode.NeedsInstantOpaqueSnap(HeaderChunkStrip))
        {
            return true;
        }

        if (showTimecode && LibraryPlayerMode.NeedsInstantOpaqueSnap(EditorPositionTimecodeHost))
        {
            return true;
        }

        return showVst && LibraryPlayerMode.NeedsInstantOpaqueSnap(VstInsertRack);
    }

    private void ApplyEditorChromeFade(
        bool show,
        bool showTimecode,
        bool showVst,
        bool showChunks,
        bool instant)
    {
        var ticket = ++_editorChromeFadeTicket;
        if (show)
        {
            RefreshHistoryStripItems();
            RefreshEditorHeaderChunks(showChunks);
        }

        ApplyEditorChromeTarget(HistoryStrip, show, instant);
        // オフ時は即 Collapse（フェード待ちだと Auto 列が幅を食い、タイムコードが広がらない）。
        ApplyEditorChromeTarget(HeaderChunkStrip, showChunks, instant: instant || !showChunks);
        ApplyEditorChromeTarget(EditorPositionTimecodeHost, showTimecode, instant);
        SyncEditorPositionSlotMinWidth(showTimecode);

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

        // 帯を出してから詳細配置（Refresh 時点ではまだ Collapsed のことがある）。
        if (show && showChunks)
        {
            SyncEditorHeaderDetailPlacement();
        }

        // サイド情報も同じフェードで消す（全タブ閉じなど）。出しは SyncEditorHeaderDetailPlacement。
        if (!show && EditorHeaderDetailSideHost.Visibility == Visibility.Visible)
        {
            ApplyEditorChromeTarget(EditorHeaderDetailSideHost, visible: false, instant);
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
        HeaderChunkStrip.Clear();
        EditorPositionTimecode.Clear();
        BindVstInsertRackSession(false);
        HideEditorHeaderDetailSide(forgetHeld: true);
    }

    private UIElement[] EditorChromeFadeTargets() =>
        [HistoryStrip, HeaderChunkStrip, EditorPositionTimecodeHost, VstInsertRack, EditorHeaderDetailSideHost];

    /// <summary>
    /// エディタヘッダのチャンク帯。オフ時は解析しない（プレイヤーのヘッダOFFと同じ）。
    /// H オフでは幅高水位・選択も捨て、再表示は1行目＋内容フィットから。
    /// 複数タブ選択時はチャンク和集合（プレイリストのヘッダ情報と同じ）。
    /// </summary>
    private void RefreshEditorHeaderChunks(bool show)
    {
        EnsureEditorHeaderRepairWired();
        if (!show)
        {
            // 空でも高水位だけ残っていることがあるので、常に忘れる。
            HeaderChunkStrip.Clear();
            HideEditorHeaderDetailSide(forgetHeld: true);
            return;
        }

        if (_suppressEditorHeaderChunkRefresh)
        {
            RefreshEditorHeaderRepairTargets();
            return;
        }

        // Clear 後の再読込は Populate が1行目を選ぶ。配置 Sync は帯 Visible 後。
        // 未保存のヘッダ作業コピーがあればそちらを表示する。
        // 複数タブ選択時は和集合。選択なしはアクティブ1本。
        var paths = CollectEditorHeaderRepairCandidatePaths();
        if (paths.Count > 1)
        {
            HeaderChunkStrip.LoadUnionFromPaths(paths);
        }
        else if (paths.Count == 1)
        {
            HeaderChunkStrip.LoadFromPath(paths[0]);
        }
        else
        {
            HeaderChunkStrip.Clear();
        }

        RefreshEditorHeaderRepairTargets();
    }

    /// <summary>タブ選択変更からチャンク帯と修復ボタンを揃える。</summary>
    private void RefreshEditorHeaderStructureFromTabSelection()
    {
        if (_tileMode || IsLibraryMaximized)
        {
            return;
        }

        RefreshEditorHeaderChunks(AppStorage.Settings.EditorShowHeaderStructure);
        SyncEditorHeaderDetailPlacement();
    }

    private bool _editorHeaderRepairWired;
    private int _editorHeaderRepairTicket;
    /// <summary>和集合チャンク選択でタブを絞るとき、ツリーを作り直さない。</summary>
    private bool _suppressEditorHeaderChunkRefresh;

    private void EnsureEditorHeaderRepairWired()
    {
        if (_editorHeaderRepairWired)
        {
            return;
        }

        _editorHeaderRepairWired = true;
        HeaderChunkStrip.RepairMessage += ShowCenterStatusToast;
        HeaderChunkStrip.ApplyRepairEditAsync = ApplyEditorHeaderRepairEditAsync;
        HeaderChunkStrip.ApplyChunkDeleteEditAsync = ApplyEditorChunkDeleteEditAsync;
        HeaderChunkStrip.ResolveChannelLayoutPaths = CollectEditorHeaderRepairCandidatePaths;
        HeaderChunkStrip.ApplyChannelLayoutEditAsync = ApplyEditorChannelLayoutEditAsync;
        HeaderChunkStrip.ApplyUnionChunkTabSelection = SelectTabsMatchingUnionChunks;
    }

    /// <summary>
    /// 和集合のチャンク行（複数可）を選んだとき、いずれかのチャンクを持つタブを選択する。
    /// 和集合ツリー自体は作り直さない。フォーカスはヘッダ側に残す。
    /// </summary>
    private void SelectTabsMatchingUnionChunks(IReadOnlyList<WaveStructureNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return;
        }

        var pathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            foreach (var member in node.MemberPaths)
            {
                if (PathUtil.TryGetFullPathTrimmed(member, out var full))
                {
                    pathSet.Add(full);
                }
                else if (!string.IsNullOrWhiteSpace(member))
                {
                    pathSet.Add(member);
                }
            }
        }

        if (pathSet.Count == 0)
        {
            return;
        }

        var selected = new List<DocumentSession>();
        DocumentSession? caret = null;
        foreach (var session in _sessions)
        {
            var path = session.Document.ResolveWaveContainerPath();
            if (string.IsNullOrWhiteSpace(path))
            {
                path = session.Document.SourcePath;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var matched = PathUtil.TryGetFullPathTrimmed(path, out var full)
                ? pathSet.Contains(full)
                : pathSet.Contains(path);
            if (!matched
                && !string.IsNullOrWhiteSpace(session.Document.SourcePath)
                && PathUtil.TryGetFullPathTrimmed(session.Document.SourcePath, out var sourceFull)
                && pathSet.Contains(sourceFull))
            {
                matched = true;
            }

            if (!matched)
            {
                continue;
            }

            selected.Add(session);
            caret ??= session;
        }

        if (selected.Count == 0 || caret is null)
        {
            return;
        }

        var keepTreeItem = HeaderChunkStrip.Tree.SelectedItem as TreeViewItem;
        _suppressEditorHeaderChunkRefresh = true;
        try
        {
            _selectedTabs.Clear();
            foreach (var session in selected)
            {
                _selectedTabs.Add(session);
            }

            _tabSelectionAnchor = caret;
            RefreshTabHeaders();
            RefreshTileChrome();
            SyncPlaylistSelectionLoudness();
            RefreshTransportCommandsEnabled();
            RefreshStatus();
            // 修復ボタンだけは絞ったタブに合わせる（和集合ツリーは維持）。
            RefreshEditorHeaderRepairTargets();
        }
        finally
        {
            _suppressEditorHeaderChunkRefresh = false;
        }

        if (keepTreeItem is not null)
        {
            keepTreeItem.Focus();
        }
    }

    /// <summary>エディタ: ヘッダ修復を作業コピーへ。SourcePath は即時上書きしない。</summary>
    private async Task<bool> ApplyEditorHeaderRepairEditAsync(IReadOnlyList<string> paths)
    {
        var changed = 0;
        string? firstError = null;
        foreach (var path in paths)
        {
            var session = FindSessionBySourceOrContainerPath(path);
            if (session is null)
            {
                continue;
            }

            var document = session.Document;
            var source = document.ResolveWaveContainerPath();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                firstError ??= UiStrings.WaveStructureFileMissing;
                continue;
            }

            var dest = AppStorage.SessionFilePath("hdr-" + Guid.NewGuid().ToString("N") + ".wav");
            var result = await Task.Run(() => WaveHeaderRepair.RepairTo(source, dest))
                .ConfigureAwait(true);
            if (!result.Success)
            {
                firstError ??= result.Error;
                TryDeleteFile(dest);
                continue;
            }

            if (!result.Changed)
            {
                TryDeleteFile(dest);
                continue;
            }

            var before = document.WaveContainerPath;
            var command = ProcessEdits.ReplaceWaveContainer(
                document,
                before,
                dest,
                "Repair Header");
            if (command is null)
            {
                TryDeleteFile(dest);
                continue;
            }

            session.History.Do(document, command);
            AudioCodec.ReloadEmbeddedMeta(document);
            changed++;
        }

        if (changed == 0)
        {
            ShowCenterStatusToast(firstError ?? UiStrings.WaveHeaderRepairNoChange);
            return false;
        }

        AfterEdit();
        // プレイヤー復帰時にヘッダ／プレイリスト行を作業コピーへ合わせる。
        MarkLibraryPlaylistStaleFromEditor();
        HeaderChunkStrip.InvalidateLoadedCache();
        RefreshEditorHeaderChunks(show: AppStorage.Settings.EditorShowHeaderStructure && !_tileMode);
        SyncEditorHeaderDetailPlacement();
        ShowCenterStatusToast(UiStrings.WaveHeaderRepairEdited);
        return true;
    }

    /// <summary>エディタ: チャンネルレイアウトを作業コピーへ。SourcePath は即時上書きしない。</summary>
    private async Task<bool> ApplyEditorChannelLayoutEditAsync(IReadOnlyList<string> paths, int channelMask)
    {
        var changed = 0;
        string? firstError = null;
        foreach (var path in paths)
        {
            var session = FindSessionBySourceOrContainerPath(path);
            if (session is null)
            {
                continue;
            }

            var document = session.Document;
            var source = document.ResolveWaveContainerPath();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                firstError ??= UiStrings.WaveStructureFileMissing;
                continue;
            }

            var ext = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(ext))
            {
                ext = ".wav";
            }

            var dest = AppStorage.SessionFilePath("chlayout-" + Guid.NewGuid().ToString("N") + ext);
            var result = await Task.Run(() => WaveChannelLayoutWriter.ApplyTo(source, dest, channelMask))
                .ConfigureAwait(true);
            if (!result.Success)
            {
                firstError ??= result.Error;
                TryDeleteFile(dest);
                continue;
            }

            if (!result.Changed)
            {
                TryDeleteFile(dest);
                continue;
            }

            var before = document.WaveContainerPath;
            var command = ProcessEdits.ReplaceWaveContainer(
                document,
                before,
                dest,
                "Channel Layout");
            if (command is null)
            {
                TryDeleteFile(dest);
                continue;
            }

            session.History.Do(document, command);
            document.SetChannelMask(channelMask);
            AudioCodec.ReloadEmbeddedMeta(document);
            changed++;
        }

        if (changed == 0)
        {
            ShowCenterStatusToast(firstError ?? UiStrings.WaveChannelLayoutNoChange);
            return false;
        }

        AfterEdit();
        MarkLibraryPlaylistStaleFromEditor();
        HeaderChunkStrip.InvalidateLoadedCache();
        RefreshEditorHeaderChunks(show: AppStorage.Settings.EditorShowHeaderStructure && !_tileMode);
        SyncEditorHeaderDetailPlacement();
        ShowCenterStatusToast(UiStrings.WaveChannelLayoutEdited);
        return true;
    }

    /// <summary>エディタ: チャンク削除を作業コピーへ。SourcePath は即時上書きしない。</summary>
    private async Task<bool> ApplyEditorChunkDeleteEditAsync(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> pathToChunkIds,
        int chunkKinds)
    {
        var changed = 0;
        string? firstError = null;
        foreach (var (path, ids) in pathToChunkIds)
        {
            var session = FindSessionBySourceOrContainerPath(path);
            if (session is null)
            {
                continue;
            }

            var document = session.Document;
            var source = document.ResolveWaveContainerPath();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                firstError ??= UiStrings.WaveStructureFileMissing;
                continue;
            }

            var removable = ids
                .Where(WaveChunkCleanPolicy.CanDelete)
                .ToHashSet(StringComparer.Ordinal);
            if (removable.Count == 0)
            {
                continue;
            }

            var dest = AppStorage.SessionFilePath("chk-" + Guid.NewGuid().ToString("N") + ".wav");
            var result = await Task.Run(() => WaveChunkStripper.StripTo(source, dest, removable))
                .ConfigureAwait(true);
            if (!result.Success)
            {
                firstError ??= result.Error;
                TryDeleteFile(dest);
                continue;
            }

            if (!result.Changed)
            {
                TryDeleteFile(dest);
                continue;
            }

            var before = document.WaveContainerPath;
            var command = ProcessEdits.ReplaceWaveContainer(
                document,
                before,
                dest,
                "Delete Chunks");
            if (command is null)
            {
                TryDeleteFile(dest);
                continue;
            }

            session.History.Do(document, command);
            AudioCodec.ReloadEmbeddedMeta(document);
            changed++;
        }

        if (changed == 0)
        {
            ShowCenterStatusToast(firstError ?? UiStrings.WaveChunkStripNoChange);
            return false;
        }

        AfterEdit();
        // プレイヤー復帰時にヘッダ／プレイリスト行を作業コピーへ合わせる。
        MarkLibraryPlaylistStaleFromEditor();
        HeaderChunkStrip.InvalidateLoadedCache();
        RefreshEditorHeaderChunks(show: AppStorage.Settings.EditorShowHeaderStructure && !_tileMode);
        SyncEditorHeaderDetailPlacement();
        ShowCenterStatusToast(UiStrings.WaveChunkStripEdited(Math.Max(1, chunkKinds)));
        return true;
    }

    private DocumentSession? FindSessionBySourceOrContainerPath(string path)
    {
        if (FindSessionByPath(path) is { } bySource)
        {
            return bySource;
        }

        foreach (var session in _sessions)
        {
            var container = session.Document.WaveContainerPath;
            if (!string.IsNullOrWhiteSpace(container)
                && string.Equals(container, path, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }

            var resolved = session.Document.ResolveWaveContainerPath();
            if (!string.IsNullOrWhiteSpace(resolved)
                && string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }
        }

        return null;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>
    /// 非タイル時のみ。選択タブ（なければアクティブ）から修復対象を集めて冒頭ボタンへ渡す。
    /// </summary>
    private void RefreshEditorHeaderRepairTargets()
    {
        EnsureEditorHeaderRepairWired();
        if (_tileMode
            || IsLibraryMaximized
            || !AppStorage.Settings.EditorShowHeaderStructure)
        {
            HeaderChunkStrip.SetRepairTargets(Array.Empty<string>());
            return;
        }

        var candidates = CollectEditorHeaderRepairCandidatePaths();
        if (candidates.Count == 0)
        {
            HeaderChunkStrip.SetRepairTargets(Array.Empty<string>());
            return;
        }

        if (candidates.Count == 1)
        {
            var only = candidates[0];
            var targets = WaveHeaderRepair.NeedsRepair(WaveContainerStructure.TryRead(only))
                ? new[] { only }
                : Array.Empty<string>();
            HeaderChunkStrip.SetRepairTargets(targets);
            return;
        }

        var ticket = ++_editorHeaderRepairTicket;
        _ = Task.Run(() => WaveHeaderRepair.CollectRepairTargets(candidates))
            .ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (ticket != _editorHeaderRepairTicket
                        || _tileMode
                        || IsLibraryMaximized
                        || !AppStorage.Settings.EditorShowHeaderStructure)
                    {
                        return;
                    }

                    if (task.IsFaulted || task.IsCanceled)
                    {
                        return;
                    }

                    HeaderChunkStrip.SetRepairTargets(task.Result);
                    SyncEditorHeaderDetailPlacement();
                });
            });
    }

    private IReadOnlyList<string> CollectEditorHeaderRepairCandidatePaths()
    {
        var sessions = HasTabSelection
            ? SelectedTabsInOrder()
            : _activeSession is null
                ? Array.Empty<DocumentSession>()
                : new[] { _activeSession };
        if (sessions.Length == 0)
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>(sessions.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            // 未保存の作業コピーがあればそちらを修復対象にする。
            var path = session.Document.ResolveWaveContainerPath();
            if (string.IsNullOrWhiteSpace(path) || !seen.Add(path))
            {
                continue;
            }

            paths.Add(path);
        }

        return paths;
    }

    private void HeaderChunkStrip_DetailChanged(object sender, EventArgs e) =>
        SyncEditorHeaderDetailPlacement();

    private void TransportChromeHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SyncEditorHeaderDetailPlacement();

    /// <summary>
    /// ビッグタイムコード用の左枠 MinWidth（控えめな下限のみ）。
    /// F12 全画面で右 Auto 列が LayoutTransform 拡大しても Star が 0 にならないようにする。
    /// 高さ基準で必要幅を取りに行くと枠が膨らみすぎてヘッダ帯と重なるのでやらない。
    /// </summary>
    private void SyncEditorPositionSlotMinWidth(bool showTimecode) =>
        EditorPositionSlot.MinWidth = showTimecode
            ? DesignMetrics.EditorPositionTimecodeMinSlotWidth
            : 0;

    /// <summary>サイド情報列の幅高水位。H トグル／エディタ退出で忘れる。</summary>
    private double _editorHeaderDetailSideHeldWidth;
    /// <summary>サイド情報のスクロールバー／見出し／フォーカス帯を一度だけ付ける。</summary>
    private bool _editorHeaderDetailSideScrollStyled;
    /// <summary>チャンク切替などのプログラムスクロールではバーを出さない。</summary>
    private bool _editorHeaderDetailSideScrollRevealSuppress;
    /// <summary>プレイヤー情報列と同じ上端フォーカスグラデ。</summary>
    private Border? _editorHeaderDetailSideFocusLine;

    /// <summary>
    /// ヘッダ情報の詳細。帯右に載る余裕があればインライン、無ければ波形とレベルメーターのあいだ。
    /// 全画面（F11／F12 のレイアウト全画面オン）は常にサイド列。
    /// </summary>
    private void SyncEditorHeaderDetailPlacement()
    {
        // H 再表示は Refresh（Collapsed）→ Visible の順。Collapse 中に DetailChanged で
        // 詳細を消すと、Visible 後まで空のままになるので触らない。
        if (HeaderChunkStrip.Visibility == Visibility.Collapsed)
        {
            return;
        }

        var showChunks = !IsLibraryMaximized
            && AppStorage.Settings.EditorShowHeaderStructure;
        var detail = showChunks ? HeaderChunkStrip.DetailText : string.Empty;
        if (!showChunks || string.IsNullOrEmpty(detail))
        {
            HeaderChunkStrip.SetInlineDetailVisible(false);
            HideEditorHeaderDetailSide(forgetHeld: !showChunks);
            return;
        }

        // iXML 等は色分けツリーが必要。帯内平文ではなくサイドへ。
        var preferSide = HeaderChunkStrip.SelectedNode is
        {
            Kind: WaveStructureContentKind.Xml,
            IsInvalid: false,
        };
        var canInline = !preferSide
            && !IsWaveformMaximized
            && CanFitEditorHeaderDetailInline();
        HeaderChunkStrip.SetInlineDetailVisible(canInline);
        if (canInline)
        {
            HideEditorHeaderDetailSide(forgetHeld: false);
        }
        else
        {
            ShowEditorHeaderDetailSide(HeaderChunkStrip.SelectedNode, detail);
        }
    }

    private bool CanFitEditorHeaderDetailInline()
    {
        var hostW = TransportChromeHost.ActualWidth;
        if (hostW <= 1)
        {
            return false;
        }

        var treeW = HeaderChunkStrip.TreeFittedWidth;
        var detailW = HeaderChunkStrip.DetailFittedWidth;
        var headerMargin = HeaderChunkStrip.Margin.Left + HeaderChunkStrip.Margin.Right;
        var leftIfInline = hostW
            - EditorChromeRightOccupiedWidth()
            - treeW
            - EditorHeaderChunkStripView.PaneGap
            - detailW
            - headerMargin;
        return leftIfInline >= DesignMetrics.EditorPositionSlotMinWidthForHeaderDetail;
    }

    private double EditorChromeRightOccupiedWidth()
    {
        static double WidthOf(FrameworkElement el) =>
            el.Visibility == Visibility.Visible
                ? el.ActualWidth + el.Margin.Left + el.Margin.Right
                : 0;

        return WidthOf(HistoryStrip)
            + WidthOf(VstInsertRack)
            + WidthOf(LoudnessMeter)
            + WidthOf(MonitorToggles)
            + WidthOf(Spectrum);
    }

    private void ShowEditorHeaderDetailSide(WaveStructureNode? node, string text)
    {
        EnsureEditorHeaderDetailSideScrollReveal();
        ApplyEditorHeaderDetailSideContent(node, text);
        var need = Math.Max(
            DesignMetrics.EditorHeaderDetailMinWidth,
            HeaderChunkStrip.DetailFittedWidth);
        _editorHeaderDetailSideHeldWidth = Math.Max(_editorHeaderDetailSideHeldWidth, need);
        EditorHeaderDetailSideHost.MinWidth = DesignMetrics.EditorHeaderDetailMinWidth;
        EditorHeaderDetailSideHost.Width = _editorHeaderDetailSideHeldWidth;

        var revealing = EditorHeaderDetailSideHost.Visibility != Visibility.Visible
            || EditorHeaderDetailSideHost.Opacity < 0.01;
        if (revealing)
        {
            // 位置を決めてから見せる（上→中央のジャンプを見せない）。
            EditorHeaderDetailSideHost.BeginAnimation(UIElement.OpacityProperty, null);
            EditorHeaderDetailSideHost.Opacity = 0;
            EditorHeaderDetailSideHost.Visibility = Visibility.Visible;
            EditorHeaderDetailSideHost.IsHitTestVisible = false;
            EditorHeaderDetailSideHost.UpdateLayout();
            SyncEditorHeaderDetailSideVerticalAlign();
            LibraryPlayerMode.FadeElementOpacity(
                EditorHeaderDetailSideHost,
                1,
                instant: false,
                hitTestVisible: true);
            return;
        }

        SyncEditorHeaderDetailSideVerticalAlign();
    }

    private void ApplyEditorHeaderDetailSideContent(WaveStructureNode? node, string text)
    {
        if (node is { Kind: WaveStructureContentKind.Xml, IsInvalid: false })
        {
            var meta = WaveStructureNode.FormatKeyValueTable(
            [
                ("Offset", string.Create(CultureInfo.InvariantCulture, $"0x{node.Offset:X} ({node.Offset})")),
                ("Size", WaveStructureNode.FormatSize(node.Size)),
            ]);
            WaveStructureXmlTree.EnsureTreeChrome(EditorHeaderDetailSideXml);
            // EnsureTreeChrome 済みでもプレイヤー情報列と同じガターを維持。
            EditorHeaderDetailSideXml.Padding = new Thickness(4, 4, 4, 6);
            // 外側 ScrollViewer に任せる（内側バーだと塊の高さが足りず見切れる）。
            ScrollViewer.SetVerticalScrollBarVisibility(
                EditorHeaderDetailSideXml,
                ScrollBarVisibility.Disabled);
            if (WaveStructureXmlTree.TryPopulate(EditorHeaderDetailSideXml, node.Content))
            {
                EditorHeaderDetailSideMeta.Text = meta;
                EditorHeaderDetailSideMeta.Visibility = Visibility.Visible;
                EditorHeaderDetailSide.Text = string.Empty;
                EditorHeaderDetailSide.ClearValue(FrameworkElement.HeightProperty);
                EditorHeaderDetailSide.Visibility = Visibility.Collapsed;
                EditorHeaderDetailSideXml.Visibility = Visibility.Visible;
                ResetEditorHeaderDetailSideScroll();
                return;
            }
        }

        EditorHeaderDetailSideXml.Items.Clear();
        EditorHeaderDetailSideXml.Visibility = Visibility.Collapsed;
        EditorHeaderDetailSideMeta.Text = string.Empty;
        EditorHeaderDetailSideMeta.Visibility = Visibility.Collapsed;
        EditorHeaderDetailSide.Text = text;
        EditorHeaderDetailSide.Height = MeasureEditorHeaderDetailSideTextHeight(text);
        EditorHeaderDetailSide.Visibility = Visibility.Visible;
        // バイナリ等は外側 ScrollViewer の位置が残るので、チャンク切替では先頭へ。
        ResetEditorHeaderDetailSideScroll();
    }

    /// <summary>サイド情報のスクロール位置を先頭へ。レイアウト後にもう一度。Reveal は抑止する。</summary>
    private void ResetEditorHeaderDetailSideScroll()
    {
        _editorHeaderDetailSideScrollRevealSuppress = true;
        EditorHeaderDetailSideScroll.ScrollToHome();
        Dispatcher.BeginInvoke(
            () =>
            {
                EditorHeaderDetailSideScroll.ScrollToHome();
                _editorHeaderDetailSideScrollRevealSuppress = false;
            },
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// StackPanel 内の TextBox は自動で全文高にならないので測って渡す。
    /// 行送りは <see cref="LibraryBrowserView.ApplyStructureDetailLineHeight"/> と同じ BlockLineHeight、
    /// Padding も足す（足りないと最終行が縦に見切れる）。
    /// </summary>
    private double MeasureEditorHeaderDetailSideTextHeight(string text)
    {
        var pad = EditorHeaderDetailSide.Padding;
        var fontSize = LibraryBrowserView.LibraryListFontSize;
        var lineHeight = TextBlock.GetLineHeight(EditorHeaderDetailSide);
        if (double.IsNaN(lineHeight) || lineHeight <= 0)
        {
            lineHeight = Math.Max(1, fontSize);
        }

        if (string.IsNullOrEmpty(text))
        {
            return lineHeight + pad.Top + pad.Bottom;
        }

        // \n 終端の空行も TextBox は1行分取る。
        var lineCount = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Length;
        // UseLayoutRounding で 1px 削れることがあるので余裕を見る。
        return (lineCount * lineHeight) + pad.Top + pad.Bottom + 1;
    }

    private void HideEditorHeaderDetailSide(bool forgetHeld)
    {
        if (forgetHeld)
        {
            _editorHeaderDetailSideHeldWidth = 0;
        }

        if (EditorHeaderDetailSideHost.Visibility == Visibility.Collapsed
            && EditorHeaderDetailSide.Text.Length == 0
            && EditorHeaderDetailSideHost.Width <= 0)
        {
            return;
        }

        EditorHeaderDetailSide.Text = string.Empty;
        EditorHeaderDetailSideMeta.Text = string.Empty;
        EditorHeaderDetailSideMeta.Visibility = Visibility.Collapsed;
        EditorHeaderDetailSideXml.Items.Clear();
        EditorHeaderDetailSideXml.Visibility = Visibility.Collapsed;
        EditorHeaderDetailSide.Visibility = Visibility.Visible;
        EditorHeaderDetailSideHost.BeginAnimation(UIElement.OpacityProperty, null);
        EditorHeaderDetailSideHost.Opacity = 0;
        EditorHeaderDetailSideHost.Visibility = Visibility.Collapsed;
        EditorHeaderDetailSideHost.IsHitTestVisible = false;
        EditorHeaderDetailSideHost.MinWidth = 0;
        EditorHeaderDetailSideHost.Width = 0;
    }

    private void EditorHeaderDetailSideHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SyncEditorHeaderDetailSideVerticalAlign();

    /// <summary>
    /// プレイヤー情報列と同じく上寄せ。溢れるときだけスクロール。
    /// 高さは中身の自然高のみ使い、不足 Height で切らない。
    /// </summary>
    private void SyncEditorHeaderDetailSideVerticalAlign()
    {
        if (EditorHeaderDetailSideHost.Visibility != Visibility.Visible
            || EditorHeaderDetailSideHost.ActualHeight <= 1)
        {
            return;
        }

        var titleH = EditorHeaderDetailSideTitle.Visibility == Visibility.Visible
            ? EditorHeaderDetailSideTitle.DesiredSize.Height
                + EditorHeaderDetailSideTitle.Margin.Top
                + EditorHeaderDetailSideTitle.Margin.Bottom
            : 0;
        if (titleH <= 0)
        {
            EditorHeaderDetailSideTitle.Measure(
                new Size(Math.Max(0, EditorHeaderDetailSideHost.ActualWidth), double.PositiveInfinity));
            titleH = EditorHeaderDetailSideTitle.DesiredSize.Height
                + EditorHeaderDetailSideTitle.Margin.Top
                + EditorHeaderDetailSideTitle.Margin.Bottom;
        }

        var avail = Math.Max(0, EditorHeaderDetailSideHost.ActualHeight - titleH);
        var width = Math.Max(0, EditorHeaderDetailSideHost.ActualWidth);

        EditorHeaderDetailSideScroll.ClearValue(FrameworkElement.HeightProperty);
        EditorHeaderDetailSideScroll.VerticalAlignment = VerticalAlignment.Stretch;
        EditorHeaderDetailSideStack.Measure(new Size(width, double.PositiveInfinity));
        var need = EditorHeaderDetailSideStack.DesiredSize.Height;

        if (need <= avail + 0.5)
        {
            EditorHeaderDetailSideScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            EditorHeaderDetailSideScroll.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            EditorHeaderDetailSideScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            EditorHeaderDetailSideScroll.MaxHeight = avail;
        }
    }

    private void EnsureEditorHeaderDetailSideScrollReveal()
    {
        if (_editorHeaderDetailSideScrollStyled)
        {
            ApplyEditorHeaderDetailSideFocusLineBrush();
            SyncEditorHeaderDetailSideFocusChrome();
            return;
        }

        // プレイヤー情報列と同じ見出し／余白／タイポ／フォーカスグラデ。
        LibraryBrowserView.ApplyPaneTitleStyle(EditorHeaderDetailSideTitle, new Thickness(8, 6, 8, 8));
        EditorHeaderDetailSideTitle.Text = UiStrings.LibraryHeaderStructureDetailLabel;

        EditorHeaderDetailSideMeta.Margin = new Thickness(8, 6, 8, 2);
        EditorHeaderDetailSideMeta.FontFamily = new FontFamily("Consolas");
        LibraryBrowserView.ApplyHeaderStructureDetailTypography(EditorHeaderDetailSideMeta);
        LibraryBrowserView.ApplyStructureDetailLineHeight(
            EditorHeaderDetailSideMeta,
            LibraryBrowserView.LibraryListFontSize);

        EditorHeaderDetailSide.Padding = new Thickness(8, 6, 8, 8);
        var detailStyle = new Style(typeof(TextBox));
        detailStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Normal));
        detailStyle.Setters.Add(new Setter(Control.FontStyleProperty, FontStyles.Normal));
        detailStyle.Setters.Add(new Setter(Control.FontStretchProperty, FontStretches.Normal));
        EditorHeaderDetailSide.Style = detailStyle;
        LibraryBrowserView.ApplyHeaderStructureDetailTypography(EditorHeaderDetailSide);
        LibraryBrowserView.ApplyStructureDetailLineHeight(
            EditorHeaderDetailSide,
            LibraryBrowserView.LibraryListFontSize);

        LibraryBrowserView.ApplyHeaderStructureDetailTypography(EditorHeaderDetailSideXml);

        _editorHeaderDetailSideFocusLine = LibraryBrowserView.CreatePaneFocusLine();
        EditorHeaderDetailSideRoot.Children.Add(_editorHeaderDetailSideFocusLine);
        EditorHeaderDetailSideHost.IsKeyboardFocusWithinChanged += (_, _) =>
            SyncEditorHeaderDetailSideFocusChrome();

        // プレイヤー情報列と同じ：ホバーに加え、実スクロールでも既定時間出す。
        if (TryFindResource("LibraryScrollBarStyle") is Style style)
        {
            EditorHeaderDetailSideScroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        }

        LibraryScrollReveal.AttachOnHover(EditorHeaderDetailSideScroll);
        EditorHeaderDetailSideScroll.ScrollChanged += EditorHeaderDetailSideScroll_ScrollChanged;
        EditorHeaderDetailSideXml.AddHandler(
            TreeViewItem.ExpandedEvent,
            new RoutedEventHandler((_, _) => SyncEditorHeaderDetailSideVerticalAlign()),
            handledEventsToo: true);
        EditorHeaderDetailSideXml.AddHandler(
            TreeViewItem.CollapsedEvent,
            new RoutedEventHandler((_, _) => SyncEditorHeaderDetailSideVerticalAlign()),
            handledEventsToo: true);
        _editorHeaderDetailSideScrollStyled = true;
        SyncEditorHeaderDetailSideFocusChrome();
    }

    private void EditorHeaderDetailSideScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // カーソルがエリア外／ホバー切れでも、実スクロールしたら既定時間（約3秒）出す。
        if (_editorHeaderDetailSideScrollRevealSuppress
            || (e.VerticalChange == 0 && e.HorizontalChange == 0))
        {
            return;
        }

        LibraryScrollReveal.RevealTemporarily(EditorHeaderDetailSideScroll);
    }

    private void SyncEditorHeaderDetailSideFocusChrome()
    {
        if (_editorHeaderDetailSideFocusLine is null)
        {
            return;
        }

        _editorHeaderDetailSideFocusLine.Visibility =
            EditorHeaderDetailSideHost.IsKeyboardFocusWithin
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void ApplyEditorHeaderDetailSideFocusLineBrush()
    {
        if (_editorHeaderDetailSideFocusLine is not null)
        {
            _editorHeaderDetailSideFocusLine.Background =
                LibraryBrowserView.CreatePaneFocusLineBrush();
        }
    }

    private void ApplyEditorHeaderDetailSideLocalizedText()
    {
        if (_editorHeaderDetailSideScrollStyled)
        {
            EditorHeaderDetailSideTitle.Text = UiStrings.LibraryHeaderStructureDetailLabel;
        }
    }

    /// <summary>詳細（サイド XML／テキスト、なければ帯内インライン）へフォーカス。</summary>
    private void FocusEditorHeaderDetail()
    {
        if (EditorHeaderDetailSideHost.Visibility == Visibility.Visible
            && EditorHeaderDetailSideHost.Opacity > 0.01)
        {
            if (EditorHeaderDetailSideXml.Visibility == Visibility.Visible)
            {
                if (EditorHeaderDetailSideXml.SelectedItem is TreeViewItem selected)
                {
                    selected.Focus();
                    return;
                }

                if (EditorHeaderDetailSideXml.Items.OfType<TreeViewItem>().FirstOrDefault() is { } first)
                {
                    first.IsSelected = true;
                    first.Focus();
                    return;
                }

                EditorHeaderDetailSideXml.Focus();
                return;
            }

            if (EditorHeaderDetailSide.Visibility == Visibility.Visible)
            {
                EditorHeaderDetailSide.Focus();
                return;
            }
        }

        HeaderChunkStrip.FocusInlineDetail();
    }

    /// <summary>
    /// 情報エリアの↑↓／PgUp／PgDn。
    /// XML 表示中は行選択、テキストはスクロール（プレイヤー情報列と同じ）。
    /// </summary>
    private bool TryHandleEditorHeaderDetailNavKey(Key key)
    {
        if (!IsEditorHeaderStructureDetailFocused)
        {
            return false;
        }

        if (EditorHeaderDetailSideXml.Visibility == Visibility.Visible
            && EditorHeaderDetailSideHost.IsKeyboardFocusWithin
            && key is Key.Up or Key.Down or Key.PageUp or Key.PageDown)
        {
            var delta = key switch
            {
                Key.Up => -1,
                Key.Down => 1,
                Key.PageUp => -EditorHeaderXmlDetailPageStep(),
                Key.PageDown => EditorHeaderXmlDetailPageStep(),
                _ => 0,
            };
            MoveEditorHeaderXmlSelection(delta);
            return true;
        }

        if (HeaderChunkStrip.IsInlineDetailFocused)
        {
            return HeaderChunkStrip.TryScrollInlineDetail(key);
        }

        if (EditorHeaderDetailSide.Visibility == Visibility.Visible
            && EditorHeaderDetailSideHost.IsKeyboardFocusWithin)
        {
            switch (key)
            {
                case Key.Up:
                    EditorHeaderDetailSideScroll.LineUp();
                    LibraryScrollReveal.RevealTemporarily(EditorHeaderDetailSideScroll);
                    return true;
                case Key.Down:
                    EditorHeaderDetailSideScroll.LineDown();
                    LibraryScrollReveal.RevealTemporarily(EditorHeaderDetailSideScroll);
                    return true;
                case Key.PageUp:
                    EditorHeaderDetailSideScroll.PageUp();
                    LibraryScrollReveal.RevealTemporarily(EditorHeaderDetailSideScroll);
                    return true;
                case Key.PageDown:
                    EditorHeaderDetailSideScroll.PageDown();
                    LibraryScrollReveal.RevealTemporarily(EditorHeaderDetailSideScroll);
                    return true;
            }
        }

        return false;
    }

    private int EditorHeaderXmlDetailPageStep()
    {
        var row = Math.Max(1d, LibraryBrowserView.LibraryListFontSize + 1);
        var visible = EditorHeaderDetailSideScroll is { ViewportHeight: > 1 }
            ? Math.Max(1, (int)Math.Floor(EditorHeaderDetailSideScroll.ViewportHeight / row))
            : 10;
        return LibraryPlayerMode.PageStep(visible);
    }

    private void MoveEditorHeaderXmlSelection(int delta)
    {
        if (delta == 0 || EditorHeaderDetailSideXml.Visibility != Visibility.Visible)
        {
            return;
        }

        var items = EditorHeaderChunkStripView.FlattenVisibleTreeItems(EditorHeaderDetailSideXml.Items);
        if (items.Count == 0)
        {
            return;
        }

        var current = EditorHeaderDetailSideXml.SelectedItem as TreeViewItem;
        var index = current is null ? -1 : items.IndexOf(current);
        if (index < 0)
        {
            index = delta > 0 ? -1 : items.Count;
        }

        var next = Math.Clamp(index + delta, 0, items.Count - 1);
        var item = items[next];
        item.IsSelected = true;
        item.BringIntoView();
        if (IsEditorHeaderStructureDetailFocused)
        {
            item.Focus();
        }
    }

    /// <summary>サイド XML ツリーの展開／折りたたみ（*／/／Shift+/・←→）。</summary>
    private bool TryHandleEditorHeaderSideXmlExpandKey(Key key, ModifierKeys modifiers)
    {
        if (EditorHeaderDetailSideXml.Visibility != Visibility.Visible
            || !EditorHeaderDetailSideHost.IsKeyboardFocusWithin)
        {
            return false;
        }

        if (LibraryPlayerMode.IsExplorerExpandAll(key, modifiers))
        {
            if (EditorHeaderDetailSideXml.SelectedItem is TreeViewItem selected)
            {
                ExpandEditorHeaderXmlSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseSubtree(key, modifiers))
        {
            if (EditorHeaderDetailSideXml.SelectedItem is TreeViewItem selected)
            {
                CollapseEditorHeaderXmlSubtree(selected);
            }

            return true;
        }

        if (LibraryPlayerMode.IsExplorerCollapseAllRoots(key, modifiers))
        {
            foreach (var root in EditorHeaderDetailSideXml.Items.OfType<TreeViewItem>())
            {
                CollapseEditorHeaderXmlSubtree(root);
            }

            return true;
        }

        if (key is Key.Left or Key.Right && modifiers == ModifierKeys.None)
        {
            return TryArrowExpandCollapseEditorHeaderXml(key);
        }

        return false;
    }

    private bool TryArrowExpandCollapseEditorHeaderXml(Key key)
    {
        if (EditorHeaderDetailSideXml.SelectedItem is not TreeViewItem item || !item.HasItems)
        {
            return false;
        }

        if (key == Key.Right)
        {
            if (!item.IsExpanded)
            {
                item.IsExpanded = true;
                return true;
            }

            if (item.Items.OfType<TreeViewItem>().FirstOrDefault() is { } firstChild)
            {
                firstChild.IsSelected = true;
                firstChild.BringIntoView();
                firstChild.Focus();
            }

            return true;
        }

        if (item.IsExpanded)
        {
            item.IsExpanded = false;
            return true;
        }

        if (ItemsControl.ItemsControlFromItemContainer(item) is TreeViewItem parent)
        {
            parent.IsSelected = true;
            parent.BringIntoView();
            parent.Focus();
            return true;
        }

        return true;
    }

    private static void ExpandEditorHeaderXmlSubtree(TreeViewItem item)
    {
        if (!item.HasItems)
        {
            return;
        }

        item.IsExpanded = true;
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            ExpandEditorHeaderXmlSubtree(child);
        }
    }

    private static void CollapseEditorHeaderXmlSubtree(TreeViewItem item)
    {
        foreach (var child in item.Items.OfType<TreeViewItem>())
        {
            CollapseEditorHeaderXmlSubtree(child);
        }

        if (item.HasItems)
        {
            item.IsExpanded = false;
        }
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

        // 載せ替えは RefreshViewsAfterHistoryPaste 済み。プレイヤーでは上書き保存も行う。
        if (IsLibraryMaximized)
        {
            var saveTargets = new List<DocumentSession>();
            for (var i = 0; i < sessions.Length; i++)
            {
                if (applied[i] > 0)
                {
                    saveTargets.Add(sessions[i]);
                }
            }

            if (saveTargets.Count > 0)
            {
                await SaveSessionsInPlaceQuietAsync(saveTargets).ConfigureAwait(true);
            }
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

    /// <summary>
    /// 破壊編集・履歴適用・Undo／Redo の共通出口。
    /// プレイリストではここで必ず行を全件載せ替える（個別の UpdateSessionRow 連打は禁止）。
    /// 新規の一括破壊編集も <see cref="TryRunBatchEdit"/>／本メソッド経由にすればカーソル破壊を避けられる。
    /// </summary>
    private void RefreshViewsAfterHistoryPaste(IReadOnlyList<DocumentSession> sessions, IReadOnlyList<int> applied)
    {
        var activeApplied = 0;
        var anyApplied = false;
        for (var i = 0; i < sessions.Count; i++)
        {
            if (applied[i] <= 0)
            {
                continue;
            }

            anyApplied = true;
            if (ReferenceEquals(sessions[i], _activeSession))
            {
                activeApplied = applied[i];
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

        // AfterEdit → SetSessions の部分パッチより後で全件張り直し、帯／キャレットを確定する。
        if (anyApplied && IsLibraryMaximized)
        {
            LibraryBrowser.RematerializeBoundPlaylistRows();
        }
        else if (anyApplied)
        {
            // エディタ中の破壊編集。プレイヤー復帰時に行とヘッダ構造を取り直す。
            MarkLibraryPlaylistStaleFromEditor();
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
