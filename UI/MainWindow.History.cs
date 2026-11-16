using System.Globalization;
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
        var showChunks = show && AppStorage.Settings.EditorShowHeaderStructure;

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
            RefreshEditorHeaderChunks(showChunks);
            BindVstInsertRackSession(showVst);

            if (wantInstant && EditorChromeNeedsOpaqueSnap(show, showTimecode, showVst, showChunks))
            {
                ApplyEditorChromeFade(show, showTimecode, showVst, showChunks, instant: true);
                return;
            }

            // ドック切替や VST 有効化など、論理表示は同じで子だけ変わる場合。
            // チャンク帯をオフにするときは即 Collapse し、タイムコード側に幅を返す。
            ApplyEditorChromeTarget(HistoryStrip, show, instant: false);
            ApplyEditorChromeTarget(HeaderChunkStrip, showChunks, instant: !showChunks);
            ApplyEditorChromeTarget(EditorPositionTimecodeHost, showTimecode, instant: false);
            ApplyEditorChromeTarget(VstInsertRack, showVst, instant: false);
            return;
        }

        ApplyEditorChromeFade(show, showTimecode, showVst, showChunks, wantInstant);
        _editorChromeShown = show;
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
    /// 既に空なら Clear／配置同期もスキップし、文書切替などが帯の重さに乗らないようにする。
    /// </summary>
    private void RefreshEditorHeaderChunks(bool show)
    {
        if (!show)
        {
            if (!HeaderChunkStrip.HasContent
                && EditorHeaderDetailSideHost.Visibility != Visibility.Visible)
            {
                return;
            }

            // H オフ／chrome 非表示：中身と幅高水位を捨てる（帯コメント通り）。
            HeaderChunkStrip.Clear();
            SyncEditorHeaderDetailPlacement();
            return;
        }

        HeaderChunkStrip.LoadFromPath(_document?.SourcePath);
        SyncEditorHeaderDetailPlacement();
    }

    private void HeaderChunkStrip_DetailChanged(object sender, EventArgs e) =>
        SyncEditorHeaderDetailPlacement();

    private void TransportChromeHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        SyncEditorHeaderDetailPlacement();

    /// <summary>サイド情報列の幅高水位。H トグル／エディタ退出で忘れる。</summary>
    private double _editorHeaderDetailSideHeldWidth;
    /// <summary>サイド情報のスクロールバー／見出し／フォーカス帯を一度だけ付ける。</summary>
    private bool _editorHeaderDetailSideScrollStyled;
    /// <summary>プレイヤー情報列と同じ上端フォーカスグラデ。</summary>
    private Border? _editorHeaderDetailSideFocusLine;

    /// <summary>
    /// ヘッダ情報の詳細。帯右に載る余裕があればインライン、無ければ波形とレベルメーターのあいだ。
    /// 全画面（F11／F12 のレイアウト全画面オン）は常にサイド列。
    /// </summary>
    private void SyncEditorHeaderDetailPlacement()
    {
        var showChunks = !IsLibraryMaximized
            && HeaderChunkStrip.Visibility == Visibility.Visible
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
        var leftIfInline = hostW - EditorChromeRightOccupiedWidth() - treeW - detailW - headerMargin;
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
    }

    /// <summary>StackPanel 内の TextBox は自動で全文高にならないので測って渡す。</summary>
    private double MeasureEditorHeaderDetailSideTextHeight(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return LibraryBrowserView.LibraryListFontSize;
        }

        var dpi = VisualTreeHelper.GetDpi(EditorHeaderDetailSide).PixelsPerDip;
        if (dpi <= 0)
        {
            dpi = 1;
        }

        var typeface = new Typeface(
            new FontFamily("Consolas"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);
        var fontSize = LibraryBrowserView.LibraryListFontSize;
        double height = 0;
        foreach (var line in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var ft = new FormattedText(
                line.Length == 0 ? " " : line,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.Black,
                dpi);
            height += Math.Max(fontSize, ft.Height);
        }

        return Math.Max(fontSize, height + 2);
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

        // プレイヤー情報列と同じ：ホバー中だけスクロールバーをフェード表示。
        if (TryFindResource("LibraryScrollBarStyle") is Style style)
        {
            EditorHeaderDetailSideScroll.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)] = style;
        }

        LibraryScrollReveal.AttachOnHover(EditorHeaderDetailSideScroll);
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
                    return true;
                case Key.Down:
                    EditorHeaderDetailSideScroll.LineDown();
                    return true;
                case Key.PageUp:
                    EditorHeaderDetailSideScroll.PageUp();
                    return true;
                case Key.PageDown:
                    EditorHeaderDetailSideScroll.PageDown();
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
