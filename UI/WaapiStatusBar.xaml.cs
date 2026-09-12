using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Wwise;

namespace MgaSonicAnvil.UI;

internal sealed partial class WaapiStatusBar : UserControl
{
    private readonly TransportIconButton _keepLockButton;
    private readonly TransportIconButton _outputFolderButton;
    private string _badgeText = "—";
    private Color _badgeBack = Colors.Transparent;
    private Color _badgeFore = Colors.Gray;
    private bool _badgeFilled;
    private bool _selectionMissing;
    private bool _keepTargetChecked;
    private bool _keepLockHovered;
    private bool _keepLockEnabled = true;
    private bool _showKeepLock;
    private bool _projectNameClickable;
    private bool _projectNameHovered;
    private bool _suppressAutoActiveEvent;
    private bool _suppressPlayPostExitEvent;

    public WaapiStatusBar()
    {
        InitializeComponent();
        Height = DesignMetrics.WaapiBarHeight;

        var keepLockSide = Math.Min(22, Math.Max(1, DesignMetrics.WaapiBarHeight - 8));
        _keepLockButton = new TransportIconButton
        {
            Icon = TransportIcon.Unlock,
            Width = keepLockSide,
            Height = keepLockSide,
            Margin = new Thickness(0),
            QuietChrome = true,
        };
        _keepLockButton.Click += KeepLockButton_Click;
        _keepLockButton.MouseEnter += (_, _) =>
        {
            _keepLockHovered = true;
            ApplyKeepLockColors();
        };
        _keepLockButton.MouseLeave += (_, _) =>
        {
            _keepLockHovered = false;
            ApplyKeepLockColors();
        };
        KeepLockButtonHost.Content = _keepLockButton;

        _outputFolderButton = new TransportIconButton
        {
            Icon = TransportIcon.Folder,
            Width = keepLockSide,
            Height = keepLockSide,
            Margin = new Thickness(0),
            QuietChrome = true,
        };
        _outputFolderButton.Click += (_, _) => OutputFolderBrowse?.Invoke(this, EventArgs.Empty);
        OutputFolderButtonHost.Content = _outputFolderButton;

        AutoActiveCheckBox.IsChecked = true;
        ActionButtonLooks.ApplyStatusExport(ExportButton);
        SetPending();
        ApplyTips();
        BadgeCanvas.Loaded += (_, _) => DrawBadge();
    }

    public void ApplyLocalizedText()
    {
        TitleLabel.Text = UiStrings.WaapiTitle;
        PlayMinusECheckBox.Content = UiStrings.LabelPlayMinusE;
        AutoActiveCheckBox.Content = UiStrings.LabelAutoActive;
        ExportButton.Content = UiStrings.ButtonExport;
        ActionButtonLooks.ApplyStatusExport(ExportButton);
        ApplyTips();
        UpdateKeepLockAppearance();
        _outputFolderButton.InvalidateVisual();
        DrawBadge();
    }

    /// <summary>配色だけ更新する。接続状態・ポーリング・文言は触らない。</summary>
    public void RefreshAppearance()
    {
        RefreshBadgeColors();
        RefreshDetailColors();
        ApplyKeepLockColors();
        ActionButtonLooks.ApplyStatusExport(ExportButton);
        _outputFolderButton.InvalidateVisual();
        DrawBadge();
    }

    private void RefreshBadgeColors()
    {
        if (_badgeText == UiStrings.WaapiBadgeConnect)
        {
            SetBadgeConnected();
            return;
        }

        if (_badgeText == UiStrings.WaapiBadgeDisconnect)
        {
            SetBadgeDisconnected();
            return;
        }

        SetBadgeNeutral();
    }

