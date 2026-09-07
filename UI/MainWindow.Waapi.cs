using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Wwise;

namespace MgaSonicAnvil.UI;

public partial class MainWindow
{
    private const int WaapiConnectedPollMs = 1500;
    private const int WaapiDisconnectedPollMs = 3000;
    private const int WaapiPollFailThreshold = 3;

    private readonly WaapiSettings _waapiSettings = WaapiSettings.Load();
    private readonly DispatcherTimer _waapiPollTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(WaapiConnectedPollMs),
    };

    private WaapiProbeResult? _waapiLastResult;
    private bool _waapiPollBusy;
    private int _waapiPollFailCount;
    private bool _keepTarget;
    private bool _waapiPanelVisible = true;
    private string _keptTargetPath = string.Empty;
    private string _keptTargetProjectFilePath = string.Empty;
    private string _lastKnownWwiseProjectFilePath = string.Empty;
    private string _lastKnownWwiseProjectName = string.Empty;

    private void InitializeWaapi()
    {
        var settings = AppStorage.Settings;
        _keepTarget = settings.WaapiKeepTarget;
        _keptTargetPath = settings.WaapiKeptTargetPath ?? string.Empty;
        _keptTargetProjectFilePath = settings.WaapiKeptTargetProjectFilePath ?? string.Empty;
        _lastKnownWwiseProjectFilePath = settings.WaapiLastProjectFilePath ?? string.Empty;
        _lastKnownWwiseProjectName = settings.WaapiLastProjectName ?? string.Empty;

        WaapiBar.KeepTargetChecked = _keepTarget;
        WaapiBar.AutoActiveChecked = settings.WaapiAutoActive;
        WaapiBar.PlayPostExitChecked = settings.WaapiPlayPostExit;
        _player.PlayExitLayer = settings.WaapiPlayPostExit;
        WaapiBar.OutputDirectory = settings.WaapiOutputDirectory ?? string.Empty;
        WaapiBar.KeepTargetChanged += WaapiBar_KeepTargetChanged;
        WaapiBar.AutoActiveChanged += (_, _) => PersistWaapiSettings();
        WaapiBar.PlayPostExitChanged += (_, _) => ApplyPlayPostExit();
        WaapiBar.ProjectNameClick += (_, _) => RequestOpenOrFocusWwiseProject();
        WaapiBar.ExportClick += (_, _) => _ = ExportToWwiseAsync();
        WaapiBar.OutputFolderBrowse += (_, _) => BrowseExportOutputFolder();
        _waapiPollTimer.Tick += async (_, _) => await PollWaapiAsync().ConfigureAwait(true);

        _waapiPanelVisible = settings.WaapiPanelVisible;
        ApplyWaapiPanelVisible();
    }

    /// <summary>Play -E チェックを切り替える（E キー）。WAAPI エリア非表示中も有効。</summary>
    private void TogglePlayPostExit()
    {
        // PlayPostExitChecked の setter はイベントを抑制するため、反映を明示的に呼ぶ。
        WaapiBar.PlayPostExitChecked = !WaapiBar.PlayPostExitChecked;
        ApplyPlayPostExit();
    }

    /// <summary>Play -E の現在値を再生プレビューへ即反映し、設定を保存する。</summary>
    private void ApplyPlayPostExit()
    {
        _player.PlayExitLayer = WaapiBar.PlayPostExitChecked;
        PersistWaapiSettings();
    }

    private void ToggleWaapiPanel()
    {
        _waapiPanelVisible = !_waapiPanelVisible;
        ApplyWaapiPanelVisible();
        AppStorage.Settings.WaapiPanelVisible = _waapiPanelVisible;
        AppStorage.Save();
        if (_waapiPanelVisible)
        {
            _ = StartWaapiAsync();
        }
    }

    private void ApplyWaapiPanelVisible()
    {
        WaapiBar.Visibility = _waapiPanelVisible
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        Transport.SetWaapiLatched(_waapiPanelVisible);
        if (!_waapiPanelVisible)
        {
            DisconnectWaapi();
        }
    }

    /// <summary>
    /// WAAPI を機能として切断する。ポーリング停止、接続状態の破棄、EXPORT 無効化。
    /// </summary>
    private void DisconnectWaapi()
    {
        _waapiPollTimer.Stop();
        _waapiLastResult = null;
        _waapiPollFailCount = 0;
        WaapiBar.SetResult(new WaapiProbeResult
        {
            Ok = false,
            Message = Domain.UiStrings.StatusDisconnected,
        });
        RefreshExportEnabled();
    }

    private void RefreshExportEnabled()
    {
        WaapiBar.ExportEnabled = _waapiPanelVisible && !_exportBusy && _document is not null;
    }

    private async Task StartWaapiAsync()
    {
        if (!_waapiPanelVisible)
        {
            return;
        }

        WaapiBar.SetPending();
        _waapiLastResult = await WaapiStartupProbe.RunAsync(_waapiSettings).ConfigureAwait(true);
        ApplyWaapiProbeResult(_waapiLastResult);
        if (_keepTarget)
        {
            await TryRestoreKeptTargetAsync().ConfigureAwait(true);
        }

        // プローブ中にパネルを閉じられたらポーリングを再開しない。
        if (!_waapiPanelVisible)
        {
            return;
        }

        _waapiPollTimer.Interval = TimeSpan.FromMilliseconds(
            _waapiLastResult?.Ok == true ? WaapiConnectedPollMs : WaapiDisconnectedPollMs);
        _waapiPollTimer.Start();
    }

    private void ApplyWaapiProbeResult(WaapiProbeResult result)
    {
        if (!_waapiPanelVisible)
        {
            return;
        }

        _waapiLastResult = result;
        WaapiBar.SetResult(result);
        if (result.Ok)
        {
            _waapiPollFailCount = 0;
            RememberLiveWwiseProject(result);
        }

        RefreshWaapiStatusDisplay();
    }

    private void RememberLiveWwiseProject(WaapiProbeResult result)
    {
        var changed = false;
        if (WaapiJson.LooksLikeProjectFilePath(result.ProjectFilePath)
            && !string.Equals(_lastKnownWwiseProjectFilePath, result.ProjectFilePath, StringComparison.OrdinalIgnoreCase))
        {
            _lastKnownWwiseProjectFilePath = result.ProjectFilePath.Trim().Trim('"');
            changed = true;
        }

        if (result.ProjectName.Length > 0
            && !string.Equals(_lastKnownWwiseProjectName, result.ProjectName, StringComparison.Ordinal))
        {
            _lastKnownWwiseProjectName = result.ProjectName;
            changed = true;
        }

        if (changed)
        {
            PersistWaapiSettings();
        }
    }

    private async Task PollWaapiAsync()
    {
        if (!_waapiPanelVisible || _waapiPollBusy || _exportBusy)
        {
            return;
        }

        // IM Importer と同じ。再生中はポーリングを休止する。
        // 毎回の HttpClient 生成・TCP 接続（未接続時は接続失敗の例外送出）が、
        // 特にデバッガ接続時にプロセス全体を短時間停止させ、
        // ASIO 出力へ周期的なノイズを乗せ、シークバーをカクつかせるため。
        if (_player.IsPlaying || _player.IsScrubbing)
        {
            return;
        }

        _waapiPollBusy = true;
        try
        {
            if (_waapiLastResult?.Ok == true)
            {
                try
                {
                    var (path, type) = await WaapiStartupProbe.RefreshSelectionAsync(_waapiSettings)
                        .ConfigureAwait(true);
                    if (!_waapiPanelVisible)
                    {
                        return;
                    }

                    _waapiPollFailCount = 0;
                    if (_waapiLastResult is { } live)
                    {
                        _waapiLastResult = new WaapiProbeResult
                        {
                            Ok = true,
                            WwiseVersion = live.WwiseVersion,
                            Project = live.Project,
                            ProjectName = live.ProjectName,
                            ProjectFilePath = live.ProjectFilePath,
                            SelectedPath = path,
                            SelectedType = type,
                        };
                    }

                    RefreshWaapiStatusDisplay();
                    return;
                }
                catch
                {
                    if (!_waapiPanelVisible)
                    {
                        return;
                    }

                    _waapiPollFailCount++;
                    if (_waapiPollFailCount < WaapiPollFailThreshold)
                    {
                        return;
                    }
                }
            }

            var result = await WaapiStartupProbe.RunAsync(_waapiSettings).ConfigureAwait(true);
            if (!_waapiPanelVisible)
            {
                return;
            }

            ApplyWaapiProbeResult(result);
            _waapiPollTimer.Interval = TimeSpan.FromMilliseconds(
                result.Ok ? WaapiConnectedPollMs : WaapiDisconnectedPollMs);
        }
        finally
        {
            _waapiPollBusy = false;
        }
    }

    private void RefreshWaapiStatusDisplay()
    {
        SuggestOriginalsIfOutputEmpty();
        var result = _waapiLastResult;
        if (result is { Ok: true })
        {
            var path = GetDisplayTargetPath();
            WaapiBar.UpdateSelection(
                result.WwiseVersion,
                result.ProjectName.Length > 0 ? result.ProjectName : _lastKnownWwiseProjectName,
                path,
                _keepTarget,
                projectNameClickable: _lastKnownWwiseProjectFilePath.Length > 0);
            return;
        }

        if (_keepTarget)
        {
            WaapiBar.UpdateDisconnectedKeepTarget(
                _lastKnownWwiseProjectName,
                _keptTargetPath);
            return;
        }

        if (_lastKnownWwiseProjectName.Length > 0)
        {
            WaapiBar.UpdateDisconnectedLastProject(
                _lastKnownWwiseProjectName,
                result?.Message ?? Domain.UiStrings.StatusDisconnected);
        }
    }

    private void SuggestOriginalsIfOutputEmpty()
    {
        if (WaapiBar.OutputDirectory.Length > 0)
        {
            return;
        }

        var originals = ExportPreflight.ResolveOriginalsRoot(
            _waapiLastResult is { Ok: true } && _waapiLastResult.ProjectFilePath.Length > 0
                ? _waapiLastResult.ProjectFilePath
                : _lastKnownWwiseProjectFilePath);
        if (originals.Length > 0)
        {
            WaapiBar.OutputDirectory = originals;
        }
    }

    private string GetDisplayTargetPath()
    {
        if (_keepTarget)
        {
            return _keptTargetPath;
        }

        return _waapiLastResult?.SelectedPath ?? string.Empty;
    }

    private async void WaapiBar_KeepTargetChanged(object? sender, EventArgs e)
    {
        _keepTarget = WaapiBar.KeepTargetChecked;
        if (_keepTarget)
        {
            var live = _waapiLastResult?.SelectedPath ?? string.Empty;
            if (live.Length > 0)
            {
                _keptTargetPath = live;
                _keptTargetProjectFilePath = _waapiLastResult?.ProjectFilePath ?? _lastKnownWwiseProjectFilePath;
            }
        }

        PersistWaapiSettings();
        if (_waapiPanelVisible && _keepTarget && _waapiLastResult?.Ok == true)
        {
            await TryRestoreKeptTargetAsync().ConfigureAwait(true);
        }

        RefreshWaapiStatusDisplay();
    }

    private async Task TryRestoreKeptTargetAsync()
    {
        if (!_waapiPanelVisible || !_keepTarget || _keptTargetPath.Length == 0)
        {
            return;
        }

        await WaapiSelection.TryRestoreKeptTargetAsync(
                _waapiSettings,
                _keptTargetPath,
                _keptTargetProjectFilePath,
                _waapiLastResult?.ProjectFilePath ?? _lastKnownWwiseProjectFilePath)
            .ConfigureAwait(true);
    }

    private void PersistWaapiSettings()
    {
        var settings = AppStorage.Settings;
        settings.WaapiKeepTarget = _keepTarget;
        settings.WaapiKeptTargetPath = _keptTargetPath;
        settings.WaapiKeptTargetProjectFilePath = _keptTargetProjectFilePath;
        settings.WaapiAutoActive = WaapiBar.AutoActiveChecked;
        settings.WaapiPlayPostExit = WaapiBar.PlayPostExitChecked;
        settings.WaapiLastProjectName = _lastKnownWwiseProjectName;
        settings.WaapiLastProjectFilePath = _lastKnownWwiseProjectFilePath;
        settings.WaapiOutputDirectory = WaapiBar.OutputDirectory;
        AppStorage.Save();
    }

    private void BrowseExportOutputFolder()
    {
        try
        {
            var current = WaapiBar.OutputDirectory;
            var originals = ExportPreflight.ResolveOriginalsRoot(
                _waapiLastResult is { Ok: true } && _waapiLastResult.ProjectFilePath.Length > 0
                    ? _waapiLastResult.ProjectFilePath
                    : _lastKnownWwiseProjectFilePath);
            var initial = Directory.Exists(current)
                ? current
                : Directory.Exists(originals)
                    ? originals
                    : string.Empty;
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Domain.UiStrings.SelectOutputFolderTitle,
                InitialDirectory = initial,
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            WaapiBar.OutputDirectory = dialog.FolderName;
            PersistWaapiSettings();
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                Domain.UiStrings.ErrSelectFolderFailed(ex.Message),
                Domain.UiStrings.DialogExportTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
    }

    private void RequestOpenOrFocusWwiseProject()
    {
        var projectPath = _lastKnownWwiseProjectFilePath;
        if (!WaapiJson.LooksLikeProjectFilePath(projectPath) || !System.IO.File.Exists(projectPath))
        {
            return;
        }

        _ = BringWwiseToFrontAsync(projectPath);
    }

    private async Task BringWwiseToFrontAsync(string? projectPath = null)
    {
        try
        {
            if (_waapiLastResult?.Ok == true)
            {
                using var client = new WaapiHttpClient(
                    _waapiSettings.Url,
                    TimeSpan.FromMilliseconds(_waapiSettings.TimeoutMs));
                await WaapiCoreCalls.BringToForegroundAsync(client).ConfigureAwait(true);
                return;
            }

            var path = projectPath ?? _lastKnownWwiseProjectFilePath;
            if (WaapiJson.LooksLikeProjectFilePath(path) && System.IO.File.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch
        {
            // 前面化失敗は EXPORT 自体を止めない。
        }
    }
}
