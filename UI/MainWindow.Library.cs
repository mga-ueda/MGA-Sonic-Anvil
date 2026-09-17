using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    private bool _libraryPlayOnArrowRelease;
    private bool _libraryWaapiSuspended;
    private bool _restoreWaapiAfterLibrary;
    private readonly HashSet<AudioDocument> _libraryPeakJobs = [];
    private int _libraryFolderShowGeneration;
    private string? _libraryFolderShownPath;

    internal bool IsLibraryMaximized => _waveformMaximizeMode == WaveformMaximizeMode.Library;

    internal bool IsLibraryListFocused =>
        IsLibraryMaximized && LibraryBrowser.IsListKeyboardFocused;

    internal bool IsLibraryGroupComboFocused =>
        IsLibraryMaximized && LibraryBrowser.IsGroupComboFocused;

    internal bool IsLibraryExplorerFocused =>
        IsLibraryMaximized && LibraryBrowser.IsExplorerFocused;

    private void ToggleLibraryMaximize() => SetWaveformMaximizeMode(
        _waveformMaximizeMode == WaveformMaximizeMode.Library
            ? WaveformMaximizeMode.Off
            : WaveformMaximizeMode.Library);

    private void ApplyLibraryChrome()
    {
        var show = IsLibraryMaximized;
        LibraryBrowser.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
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

        ForEachWaveform(view => view.SeekAndSelectOnly = show);
        RefreshTransportCommandsEnabled();
        ApplyLibraryWaapi(show);
        ApplyLibraryPlayerHost(show);
        if (show)
        {
            ProbeLibraryTags();
            // ツリーは既に選択済みでも SelectedItemChanged が再発火しないので、突入時に一覧を載せる。
            // 起動／ドロップの RegisterLibraryPaths は generation を進めて、この置換を取り消す。
            if (LibraryBrowser.TryGetSelectedExplorerFolder(out var folder))
            {
                _libraryFolderShownPath = null;
                ScheduleShowLibraryFolder(folder);
            }
            else if (_libraryPlayFirstPending)
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
        }
        else
        {
            _libraryPlayFirstPending = false;
            _libraryPlayOnArrowRelease = false;
            UpgradeLibraryPeaksForEditor();
            if (_activeSession?.Document is { } leaving
                && (leaving.IsDeferredLoad || leaving.IsStreamPlayback))
            {
                _ = EnsureLibrarySessionLoadedAsync(_activeSession);
            }
        }
    }

    private void UpgradeLibraryPeaksForEditor()
    {
        foreach (var session in _sessions)
        {
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
            if (_activeSession is not null)
            {
                BindSingleWorkspace(_activeSession);
            }

            return;
        }

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

    private void KeepLibraryListActive()
    {
        if (IsLibraryMaximized
            && !IsLibraryGroupComboFocused
            && !IsLibraryExplorerFocused
            && !LibraryBrowser.IsColumnFilterFocused
            && !LibraryBrowser.IsListKeyboardFocused)
        {
            LibraryBrowser.FocusList();
        }
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

        if (cancelled)
        {
            return false;
        }

        ApplyLibraryHandoffSelection(keep, current);
        return true;
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

    private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_waveMenu is { IsOpen: true })
        {
            return;
        }

        if (e.OriginalSource is DependencyObject origin
            && (LibraryBrowser.IsColumnFilterOrigin(origin)
                || LibraryBrowser.IsColumnFilterFocused
                || LibraryBrowser.IsExplorerOrigin(origin)
                || LibraryBrowser.IsExplorerFocused
                || LibraryBrowser.IsGroupComboOrigin(origin)
                || LibraryBrowser.IsGroupComboFocused
                || IsComboOrPopupOrigin(origin)))
        {
            return;
        }

        if (!IsLibraryMaximized)
        {
            return;
        }

        // Preview 中にリストへフォーカスすると、Silent Skip などの CheckBox が
        // LostKeyboardFocus で IsPressed を落としてクリックが無効になる。
        Dispatcher.BeginInvoke(KeepLibraryListActive, DispatcherPriority.Input);
    }

    private static bool IsComboOrPopupOrigin(DependencyObject? origin)
    {
        while (origin is not null)
        {
            if (origin is ComboBox or System.Windows.Controls.Primitives.Popup)
            {
                return true;
            }

            origin = System.Windows.Media.VisualTreeHelper.GetParent(origin)
                ?? System.Windows.LogicalTreeHelper.GetParent(origin);
        }

        return false;
    }

    private void LibraryBrowser_SessionActivated(object? sender, DocumentSession session)
    {
        _ = PlayLibrarySessionAsync(session);
        KeepLibraryListActive();
    }

    private void RegisterLibraryPaths(IReadOnlyList<string> paths)
    {
        // 起動・ドロップを優先し、突入時のフォルダ一覧置換が後から上書きしないようにする。
        _libraryFolderShowGeneration++;
        var playFirst = _libraryPlayFirstPending || _sessions.Count == 0;
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
        if (playFirst)
        {
            SelectAndPlayFirstLibraryTrack();
            _libraryPlayFirstPending = false;
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
    }

    /// <summary>
    /// リスト選択をプレイリストから外す。ファイルの削除はしない。
    /// </summary>
    private void RemoveLibrarySelectedFromList()
    {
        var selected = LibraryBrowser.SelectedSessions;
        if (selected.Length == 0)
        {
            return;
        }

        var drop = selected;
        var dropSet = new HashSet<DocumentSession>(drop);
        DocumentSession? next = null;
        var anchor = LibraryBrowser.SelectedSession ?? _activeSession;
        if (anchor is not null)
        {
            var order = _sessions.ToList();
            var index = order.IndexOf(anchor);
            for (var i = index + 1; i < order.Count; i++)
            {
                if (!dropSet.Contains(order[i]))
                {
                    next = order[i];
                    break;
                }
            }

            if (next is null)
            {
                for (var i = index - 1; i >= 0; i--)
                {
                    if (!dropSet.Contains(order[i]))
                    {
                        next = order[i];
                        break;
                    }
                }
            }
        }

        if (IsPlaybackActive() && _activeSession is { } playing && dropSet.Contains(playing))
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

                // リストから外すだけ。閉じたタブ再開にも残さない。ファイルは消さない。
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
            return;
        }

        if (_sessions.Count == 0)
        {
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);
            return;
        }

        next ??= _sessions[0];
        LibraryBrowser.SetSessions(_sessions, next, [next]);
        _ = PlayLibrarySessionAsync(next);
        KeepLibraryListActive();
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
        LibraryBrowser.SetArtwork(first?.Document?.Artwork);
        _ = PlayLibrarySessionAsync(first);
    }

    private void ShowLibraryArtworkOrClear(DocumentSession? session)
    {
        if (session is null)
        {
            LibraryBrowser.SetArtwork(null);
            return;
        }

        ShowLibraryArtwork(session);
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
        _ = PlayLibrarySessionAsync(LibraryBrowser.SelectedSession ?? _activeSession);
    }

    private async Task PlayLibrarySessionAsync(DocumentSession? session)
    {
        _libraryPlayOnArrowRelease = false;
        if (session is null)
        {
            LibraryBrowser.SetArtwork(null);
            return;
        }

        await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
        if (session.Document.IsDeferredLoad
            || !ReferenceEquals(_libraryLoadSession, session))
        {
            return;
        }

        if (IsPlaybackActive())
        {
            StopPlayback();
        }

        ApplyLoadedLibrarySession(session);
        StartPlayback(0, prerollSeconds: 0);
        ShowLibraryArtwork(session);
        // 再生中の全デコードは無音の原因になるので、停止後に波形ピークを作る。
        _ = DeferStreamPeaksUntilIdleAsync(session);
        KeepLibraryListActive();
    }

    private async void PlayLibrarySelectionOrToggle()
    {
        var session = LibraryBrowser.SelectedSession ?? _activeSession;
        if (session is null)
        {
            TogglePlayback();
            KeepLibraryListActive();
            return;
        }

        await EnsureLibrarySessionLoadedAsync(session).ConfigureAwait(true);
        if (!session.Document.IsDeferredLoad)
        {
            ApplyLoadedLibrarySession(session);
            TogglePlayback();
            ShowLibraryArtwork(session);
            if (!IsPlaybackActive())
            {
                _ = FillLibraryPeaksAsync(session);
            }
            else
            {
                _ = DeferStreamPeaksUntilIdleAsync(session);
            }
        }

        KeepLibraryListActive();
    }

    private Task EnsureLibrarySessionLoadedAsync(DocumentSession session)
    {
        if (IsPlaybackActive() && !ReferenceEquals(session, _activeSession))
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
            return LoadDeferredLibrarySessionAsync(session, gen);
        }

        if (!session.Document.IsDeferredLoad)
        {
            return Task.CompletedTask;
        }

        return LoadDeferredLibrarySessionAsync(session, gen);
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
                await LoadDeferredLibrarySessionAsync(session, generation).ConfigureAwait(true);
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

    private async Task LoadDeferredLibrarySessionAsync(DocumentSession session, int generation)
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
            return;
        }

        ApplyLoadedLibrarySession(session);
        _ = FillLibraryPeaksAsync(session);
    }

    private async Task FillLibraryPeaksAsync(DocumentSession session)
    {
        var document = session.Document;
        if (document.IsDeferredLoad)
        {
            return;
        }

        // 再生中に同じファイルを全デコードすると MediaFoundation が競合して無音になる。
        if (document.IsStreamPlayback && IsPlaybackActive())
        {
            _ = DeferStreamPeaksUntilIdleAsync(session);
            return;
        }

        var display = IsLibraryMaximized;
        if (!document.Peaks.IsEmpty && (display || !document.Peaks.NeedsEditorDetail))
        {
            return;
        }

        if (!_libraryPeakJobs.Add(document))
        {
            return;
        }

        var retry = false;
        try
        {
            PeakPyramid peaks;
            if (document.IsStreamPlayback
                && document.SourcePath is { Length: > 0 } path
                && File.Exists(path))
            {
                peaks = await Task.Run(() =>
                {
                    using var source = AudioStreamSource.Open(path);
                    return PeakPyramid.BuildPlayerDisplayStreaming(source);
                }).ConfigureAwait(true);
            }
            else
            {
                var interleaved = document.Interleaved;
                var channels = document.Channels;
                var sampleCount = document.SampleCount;
                peaks = await Task.Run(() => display
                        ? PeakPyramid.BuildPlayerDisplay(interleaved, channels, sampleCount)
                        : PeakPyramid.Build(interleaved, channels, sampleCount))
                    .ConfigureAwait(true);
            }

            if (!ReferenceEquals(session.Document, document))
            {
                return;
            }

            if (IsLibraryMaximized != display && !document.IsStreamPlayback)
            {
                retry = true;
                return;
            }

            document.ReplacePeaks(peaks);
            if (ReferenceEquals(_activeSession, session))
            {
                Waveform.Refresh();
                if (!IsLibraryMaximized)
                {
                    Overview.Refresh();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or InvalidOperationException)
        {
            // ピークだけ失敗しても再生は続ける。
        }
        finally
        {
            _libraryPeakJobs.Remove(document);
        }

        if (retry)
        {
            _ = FillLibraryPeaksAsync(session);
        }
    }

    private async Task DeferStreamPeaksUntilIdleAsync(DocumentSession session)
    {
        for (var i = 0; i < 120; i++)
        {
            await Task.Delay(250).ConfigureAwait(true);
            if (!IsLibraryMaximized || !ReferenceEquals(_activeSession, session))
            {
                return;
            }

            if (!IsPlaybackActive())
            {
                await FillLibraryPeaksAsync(session).ConfigureAwait(true);
                return;
            }
        }
    }

    private void ApplyLoadedLibrarySession(DocumentSession session)
    {
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
            return;
        }

        if (needsBind)
        {
            BindWorkspace(session);
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
        LibraryBrowser.SetArtwork(session.Document.Artwork);
        if (!hadArt && session.Document.HasArtwork)
        {
            LibraryBrowser.UpdateSessionRow(session);
        }
    }

    private static void ScanLibraryArtwork(DocumentSession session)
    {
        var document = session.Document;
        if (document.HasArtwork
            || document.SourceKind != AudioFileKind.Mp3
            || document.SourcePath is not { Length: > 0 } path
            || !File.Exists(path))
        {
            return;
        }

        if (Id3Artwork.TryRead(path, out var artwork))
        {
            document.SetArtwork(artwork);
        }
    }

    private void LibraryBrowser_ArtworkDropped(object? sender, string path)
    {
        if (_activeSession is not { } session || IsUiBusy)
        {
            return;
        }

        if (!LibraryBrowserView.TryReadImageBytes(path, out var bytes))
        {
            return;
        }

        var document = session.Document;
        document.SetArtwork(bytes);
        LibraryBrowser.SetArtwork(bytes);
        if (document.SourceKind == AudioFileKind.Mp3
            && document.SourcePath is { Length: > 0 } filePath
            && File.Exists(filePath))
        {
            Id3Artwork.TryWrite(filePath, bytes);
            document.RefreshFileBytes();
        }

        LibraryBrowser.UpdateSessionRow(session);
        RefreshStatus();
        RefreshTitle();
        KeepLibraryListActive();
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
        if (IsLibraryMaximized)
        {
            ScheduleShowLibraryFolder(path);
        }
        else
        {
            AppStorage.Save();
        }
    }

    private void LibraryBrowser_ExplorerWidthChanged(object? sender, double width)
    {
        AppStorage.Settings.LibraryExplorerWidth = DesignMetrics.ClampLibraryExplorerWidth(width);
        AppStorage.Save();
    }

    private void LibraryBrowser_ExplorerFolderOpened(object? sender, string path)
    {
        // ツリーからの読み込みは直下のみ。再帰すると大量ファイルで極端に重くなる。
        var files = AudioCodec.CollectPlayerOpenableFromDirectory(path, recursive: false);
        if (files.Length == 0)
        {
            return;
        }

        RegisterLibraryPaths(files);
    }

    /// <summary>
    /// ツリー選択の表示。キー連打で毎回走査しないよう短く待ち、直下のファイルを 1 件ずつリストへ載せる。
    /// フォルダ移動で generation が進むと途中キャンセルする。
    /// </summary>
    private async void ScheduleShowLibraryFolder(string path)
    {
        var generation = ++_libraryFolderShowGeneration;
        try
        {
            await Task.Delay(180).ConfigureAwait(true);
            if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
            {
                return;
            }

            AppStorage.Save();

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return;
            }

            if (string.Equals(_libraryFolderShownPath, full, StringComparison.OrdinalIgnoreCase)
                && _sessions.Count > 0)
            {
                return;
            }

            var files = await Task.Run(
                    () => AudioCodec.CollectPlayerOpenableFromDirectory(full, recursive: false))
                .ConfigureAwait(true);
            if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
            {
                return;
            }

            if (!TryClearLibrarySessions())
            {
                return;
            }

            _libraryFolderShownPath = null;
            LibraryBrowser.SetSessions(_sessions, null);
            LibraryBrowser.SetArtwork(null);

            if (files.Length == 0)
            {
                _libraryPlayFirstPending = false;
                _libraryFolderShownPath = full;
                return;
            }

            var playFirst = _libraryPlayFirstPending;
            _libraryPlayFirstPending = false;
            var firstHandled = false;

            for (var i = 0; i < files.Length; i++)
            {
                if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    return;
                }

                var file = files[i];
                var session = await Task.Run(() =>
                {
                    var document = AudioDocument.CreateDeferred(file);
                    AudioTagProbe.Ensure(document);
                    return new DocumentSession(document);
                }).ConfigureAwait(true);

                if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
                {
                    LibraryBrowser.FinishIncrementalSessionLoad();
                    return;
                }

                _sessions.Add(session);
                var select = !firstHandled;
                if (select)
                {
                    firstHandled = true;
                    ScanLibraryArtwork(session);
                }

                LibraryBrowser.AppendSession(session, select);
                if (select)
                {
                    LibraryBrowser.SetArtwork(session.Document.Artwork);
                    if (playFirst)
                    {
                        _ = PlayLibrarySessionAsync(session);
                    }
                }

                // 1 件ごとに UI へ制御を返す（キー連打・描画を止めない）。
                await Dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);
            }

            if (generation != _libraryFolderShowGeneration || !IsLibraryMaximized)
            {
                LibraryBrowser.FinishIncrementalSessionLoad();
                return;
            }

            _libraryFolderShownPath = full;
            LibraryBrowser.FinishIncrementalSessionLoad();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private bool TryClearLibrarySessions()
    {
        if (_sessions.Count == 0)
        {
            return true;
        }

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
            _libraryFolderShownPath = null;
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

        if (!cancelled)
        {
            _libraryFolderShownPath = null;
        }

        return !cancelled;
    }
}
