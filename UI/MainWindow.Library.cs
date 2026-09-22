using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private int _libraryLoadGeneration;
    private DocumentSession? _libraryLoadSession;
    private bool _libraryPlayFirstPending;

    /// <summary>ツリーの Enter（クリア後）だけ true。Shift+Enter の追加では立てない。</summary>
    private bool _libraryExplorerPlayOnOpen;
    /// <summary>再帰追加中に再生する最初の登録曲。グループ再配置で他曲へ飛ばないようにする。</summary>
    private DocumentSession? _libraryFolderPlaySession;
    /// <summary>プレイリスト置換中は空セッションで既定ウォッシュへ落とさない。</summary>
    private bool _libraryHoldJacketWash;
    private bool _libraryPlayOnArrowRelease;
    /// <summary>Space 再生。true のあいだは曲末で次へ進まず停止する。</summary>
    private bool _libraryStopAfterTrack;
    /// <summary>F9。プレイヤーのサイド／メーター／トランスポート／ステータスを隠す。波形は残す。再生は止めない。</summary>
    private bool _libraryMinimalChrome;
    private bool _libraryPlayerHostActive;
    private readonly LibraryPlaylistRemoveUndo _libraryPlaylistUndo = new();
    private bool _libraryWaapiSuspended;
    private bool _restoreWaapiAfterLibrary;
    private readonly HashSet<AudioDocument> _libraryPeakJobs = [];
    private readonly Dictionary<AudioDocument, CancellationTokenSource> _libraryPeakJobCts = [];
    private int _libraryPeakGeneration;
    private int _libraryWavePaintTicket;
    private CancellationTokenSource _libraryPeakCts = new();
    private int _libraryFolderShowGeneration;
    private int _gaplessToken;
    private DocumentSession? _gaplessTarget;
    private bool _gaplessInFlight;
    private bool _gaplessFailed;
    private bool _playerMeterChrome;
    private bool? _playerMetersShown;

    internal bool IsLibraryMaximized => _waveformMaximizeMode == WaveformMaximizeMode.Library;

    /// <summary>F11 とプレイヤーでは dB 目盛り列を畳む。編集時だけ残す。</summary>
    private bool ShowWaveformScaleLane =>
        _waveformMaximizeMode != WaveformMaximizeMode.Waveform && !IsLibraryMaximized;

    internal bool IsLibraryGroupComboFocused =>
        IsLibraryMaximized && LibraryBrowser.IsGroupComboFocused;

    internal bool IsLibraryExplorerFocused =>
        IsLibraryMaximized && LibraryBrowser.IsExplorerFocused;

    internal bool IsLibraryFavoritesFocused =>
        IsLibraryMaximized && LibraryBrowser.IsFavoritesFocused;

    private void ToggleLibraryMaximize()
    {
        // F9 ミニマム中の F10 はエディタへ落とさず、通常の F10 プレイヤーへ戻す。
        if (IsLibraryMaximized && _libraryMinimalChrome)
        {
            LeaveLibraryMinimalChrome();
            return;
        }

        SetWaveformMaximizeMode(
            _waveformMaximizeMode == WaveformMaximizeMode.Library
                ? WaveformMaximizeMode.Off
                : WaveformMaximizeMode.Library);
    }

    /// <summary>
    /// F9 ミニマムプレイヤー。どのモードからでも入れる。
    /// ミニマム中の F9／F10 で通常の F10 プレイヤーへ戻す（Esc や F1 では戻さない）。
    /// 切替時にウィンドウ位置・サイズをスロットへ記憶／復元する。再生中はそのまま。
    /// </summary>
    private void ToggleLibraryMinimalChrome()
    {
        if (IsLibraryMaximized && _libraryMinimalChrome)
        {
            LeaveLibraryMinimalChrome();
            return;
        }

        EnterLibraryMinimalChrome();
    }

    /// <summary>ミニマム解除 → 通常の F10。F9／F10 から。</summary>
    private void LeaveLibraryMinimalChrome()
    {
        if (!IsLibraryMaximized || !_libraryMinimalChrome)
        {
            return;
        }

        PersistCurrentWindowPlacement();
        _libraryMinimalChrome = false;
        ApplyWaveformMaximizeChrome();
        TryApplyCurrentModePlacement();
        AppStorage.Save();
        FocusLibraryPaneForPlaylist();
    }

    private void EnterLibraryMinimalChrome()
    {
        if (IsLibraryMaximized)
        {
            if (_libraryMinimalChrome)
            {
                return;
            }

            PersistCurrentWindowPlacement();
            _libraryMinimalChrome = true;
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
            AppStorage.Save();
            LibraryBrowser.FocusList();
            return;
        }

        var preservePlayback = IsPlaybackActive();
        _libraryMinimalChrome = true;
        SetWaveformMaximizeMode(
            WaveformMaximizeMode.Library,
            playFirstOnLibrary: !preservePlayback);
        // SetWaveformMaximizeMode がフルスクリーン復帰でフラグを戻すことがあるので再適用。
        _libraryMinimalChrome = true;
        ApplyWaveformMaximizeChrome();
        TryApplyCurrentModePlacement();
        AppStorage.Save();
        LibraryBrowser.FocusList();
    }

    private void ApplyLibraryChrome()
    {
        var show = IsLibraryMaximized;
        ApplyStatusFieldChrome();
        LibraryBrowser.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            LibraryBrowser.SetGlowExtendsWaveform(true);
            ApplyLibraryWashChrome(true);
        }
        else
        {
            ApplyLibraryWashChrome(false);
            LibraryBrowser.SetGlowExtendsWaveform(false);
        }
        LibrarySplitter.Visibility = Visibility.Collapsed;
        DocumentTabHost.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        LibraryRowDef.MinHeight = show ? DesignMetrics.LibraryPaneMinHeight : 0;
        LibraryRowDef.Height = show
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        if (show)
        {
            LibraryWaveformRowDef.MinHeight = DesignMetrics.LibraryWaveformHeight;
            LibraryWaveformRowDef.MaxHeight = DesignMetrics.LibraryWaveformHeight;
            LibraryWaveformRowDef.Height = new GridLength(DesignMetrics.LibraryWaveformHeight);
            WaveformHostBorder.MinHeight = DesignMetrics.LibraryPaneMinHeight + DesignMetrics.LibraryWaveformHeight;
        }
        else
        {
            LibraryWaveformRowDef.MinHeight = 0;
            LibraryWaveformRowDef.MaxHeight = double.PositiveInfinity;
            LibraryWaveformRowDef.Height = new GridLength(1, GridUnitType.Star);
            ApplyWaveformHeightScale();
        }

        PrimaryWaveform.SeekAndSelectOnly = show;
        ForEachWaveform(view => view.SeekAndSelectOnly = show);
        TimeScrollStrip.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        HistoryStrip.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show)
        {
            CloseEditHistory(commit: true);
        }

        RefreshTransportCommandsEnabled();
        ApplyLibraryWaapi(show);
        ApplyLibraryPlayerHost(show);
        if (show)
        {
            ProbeLibraryTags();
            if (_libraryPlayFirstPending)
            {
                SelectAndPlayFirstLibraryTrack();
                if (_sessions.Count > 0)
                {
                    _libraryPlayFirstPending = false;
                }
            }
            else
            {
                LibraryBrowser.SetSessions(_sessions, _activeSession);
                ShowLibraryArtworkOrClear(_activeSession);
            }

            // 行高が 133px に落ちたあとの描画でないと、エディタサイズのビットマップが画面外へずれたまま残る。
            ScheduleLibraryWaveformPaint();
        }
        else
        {
            _libraryWavePaintTicket++;
            _libraryPlayFirstPending = false;
            _libraryPlayOnArrowRelease = false;
            _libraryStopAfterTrack = false;
            _libraryHoldJacketWash = false;
            _libraryPlaylistUndo.Clear();
            _ = LeaveLibraryMaximizeAsync();
        }

        SyncPlayerMeterFade();
    }

    /// <summary>
    /// プレイヤーのレベルメーター／スペアナ／ラウドネス／ゴニオ／サラウンド。
    /// 突入は消えた状態から。空なら出したままフェードアウトしない。
    /// 停止中の追加は 1 秒フェードイン、音声で動き始めたら即表示。空になったら 1 秒フェードアウト。
    /// </summary>
    private void SyncPlayerMeterFade()
    {
        var player = IsLibraryMaximized;
        var show = LibraryPlayerMode.ShowsPlayerMeters(player, _sessions.Count);
        var entering = player && !_playerMeterChrome;
        var leaving = !player && _playerMeterChrome;
        _playerMeterChrome = player;
        var instantReveal = LibraryPlayerMode.InstantPlayerMeterReveal(
            show,
            player && IsPlaybackActive());

        if (leaving)
        {
            ApplyPlayerMeterFade(visible: true, instant: true);
            _playerMetersShown = true;
            return;
        }

        if (LibraryPlayerMode.SnapPlayerMetersHiddenOnEnter(entering, instantReveal))
        {
            ApplyPlayerMeterFade(visible: false, instant: true);
            _playerMetersShown = false;
        }

        if (_playerMetersShown == show)
        {
            if (show && instantReveal)
            {
                ApplyPlayerMeterFade(visible: true, instant: true);
            }

            return;
        }

        ApplyPlayerMeterFade(show, instantReveal);
        _playerMetersShown = show;
    }

    private void ApplyPlayerMeterFade(bool visible, bool instant)
    {
        var to = visible ? 1d : 0d;
        foreach (var target in PlayerMeterFadeTargets())
        {
            var from = target.Opacity;
            target.BeginAnimation(UIElement.OpacityProperty, null);
            target.Opacity = from;
            target.IsHitTestVisible = visible;
            if (instant || Math.Abs(from - to) < 0.001)
            {
                target.Opacity = to;
                continue;
            }

            target.BeginAnimation(UIElement.OpacityProperty, LibraryPlayerMode.CreateMeterFade(from, to));
        }
    }

    private UIElement[] PlayerMeterFadeTargets() =>
        [LevelMeter, VectorScope, Spectrum, LoudnessMeter];

    /// <summary>
    /// プレイヤー中はクロムの塗りを外し、ウィンドウ全体のジャケットウォッシュを透かす。
    /// 入るときはグローを出してから塗りを外し、出るときは先に塗りを戻す。
    /// </summary>
    private void ApplyLibraryWashChrome(bool show)
    {
        if (show)
        {
            StatusBarHost.Background = Brushes.Transparent;
            TipsPanel.Background = Brushes.Transparent;
            WaveformHostBorder.Background = Brushes.Transparent;
            WaveformTileHost.Background = null;
            MeterColumn.Background = Brushes.Transparent;
            MeterTopSlot.Background = Brushes.Transparent;
            TransportChromeHost.Background = Brushes.Transparent;
            DocumentTabHost.Background = Brushes.Transparent;
            TransportBarHost.Background = Brushes.Transparent;
            LoudnessMeter.Background = Brushes.Transparent;
        }
        else
        {
            StatusBarHost.SetResourceReference(Border.BackgroundProperty, "StatusBarBackBrush");
            TipsPanel.SetResourceReference(Border.BackgroundProperty, "WaapiBarBackBrush");
            WaveformHostBorder.SetResourceReference(Border.BackgroundProperty, "WaveformBackBrush");
            WaveformTileHost.SetResourceReference(Panel.BackgroundProperty, "WaveformBackBrush");
            MeterColumn.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            MeterTopSlot.SetResourceReference(Border.BackgroundProperty, "TransportBackBrush");
            TransportChromeHost.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            DocumentTabHost.SetResourceReference(Border.BackgroundProperty, "TransportBackBrush");
            TransportBarHost.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
            LoudnessMeter.SetResourceReference(Panel.BackgroundProperty, "TransportBackBrush");
        }

        LevelMeter.WashThrough = show;
        VectorScope.WashThrough = show;
        Spectrum.WashThrough = show;
        Transport.SetWashThrough(show);
        if (_waapiToggle is not null)
        {
            _waapiToggle.WashThrough = show;
            _waapiToggle.InvalidateVisual();
        }

        LevelMeter.InvalidateVisual();
        VectorScope.InvalidateVisual();
        Spectrum.InvalidateVisual();
    }

    /// <summary>
    /// プレイヤー帯のレイアウトが決まってから波形を描き直す。
    /// エディタの拡大表示のまま描くと、133px の帯では画面外に残って空に見える。
    /// ウィンドウサイズの復元はクロム適用のあとなので、Loaded だけでは間に合わない。
    /// </summary>
    private void ScheduleLibraryWaveformPaint()
    {
        var ticket = ++_libraryWavePaintTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => PaintLibraryWaveform(ticket));
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => PaintLibraryWaveform(ticket));
    }

    private void PaintLibraryWaveform(int ticket)
    {
        if (ticket != _libraryWavePaintTicket || !IsLibraryMaximized || Waveform.Document is null)
        {
            return;
        }

        Waveform.UpdateLayout();
        if (Waveform.ActualWidth < 8 || Waveform.ActualHeight < 8)
        {
            return;
        }

        Waveform.Refresh();
    }

    /// <summary>
    /// プレイヤー退出。再生は先に止めてからクロームを戻し、そのあとフル PCM へ昇格する。
    /// 133px 帯のままだとタイルが収まらず 1 本に落ちるので、レイアウト後に並べ直す。
    /// </summary>
    private async Task LeaveLibraryMaximizeAsync()
    {
        var active = _activeSession;
        CancelLibraryPeakJobs();
        CancelLibraryGapless();
        if (active is not null)
        {
            active.PlayheadFrame = Waveform.PlayheadFrame;
            active.Document.CursorFrame = Waveform.PlayheadFrame;
        }

        // レイアウトと波形の初回描画を、フルデコードより先に通す。
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Loaded);
        if (IsLibraryMaximized)
        {
            return;
        }

        if (active is not null
            && ReferenceEquals(active, _activeSession)
            && LibraryPlayerMode.NeedsEditorPcmUpgrade(active.Document))
        {
            await EnsureLibrarySessionLoadedAsync(active).ConfigureAwait(true);
            if (IsLibraryMaximized)
            {
                return;
            }
        }

        ApplyPreferredMultiFileArrange(_sessions.Count);
        await UpgradeKeptLibrarySessionsForEditorAsync(active).ConfigureAwait(true);
        if (IsLibraryMaximized)
        {
            return;
        }

        UpgradeLibraryPeaksForEditor();
        ForEachWaveform(view => view.Refresh());
        Overview.Refresh();
    }

    /// <summary>選択して残したファイルを、表示中以外もフル PCM へ。タイルが空／1 レーンのまま残らない。</summary>
    private async Task UpgradeKeptLibrarySessionsForEditorAsync(DocumentSession? active)
    {
        var sessions = _sessions.ToArray();
        foreach (var session in sessions)
        {
            if (IsLibraryMaximized || !_sessions.Contains(session))
            {
                return;
            }

            if (ReferenceEquals(session, active)
                || !LibraryPlayerMode.NeedsEditorPcmUpgrade(session.Document))
            {
                continue;
            }

            await EnsureLibrarySessionLoadedAsync(session, bind: false).ConfigureAwait(true);
        }
    }

    private void CancelLibraryPeakJobs()
    {
        _libraryPeakGeneration++;
        _libraryPeakJobs.Clear();
        foreach (var job in _libraryPeakJobCts.Values)
        {
            try
            {
                job.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _libraryPeakJobCts.Clear();
        var previous = _libraryPeakCts;
        _libraryPeakCts = new CancellationTokenSource();
        previous.Cancel();
        previous.Dispose();
    }

    /// <summary>
    /// 表示中と次曲以外の走査だけ止める。世代は進めない（今の波形を途中で捨てない）。
    /// </summary>
    private void TrimUnwantedLibraryPeakJobs()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        var playing = _activeSession?.Document;
        var next = _gaplessTarget?.Document;
        foreach (var pair in _libraryPeakJobCts)
        {
            if (LibraryPlayerMode.KeepPeakJob(pair.Key, playing, next))
            {
                continue;
            }

            try
            {
                pair.Value.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void UpgradeLibraryPeaksForEditor()
    {
        foreach (var session in _sessions)
        {
            if (!LibraryPlayerMode.NeedsEditorPeakUpgrade(session.Document))
            {
                continue;
            }

            _ = FillLibraryPeaksAsync(session);
        }
    }

    private void RefreshTransportCommandsEnabled() =>
        Transport.SetCommandsEnabled(_document is not null, allowEdit: !IsLibraryMaximized);

    private void ApplyLibraryWaapi(bool show)
    {
        if (show)
        {
            if (!_libraryWaapiSuspended)
            {
                _restoreWaapiAfterLibrary = _waapiPanelVisible;
                _libraryWaapiSuspended = true;
                if (_waapiPanelVisible)
                {
                    _waapiPanelVisible = false;
                    ApplyWaapiPanelVisible();
                }
            }

            SetWaapiToggleEnabled(false);
            return;
        }

        if (!_libraryWaapiSuspended)
        {
            SetWaapiToggleEnabled(true);
            return;
        }

        var restore = _restoreWaapiAfterLibrary;
        _libraryWaapiSuspended = false;
        _restoreWaapiAfterLibrary = false;
        SetWaapiToggleEnabled(true);
        if (restore)
        {
            ShowWaapiPanel();
        }
    }

    private void SetWaapiToggleEnabled(bool enabled)
    {
        if (_waapiToggle is null)
        {
            return;
        }

        _waapiToggle.IsEnabled = enabled;
        _waapiToggle.InvalidateVisual();
    }

    private void ApplyLibraryPlayerHost(bool show)
    {
        if (show)
        {
            if (_tileGrid is not null)
            {
                _tileGrid.Visibility = Visibility.Collapsed;
            }

            SingleWaveformHost.Visibility = Visibility.Visible;
            PrimaryWaveform.Visibility = Visibility.Visible;
            _tileActiveView = null;

            // F9↔F10 のクローム切替で毎回 Bind すると ResetWorkspaceInteraction が再生を止める。
            if (_activeSession is not null)
            {
                var sameDocument = ReferenceEquals(Waveform.Document, _activeSession.Document);
                if (_libraryPlayerHostActive && sameDocument)
                {
                    if (IsPlaybackActive())
                    {
                        AttachPlaybackToActiveWaveform();
                    }

                    ScheduleLibraryWaveformPaint();
                }
                else if (sameDocument && IsPlaybackActive())
                {
                    Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
                    Waveform.ResetTimeZoom();
                    Waveform.ResetAmpZoom();
                    Waveform.LoopEnabled = _activeSession.LoopEnabled;
                    AttachPlaybackToActiveWaveform();
                    ScheduleLibraryWaveformPaint();
                }
                else
                {
                    BindSingleWorkspace(_activeSession);
                }
            }

            _libraryPlayerHostActive = true;
            return;
        }

        _libraryPlayerHostActive = false;
        if (_tileMode && _tileGrid is not null)
        {
            _tileGrid.Visibility = Visibility.Visible;
            if (_activeSession is not null)
            {
                TryBindTiledWorkspace(_activeSession, resetInteraction: false);
            }
        }
    }

    private void RefreshLibraryBrowser()
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        LibraryBrowser.SetSessions(_sessions, _activeSession);
        ShowLibraryArtworkOrClear(_activeSession);
    }

    /// <summary>
    /// プレイヤーを抜けるとき、リストで選んだファイルだけ残す。保存確認でキャンセルしたら false。
    /// </summary>
    private bool KeepOnlyLibrarySelectedSessions()
    {
        var selected = LibraryBrowser.SelectedSessions;
        var current = LibraryBrowser.SelectedSession ?? _activeSession;
        var keep = LibraryPlayerMode.SessionsToKeep(selected, current);
        var drop = LibraryPlayerMode.SessionsToDrop(_sessions, keep);
        if (drop.Length == 0)
        {
            ApplyLibraryHandoffSelection(keep, current);
            return true;
        }

        var dirty = new List<DocumentSession>();
        var clean = new List<DocumentSession>();
        foreach (var session in drop)
        {
            if (session.Document.IsDirty)
            {
                dirty.Add(session);
            }
            else
            {
                clean.Add(session);
            }
        }

        // 未編集は 1 件ずつ CloseSession しない。タブ再構築の繰り返しが再生中に重い。
        if (clean.Count > 0)
        {
            DropLibrarySessionsFast(clean);
        }

        if (dirty.Count > 0)
        {
            var cancelled = false;
            RunCloseBatch(dirty, () =>
            {
                foreach (var session in dirty)
                {
                    if (ReferenceEquals(session, _activeSession))
                    {
                        continue;
                    }

                    if (!CloseSession(session, rememberClosed: !session.Document.IsDeferredLoad))
                    {
                        cancelled = true;
                        return;
                    }
                }

                if (_activeSession is { } active
                    && dirty.Contains(active)
                    && !CloseSession(active, rememberClosed: !active.Document.IsDeferredLoad))
                {
                    cancelled = true;
                }
            });

            if (cancelled)
            {
                return false;
            }
        }

        ApplyLibraryHandoffSelection(keep, current);
        if (_tileMode)
        {
            DropStaleTilePanes();
        }

        return true;
    }

    private void DropLibrarySessionsFast(IReadOnlyList<DocumentSession> drop)
    {
        var dropSet = drop as HashSet<DocumentSession> ?? [.. drop];
        for (var i = _sessions.Count - 1; i >= 0; i--)
        {
            var session = _sessions[i];
            if (!dropSet.Contains(session))
            {
                continue;
            }

            _selectedTabs.Remove(session);
            if (ReferenceEquals(_tabSelectionAnchor, session))
            {
                _tabSelectionAnchor = null;
            }

            if (!session.Document.IsDeferredLoad)
            {
                RememberClosedTab(session, i);
            }

            _sessions.RemoveAt(i);
        }
    }

    private void ApplyLibraryHandoffSelection(
        IReadOnlyList<DocumentSession> keep,
        DocumentSession? current)
    {
        var remaining = new List<DocumentSession>();
        foreach (var session in _sessions)
        {
            foreach (var item in keep)
            {
                if (ReferenceEquals(item, session))
                {
                    remaining.Add(session);
                    break;
                }
            }
        }

        if (current is null || !_sessions.Contains(current))
        {
            current = remaining.Count > 0 ? remaining[0] : _activeSession;
        }

        if (current is not null && !ReferenceEquals(_activeSession, current))
        {
            BindWorkspace(current);
        }

        _selectedTabs.Clear();
        if (remaining.Count >= 2)
        {
            foreach (var session in remaining)
            {
                _selectedTabs.Add(session);
            }
        }

        _tabSelectionAnchor = current;
        RebuildTabBar();
        RefreshTileChrome();
    }

    private void LibraryBrowser_SessionActivated(object? sender, DocumentSession session)
    {
        // フォルダ再帰追加中は、最初に登録した曲以外へ再生を付け替えない。
        if (_libraryFolderPlaySession is not null)
        {
            if (ReferenceEquals(session, _libraryFolderPlaySession))
            {
                ShowLibraryArtwork(session);
            }

            return;
        }

        // 停止中のクリックや選択は再生しない。選択曲の波形だけ出す。再生中だけその曲へ切り替える。
        if (!IsPlaybackActive())
        {
            PreviewLibrarySession(session);
            return;
        }

        _ = PlayLibrarySessionAsync(session);
    }

    private void LibraryBrowser_SessionPlayRequested(object? sender, DocumentSession session)
    {
        _ = PlayLibrarySessionAsync(session);
    }

    private void RegisterLibraryPaths(IReadOnlyList<string> paths) =>
        RegisterLibraryPaths(paths, play: true);

    private void RegisterLibraryPaths(IReadOnlyList<string> paths, bool play)
    {
        // 進行中のフォルダ追加を止めて、起動・ドロップを優先する。
        _libraryFolderShowGeneration++;
        var playFirst = play && (_libraryPlayFirstPending || _sessions.Count == 0);
        DocumentSession? opened = null;
        DocumentSession? existingFirst = null;
        foreach (var path in paths)
        {
            var existing = FindSessionByPath(path);
            if (existing is not null)
            {
                existingFirst ??= existing;
                continue;
            }

            var document = AudioDocument.CreateDeferred(path);
            AudioTagProbe.Ensure(document);
            var session = new DocumentSession(document);
            _sessions.Add(session);
            opened ??= session;
        }

        RebuildTabBar();
        if (!play)
        {
            // 追加だけ。選択も再生中の曲も動かさない（選択変更は SessionActivated で再生し直す）。
            if (IsLibraryMaximized)
            {
                LibraryBrowser.SetSessions(
                    _sessions,
                    LibraryBrowser.SelectedSession,
                    LibraryBrowser.SelectedSessions);
            }

            SyncPlayerMeterFade();
            return;
        }

        if (playFirst)
        {
            SelectAndPlayFirstLibraryTrack();
            _libraryPlayFirstPending = false;
            SyncPlayerMeterFade();
            return;
        }

        var active = LaunchFiles.PreferOpened(opened, existingFirst);
        if (IsLibraryMaximized && active is not null)
        {
            LibraryBrowser.SetSessions(_sessions, active, [active]);
        }

        if (active is not null)
        {
            _ = PlayLibrarySessionAsync(active);
        }

        SyncPlayerMeterFade();
    }

    /// <summary>
    /// リスト選択をプレイリストから外す。ファイルの削除はしない。
    /// 未編集はまとめて外す。1 件ずつ閉じるとタブとリストの再構築が曲数分走る。
    /// </summary>
    private void RemoveLibrarySelectedFromList()
    {
        var selected = LibraryBrowser.SelectedSessions;
        if (selected.Length == 0)
        {
            return;
        }

        foreach (var session in selected)
        {
            if (session.Document.IsDirty)
            {
                RemoveLibrarySelectedFromListAsking(selected);
                return;
            }
        }

        var dropSet = new HashSet<DocumentSession>(selected);
        var removedSessions = new DocumentSession[selected.Length];
        var removedIndices = new int[selected.Length];
        var removedCount = 0;
        foreach (var session in selected)
        {
            var index = _sessions.IndexOf(session);
            if (index < 0)
            {
                continue;
            }

            removedSessions[removedCount] = session;
            removedIndices[removedCount] = index;
            removedCount++;
        }

        if (removedCount == 0)
        {
            return;
        }

        if (removedCount != removedSessions.Length)
        {
            Array.Resize(ref removedSessions, removedCount);
            Array.Resize(ref removedIndices, removedCount);
        }

        var next = NextLibrarySessionAfter(dropSet);
        var resumePlayback = IsPlaybackActive()
            && _activeSession is { } playing
            && dropSet.Contains(playing);
        if (resumePlayback)
        {
            StopPlayback();
        }

        foreach (var session in selected)
        {
            _selectedTabs.Remove(session);
            if (ReferenceEquals(_tabSelectionAnchor, session))
            {
                _tabSelectionAnchor = null;
            }
        }

        _libraryPlaylistUndo.Push(removedSessions, removedIndices);
        _sessions.RemoveAll(dropSet.Contains);
        DetachTabItems(dropSet);

        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);
            RefreshStatus();
            SyncPlayerMeterFade();
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        next ??= _sessions[0];
        LibraryBrowser.SetSessions(_sessions, next, [next]);
        RefreshStatus();
        SyncPlayerMeterFade();
        if (resumePlayback)
        {
            _ = PlayLibrarySessionAsync(next);
            return;
        }

        PreviewLibrarySession(next);
    }

    /// <summary>プレイヤー中の Ctrl+Z。Delete で外した曲を戻す。</summary>
    private bool TryUndoLibraryPlaylistRemove()
    {
        if (!IsLibraryMaximized || !_libraryPlaylistUndo.TryPop(out var sessions, out var indices))
        {
            return false;
        }

        LibraryPlaylistRemoveUndo.RestoreInto(_sessions, sessions, indices);
        LibraryBrowser.SetSessions(_sessions, sessions[0], sessions);
        RefreshStatus();
        SyncPlayerMeterFade();
        if (IsPlaybackActive())
        {
            LibraryBrowser.RequestListFocus();
            return true;
        }

        PreviewLibrarySession(sessions[0]);
        LibraryBrowser.RequestListFocus();
        return true;
    }

    private DocumentSession? NextLibrarySessionAfter(HashSet<DocumentSession> dropSet)
    {
        var anchor = LibraryBrowser.SelectedSession ?? _activeSession;
        if (anchor is null)
        {
            return null;
        }

        var index = _sessions.IndexOf(anchor);
        for (var i = index + 1; i < _sessions.Count; i++)
        {
            if (!dropSet.Contains(_sessions[i]))
            {
                return _sessions[i];
            }
        }

        for (var i = index - 1; i >= 0; i--)
        {
            if (!dropSet.Contains(_sessions[i]))
            {
                return _sessions[i];
            }
        }

        return null;
    }

    private void DetachTabItems(HashSet<DocumentSession> drop)
    {
        for (var i = DocumentTabs.Children.Count - 1; i >= 0; i--)
        {
            if (DocumentTabs.Children[i] is FrameworkElement { Tag: DocumentSession session }
                && drop.Contains(session))
            {
                DocumentTabs.Children.RemoveAt(i);
            }
        }
    }

    private void RemoveLibrarySelectedFromListAsking(DocumentSession[] drop)
    {
        var dropSet = new HashSet<DocumentSession>(drop);
        DocumentSession? next = NextLibrarySessionAfter(dropSet);
        var resumePlayback = IsPlaybackActive()
            && _activeSession is { } playing
            && dropSet.Contains(playing);
        if (resumePlayback)
        {
            StopPlayback();
        }

        var cancelled = false;
        RunCloseBatch(drop, () =>
        {
            foreach (var session in drop)
            {
                if (ReferenceEquals(session, _activeSession))
                {
                    continue;
                }

                if (!CloseSession(session, rememberClosed: false))
                {
                    cancelled = true;
                    return;
                }
            }

            if (_activeSession is { } active
                && dropSet.Contains(active)
                && !CloseSession(active, rememberClosed: false))
            {
                cancelled = true;
            }
        });

        if (cancelled)
        {
            SyncPlayerMeterFade();
            return;
        }

        if (_sessions.Count == 0)
        {
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);
            SyncPlayerMeterFade();
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        next ??= _sessions[0];
        LibraryBrowser.SetSessions(_sessions, next, [next]);
        SyncPlayerMeterFade();
        if (resumePlayback)
        {
            _ = PlayLibrarySessionAsync(next);
            return;
        }

        PreviewLibrarySession(next);
    }

    private void SelectAndPlayFirstLibraryTrack()
    {
        var first = LibraryPlayerMode.FirstSession(_sessions);
        var selected = first is null
            ? (IReadOnlyList<DocumentSession>)[]
            : [first];
        if (first is not null)
        {
            ScanLibraryArtwork(first);
        }

        LibraryBrowser.SetSessions(_sessions, first, selected);
        LibraryBrowser.SetArtwork(first?.Document, keepCurrentIfEmpty: first is null && _libraryHoldJacketWash);
        if (first is not null)
        {
            _libraryHoldJacketWash = false;
            LibraryBrowser.RequestListFocus();
        }

        _ = PlayLibrarySessionAsync(first);
    }

    private void ShowLibraryArtworkOrClear(DocumentSession? session)
    {
        if (session is null)
        {
            LibraryBrowser.SetArtwork(null, keepCurrentIfEmpty: _libraryHoldJacketWash);
            return;
        }

        _libraryHoldJacketWash = false;
        PreviewLibrarySession(session);
    }

    /// <summary>
    /// 停止中に選択曲のジャケットと波形を出す。再生は始めない。
    /// </summary>
    private void PreviewLibrarySession(DocumentSession session)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        ShowLibraryArtwork(session);
        if (IsPlaybackActive())
        {
            return;
        }

        _ = ShowLibrarySessionWaveformAsync(session);
    }

    private async Task ShowLibrarySessionWaveformAsync(DocumentSession session)
    {
        await EnsureLibrarySessionLoadedAsync(session, bind: true).ConfigureAwait(true);
        if (!ReferenceEquals(_libraryLoadSession, session)
            || !IsLibraryMaximized
            || IsPlaybackActive())
        {
            return;
        }

        if (!ReferenceEquals(Waveform.Document, session.Document)
            || !ReferenceEquals(_activeSession, session))
        {
            ApplyLoadedLibrarySession(session);
        }
        else
        {
            ScheduleLibraryWaveformPaint();
        }

        _ = FillLibraryPeaksAsync(session);
    }

    private void ProbeLibraryTags()
    {
        foreach (var session in _sessions)
        {
            AudioTagProbe.Ensure(session.Document);
        }
    }

    private void TryPlayLibraryAfterArrowRelease()
    {
        if (!_libraryPlayOnArrowRelease || !IsLibraryMaximized)
        {
            _libraryPlayOnArrowRelease = false;
            return;
        }

        if (Keyboard.IsKeyDown(Key.Up)
            || Keyboard.IsKeyDown(Key.Down)
            || Keyboard.IsKeyDown(Key.Home)
            || Keyboard.IsKeyDown(Key.End)
            || Keyboard.IsKeyDown(Key.PageUp)
            || Keyboard.IsKeyDown(Key.PageDown))
        {
            return;
        }

        _libraryPlayOnArrowRelease = false;
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (!IsPlaybackActive())
        {
            if (session is not null)
            {
                PreviewLibrarySession(session);
            }

            return;
        }

        _ = PlayLibrarySessionAsync(session);
    }

    private void CancelLibraryGapless()
    {
        _gaplessToken++;
        _gaplessTarget = null;
        _gaplessInFlight = false;
        _gaplessFailed = false;
        _player.ClearGaplessNext();
    }

    private void ScheduleLibraryGaplessPrefetch()
    {
        if (!IsLibraryMaximized
            || !IsPlaybackActive()
            || _activeSession is null
            || _libraryStopAfterTrack)
        {
            return;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null)
        {
            return;
        }

        if (ReferenceEquals(next, _gaplessTarget)
            && (_gaplessInFlight || _gaplessFailed || _player.HasGaplessArmed))
        {
            return;
        }

        _gaplessToken++;
        _player.ClearGaplessNext();
        _gaplessTarget = next;
        _gaplessInFlight = true;
        _gaplessFailed = false;
        var token = _gaplessToken;
        _ = FillLibraryPeaksAsync(next);
        _ = PrefetchLibraryGaplessAsync(next, token);
    }

    private async Task PrefetchLibraryGaplessAsync(DocumentSession next, int token)
    {
        AudioStreamSource? source = null;
        try
        {
            source = await Task.Run(() => OpenLibraryGaplessSource(next)).ConfigureAwait(true);
            if (token != _gaplessToken || source is null)
            {
                source?.Dispose();
                if (token == _gaplessToken && source is null)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            if (!IsLibraryMaximized
                || !IsPlaybackActive()
                || !ReferenceEquals(next, LibraryBrowser.NextPlaylistSession(_activeSession)))
            {
                source.Dispose();
                if (token == _gaplessToken)
                {
                    _gaplessFailed = true;
                }

                return;
            }

            ScanLibraryArtwork(next);
            if (!_player.TryArmGapless(source, next.Document))
            {
                source.Dispose();
                _gaplessFailed = true;
            }
            else
            {
                _ = FillLibraryPeaksAsync(next);
            }
        }
        catch
        {
            source?.Dispose();
            if (token == _gaplessToken)
            {
                _gaplessFailed = true;
            }
        }
        finally
        {
            if (token == _gaplessToken)
            {
                _gaplessInFlight = false;
            }
        }
    }

    private static AudioStreamSource? OpenLibraryGaplessSource(DocumentSession next)
    {
        var document = next.Document;
        var path = document.SourcePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || !AudioCodec.CanStreamPlay(path))
        {
            return null;
        }

        // ActivateStreamPlayback は PCM とピークを捨てる。
        // 1曲のときは次曲も同じ曲なので、エディタで見えていた波形がプレイヤーへ戻すと消える。
        if (!document.IsStreamPlayback
            && document.Interleaved.Length == 0
            && document.Peaks.IsEmpty
            && !AudioCodec.TryActivateStreamPlayback(document))
        {
            return null;
        }

        return AudioStreamSource.Open(path, prebufferTimeoutMs: 200);
    }

    private bool AdoptLibraryGaplessAdvance()
    {
        if (!IsLibraryMaximized || _libraryStopAfterTrack || !_player.HasGaplessAdvancePending)
        {
            return false;
        }

        if (!_player.TryTakeGaplessAdvance(out var document) || document is null)
        {
            return false;
        }

        DocumentSession? session = null;
        foreach (var item in _sessions)
        {
            if (ReferenceEquals(item.Document, document))
            {
                session = item;
                break;
            }
        }

        if (session is null)
        {
            return false;
        }

        _gaplessTarget = null;
        _gaplessFailed = false;
        _activeSession = session;
        Waveform.Document = document;
        Overview.Document = document;
        Waveform.SetAnalysisView(WaveformAnalysisView.Waveform);
        Waveform.ResetTimeZoom();
        Waveform.ResetAmpZoom();
        Waveform.PlayheadFrame = _player.CursorFrame;
        // Document の差し替えが軌跡の記録を止める。再生は続いているので付け直す。
        Waveform.SetTrailRecording(true);
        LibraryBrowser.SelectSessionQuiet(session);
        LibraryBrowser.UpdateSessionRow(session);
        ShowLibraryArtwork(session);
        _ = FillLibraryPeaksAsync(session);
        Transport.SetPlaying(true);
        RefreshTitle();
        RefreshStatus();
        SyncMonitorLayout();
        ScheduleLibraryGaplessPrefetch();
        return true;
    }

    private bool TryAdvanceLibraryPlaylist()
    {
        if (_libraryStopAfterTrack)
        {
            return false;
        }

        var next = LibraryBrowser.NextPlaylistSession(_activeSession);
        if (next is null)
        {
            return false;
        }

        PausePlaybackSoft();
        _ = PlayLibrarySessionAsync(next);
        return true;
    }

    private bool TryStepLibraryPlaylist(int direction)
    {
        var session = direction < 0
            ? LibraryBrowser.PreviousPlaylistSession(_activeSession)
            : LibraryBrowser.NextPlaylistSession(_activeSession);
        if (session is null)
        {
            return false;
        }

        PausePlaybackSoft();
        _ = PlayLibrarySessionAsync(session);
        return true;
    }

    private void ToggleLibraryPlayPause()
    {
        if (IsPlaybackActive())
        {
            PausePlaybackHere();
            return;
        }

        _libraryStopAfterTrack = false;
        if (_document is not null)
        {
            StartPlayback(_document.CursorFrame, prerollSeconds: 0);
            ScheduleLibraryGaplessPrefetch();
            return;
        }

        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    private void RestartLibraryTrack()
    {
        _libraryStopAfterTrack = false;
        var session = _activeSession ?? LibraryBrowser.SelectedSession;
        if (session is not null)
        {
            _ = PlayLibrarySessionAsync(session);
            return;
        }

        if (_document is not null)
        {
            StartPlayback(0, prerollSeconds: 0);
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void SeekLibraryBySeconds(double seconds)
    {
        if (_document is null)
        {
            return;
        }

        var frames = (long)Math.Round(_document.SampleRate * seconds);
        var current = _player.PendingSeekFrame
            ?? (IsPlaybackActive() ? _player.CursorFrame : _document.CursorFrame);
        SeekFrame(
            current + frames,
            IsPlaybackActive() ? LibraryPlayerMode.SeekNudgeFadeMilliseconds : 0);
    }

    private bool BeginOrContinueSeekNudge(int direction)
    {
        if (direction == 0 || _document is null)
        {
            return false;
        }

        if (_seekNudgeDirection == direction && _seekNudgeTimer.IsEnabled)
        {
            return true;
        }

        SeekLibraryBySeconds(
            direction < 0 ? -LibraryPlayerMode.SeekNudgeSeconds : LibraryPlayerMode.SeekNudgeSeconds);
        _seekNudgeDirection = direction;
        _seekNudgeRepeatStarted = false;
        _seekNudgeLastAt = Environment.TickCount64;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
        _seekNudgeTimer.Start();
        return true;
    }

    private void OnSeekNudgeTick()
    {
        if (_seekNudgeDirection == 0 || !IsSeekNudgeHeld(_seekNudgeDirection))
        {
            StopSeekNudge();
            return;
        }

        var now = Environment.TickCount64;
        var seconds = _seekNudgeDirection < 0
            ? -LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted)
            : LibraryPlayerMode.SeekNudgeCatchUpSeconds(now - _seekNudgeLastAt, _seekNudgeRepeatStarted);
        SeekLibraryBySeconds(seconds);

        _seekNudgeLastAt = now;
        if (_seekNudgeRepeatStarted)
        {
            return;
        }

        _seekNudgeRepeatStarted = true;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: true));
        _seekNudgeTimer.Start();
    }

    private static bool IsSeekNudgeHeld(int direction)
    {
        var keyDown = direction < 0
            ? Keyboard.IsKeyDown(Key.NumPad7)
            : Keyboard.IsKeyDown(Key.NumPad9);
        return keyDown && Keyboard.Modifiers == ModifierKeys.None;
    }

    private void StopSeekNudge()
    {
        _seekNudgeDirection = 0;
        _seekNudgeRepeatStarted = false;
        _seekNudgeTimer.Stop();
        _seekNudgeTimer.Interval = TimeSpan.FromMilliseconds(
            LibraryPlayerMode.SeekNudgeTimerIntervalMs(repeatStarted: false));
    }

    private async Task PlayLibrarySessionAsync(DocumentSession? session, bool stopAfterTrack = false)
    {
        CancelLibraryGapless();
        _libraryPlayOnArrowRelease = false;
        _libraryStopAfterTrack = stopAfterTrack;
        if (session is null)
        {
            LibraryBrowser.SetArtwork(null);
            return;
        }

        ShowLibraryArtwork(session);
        await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
        if (session.Document.IsDeferredLoad
            || !ReferenceEquals(_libraryLoadSession, session))
        {
            return;
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.SelectSessionQuiet(session);
        }

        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        ApplyLoadedLibrarySession(session);
        StartPlayback(0, prerollSeconds: 0);
        ShowLibraryArtwork(session);
        if (!stopAfterTrack)
        {
            ScheduleLibraryGaplessPrefetch();
        }

        // 再生とは別ハンドルでピーク走査する（停止待ちしない）。
        _ = FillLibraryPeaksAsync(session);
    }

    /// <summary>
    /// Space。選択曲を先頭から再生し、終わったら停止する。再生中なら開始位置へ戻して止める。
    /// </summary>
    private async void PlayLibrarySelectionOnceOrToggle()
    {
        if (IsPlaybackActive())
        {
            HaltPlaybackToStart();
            _libraryStopAfterTrack = false;
            return;
        }

        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null)
        {
            return;
        }

        await PlayLibrarySessionAsync(session, stopAfterTrack: true).ConfigureAwait(true);
    }

    /// <summary>
    /// トランスポートの再生ボタン。Enter と同じく連続再生（曲末で次へ）。
    /// </summary>
    private void ToggleLibraryTransportPlayback()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        if (IsPlaybackActive())
        {
            HaltPlaybackToStart();
            _libraryStopAfterTrack = false;
            return;
        }

        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    private Task EnsureLibrarySessionLoadedAsync(DocumentSession session, bool bind = true)
    {
        if (bind && IsPlaybackActive() && !ReferenceEquals(session, _activeSession))
        {
            StopPlayback();
        }

        _libraryLoadGeneration++;
        var gen = _libraryLoadGeneration;
        _libraryLoadSession = session;

        if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
        {
            return Task.CompletedTask;
        }

        // プレイヤー中のストリーム再生はフル Load しない。
        if (IsLibraryMaximized
            && session.Document.SourcePath is { Length: > 0 }
            && AudioCodec.CanStreamPlay(session.Document.SourcePath))
        {
            return ActivateStreamLibrarySessionAsync(session, gen);
        }

        // エディタ復帰時はストリームをフル PCM に昇格する。
        if (session.Document.IsStreamPlayback)
        {
            return LoadDeferredLibrarySessionAsync(session, gen, bind);
        }

        if (!session.Document.IsDeferredLoad)
        {
            return Task.CompletedTask;
        }

        return LoadDeferredLibrarySessionAsync(session, gen, bind);
    }

    private async Task ActivateStreamLibrarySessionAsync(DocumentSession session, int generation)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var ok = await Task.Run(() =>
            {
                if (session.Document.IsStreamPlayback
                    && session.Document.FrameCount > 0
                    && session.Document.SampleRate > 0)
                {
                    return true;
                }

                return AudioCodec.TryActivateStreamPlayback(session.Document);
            }).ConfigureAwait(true);
            if (!ok)
            {
                // ストリーム不可なら従来どおりフル展開。
                await LoadDeferredLibrarySessionAsync(session, generation, bind: true).ConfigureAwait(true);
                return;
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            return;
        }

        ApplyLoadedLibrarySession(session);
        // ピークは再生と同時に走らせない（Activate 直後の再生と MF が競合する）。
        if (!IsPlaybackActive())
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    private async Task LoadDeferredLibrarySessionAsync(DocumentSession session, int generation, bool bind = true)
    {
        var path = session.Document.SourcePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var loaded = await Task.Run(() =>
            {
                var document = AudioCodec.Load(path, buildPeaks: false);
                document.ReplacePeaks(
                    PeakPyramid.BuildDisplay(document.Interleaved, document.Channels, document.SampleCount));
                return document;
            }).ConfigureAwait(true);
            if (!session.Document.IsDeferredLoad && !session.Document.IsStreamPlayback)
            {
                return;
            }

            var tags = session.Document.Tags;
            var previousArt = session.Document.Artwork;
            loaded.CursorFrame = Math.Clamp(session.PlayheadFrame, 0, loaded.FrameCount);
            session.ReplaceDocument(loaded);
            loaded.ApplyTags(tags);
            if (!loaded.HasArtwork && previousArt is { Length: > 0 })
            {
                loaded.SetArtwork(previousArt);
            }

            if (loaded.SourcePath is { } openedPath)
            {
                RememberOpenedPath(openedPath);
            }
        }
        catch (Exception ex)
        {
            if (generation == _libraryLoadGeneration && ReferenceEquals(session, _libraryLoadSession))
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    $"{UiStrings.ErrorOpenFailed}\n{Path.GetFileName(path)}: {ex.Message}",
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return;
        }

        if (generation != _libraryLoadGeneration || !ReferenceEquals(session, _libraryLoadSession))
        {
            if (bind || !_sessions.Contains(session))
            {
                return;
            }
        }

        ApplyLoadedLibrarySession(session, bind);
        _ = FillLibraryPeaksAsync(session);
    }

    private async Task FillLibraryPeaksAsync(DocumentSession session)
    {
        var document = session.Document;
        if (document.IsDeferredLoad)
        {
            return;
        }

        var display = IsLibraryMaximized;
        TrimUnwantedLibraryPeakJobs();
        // 途中スナップショット（未走査は 0）が残っている間は、完成までやり直す。
        // プレイヤー包絡（1ch）をエディタで使い続けるとレーンが 1 本のまま。
        if (LibraryPlayerMode.CanReusePeaks(display, document))
        {
            // エディタで作り終えたピークは作り直さない。ただし表示中の波形はプレイヤー帯で描き直す。
            if (display && ReferenceEquals(_activeSession, session))
            {
                Waveform.Refresh();
            }

            return;
        }

        if (!display && document.IsStreamPlayback)
        {
            return;
        }

        if (!_libraryPeakJobs.Add(document))
        {
            return;
        }

        var peakGeneration = _libraryPeakGeneration;
        var pendingPartial = new PeakPyramid?[1];
        var invokePending = 0;
        var jobCts = CancellationTokenSource.CreateLinkedTokenSource(_libraryPeakCts.Token);
        _libraryPeakJobCts[document] = jobCts;
        var token = jobCts.Token;
        var retry = false;
        try
        {
            PeakPyramid peaks;
            if (document.IsStreamPlayback
                && document.SourcePath is { Length: > 0 } path
                && File.Exists(path))
            {
                var dispatcher = Dispatcher;
                peaks = await Task.Run(() => PeakPyramid.BuildPlayerDisplayFromPath(
                        path,
                        onProgress: partial =>
                        {
                            if (peakGeneration != _libraryPeakGeneration)
                            {
                                return;
                            }

                            Volatile.Write(ref pendingPartial[0], partial);
                            if (Interlocked.Exchange(ref invokePending, 1) != 0)
                            {
                                return;
                            }

                            // Normal は Input より先。長いピーク走査中にキーとマウスが飢える。
                            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                            {
                                Interlocked.Exchange(ref invokePending, 0);
                                var snapshot = Volatile.Read(ref pendingPartial[0]);
                                if (snapshot is null || peakGeneration != _libraryPeakGeneration)
                                {
                                    return;
                                }

                                TryApplyLibraryPeaks(session, document, peakGeneration, snapshot, throttlePaint: true);
                            });
                        },
                        token),
                    token).ConfigureAwait(true);
            }
            else
            {
                var interleaved = document.Interleaved;
                var channels = document.Channels;
                var sampleCount = document.SampleCount;
                peaks = await Task.Run(() => display
                        ? PeakPyramid.BuildPlayerDisplay(interleaved, channels, sampleCount)
                        : PeakPyramid.Build(interleaved, channels, sampleCount),
                    token).ConfigureAwait(true);
            }

            var superseded = peakGeneration != _libraryPeakGeneration
                || !ReferenceEquals(session.Document, document);
            if (!superseded && IsLibraryMaximized != display && !document.IsStreamPlayback)
            {
                retry = true;
            }
            else if (!superseded)
            {
                TryApplyLibraryPeaks(session, document, peakGeneration, peaks);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException or OperationCanceledException)
        {
            // ピークだけ失敗しても再生は続ける。
            // 今の曲の走査が次曲の先読みで止まったときは、世代を進めずにやり直す。
            if (ex is OperationCanceledException
                && IsLibraryMaximized
                && _sessions.Contains(session)
                && ReferenceEquals(_activeSession, session)
                && ReferenceEquals(session.Document, document)
                && document.Peaks.IsBuilding)
            {
                retry = true;
            }
        }
        finally
        {
            if (_libraryPeakJobCts.TryGetValue(document, out var tracked)
                && ReferenceEquals(tracked, jobCts))
            {
                _libraryPeakJobCts.Remove(document);
            }

            jobCts.Dispose();
            if (peakGeneration == _libraryPeakGeneration)
            {
                _libraryPeakJobs.Remove(document);
            }
        }

        if (retry)
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    /// <summary>
    /// 同じ曲の、より手前で止まったスナップショットでは置き換えない。
    /// </summary>
    private bool TryApplyLibraryPeaks(
        DocumentSession session,
        AudioDocument document,
        int peakGeneration,
        PeakPyramid peaks,
        bool throttlePaint = false)
    {
        if (peakGeneration != _libraryPeakGeneration
            || !ReferenceEquals(session.Document, document)
            || !IsNewerLibraryPeaks(document.Peaks, peaks))
        {
            return false;
        }

        document.ReplacePeaks(peaks);
        if (document.IsStreamPlayback
            && !peaks.IsEmpty
            && !peaks.IsBuilding
            && peaks.FrameCount > 0
            && peaks.FrameCount != document.FrameCount)
        {
            document.SyncStreamPlaybackMeta(
                document.SampleRate,
                document.Channels,
                document.BitsPerSample,
                peaks.FrameCount);
        }

        RefreshLibrarySessionWaveform(session, throttlePaint);
        return true;
    }

    private void RefreshLibrarySessionWaveform(DocumentSession session, bool throttlePaint)
    {
        WaveformView? view = null;
        if (FindTilePane(session) is { } pane)
        {
            view = pane.View;
        }
        else if (ReferenceEquals(_activeSession, session))
        {
            view = Waveform;
        }

        if (view is null)
        {
            return;
        }

        if (throttlePaint)
        {
            view.RefreshThrottled();
        }
        else
        {
            view.Refresh();
        }

        if (!IsLibraryMaximized && ReferenceEquals(_activeSession, session))
        {
            Overview.Refresh();
        }
    }

    private static bool IsNewerLibraryPeaks(PeakPyramid current, PeakPyramid incoming)
    {
        if (incoming.IsEmpty)
        {
            return false;
        }

        if (current.IsEmpty
            || current.FrameCount != incoming.FrameCount
            || current.Channels != incoming.Channels)
        {
            return true;
        }

        return incoming.FilledFrames >= current.FilledFrames;
    }

    private void ApplyLoadedLibrarySession(DocumentSession session, bool bind = true)
    {
        if (!bind)
        {
            ApplyBackgroundLibrarySession(session);
            return;
        }

        var needsBind = !ReferenceEquals(session, _activeSession)
            || _tileActiveView is not null
            || !ReferenceEquals(Waveform.Document, session.Document);
        if (IsLibraryMaximized)
        {
            if (needsBind)
            {
                BindSingleWorkspace(session);
            }

            LibraryBrowser.UpdateSessionRow(session);
            ScheduleLibraryWaveformPaint();
            return;
        }

        if (needsBind)
        {
            BindWorkspace(session);
        }
    }

    /// <summary>非表示タブ／タイルをフル PCM に差し替える。アクティブへ切り替えない。</summary>
    private void ApplyBackgroundLibrarySession(DocumentSession session)
    {
        if (FindTilePane(session) is { } pane)
        {
            ApplySessionToView(pane.View, session, applyAnalysis: false);
            pane.View.Refresh();
            RefreshTileChrome();
        }

        if (IsLibraryMaximized)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private void ShowLibraryArtwork(DocumentSession session)
    {
        if (!IsLibraryMaximized)
        {
            return;
        }

        var hadArt = session.Document.HasArtwork;
        ScanLibraryArtwork(session);
        _libraryHoldJacketWash = false;
        LibraryBrowser.SetArtwork(session.Document);
        if (!hadArt && session.Document.HasArtwork)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private static void ScanLibraryArtwork(DocumentSession session)
    {
        var document = session.Document;
        if (document.HasArtwork
            || document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path))
        {
            return;
        }

        if (document.SourceKind == AudioFileKind.Mp3)
        {
            if (Id3Artwork.TryRead(path, out var artwork))
            {
                document.SetArtwork(artwork);
            }

            return;
        }

        if (document.SourceKind == AudioFileKind.M4a
            && M4aArtwork.TryRead(path, out var cover))
        {
            document.SetArtwork(cover);
        }
    }

    private void LibraryBrowser_VisibleColumnsChanged(object? sender, IReadOnlyCollection<LibraryFileColumn> columns)
    {
        AppStorage.Settings.ApplyLibraryListColumns(columns);
        AppStorage.Save();
    }

    private void LibraryBrowser_GroupChanged(object? sender, LibraryFileGroup group)
    {
        AppStorage.Settings.ApplyLibraryListGroup(group);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerFolderChanged(object? sender, string path)
    {
        AppStorage.Settings.ApplyLibraryExplorerPath(path);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerExpandedChanged(object? sender, IReadOnlyList<string> paths)
    {
        AppStorage.Settings.ApplyLibraryExplorerExpanded(paths);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerWidthChanged(object? sender, double width)
    {
        AppStorage.Settings.LibraryExplorerWidth = DesignMetrics.ClampLibraryExplorerWidth(width);
        AppStorage.Save();
    }

    private void LibraryBrowser_FavoritesSplitChanged(object? sender, double ratio)
    {
        AppStorage.Settings.LibraryFavoritesSplit = DesignMetrics.ClampLibraryFavoritesSplit(ratio);
        AppStorage.Save();
    }

    private void LibraryBrowser_ShuffleChanged(object? sender, EventArgs e)
    {
        AppStorage.Settings.LibraryShuffle = LibraryBrowser.ShuffleEnabled;
        AppStorage.Save();
        if (IsLibraryMaximized && IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }
    }

    private void LibraryBrowser_FavoritesChanged(object? sender, IReadOnlyList<string> paths)
    {
        AppStorage.Settings.ApplyLibraryFavoritePaths(paths);
        AppStorage.Save();
    }

    private void LibraryBrowser_ClearPlaylistRequested(object? sender, EventArgs e) =>
        RemoveLibrarySelectedFromList();

    private void LibraryBrowser_FavoritesActivated(object? sender, LibraryFavoritesActivateEventArgs e)
    {
        if (e.Paths.Count == 0)
        {
            return;
        }

        if (e.ClearPlaylist)
        {
            _libraryHoldJacketWash = true;
            if (!TryClearLibrarySessions())
            {
                _libraryHoldJacketWash = false;
                return;
            }

            _libraryExplorerPlayOnOpen = true;
        }

        try
        {
            _ = OpenLibraryFoldersRecursiveAsync(e.Paths, LibraryExplorerPlaylistWalk.Inactive);
        }
        finally
        {
            _libraryExplorerPlayOnOpen = false;
        }
    }

    private void LibraryBrowser_ExplorerFoldersOpened(object? sender, IReadOnlyList<string> folders) =>
        _ = OpenLibraryFoldersRecursiveAsync(folders);

    private async void LibraryBrowser_ExplorerFolderOpened(object? sender, string path) =>
        await OpenLibraryFoldersRecursiveAsync([path]).ConfigureAwait(true);

    /// <summary>ツリーの Enter。プレイリストを空にしてから、選んだフォルダ配下を載せる。</summary>
    private void ReplaceLibraryFromExplorerFolder()
    {
        if (LibraryBrowser.SelectedExplorerFolders.Length == 0)
        {
            return;
        }

        _libraryHoldJacketWash = true;
        if (!TryClearLibrarySessions())
        {
            _libraryHoldJacketWash = false;
            return;
        }

        _libraryExplorerPlayOnOpen = true;
        try
        {
            LibraryBrowser.OpenSelectedFolder();
        }
        finally
        {
            _libraryExplorerPlayOnOpen = false;
        }
    }

    /// <summary>
    /// フォルダ配下（と単体ファイル）を再帰収集し、1 曲ずつ載せる。
    /// 再生するのは Enter（クリア後）だけ。Shift+Enter・ダブルクリック・追加メニューは再生も停止もしない。
    /// 再生が始まる追加（Enter）だけ、最初の曲を載せたときにプレイリストへフォーカスする。
    /// 検索中のツリーは見えているフォルダだけ。ファイル名ヒットはそのファイル、フォルダ名ヒットは配下すべて。
    /// お気に入りは検索フィルタを掛けない。
    /// </summary>
    private async Task OpenLibraryFoldersRecursiveAsync(
        IReadOnlyList<string> folders,
        LibraryExplorerPlaylistWalk? walk = null)
    {
        var playFirst = _libraryExplorerPlayOnOpen;
        _libraryExplorerPlayOnOpen = false;
        var generation = ++_libraryFolderShowGeneration;
        _libraryFolderPlaySession = null;
        var filter = walk ?? LibraryBrowser.SnapshotExplorerPlaylistWalk();
        var remaining = new Stack<(string Path, bool AncestorHit)>();
        for (var i = folders.Count - 1; i >= 0; i--)
        {
            try
            {
                remaining.Push((Path.GetFullPath(folders[i]), false));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
            }
        }

        if (remaining.Count == 0)
        {
            ReleaseHeldLibraryJacket();
            return;
        }

        _libraryPlayFirstPending = false;
        var firstHandled = false;
        async Task<bool> AppendFoundAsync(string path)
        {
            var first = !firstHandled;
            var select = playFirst && first;
            if (!await TryAppendLibrarySessionAsync(path, generation, select, play: select)
                    .ConfigureAwait(true))
            {
                return false;
            }

            if (first && playFirst)
            {
                LibraryBrowser.RequestListFocus();
            }

            firstHandled = true;
            return true;
        }

        try
        {
            while (remaining.Count > 0)
            {
                if (generation != _libraryFolderShowGeneration)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    return;
                }

                if (!IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    _libraryHoldJacketWash = false;
                    return;
                }

                var current = remaining.Pop();
                if (!Directory.Exists(current.Path))
                {
                    if (File.Exists(current.Path)
                        && AudioCodec.IsPlayerOpenable(current.Path)
                        && filter.IncludeFile(current.Path, current.AncestorHit)
                        && FindSessionByPath(current.Path) is null)
                    {
                        if (!await AppendFoundAsync(current.Path).ConfigureAwait(true))
                        {
                            LibraryBrowser.FinishIncrementalSessionLoad();
                            return;
                        }
                    }

                    continue;
                }

                var layer = await Task.Run(() =>
                {
                    var folderHit = filter.FolderNameHit(current.Path, current.AncestorHit);
                    AudioCodec.CollectPlayerOpenableDirectoryLayer(current.Path, out var files, out var children);
                    var included = new List<string>();
                    foreach (var file in files)
                    {
                        if (filter.IncludeFile(file, folderHit))
                        {
                            included.Add(file);
                        }
                    }

                    var next = new List<(string Path, bool AncestorHit)>();
                    foreach (var child in children)
                    {
                        if (filter.IncludeChild(child, folderHit))
                        {
                            next.Add((child, folderHit));
                        }
                    }

                    return (files: included.ToArray(), children: next.ToArray());
                }).ConfigureAwait(true);

                if (generation != _libraryFolderShowGeneration)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    return;
                }

                if (!IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    _libraryHoldJacketWash = false;
                    return;
                }

                foreach (var file in layer.files)
                {
                    if (FindSessionByPath(file) is not null)
                    {
                        continue;
                    }

                    if (!await AppendFoundAsync(file).ConfigureAwait(true))
                    {
                        LibraryBrowser.FinishIncrementalSessionLoad();
                        return;
                    }
                }

                for (var i = layer.children.Length - 1; i >= 0; i--)
                {
                    remaining.Push(layer.children[i]);
                }
            }

            if (generation == _libraryFolderShowGeneration && IsLibraryMaximized)
            {
                LibraryBrowser.FinishIncrementalSessionLoad();
                if (firstHandled)
                {
                    _libraryHoldJacketWash = false;
                    if (playFirst && _libraryFolderPlaySession is { } first)
                    {
                        LibraryBrowser.SelectSessionQuiet(first);
                    }
                }
                else
                {
                    ReleaseHeldLibraryJacket();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (generation == _libraryFolderShowGeneration)
            {
                ReleaseHeldLibraryJacket();
            }
        }
        finally
        {
            if (generation == _libraryFolderShowGeneration)
            {
                _libraryFolderPlaySession = null;
            }
        }
    }

    /// <summary>
    /// 1 曲を裏でタグ読みしてリストへ追加。generation が変わったら false（途中キャンセル）。
    /// </summary>
    private async Task<bool> TryAppendLibrarySessionAsync(
        string file,
        int generation,
        bool select,
        bool play)
    {
        if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
        {
            return false;
        }

        var session = await Task.Run(() =>
        {
            var document = AudioDocument.CreateDeferred(file);
            AudioTagProbe.Ensure(document);
            return new DocumentSession(document);
        }).ConfigureAwait(true);

        if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
        {
            return false;
        }

        _sessions.Add(session);
        if (play)
        {
            _libraryFolderPlaySession = session;
        }

        if (select)
        {
            ScanLibraryArtwork(session);
        }

        LibraryBrowser.AppendSession(session, select);
        SyncPlayerMeterFade();
        if (select)
        {
            _libraryHoldJacketWash = false;
            LibraryBrowser.SetArtwork(session.Document);
            if (play)
            {
                _ = PlayLibrarySessionAsync(session);
            }
        }

        // 1 件ごとに UI へ制御を返す（キー連打・描画を止めない）。
        await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
        return true;
    }

    private void ReleaseHeldLibraryJacket()
    {
        if (!_libraryHoldJacketWash)
        {
            return;
        }

        _libraryHoldJacketWash = false;
        if (_sessions.Count == 0 && IsLibraryMaximized)
        {
            LibraryBrowser.SetArtwork(null);
            ActivateLibraryExplorerIfPlaylistEmpty();
        }
    }

    /// <summary>
    /// プレイリストが空のままならライブラリ（フォルダツリー）をアクティブにする。
    /// クリア直後に再追加する途中（ジャケット保持中）は呼ばない。
    /// </summary>
    private void ActivateLibraryExplorerIfPlaylistEmpty()
    {
        if (!IsLibraryMaximized || _sessions.Count != 0 || _libraryHoldJacketWash)
        {
            return;
        }

        LibraryBrowser.RequestExplorerFocus();
    }

    /// <summary>F10 突入／ミニマム解除。空ならツリー、曲があればプレイリスト。</summary>
    private void FocusLibraryPaneForPlaylist()
    {
        if (!IsLibraryMaximized || _libraryMinimalChrome)
        {
            return;
        }

        if (_sessions.Count == 0)
        {
            ActivateLibraryExplorerIfPlaylistEmpty();
            return;
        }

        LibraryBrowser.RequestListFocus();
    }

    private bool TryClearLibrarySessions()
    {
        if (_sessions.Count == 0)
        {
            if (IsLibraryMaximized)
            {
                LibraryBrowser.SetSessions(_sessions, null);
            }

            return true;
        }

        CancelLibraryGapless();
        CancelLibraryPeakJobs();
        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        // 未編集だけなら1件ずつ閉じない（タブ再構築の繰り返しが重い）。まとめて消す。
        var allClean = true;
        foreach (var session in _sessions)
        {
            if (session.Document.IsDirty)
            {
                allClean = false;
                break;
            }
        }

        if (allClean)
        {
            _selectedTabs.Clear();
            _tabSelectionAnchor = null;
            _sessions.Clear();
            BindWorkspace(null);
            RebuildTabBar();
            NotifyWaveformSessionsChanged();
            SyncPlayerMeterFade();
            return true;
        }

        var drop = _sessions.ToArray();
        var cancelled = false;
        RunCloseBatch(drop, () =>
        {
            foreach (var session in drop)
            {
                if (ReferenceEquals(session, _activeSession))
                {
                    continue;
                }

                if (!CloseSession(session, rememberClosed: !session.Document.IsDeferredLoad))
                {
                    cancelled = true;
                    return;
                }
            }

            if (_activeSession is { } active
                && Array.IndexOf(drop, active) >= 0
                && !CloseSession(active, rememberClosed: !active.Document.IsDeferredLoad))
            {
                cancelled = true;
            }
        });

        if (!cancelled && IsLibraryMaximized)
        {
            LibraryBrowser.SetSessions(_sessions, _activeSession);
        }

        SyncPlayerMeterFade();
        return !cancelled;
    }
}
