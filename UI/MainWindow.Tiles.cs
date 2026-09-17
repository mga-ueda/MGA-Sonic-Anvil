using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>タイル表示。複数読み込み時は設定の並べ方、終了時の配置は作業コピーに残す。</summary>
    private WaveformTileArrange _tileArrange;
    private bool _tileMode => _tileArrange != WaveformTileArrange.Off;
    private bool _tileLayoutBusy;
    private bool _tileNeedsInitialBind;
    private int _tileBuildGeneration;
    private Grid? _tileGrid;
    private WaveformView? _tileActiveView;
    private readonly List<WaveformTilePane> _tilePanes = [];

    private sealed record WaveformTilePane(
        DocumentSession Session,
        WaveformView View,
        Border Host,
        Border Header,
        TextBlock Title,
        DockPanel Body,
        Grid Layers);

    private void RestoreWaveformTileArrange(string? stored)
    {
        var arrange = WaveformTileLayout.Parse(stored);
        if (arrange == WaveformTileArrange.Off || _sessions.Count < 2 || _activeSession is null)
        {
            return;
        }

        var usable = ResolveUsableTileArrange(arrange);
        if (usable == WaveformTileArrange.Off)
        {
            return;
        }

        ApplyTileArrange(usable);
    }

    private void ApplyPreferredOrRestoredTileArrange(AppSettings settings)
    {
        var preferred = WaveformTileLayout.Parse(settings.MultiFileArrange);
        if (preferred != WaveformTileArrange.Off)
        {
            RestoreWaveformTileArrange(WaveformTileLayout.Format(preferred));
            return;
        }

        RestoreWaveformTileArrange(settings.WaveformTileArrange);
    }

    private void ApplyPreferredMultiFileArrange(int openedCount)
    {
        if (_sessions.Count < 2 || _activeSession is null)
        {
            return;
        }

        var preferred = WaveformTileLayout.Parse(AppStorage.Settings.MultiFileArrange);
        if (preferred != WaveformTileArrange.Off && (!_tileMode || openedCount >= 2))
        {
            var usable = ResolveUsableTileArrange(preferred);
            if (usable != WaveformTileArrange.Off)
            {
                ApplyTileArrange(usable);
                return;
            }
        }

        NotifyWaveformSessionsChanged();
    }

    private void CycleWaveformTile()
    {
        if (_sessions.Count < 2 || _activeSession is null)
        {
            if (_tileMode)
            {
                ExitWaveformTileMode();
                Keyboard.Focus(Waveform);
            }

            return;
        }

        var (width, height) = TileHostSize();
        var next = WaveformTileLayout.Next(_tileArrange, _sessions.Count, width, height);
        if (next == _tileArrange)
        {
            return;
        }

        if (next == WaveformTileArrange.Off)
        {
            ExitWaveformTileMode();
            Keyboard.Focus(Waveform);
            return;
        }

        ApplyTileArrange(next);
        Keyboard.Focus(Waveform);
    }

    /// <summary>メニューから配置を直接指定する。Off で解除、同じ配置なら何もしない。左右・上下が収まらなければ格子へ、それも無理なら動かない。</summary>
    private void SetWaveformTileArrange(WaveformTileArrange arrange)
    {
        if (arrange == WaveformTileArrange.Off)
        {
            if (_tileMode)
            {
                ExitWaveformTileMode();
            }

            Keyboard.Focus(Waveform);
            return;
        }

        if (_sessions.Count < 2 || _activeSession is null)
        {
            Keyboard.Focus(Waveform);
            return;
        }

        if (arrange == _tileArrange)
        {
            Keyboard.Focus(Waveform);
            return;
        }

        var usable = ResolveUsableTileArrange(arrange);
        if (usable == WaveformTileArrange.Off)
        {
            return;
        }

        if (usable == _tileArrange)
        {
            Keyboard.Focus(Waveform);
            return;
        }

        ApplyTileArrange(usable);
        Keyboard.Focus(Waveform);
    }

    private void WaveformTileHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        EnsureUsableTileArrange();

    /// <summary>今のタイル配置が収まらなくなったら格子を試し、それも無理なら解除する。</summary>
    private void EnsureUsableTileArrange()
    {
        if (!_tileMode || _tileLayoutBusy)
        {
            return;
        }

        if (TileArrangeFits(_tileArrange))
        {
            return;
        }

        var usable = ResolveUsableTileArrange(_tileArrange);
        if (usable == WaveformTileArrange.Off)
        {
            ExitWaveformTileMode();
            return;
        }

        ApplyTileArrange(usable);
    }

    private bool CanOfferTileArrange(WaveformTileArrange arrange) =>
        _sessions.Count > 1
        && (arrange == WaveformTileArrange.Off
            || arrange == _tileArrange
            || TileArrangeFits(arrange));

    private bool TileArrangeFits(WaveformTileArrange arrange)
    {
        var (width, height) = TileHostSize();
        return WaveformTileLayout.Fits(arrange, _sessions.Count, width, height);
    }

    private WaveformTileArrange ResolveUsableTileArrange(WaveformTileArrange arrange)
    {
        var (width, height) = TileHostSize();
        return WaveformTileLayout.Fallback(arrange, _sessions.Count, width, height);
    }

    private (double Width, double Height) TileHostSize()
    {
        var width = WaveformTileHost.ActualWidth;
        var height = WaveformTileHost.ActualHeight;
        if (width > 1 && height > 1)
        {
            return (width, height);
        }

        return (
            WaveformHostBorder.ActualWidth,
            WaveformHostBorder.ActualHeight - DesignMetrics.WaveformScrollBarHeight);
    }

    private void ApplyActiveAnalysisToAllSessions()
    {
        var mode = Waveform.AnalysisView;
        foreach (var session in _sessions)
        {
            session.AnalysisView = mode;
        }
    }

    private void ApplyTileArrange(WaveformTileArrange arrange)
    {
        if (arrange == WaveformTileArrange.Off || !TileArrangeFits(arrange))
        {
            return;
        }

        var fromOff = !_tileMode;
        if (fromOff)
        {
            CaptureActiveSessionView();
            ApplyActiveAnalysisToAllSessions();
            _tileNeedsInitialBind = true;
        }
        else
        {
            CaptureAllTileViews();
        }

        _tileArrange = arrange;
        var generation = ++_tileBuildGeneration;
        RelayoutTileGrid();
        if (fromOff)
        {
            BuildNextTile(generation);
            return;
        }

        QueueTileBuild(generation);
    }

    private void QueueTileBuild(int generation) =>
        Dispatcher.BeginInvoke(() => BuildNextTile(generation), DispatcherPriority.Background);

    private void BuildNextTile(int generation)
    {
        if (generation != _tileBuildGeneration || !_tileMode)
        {
            return;
        }

        var session = NextSessionNeedingTile();
        if (session is null)
        {
            if (_activeSession is not null && FindTilePane(_activeSession) is not null)
            {
                TryBindTiledWorkspace(_activeSession, resetInteraction: false);
            }

            FinalizeTileReveal();
            RefreshTileChrome();
            return;
        }

        var fallback = _activeSession?.AnalysisView ?? WaveformAnalysisView.Waveform;
        var pane = CreateWaveformTilePane(session, fallback);
        if (generation != _tileBuildGeneration || !_sessions.Contains(session))
        {
            if (_tileMode && _sessions.Contains(session))
            {
                _tilePanes.Add(pane);
                PlaceTilePane(pane);
            }
            else
            {
                UnhookWaveformEvents(pane.View);
                pane.View.DisposeSpectrogram();
            }

            return;
        }

        _tilePanes.Add(pane);
        PlaceTilePane(pane);
        if (ReferenceEquals(session, _activeSession) || _tileActiveView is null)
        {
            var reset = _tileNeedsInitialBind && !IsPlaybackActive();
            TryBindTiledWorkspace(session, resetInteraction: reset);
            _tileNeedsInitialBind = false;
            AttachPlaybackToActiveWaveform();
        }

        Dispatcher.Invoke(static () => { }, DispatcherPriority.Input);
        if (generation != _tileBuildGeneration)
        {
            return;
        }

        QueueTileBuild(generation);
    }

    private DocumentSession? NextSessionNeedingTile()
    {
        if (_activeSession is not null
            && _sessions.Contains(_activeSession)
            && FindTilePane(_activeSession) is null)
        {
            return _activeSession;
        }

        foreach (var session in _sessions)
        {
            if (FindTilePane(session) is null)
            {
                return session;
            }
        }

        return null;
    }

    private void RelayoutTileGrid()
    {
        if (!_tileMode)
        {
            return;
        }

        _tileLayoutBusy = true;
        try
        {
            DropStaleTilePanes();
            WaveformTileLayout.ChooseGrid(_tileArrange, _sessions.Count, out var rows, out var cols);
            var grid = CreateTileGrid(rows, cols);
            for (var i = 0; i < _sessions.Count; i++)
            {
                var pane = FindTilePane(_sessions[i]);
                if (pane is null)
                {
                    continue;
                }

                DetachTileHost(pane.Host);
                ApplyTileCell(pane.Host, i, rows, cols);
                grid.Children.Add(pane.Host);
            }

            AddUnusedTileFillers(grid, _sessions.Count, rows, cols);
            _tileGrid = grid;
            if (grid.Children.Count > 0)
            {
                RevealTileGrid();
            }
            RefreshTileChrome();
        }
        finally
        {
            _tileLayoutBusy = false;
        }
    }

    private void PlaceTilePane(WaveformTilePane pane)
    {
        WaveformTileLayout.ChooseGrid(_tileArrange, _sessions.Count, out var rows, out var cols);
        if (_tileGrid is null
            || _tileGrid.RowDefinitions.Count != rows
            || _tileGrid.ColumnDefinitions.Count != cols)
        {
            RelayoutTileGrid();
            return;
        }

        var index = _sessions.IndexOf(pane.Session);
        if (index < 0)
        {
            return;
        }

        DetachTileHost(pane.Host);
        ApplyTileCell(pane.Host, index, rows, cols);
        _tileGrid.Children.Add(pane.Host);
        RevealTileGrid();
        RefreshTileChrome();
    }

    /// <summary>
    /// タイル用グリッドを前面に重ねる。ちらつき防止のため、単一表示の波形は
    /// 消さずに下へ残し、タイルが揃った時点（<see cref="FinalizeTileReveal"/>）で隠す。
    /// </summary>
    private void RevealTileGrid()
    {
        if (_tileGrid is null || WaveformTileHost.Children.Contains(_tileGrid))
        {
            return;
        }

        for (var i = WaveformTileHost.Children.Count - 1; i >= 0; i--)
        {
            var child = WaveformTileHost.Children[i];
            if (ReferenceEquals(child, SingleWaveformHost) || ReferenceEquals(child, _tileGrid))
            {
                continue;
            }

            WaveformTileHost.Children.RemoveAt(i);
        }

        WaveformTileHost.Children.Add(_tileGrid);
    }

    /// <summary>全タイルが揃ったら下の単一表示を隠す。隙間は波形エリアと同じ配色のまま。</summary>
    private void FinalizeTileReveal()
    {
        if (!_tileMode || _tileGrid is null)
        {
            return;
        }

        ApplyWaveformTileBackground(_tileGrid);
        PrimaryWaveform.Document = null;
        PrimaryWaveform.Visibility = Visibility.Collapsed;
        SingleWaveformHost.Visibility = Visibility.Collapsed;
    }

    private Grid CreateTileGrid(int rows, int cols)
    {
        var grid = new Grid
        {
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
            // 検索のぼかしが隣のタイルへはみ出しても、波形エリアの外へは出さない。
            ClipToBounds = true,
        };
        ApplyWaveformTileBackground(grid);
        for (var r = 0; r < rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        for (var c = 0; c < cols; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        return grid;
    }

    private static void ApplyWaveformTileBackground(Panel panel) =>
        panel.SetResourceReference(Panel.BackgroundProperty, "WaveformBackBrush");

    private static void ApplyWaveformTileBackground(Border border) =>
        border.SetResourceReference(Border.BackgroundProperty, "WaveformBackBrush");

    /// <summary>余りマスに波形背景を敷く。透明のままだとライト／ダークとも GPU の黒が見える。</summary>
    private void AddUnusedTileFillers(Grid grid, int count, int rows, int cols)
    {
        foreach (var cell in WaveformTileLayout.UnusedCells(count, rows, cols))
        {
            var fill = new Border { IsHitTestVisible = false };
            ApplyWaveformTileBackground(fill);
            ApplyTileDivider(fill, cell, cols);
            Grid.SetRow(fill, cell.Row);
            Grid.SetColumn(fill, cell.Column);
            Grid.SetRowSpan(fill, cell.RowSpan);
            Grid.SetColumnSpan(fill, cell.ColumnSpan);
            grid.Children.Add(fill);
        }
    }

    private void ApplyTileCell(Border host, int index, int rows, int cols)
    {
        var cell = WaveformTileLayout.Cell(index, _sessions.Count, rows, cols);
        Grid.SetRow(host, cell.Row);
        Grid.SetColumn(host, cell.Column);
        Grid.SetRowSpan(host, cell.RowSpan);
        Grid.SetColumnSpan(host, cell.ColumnSpan);
        ApplyTileDivider(host, cell, cols);
    }

    /// <summary>
    /// F11 最大化中のタイル右端の縦仕切り幅。偶数にして、検索のすりガラスが左右から半分ずつ重なれるようにする。
    /// </summary>
    private const double TileDividerWidth = 4;

    /// <summary>
    /// F11 最大化中だけ、隣の波形と地続きに見えないよう右端に縦仕切りを入れる。
    /// 配色はチャンネル名・dB 目盛り列と同じ。横の境目はファイル名帯が仕切りになるので入れない。
    /// 通常表示ではラベル列が残るため仕切りは不要。
    /// </summary>
    private void ApplyTileDivider(Border host, WaveformTileCell cell, int cols)
    {
        var divider = _waveformMaximized && cell.Column + cell.ColumnSpan < cols;
        host.BorderThickness = new Thickness(0, 0, divider ? TileDividerWidth : 0, 0);
        host.SetResourceReference(Border.BorderBrushProperty, "TimelineWellBackBrush");
    }

    /// <summary>F11 の出入りで既存タイル（埋め草含む）の縦仕切りを付け外しする。</summary>
    private void RefreshTileDividers()
    {
        if (!_tileMode || _tileGrid is null)
        {
            return;
        }

        var cols = _tileGrid.ColumnDefinitions.Count;
        foreach (var child in _tileGrid.Children.OfType<Border>())
        {
            if (ReferenceEquals(child, _tileSearchFrost))
            {
                continue;
            }

            var cell = new WaveformTileCell(
                Grid.GetRow(child),
                Grid.GetColumn(child),
                Grid.GetRowSpan(child),
                Grid.GetColumnSpan(child));
            ApplyTileDivider(child, cell, cols);
        }
    }

    private static void DetachTileHost(FrameworkElement host)
    {
        if (host.Parent is Panel parent)
        {
            parent.Children.Remove(host);
        }
    }

    private void ExitWaveformTileMode(bool bindPrimary = true)
    {
        if (!_tileMode && _tilePanes.Count == 0)
        {
            return;
        }

        CloseTileSearch();
        _tileBuildGeneration++;
        CaptureAllTileViews();
        TearDownWaveformTiles();
        _tileArrange = WaveformTileArrange.Off;
        _tileNeedsInitialBind = false;
        _tileGrid = null;
        _tileActiveView = null;
        WaveformTileHost.Children.Clear();
        WaveformTileHost.Children.Add(SingleWaveformHost);
        SingleWaveformHost.Visibility = Visibility.Visible;
        PrimaryWaveform.Visibility = Visibility.Visible;
        Overview.SeekTrailSource = PrimaryWaveform;
        if (!bindPrimary)
        {
            return;
        }

        if (_activeSession is not null)
        {
            ApplySessionToView(PrimaryWaveform, _activeSession, applyAnalysis: true);
        }
        else
        {
            PrimaryWaveform.Document = null;
        }

        Transport.SetAnalysisView(PrimaryWaveform.AnalysisView);
        SyncViewChrome();
        RefreshStatus();
        AttachPlaybackToActiveWaveform();
    }

    private void NotifyWaveformSessionsChanged()
    {
        if (!_tileMode)
        {
            return;
        }

        if (_sessions.Count < 2)
        {
            ExitWaveformTileMode(bindPrimary: false);
            if (_activeSession is not null)
            {
                BindSingleWorkspace(_activeSession);
            }

            return;
        }

        if (!TileArrangeFits(_tileArrange))
        {
            var usable = ResolveUsableTileArrange(_tileArrange);
            if (usable == WaveformTileArrange.Off)
            {
                ExitWaveformTileMode();
                return;
            }

            ApplyTileArrange(usable);
            return;
        }

        DropStaleTilePanes();
        var generation = ++_tileBuildGeneration;
        RelayoutTileGrid();
        QueueTileBuild(generation);
    }

    private void DropStaleTilePanes()
    {
        for (var i = _tilePanes.Count - 1; i >= 0; i--)
        {
            var pane = _tilePanes[i];
            if (_sessions.Contains(pane.Session))
            {
                continue;
            }

            UnhookWaveformEvents(pane.View);
            pane.View.DisposeSpectrogram();
            DetachTileHost(pane.Host);
            _tilePanes.RemoveAt(i);
            if (ReferenceEquals(_tileActiveView, pane.View))
            {
                _tileActiveView = null;
            }
        }
    }

    private WaveformTilePane CreateWaveformTilePane(
        DocumentSession session,
        WaveformAnalysisView analysisFallback)
    {
        var view = new WaveformView { ClipToBounds = true };
        ApplySessionToView(view, session, applyAnalysis: true, analysisFallback);
        HookWaveformEvents(view);

        var title = new TextBlock
        {
            Text = session.TabTitle,
            FontSize = 11,
            FontFamily = new FontFamily("Consolas"),
            Margin = new Thickness(10, 0, 10, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Cursor = Cursors.Hand,
        };
        TipService.Set(title, (session.Document.SourcePath ?? UiStrings.UntitledDocument)
            + Environment.NewLine + UiStrings.TipRenameFile);
        title.MouseLeftButtonDown += (_, e) =>
        {
            if (TryBeginFileNameEditFromClick(session, title, e))
            {
                e.Handled = true;
            }
        };
        var headerBody = new DockPanel();
        headerBody.Children.Add(title);
        var header = new Border
        {
            Height = DesignMetrics.DocumentTabBarHeight,
            Child = headerBody,
            Cursor = Cursors.Hand,
        };
        header.SizeChanged += (_, _) =>
        {
            title.MaxWidth = Math.Max(0, header.ActualWidth - title.Margin.Left - title.Margin.Right);
        };
        header.MouseLeftButtonUp += (_, e) => HandleTabOrTileChromeClick(session, e);
        header.MouseRightButtonUp += (_, e) =>
        {
            e.Handled = true;
            OpenTabContextMenu(header, session);
        };
        var body = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(view);

        // 検索のぼかしはタイル境界を越えて隣へ混ぜる。クリップはグリッド全体側。
        var layers = new Grid { ClipToBounds = true };
        layers.Children.Add(body);

        var host = new Border
        {
            Child = layers,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true,
        };
        host.PreviewMouseDown += (_, e) =>
        {
            // すりガラスに覆われたタイルは操作させない（アクティブ化もしない）。
            if (IsTileSearchVeiled(session))
            {
                e.Handled = true;
                return;
            }

            if (_tileLayoutBusy
                || e.OriginalSource is DependencyObject origin && IsDescendantOf(origin, header))
            {
                return;
            }

            if (ReferenceEquals(session, _activeSession))
            {
                return;
            }

            ActivateSession(session);
        };

        return new WaveformTilePane(session, view, host, header, title, body, layers);
    }

    private void TearDownWaveformTiles()
    {
        foreach (var pane in _tilePanes)
        {
            UnhookWaveformEvents(pane.View);
            pane.View.DisposeSpectrogram();
        }

        _tilePanes.Clear();
        _tileActiveView = null;
        _tileGrid = null;
        _tileSearchFrost = null;
        WaveformTileHost.Children.Clear();
    }

    private bool TryBindTiledWorkspace(DocumentSession session, bool resetInteraction = true)
    {
        var pane = FindTilePane(session);
        if (pane is null)
        {
            return false;
        }

        _bindingWorkspace = true;
        try
        {
            if (resetInteraction)
            {
                ResetWorkspaceInteraction();
            }
            _activeSession = session;
            _tileActiveView = pane.View;
            Overview.SeekTrailSource = pane.View;
            Overview.Document = _document;
            if (_recording && _recordSession is not null && ReferenceEquals(session, _recordSession))
            {
                pane.View.SetLiveRecording(true);
            }

            ApplyChannelSolo();
            Overview.SetSelectedMarkerFrames(pane.View.SelectedMarkerFrames);
            SyncOverviewPlayhead();
            Transport.SetCommandsEnabled(_document is not null);
            if (resetInteraction)
            {
                Transport.SetPlaying(false);
                ExtinguishMeter();
            }

            SyncMonitorLayout();
            LoudnessMeter.Document = _document;
            if (resetInteraction)
            {
                LoudnessMeter.ResetLive();
            }
            RebuildTabBar();
            RefreshTileChrome();
            RefreshTitle();
            SyncViewChrome();
            RefreshStatus();
            RefreshHistoryStrip();
            Transport.SetAnalysisView(pane.View.AnalysisView);
        }
        finally
        {
            _bindingWorkspace = false;
        }

        return true;
    }

    private void ApplySessionToView(
        WaveformView view,
        DocumentSession session,
        bool applyAnalysis,
        WaveformAnalysisView? analysisFallback = null)
    {
        view.Document = session.Document;
        if (applyAnalysis)
        {
            var mode = WaveformTileLayout.ResolveAnalysis(session.AnalysisView, analysisFallback ?? WaveformAnalysisView.Waveform);
            session.AnalysisView = mode;
            view.SetAnalysisView(mode);
        }

        view.ApplyPersistedView(
            session.TimeZoom,
            session.AmpZoom,
            session.ViewStart,
            session.PlayheadFrame);
        view.RestoreSelectedMarkers(session.SelectedMarkerFrames);
        view.LoopEnabled = session.LoopEnabled;
        view.ShowScaleLane = !_waveformMaximized;
        view.SetSoloMask(session.SoloMask);
        view.ApplySpeakerLayout(
            AppStorage.Settings.ResolvedPlaybackLayout(),
            AppStorage.Settings.ResolvedFileChannelMap());
        view.LoudnessTargetLufs = AppStorage.Settings.ResolvedLoudnessTargetLufs();
        if (_recording && _recordSession is not null && ReferenceEquals(session, _recordSession))
        {
            view.SetLiveRecording(true);
        }
    }

    private void CaptureAllTileViews()
    {
        foreach (var pane in _tilePanes)
        {
            if (_sessions.Contains(pane.Session))
            {
                CaptureViewToSession(pane.Session, pane.View);
            }
        }
    }

    private WaveformTilePane? FindTilePane(DocumentSession session)
    {
        foreach (var pane in _tilePanes)
        {
            if (ReferenceEquals(pane.Session, session))
            {
                return pane;
            }
        }

        return null;
    }

    private DocumentSession? SessionForWaveform(WaveformView view)
    {
        if (!_tileMode)
        {
            return ReferenceEquals(view, PrimaryWaveform) ? _activeSession : null;
        }

        foreach (var pane in _tilePanes)
        {
            if (ReferenceEquals(pane.View, view))
            {
                return pane.Session;
            }
        }

        return null;
    }

    private void RefreshTileChrome()
    {
        if (!_tileMode)
        {
            return;
        }

        if (_tileGrid is not null)
        {
            ApplyWaveformTileBackground(_tileGrid);
        }

        foreach (var pane in _tilePanes)
        {
            var highlighted = IsTabChromeHighlighted(pane.Session);
            var dirty = pane.Session.Document.IsDirty;
            if (!ReferenceEquals(pane.Title, _fileNameEditTitle))
            {
                pane.Title.Text = pane.Session.TabTitle;
            }
            pane.Title.Foreground = (Brush)FindResource(
                dirty ? "DirtyAccentBrush" : highlighted ? "PrimaryForeBrush" : "MutedForeBrush");
            var path = pane.Session.Document.SourcePath ?? UiStrings.UntitledDocument;
            TipService.Set(pane.Title, path + Environment.NewLine + UiStrings.TipRenameFile);
            ApplyWaveformTileBackground(pane.Host);
            pane.Header.Background = BrushOrTransparent(
                highlighted ? "WaveformTileActiveHeaderBrush" : "TimelineWellBackBrush");
        }

        ApplyTileSearchVeils();
    }

    private void ForEachWaveform(Action<WaveformView> action)
    {
        if (_tileMode)
        {
            foreach (var pane in _tilePanes)
            {
                action(pane.View);
            }

            return;
        }

        action(PrimaryWaveform);
    }

    private void DisposeAllWaveforms()
    {
        _tileBuildGeneration++;
        ForEachWaveform(view => view.DisposeSpectrogram());
        if (_tileMode)
        {
            PrimaryWaveform.DisposeSpectrogram();
        }
    }

    private WaveformView? FindWaveformFromOrigin(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is WaveformView view)
            {
                return view;
            }

            origin = VisualTreeHelper.GetParent(origin);
        }

        return null;
    }

    private void HookWaveformEvents(WaveformView view)
    {
        view.AnalysisViewChanged += OnWaveformAnalysisViewChanged;
        view.CursorCommitted += OnWaveformCursorCommitted;
        view.ScrubStarted += OnWaveformScrubStarted;
        view.ScrubPreviewed += OnWaveformScrubPreviewed;
        view.ScrubEnded += OnWaveformScrubEnded;
        view.MarkerCommentCommitted += OnWaveformMarkerCommentCommitted;
        view.RegionNameCommitted += OnWaveformRegionNameCommitted;
        view.TimelineDragStarting += OnWaveformTimelineDragStarting;
        view.TimelineLayoutCommitted += OnWaveformTimelineLayoutCommitted;
        view.MarkersChanged += OnWaveformMarkersChanged;
        view.ContextMenuRequested += OnWaveformContextMenuRequested;
        view.SelectionChanged += OnWaveformSelectionChangedFromView;
        view.ChannelLabelClicked += OnWaveformChannelLabelClicked;
        view.ViewChanged += OnWaveformViewChanged;
    }

    private void UnhookWaveformEvents(WaveformView view)
    {
        view.AnalysisViewChanged -= OnWaveformAnalysisViewChanged;
        view.CursorCommitted -= OnWaveformCursorCommitted;
        view.ScrubStarted -= OnWaveformScrubStarted;
        view.ScrubPreviewed -= OnWaveformScrubPreviewed;
        view.ScrubEnded -= OnWaveformScrubEnded;
        view.MarkerCommentCommitted -= OnWaveformMarkerCommentCommitted;
        view.RegionNameCommitted -= OnWaveformRegionNameCommitted;
        view.TimelineDragStarting -= OnWaveformTimelineDragStarting;
        view.TimelineLayoutCommitted -= OnWaveformTimelineLayoutCommitted;
        view.MarkersChanged -= OnWaveformMarkersChanged;
        view.ContextMenuRequested -= OnWaveformContextMenuRequested;
        view.SelectionChanged -= OnWaveformSelectionChangedFromView;
        view.ChannelLabelClicked -= OnWaveformChannelLabelClicked;
        view.ViewChanged -= OnWaveformViewChanged;
    }

    private bool IsLiveWaveform(object? sender) =>
        !_bindingWorkspace && !_tileLayoutBusy && ReferenceEquals(sender, Waveform);

    private void OnWaveformAnalysisViewChanged(object? sender, EventArgs e)
    {
        if (sender is not WaveformView view)
        {
            return;
        }

        if (SessionForWaveform(view) is { } session)
        {
            session.AnalysisView = view.AnalysisView;
        }

        if (!IsLiveWaveform(sender))
        {
            return;
        }

        Transport.SetAnalysisView(view.AnalysisView);
    }

    private void OnWaveformCursorCommitted(object? sender, long frame)
    {
        if (IsLiveWaveform(sender))
        {
            OnCursorCommitted(frame);
        }
    }

    private void OnWaveformScrubStarted(object? sender, long frame)
    {
        if (IsLiveWaveform(sender))
        {
            OnScrubStarted(frame);
        }
    }

    private void OnWaveformScrubPreviewed(object? sender, long frame)
    {
        if (IsLiveWaveform(sender))
        {
            OnScrubPreviewed(frame);
        }
    }

    private void OnWaveformScrubEnded(object? sender, (long Frame, bool Commit) e)
    {
        if (IsLiveWaveform(sender))
        {
            OnScrubEnded(e.Frame, e.Commit);
        }
    }

    private void OnWaveformMarkerCommentCommitted(object? sender, (long Frame, string Comment) e)
    {
        if (IsLiveWaveform(sender))
        {
            CommitMarkerComment(e.Frame, e.Comment);
        }
    }

    private void OnWaveformRegionNameCommitted(object? sender, (WaveSelection Region, string Name) e)
    {
        if (IsLiveWaveform(sender))
        {
            CommitRegionName(e.Region, e.Name);
        }
    }

    private void OnWaveformTimelineDragStarting(object? sender, EventArgs e)
    {
        if (IsLiveWaveform(sender))
        {
            CommitTimelineNudgeSession();
            StopPlaceRepeat();
        }
    }

    private void OnWaveformTimelineLayoutCommitted(
        object? sender,
        (MarkerSnapshot[] MarkersBefore, WaveRegion[] RegionsBefore, WaveSelection LoopBefore) e)
    {
        if (IsLiveWaveform(sender))
        {
            CommitTimelineLayout(e.MarkersBefore, e.RegionsBefore, e.LoopBefore);
        }
    }

    private void OnWaveformMarkersChanged(object? sender, EventArgs e)
    {
        if (!IsLiveWaveform(sender))
        {
            return;
        }

        Overview.SetSelectedMarkerFrames(Waveform.SelectedMarkerFrames);
        Overview.Refresh();
    }

    private void OnWaveformContextMenuRequested(object? sender, WaveformContextHit hit)
    {
        if (sender is WaveformView view && SessionForWaveform(view) is { } session
            && !ReferenceEquals(session, _activeSession))
        {
            ActivateSession(session);
        }

        OpenWaveformContextMenu(hit);
    }

    private void OnWaveformSelectionChangedFromView(object? sender, EventArgs e)
    {
        if (IsLiveWaveform(sender))
        {
            OnWaveformSelectionChanged();
        }
    }

    private void OnWaveformChannelLabelClicked(object? sender, (int Channel, bool Add, bool Mute) e)
    {
        if (IsLiveWaveform(sender))
        {
            ToggleChannelSolo(e.Channel, e.Add, e.Mute);
        }
    }

    private void OnWaveformViewChanged(object? sender, EventArgs e)
    {
        if (IsLiveWaveform(sender))
        {
            SyncViewChrome();
        }
    }
}
