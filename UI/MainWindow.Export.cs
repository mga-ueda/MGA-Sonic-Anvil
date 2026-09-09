using System.IO;
using System.Windows;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Wwise;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private bool _exportBusy;

    private async Task ExportToWwiseAsync()
    {
        if (_exportBusy || IsUiBusy)
        {
            return;
        }

        if (!_waapiPanelVisible)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.PreflightWaapiOff,
                UiStrings.DialogExportTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_waapiLastResult is not { Ok: true })
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.PreflightWaapiDisconnected,
                UiStrings.DialogExportTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (_document is null)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.PreflightNoDocument,
                UiStrings.DialogExportTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        StopPlaybackForExport();
        _exportBusy = true;
        WaapiBar.ExportEnabled = false;
        try
        {
            var probe = await WaapiStartupProbe.RunAsync(_waapiSettings).ConfigureAwait(true);
            if (!_waapiPanelVisible)
            {
                return;
            }

            ApplyWaapiProbeResult(probe);
            if (_keepTarget)
            {
                await TryRestoreKeptTargetAsync().ConfigureAwait(true);
                probe = _waapiLastResult ?? probe;
            }

            var outputDirectory = ResolveExportOutputDirectory(probe);
            var preflight = ExportPreflight.Evaluate(
                outputDirectory,
                probe,
                hasDocument: true,
                keepTarget: _keepTarget,
                keptTargetPath: _keptTargetPath,
                fallbackProjectFilePath: _keptTargetProjectFilePath);
            if (!preflight.CanExport)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    preflight.Reason,
                    UiStrings.DialogExportTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            WaapiBar.OutputDirectory = preflight.OutputDirectory;
            AppStorage.Settings.WaapiOutputDirectory = preflight.OutputDirectory;
            AppStorage.Save();

            var plan = WaveOnlyPlanBuilder.Build(_document);
            if (plan.Segments.Count == 0)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    UiStrings.PreflightNoParts,
                    UiStrings.DialogExportTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var created = await WaveOnlyWaapiImporter.ImportAsync(
                    _waapiSettings,
                    plan,
                    _document,
                    preflight.TargetPath,
                    preflight.OutputDirectory,
                    playPostExit: WaapiBar.PlayPostExitChecked)
                .ConfigureAwait(true);

            if (WaapiBar.AutoActiveChecked)
            {
                await BringWwiseToFrontAsync(preflight.ProjectFilePath).ConfigureAwait(true);
            }

            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.DialogExportSucceeded(created),
                UiStrings.DialogExportTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.DialogExportFailed(ex.Message),
                UiStrings.DialogExportTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _exportBusy = false;
            RefreshExportEnabled();
        }
    }

    private string ResolveExportOutputDirectory(WaapiProbeResult? probe)
    {
        var preferred = WaapiBar.OutputDirectory;
        if (preferred.Length == 0)
        {
            preferred = AppStorage.Settings.WaapiOutputDirectory?.Trim() ?? string.Empty;
        }
        var originals = ExportPreflight.ResolveOriginalsRoot(
            probe is { Ok: true } && probe.ProjectFilePath.Length > 0
                ? probe.ProjectFilePath
                : _lastKnownWwiseProjectFilePath);
        if (preferred.Length > 0
            && Directory.Exists(preferred)
            && (originals.Length == 0 || ExportPreflight.IsUnderDirectory(preferred, originals)))
        {
            return preferred;
        }

        if (originals.Length > 0)
        {
            try
            {
                Directory.CreateDirectory(originals);
            }
            catch
            {
                // Preflight が存在チェックする。
            }

            return originals;
        }

        return preferred;
    }
}
