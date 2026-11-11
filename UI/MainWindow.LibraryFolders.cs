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
    private Task RegisterLibraryPathsAsync(IReadOnlyList<string> paths) =>
        RegisterLibraryPathsAsync(paths, play: true);

    private async Task RegisterLibraryPathsAsync(IReadOnlyList<string> paths, bool play)
    {
        // 進行中のフォルダ追加を止めて、起動・ドロップを優先する。
        CancelLibraryFolderShow();
        var playFirst = play && (_libraryPlayFirstPending || _sessions.Count == 0);
        DocumentSession? opened = null;
        DocumentSession? existingFirst = null;
        var showProgress = paths.Count > 1;
        // フォルダ再帰追加と同じく、追加完了まで背景色替えを遅延する。
        var batchGeneration = _libraryFolderShowGeneration;
        var deferWash = IsLibraryMaximized;
        if (deferWash)
        {
            _libraryFolderShowInProgress = true;
        }

        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (showProgress)
                {
                    SetOpenStatus(i + 1, paths.Count, Path.GetFileName(path) ?? path);
                }

                var existing = FindSessionByPath(path);
                if (existing is not null)
                {
                    existingFirst ??= existing;
                    continue;
                }

                DocumentSession session;
                try
                {
                    session = await Task.Run(() => CreateLibrarySession(path)).ConfigureAwait(true);
                }
                catch (Exception)
                {
                    continue;
                }

                _sessions.Add(session);
                opened ??= session;
            }

            RebuildTabBar();
            if (!play)
            {
                // 追加だけ。既存の選択は動かさない。空リストへ足したときだけ 1 行目を選び、停止中プレビューを出す。
                if (IsLibraryMaximized)
                {
                    var keep = LibraryBrowser.SelectedSession;
                    var keepMany = LibraryBrowser.SelectedSessions;
                    if (keep is null)
                    {
                        keep = LibraryPlayerMode.FirstSession(_sessions);
                        if (keep is not null)
                        {
                            keepMany = [keep];
                        }
                    }

                    LibraryBrowser.SetSessions(_sessions, keep, keepMany);
                    if (keep is not null)
                    {
                        QueuePendingLibraryWash(keep);
                        PreviewIdleLibrarySessionWithoutArtwork(keep);
                    }
                }

                SyncPlayerMeterFade();
                return;
            }

            if (playFirst)
            {
                SelectAndPlayFirstLibraryTrack(deferAmbientWash: deferWash);
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
                if (deferWash)
                {
                    QueuePendingLibraryWash(active);
                    _ = PlayLibrarySessionAsync(active, keepAmbientGlow: true);
                }
                else
                {
                    _ = PlayLibrarySessionAsync(active);
                }
            }

            SyncPlayerMeterFade();
        }
        finally
        {
            if (showProgress)
            {
                ClearOpenStatus();
            }

            if (deferWash
                && LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                    batchGeneration,
                    _libraryFolderShowGeneration))
            {
                _libraryFolderShowInProgress = false;
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }
        }
    }


    private void LibraryBrowser_ExplorerFoldersOpened(object? sender, IReadOnlyList<string> folders)
    {
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        var existing = LibraryShellFiles.ExistingPaths(folders);
        if (existing.Length == 0)
        {
            return;
        }

        _ = OpenLibraryFoldersRecursiveAsync(
            existing,
            playFirst: false,
            recursive: LibraryBrowser.ExplorerOpenRecursive);
    }

    private async void LibraryBrowser_ExplorerFolderOpened(object? sender, string path)
    {
        if (_libraryFolderShowInProgress)
        {
            return;
        }

        var existing = LibraryShellFiles.ExistingPaths([path]);
        if (existing.Length == 0)
        {
            return;
        }

        await OpenLibraryFoldersRecursiveAsync(
            existing,
            playFirst: false,
            recursive: LibraryBrowser.ExplorerOpenRecursive).ConfigureAwait(true);
    }

    /// <summary>
    /// ツリーの Enter／Ctrl+Enter（クリア系）。
    /// プレイリストを空にしてから載せる。再帰は <see cref="LibraryBrowserView.ExplorerOpenRecursive"/>。
    /// 追加の最中でも、別フォルダへのクリア追加で進行中を中断して差し替える。
    /// </summary>
    private void ReplaceLibraryFromExplorerFolder()
    {
        // 欠損（赤字）ルートの Enter はスルー。クリアだけ走ると空リストなのにメーターが出る。
        var folders = LibraryShellFiles.ExistingPaths(LibraryBrowser.SelectedExplorerFolders);
        if (folders.Length == 0)
        {
            return;
        }

        // TryClear → CancelLibraryFolderShow で世代を進め、進行中の追加を破棄する。
        _libraryHoldJacketWash = true;
        if (!TryClearLibrarySessions())
        {
            _libraryHoldJacketWash = false;
            return;
        }

        // イベント経由だと共有フラグ／再入で打ち切られるので、再生意図を引数で直接渡す。
        _ = OpenLibraryFoldersRecursiveAsync(
            folders,
            LibraryBrowser.SnapshotExplorerPlaylistWalk(),
            playFirst: LibraryBrowser.AutoPlayEnabled,
            recursive: LibraryBrowser.ExplorerOpenRecursive,
            focusPlaylist: true);
    }

    /// <summary>
    /// フォルダ配下（と単体ファイル）を収集し、1 曲ずつ載せる。
    /// 再生するのはクリアして追加かつ Auto Play オンのときだけ。
    /// Shift 付き追加は再生も停止もしない。
    /// ツリーの Enter（クリア追加）は Auto Play のオン／オフに関わらず、載せ終わったあとプレイリストへフォーカスする。
    /// クリア追加の再生は先頭が音声なら見つけ次第、動画／PDF は全件追加後。
    /// 検索中のツリーは見えているフォルダだけ。ファイル名ヒットはそのファイル、フォルダ名ヒットは配下すべて。
    /// お気に入りは検索フィルタを掛けない。
    /// </summary>
    private async Task OpenLibraryFoldersRecursiveAsync(
        IReadOnlyList<string> folders,
        LibraryExplorerPlaylistWalk? walk = null,
        bool playFirst = false,
        bool recursive = true,
        bool placeByCurrentSort = false,
        bool focusPlaylist = false)
    {
        LibraryPlaylistDocuments.ApplyFromSettings(AppStorage.Settings);
        var generation = ++_libraryFolderShowGeneration;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = true;
        ClearPendingLibraryWash();
        // 前の追加が残した進捗表示を消す（世代不一致の finally では消さないため）。
        ClearOpenStatus();
        var filter = walk ?? LibraryBrowser.SnapshotExplorerPlaylistWalk();
        var roots = new List<(string Path, bool AncestorHit)>(folders.Count);
        for (var i = 0; i < folders.Count; i++)
        {
            if (PathUtil.TryGetFullPath(folders[i], out var full))
            {
                roots.Add((full, false));
            }
        }

        if (roots.Count == 0)
        {
            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                _libraryFolderShowInProgress = false;
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }

            return;
        }

        _libraryPlayFirstPending = false;
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is { Length: > 0 } path)
            {
                known.Add(path);
            }
        }

        var channel = Channel.CreateUnbounded<LibraryFolderProduceItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true,
            });
        var produce = Task.Run(() =>
        {
            try
            {
                ProduceLibraryFolderSessions(
                    roots,
                    filter,
                    known,
                    channel.Writer,
                    () => generation != _libraryFolderShowGeneration,
                    recursive);
            }
            finally
            {
                channel.Writer.TryComplete();
            }
        });

        var firstHandled = false;
        var folderPlayStarted = false;
        var playlistFocusTicket = 0;
        var playlistWasEmpty = _sessions.Count == 0;
        var loaded = 0;
        var planned = 0;
        var showProgress = false;
        var pending = new List<DocumentSession>();
        var flushClock = Stopwatch.StartNew();

        bool Alive() => generation == _libraryFolderShowGeneration && IsLibraryMaximized;

        void NotePlanned(int count)
        {
            if (count <= 0)
            {
                return;
            }

            planned += count;
            if (planned > 1)
            {
                showProgress = true;
            }
        }

        bool FlushPending()
        {
            if (pending.Count == 0)
            {
                return true;
            }

            if (!Alive())
            {
                return false;
            }

            var first = !firstHandled;
            var selectFirst = first
                && ShouldSelectAppendedLibrarySession(
                    playFirst,
                    firstInBatch: true,
                    playlistWasEmpty);
            var head = pending[0];
            if (placeByCurrentSort)
            {
                foreach (var session in pending)
                {
                    PlacePlaylistSession(session, selectFirst && ReferenceEquals(session, head));
                }
            }
            else
            {
                foreach (var session in pending)
                {
                    _sessions.Add(session);
                }
            }

            if (selectFirst)
            {
                ScanLibraryArtwork(head);
            }

            if (!placeByCurrentSort)
            {
                LibraryBrowser.AppendSessions(pending, selectFirst ? head : null);
            }
            SyncPlayerMeterFade();
            loaded += pending.Count;
            if (showProgress)
            {
                var name = Path.GetFileName(pending[^1].Document.SourcePath ?? string.Empty);
                SetOpenStatus(loaded, Math.Max(planned, loaded), name);
            }

            if (first && playFirst)
            {
                _libraryFolderPlaySession = head;
                if (ShouldPlayFolderFirstImmediately(head.Document.SourcePath))
                {
                    folderPlayStarted = true;
                    // 動的背景は全件載せ終わってから差し替える。
                    _ = PlayLibrarySessionAsync(head, keepAmbientGlow: true);
                    if (Alive())
                    {
                        playlistFocusTicket = LibraryBrowser.RequestListFocus();
                    }
                }
            }
            else if (playFirst && folderPlayStarted && IsPlaybackActive())
            {
                ScheduleLibraryGaplessPrefetch();
            }

            firstHandled = true;
            pending.Clear();
            flushClock.Restart();
            return Alive();
        }

        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync().ConfigureAwait(true))
            {
                if (!Alive())
                {
                    if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                            generation,
                            _libraryFolderShowGeneration))
                    {
                        LibraryBrowser.FinishIncrementalSessionLoad();
                        EnsurePendingLibraryWashFromPlaylist();
                    }

                    return;
                }

                if (item.PlanDelta > 0)
                {
                    NotePlanned(item.PlanDelta);
                    continue;
                }

                if (item.Session is null)
                {
                    continue;
                }

                pending.Add(item.Session);
                var flush = item.FlushNow
                    || (!firstHandled && playFirst)
                    || pending.Count >= LibraryPlayerMode.PlaylistLoadUiFlushCount
                    || flushClock.ElapsedMilliseconds >= LibraryPlayerMode.PlaylistLoadUiFlushMs;
                if (!flush)
                {
                    continue;
                }

                if (!FlushPending())
                {
                    if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                            generation,
                            _libraryFolderShowGeneration))
                    {
                        LibraryBrowser.FinishIncrementalSessionLoad();
                        EnsurePendingLibraryWashFromPlaylist();
                    }

                    return;
                }
            }

            if (!FlushPending())
            {
                if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                        generation,
                        _libraryFolderShowGeneration))
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    EnsurePendingLibraryWashFromPlaylist();
                }

                return;
            }

            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration)
                && IsLibraryMaximized)
            {
                // 列・波形の仕上げと並行してウォッシュを裏で作り、バッチ終了後に 1 回だけ Apply。
                Task<LibraryBrowserView.PreparedArtworkPresentation>? artPrepare = null;
                DocumentSession? artSession = null;
                var artHadArt = false;
                if (firstHandled && playFirst && _libraryFolderPlaySession is { } playHead)
                {
                    artSession = playHead;
                    artHadArt = playHead.Document.HasArtwork;
                    artPrepare = Task.Run(() =>
                    {
                        ScanLibraryArtwork(playHead);
                        return LibraryBrowserView.PrepareArtworkPresentation(playHead.Document.Artwork);
                    });
                }
                else if (firstHandled && !playFirst && LibraryBrowser.SelectedSession is { } selectedIdle)
                {
                    artSession = selectedIdle;
                    artHadArt = selectedIdle.Document.HasArtwork;
                    artPrepare = Task.Run(() =>
                    {
                        ScanLibraryArtwork(selectedIdle);
                        return LibraryBrowserView.PrepareArtworkPresentation(selectedIdle.Document.Artwork);
                    });
                }

                LibraryBrowser.FinishIncrementalSessionLoad();
                if (firstHandled)
                {
                    if (playFirst && _libraryFolderPlaySession is { } first)
                    {
                        var selected = LibraryBrowser.SelectedSession;
                        if (ShouldRestoreFolderPlaySelection(selected, first))
                        {
                            LibraryBrowser.SelectSessionQuiet(first);
                        }

                        QueuePendingLibraryWash(first, artPrepare, artHadArt);
                        if (!folderPlayStarted)
                        {
                            // 再生開始はウォッシュ準備と並行。色替えはバッチ終了 Flush で Apply。
                            var playTask = PlayLibrarySessionAsync(first, keepAmbientGlow: true);
                            await playTask.ConfigureAwait(true);
                            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                    generation,
                                    _libraryFolderShowGeneration)
                                && IsLibraryMaximized)
                            {
                                LibraryBrowser.RequestListFocus();
                            }
                        }
                        else
                        {
                            if (IsPlaybackActive())
                            {
                                ScheduleLibraryGaplessPrefetch();
                            }

                            // 再生開始直後に移しても、続きの行追加でツリーへ戻る。
                            // 仕上げのあと、そのあいだにツリーへ残す操作が無ければもう一度移す。
                            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                    generation,
                                    _libraryFolderShowGeneration)
                                && IsLibraryMaximized)
                            {
                                LibraryBrowser.RequestListFocusIfCurrent(playlistFocusTicket);
                            }
                        }
                    }
                    else if (!playFirst)
                    {
                        if (artSession is not null)
                        {
                            QueuePendingLibraryWash(artSession, artPrepare, artHadArt);
                            PreviewIdleLibrarySessionWithoutArtwork(artSession);
                        }
                        else
                        {
                            PreviewIdleLibrarySession(LibraryBrowser.SelectedSession);
                        }

                        // 再生は Auto Play オンのときだけ。フォーカスはツリーの Enter なら常にプレイリストへ。
                        if (focusPlaylist
                            && LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                generation,
                                _libraryFolderShowGeneration)
                            && IsLibraryMaximized)
                        {
                            LibraryBrowser.RequestListFocus();
                        }
                    }
                }
                else
                {
                    EnsurePendingLibraryWashFromPlaylist();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                EnsurePendingLibraryWashFromPlaylist();
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            try
            {
                await produce.ConfigureAwait(true);
            }
            catch (Exception)
            {
            }

            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration))
            {
                _libraryFolderPlaySession = null;
                _libraryFolderShowInProgress = false;
                if (showProgress)
                {
                    ClearOpenStatus();
                }

                // キャンセルで途中終了しても、載っている曲があればここで 1 回 Apply に乗る。
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>
    /// フォルダ走査とタグ読みはすべて裏スレッド。UI へは完成したセッションだけ流す。
    /// </summary>
    private static void ProduceLibraryFolderSessions(
        IReadOnlyList<(string Path, bool AncestorHit)> roots,
        LibraryExplorerPlaylistWalk filter,
        HashSet<string> knownPaths,
        ChannelWriter<LibraryFolderProduceItem> writer,
        Func<bool> cancelled,
        bool recursive = true)
    {
        var remaining = new Stack<(string Path, bool AncestorHit)>(roots.Count);
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            remaining.Push(roots[i]);
        }

        while (remaining.Count > 0)
        {
            if (cancelled())
            {
                return;
            }

            var current = remaining.Pop();
            if (!Directory.Exists(current.Path))
            {
                if (File.Exists(current.Path)
                    && AudioCodec.IsPlayerOpenable(current.Path)
                    && filter.IncludeFile(current.Path, current.AncestorHit)
                    && knownPaths.Add(current.Path))
                {
                    writer.TryWrite(LibraryFolderProduceItem.Plan(1));
                    try
                    {
                        var session = CreateLibrarySession(current.Path);
                        writer.TryWrite(LibraryFolderProduceItem.FromSession(session, flushNow: true));
                    }
                    catch (Exception)
                    {
                    }
                }

                continue;
            }

            string[] files;
            (string Path, bool AncestorHit)[] children;
            try
            {
                var folderHit = filter.FolderNameHit(current.Path, current.AncestorHit);
                AudioCodec.CollectPlayerOpenableDirectoryLayer(current.Path, out var layerFiles, out var layerChildren);
                var included = new List<string>();
                foreach (var file in layerFiles)
                {
                    if (filter.IncludeFile(file, folderHit) && knownPaths.Add(file))
                    {
                        included.Add(file);
                    }
                }

                var next = new List<(string Path, bool AncestorHit)>();
                foreach (var child in layerChildren)
                {
                    if (filter.IncludeChild(child, folderHit))
                    {
                        next.Add((child, folderHit));
                    }
                }

                files = included.ToArray();
                children = next.ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (files.Length > 0)
            {
                writer.TryWrite(LibraryFolderProduceItem.Plan(files.Length));
            }

            // キャッシュが揃っていれば層ごと作って一気に流す。欠けがあるときは1曲ずつ。
            if (files.Length > 1 && LibraryPlaylistCache.AreAllWarm(files))
            {
                for (var i = 0; i < files.Length; i++)
                {
                    if (cancelled())
                    {
                        return;
                    }

                    try
                    {
                        var session = CreateLibrarySession(files[i]);
                        writer.TryWrite(
                            LibraryFolderProduceItem.FromSession(session, flushNow: i + 1 == files.Length));
                    }
                    catch (Exception)
                    {
                    }
                }
            }
            else
            {
                for (var i = 0; i < files.Length; i++)
                {
                    if (cancelled())
                    {
                        return;
                    }

                    try
                    {
                        var session = CreateLibrarySession(files[i]);
                        writer.TryWrite(LibraryFolderProduceItem.FromSession(session, flushNow: false));
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            if (!recursive)
            {
                continue;
            }

            for (var i = children.Length - 1; i >= 0; i--)
            {
                remaining.Push(children[i]);
            }
        }
    }

    private readonly struct LibraryFolderProduceItem
    {
        private LibraryFolderProduceItem(DocumentSession? session, int planDelta, bool flushNow)
        {
            Session = session;
            PlanDelta = planDelta;
            FlushNow = flushNow;
        }

        public DocumentSession? Session { get; }

        public int PlanDelta { get; }

        public bool FlushNow { get; }

        public static LibraryFolderProduceItem Plan(int count) => new(null, count, false);

        public static LibraryFolderProduceItem FromSession(DocumentSession session, bool flushNow) =>
            new(session, 0, flushNow);
    }

    /// <summary>
    /// フォルダ登録完了時に先頭曲へ選択を戻してよいか。
    /// 読み込み中にユーザーが別行へ移していれば false（シアンを奪わない）。
    /// </summary>
    internal static bool ShouldRestoreFolderPlaySelection(
        DocumentSession? selected,
        DocumentSession first) =>
        selected is null || ReferenceEquals(selected, first);

    /// <summary>
    /// Enter の先頭再生を全件追加前に始めてよいか。
    /// 動画／PDF は Open と後続追加が競合するので待つ。
    /// </summary>
    internal static bool ShouldPlayFolderFirstImmediately(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !LibraryPlaylistDocuments.IsDocument(path);

    /// <summary>
    /// 追加した曲をリストで選ぶか。Enter 再生は先頭のみ。Shift+Enter は空リストへ足したときだけ 1 行目を選ぶ。
    /// </summary>
    internal static bool ShouldSelectAppendedLibrarySession(
        bool playFirst,
        bool firstInBatch,
        bool playlistWasEmpty) =>
        firstInBatch && (playFirst || playlistWasEmpty);

    /// <summary>進行中のフォルダ再帰追加を破棄する（Enter 置換・ドロップなど）。</summary>
    private void CancelLibraryFolderShow()
    {
        _libraryFolderShowGeneration++;
        _libraryArtworkShowGeneration++;
        _libraryFolderPlaySession = null;
        _libraryFolderShowInProgress = false;
        // 差し替え側のバッチが新しい保留を積む。旧バッチの Flush は世代不一致で走らない。
        ClearPendingLibraryWash();
        ClearOpenStatus();
    }
}
