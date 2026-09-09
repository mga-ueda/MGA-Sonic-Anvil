using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private void ExportAllTabsMp3() => _ = ExportTabsAsync(_sessions.ToArray(), AudioFileKind.Mp3);

    private void ExportTabs(IReadOnlyList<DocumentSession> sessions, AudioFileKind kind) =>
        _ = ExportTabsAsync(sessions, kind);

    private async Task ExportTabsAsync(IReadOnlyList<DocumentSession> sessions, AudioFileKind kind)
    {
        if (IsUiBusy)
        {
            return;
        }

        if (sessions.Count == 0)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ErrorNoDocument, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var extension = kind == AudioFileKind.Mp3 ? ".mp3" : ".wav";
        if (!TryPlanExportJobs(sessions, extension, out var jobs))
        {
            return;
        }

        if (jobs.Count == 0)
        {
            OwnerCenteredMessageBox.Show(this, UiStrings.ExportNone, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var overwrite = ConfirmExportOverwrites(jobs);
        if (overwrite == MessageBoxResult.Cancel)
        {
            return;
        }

        var skipped = 0;
        if (overwrite == MessageBoxResult.No)
        {
            skipped = jobs.RemoveAll(job => File.Exists(job.Path));
            if (jobs.Count == 0)
            {
                OwnerCenteredMessageBox.Show(this, UiStrings.ExportNone, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        StopPlaybackForExport();
        var options = AppStorage.Settings.ToMp3EncodeOptions();
        var outcomes = new ExportOutcome[jobs.Count];
        var parallelism = kind == AudioFileKind.Mp3 && !Mp3Encode.UsesLame(options.LameExePath)
            ? 1
            : AudioExport.WorkerCount(jobs.Count, AppStorage.Settings.ExportParallelism);
        var tracker = new ExportProgressTracker(
            jobs.Select(job => job.Name).ToArray(),
            jobs.Select(job => job.Document.FrameCount).ToArray(),
            new Progress<ExportProgressSnapshot>(ApplyExportProgress));

        _tabExportBusy = true;
        try
        {
            ShowExportBusyGlass(kind);
            ApplyExportProgress(tracker.Capture());
            await Task.Run(() => RunExportJobs(jobs, kind, options, parallelism, tracker, outcomes))
                .ConfigureAwait(true);
        }
        finally
        {
            _tabExportBusy = false;
            _busyGlass.HideOverlay();
        }

        var ok = 0;
        var failed = 0;
        var errors = new StringBuilder();
        Mp3EncoderKind? encoder = null;
        for (var i = 0; i < outcomes.Length; i++)
        {
            var outcome = outcomes[i];
            if (outcome.Error is { } ex)
            {
                failed++;
                if (errors.Length > 0)
                {
                    errors.AppendLine();
                }

                errors.Append(jobs[i].Name).Append(": ").Append(ex.Message);
                continue;
            }

            ok++;
            encoder ??= outcome.Encoder;
        }

        ShowExportSummary(kind, ok, skipped, failed, encoder, errors.ToString());
    }

    private static void RunExportJobs(
        IReadOnlyList<ExportJob> jobs,
        AudioFileKind kind,
        Mp3EncodeOptions options,
        int parallelism,
        ExportProgressTracker tracker,
        ExportOutcome[] outcomes)
    {
        Parallel.For(0, jobs.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, i =>
        {
            var job = jobs[i];
            var jobProgress = new Progress<double>(p => tracker.Report(i, p, ExportJobState.Running));
            tracker.Report(i, 0, ExportJobState.Running);
            try
            {
                if (kind == AudioFileKind.Mp3)
                {
                    outcomes[i] = new ExportOutcome(AudioCodec.SaveMp3(job.Document, job.Path, options, jobProgress), null);
                }
                else
                {
                    AudioCodec.SaveWave(job.Document, job.Path, jobProgress);
                    outcomes[i] = new ExportOutcome(null, null);
                }

                tracker.Report(i, 1, ExportJobState.Done);
            }
            catch (Exception ex)
            {
                tracker.Report(i, 1, ExportJobState.Failed);
                outcomes[i] = new ExportOutcome(null, ex);
            }
        });
    }

    private void ApplyExportProgress(ExportProgressSnapshot snap) =>
        _busyGlass.SetExportView(
            snap.Overall,
            UiStrings.OverlayExportCount(snap.Finished, snap.Total),
            snap.Running);

    private void ShowExportBusyGlass(AudioFileKind kind)
    {
        RootChrome.UpdateLayout();
        RootDock.UpdateLayout();
        _busyGlass.ShowOverlay(
            RootChrome,
            RootDock,
            GetBusyGlassCoverBounds(),
            kind == AudioFileKind.Mp3 ? UiStrings.OverlayExportMp3 : UiStrings.OverlayExportWave);
    }

    private bool TryPlanExportJobs(
        IReadOnlyList<DocumentSession> sessions,
        string extension,
        out List<ExportJob> jobs)
    {
        jobs = [];
        if (sessions.Count == 0)
        {
            return true;
        }

        if (sessions.Count == 1)
        {
            return TryPlanSingleExportJob(sessions[0], extension, jobs);
        }

        if (!TryPickExportFolder(sessions, out var folder))
        {
            return false;
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            var dest = AudioExport.UniqueInDirectory(
                folder,
                AudioExport.SuggestBaseName(session.Document.SourcePath, session.DisplayName),
                extension,
                reserved);
            jobs.Add(new ExportJob(session.DisplayName, dest, session.Document));
        }

        return true;
    }

    private bool TryPlanSingleExportJob(DocumentSession session, string extension, List<ExportJob> jobs)
    {
        var dialog = new SaveFileDialog
        {
            Filter = extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                ? UiStrings.FilterSaveMp3
                : UiStrings.FilterSaveWave,
            Title = extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                ? UiStrings.MenuExportMp3
                : UiStrings.MenuExportWave,
            FileName = AudioExport.SuggestBaseName(session.Document.SourcePath, session.DisplayName) + extension,
            OverwritePrompt = true,
            InitialDirectory = ResolveExportInitialDirectory(session),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        RememberExportFolder(Path.GetDirectoryName(dialog.FileName));
        jobs.Add(new ExportJob(session.DisplayName, dialog.FileName, session.Document));
        return true;
    }

    private bool TryPickExportFolder(IReadOnlyList<DocumentSession> sessions, out string folder)
    {
        folder = string.Empty;
        var folderDialog = new OpenFolderDialog
        {
            Title = UiStrings.ExportFolderTitle,
            InitialDirectory = ResolveExportInitialDirectory(sessions[0]),
        };
        if (folderDialog.ShowDialog(this) != true)
        {
            return false;
        }

        folder = folderDialog.FolderName;
        RememberExportFolder(folder);
        return true;
    }

    private static string ResolveExportInitialDirectory(DocumentSession session)
    {
        var last = AppStorage.Settings.LastExportFolder;
        if (!string.IsNullOrWhiteSpace(last) && Directory.Exists(last))
        {
            return last;
        }

        var fromSource = Path.GetDirectoryName(session.Document.SourcePath);
        if (!string.IsNullOrWhiteSpace(fromSource) && Directory.Exists(fromSource))
        {
            return fromSource;
        }

        var fromDoc = Path.GetDirectoryName(AppStorage.Settings.LastDocumentPath);
        return !string.IsNullOrWhiteSpace(fromDoc) && Directory.Exists(fromDoc)
            ? fromDoc
            : string.Empty;
    }

    private static void RememberExportFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return;
        }

        AppStorage.Settings.LastExportFolder = Path.GetFullPath(folder);
        AppStorage.Save();
    }

    private MessageBoxResult ConfirmExportOverwrites(List<ExportJob> jobs)
    {
        var existing = jobs
            .Where(job => File.Exists(job.Path))
            .Select(job => Path.GetFileName(job.Path))
            .ToArray();
        if (existing.Length == 0)
        {
            return MessageBoxResult.Yes;
        }

        return OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ConfirmOverwriteFiles(existing.Length, AudioExport.FormatOverwritePreview(existing)),
            UiStrings.AppName,
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
    }

    private void ShowExportSummary(
        AudioFileKind kind,
        int ok,
        int skipped,
        int failed,
        Mp3EncoderKind? encoder,
        string errors)
    {
        if (failed == 0 && skipped == 0 && ok > 0)
        {
            var text = kind == AudioFileKind.Mp3 && encoder is { } used
                ? UiStrings.ExportMp3Done(ok, used)
                : UiStrings.ExportWaveDone(ok);
            OwnerCenteredMessageBox.Show(this, text, UiStrings.AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var encoderLabel = kind == AudioFileKind.Mp3 && encoder is { } enc
            ? enc == Mp3EncoderKind.Lame ? UiStrings.LabelMp3EncoderLame : UiStrings.LabelMp3EncoderWindows
            : null;
        var icon = failed > 0 ? MessageBoxImage.Error : MessageBoxImage.Information;
        OwnerCenteredMessageBox.Show(
            this,
            UiStrings.ExportPartial(ok, skipped, failed, encoderLabel, errors),
            UiStrings.AppName,
            MessageBoxButton.OK,
            icon);
    }

    private void ShowMp3EncoderResult(Mp3EncoderKind encoder)
    {
        OwnerCenteredMessageBox.Show(
            this,
            UiStrings.InfoMp3Wrote(encoder),
            UiStrings.MenuSaveMp3,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private readonly record struct ExportJob(string Name, string Path, AudioDocument Document);

    private readonly record struct ExportOutcome(Mp3EncoderKind? Encoder, Exception? Error);
}
