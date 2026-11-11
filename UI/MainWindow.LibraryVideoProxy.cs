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
    /// 本再生直前。プロキシ不要／作成不可なら true のまま通し、必要なら Ensure する。
    /// </summary>
    private async Task<bool> EnsureVideoProxyBeforePlaybackAsync(DocumentSession session)
    {
        if (!LibraryPlaylistDocuments.IsVideo(session.Document)
            || CanShowPlaylistVideo(session.Document))
        {
            return true;
        }

        return await TryPrepareVideoProxyAsync(session, playTicket: -1).ConfigureAwait(true);
    }

    private async Task<bool> TryPrepareVideoProxyAsync(
        DocumentSession session,
        int playTicket = -1,
        bool forceEncode = false,
        bool manageBusy = true,
        IProgress<double>? progress = null)
    {
        if (!LibraryPlaylistDocuments.IsVideo(session.Document)
            || session.Document.SourcePath is not { Length: > 0 } path)
        {
            return false;
        }

        // force は設定抑止を無視して作る。現行キャッシュがあれば再生成しない。
        var needsEncode = forceEncode
            ? !VideoCodecProbe.CanPlayWithoutProxy(path)
                && VideoProxy.CanForceEncodeProxy()
                && !VideoProxy.TryGetCached(path, out _)
            : !CanShowPlaylistVideo(session.Document) && VideoProxy.CanEncodeProxy();
        // 自分のプロキシ生成中は差し替え可。別種の Busy だけ拒否する。
        if (needsEncode && manageBusy && IsUiBusy && !_videoProxyBusy)
        {
            return false;
        }

        var token = CancellationToken.None;
        if (needsEncode && manageBusy)
        {
            _videoProxyCts?.Cancel();
            _videoProxyCts?.Dispose();
            _videoProxyCts = new CancellationTokenSource();
            token = _videoProxyCts.Token;
            _videoProxyBusy = true;
            ShowBusyGlass(UiStrings.OverlayVideoProxy);
        }
        else if (needsEncode)
        {
            token = _videoProxyCts?.Token ?? CancellationToken.None;
        }

        try
        {
            progress ??= needsEncode
                ? new Progress<double>(p => _busyGlass.SetProgress(p))
                : null;
            await VideoProxy.EnsurePlayableAsync(path, progress, token, forceEncode).ConfigureAwait(true);
            // プロキシ完了後は再生チケットだけ見る。待ちのあいだに別ロードが
            // _libraryLoadSession を動かしても、この Enter の本再生は続ける。
            if (!IsLoaded
                || token.IsCancellationRequested
                || (playTicket >= 0 && playTicket != _libraryPlayTicket))
            {
                return false;
            }

            _libraryLoadSession = session;
            ActivateVisualLibraryMeta(session);
            if (!session.Document.HasArtwork
                && session.Document.SourcePath is { Length: > 0 } source
                && VideoArtwork.TryRead(source, out var frame))
            {
                session.Document.SetArtwork(frame);
                LibraryBrowser.UpdateSessionRow(session);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (IsLoaded)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    ex.Message,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
        finally
        {
            // 自分が始めた生成のときだけ Busy を下ろす（差し替え後の新しい生成は残す）。
            if (needsEncode && manageBusy && (_videoProxyCts is null || token == _videoProxyCts.Token))
            {
                _videoProxyBusy = false;
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }
            }
        }
    }

    /// <summary>
    /// Ctrl+Shift+P。設定の「自動生成しない」や F8／引数起動の抑止を無視してプロキシを作る。
    /// プレイリスト複数選択・本再生・ミニプレイヤーの動画が対象。
    /// 現行バージョンのキャッシュがあるものはスキップする。
    /// プロキシ不要（MJPEG 等）／ffmpeg 無しは何もしない。
    /// </summary>
    private void RequestForceVideoProxyEncode() => _ = ForceVideoProxyEncodeAsync();

    private async Task ForceVideoProxyEncodeAsync()
    {
        if (!IsLibraryMaximized || IsUiBusy || !VideoProxy.CanForceEncodeProxy())
        {
            return;
        }

        var candidates = new List<DocumentSession>();
        var targets = new List<DocumentSession>();
        foreach (var session in ResolveForceVideoProxySessions())
        {
            if (LibraryPlaylistDocuments.IsVideo(session.Document)
                && session.Document.SourcePath is { Length: > 0 } path
                && !VideoCodecProbe.CanPlayWithoutProxy(path))
            {
                candidates.Add(session);
                if (!VideoProxy.TryGetCached(path, out _))
                {
                    targets.Add(session);
                }
            }
        }

        if (targets.Count == 0)
        {
            if (candidates.Count > 0 && IsLoaded)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.InfoVideoProxyAlreadyReady,
                    UiStrings.AppName,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            return;
        }

        DocumentSession? shownSession = null;
        string? shownPath = null;
        var wasShown = false;
        var wasPlaying = false;
        var released = false;
        foreach (var session in targets)
        {
            var path = session.Document.SourcePath!;
            if (!LibraryBrowser.IsPlaylistVisualSameSource(path))
            {
                continue;
            }

            shownSession = session;
            shownPath = path;
            wasShown = LibraryBrowser.PlaylistVisualShown;
            wasPlaying = LibraryBrowser.PlaylistVisualPlaying;
            if (wasShown && session.Document.SampleRate > 0)
            {
                var seconds = LibraryBrowser.PlaylistVisualPosition.TotalSeconds;
                if (seconds > 0 || wasPlaying || IsPlaybackActive() || _playlistVideoImmersivePause)
                {
                    session.Document.CursorFrame = (long)Math.Round(seconds * session.Document.SampleRate);
                    Waveform.PlayheadFrame = session.Document.CursorFrame;
                }
            }

            break;
        }

        // 本再生中の差し替えは効かないので、生成前に止めて暗いプレビューへ。
        // 次の Space／Enter でプロキシが使われる。
        if (wasShown && (wasPlaying || IsPlaybackActive() || _playlistVideoImmersivePause))
        {
            DropPlaylistVideoToDimPreview();
        }
        else if (IsPlaybackActive())
        {
            StopPlayback();
        }

        // DXGI が既存プロキシを開いたままだと上書き Move に失敗し、作り直した絵も載らない。
        if (wasShown && shownPath is not null)
        {
            released = LibraryBrowser.ReleasePlaylistVideoProxyForRewrite(shownPath);
        }

        _videoProxyCts?.Cancel();
        _videoProxyCts?.Dispose();
        _videoProxyCts = new CancellationTokenSource();
        var token = _videoProxyCts.Token;
        _videoProxyBusy = true;
        var total = targets.Count;
        ShowBusyGlass(UiStrings.OverlayVideoProxyBatch(1, total));

        var ok = 0;
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (token.IsCancellationRequested || !IsLoaded)
                {
                    break;
                }

                var index = i;
                _busyGlass.SetMessage(UiStrings.OverlayVideoProxyBatch(index + 1, total));
                _busyGlass.SetProgress(index / (double)total);
                var progress = new Progress<double>(p =>
                    _busyGlass.SetProgress((index + Math.Clamp(p, 0, 1)) / total));
                if (await TryPrepareVideoProxyAsync(
                        targets[i],
                        playTicket: -1,
                        forceEncode: true,
                        manageBusy: false,
                        progress).ConfigureAwait(true))
                {
                    ok++;
                    await RestartLibraryPeaksForSessionAsync(targets[i]).ConfigureAwait(true);
                    LibraryBrowser.UpdateSessionRow(targets[i]);
                }
            }
        }
        finally
        {
            if (_videoProxyCts is null || token == _videoProxyCts.Token)
            {
                _videoProxyBusy = false;
                if (_busyGlass.IsShowingBusy)
                {
                    _busyGlass.BeginFadeOut();
                }
            }
        }

        if (!IsLoaded || ok == 0)
        {
            if (released
                && shownSession is not null
                && shownPath is not null
                && IsLoaded
                && LibraryBrowser.IsPlaylistVisualSameSource(shownPath))
            {
                LibraryBrowser.ShowPlaylistVisual(shownSession, play: false);
            }

            return;
        }

        // ActivateVisualLibraryMeta → ActivateStreamPlayback がピークを捨てる。
        // プロキシができたら暗いプレビューへ載せ替え（再開はしない）。
        if (wasShown
            && shownSession is not null
            && shownPath is not null
            && LibraryBrowser.IsPlaylistVisualSameSource(shownPath))
        {
            LibraryBrowser.ShowPlaylistVisual(shownSession, play: false);
        }

        RefreshPlaylistVideoFileName();
        SchedulePlaylistVideoFileNamePlacement();
        RefreshPlaylistTrackIndex();
    }

    /// <summary>
    /// 走査中なら止めてからやり直す。プロキシ差し替え後に原ファイル走査が残ると波形が空のままになる。
    /// </summary>
    private async Task RestartLibraryPeaksForSessionAsync(DocumentSession session)
    {
        var document = session.Document;
        if (_libraryPeakJobCts.TryGetValue(document, out var job))
        {
            try
            {
                job.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_libraryPeakTasks.TryGetValue(document, out var running))
        {
            try
            {
                await running.ConfigureAwait(true);
            }
            catch
            {
                // ピーク失敗は再生を止めない（FillLibraryPeaksCoreAsync 内でも握る）。
            }
        }

        await FillLibraryPeaksAsync(session).ConfigureAwait(true);
    }

    /// <summary>
    /// 本再生／ミニプレイヤー中は再生中の動画1本、それ以外は複数選択（無ければアクティブ）。
    /// </summary>
    private IReadOnlyList<DocumentSession> ResolveForceVideoProxySessions()
    {
        if (IsVideoMiniPlayerActive()
            || (LibraryBrowser.PlaylistVisualShown
                && LibraryBrowser.PlaylistVisualIsVideo
                && (LibraryBrowser.PlaylistVisualPlaying || _playlistVideoImmersivePause)))
        {
            var playing = _activeSession ?? LibraryBrowser.SelectedSession;
            return playing is null ? [] : [playing];
        }

        var selected = LibraryBrowser.SelectedSessions;
        if (selected.Length > 0)
        {
            return selected;
        }

        var one = LibraryBrowser.SelectedSession ?? _activeSession;
        return one is null ? [] : [one];
    }
}
