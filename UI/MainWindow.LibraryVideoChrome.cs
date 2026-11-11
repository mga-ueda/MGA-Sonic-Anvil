using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Channels;
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
    private void SyncPlaylistVideoChromeFade()
    {
        SyncPlayerScratchEnabled();

        var hide = LibraryPlayerMode.HidesChromeForPlaylistVisual(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualPlaying,
            LibraryBrowser.PlaylistVisualIsPdf,
            LibraryBrowser.PlaylistVisualIsVideo,
            _playlistVisualChromeBridge,
            _playlistVideoImmersivePause,
            simpleSeekScrubHold: IsSimpleSeekChromeHoldActive());
        if (LibraryPlayerMode.HidesChromeForVideoFileLaunch(_videoLaunchPlacement, IsLibraryMaximized))
        {
            hide = true;
        }

        ApplySilentSkipFromSettings();

        if (!hide)
        {
            // F 全画面はここでは解除しない（ユーザーの F でのみ戻す）。
            EnsurePlaylistVideoWindowRestored();
        }

        // PDF 表示中は A／T を出さないだけ（フラグは触らない。本再生もミニも保存した A／T を使う）。
        var hideAtOverlays = LibraryPlayerMode.HidesPlaylistVideoChromeOverlaysForPdf(
                LibraryBrowser.PlaylistVisualIsPdf,
                hide)
            || (hide && !_playlistVideoHud);

        var visible = !hide;
        var instant = !IsLibraryMaximized
            || LibraryPlayerMode.HidesChromeForVideoFileLaunch(_videoLaunchPlacement, IsLibraryMaximized);
        if (_playlistVideoChromeVisible != visible || instant)
        {
            _playlistVideoChromeVisible = visible;
            LibraryPlayerMode.FadeElementOpacity(RootDock, 1, instant: true, hitTestVisible: true);
            foreach (var target in PlaylistVideoChromeTargets())
            {
                // ステータスは下余白になるので Opacity ではなく Collapse で消す（動画ミニと同じ）。
                if (ReferenceEquals(target, StatusBarHost))
                {
                    continue;
                }

                LibraryPlayerMode.FadeElementOpacity(
                    target,
                    visible ? 1 : 0,
                    instant,
                    hitTestVisible: visible,
                    seconds: LibraryPlayerMode.ChromeFadeSeconds);
            }

            // メニューバーは領域を残したまま見た目だけ合わせる（Collapsed にしない）。
            SyncMainMenuBarChromeOpacity(instant);
        }
        else if (visible)
        {
            // 状態フラグは見えるのに Opacity が固まったまま、を起こさない。
            // フェードイン中の再 Sync（DimPreview の StateChanged ＋直後の Sync など）では触らない。
            EnsurePlaylistVideoChromeOpaque();
        }

        ApplyVideoOverlayBottomFlush(hide);
        ApplyPlaylistVideoHudFade(hideAtOverlays, instant);
        ApplyPlayerWaveformDockBesideLoudness();
        if (IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo)
        {
            EnsureLibraryVisualPlayheadTicker();
        }

        ApplyPlaylistVideoTimecodeChrome(instant);
        SyncPlaylistTrackIndexToast(
            LibraryPlayerMode.ShowsPlaylistTrackIndex(
                hide,
                LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf,
                _sessions.Count));
        // PDF ページ番号は常時出さない。本表示を抜けたら隠し、切替時だけトーストする。
        if (!LibraryPlayerMode.ShowsPlaylistPdfPageNumber(hide, LibraryBrowser.PlaylistVisualIsPdf))
        {
            HidePlaylistPdfPageNumber();
        }
        if (!hide)
        {
            EnsurePlaylistVideoHudOpaque();
        }
        else if (_playlistVideoHud && LibraryBrowser.PlaylistVisualIsVideo)
        {
            // メーターだけ戻して波形が Opacity 0 のまま残るのを防ぐ（PDF では出さない）。
            EnsurePlaylistVideoHudOpaque();
        }

        SyncPlaylistVideoUiShadows(
            !hide,
            hideAtOverlays && LibraryBrowser.PlaylistVisualIsVideo);
        SyncPlaylistVisualCursorHide();
    }

    private void EnsurePlaylistVideoWindowRestored()
    {
        // F 全画面中はウィンドウ寸法も触らない（停止やクローム復帰で勝手に戻さない）。
        if (_playlistVideoFullscreen)
        {
            return;
        }

        // レイアウト全画面（F11／F12 等）のままプレイヤーへ移るときはタイトルバーを出さない。
        if (IsWaveformMaximized)
        {
            return;
        }

        if (!IsLibraryMaximized)
        {
            return;
        }

        if (WindowStyle == WindowStyle.None)
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        }

        // 全画面のモニター寸法が settings に残っていると、ステータスバーがタスクバー下に隠れる。
        if (IsLikelyExclusiveFullscreenSize())
        {
            RestorePlaylistVideoWindowBounds();
        }
    }

    private void RestorePlaylistVideoWindowBounds()
    {
        WindowState = WindowState.Normal;
        var bounds = _boundsBeforeVideoFullscreen;
        if (bounds.Width >= MinWidth
            && bounds.Height >= MinHeight
            && !IsMonitorCoveringBounds(bounds))
        {
            Left = bounds.X;
            Top = bounds.Y;
            Width = Math.Max(MinWidth, bounds.Width);
            Height = Math.Max(MinHeight, bounds.Height);
            if (_windowStateBeforeVideoFullscreen == WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }

            PersistCurrentWindowPlacement();
            return;
        }

        if (TryApplyCurrentModePlacement() && !IsLikelyExclusiveFullscreenSize())
        {
            PersistCurrentWindowPlacement();
            return;
        }

        FitPlayerToWorkArea();
        PersistCurrentWindowPlacement();
    }

    private bool IsLikelyExclusiveFullscreenSize()
    {
        if (WindowState == WindowState.Maximized)
        {
            return false;
        }

        if (!WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            return false;
        }

        return Width >= monitor.Width - 4 && Height >= monitor.Height - 4;
    }

    private bool IsMonitorCoveringBounds(Rect bounds)
    {
        if (WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            return bounds.Width >= monitor.Width - 4 && bounds.Height >= monitor.Height - 4;
        }

        return bounds.Width >= SystemParameters.PrimaryScreenWidth - 4
            && bounds.Height >= SystemParameters.PrimaryScreenHeight - 4;
    }

    private void FitPlayerToWorkArea()
    {
        var work = SystemParameters.WorkArea;
        if (WindowPlacement.TryGetContainingMonitorDip(this, out var monitor))
        {
            // モニター全面ではなく作業領域相当（タスクバー分を引いた高さ）に収める。
            work = new Rect(
                monitor.X,
                monitor.Y,
                monitor.Width,
                Math.Max(MinHeight, monitor.Height - 48));
        }

        WindowState = WindowState.Normal;
        Width = Math.Max(MinWidth, work.Width * 0.9);
        Height = Math.Max(MinHeight, work.Height * 0.9);
        Left = work.X + Math.Max(0, (work.Width - Width) * 0.5);
        Top = work.Y + Math.Max(0, (work.Height - Height) * 0.5);
    }

    private void EnsurePlaylistVideoChromeOpaque()
    {
        LibraryPlayerMode.FadeElementOpacity(RootDock, 1, instant: true, hitTestVisible: true);
        foreach (var target in PlaylistVideoChromeTargets())
        {
            if (ReferenceEquals(target, StatusBarHost))
            {
                continue;
            }

            // フェードイン中は NeedsForcedOpaque が false（本再生→暗いプレビュー復帰の 0.2s を守る）。
            if (LibraryPlayerMode.NeedsForcedOpaque(target))
            {
                LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
            }
        }

        RestoreStatusBarAfterVideoOverlay();
        ApplyMainMenuBarVisibility();
    }

    /// <summary>
    /// 動画オーバーレイ中はステータスを畳み、アナライザ／ピークを窓下端へ揃える。
    /// 動画ミニと同じく、透明のまま高さが残る余白を作らない。
    /// </summary>
    private void ApplyVideoOverlayBottomFlush(bool chromeHidden)
    {
        var flush = LibraryPlayerMode.FlushesAnalyzersToWindowBottom(
            libraryMaximized: IsLibraryMaximized,
            videoMini: IsVideoMiniPlayerActive(),
            playlistVideoFullscreen: _playlistVideoFullscreen,
            visualShown: LibraryBrowser.PlaylistVisualShown,
            isVideo: LibraryBrowser.PlaylistVisualIsVideo,
            chromeHidden: chromeHidden);

        if (flush)
        {
            StatusBarHost.BeginAnimation(UIElement.OpacityProperty, null);
            StatusBarHost.Opacity = 1;
            StatusBarHost.IsHitTestVisible = false;
            StatusBarHost.Visibility = Visibility.Collapsed;
            ApplyAnalyzerBottomFlush(AnalyzerLayoutScale);
            SchedulePlaylistVideoFileNamePlacement();
            return;
        }

        RestoreStatusBarAfterVideoOverlay();
        if (!IsVideoMiniPlayerActive())
        {
            ClearAnalyzerBottomFlush();
        }

        SchedulePlaylistVideoFileNamePlacement();
    }

    private void RestoreStatusBarAfterVideoOverlay()
    {
        if (IsVideoMiniPlayerActive() || _playlistVideoFullscreen)
        {
            StatusBarHost.Visibility = Visibility.Collapsed;
            return;
        }

        var wantStatusBar = _activeLayoutSlot is { } statusSlot
            ? AppStorage.Settings.ResolvedLayoutPreset(statusSlot).ResolvedShowStatusBar(statusSlot)
            : true;
        if (wantStatusBar
            && StatusBarHost.Visibility != Visibility.Visible
            && !_libraryMinimalChrome)
        {
            StatusBarHost.BeginAnimation(UIElement.OpacityProperty, null);
            StatusBarHost.Opacity = 1;
            StatusBarHost.IsHitTestVisible = true;
            StatusBarHost.Visibility = Visibility.Visible;
        }
        else if (!wantStatusBar)
        {
            StatusBarHost.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>スペアナ／ラウドネス／モニター／ピークを下段帯の下端へ揃える。</summary>
    private void ApplyAnalyzerBottomFlush(double layoutScale)
    {
        Spectrum.VerticalAlignment = VerticalAlignment.Bottom;
        LoudnessMeter.VerticalAlignment = VerticalAlignment.Bottom;
        MonitorToggles.VerticalAlignment = VerticalAlignment.Bottom;
        ApplyLevelMeterHeightCap(layoutScale);
    }

    private void ClearAnalyzerBottomFlush()
    {
        Spectrum.VerticalAlignment = VerticalAlignment.Top;
        LoudnessMeter.VerticalAlignment = VerticalAlignment.Stretch;
        MonitorToggles.VerticalAlignment = VerticalAlignment.Center;
        ClearLevelMeterHeightCap();
    }

    private void EnsurePlaylistVideoHudOpaque()
    {
        foreach (var target in PlaylistVideoHudTargets())
        {
            // 波形はメーター非表示条件でも HUD オンなら出す。WAAPI／アナライザは空リストでは出さない。
            if (ReferenceEquals(target, WaveformTileHost)
                || LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count))
            {
                if (LibraryPlayerMode.NeedsForcedOpaque(target))
                {
                    LibraryPlayerMode.FadeElementOpacity(target, 1, instant: true, hitTestVisible: true);
                }
            }
        }
    }

    private void ApplyPlaylistVideoHudFade(bool hideHud, bool instant)
    {
        var showMeters = LibraryPlayerMode.ShowsPlayerMeters(_sessions.Count);
        foreach (var target in PlaylistVideoHudTargets())
        {
            // 空リストの WAAPI／アナライザは SyncPlayerMeterFade（アウト 1 秒）に任せる。
            // ここで 0.2 秒フェードすると、先に始まった 1 秒アウトを潰す。
            if (IsPlayerMeterHudTarget(target) && !showMeters)
            {
                continue;
            }

            LibraryPlayerMode.FadeElementOpacity(
                target,
                hideHud ? 0 : 1,
                instant,
                hitTestVisible: !hideHud,
                seconds: LibraryPlayerMode.ChromeFadeSeconds);
        }
    }

    private bool IsPlayerMeterHudTarget(UIElement target) =>
        ReferenceEquals(target, MeterTopSlot)
        || ReferenceEquals(target, LevelMeter)
        || ReferenceEquals(target, VectorScope)
        || ReferenceEquals(target, Spectrum)
        || ReferenceEquals(target, LoudnessMeter)
        || ReferenceEquals(target, MonitorToggles);

    private bool TryTogglePlaylistVideoFullscreen()
    {
        if (!IsLibraryMaximized)
        {
            return false;
        }

        // 解除は映像の有無にかかわらず受け付ける（再生終了後に全画面が残ったとき用）。
        if (_playlistVideoFullscreen)
        {
            SetPlaylistVideoFullscreen(false);
            return true;
        }

        if (!LibraryBrowser.PlaylistVisualShown
            || !(LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf))
        {
            return false;
        }

        SetPlaylistVideoFullscreen(true);
        return true;
    }

    /// <summary>
    /// 動画／PDF の本再生（明るい表示）／ミニ中のダブルクリックは
    /// フル（タイトルバー無しの F 全画面）⇔ミニ（F8）切替。
    /// 暗いプレビュー上は F キーと同じ全画面オン／オフ。操作系 UI 上は無視。
    /// </summary>
    private void MainWindow_PreviewMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.Handled)
        {
            return;
        }

        if (LibraryPlayerMode.BlocksVideoFullscreenDoubleClick(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (LibraryPlayerMode.PrefersVisualFullMiniToggleOnDoubleClick(
                IsVideoRealPlayback() || IsPdfBrightDisplay(),
                IsVideoMiniPlayerActive()))
        {
            if (TryTogglePlaylistVisualFullAndMini())
            {
                e.Handled = true;
            }

            return;
        }

        if (TryTogglePlaylistVideoFullscreen())
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// フル（タイトルバー無し）⇔ミニ。全画面中はミニへ、それ以外はフルへ。
    /// </summary>
    private bool TryTogglePlaylistVisualFullAndMini()
    {
        if (LibraryPlayerMode.PrefersEnterMiniOnVisualFullMiniDoubleClick(_playlistVideoFullscreen))
        {
            if (IsVideoMiniPlayerActive())
            {
                // ミニ＋全画面 → 全画面だけ外してミニ窓へ。
                SetPlaylistVideoFullscreen(false);
                return true;
            }

            if (!LibraryPlayerMode.CanToggleVideoMiniPlayer(
                    alreadyActive: false,
                    IsVideoMiniEnterEligible(),
                    hasPlayableSession: _sessions.Count > 0))
            {
                return false;
            }

            // 全画面の本再生 → ミニ（Enter 側で全画面を先に抜ける）。
            ToggleVideoMiniPlayer();
            return true;
        }

        // ミニまたは窓付き本再生 → フル（タイトルバー無し）。
        if (!IsLibraryMaximized
            || !(LibraryBrowser.PlaylistVisualShown
                && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf)))
        {
            return false;
        }

        SetPlaylistVideoFullscreen(true);
        return true;
    }

    private bool TryTogglePlaylistVideoHud()
    {
        if (!IsPlaylistVisualHudContext())
        {
            return false;
        }

        _playlistVideoHud = !_playlistVideoHud;
        ApplyPlaylistVideoHudFade(!_playlistVideoHud, instant: false);
        ApplyPlaylistVideoTimecodeChrome(instant: false);
        SyncPlaylistVideoUiShadows(
            _playlistVideoChromeVisible,
            IsPlaylistVisualHudContext() && !_playlistVideoHud);
        RememberVideoChrome();
        return true;
    }

    private bool TryTogglePlaylistVideoTimecode()
    {
        if (!IsPlaylistVisualHudContext())
        {
            return false;
        }

        _playlistVideoTimecode = !_playlistVideoTimecode;
        ApplyPlaylistVideoTimecodeChrome(instant: false);
        RememberVideoChrome();
        return true;
    }

    /// <summary>T でまとめて出すもの。タイムコードとファイル名。簡易シークバーは A オフで別判定。</summary>
    private void ApplyPlaylistVideoTimecodeChrome(bool instant)
    {
        var chromeHidden = !_playlistVideoChromeVisible;
        var video = LibraryBrowser.PlaylistVisualIsVideo;
        ApplyPlaylistVideoTimecodeFade(
            LibraryPlayerMode.ShowsPlaylistVideoTimecode(
                chromeHidden,
                _playlistVideoTimecode,
                video));
        ApplyPlaylistVideoFileNameFade(
            LibraryPlayerMode.ShowsPlaylistVideoFileName(
                chromeHidden,
                _playlistVideoTimecode,
                video),
            instant);
        ApplyPlaylistVideoSimpleSeekFade(
            LibraryPlayerMode.ShowsPlaylistVideoSimpleSeek(
                chromeHidden,
                _playlistVideoHud,
                video),
            instant);
    }

    private bool IsPlaylistVideoSimpleSeekArmed() =>
        LibraryPlayerMode.ShowsPlaylistVideoSimpleSeek(
            !_playlistVideoChromeVisible,
            _playlistVideoHud,
            LibraryBrowser.PlaylistVisualIsVideo);

    private bool _playlistVideoSimpleSeekWantVisible;
    private bool _playlistVideoSimpleSeekWasArmed;
    private long _playlistVideoSimpleSeekKeyboardUntil;
    private DispatcherTimer? _playlistVideoSimpleSeekKeyboardTimer;
    private DispatcherTimer? _playlistVideoSimpleSeekHitTestOffTimer;

    private void ApplyPlaylistVideoSimpleSeekFade(bool show, bool instant)
    {
        if (!show)
        {
            _playlistVideoSimpleSeekWasArmed = false;
            _playlistVideoSimpleSeekKeyboardUntil = 0;
            _playlistVideoSimpleSeekKeyboardTimer?.Stop();
            SetPlaylistVideoSimpleSeekVisible(false, instant);
            return;
        }

        PlaylistVideoSimpleSeek.Margin = DesignMetrics.PlaylistVideoSimpleSeekMargin(VideoMiniOverlayLayoutScale);
        SyncPlaylistVideoSimpleSeekRatio();
        var revealNow = !_playlistVideoSimpleSeekWasArmed;
        _playlistVideoSimpleSeekWasArmed = true;
        if (revealNow)
        {
            // A でアナライザを隠した瞬間は、マウスを待たずすぐ出す。
            NotePlaylistVideoSimpleSeekKeyboard(instant: true);
            return;
        }

        RefreshPlaylistVideoSimpleSeekPresence(instant);
    }

    /// <summary>マウス移動かキーボードの時間移動から 3 秒以内、またはドラッグ中なら出す。</summary>
    private void RefreshPlaylistVideoSimpleSeekPresence(bool instant = false)
    {
        if (!IsPlaylistVideoSimpleSeekArmed())
        {
            _playlistVideoSimpleSeekWasArmed = false;
            _playlistVideoSimpleSeekKeyboardUntil = 0;
            _playlistVideoSimpleSeekKeyboardTimer?.Stop();
            SetPlaylistVideoSimpleSeekVisible(false, instant);
            return;
        }

        var active = Environment.TickCount64 < _playlistVideoSimpleSeekKeyboardUntil;
        SetPlaylistVideoSimpleSeekVisible(
            active || PlaylistVideoSimpleSeek.IsDragging,
            instant);
    }

    private void SetPlaylistVideoSimpleSeekVisible(bool visible, bool instant)
    {
        if (_playlistVideoSimpleSeekWantVisible == visible && !instant)
        {
            // フェードアウト Completed が遅延でフェードインを潰すと、意図は表示のまま Opacity 0 に固まる。
            // そのときは再フェードする。ヒットだけ戻すケースも残す。
            if (visible && LibraryPlayerMode.NeedsForcedOpaque(PlaylistVideoSimpleSeek))
            {
                // fall through
            }
            else
            {
                if (PlaylistVideoSimpleSeek.IsHitTestVisible != visible)
                {
                    PlaylistVideoSimpleSeek.IsHitTestVisible = visible;
                }

                return;
            }
        }

        _playlistVideoSimpleSeekWantVisible = visible;
        if (visible)
        {
            _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
            SyncPlaylistVideoSimpleSeekRatio();
        }

        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoSimpleSeek,
            visible ? 1 : 0,
            instant,
            // フェードアウト中もドラッグできるように、消えるまでヒットを残す。
            hitTestVisible: visible || instant ? visible : null,
            seconds: visible
                ? LibraryPlayerMode.ChromeFadeSeconds
                : LibraryPlayerMode.PlaylistVideoSimpleSeekFadeOutSeconds);
        if (!visible && !instant)
        {
            SchedulePlaylistVideoSimpleSeekHitTestOff();
        }
        else if (!visible)
        {
            _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
        }
    }

    private void SchedulePlaylistVideoSimpleSeekHitTestOff()
    {
        if (_playlistVideoSimpleSeekWantVisible)
        {
            return;
        }

        _playlistVideoSimpleSeekHitTestOffTimer ??= new DispatcherTimer();
        _playlistVideoSimpleSeekHitTestOffTimer.Stop();
        _playlistVideoSimpleSeekHitTestOffTimer.Interval = TimeSpan.FromSeconds(
            LibraryPlayerMode.PlaylistVideoSimpleSeekFadeOutSeconds);
        _playlistVideoSimpleSeekHitTestOffTimer.Tick -= OnPlaylistVideoSimpleSeekHitTestOffTick;
        _playlistVideoSimpleSeekHitTestOffTimer.Tick += OnPlaylistVideoSimpleSeekHitTestOffTick;
        _playlistVideoSimpleSeekHitTestOffTimer.Start();
    }

    private void OnPlaylistVideoSimpleSeekHitTestOffTick(object? sender, EventArgs e)
    {
        _playlistVideoSimpleSeekHitTestOffTimer?.Stop();
        if (_playlistVideoSimpleSeekWantVisible || PlaylistVideoSimpleSeek.IsDragging)
        {
            return;
        }

        PlaylistVideoSimpleSeek.IsHitTestVisible = false;
    }

    private void NotePlaylistVideoSimpleSeekKeyboard(bool instant = false)
    {
        if (!IsPlaylistVideoSimpleSeekArmed())
        {
            return;
        }

        _playlistVideoSimpleSeekKeyboardUntil = Environment.TickCount64
            + (long)(LibraryPlayerMode.PlaylistVideoSimpleSeekKeyboardHoldSeconds * 1000);
        _playlistVideoSimpleSeekKeyboardTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistVideoSimpleSeekKeyboardHoldSeconds),
        };
        _playlistVideoSimpleSeekKeyboardTimer.Stop();
        _playlistVideoSimpleSeekKeyboardTimer.Tick -= OnPlaylistVideoSimpleSeekKeyboardTick;
        _playlistVideoSimpleSeekKeyboardTimer.Tick += OnPlaylistVideoSimpleSeekKeyboardTick;
        _playlistVideoSimpleSeekKeyboardTimer.Start();
        RefreshPlaylistVideoSimpleSeekPresence(instant);
    }

    private void OnPlaylistVideoSimpleSeekKeyboardTick(object? sender, EventArgs e)
    {
        _playlistVideoSimpleSeekKeyboardTimer?.Stop();
        var remain = _playlistVideoSimpleSeekKeyboardUntil - Environment.TickCount64;
        if (remain > 0 && _playlistVideoSimpleSeekKeyboardTimer is not null)
        {
            _playlistVideoSimpleSeekKeyboardTimer.Interval = TimeSpan.FromMilliseconds(remain);
            _playlistVideoSimpleSeekKeyboardTimer.Start();
            return;
        }

        RefreshPlaylistVideoSimpleSeekPresence();
    }

    private void OnPlaylistVideoSimpleSeekPointerMove(object sender, MouseEventArgs e)
    {
        NotePlaylistVideoSimpleSeekKeyboard();
        NotePlaylistVisualCursorActivity();
    }

    private bool _playlistVisualCursorHideArmed;
    private bool _playlistVisualCursorHidden;
    private long _playlistVisualCursorIdleUntil;
    private DispatcherTimer? _playlistVisualCursorHideTimer;

    private bool IsPlaylistVisualCursorHideArmed() =>
        LibraryPlayerMode.AllowsPlaylistVisualCursorHide(
            LibraryPlayerMode.HidesMouseCursorForPlaylistVisual(
                !_playlistVideoChromeVisible,
                LibraryBrowser.PlaylistVisualIsVideo,
                LibraryBrowser.PlaylistVisualIsPdf),
            blockingDialogOpen: HasBlockingOwnedDialog());

    /// <summary>設定など、所有して表示中の子ウィンドウがあるか。</summary>
    private bool HasBlockingOwnedDialog()
    {
        try
        {
            foreach (Window child in OwnedWindows)
            {
                if (!ReferenceEquals(child, this) && child.IsVisible)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    /// <summary>ダイアログ表示中はカーソルを出し、閉じたあとに本再生の消す挙動へ戻す。</summary>
    internal void SuspendPlaylistVisualCursorHideForDialog()
    {
        _playlistVisualCursorHideTimer?.Stop();
        SetPlaylistVisualCursorHidden(false);
    }

    internal void ResumePlaylistVisualCursorHideAfterDialog() =>
        SyncPlaylistVisualCursorHide();

    /// <summary>
    /// 動画／PDF 本再生中はカーソルを消し、マウス移動で一時表示してからまた消す。
    /// </summary>
    private void SyncPlaylistVisualCursorHide()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            _playlistVisualCursorHideArmed = false;
            _playlistVisualCursorIdleUntil = 0;
            _playlistVisualCursorHideTimer?.Stop();
            SetPlaylistVisualCursorHidden(false);
            return;
        }

        var justArmed = !_playlistVisualCursorHideArmed;
        _playlistVisualCursorHideArmed = true;
        if (justArmed)
        {
            // 本再生突入時はすぐ消す（マウスを待たない）。
            SetPlaylistVisualCursorHidden(true);
            return;
        }

        RefreshPlaylistVisualCursorHide();
    }

    private void NotePlaylistVisualCursorActivity()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            return;
        }

        _playlistVisualCursorIdleUntil = Environment.TickCount64
            + (long)(LibraryPlayerMode.PlaylistVisualCursorHideIdleSeconds * 1000);
        _playlistVisualCursorHideTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistVisualCursorHideIdleSeconds),
        };
        _playlistVisualCursorHideTimer.Stop();
        _playlistVisualCursorHideTimer.Tick -= OnPlaylistVisualCursorHideTick;
        _playlistVisualCursorHideTimer.Tick += OnPlaylistVisualCursorHideTick;
        _playlistVisualCursorHideTimer.Start();
        SetPlaylistVisualCursorHidden(false);
    }

    private void OnPlaylistVisualCursorHideTick(object? sender, EventArgs e)
    {
        _playlistVisualCursorHideTimer?.Stop();
        var remain = _playlistVisualCursorIdleUntil - Environment.TickCount64;
        if (remain > 0 && _playlistVisualCursorHideTimer is not null)
        {
            _playlistVisualCursorHideTimer.Interval = TimeSpan.FromMilliseconds(remain);
            _playlistVisualCursorHideTimer.Start();
            return;
        }

        RefreshPlaylistVisualCursorHide();
    }

    private void RefreshPlaylistVisualCursorHide()
    {
        if (!IsPlaylistVisualCursorHideArmed())
        {
            SyncPlaylistVisualCursorHide();
            return;
        }

        var keepVisible = Environment.TickCount64 < _playlistVisualCursorIdleUntil
            || PlaylistVideoSimpleSeek.IsDragging;
        SetPlaylistVisualCursorHidden(!keepVisible);
    }

    private void SetPlaylistVisualCursorHidden(bool hidden)
    {
        if (_playlistVisualCursorHidden == hidden)
        {
            if (hidden && Mouse.OverrideCursor != Cursors.None)
            {
                Mouse.OverrideCursor = Cursors.None;
            }
            else if (!hidden && ReferenceEquals(Mouse.OverrideCursor, Cursors.None))
            {
                Mouse.OverrideCursor = null;
            }

            return;
        }

        _playlistVisualCursorHidden = hidden;
        Mouse.OverrideCursor = hidden ? Cursors.None : null;
    }

    private void SyncPlaylistVideoSimpleSeekRatio()
    {
        if (PlaylistVideoSimpleSeek.IsDragging || _document is null)
        {
            return;
        }

        PlaylistVideoSimpleSeek.Ratio = LibraryPlayerMode.SeekRatioAtFrame(
            Waveform.PlayheadFrame,
            _document.FrameCount);
    }

    private void PlaylistVideoSimpleSeek_RatioSeeked(object sender, double ratio)
    {
        if (_document is null)
        {
            return;
        }

        var frame = LibraryPlayerMode.FrameAtSeekRatio(ratio, _document.FrameCount);
        if (IsLibraryMaximized && LibraryPlaylistDocuments.IsVideo(_document))
        {
            // 再生中はセッションで止め、停止中も通常 Seek の Play→Pause 連打を避ける。
            if (_player.IsPlaying || LibraryBrowser.PlaylistVisualPlaying || _simpleSeekScrubActive)
            {
                BeginSimpleSeekScrubSession(frame);
                if (!_player.IsScrubbing)
                {
                    SeekFrame(frame);
                    return;
                }

                _document.CursorFrame = frame;
                Waveform.PlayheadFrame = frame;
                _player.CaptureScrub(_document, frame);
                RequestPlaylistVideoScrub(frame);
                SyncOverviewPlayhead();
                SyncTransportPosition(frame);
                RefreshStatus();
                return;
            }

            _document.CursorFrame = frame;
            Waveform.PlayheadFrame = frame;
            _player.Seek(frame);
            RequestPlaylistVideoScrub(frame);
            SyncOverviewPlayhead();
            SyncTransportPosition(frame);
            RefreshStatus();
            return;
        }

        SeekFrame(frame);
    }

    /// <summary>
    /// 簡易シークのドラッグ／グレイン中は、映像がフレーム停止してもクローム／アナライザを出さない。
    /// </summary>
    private bool IsSimpleSeekChromeHoldActive() =>
        _simpleSeekScrubActive || PlaylistVideoSimpleSeek.IsDragging;

    private void BeginSimpleSeekScrubSession(long frame)
    {
        if (_simpleSeekScrubActive || _document is null)
        {
            return;
        }

        _simpleSeekScrubActive = true;
        _resumeAfterSimpleSeek = _player.IsPlaying || LibraryBrowser.PlaylistVisualPlaying;
        StopPlaybackShuttle();
        StopSeekNudge();
        StopVideoFrameStep();
        _playTimer.Stop();
        Waveform.SetTrailRecording(false);
        try
        {
            // グレインスクラブへ切り替え（再生デコーダの Seek 連打を避ける）。
            _player.BeginScrub(_document, frame);
            _playbackGeneration = _player.Generation;
            RequestPlaylistVideoScrub(frame, force: true);
            StartMeterRendering();
            Transport.SetPlaying(true);
        }
        catch
        {
            _resumeAfterSimpleSeek = false;
            _simpleSeekScrubActive = false;
            CancelPlaylistVideoScrub();
        }
    }

    private void OnPlaylistVideoSimpleSeekInteractionEnded()
    {
        if (_simpleSeekScrubActive && _document is not null)
        {
            var frame = Waveform.PlayheadFrame;
            var resume = _resumeAfterSimpleSeek;
            _resumeAfterSimpleSeek = false;
            _simpleSeekScrubActive = false;
            FlushPlaylistVideoScrub();
            if (_player.IsScrubbing)
            {
                _player.EndScrub();
            }

            if (resume)
            {
                StartPlayback(frame, prerollSeconds: 0);
            }
            else
            {
                PausePlaybackSoft();
                SeekFrame(frame);
            }
        }
        else
        {
            FlushPlaylistVideoScrub();
        }

        RefreshPlaylistVideoSimpleSeekPresence();
        // ドラッグ中はカーソルを残していたので、離したらアイドル判定へ戻す。
        NotePlaylistVisualCursorActivity();
    }

    /// <summary>
    /// 動画スクラブの Position 更新を間引く。UI ヘッドは呼び出し側が即時更新する。
    /// ドラッグ中は粗いプレビュー、静止後に正確な絵を出す。
    /// </summary>
    private void RequestPlaylistVideoScrub(long frame, bool force = false)
    {
        if (_document is null || !LibraryPlaylistDocuments.IsVideo(_document))
        {
            return;
        }

        _playlistVideoScrubFrame = frame;
        RestartPlaylistVideoScrubExactTimer();

        var now = Environment.TickCount64;
        if (force
            || now - _playlistVideoScrubLastMs >= LibraryPlayerMode.VideoScrubSeekMinIntervalMs)
        {
            FlushPlaylistVideoScrub(exact: false);
            return;
        }

        _playlistVideoScrubTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.VideoScrubSeekMinIntervalMs),
        };
        _playlistVideoScrubTimer.Tick -= OnPlaylistVideoScrubTimerTick;
        _playlistVideoScrubTimer.Tick += OnPlaylistVideoScrubTimerTick;
        if (!_playlistVideoScrubTimer.IsEnabled)
        {
            var remain = LibraryPlayerMode.VideoScrubSeekMinIntervalMs - (now - _playlistVideoScrubLastMs);
            _playlistVideoScrubTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, remain));
            _playlistVideoScrubTimer.Start();
        }
    }

    private void OnPlaylistVideoScrubTimerTick(object? sender, EventArgs e)
    {
        _playlistVideoScrubTimer?.Stop();
        FlushPlaylistVideoScrub(exact: false);
    }

    /// <summary>
    /// 溜めたスクラブ位置を映像へ渡す。
    /// <paramref name="exact"/> が false のときは粗いプレビュー（ドラッグ中）。true は正確な絵で、
    /// 溜めた位置が無くても粗い絵だけ出していたフレームがあればそれを正確に出し直す（離した直後・静止後）。
    /// </summary>
    private void FlushPlaylistVideoScrub(bool exact = true)
    {
        _playlistVideoScrubTimer?.Stop();
        if (_document is null)
        {
            return;
        }

        var preview = !exact;
        var frame = _playlistVideoScrubFrame;
        if (frame < 0)
        {
            if (preview || _playlistVideoScrubPreviewFrame < 0)
            {
                return;
            }

            frame = _playlistVideoScrubPreviewFrame;
        }

        _playlistVideoScrubFrame = -1;
        _playlistVideoScrubPreviewFrame = preview ? frame : -1;
        if (!preview)
        {
            _playlistVideoScrubExactTimer?.Stop();
        }

        _playlistVideoScrubLastMs = Environment.TickCount64;
        LibraryBrowser.ScrubPlaylistVisual(
            TimeSpan.FromSeconds(frame / (double)Math.Max(1, _document.SampleRate)),
            scrubPreview: preview);
    }

    /// <summary>最後のスクラブ要求から <see cref="LibraryPlayerMode.VideoScrubExactIdleMs"/> 静止したら正確な絵を 1 回出す。</summary>
    private void RestartPlaylistVideoScrubExactTimer()
    {
        _playlistVideoScrubExactTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(LibraryPlayerMode.VideoScrubExactIdleMs),
        };
        _playlistVideoScrubExactTimer.Tick -= OnPlaylistVideoScrubExactTimerTick;
        _playlistVideoScrubExactTimer.Tick += OnPlaylistVideoScrubExactTimerTick;
        _playlistVideoScrubExactTimer.Stop();
        _playlistVideoScrubExactTimer.Start();
    }

    private void OnPlaylistVideoScrubExactTimerTick(object? sender, EventArgs e)
    {
        _playlistVideoScrubExactTimer?.Stop();
        FlushPlaylistVideoScrub(exact: true);
    }

    private void CancelPlaylistVideoScrub()
    {
        _playlistVideoScrubTimer?.Stop();
        _playlistVideoScrubExactTimer?.Stop();
        _playlistVideoScrubFrame = -1;
        _playlistVideoScrubPreviewFrame = -1;
    }

    private void ApplyPlaylistVideoTimecodeFade(bool show)
    {
        if (show)
        {
            // T で出した瞬間にも窓比例を当てる（リサイズ前に Identity のまま残るのを防ぐ）。
            ApplyPlaylistVideoTimecodeOverlayScale(VideoMiniOverlayLayoutScale);
        }

        _playlistVideoTimecodeHost.IsOpen = show;
        if (show)
        {
            _playlistVideoTimecodeHost.RefreshPlacementAfterLayout();
        }
    }

    private void ApplyPlaylistVideoFileNameFade(bool show, bool instant)
    {
        if (show)
        {
            RefreshPlaylistVideoFileName();
        }
        else
        {
            PlaylistVideoFileName.RenderTransform = Transform.Identity;
        }

        LibraryPlayerMode.FadeElementOpacity(
            PlaylistVideoFileName,
            show ? 1 : 0,
            instant,
            hitTestVisible: false,
            seconds: LibraryPlayerMode.ChromeFadeSeconds);
        if (show)
        {
            SchedulePlaylistVideoFileNamePlacement();
        }
    }

    /// <summary>
    /// 複数再生の位置トースト。許可時は曲／件数が変わったときだけ出し、少ししたらフェードアウト。
    /// </summary>
    private void SyncPlaylistTrackIndexToast(bool allow)
    {
        if (!allow)
        {
            _playlistTrackIndexToastKey = null;
            HidePlaylistTrackIndex();
            return;
        }

        var key = BuildPlaylistTrackIndexToastKey();
        if (string.Equals(_playlistTrackIndexToastKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _playlistTrackIndexToastKey = key;
        FlashPlaylistTrackIndex();
    }

    private string BuildPlaylistTrackIndexToastKey()
    {
        var index = DisplayedPlaylistIndex(_activeSession);
        var count = DisplayedPlaylistCount();
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{index}/{count}");
    }

    /// <summary>曲切替・本再生／ミニ突入時。番号を出して一定時間で閉じる。</summary>
    private void FlashPlaylistTrackIndex()
    {
        _playlistTrackIndexToast.SetOverlayScale(
            IsVideoMiniPlayerActive() && !_playlistVideoFullscreen
                ? VideoMiniOverlayLayoutScale
                : 1);
        var index = DisplayedPlaylistIndex(_activeSession);
        var count = DisplayedPlaylistCount();
        _playlistTrackIndexToast.Show(
            LibraryPlayerMode.FormatPlaylistTrackLabel(index, count),
            TimeSpan.FromSeconds(LibraryPlayerMode.PlaylistTrackIndexToastHoldSeconds));
    }

    private void HidePlaylistTrackIndex() => _playlistTrackIndexToast.Hide();


    private void RefreshPlaylistTrackIndex()
    {
        var index = DisplayedPlaylistIndex(_activeSession);
        var count = DisplayedPlaylistCount();
        _playlistTrackIndexToast.Text = LibraryPlayerMode.FormatPlaylistTrackLabel(index, count);
    }

    /// <summary>ページ切替成功時。番号を出して一定時間で閉じる。</summary>
    private void FlashPlaylistPdfPageNumber()
    {
        if (!LibraryPlayerMode.ShowsPlaylistPdfPageNumber(
                !_playlistVideoChromeVisible,
                LibraryBrowser.PlaylistVisualIsPdf))
        {
            return;
        }

        _playlistPdfPageNumberToast.SetOverlayScale(
            IsVideoMiniPlayerActive() && !_playlistVideoFullscreen
                ? VideoMiniOverlayLayoutScale
                : 1);
        _playlistPdfPageNumberToast.Show(
            LibraryPlayerMode.FormatPdfPageLabel(
                LibraryBrowser.PlaylistPdfPageIndex,
                LibraryBrowser.PlaylistPdfPageCount),
            TimeSpan.FromSeconds(LibraryPlayerMode.PdfPageNumberToastHoldSeconds));
    }

    private void HidePlaylistPdfPageNumber() => _playlistPdfPageNumberToast.Hide();


    private void RefreshPlaylistPdfPageNumber()
    {
        _playlistPdfPageNumberToast.Text = LibraryPlayerMode.FormatPdfPageLabel(
            LibraryBrowser.PlaylistPdfPageIndex,
            LibraryBrowser.PlaylistPdfPageCount);
    }

    private void RefreshPlaylistVideoFileName()
    {
        var text = _activeSession?.DisplayName
            ?? (_document?.SourcePath is { Length: > 0 } path
                ? System.IO.Path.GetFileName(path)
                : string.Empty);
        var source = _activeSession?.Document.SourcePath ?? _document?.SourcePath;
        var usingProxy = source is { Length: > 0 }
            && LibraryPlaylistDocuments.IsVideo(source)
            && VideoProxy.TryGetCached(source, out _);
        var rawName = text;
        text = LibraryPlayerMode.FormatPlaylistVideoFileName(text, usingProxy);
        PlaylistVideoFileNameText.Text = text;
        PlaylistVideoFileNameShadow.Text = text;
        FileNameColorMarkerVisual.Apply(PlaylistVideoFileNameMarker, rawName, source);
    }

    private void SchedulePlaylistVideoFileNamePlacement()
    {
        // リサイズ直後は Render 前に測ると帯位置が古いので、複数優先度で当てる。
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, SyncPlaylistVideoFileNamePlacement);
        Dispatcher.BeginInvoke(DispatcherPriority.Render, SyncPlaylistVideoFileNamePlacement);
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, SyncPlaylistVideoFileNamePlacement);
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, SyncPlaylistVideoFileNamePlacement);
        // A で HUD／ファイル名をフェードインした直後は Idle 時点でレイアウトが未確定なことがある。
        _playlistVideoFileNamePlaceTimer ??= new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(LibraryPlayerMode.ChromeFadeSeconds + 0.05),
        };
        _playlistVideoFileNamePlaceTimer.Tick -= OnPlaylistVideoFileNamePlaceTimerTick;
        _playlistVideoFileNamePlaceTimer.Tick += OnPlaylistVideoFileNamePlaceTimerTick;
        _playlistVideoFileNamePlaceTimer.Stop();
        _playlistVideoFileNamePlaceTimer.Interval = TimeSpan.FromSeconds(
            LibraryPlayerMode.ChromeFadeSeconds + 0.05);
        _playlistVideoFileNamePlaceTimer.Start();
    }

    private void OnPlaylistVideoFileNamePlaceTimerTick(object? sender, EventArgs e)
    {
        _playlistVideoFileNamePlaceTimer?.Stop();
        SyncPlaylistVideoFileNamePlacement();
    }

    /// <summary>
    /// ファイル名は常に中央。波形や狭幅で重なる帯があればその直上へ（左へはずらさない）。
    /// リサイズ後も Schedule 経由で再計算する。
    /// </summary>
    private void SyncPlaylistVideoFileNamePlacement()
    {
        var baseMargin = DesignMetrics.PlaylistVideoFileNameMargin;
        var gap = DesignMetrics.PlaylistVideoFileNameLoudnessGap;
        // 横ずらしは使わない（狭幅では上へ逃す）。前回分をクリア。
        PlaylistVideoFileName.RenderTransform = Transform.Identity;

        // まず最下段（既定余白）に置き、重なる帯があれば上げ、空きがあれば下へ戻す。
        var bottom = baseMargin.Bottom;
        PlaylistVideoFileName.Margin = new Thickness(
            baseMargin.Left,
            0,
            baseMargin.Right,
            bottom);

        var showName = LibraryPlayerMode.ShowsPlaylistVideoFileName(
            !_playlistVideoChromeVisible,
            _playlistVideoTimecode,
            LibraryBrowser.PlaylistVisualIsVideo);
        // 簡易シーク（A オフ）のあいだは帯の上へ上げず、画面下中央に置く。
        if (showName
            && !IsPlaylistVideoSimpleSeekArmed()
            && RootChrome.ActualHeight >= 8
            && RootChrome.ActualWidth >= 8)
        {
            PlaylistVideoFileName.UpdateLayout();
            LoudnessMeter.UpdateLayout();
            Spectrum.UpdateLayout();
            MonitorToggles.UpdateLayout();
            if (TryGetPlaylistVideoHudBandTop(out var bandTop))
            {
                bottom = LibraryPlayerMode.PlaylistVideoFileNameBottomAboveBand(
                    baseMargin.Bottom,
                    RootChrome.ActualHeight,
                    bandTop,
                    gap);
                PlaylistVideoFileName.Margin = new Thickness(
                    baseMargin.Left,
                    0,
                    baseMargin.Right,
                    bottom);
                PlaylistVideoFileName.UpdateLayout();

                // 上げすぎで帯とのあいだに空きがあれば、隙間が gap になるまで下へ寄せる。
                if (TryGetPlaylistVideoFileNameBox(out var nameLifted)
                    && TryGetPlaylistVideoHudBandTop(out bandTop))
                {
                    var snug = LibraryPlayerMode.PlaylistVideoFileNameSnugBottomTowardBand(
                        bottom,
                        nameLifted.Bottom,
                        bandTop,
                        gap);
                    if (Math.Abs(snug - bottom) > 0.5)
                    {
                        bottom = snug;
                        PlaylistVideoFileName.Margin = new Thickness(
                            baseMargin.Left,
                            0,
                            baseMargin.Right,
                            bottom);
                    }
                }
            }
        }

        QueueVideoHudFrostLayout();
    }

    private bool TryGetPlaylistVideoFileNameBox(out Rect nameBox)
    {
        nameBox = default;
        if (PlaylistVideoFileName.RenderSize.Width < 1 || PlaylistVideoFileName.RenderSize.Height < 1)
        {
            return false;
        }

        try
        {
            nameBox = PlaylistVideoFileName.TransformToVisual(RootChrome)
                .TransformBounds(new Rect(PlaylistVideoFileName.RenderSize));
            return nameBox.Width >= 1 && nameBox.Height >= 1;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// ファイル名と横に重なる下段コンテンツの上端（RootChrome 座標）。
    /// 波形ドック時の波形、または狭幅で重なったラウドネス／スペアナ／モニター。
    /// </summary>
    private bool TryGetPlaylistVideoHudBandTop(out double bandTop)
    {
        bandTop = double.PositiveInfinity;
        var parentH = RootChrome.ActualHeight;
        if (parentH < 8 || !TryGetPlaylistVideoFileNameBox(out var nameBox))
        {
            return false;
        }

        var found = false;
        FrameworkElement[] band =
        [
            WaveformTileHost,
            LoudnessMeter,
            Spectrum,
            MonitorToggles,
        ];
        foreach (var element in band)
        {
            if (!IsVideoHudFrostElementShown(element) || element.ActualHeight < 2)
            {
                continue;
            }

            // 波形はドック中だけ。オフ／下段帯のときは上げない。
            if (ReferenceEquals(element, WaveformTileHost)
                && (!_playerWaveformDockedBesideLoudness
                    || PlayerWaveformSlot.Visibility != Visibility.Visible))
            {
                continue;
            }

            try
            {
                var box = element.TransformToVisual(RootChrome)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (box.Height < 1
                    || box.Width < 1
                    || box.Bottom < parentH * 0.45
                    || !LibraryPlayerMode.PlaylistVideoFileNameBandOverlapsName(nameBox, box))
                {
                    continue;
                }

                bandTop = Math.Min(bandTop, box.Top);
                found = true;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return found && !double.IsInfinity(bandTop);
    }

    private bool TryTogglePdfBackgroundPin()
    {
        if (!IsLibraryMaximized)
        {
            return false;
        }

        // PDF 表示中は常に今のページで固定し直し、即抜ける（既にオンでも 1 回で反映）。
        if (LibraryBrowser.PlaylistVisualIsPdf)
        {
            if (!LibraryBrowser.TryPinPdfBackground())
            {
                return false;
            }

            ExitPlaylistPdfView(restoreDimPreview: false);
            return true;
        }

        // 表示していないときの B は解除。
        if (LibraryBrowser.PdfBackgroundPinned)
        {
            LibraryBrowser.ClearPdfBackgroundPin();
            return true;
        }

        return false;
    }

    /// <summary>
    /// PDF の明るい表示を閉じる（Enter／Space／Escape）。暗いプレビューへ戻す。
    /// W 固定直後は <paramref name="restoreDimPreview"/> を false にし、静止背景だけ残す。
    /// </summary>
    private void ExitPlaylistPdfView(bool restoreDimPreview = true)
    {
        if (!LibraryBrowser.PlaylistVisualIsPdf)
        {
            return;
        }

        SyncPlaylistPdfCursorFromVisual();
        _libraryStopAfterTrack = false;
        SetPlaylistVideoFullscreen(false);
        if (restoreDimPreview
            && LibraryPlayerMode.RestoresPdfDimPreviewOnExit(LibraryBrowser.PdfBackgroundPinned))
        {
            // Hide すると背面の暗いプレビューまで消える。本再生と同じく dim へ戻す。
            LibraryBrowser.SetPlaylistVisualPlaying(false);
        }
        else
        {
            LibraryBrowser.HidePlaylistVisual();
        }

        Transport.SetPlaying(false);
        EnsurePlaylistVideoWindowRestored();
    }

    /// <summary>
    /// Escape／Q で本再生／イマーシブ一時停止／PDF 明るい表示から暗いプレビューへ戻す（F10）。
    /// 動画ミニの Escape／Q は Shortcut 側でプレイリストへ戻す。
    /// </summary>
    private bool TryExitPlaylistRealPlayback()
    {
        if (LibraryBrowser.PlaylistVisualIsPdf && LibraryBrowser.PlaylistVisualPlaying)
        {
            ExitPlaylistPdfView();
            return true;
        }

        if (!LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(
                IsPlaybackActive(),
                _playlistVideoImmersivePause))
        {
            return false;
        }

        // 動画本再生・フレーム静止・イマーシブ一時停止以外（音声のみの再生）は Escape では止めない。
        if (!IsPlaylistVideoPlaying()
            && !_playlistVideoImmersivePause
            && !LibraryBrowser.PlaylistVisualFramePaused)
        {
            return false;
        }

        StopVideoFrameStep();
        DropPlaylistVideoToDimPreview();
        _libraryStopAfterTrack = false;
        return true;
    }

    private bool CanOfferExitRealPlayback()
    {
        if (IsVideoMiniPlayerActive())
        {
            return true;
        }

        if (LibraryBrowser.PlaylistVisualIsPdf && LibraryBrowser.PlaylistVisualPlaying)
        {
            return true;
        }

        if (!LibraryPlayerMode.HaltsLibraryPlaybackOnSpace(
                IsPlaybackActive(),
                _playlistVideoImmersivePause))
        {
            return false;
        }

        return IsPlaylistVideoPlaying()
            || _playlistVideoImmersivePause
            || LibraryBrowser.PlaylistVisualFramePaused;
    }

    private bool CanOfferForceVideoProxy()
    {
        if (!IsLibraryMaximized || IsUiBusy || !VideoProxy.CanForceEncodeProxy())
        {
            return false;
        }

        foreach (var session in ResolveForceVideoProxySessions())
        {
            if (LibraryPlaylistDocuments.IsVideo(session.Document)
                && session.Document.SourcePath is { Length: > 0 } path
                && !VideoCodecProbe.CanPlayWithoutProxy(path))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsPlaylistVideoPlaying() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsVideo
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>
    /// 停止中の Enter を本再生／表示にする対象（動画／PDF）。Enter は先頭から、Space は再開。
    /// 本再生中の Enter はその場停止（immersive）で、ここは使わない。
    /// </summary>
    private bool IsLibraryVisualTransportTarget()
    {
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        return session is not null && LibraryPlaylistDocuments.IsVisual(session.Document);
    }

    /// <summary>
    /// 本再生中、または ←→／テンキー 0 のフレーム一時停止中。
    /// 停止扱いにすると次曲選択が暗いプレビューになってしまうので、プレイリスト送りでは再生継続とみなす。
    /// </summary>
    private bool ContinuesLibraryPlaylistPlayback() =>
        IsPlaybackActive() || _playlistVideoImmersivePause;

    private bool IsPlaylistVisualHudContext() =>
        LibraryPlayerMode.IsPlaylistVideoHudContext(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualIsVideo,
            LibraryBrowser.PlaylistVisualPlaying,
            _playlistVideoImmersivePause,
            LibraryBrowser.PlaylistVisualFramePaused);

    private void SetPlaylistVideoFullscreen(bool on)
    {
        if (_playlistVideoFullscreen == on)
        {
            return;
        }

        if (on)
        {
            PersistCurrentWindowPlacement();
            _windowStyleBeforeVideoFullscreen = WindowStyle == WindowStyle.None
                ? WindowStyle.SingleBorderWindow
                : WindowStyle;
            _resizeModeBeforeVideoFullscreen = ResizeMode == ResizeMode.NoResize
                ? ResizeMode.CanResize
                : ResizeMode;
            _windowStateBeforeVideoFullscreen = WindowState;
            _boundsBeforeVideoFullscreen = WindowState == WindowState.Normal
                ? new Rect(Left, Top, Width, Height)
                : RestoreBounds;
            _playlistVideoFullscreen = true;
            ApplyWaveformFullscreenFrame();
            // 全画面寸法＋倍率 1 に列幅／行高を合わせ直す（縮小レイアウトのままだと見切れる）。
            ApplyLibraryMinimalChrome();
            SyncWindowProportionalOverlayScale();
            ApplyVideoOverlayBottomFlush(chromeHidden: !_playlistVideoChromeVisible);
            return;
        }

        // settings ではなく入る直前の寸法へ戻す（全画面中にモニター寸法が保存されるとステータスバーが隠れる）。
        // 全画面中に次の動画へ移っていたら、抜けたタイミングで原寸配置を反映する。
        _playlistVideoFullscreen = false;
        SyncShellFullscreen();
        WindowStyle = _windowStyleBeforeVideoFullscreen == WindowStyle.None
            ? WindowStyle.SingleBorderWindow
            : _windowStyleBeforeVideoFullscreen;
        ResizeMode = _resizeModeBeforeVideoFullscreen == ResizeMode.NoResize
            ? ResizeMode.CanResize
            : _resizeModeBeforeVideoFullscreen;
        if (_videoLaunchPlacement)
        {
            ResizeMode = ResizeMode.CanResize;
        }

        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        if (_videoLaunchPlacementDeferred)
        {
            _videoLaunchPlacementDeferred = false;
            if (TryApplyVideoLaunchPlacement())
            {
                SyncWindowProportionalOverlayScale();
                return;
            }
        }

        RestorePlaylistVideoWindowBounds();
        SyncWindowProportionalOverlayScale();
        ApplyVideoOverlayBottomFlush(chromeHidden: !_playlistVideoChromeVisible);
    }

    /// <summary>動画再生中に隠す前面 UI。波形・レベル／スペアナ／ゴニオ／サラウンド／ラウドネスは残す。</summary>
    private UIElement[] PlaylistVideoChromeTargets() =>
        [
            StatusBarHost,
            WaapiBar,
            LibraryBrowser,
            LibrarySplitter,
            MeterColumnSplitter,
            TransportToolbarHost,
            DocumentTabHost,
            HistoryStrip,
        ];

    /// <summary>A トグルで出す／消す HUD。波形・アナライザに加え WAAPI 表記（MeterTopSlot）も含む。</summary>
    private UIElement[] PlaylistVideoHudTargets() =>
        [WaveformTileHost, MeterTopSlot, LevelMeter, VectorScope, Spectrum, LoudnessMeter, MonitorToggles];

    private void SyncPlaylistVideoUiShadows(bool chromeVisible, bool hideHud)
    {
        var video = IsLibraryMaximized
            && LibraryBrowser.PlaylistVisualShown
            && LibraryBrowser.PlaylistVisualIsVideo;
        foreach (var target in PlaylistVideoChromeTargets())
        {
            LibraryPlayerMode.SetVideoUiShadow(target, video && chromeVisible);
        }

        foreach (var target in PlaylistVideoHudTargets())
        {
            LibraryPlayerMode.SetVideoUiShadow(target, video && !hideHud);
        }

        // ファイル名は XAML の文字シルエット影。ラベル全体への Effect は使わない。T と一緒に出す。
        PlaylistVideoFileName.Effect = null;
        LibraryPlayerMode.SetVideoTextShadow(
            PlaylistVideoFileNameShadow,
            video
            && LibraryPlayerMode.ShowsPlaylistVideoFileName(
                !_playlistVideoChromeVisible,
                _playlistVideoTimecode,
                isVideo: true));
        // タイムコードはトーストと同じ Acrylic（DWM Round）。要素 Effect は付けない。
        SyncPlaylistVideoHudLabelColors();
    }

    /// <summary>動画本再生／ミニ限定。メーター文字をファイル名と同じ #F5F5F5 に。</summary>
    private void SyncPlaylistVideoHudLabelColors()
    {
        var on = LibraryPlayerMode.UsesVideoOverlayLabelFore(
            IsLibraryMaximized,
            LibraryBrowser.PlaylistVisualShown,
            LibraryBrowser.PlaylistVisualIsVideo,
            !_playlistVideoChromeVisible,
            IsVideoMiniPlayerActive());
        LevelMeter.VideoOverlayLabels = on;
        VectorScope.VideoOverlayLabels = on;
        Spectrum.VideoOverlayLabels = on;
        LoudnessMeter.VideoOverlayLabels = on;
        MonitorToggles.VideoOverlayLabels = on;
        ForEachWaveform(view => view.VideoOverlayLabels = on);
        SyncVideoHudFrost(on);
    }

    private DispatcherTimer? _videoHudFrostTimer;

    /// <summary>
    /// 動画本再生／ミニの波形・ピーク・ゴニオ・スペアナ・ラウドネス・ファイル名の背後だけ、
    /// 少し暗くする。ラウドネスとスペアナのあいだ（モニター列）は一枚で埋める。縁は映像へ溶かす。
    /// </summary>
    private void SyncVideoHudFrost(bool on)
    {
        if (!on)
        {
            _videoHudFrostTimer?.Stop();
            _videoHudFrostLayoutQueued = false;
            LibraryBrowser.SetVideoHudFrost(false, null);
            return;
        }

        _videoHudFrostTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _videoHudFrostTimer.Tick -= OnVideoHudFrostTick;
        _videoHudFrostTimer.Tick += OnVideoHudFrostTick;
        if (!_videoHudFrostTimer.IsEnabled)
        {
            _videoHudFrostTimer.Start();
        }

        OnVideoHudFrostTick(null, EventArgs.Empty);
    }

    private bool _videoHudFrostLayoutQueued;

    /// <summary>レイアウト直後のすりガラス位置合わせ。連打は1フレームにまとめる。</summary>
    private void QueueVideoHudFrostLayout()
    {
        if (_videoHudFrostTimer is not { IsEnabled: true } || _videoHudFrostLayoutQueued)
        {
            return;
        }

        _videoHudFrostLayoutQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _videoHudFrostLayoutQueued = false;
            if (_videoHudFrostTimer is { IsEnabled: true })
            {
                OnVideoHudFrostTick(null, EventArgs.Empty);
            }
        });
    }

    private void OnVideoHudFrostTick(object? sender, EventArgs e) =>
        LibraryBrowser.SetVideoHudFrost(true, BuildVideoHudFrostClip());

    private Geometry? BuildVideoHudFrostClip()
    {
        var stage = LibraryBrowser.VideoHudFrostTarget;
        if (stage is null || stage.ActualWidth < 1 || stage.ActualHeight < 1)
        {
            return null;
        }

        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var element in VideoHudFrostElements)
        {
            // ドック中の波形は帯の外接一枚に含める。個別にも入れると暗幕が二重になる。
            if (_playerWaveformDockedBesideLoudness
                && ReferenceEquals(element, WaveformTileHost))
            {
                continue;
            }

            if (!IsVideoHudFrostElementShown(element))
            {
                continue;
            }

            try
            {
                var transform = element.TransformToVisual(stage);
                var rect = transform.TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (rect.Width < 1 || rect.Height < 1)
                {
                    continue;
                }

                group.Children.Add(new RectangleGeometry(rect));
            }
            catch (InvalidOperationException)
            {
            }
        }

        // ラウドネス〜モニター列〜スペアナは外接一枚。列マージンやボタン周りの隙間を残さない。
        TryAddLoudnessMonitorSpectrumFrost(group, stage);

        return group.Children.Count == 0 ? null : group;
    }

    /// <summary>
    /// ラウドネス・モニター列・スペアナをまとめた外接矩形を一枚だけ追加する。
    /// 個別 Rect だと列マージンと MonitorToggles 上下の空きが抜ける。
    /// ドック中は波形もこの一枚だけ（個別 frost とは排他）。
    /// </summary>
    private void TryAddLoudnessMonitorSpectrumFrost(GeometryGroup group, Visual stage)
    {
        // ドック中は波形も同じ帯に含め、隙間を残さない（個別側ではスキップ済み）。
        FrameworkElement[] band = _playerWaveformDockedBesideLoudness
            ? [WaveformTileHost, LoudnessMeter, MonitorToggles, Spectrum]
            : [LoudnessMeter, MonitorToggles, Spectrum];
        var has = false;
        var left = 0d;
        var top = 0d;
        var right = 0d;
        var bottom = 0d;
        foreach (var element in band)
        {
            if (!IsVideoHudFrostElementShown(element))
            {
                continue;
            }

            try
            {
                var rect = element.TransformToVisual(stage)
                    .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
                if (rect.Width < 1 || rect.Height < 1)
                {
                    continue;
                }

                if (!has)
                {
                    left = rect.Left;
                    top = rect.Top;
                    right = rect.Right;
                    bottom = rect.Bottom;
                    has = true;
                }
                else
                {
                    left = Math.Min(left, rect.Left);
                    top = Math.Min(top, rect.Top);
                    right = Math.Max(right, rect.Right);
                    bottom = Math.Max(bottom, rect.Bottom);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (!has)
        {
            return;
        }

        var width = right - left;
        var height = bottom - top;
        if (width < 1 || height < 1)
        {
            return;
        }

        group.Children.Add(new RectangleGeometry(new Rect(left, top, width, height)));
    }

    private static bool IsVideoHudFrostElementShown(FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth < 2 || element.ActualHeight < 2)
        {
            return false;
        }

        var opacity = 1d;
        DependencyObject? current = element;
        while (current is UIElement visual)
        {
            opacity *= visual.Opacity;
            if (opacity <= 0.05)
            {
                return false;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return true;
    }

    private FrameworkElement[]? _videoHudFrostElements;

    /// <summary>
    /// ラウドネス／モニター／スペアナは <see cref="TryAddLoudnessMonitorSpectrumFrost"/> で一枚。
    /// 個別に入れると列マージンが抜けて隙間になる。
    /// ドック中の波形も同メソッド側だけ（ここでも入れると暗幕が二重）。
    /// </summary>
    private FrameworkElement[] VideoHudFrostElements =>
        _videoHudFrostElements ??=
            [WaveformTileHost, LevelMeter, VectorScope, PlaylistVideoFileName];
}
