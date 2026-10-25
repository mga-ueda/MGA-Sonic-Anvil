using System.IO;
using System.Windows;
using System.Windows.Input;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    /// <summary>
    /// プレイヤーのファイル操作。貼り付け・移動・削除・名前変更・フォルダ作成・複製に undo は無い。
    /// </summary>
    private bool TryHandleLibraryFileShortcut(Key key, ModifierKeys modifiers)
    {
        var tree = IsLibraryExplorerFocused;
        var favorites = IsLibraryFavoritesFocused;
        var list = LibraryBrowser.IsListKeyboardFocused;

        if (key == Key.F2 && modifiers == ModifierKeys.None)
        {
            if (IsUiBusy || IsEditingFileName)
            {
                return true;
            }

            if (tree)
            {
                RenameExplorerSelection();
            }
            else if (favorites)
            {
                RenameFavoriteSelection();
            }
            else if (list)
            {
                RenamePlaylistSelection();
            }

            return true;
        }

        if (key == Key.F7 && modifiers == ModifierKeys.None)
        {
            if (!IsUiBusy && tree)
            {
                CreateExplorerFolder();
            }

            return true;
        }

        if (key == Key.C && modifiers == ModifierKeys.Control)
        {
            return favorites;
        }

        if (key == Key.X && modifiers == ModifierKeys.Control)
        {
            if (favorites || IsUiBusy)
            {
                return true;
            }

            if (tree || list)
            {
                LibraryBrowser.CutSelected();
                return true;
            }

            return false;
        }

        if (key == Key.V && modifiers == ModifierKeys.Control)
        {
            if (favorites || IsUiBusy)
            {
                return true;
            }

            if (tree || list)
            {
                PasteLibraryClipboard(tree);
                return true;
            }

            return false;
        }

        if (key == Key.D && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (favorites || (!tree && !list))
            {
                return favorites;
            }

            if (IsUiBusy)
            {
                return true;
            }

            if (tree)
            {
                DuplicateExplorerFolders();
            }
            else
            {
                DuplicatePlaylistFiles();
            }

            return true;
        }

        if (key == Key.Delete && modifiers == ModifierKeys.Control)
        {
            if (favorites)
            {
                return true;
            }

            if (IsUiBusy && (tree || list))
            {
                return true;
            }

            if (tree)
            {
                DeleteExplorerSelection();
                return true;
            }

            if (list)
            {
                DeletePlaylistFiles();
                return true;
            }

            return false;
        }

        return false;
    }

    private void PasteLibraryFromMenu(LibraryPane pane)
    {
        if (pane == LibraryPane.Favorites || IsUiBusy)
        {
            return;
        }

        PasteLibraryClipboard(pane == LibraryPane.Explorer);
    }

    private void RenameLibraryFromMenu(LibraryPane pane)
    {
        if (IsUiBusy || IsEditingFileName)
        {
            return;
        }

        switch (pane)
        {
            case LibraryPane.Explorer:
                RenameExplorerSelection();
                break;
            case LibraryPane.Favorites:
                RenameFavoriteSelection();
                break;
            case LibraryPane.List:
                RenamePlaylistSelection();
                break;
        }
    }

    private void DeleteLibraryFromMenu(LibraryPane pane)
    {
        if (pane == LibraryPane.Favorites || IsUiBusy)
        {
            return;
        }

        if (pane == LibraryPane.Explorer)
        {
            DeleteExplorerSelection();
            return;
        }

        DeletePlaylistFiles();
    }

    private void PasteLibraryClipboard(bool ontoTree)
    {
        var pane = ontoTree ? LibraryPane.Explorer : LibraryPane.List;
        try
        {
            PasteLibraryClipboardCore(ontoTree);
        }
        finally
        {
            if (LibraryBrowser.ClearCutGhost())
            {
                try
                {
                    Clipboard.Clear();
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                }
            }

            LibraryBrowser.FocusPane(pane);
        }
    }

    private void PasteLibraryClipboardCore(bool ontoTree)
    {
        if (!SystemClipboard.TryReadFileDrop(out var paths, out var move, out var origin))
        {
            ShowLibraryMessage(UiStrings.ErrorClipboardBusy, MessageBoxImage.Warning);
            return;
        }

        if (paths.Length == 0)
        {
            return;
        }

        if (!ontoTree)
        {
            if (string.Equals(origin, LibraryShellFiles.OriginPlaylist, StringComparison.Ordinal))
            {
                ShowLibraryMessage(UiStrings.LibrarySamePlace, MessageBoxImage.Information);
                return;
            }

            RegisterLibraryPaths(paths);
            LibraryBrowser.ShowTransferToast("Paste", paths);
            return;
        }

        if (!LibraryBrowser.TryGetSelectedExplorerFolder(out var folder))
        {
            ShowLibraryFailure(new DirectoryNotFoundException());
            return;
        }

        var completed = true;
        var pasted = new List<string>();
        foreach (var source in paths)
        {
            if (!TransferLibraryPath(source, folder, move))
            {
                completed = false;
                break;
            }

            pasted.Add(source);
        }

        LibraryBrowser.ShowTransferToast("Paste", pasted);

        if (completed && move)
        {
            try
            {
                Clipboard.Clear();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }
        }

        LibraryBrowser.RefreshExplorer();
    }

    private void RegisterLibraryPaths(IReadOnlyList<string> paths)
    {
        var ready = new List<string>();
        string? missing = null;
        foreach (var path in paths)
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                ready.Add(path);
            }
            else
            {
                missing ??= path;
            }
        }

        if (missing is not null)
        {
            ShowLibraryFailure(new FileNotFoundException(null, missing));
        }

        if (ready.Count == 0)
        {
            return;
        }

        _ = OpenLibraryFoldersRecursiveAsync(
            ready,
            playFirst: false,
            recursive: false,
            placeByCurrentSort: true);
    }

    private bool TransferLibraryPath(string source, string destinationDirectory, bool move)
    {
        string full;
        try
        {
            full = Path.GetFullPath(source);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ShowLibraryFailure(ex);
            return false;
        }

        var isDirectory = Directory.Exists(full);
        if (!isDirectory && !File.Exists(full))
        {
            ShowLibraryFailure(new FileNotFoundException(null, full));
            return false;
        }

        if (isDirectory && LibraryFileTransfer.IsReparsePoint(full))
        {
            ShowLibraryFailure(new IOException(UiStrings.ErrorLibraryReparse));
            return false;
        }

        if (!Directory.Exists(destinationDirectory))
        {
            ShowLibraryFailure(new DirectoryNotFoundException(destinationDirectory));
            return false;
        }

        if (isDirectory && LibraryFileTransfer.IsSameOrUnder(destinationDirectory, full))
        {
            ShowLibraryMessage(UiStrings.LibrarySamePlace, MessageBoxImage.Information);
            return false;
        }

        var name = Path.GetFileName(LibraryFileTransfer.NormalizePrefix(full));
        var dest = Path.Combine(destinationDirectory, name);
        var occupied = File.Exists(dest) || Directory.Exists(dest);
        var overwrite = false;
        if (occupied || LibraryFileTransfer.SameFullPath(full, dest))
        {
            var choice = ConfirmChoiceWindow.Show(
                this,
                UiStrings.LibraryNameConflict(name),
                UiStrings.AppName,
                MessageBoxButton.YesNoCancel,
                MessageBoxResult.No,
                UiStrings.LibraryOverwrite,
                UiStrings.LibraryRename,
                UiStrings.LibraryCancel);
            if (choice == MessageBoxResult.Cancel)
            {
                return false;
            }

            if (choice == MessageBoxResult.Yes)
            {
                if (LibraryFileTransfer.SameFullPath(full, dest)
                    || LibraryFileTransfer.IsSameOrUnder(full, dest)
                    || LibraryFileTransfer.IsSameOrUnder(dest, full))
                {
                    ShowLibraryMessage(UiStrings.LibrarySamePlace, MessageBoxImage.Information);
                    return false;
                }

                overwrite = true;
            }
            else
            {
                try
                {
                    dest = LibraryFileTransfer.UniquePath(destinationDirectory, name, isDirectory);
                }
                catch (IOException ex)
                {
                    ShowLibraryFailure(ex);
                    return false;
                }
            }
        }

        try
        {
            if (overwrite)
            {
                DeleteDestination(dest);
            }

            if (move)
            {
                MoveLibraryPath(full, dest, isDirectory);
                FinishLibraryMove(full, dest, isDirectory);
            }
            else if (isDirectory)
            {
                CopyLibraryDirectory(full, dest, cloneCatalog: true);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Copy(full, dest));
            }
            else
            {
                File.Copy(full, dest, overwrite: false);
                LibraryCatalog.CloneEntry(full, dest);
                LibraryMediaCache.CopyEntry(full, dest);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Copy(full, dest));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            ShowLibraryFailure(ex);
            return false;
        }

        return true;
    }

    private void MoveLibraryPath(string source, string destination, bool directory)
    {
        if (LibraryFileTransfer.IsCaseOnlyChange(source, destination))
        {
            var parent = Path.GetDirectoryName(source) ?? Path.GetTempPath();
            var temp = Path.Combine(parent, "." + Guid.NewGuid().ToString("N"));
            MoveLibraryCore(source, temp, directory);
            MoveLibraryCore(temp, destination, directory);
            return;
        }

        MoveLibraryCore(source, destination, directory);
    }

    private static void MoveLibraryCore(string source, string destination, bool directory)
    {
        if (!directory)
        {
            File.Move(source, destination);
            return;
        }

        try
        {
            Directory.Move(source, destination);
        }
        catch (IOException ex) when ((ex.HResult & 0xFFFF) == 17)
        {
            CopyLibraryDirectory(source, destination, cloneCatalog: false);
            Directory.Delete(source, recursive: true);
        }
    }

    private static void CopyLibraryDirectory(string source, string destination, bool cloneCatalog)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            var target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: false);
            if (cloneCatalog)
            {
                LibraryCatalog.CloneEntry(file, target);
                LibraryMediaCache.CopyEntry(file, target);
            }
        }

        foreach (var child in Directory.EnumerateDirectories(source))
        {
            if (LibraryFileTransfer.IsReparsePoint(child))
            {
                continue;
            }

            CopyLibraryDirectory(child, Path.Combine(destination, Path.GetFileName(child)), cloneCatalog);
        }
    }

    private void DeleteDestination(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var indexed in LibraryCatalog.ListIndexedFiles(path))
            {
                LibraryMediaCache.DeleteEntry(indexed);
                LibraryPlaylistWaveform.Forget(indexed);
            }

            LibraryCatalog.RemovePrefix(path);
            Directory.Delete(path, recursive: true);
            ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(path));
            return;
        }

        if (File.Exists(path))
        {
            LibraryMediaCache.DeleteEntry(path);
            LibraryPlaylistWaveform.Forget(path);
            LibraryCatalog.RemovePrefix(path);
            File.Delete(path);
            ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(path));
        }
    }

    private void FinishLibraryMove(string source, string destination, bool directory)
    {
        foreach (var indexed in directory ? LibraryCatalog.ListIndexedFiles(source) : [source])
        {
            if (!LibraryFileTransfer.TryMapPath(indexed, source, destination, out var mapped))
            {
                continue;
            }

            LibraryMediaCache.MoveEntry(indexed, mapped);
            LibraryPlaylistWaveform.Retarget(indexed, mapped);
        }

        ApplyFileNameColorEdit(FileNameColorAssignmentSync.Move(source, destination));
        LibraryCatalog.RetargetPrefix(source, destination);
        RetargetOpenSessions(source, destination);
        LibraryBrowser.RemapFilesystemPaths(source, destination, refreshTree: directory, selectFolder: directory ? destination : null);
    }

    private void RetargetOpenSessions(string oldPrefix, string newPrefix)
    {
        var changed = new List<DocumentSession>();
        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            if (!LibraryFileTransfer.TryMapPath(path, oldPrefix, newPrefix, out var mapped)
                || string.Equals(path, mapped, StringComparison.Ordinal))
            {
                continue;
            }

            session.Document.SourcePath = mapped;
            changed.Add(session);
        }

        if (changed.Count == 0)
        {
            return;
        }

        if (!LibraryBrowser.PlaylistSortActive)
        {
            foreach (var session in changed)
            {
                LibraryBrowser.UpdateSessionRow(session);
            }

            return;
        }

        foreach (var session in changed)
        {
            _sessions.Remove(session);
        }

        var indices = LibraryBrowser.RepositionByCurrentSort(changed);
        for (var i = 0; i < changed.Count; i++)
        {
            var at = Math.Clamp(indices[i], 0, _sessions.Count);
            _sessions.Insert(at, changed[i]);
        }

        LibraryBrowser.FinishIncrementalSessionLoad();
    }

    private void RenameExplorerSelection()
    {
        try
        {
            foreach (var path in LibraryBrowser.SelectedExplorerFolders)
            {
                if (!RenameLibraryPath(path, directory: true))
                {
                    return;
                }
            }
        }
        finally
        {
            LibraryBrowser.FocusExplorer();
        }
    }

    private void RenameFavoriteSelection()
    {
        foreach (var path in LibraryBrowser.SelectedFavoritePaths)
        {
            var directory = Directory.Exists(path) || !File.Exists(path);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                ShowLibraryFailure(new FileNotFoundException(null, path));
                return;
            }

            if (!RenameLibraryPath(path, directory))
            {
                return;
            }
        }
    }

    private void RenamePlaylistSelection()
    {
        foreach (var session in LibraryBrowser.SelectedSessions)
        {
            if (session.Document.SourcePath is not { Length: > 0 } path)
            {
                ShowLibraryFailure(new FileNotFoundException());
                return;
            }

            if (!RenameLibraryPath(path, directory: false))
            {
                return;
            }
        }
    }

    private bool RenameLibraryPath(string path, bool directory)
    {
        if (directory)
        {
            if (!Directory.Exists(path))
            {
                ShowLibraryFailure(new DirectoryNotFoundException(path));
                return false;
            }
        }
        else if (!File.Exists(path))
        {
            ShowLibraryFailure(new FileNotFoundException(null, path));
            return false;
        }

        var current = Path.GetFileName(LibraryFileTransfer.NormalizePrefix(path));
        var parent = Path.GetDirectoryName(LibraryFileTransfer.NormalizePrefix(path));
        if (string.IsNullOrEmpty(parent))
        {
            ShowLibraryFailure(new IOException(UiStrings.ErrorLibraryFileOp));
            return false;
        }

        while (true)
        {
            var typed = LibraryNameWindow.Ask(
                this,
                UiStrings.LibraryRenameTitle,
                current,
                selectStem: !directory);
            if (typed is null)
            {
                return false;
            }

            if (!LibraryFileTransfer.TryAcceptName(typed, directory, current, out var name, out var error))
            {
                ShowFileNameError(error);
                continue;
            }

            if (string.Equals(name, current, StringComparison.Ordinal))
            {
                return true;
            }

            var dest = Path.Combine(parent, name);
            if ((File.Exists(dest) || Directory.Exists(dest))
                && !LibraryFileTransfer.IsCaseOnlyChange(path, dest))
            {
                ShowLibraryMessage(UiStrings.ErrorFileNameExists, MessageBoxImage.Information);
                continue;
            }

            try
            {
                ReleaseLibraryPaths(new[] { path });
                MoveLibraryPath(path, dest, directory);
                FinishLibraryMove(path, dest, directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                return false;
            }

            return true;
        }
    }

    private void CreateExplorerFolder()
    {
        if (!LibraryBrowser.TryGetSelectedExplorerFolder(out var parent) || !Directory.Exists(parent))
        {
            ShowLibraryFailure(new DirectoryNotFoundException(parent));
            return;
        }

        while (true)
        {
            var typed = LibraryNameWindow.Ask(this, UiStrings.LibraryNewFolderTitle, string.Empty, selectStem: false);
            if (typed is null)
            {
                return;
            }

            if (!LibraryFileTransfer.TryAcceptName(typed, directoryName: true, currentName: string.Empty, out var name, out var error))
            {
                ShowFileNameError(error);
                continue;
            }

            var dest = Path.Combine(parent, name);
            if (File.Exists(dest) || Directory.Exists(dest))
            {
                ShowLibraryMessage(UiStrings.ErrorFileNameExists, MessageBoxImage.Information);
                continue;
            }

            try
            {
                Directory.CreateDirectory(dest);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                return;
            }

            LibraryBrowser.RefreshExplorer();
            LibraryBrowser.FocusExplorerFolder(dest);
            return;
        }
    }

    private void DuplicateExplorerFolders()
    {
        try
        {
            DuplicateExplorerFoldersCore();
        }
        finally
        {
            LibraryBrowser.FocusExplorer();
        }
    }

    private void DuplicateExplorerFoldersCore()
    {
        var roots = ExplorerRootSet();
        var targets = new List<string>();
        foreach (var path in LibraryBrowser.SelectedExplorerFolders)
        {
            if (!roots.Contains(LibraryFileTransfer.NormalizePrefix(path)))
            {
                targets.Add(path);
            }
        }

        if (targets.Count == 0)
        {
            return;
        }

        string? select = null;
        foreach (var source in targets)
        {
            if (!Directory.Exists(source))
            {
                ShowLibraryFailure(new DirectoryNotFoundException(source));
                return;
            }

            if (LibraryFileTransfer.IsReparsePoint(source))
            {
                ShowLibraryFailure(new IOException(UiStrings.ErrorLibraryReparse));
                return;
            }

            var parent = Path.GetDirectoryName(LibraryFileTransfer.NormalizePrefix(source));
            if (string.IsNullOrEmpty(parent))
            {
                continue;
            }

            try
            {
                var dest = LibraryFileTransfer.UniquePath(
                    parent,
                    Path.GetFileName(LibraryFileTransfer.NormalizePrefix(source)),
                    directoryName: true);
                CopyLibraryDirectory(source, dest, cloneCatalog: true);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Copy(source, dest));
                select = dest;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                return;
            }
        }

        LibraryBrowser.RefreshExplorer();
        if (select is not null)
        {
            LibraryBrowser.FocusExplorerFolder(select);
        }
    }

    private void DuplicatePlaylistFiles()
    {
        var copies = new List<DocumentSession>();
        foreach (var session in LibraryBrowser.SelectedSessions)
        {
            if (session.Document.SourcePath is not { Length: > 0 } source || !File.Exists(source))
            {
                ShowLibraryFailure(new FileNotFoundException(null, session.Document.SourcePath));
                return;
            }

            var parent = Path.GetDirectoryName(source);
            if (string.IsNullOrEmpty(parent))
            {
                ShowLibraryFailure(new IOException(UiStrings.ErrorLibraryFileOp));
                return;
            }

            try
            {
                var dest = LibraryFileTransfer.UniquePath(parent, Path.GetFileName(source), directoryName: false);
                File.Copy(source, dest, overwrite: false);
                LibraryCatalog.CloneEntry(source, dest);
                LibraryMediaCache.CopyEntry(source, dest);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Copy(source, dest));
                copies.Add(CreateLibrarySession(dest));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                return;
            }
        }

        if (copies.Count == 0)
        {
            return;
        }

        foreach (var session in copies)
        {
            PlacePlaylistSession(session, select: false);
        }

        LibraryBrowser.FinishIncrementalSessionLoad();
    }

    /// <summary>
    /// ソート見出しがあるときはその位置、無いときは末尾へ載せる。内部リストも同じ位置にする。
    /// </summary>
    private void PlacePlaylistSession(DocumentSession session, bool select)
    {
        var index = LibraryBrowser.InsertSessionByCurrentSort(session, select);
        _sessions.Insert(Math.Clamp(index, 0, _sessions.Count), session);
    }

    private void DeletePlaylistFiles()
    {
        var sessions = LibraryBrowser.SelectedSessions;
        if (sessions.Length == 0)
        {
            return;
        }

        var paths = new List<string>();
        foreach (var session in sessions)
        {
            if (session.Document.SourcePath is not { Length: > 0 } path)
            {
                ShowLibraryFailure(new FileNotFoundException());
                return;
            }

            if (!File.Exists(path))
            {
                ShowLibraryFailure(new FileNotFoundException(null, path));
                return;
            }

            paths.Add(path);
        }

        if (!ConfirmLibraryDelete(paths))
        {
            return;
        }

        ReleaseLibraryPaths(paths);
        var deleted = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(deleted));
                DropLibrarySessionsWithoutUndo(SessionsUnder(deleted));
                return;
            }

            LibraryMediaCache.DeleteEntry(path);
            LibraryPlaylistWaveform.Forget(path);
            LibraryCatalog.RemovePrefix(path);
            deleted.Add(path);
        }

        ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(deleted));
        DropLibrarySessionsWithoutUndo(sessions);
    }

    private void DeleteExplorerSelection()
    {
        string? above = null;
        try
        {
            above = DeleteExplorerSelectionCore();
        }
        finally
        {
            if (!string.IsNullOrEmpty(above))
            {
                LibraryBrowser.FocusExplorerFolder(above);
            }

            LibraryBrowser.FocusExplorer();
        }
    }

    private string? DeleteExplorerSelectionCore()
    {
        var roots = ExplorerRootSet();
        var targets = new List<string>();
        foreach (var path in LibraryBrowser.SelectedExplorerFolders)
        {
            var normalized = LibraryFileTransfer.NormalizePrefix(path);
            if (roots.Contains(normalized))
            {
                continue;
            }

            if (!Directory.Exists(path) && !File.Exists(path))
            {
                ShowLibraryFailure(new DirectoryNotFoundException(path));
                return null;
            }

            targets.Add(path);
        }

        if (targets.Count == 0 || !ConfirmLibraryDelete(targets))
        {
            return null;
        }

        var above = LibraryBrowser.ExplorerRowAbove(targets);
        ReleaseLibraryPaths(targets);
        var deleted = new List<string>();
        foreach (var path in targets)
        {
            var indexed = LibraryCatalog.ListIndexedFiles(path);
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                ShowLibraryFailure(ex);
                ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(deleted));
                DropLibrarySessionsWithoutUndo(SessionsUnder(deleted));
                LibraryBrowser.RefreshExplorer();
                return above;
            }

            foreach (var file in indexed)
            {
                LibraryMediaCache.DeleteEntry(file);
                LibraryPlaylistWaveform.Forget(file);
            }

            LibraryCatalog.RemovePrefix(path);
            deleted.Add(path);
        }

        ApplyFileNameColorEdit(FileNameColorAssignmentSync.Forget(deleted));
        DropLibrarySessionsWithoutUndo(SessionsUnder(targets));
        LibraryBrowser.RefreshExplorer();
        return above;
    }

    private HashSet<string> ExplorerRootSet()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in LibraryBrowser.ExplorerRootPaths)
        {
            if (PathUtil.TryNormalizePrefix(path, out var full))
            {
                roots.Add(full);
            }
        }

        return roots;
    }

    private bool ConfirmLibraryDelete(IReadOnlyList<string> paths)
    {
        var text = paths.Count == 1
            ? UiStrings.ConfirmDeleteFile(Path.GetFileName(LibraryFileTransfer.NormalizePrefix(paths[0])))
            : UiStrings.ConfirmDeleteSelectedItems(paths.Count);
        return ConfirmFileAction(text, MessageBoxImage.Warning, MessageBoxResult.No);
    }

    private void ReleaseLibraryPaths(IReadOnlyList<string> paths)
    {
        var sessions = SessionsUnder(paths);
        if (sessions.Length == 0)
        {
            return;
        }

        var drop = new HashSet<DocumentSession>(sessions);
        if (IsPlaybackActive() && _activeSession is { } playing && drop.Contains(playing))
        {
            StopPlayback();
        }

        ReleaseLibraryStreamsForRemoved(drop);
    }

    private DocumentSession[] SessionsUnder(IReadOnlyList<string> paths)
    {
        var found = new List<DocumentSession>();
        foreach (var session in _sessions)
        {
            if (session.Document.SourcePath is not { Length: > 0 } path)
            {
                continue;
            }

            foreach (var root in paths)
            {
                if (LibraryFileTransfer.IsSameOrUnder(path, root))
                {
                    found.Add(session);
                    break;
                }
            }
        }

        return found.ToArray();
    }

    private void DropLibrarySessionsWithoutUndo(DocumentSession[] selected)
    {
        if (selected.Length == 0)
        {
            return;
        }

        var dropSet = new HashSet<DocumentSession>(selected);
        var next = NextLibrarySessionAfter(dropSet);
        if (IsPlaybackActive() && _activeSession is { } playing && dropSet.Contains(playing))
        {
            StopPlayback();
        }

        ReleaseLibraryStreamsForRemoved(dropSet);
        CancelLibraryPeakJobsFor(dropSet);
        foreach (var session in selected)
        {
            _selectedTabs.Remove(session);
            if (ReferenceEquals(_tabSelectionAnchor, session))
            {
                _tabSelectionAnchor = null;
            }
        }

        _sessions.RemoveAll(dropSet.Contains);
        DetachTabItems(dropSet);
        if (_sessions.Count == 0)
        {
            BindWorkspace(null);
            EndLibraryBackgroundPlayback();
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
        if (IsPlaybackActive())
        {
            ScheduleLibraryGaplessPrefetch();
        }

        PreviewLibrarySession(next);
    }

    private void ShowLibraryFailure(Exception exception) =>
        ShowLibraryMessage(LibraryFileTransfer.Describe(exception), MessageBoxImage.Error);

    private void ShowLibraryMessage(string text, MessageBoxImage icon)
    {
        OwnerCenteredMessageBox.Show(
            this,
            text,
            UiStrings.AppName,
            MessageBoxButton.OK,
            icon);
    }
}
