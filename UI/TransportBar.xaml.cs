using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class TransportBar : UserControl
{
    private readonly TransportIconButton _play;
    private readonly Dictionary<TransportCommand, TransportIconButton> _buttons = new();
    private readonly List<(TextBlock Label, Func<string> Title, List<TransportIconButton> Buttons)> _groups = [];

    public event EventHandler<TransportCommand>? CommandInvoked;

    public TransportBar()
    {
        InitializeComponent();
        _play = BuildFromCatalog();
        ApplyLocalizedText();
        ApplyVisibilityFromSettings(AppStorage.Settings);
    }

    public void ApplyLocalizedText()
    {
        foreach (var item in TransportToolbarCatalog.All)
        {
            SetTooltip(item.Command, item.Tooltip());
        }

        foreach (var (label, title, _) in _groups)
        {
            label.Text = title();
        }
    }

    private void SetTooltip(TransportCommand command, string tooltip)
    {
        if (_buttons.TryGetValue(command, out var button))
        {
            TransportToolTip.Attach(button, tooltip);
        }
    }

    public void SetPlaying(bool playing)
    {
        _play.IsPlaying = playing;
        _play.InvalidateVisual();
    }

    public void SetRecording(bool recording)
    {
        if (_buttons.TryGetValue(TransportCommand.Record, out var button))
        {
            button.IsLatched = recording;
            button.InvalidateVisual();
        }
    }

    public void SetRangeClickEnabled(bool enabled)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleRangeClick, out var button))
        {
            button.IsLatched = enabled;
            button.InvalidateVisual();
        }
    }

    public void SetAnalysisView(WaveformAnalysisView view)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleSpectrogram, out var spectrogram))
        {
            spectrogram.IsLatched = view is WaveformAnalysisView.Spectrogram or WaveformAnalysisView.Overlay;
            spectrogram.Icon = view == WaveformAnalysisView.Overlay
                ? TransportIcon.Overlay
                : TransportIcon.Analysis;
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleLoudnessView, out var loudness))
        {
            loudness.IsLatched = view == WaveformAnalysisView.Loudness;
        }
    }

    public void SetMaximizeMode(WaveformMaximizeMode mode)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleLibraryMaximize, out var library))
        {
            var player = mode == WaveformMaximizeMode.Library;
            library.Icon = player ? TransportIcon.EditorMode : TransportIcon.PlayerMode;
            library.IsLatched = player;
            TransportToolTip.Attach(library, UiStrings.TooltipLibraryMaximize);
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleAnalyzerMaximize, out var analyzers))
        {
            var on = mode == WaveformMaximizeMode.Analyzers;
            analyzers.Icon = on ? TransportIcon.AnalyzerRestore : TransportIcon.AnalyzerMaximize;
            analyzers.IsLatched = on;
            TransportToolTip.Attach(analyzers, UiStrings.TooltipAnalyzerMaximize);
        }
    }

    public FrameworkElement? ButtonFor(TransportCommand command) =>
        _buttons.TryGetValue(command, out var button) ? button : null;

    public void SetWashThrough(bool wash)
    {
        if (wash)
        {
            Background = Brushes.Transparent;
        }
        else
        {
            SetResourceReference(BackgroundProperty, "TransportBackBrush");
        }

        foreach (var button in _buttons.Values)
        {
            button.WashThrough = wash;
            button.InvalidateVisual();
        }
    }

    public void RefreshAppearance()
    {
        InvalidateVisual();
        foreach (var button in _buttons.Values)
        {
            button.InvalidateVisual();
        }

        foreach (var (label, _, _) in _groups)
        {
            label.Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        }
    }

    public void SetCommandsEnabled(bool enabled, bool allowEdit = true)
    {
        foreach (var (command, button) in _buttons)
        {
            if (command is TransportCommand.Open
                or TransportCommand.OpenSettings
                or TransportCommand.OpenColorPanel
                or TransportCommand.OpenManual
                or TransportCommand.ToggleLibraryMaximize
                or TransportCommand.ToggleAnalyzerMaximize)
            {
                button.IsEnabled = true;
                continue;
            }

            if (!allowEdit && LibraryPlayerMode.BlocksTransport(command))
            {
                button.IsEnabled = false;
                continue;
            }

            button.IsEnabled = enabled;
        }
    }

    /// <summary>設定の非表示リストに従い、グループ見出し／ボタンを畳む。</summary>
    public void ApplyVisibilityFromSettings(AppSettings settings)
    {
        var anyVisible = false;
        foreach (var (label, _, buttons) in _groups)
        {
            var visibleCount = 0;
            foreach (var button in buttons)
            {
                var show = TransportToolbarCatalog.IsCommandVisible(settings, button.CommandKind);
                button.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                {
                    visibleCount++;
                }
            }

            label.Visibility = visibleCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            anyVisible |= visibleCount > 0;
        }

        Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>いま表示中のコマンドがあるか（領域自体の表示判定用）。</summary>
    public bool HasVisibleCommands =>
        _groups.Any(group => group.Buttons.Any(button => button.Visibility == Visibility.Visible));

    private TransportIconButton BuildFromCatalog()
    {
        TransportIconButton? play = null;
        string? currentGroupKey = null;
        TextBlock? groupLabel = null;
        List<TransportIconButton>? groupButtons = null;
        var firstGroup = true;

        foreach (var item in TransportToolbarCatalog.All)
        {
            var groupKey = item.GroupTitle();
            if (!string.Equals(currentGroupKey, groupKey, StringComparison.Ordinal))
            {
                currentGroupKey = groupKey;
                groupLabel = CreateGroupLabel(item.GroupTitle, firstGroup);
                firstGroup = false;
                groupButtons = [];
                _groups.Add((groupLabel, item.GroupTitle, groupButtons));
                ItemsHost.Children.Add(groupLabel);
            }

            var button = new TransportIconButton
            {
                CommandKind = item.Command,
                Icon = item.Icon,
                Margin = new Thickness(0, 0, DesignMetrics.TransportButtonGap, 0),
            };
            TransportToolTip.Attach(button, item.Tooltip());
            button.Click += (_, _) => CommandInvoked?.Invoke(this, item.Command);
            _buttons[item.Command] = button;
            ItemsHost.Children.Add(button);
            groupButtons!.Add(button);
            if (item.Command == TransportCommand.TogglePlayback)
            {
                play = button;
            }
        }

        return play!;
    }

    private static TextBlock CreateGroupLabel(Func<string> title, bool firstGroup) =>
        new()
        {
            Text = title(),
            Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush")),
            FontWeight = FontWeights.Bold,
            FontSize = DesignMetrics.TransportGroupLabelFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(
                firstGroup ? 0 : DesignMetrics.TransportGroupGap,
                0,
                DesignMetrics.TransportGroupLabelGap,
                0),
        };
}
