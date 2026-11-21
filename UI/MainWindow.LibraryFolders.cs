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
        if (deferWash || showProgress)
        {
            SetLibraryFolderShowInProgress(true);
            if (showProgress)
            {
                LibraryBrowser.SetPlaylistEnumerateProgress(0, paths.Count);
            }
        }

        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                if (showProgress)
                {
                    SetOpenStatus(i + 1, paths.Count, Path.GetFileName(path) ?? path);
                    LibraryBrowser.SetPlaylistEnumerateProgress(i + 1, paths.Count);
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

            if ((deferWash || showProgress)
                && LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                    batchGeneration,
                    _libraryFolderShowGeneration))
            {
                SetLibraryFolderShowInProgress(false);
                if (deferWash)
                {
                    await FlushPendingLibraryWashAsync().ConfigureAwait(true);
                }
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
    /// 大量追加はフォルダ名を先に集めて見出し順に並べ、フォルダごとにファイル名スタブを流す。
    /// タグ／列は後追いし、並び確定後の表示冒頭だけを Auto Play する（読み込み中の仮順では鳴らさない）。
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
        SetLibraryFolderShowInProgress(true);
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
                SetLibraryFolderShowInProgress(false);
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
        var playlistWasEmpty = _sessions.Count == 0;
        var loaded = 0;
        var planned = 0;
        var showProgress = false;
        var pending = new List<DocumentSession>();
        var batchSessions = new List<DocumentSession>();
        var enrichTasks = new List<Task>();
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

            // 行がまだ無くても赤バッジで件数を出す。
            LibraryBrowser.SetPlaylistEnumerateProgress(loaded, planned);
        }

        void RefreshEnumerateBadge(string? label = null)
        {
            if (label is not null)
            {
                LibraryBrowser.SetPlaylistEnumerateBusy(true, label);
                return;
            }

            LibraryBrowser.SetPlaylistEnumerateProgress(loaded, planned);
        }

        void ScheduleTagEnrich(IReadOnlyList<DocumentSession> sessions)
        {
            if (sessions.Count == 0)
            {
                return;
            }

            // UI は触らずタグだけ先読み。行の差し替えは全スタブ後に一度だけ。
            var copy = sessions.ToArray();
            enrichTasks.Add(EnrichPlaylistSessionsTagsOnlyAsync(copy, generation));
        }

        async Task<bool> FlushPendingAsync()
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
            // Auto Play は並び確定後に表示冒頭を選ぶ。途中は空リストへ足したときだけ仮選択。
            var selectFirst = first
                && !playFirst
                && ShouldSelectAppendedLibrarySession(
                    playFirst: false,
                    firstInBatch: true,
                    playlistWasEmpty);
            var head = pending[0];
            if (placeByCurrentSort)
            {
                // 既存リストへの差し込みは、ソートキーを揃えてから正しい位置へ入れる。
                var placeBatch = pending.ToArray();
                await Task.Run(() =>
                {
                    foreach (var session in placeBatch)
                    {
                        EnrichLibrarySessionTags(session);
                    }
                }).ConfigureAwait(true);
                if (!Alive())
                {
                    return false;
                }

                foreach (var session in pending)
                {
                    PlacePlaylistSession(session, selectFirst && ReferenceEquals(session, head));
                    batchSessions.Add(session);
                }
            }
            else
            {
                foreach (var session in pending)
                {
                    _sessions.Add(session);
                    batchSessions.Add(session);
                }

                // ファイル名スタブを末尾へ先出し。生産側がフォルダ名順で流すので
                // ここでの Lock はしない（毎フラッシュの再バインドが重い）。最終ロックは後で。
                LibraryBrowser.AppendSessionsRaw(pending, selectFirst ? head : null);
                ScheduleTagEnrich(pending);
            }

            if (selectFirst)
            {
                ScanLibraryArtwork(head);
            }

            SyncPlayerMeterFade();
            loaded += pending.Count;
            RefreshEnumerateBadge();
            if (showProgress)
            {
                var name = Path.GetFileName(pending[^1].Document.SourcePath ?? string.Empty);
                SetOpenStatus(loaded, Math.Max(planned, loaded), name);
            }

            // 行を描画してから次の受信へ進む。再生は並び確定後のみ（途中の仮順で鳴らさない）。
            await Dispatcher.Yield(DispatcherPriority.Render);

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
                    || (!firstHandled && (playFirst || playlistWasEmpty))
                    || pending.Count >= LibraryPlayerMode.PlaylistLoadUiFlushCount
                    || flushClock.ElapsedMilliseconds >= LibraryPlayerMode.PlaylistLoadUiFlushMs;
                if (!flush)
                {
                    continue;
                }

                if (!await FlushPendingAsync().ConfigureAwait(true))
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

            if (!await FlushPendingAsync().ConfigureAwait(true))
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

            if (enrichTasks.Count > 0 || (!placeByCurrentSort && batchSessions.Count > 0))
            {
                RefreshEnumerateBadge(UiStrings.PlaylistBusyEnumerateFinalize);
            }

            if (enrichTasks.Count > 0)
            {
                await Task.WhenAll(enrichTasks).ConfigureAwait(true);
            }

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

            if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(generation, _libraryFolderShowGeneration)
                && IsLibraryMaximized)
            {
                DocumentSession? playlistHead = null;
                if (firstHandled && !placeByCurrentSort && batchSessions.Count > 0)
                {
                    // タグを行へ一度だけ載せ、現行ソートで表示を確定する。冒頭＝FirstPlaylistSession。
                    LibraryBrowser.UpdateSessionRows(batchSessions);
                    await Dispatcher.Yield(DispatcherPriority.Render);
                    var preserve = _libraryFolderPlaySession
                        ?? LibraryBrowser.SelectedSession
                        ?? _activeSession;
                    playlistHead = LibraryBrowser.LockPlaylistOrder(
                        preserve,
                        preserve is null ? null : [preserve]);
                    SyncSessionsToPlaylistOrder();
                    playlistHead = LibraryBrowser.FirstPlaylistSession ?? playlistHead;
                }
                else if (firstHandled)
                {
                    playlistHead = LibraryBrowser.FirstPlaylistSession
                        ?? LibraryBrowser.SelectedSession;
                }

                // 列・波形の仕上げと並行してウォッシュを裏で作り、バッチ終了後に 1 回だけ Apply。
                Task<LibraryBrowserView.PreparedArtworkPresentation>? artPrepare = null;
                DocumentSession? artSession = null;
                var artHadArt = false;
                if (firstHandled && playFirst && playlistHead is { } playHead)
                {
                    _libraryFolderPlaySession = playHead;
                    artSession = playHead;
                    artHadArt = playHead.Document.HasArtwork;
                    artPrepare = Task.Run(() =>
                    {
                        EnrichLibrarySessionMedia(playHead);
                        ScanLibraryArtwork(playHead);
                        return LibraryBrowserView.PrepareArtworkPresentation(playHead.Document.Artwork);
                    });
                }
                else if (firstHandled && !playFirst)
                {
                    artSession = playlistHead ?? LibraryBrowser.SelectedSession;
                    if (artSession is not null)
                    {
                        artHadArt = artSession.Document.HasArtwork;
                        var idle = artSession;
                        artPrepare = Task.Run(() =>
                        {
                            EnrichLibrarySessionMedia(idle);
                            ScanLibraryArtwork(idle);
                            return LibraryBrowserView.PrepareArtworkPresentation(idle.Document.Artwork);
                        });
                    }
                }

                LibraryBrowser.FinishIncrementalSessionLoad();
                if (batchSessions.Count > 0)
                {
                    _ = EnrichPlaylistSessionsMediaAsync(batchSessions.ToArray(), generation);
                }

                if (firstHandled)
                {
                    if (playFirst && playlistHead is { } first)
                    {
                        // 最終表示冒頭を選んで再生し続ける。
                        LibraryBrowser.SelectSessionQuiet(first);
                        QueuePendingLibraryWash(first, artPrepare, artHadArt);
                        if (!ReferenceEquals(_activeSession, first))
                        {
                            await PlayLibrarySessionAsync(first, keepAmbientGlow: true)
                                .ConfigureAwait(true);
                        }

                        if (LibraryPlayerMode.OwnsPlaylistBatchCompletion(
                                generation,
                                _libraryFolderShowGeneration)
                            && IsLibraryMaximized)
                        {
                            LibraryBrowser.RequestListFocus();
                        }
                    }
                    else if (!playFirst)
                    {
                        if (playlistWasEmpty
                            && artSession is not null
                            && ShouldRestoreFolderPlaySelection(LibraryBrowser.SelectedSession, artSession))
                        {
                            LibraryBrowser.SelectSessionQuiet(artSession);
                        }

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
                SetLibraryFolderShowInProgress(false);
                if (showProgress)
                {
                    ClearOpenStatus();
                }

                // キャンセルで途中終了しても、載っている曲があればここで 1 回 Apply に乗る。
                await FlushPendingLibraryWashAsync().ConfigureAwait(true);
            }
        }
    }

    /// <summary>タグだけ読む。行 UI は更新しない（大量追加中の再描画を避ける）。</summary>
    private async Task EnrichPlaylistSessionsTagsOnlyAsync(DocumentSession[] sessions, int generation)
    {
        await Task.Run(() =>
        {
            foreach (var session in sessions)
            {
                if (generation != _libraryFolderShowGeneration)
                {
                    return;
                }

                try
                {
                    EnrichLibrarySessionTags(session);
                }
                catch (Exception)
                {
                }
            }
        }).ConfigureAwait(true);
    }

    private async Task EnrichPlaylistSessionsMediaAsync(DocumentSession[] sessions, int generation)
    {
        await Task.Run(() =>
        {
            foreach (var session in sessions)
            {
                if (generation != _libraryFolderShowGeneration)
                {
                    return;
                }

                try
                {
                    EnrichLibrarySessionMedia(session);
                }
                catch (Exception)
                {
                }
            }
        }).ConfigureAwait(true);

        if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
        {
            return;
        }

        LibraryBrowser.UpdateSessionRows(sessions);
    }

    /// <summary>
    /// フォルダ走査は裏スレッド。
    /// 1) フォルダ名だけ再帰収集 → 見出し名順に並べる
    /// 2) その順でフォルダごとにファイル（名前順）をスタブ送信
    /// タグは UI 側で後追い。親フォルダグループ＋名前ソートなら流した順＝最終順。
    /// </summary>
    private static void ProduceLibraryFolderSessions(
        IReadOnlyList<(string Path, bool AncestorHit)> roots,
        LibraryExplorerPlaylistWalk filter,
        HashSet<string> knownPaths,
        ChannelWriter<LibraryFolderProduceItem> writer,
        Func<bool> cancelled,
        bool recursive = true)
    {
        var looseFiles = new List<string>();
        var folders = new List<(string Path, bool FolderHit)>();
        CollectLibraryFolderTargets(roots, filter, recursive, cancelled, looseFiles, folders);
        if (cancelled())
        {
            return;
        }

        if (looseFiles.Count > 0)
        {
            looseFiles.Sort(StringComparer.OrdinalIgnoreCase);
            EmitLibraryFolderFileBatch(looseFiles, knownPaths, writer, cancelled);
            if (cancelled())
            {
                return;
            }
        }

        if (folders.Count == 0)
        {
            return;
        }

        var folderPaths = new List<string>(folders.Count);
        var folderHits = new Dictionary<string, bool>(folders.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in folders)
        {
            folderPaths.Add(entry.Path);
            folderHits[entry.Path] = entry.FolderHit;
        }

        LibraryPlayerMode.SortFoldersByDisplayName(folderPaths);

        foreach (var folder in folderPaths)
        {
            if (cancelled())
            {
                return;
            }

            if (!folderHits.TryGetValue(folder, out var folderHit))
            {
                folderHit = false;
            }

            string[] layerFiles;
            try
            {
                AudioCodec.CollectPlayerOpenableDirectoryLayer(folder, out layerFiles, out _);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var included = new List<string>(layerFiles.Length);
            foreach (var file in layerFiles)
            {
                if (filter.IncludeFile(file, folderHit))
                {
                    included.Add(file);
                }
            }

            if (included.Count == 0)
            {
                continue;
            }

            EmitLibraryFolderFileBatch(included, knownPaths, writer, cancelled);
        }
    }

    /// <summary>ルートからフォルダ一覧と単体ファイルを集める（中身のファイル列挙はまだしない）。</summary>
    private static void CollectLibraryFolderTargets(
        IReadOnlyList<(string Path, bool AncestorHit)> roots,
        LibraryExplorerPlaylistWalk filter,
        bool recursive,
        Func<bool> cancelled,
        List<string> looseFiles,
        List<(string Path, bool FolderHit)> folders)
    {
        var remaining = new Stack<(string Path, bool AncestorHit)>(roots.Count);
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            remaining.Push(roots[i]);
        }

        var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                    && filter.IncludeFile(current.Path, current.AncestorHit))
                {
                    looseFiles.Add(current.Path);
                }

                continue;
            }

            bool folderHit;
            string[] children;
            try
            {
                folderHit = filter.FolderNameHit(current.Path, current.AncestorHit);
                children = AudioCodec.CollectPlayerOpenableChildDirectories(current.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (seenFolders.Add(current.Path))
            {
                folders.Add((current.Path, folderHit));
            }

            if (!recursive)
            {
                continue;
            }

            for (var i = children.Length - 1; i >= 0; i--)
            {
                var child = children[i];
                if (filter.IncludeChild(child, folderHit))
                {
                    remaining.Push((child, folderHit));
                }
            }
        }
    }

    private static void EmitLibraryFolderFileBatch(
        IReadOnlyList<string> files,
        HashSet<string> knownPaths,
        ChannelWriter<LibraryFolderProduceItem> writer,
        Func<bool> cancelled)
    {
        var toEmit = new List<string>(files.Count);
        foreach (var path in files)
        {
            if (knownPaths.Add(path))
            {
                toEmit.Add(path);
            }
        }

        if (toEmit.Count == 0)
        {
            return;
        }

        writer.TryWrite(LibraryFolderProduceItem.Plan(toEmit.Count));
        for (var i = 0; i < toEmit.Count; i++)
        {
            if (cancelled())
            {
                return;
            }

            try
            {
                var session = CreateLibrarySessionStub(toEmit[i]);
                writer.TryWrite(
                    LibraryFolderProduceItem.FromSession(
                        session,
                        flushNow: i + 1 == toEmit.Count));
            }
            catch (Exception)
            {
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
    /// 表示冒頭が音声なら即再生してよいか（動画／PDF は Open と競合しやすい）。
    /// フォルダ大量追加の Auto Play は並び確定後の表示冒頭を再生するため、ここでは使わない。
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
        SetLibraryFolderShowInProgress(false);
        // 差し替え側のバッチが新しい保留を積む。旧バッチの Flush は世代不一致で走らない。
        ClearPendingLibraryWash();
        ClearOpenStatus();
    }

    private void SetLibraryFolderShowInProgress(bool inProgress)
    {
        _libraryFolderShowInProgress = inProgress;
        LibraryBrowser.SetPlaylistEnumerateBusy(inProgress);
    }
}