    private void RefreshDetailColors()
    {
        if (_badgeText == UiStrings.WaapiBadgeConnect)
        {
            ApplyPathForeColor(connected: true);
            return;
        }

        if (VersionLabel.Visibility == Visibility.Visible || _showKeepLock)
        {
            PathLabel.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarDetailForeBrush"));
            SepAfterVersion.Foreground = PathLabel.Foreground;
            SepAfterProject.Foreground = PathLabel.Foreground;
            VersionLabel.Foreground = PathLabel.Foreground;
            ApplyProjectNameColors();
            return;
        }

        PathLabel.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get(
            _badgeFilled ? "StatusBarErrorDetailForeBrush" : "StatusBarTitleForeBrush"));
    }

    private void ApplyTips()
    {
        TipService.Set(TitleLabel, UiStrings.TipWaapiConnection);
        TipService.Set(BadgeCanvas, UiStrings.TipWaapiConnection);
        TipService.Set(VersionLabel, UiStrings.TipWwiseVersion);
        TipService.Set(PlayMinusECheckBox, UiStrings.TipPlayMinusE);
        TipService.Set(AutoActiveCheckBox, UiStrings.TipAutoActive);
        TipService.Set(ExportButton, UiStrings.TipExport);
        TipService.Set(OutputPathBox, UiStrings.TipOutputPath);
        TipService.Set(_outputFolderButton, UiStrings.TipOutputFolder);
        ApplyProjectNameTip();
        ApplyPathTip();
        ApplyKeepTargetTips();
    }

    private void ApplyProjectNameTip() =>
        TipService.Set(
            ProjectNameLabel,
            _projectNameClickable ? UiStrings.TipWwiseProjectNameOpen : UiStrings.TipWwiseProjectName);

    private void ApplyPathTip() =>
        TipService.Set(
            PathLabel,
            VersionLabel.Visibility == Visibility.Visible
                ? UiStrings.TipWaapiTargetPath
                : UiStrings.TipWaapiConnection);

    private void ApplyKeepTargetTips()
    {
        var tip = _keepTargetChecked ? UiStrings.TipKeepTargetLock : UiStrings.TipKeepTargetUnlock;
        TipService.Set(_keepLockButton, tip);
        TipService.Set(KeepStateLabel, tip);
    }

    public event EventHandler? KeepTargetChanged;
    public event EventHandler? AutoActiveChanged;
    public event EventHandler? PlayPostExitChanged;
    public event EventHandler? ProjectNameClick;
    public event EventHandler? ExportClick;
    public event EventHandler? OutputFolderBrowse;

    public string OutputDirectory
    {
        get => OutputPathBox.Text?.Trim() ?? string.Empty;
        set => OutputPathBox.Text = value ?? string.Empty;
    }

    public bool ExportEnabled
    {
        get => ExportButton.IsEnabled;
        set => ExportButton.IsEnabled = value;
    }

    public bool KeepTargetChecked
    {
        get => _keepTargetChecked;
        set
        {
            if (_keepTargetChecked == value)
            {
                return;
            }

            _keepTargetChecked = value;
            UpdateKeepLockAppearance();
        }
    }

    public bool AutoActiveChecked
    {
        get => AutoActiveCheckBox.IsChecked == true;
        set
        {
            if (AutoActiveCheckBox.IsChecked == value)
            {
                return;
            }

            _suppressAutoActiveEvent = true;
            try
            {
                AutoActiveCheckBox.IsChecked = value;
            }
            finally
            {
                _suppressAutoActiveEvent = false;
            }
        }
    }

    /// <summary>Play -E（Wwise Play post-exit）。既定オフ。</summary>
    public bool PlayPostExitChecked
    {
        get => PlayMinusECheckBox.IsChecked == true;
        set
        {
            if (PlayMinusECheckBox.IsChecked == value)
            {
                return;
            }

            _suppressPlayPostExitEvent = true;
            try
            {
                PlayMinusECheckBox.IsChecked = value;
            }
            finally
            {
                _suppressPlayPostExitEvent = false;
            }
        }
    }

    public void SetPending()
    {
        _selectionMissing = false;
        _keepLockEnabled = false;
        _showKeepLock = false;
        SetProjectNameClickable(false);
        _badgeText = "…";
        SetBadgeNeutral();
        SetPlainDetail(UiStrings.StatusChecking, Theme.Get("StatusBarTitleForeBrush"));
    }

    public void SetResult(WaapiProbeResult result)
    {
        _keepLockEnabled = result.Ok;
        if (result.Ok)
        {
            _selectionMissing = !result.HasSelection;
            SetBadgeConnected();
            ApplyPathForeColor(connected: true);
            SetStructuredDetail(
                result.WwiseVersion,
                result.ProjectName,
                result.HasSelection ? result.SelectedPath : UiStrings.StatusNoneSelected,
                projectNameClickable: false);
        }
        else
        {
            _selectionMissing = false;
            _showKeepLock = false;
            SetProjectNameClickable(false);
            SetBadgeDisconnected();
            ApplyPathForeColor(connected: false);
            SetPlainDetail(
                result.Message.Length > 0 ? result.Message : UiStrings.StatusDisconnected,
                Theme.Get("StatusBarErrorDetailForeBrush"));
        }
    }

    public void UpdateSelection(
        string wwiseVersion,
        string projectName,
        string selectedPath,
        bool keepTarget = false,
        bool projectNameClickable = false)
    {
        _keepLockEnabled = true;
        if (keepTarget != _keepTargetChecked)
        {
            _keepTargetChecked = keepTarget;
            UpdateKeepLockAppearance();
        }

        _selectionMissing = keepTarget
            ? selectedPath.Length == 0
            : string.IsNullOrEmpty(selectedPath);
        SetBadgeConnected();
        ApplyPathForeColor(connected: true);
        SetStructuredDetail(
            wwiseVersion,
            projectName,
            string.IsNullOrEmpty(selectedPath) ? UiStrings.StatusNoneSelected : selectedPath,
            projectNameClickable: projectNameClickable && projectName.Length > 0);
    }

    public void UpdateDisconnectedKeepTarget(string projectName, string keptPath) =>
        UpdateDisconnectedStatus(
            projectName,
            string.IsNullOrEmpty(keptPath) ? UiStrings.StatusNoneSelected : keptPath,
            keepTargetChecked: true,
            projectNameClickable: projectName.Length > 0);

    public void UpdateDisconnectedLastProject(string projectName, string detailText) =>
        UpdateDisconnectedStatus(
            projectName,
            string.IsNullOrEmpty(detailText) ? UiStrings.StatusDisconnected : detailText,
            keepTargetChecked: false,
            projectNameClickable: projectName.Length > 0);

    private void UpdateDisconnectedStatus(
        string projectName,
        string detailText,
        bool keepTargetChecked,
        bool projectNameClickable)
    {
        _keepLockEnabled = true;
        if (keepTargetChecked != _keepTargetChecked)
        {
            _keepTargetChecked = keepTargetChecked;
            UpdateKeepLockAppearance();
        }

        _selectionMissing = keepTargetChecked && detailText == UiStrings.StatusNoneSelected;
        _showKeepLock = true;
        SetBadgeDisconnected();
        PathLabel.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarDetailForeBrush"));
        SepAfterVersion.Foreground = PathLabel.Foreground;
        SepAfterProject.Foreground = PathLabel.Foreground;
        VersionLabel.Foreground = PathLabel.Foreground;
        SetStructuredDetail(
            wwiseVersion: string.Empty,
            projectName,
            detailText,
            projectNameClickable: projectNameClickable);
    }

    private void SetPlainDetail(string text, Color foreColor)
    {
        _showKeepLock = false;
        VersionLabel.Visibility = Visibility.Collapsed;
        SepAfterVersion.Visibility = Visibility.Collapsed;
        ProjectNameLabel.Visibility = Visibility.Collapsed;
        SepAfterProject.Visibility = Visibility.Collapsed;
        PathLabel.Text = text;
        PathLabel.Visibility = Visibility.Visible;
        PathLabel.Foreground = WpfControlHelpers.FrozenBrush(foreColor);
        UpdateKeepLockVisibility();
        ApplyPathTip();
        DrawBadge();
    }

    private void SetStructuredDetail(
        string wwiseVersion,
        string projectName,
        string pathText,
        bool projectNameClickable)
    {
        _showKeepLock = true;
        var hasVersion = !string.IsNullOrWhiteSpace(wwiseVersion);
        VersionLabel.Text = hasVersion ? FormatDisplayVersion(wwiseVersion) : string.Empty;
        VersionLabel.Visibility = hasVersion ? Visibility.Visible : Visibility.Collapsed;

        var hasProject = projectName.Length > 0;
        ProjectNameLabel.Text = projectName;
        ProjectNameLabel.Visibility = hasProject ? Visibility.Visible : Visibility.Collapsed;
        SepAfterVersion.Visibility = hasVersion ? Visibility.Visible : Visibility.Collapsed;
        SepAfterProject.Visibility = hasProject ? Visibility.Visible : Visibility.Collapsed;
        PathLabel.Text = pathText;
        PathLabel.Visibility = Visibility.Visible;
        SetProjectNameClickable(projectNameClickable && hasProject);
        UpdateKeepLockVisibility();
        ApplyPathTip();
        DrawBadge();
    }

    private void SetProjectNameClickable(bool clickable)
    {
        _projectNameClickable = clickable;
        if (!clickable)
        {
            _projectNameHovered = false;
        }

        ApplyProjectNameColors();
        ApplyProjectNameTip();
    }

    private void UpdateKeepLockVisibility()
    {
        KeepLockPanel.Visibility = _showKeepLock ? Visibility.Visible : Visibility.Collapsed;
        _keepLockButton.IsEnabled = _keepLockEnabled;
        KeepStateLabel.Text = _keepTargetChecked
            ? UiStrings.KeepTargetOnLabel
            : UiStrings.KeepTargetOffLabel;
    }

    private void UpdateKeepLockAppearance()
    {
        _keepLockButton.Icon = _keepTargetChecked ? TransportIcon.Lock : TransportIcon.Unlock;
        KeepStateLabel.Text = _keepTargetChecked
            ? UiStrings.KeepTargetOnLabel
            : UiStrings.KeepTargetOffLabel;
        ApplyKeepLockColors();
        ApplyKeepTargetTips();
    }

    private void ApplyKeepLockColors()
    {
        var key = _keepTargetChecked
            ? (_keepLockHovered ? "KeepTargetLockHoverForeBrush" : "KeepTargetLockForeBrush")
            : (_keepLockHovered ? "KeepTargetUnlockHoverForeBrush" : "KeepTargetUnlockForeBrush");
        _keepLockButton.IconForeOverride = Theme.Get(key);
        _keepLockButton.InvalidateVisual();
    }

    private void ApplyProjectNameColors()
    {
        if (_projectNameClickable)
        {
            ProjectNameLabel.Foreground = WpfControlHelpers.FrozenBrush(
                Theme.Get(_projectNameHovered ? "ActionLinkForeBrush" : "ActionLinkForeBrush"));
            ProjectNameLabel.Cursor = Cursors.Hand;
            return;
        }

        ProjectNameLabel.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("StatusBarDetailForeBrush"));
        ProjectNameLabel.Cursor = Cursors.Arrow;
    }

    private void ApplyPathForeColor(bool connected)
    {
        var error = !connected || _selectionMissing;
        var fore = error ? Theme.Get("StatusBarErrorDetailForeBrush") : Theme.Get("StatusBarDetailForeBrush");
        PathLabel.Foreground = WpfControlHelpers.FrozenBrush(fore);
        var detail = connected ? Theme.Get("StatusBarDetailForeBrush") : Theme.Get("StatusBarErrorDetailForeBrush");
        VersionLabel.Foreground = WpfControlHelpers.FrozenBrush(detail);
        SepAfterVersion.Foreground = VersionLabel.Foreground;
        SepAfterProject.Foreground = VersionLabel.Foreground;
        ApplyProjectNameColors();
    }

    private void SetBadgeConnected()
    {
        _badgeText = UiStrings.WaapiBadgeConnect;
        _badgeBack = Theme.Get("StatusBarConnectedBadgeBackBrush");
        _badgeFore = Colors.White;
        _badgeFilled = true;
    }

    private void SetBadgeDisconnected()
    {
        _badgeText = UiStrings.WaapiBadgeDisconnect;
        _badgeBack = Theme.Get("StatusBarDisconnectedBadgeBackBrush");
        _badgeFore = Colors.White;
        _badgeFilled = true;
    }

    private void SetBadgeNeutral()
    {
        _badgeBack = Theme.Get("WaapiBarBackBrush");
        _badgeFore = Theme.Get("StatusBarTitleForeBrush");
        _badgeFilled = false;
    }

    internal string BadgeText => _badgeText;

    internal static bool TryClearBadgeCanvas(Panel canvas)
    {
        if (canvas.ActualHeight <= 0)
        {
            return false;
        }

        canvas.Children.Clear();
        return true;
    }

    private void DrawBadge()
    {
        if (!TryClearBadgeCanvas(BadgeCanvas))
        {
            return;
        }

        var formatted = new FormattedText(
            _badgeText,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            10,
            WpfControlHelpers.FrozenBrush(_badgeFore),
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var padX = 6d;
        var padY = 2d;
        var width = formatted.Width + padX * 2;
        var height = formatted.Height + padY * 2;
        BadgeCanvas.Width = Math.Max(36, width);

        if (_badgeFilled)
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = WpfControlHelpers.FrozenBrush(_badgeBack),
            };
            Canvas.SetTop(rect, (BadgeCanvas.ActualHeight - height) / 2);
            BadgeCanvas.Children.Add(rect);
        }

        var text = new TextBlock
        {
            Text = _badgeText,
            Foreground = WpfControlHelpers.FrozenBrush(_badgeFore),
            FontWeight = FontWeights.Bold,
            FontSize = 10,
        };
        Canvas.SetLeft(text, padX);
        Canvas.SetTop(text, (BadgeCanvas.ActualHeight - formatted.Height) / 2);
        BadgeCanvas.Children.Add(text);
    }

    private static string FormatDisplayVersion(string wwiseVersion)
    {
        var wwise = UiStrings.LabelWwise;
        if (string.IsNullOrWhiteSpace(wwiseVersion))
        {
            return wwise;
        }

        var text = wwiseVersion.Trim();
        if (text.StartsWith("Wwise ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = text["Wwise ".Length..].Trim().TrimStart('v', 'V', ' ');
            return rest.Length == 0 ? wwise : $"{wwise} v{rest}";
        }

        return text.StartsWith('v') || text.StartsWith('V')
            ? $"{wwise} {text}"
            : $"{wwise} v{text}";
    }

    private void AutoActiveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressAutoActiveEvent)
        {
            return;
        }

        AutoActiveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlayMinusECheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressPlayPostExitEvent)
        {
            return;
        }

        PlayPostExitChanged?.Invoke(this, EventArgs.Empty);
    }

    private void KeepLockButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_keepLockEnabled)
        {
            return;
        }

        KeepTargetChecked = !KeepTargetChecked;
        KeepTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ProjectNameLabel_Click(object sender, MouseButtonEventArgs e)
    {
        if (_projectNameClickable)
        {
            ProjectNameClick?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ProjectNameLabel_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_projectNameClickable)
        {
            return;
        }

        _projectNameHovered = true;
        ApplyProjectNameColors();
    }

    private void ProjectNameLabel_MouseLeave(object sender, MouseEventArgs e)
    {
        _projectNameHovered = false;
        ApplyProjectNameColors();
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e) =>
        ExportClick?.Invoke(this, EventArgs.Empty);

    private void OutputPathBox_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            OutputFolderBrowse?.Invoke(this, EventArgs.Empty);
        }
    }
}
