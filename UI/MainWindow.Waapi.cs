using System.IO;
using System.Windows;
using System.Windows.Threading;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;
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
    private bool _wwiseProjectActivateBusy;
    private bool _yieldedAlwaysOnTopToWwise;

    private void PlaceWaapiToggle()
    {
        var button = new TransportIconButton
        {
            CommandKind = TransportCommand.ToggleWaapi,
            Icon = TransportIcon.Waapi,
            QuietChrome = true,
            Width = DesignMetrics.TransportWaapiButtonWidth,
            Height = DesignMetrics.ProjectBarHeight,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        TipService.Set(button, UiStrings.TipWaapiToggle);
        button.Click += (_, _) => ToggleWaapiPanel();
        MeterTopSlot.Child = button;
        _waapiToggle = button;
        button.WashThrough = IsLibraryMaximized;
    }

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
        Activated += (_, _) => RestoreAlwaysOnTopAfterWwiseFocus();
        _waapiPollTimer.Tick += async (_, _) => await PollWaapiAsync().ConfigureAwait(true);

        _waapiPanelVisible = settings.WaapiPanelVisible;
        ApplyWaapiPanelVisible();
    }

    /// <summary>Play -E チェックを切り替える（Alt+E）。WAAPI オフ時は何もしない。</summary>
    private void TogglePlayPostExit()
    {
        if (!_waapiPanelVisible)
        {
            return;
        }

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
        if (IsLibraryMaximized)
        {
            return;
        }

        if (_waapiPanelVisible)
        {
            _waapiPanelVisible = false;
            ApplyWaapiPanelVisible();
            AppStorage.Settings.WaapiPanelVisible = false;
            AppStorage.Save();
            return;
        }

        ShowWaapiPanel();
    }

    private void ShowWaapiPanel()
    {
        if (IsLibraryMaximized || _waapiPanelVisible)
        {
            return;
        }

        _waapiPanelVisible = true;
        ApplyWaapiPanelVisible();
        AppStorage.Settings.WaapiPanelVisible = true;
        AppStorage.Save();
        _ = StartWaapiAsync();
    }

    private void LaunchWwiseProjectFromShortcut()
    {
        if (IsLibraryMaximized)
        {
            return;
        }

        ShowWaapiPanel();
        RequestOpenOrFocusWwiseProject();
    }

    private void ApplyWaapiPanelVisible()
    {
        WaapiBar.Visibility = _waveformMaximizeMode is not WaveformMaximizeMode.Waveform
            and not WaveformMaximizeMode.Library
            && _waapiPanelVisible
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
        if (_waapiToggle is not null)
        {
            _waapiToggle.IsLatched = _waapiPanelVisible;
            _waapiToggle.InvalidateVisual();
        }

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
        WaapiBar.ExportEnabled =
            _waapiPanelVisible
            && !_exportBusy
            && !IsUiBusy
            && _document is not null
            && _waapiLastResult is { Ok: true };
    }

    private async Task StartWaapiAsync()
    {
        if (!_waapiPanelVisible || IsLibraryMaximized)
        {
            return;
        }

        WaapiBar.SetPending();
        _waapiLastResult = null;
        RefreshExportEnabled();
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
        RefreshExportEnabled();
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
        var rememberedName = ResolveRememberedWwiseProjectDisplayName(allowUnnamed: false);
        var displayName = result is { ProjectName.Length: > 0 }
            ? result.ProjectName
            : rememberedName;
        var launchPath = ResolveWwiseProjectFilePathForLaunch();
        var hasLaunchPath = launchPath.Length > 0;
        var clickable = hasLaunchPath && displayName.Length > 0;

        if (result is { Ok: true })
        {
            WaapiBar.UpdateSelection(
                result.WwiseVersion,
                displayName,
                GetDisplayTargetPath(),
                _keepTarget,
                projectNameClickable: clickable);
            return;
        }

        if (_keepTarget)
        {
            WaapiBar.UpdateDisconnectedKeepTarget(
                ResolveRememberedWwiseProjectDisplayName(allowUnnamed: true),
                _keptTargetPath,
                projectNameClickable: hasLaunchPath);
            return;
        }

        if (displayName.Length > 0)
        {
            WaapiBar.UpdateDisconnectedLastProject(
                displayName,
                result?.Message ?? Domain.UiStrings.StatusDisconnected,
                projectNameClickable: clickable);
        }
    }

    private string ResolveRememberedWwiseProjectDisplayName(bool allowUnnamed)
    {
        if (_lastKnownWwiseProjectName.Length > 0)
        {
            return _lastKnownWwiseProjectName;
        }

        var derived = DeriveWwiseProjectDisplayName(
            _keptTargetProjectFilePath.Length > 0
                ? _keptTargetProjectFilePath
                : _lastKnownWwiseProjectFilePath);
        if (derived.Length > 0)
        {
            return derived;
        }

        return allowUnnamed ? UiStrings.LabelUnnamedProject : string.Empty;
    }

    private static string DeriveWwiseProjectDisplayName(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileNameWithoutExtension(filePath.Trim().Trim('"')) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private string ResolveWwiseProjectFilePathForLaunch()
    {
        if (WaapiJson.LooksLikeProjectFilePath(_keptTargetProjectFilePath))
        {
            return _keptTargetProjectFilePath.Trim().Trim('"');
        }

        if (WaapiJson.LooksLikeProjectFilePath(_lastKnownWwiseProjectFilePath))
        {
            return _lastKnownWwiseProjectFilePath.Trim().Trim('"');
        }

        var live = _waapiLastResult?.ProjectFilePath ?? string.Empty;
        if (WaapiJson.LooksLikeProjectFilePath(live))
        {
            return live.Trim().Trim('"');
        }

        return WwiseProjectActivator.TryFindProjectFileNearDirectory(WaapiBar.OutputDirectory);
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
        if (_wwiseProjectActivateBusy)
        {
            return;
        }

        var path = ResolveWwiseProjectFilePathForLaunch();
        if (path.Length == 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.LogWwiseProjectPathMissing,
                UiStrings.LabelWwise,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RememberResolvedLaunchPath(path);
        YieldAlwaysOnTopToWwise();
        // クリック直後のフォアグラウンド権限のうちに、既に開いている Authoring を前面化する。
        WwiseProjectActivator.TryFocusExistingAuthoring(path);
        _ = OpenOrFocusWwiseProjectAsync(path);
    }

    private void RememberResolvedLaunchPath(string path)
    {
        if (string.Equals(_lastKnownWwiseProjectFilePath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _lastKnownWwiseProjectFilePath = path;
        if (_lastKnownWwiseProjectName.Length == 0)
        {
            _lastKnownWwiseProjectName = DeriveWwiseProjectDisplayName(path);
        }

        PersistWaapiSettings();
    }

    private void YieldAlwaysOnTopToWwise()
    {
        if (!Topmost)
        {
            return;
        }

        _yieldedAlwaysOnTopToWwise = true;
        Topmost = false;
    }

    private void RestoreAlwaysOnTopAfterWwiseFocus()
    {
        if (!_yieldedAlwaysOnTopToWwise || !AppStorage.Settings.AlwaysOnTop)
        {
            return;
        }

        _yieldedAlwaysOnTopToWwise = false;
        Topmost = true;
    }

    private async Task OpenOrFocusWwiseProjectAsync(string? projectFilePath = null)
    {
        if (_wwiseProjectActivateBusy)
        {
            return;
        }

        var path = projectFilePath is { Length: > 0 }
            ? projectFilePath
            : ResolveWwiseProjectFilePathForLaunch();
        if (path.Length == 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.LogWwiseProjectPathMissing,
                UiStrings.LabelWwise,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        RememberResolvedLaunchPath(path);
        _wwiseProjectActivateBusy = true;
        try
        {
            YieldAlwaysOnTopToWwise();
            var (ok, message) = await WwiseProjectActivator.OpenOrFocusAsync(_waapiSettings, path)
                .ConfigureAwait(true);
            if (ok)
            {
                YieldAlwaysOnTopToWwise();
                WwiseProjectActivator.TryFocusExistingAuthoring(path);
            }
            else if (message.Length > 0 && !_closing)
            {
                OwnerCenteredMessageBox.Show(
                    this,
                    message,
                    UiStrings.LabelWwise,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _wwiseProjectActivateBusy = false;
        }
    }

    /// <summary>EXPORT 後の Auto Active。失敗しても EXPORT 自体は止めない。</summary>
    private async Task BringWwiseToFrontAsync(string? projectPath = null)
    {
        try
        {
            var path = projectPath is { Length: > 0 }
                ? projectPath
                : ResolveWwiseProjectFilePathForLaunch();
            YieldAlwaysOnTopToWwise();
            if (WaapiJson.LooksLikeProjectFilePath(path))
            {
                WwiseProjectActivator.TryFocusExistingAuthoring(path);
                await WwiseProjectActivator.OpenOrFocusAsync(_waapiSettings, path)
                    .ConfigureAwait(true);
                WwiseProjectActivator.TryFocusExistingAuthoring(path);
                return;
            }

            await WwiseProjectActivator.BringToForegroundAsync(_waapiSettings)
                .ConfigureAwait(true);
        }
        catch
        {
            // 前面化失敗は EXPORT 自体を止めない。
        }
    }
}
