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
    /// <summary>
    /// F8 動画専用ミニプレイヤー。前面 UI 無し・映像原寸ウィンドウ枠。
    /// 突入は背面に動画があるとき（本再生または暗いバックグラウンドプレビュー）かつ
    /// プレイリストに再生対象があるとき（拡張子連動は自動）。暗いプレビューからは本再生を再開して入る。
    /// もう一度 F8 で通常プレイヤーへ（再生は続ける）。F9／F10 でプレイリストへ戻る。
    /// </summary>
    private void ToggleVideoMiniPlayer()
    {
        if (RejectOutsideFadeToolAction())
        {
            return;
        }

        var active = IsVideoMiniPlayerActive();
        if (!LibraryPlayerMode.CanToggleVideoMiniPlayer(
                active,
                IsVideoMiniEnterEligible(),
                hasPlayableSession: _sessions.Count > 0))
        {
            return;
        }

        if (active)
        {
            // F8 解除は再生を続けたまま通常プレイヤーへ。
            LeaveVideoMiniKeepingPlayback();
            return;
        }

        // F8 は設定にかかわらずプロキシを作らない（本再生再開より先に抑止する）。
        VideoProxy.SuppressAutoEncode = true;

        // 暗いバックグラウンド／フレーム一時停止からは、いまの位置で本再生してからミニへ。
        if (LibraryBrowser.PlaylistVisualIsVideo && !IsVideoRealPlayback())
        {
            ResumeVideoPlaybackForMini();
        }
        else if (LibraryBrowser.PlaylistVisualIsPdf && !LibraryBrowser.PlaylistVisualPlaying)
        {
            // PDF の暗いプレビューからは明るい表示にしてからミニへ。
            LibraryBrowser.SetPlaylistVisualPlaying(true);
        }

        EnterVideoMiniPlayer();
    }

    private bool IsVideoMiniPlayerActive() =>
        IsLibraryMaximized && _videoLaunchPlacement;

    /// <summary>動画の本再生中（暗いプレビューは含まない）。</summary>
    private bool IsVideoRealPlayback() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsVideo
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>PDF の明るい表示中（暗いプレビューは含まない）。</summary>
    private bool IsPdfBrightDisplay() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && LibraryBrowser.PlaylistVisualIsPdf
        && LibraryBrowser.PlaylistVisualPlaying;

    /// <summary>
    /// 捨てマーカー／InOut／番号ジャンプを使うか。
    /// 音声のプレイリスト波形、動画本再生・F8・イマーシブ一時停止（暗いプレビューは不可）。
    /// </summary>
    private bool IsPlayerScratchContext() =>
        LibraryPlayerMode.UsesPlayerMarkerShortcuts(
            videoMiniPlayer: IsVideoMiniPlayerActive(),
            videoRealPlayback: IsVideoRealPlayback(),
            videoImmersivePause: IsLibraryMaximized
                && LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && _playlistVideoImmersivePause,
            audioWaveform: IsLibraryMaximized
                && _document is not null
                && !LibraryPlaylistDocuments.IsVisual(_document));

    /// <summary>
    /// Shift＋J／K で選択を伸ばせるか。
    /// エディタ全般、およびプレイヤーでシーク＆選択できる波形（捨てキューと同じコンテキスト。暗いプレビューは不可）。
    /// </summary>
    private bool AllowsWaveformSelectionTransport() =>
        _document is not null
        && (!IsLibraryMaximized || IsPlayerScratchContext());

    private void SyncPlayerScratchEnabled()
    {
        var enabled = IsPlayerScratchContext();
        PrimaryWaveform.PlayerScratchEnabled = enabled;
        ForEachWaveform(view => view.PlayerScratchEnabled = enabled);
    }

    /// <summary>
    /// プレイヤー捨てキュー（I／O／M／Ins）。呼び出し前に <see cref="LibraryPlayerMode.AllowsPlayerScratchCueKey"/> でゲートすること。
    /// </summary>
    private bool TryHandlePlayerScratchCueKey(Key key, ModifierKeys modifiers)
    {
        if (!LibraryPlayerMode.IsPlayerScratchCueKey(key, modifiers)
            || !IsPlayerScratchContext())
        {
            return false;
        }

        if (key is Key.I or Key.O)
        {
            TrySetPlayerAbPoint(key == Key.I);
            return true;
        }

        TryAddPlayerScratchMarkerAtPlayhead();
        return true;
    }

    /// <summary>F8 突入可。背面に動画または PDF が出ていれば本再生でなくても可。</summary>
    private bool IsVideoMiniEnterEligible() =>
        IsLibraryMaximized
        && LibraryBrowser.PlaylistVisualShown
        && (LibraryBrowser.PlaylistVisualIsVideo || LibraryBrowser.PlaylistVisualIsPdf);

    /// <summary>暗いプレビュー等から F8 用に本再生を再開する。</summary>
    private void ResumeVideoPlaybackForMini()
    {
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null || !LibraryPlaylistDocuments.IsVideo(session.Document))
        {
            return;
        }

        ClearPlaylistVideoImmersivePause(sync: false);
        StartLibraryVisualPlayback(session, stopAfterTrack: false);
    }

    /// <summary>F9 レイアウトプリセットを適用する。</summary>
    private void ToggleLibraryMinimalChrome() =>
        ApplyLayoutPreset(WindowLayoutSlot.F9, fromUserShortcut: true);

    /// <summary>ミニマム中の F9。通常のエディタへ戻す。F9 のウィンドウ位置は残す。</summary>
    private void LeaveMinimalToEditor()
    {
        if (!IsLibraryMaximized || !_libraryMinimalChrome)
        {
            return;
        }

        SetWaveformMaximizeMode(WaveformMaximizeMode.Off);
        if (IsLibraryMaximized)
        {
            return;
        }

        _libraryMinimalChrome = false;
    }

    /// <summary>ミニマム解除 → 通常の F10。F10 から。</summary>
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
            playFirstOnLibrary: !preservePlayback,
            retainMinimalChrome: true);
        LibraryBrowser.FocusList();
    }

    /// <summary>動画ミニへ入る（F8／拡張子連動／引数）。プロキシは作らない。</summary>
    private void EnterVideoMiniPlayer()
    {
        // F 全画面中の F8 は全画面を抜けてから原寸ミニへ（遅延配置のままだと変化が分からない）。
        if (LibraryPlayerMode.ExitsVideoFullscreenBeforeEnterMini(
                _playlistVideoFullscreen,
                alreadyVideoMini: _videoLaunchPlacement))
        {
            SetPlaylistVideoFullscreen(false);
        }

        if (IsLibraryMaximized && !_videoLaunchPlacement)
        {
            PersistCurrentWindowPlacement();
        }

        BeginVideoMiniPlayerSession();
        ApplyVideoMiniPlayerUi();
        if (!IsLibraryMaximized)
        {
            SetWaveformMaximizeMode(
                WaveformMaximizeMode.Library,
                playFirstOnLibrary: !IsPlaybackActive(),
                retainMinimalChrome: true);
        }
        else
        {
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
        }

        SyncPlaylistVideoChromeFade();
        ScheduleLibraryWaveformPaint();
        AppStorage.Save();
    }

    /// <summary>動画ミニセッション開始。F9／F10 本再生以外ではプロキシを作らない。</summary>
    private void BeginVideoMiniPlayerSession()
    {
        if (!_videoLaunchPlacement)
        {
            _videoLaunchScale = LibraryVideoWindowScale.Native;
            _videoLaunchScaleLatched = false;
        }

        _videoLaunchPlacement = true;
        VideoProxy.SuppressAutoEncode = true;
        if (!_playlistVideoFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
        }
    }

    private void ApplyVideoMiniPlayerUi()
    {
        _libraryMinimalChrome = true;
        ApplyRememberedVideoChrome();
        if (!_playlistVideoFullscreen)
        {
            ResizeMode = ResizeMode.CanResize;
        }
    }

    /// <summary>動画本再生と動画ミニで共通の A／T。未変更なら両方オン。</summary>
    private void ApplyRememberedVideoChrome()
    {
        var settings = AppStorage.Settings;
        LibraryPlayerMode.ResolveVideoLaunchChrome(
            settings.VideoChromeChosen,
            settings.VideoLaunchHud,
            settings.VideoLaunchTimecode,
            out _playlistVideoHud,
            out _playlistVideoTimecode);
    }

    /// <summary>動画本再生／ミニで変えた A／T を以後ずっと使う。</summary>
    private void RememberVideoChrome()
    {
        var settings = AppStorage.Settings;
        if (settings.VideoChromeChosen
            && settings.VideoLaunchHud == _playlistVideoHud
            && settings.VideoLaunchTimecode == _playlistVideoTimecode)
        {
            return;
        }

        settings.VideoChromeChosen = true;
        settings.VideoLaunchHud = _playlistVideoHud;
        settings.VideoLaunchTimecode = _playlistVideoTimecode;
        AppStorage.Save();
    }

    /// <summary>F8 解除。再生は続けたまま通常の F10 プレイヤーへ。</summary>
    private void LeaveVideoMiniKeepingPlayback()
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        // ミニ寸法の SizeChanged が F10 枠を上書きしないよう、復元まで Persist を止める。
        PersistCurrentWindowPlacement();
        _suppressLayoutPlacementPersist = true;
        try
        {
            EndVideoLaunchSession();
            _libraryMinimalChrome = false;
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
            SyncPlaylistVideoChromeFade();
        }
        finally
        {
            _suppressLayoutPlacementPersist = false;
        }

        AppStorage.Save();
        FocusLibraryPaneForPlaylist();
    }

    /// <summary>
    /// F9／F10。動画ミニを解き、プレイリストが見える通常／F9 プレイヤーへ戻す。
    /// 本再生中は暗いプレビューへ落として前面 UI（リスト）を出す。
    /// </summary>
    private void LeaveVideoMiniToPlaylist(bool minimalChrome)
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        // ミニ窓のまま MinSize 引き上げ等で SizeChanged すると、突入前に残した
        // F10／F9 枠が映像サイズで潰れる。復元が終わるまで Persist しない。
        PersistCurrentWindowPlacement();
        _suppressLayoutPlacementPersist = true;
        try
        {
            EndVideoLaunchSession();
            _playlistVideoImmersivePause = false;
            if (IsPlaybackActive() || LibraryBrowser.PlaylistVisualPlaying)
            {
                DropPlaylistVideoToDimPreview();
            }

            _libraryMinimalChrome = minimalChrome;
            ApplyWaveformMaximizeChrome();
            TryApplyCurrentModePlacement();
            SyncPlaylistVideoChromeFade();
        }
        finally
        {
            _suppressLayoutPlacementPersist = false;
        }

        AppStorage.Save();
        if (minimalChrome)
        {
            LibraryBrowser.FocusList();
        }
        else
        {
            FocusLibraryPaneForPlaylist();
        }
    }

    /// <summary>
    /// F11／F12 へ移る前に動画ミニだけ解く（プレイリストは触らない。動画の持ち込みは KeepOnly で除外）。
    /// </summary>
    private void LeaveVideoMiniBeforeWaveformFullscreen()
    {
        if (!IsVideoMiniPlayerActive())
        {
            return;
        }

        PersistCurrentWindowPlacement();
        _suppressLayoutPlacementPersist = true;
        try
        {
            EndVideoLaunchSession();
            _playlistVideoImmersivePause = false;
            _libraryMinimalChrome = false;
            LibraryBrowser.HidePlaylistVisual();
            // F11／F12 が覚える枠が映像サイズにならないよう、先に F10 寸法へ戻す。
            WindowPlacement.TryApply(this, AppStorage.Settings, MainWindowPlacementKind.Player);
            SyncPlaylistVideoChromeFade();
        }
        finally
        {
            _suppressLayoutPlacementPersist = false;
        }

        AppStorage.Save();
    }
}
