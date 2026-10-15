using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class TransportBar : UserControl
{
    private readonly TransportIconButton _play;
    private readonly Dictionary<TransportCommand, TransportIconButton> _buttons = new();
    private readonly List<(TextBlock Label, Func<string> Title)> _groupLabels = [];

    public event EventHandler<TransportCommand>? CommandInvoked;

    public TransportBar()
    {
        InitializeComponent();
        _play = AddGroup(
            bottomRow: false,
            () => UiStrings.LabelTransportGroup,
            (TransportCommand.TogglePlayback, TransportIcon.PlayPause, UiStrings.TooltipPlay),
            (TransportCommand.Record, TransportIcon.Record, UiStrings.TooltipRecord));

        AddGroup(
            bottomRow: false,
            () => UiStrings.LabelEditGroup,
            (TransportCommand.FadeIn, TransportIcon.FadeIn, UiStrings.TooltipFadeIn),
            (TransportCommand.FadeOut, TransportIcon.FadeOut, UiStrings.TooltipFadeOut),
            (TransportCommand.FadeAround, TransportIcon.FadeAround, UiStrings.TooltipFadeAround),
            (TransportCommand.Normalize, TransportIcon.Normalize, UiStrings.TooltipNormalize),
            (TransportCommand.Volume, TransportIcon.Volume, UiStrings.TooltipVolume),
            (TransportCommand.Pitch, TransportIcon.Pitch, UiStrings.TooltipPitch),
            (TransportCommand.TimeStretch, TransportIcon.TimeStretch, UiStrings.TooltipTimeStretch),
            (TransportCommand.Reverse, TransportIcon.Reverse, UiStrings.TooltipReverse),
            (TransportCommand.Delete, TransportIcon.Delete, UiStrings.TooltipDelete));

        AddGroup(
            bottomRow: false,
            () => UiStrings.LabelFileGroup,
            (TransportCommand.NewDocument, TransportIcon.NewDocument, UiStrings.TooltipNew),
            (TransportCommand.Open, TransportIcon.Folder, UiStrings.TooltipOpen),
            (TransportCommand.Save, TransportIcon.Save, UiStrings.TooltipSave),
            (TransportCommand.SaveAs, TransportIcon.SaveAs, UiStrings.TooltipSaveAs),
            (TransportCommand.SaveMp3, TransportIcon.SaveMp3, UiStrings.TooltipSaveMp3));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelMarkerGroup,
            (TransportCommand.AddMarker, TransportIcon.AddMarker, UiStrings.TooltipAddMarker),
            (TransportCommand.SetLoop, TransportIcon.SetLoop, UiStrings.TooltipSetLoop),
            (TransportCommand.SetRegion, TransportIcon.SetRegion, UiStrings.TooltipSetRegion),
            (TransportCommand.ToggleRangeClick, TransportIcon.RangeClick, UiStrings.TooltipRangeClick));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelViewGroup,
            (TransportCommand.ToggleSpectrogram, TransportIcon.Analysis, UiStrings.TooltipSpectrogramView),
            (TransportCommand.ToggleLoudnessView, TransportIcon.Loudness, UiStrings.TooltipLoudnessView),
            (TransportCommand.CenterPlayhead, TransportIcon.Center, UiStrings.TooltipCenterPlayhead),
            (TransportCommand.History, TransportIcon.History, UiStrings.TooltipHistory),
            (TransportCommand.ToggleLibraryMaximize, TransportIcon.PlayerMode, UiStrings.TooltipLibraryMaximize),
            (TransportCommand.ToggleAnalyzerMaximize, TransportIcon.AnalyzerMaximize, UiStrings.TooltipAnalyzerMaximize),
            (TransportCommand.OpenColorPanel, TransportIcon.ColorPalette, UiStrings.TooltipColorPanel));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelOtherGroup,
            (TransportCommand.OpenSettings, TransportIcon.Settings, UiStrings.TooltipSettings),
            (TransportCommand.OpenManual, TransportIcon.Help, UiStrings.TooltipManualHelp));

        ApplyLocalizedText();
    }

    public void ApplyLocalizedText()
    {
        SetTooltip(TransportCommand.TogglePlayback, UiStrings.TooltipPlay);
        SetTooltip(TransportCommand.Record, UiStrings.TooltipRecord);
        SetTooltip(TransportCommand.FadeIn, UiStrings.TooltipFadeIn);
        SetTooltip(TransportCommand.FadeOut, UiStrings.TooltipFadeOut);
        SetTooltip(TransportCommand.FadeAround, UiStrings.TooltipFadeAround);
        SetTooltip(TransportCommand.Normalize, UiStrings.TooltipNormalize);
        SetTooltip(TransportCommand.Volume, UiStrings.TooltipVolume);
        SetTooltip(TransportCommand.Pitch, UiStrings.TooltipPitch);
        SetTooltip(TransportCommand.TimeStretch, UiStrings.TooltipTimeStretch);
        SetTooltip(TransportCommand.Reverse, UiStrings.TooltipReverse);
        SetTooltip(TransportCommand.Delete, UiStrings.TooltipDelete);
        SetTooltip(TransportCommand.AddMarker, UiStrings.TooltipAddMarker);
        SetTooltip(TransportCommand.SetLoop, UiStrings.TooltipSetLoop);
        SetTooltip(TransportCommand.SetRegion, UiStrings.TooltipSetRegion);
        SetTooltip(TransportCommand.ToggleRangeClick, UiStrings.TooltipRangeClick);
        SetTooltip(TransportCommand.NewDocument, UiStrings.TooltipNew);
        SetTooltip(TransportCommand.Open, UiStrings.TooltipOpen);
        SetTooltip(TransportCommand.Save, UiStrings.TooltipSave);
        SetTooltip(TransportCommand.SaveAs, UiStrings.TooltipSaveAs);
        SetTooltip(TransportCommand.SaveMp3, UiStrings.TooltipSaveMp3);
        SetTooltip(TransportCommand.ToggleSpectrogram, UiStrings.TooltipSpectrogramView);
        SetTooltip(TransportCommand.ToggleLoudnessView, UiStrings.TooltipLoudnessView);
        SetTooltip(TransportCommand.CenterPlayhead, UiStrings.TooltipCenterPlayhead);
        SetTooltip(TransportCommand.History, UiStrings.TooltipHistory);
        SetTooltip(TransportCommand.ToggleLibraryMaximize, UiStrings.TooltipLibraryMaximize);
        SetTooltip(TransportCommand.ToggleAnalyzerMaximize, UiStrings.TooltipAnalyzerMaximize);
        SetTooltip(TransportCommand.OpenColorPanel, UiStrings.TooltipColorPanel);
        SetTooltip(TransportCommand.OpenSettings, UiStrings.TooltipSettings);
        SetTooltip(TransportCommand.OpenManual, UiStrings.TooltipManualHelp);
        foreach (var (label, title) in _groupLabels)
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
            TransportToolTip.Attach(
                library,
                player ? UiStrings.TooltipLibraryMaximizeOff : UiStrings.TooltipLibraryMaximize);
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleAnalyzerMaximize, out var analyzers))
        {
            var on = mode == WaveformMaximizeMode.Analyzers;
            analyzers.Icon = on ? TransportIcon.AnalyzerRestore : TransportIcon.AnalyzerMaximize;
            analyzers.IsLatched = on;
            TransportToolTip.Attach(
                analyzers,
                on ? UiStrings.TooltipAnalyzerMaximizeOff : UiStrings.TooltipAnalyzerMaximize);
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

        foreach (var (label, _) in _groupLabels)
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

    private TransportIconButton AddGroup(
        bool bottomRow,
        Func<string> title,
        params (TransportCommand Command, TransportIcon Icon, string Tooltip)[] items)
    {
        var created = CreateGroupPanel(title);
        TransportIconButton? first = null;
        foreach (var item in items)
        {
            var button = new TransportIconButton
            {
                CommandKind = item.Command,
                Icon = item.Icon,
                Margin = new Thickness(0, 0, DesignMetrics.TransportButtonGap, 0),
            };
            TransportToolTip.Attach(button, item.Tooltip);
            button.Click += (_, _) => CommandInvoked?.Invoke(this, item.Command);
            _buttons[item.Command] = button;
            created.Buttons.Children.Add(button);
            first ??= button;
        }

        (bottomRow ? BottomRow : TopRow).Children.Add(created.Group);
        return first!;
    }

    private (StackPanel Group, StackPanel Buttons) CreateGroupPanel(Func<string> title)
    {
        var group = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, DesignMetrics.TransportGroupGap, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var label = new TextBlock
        {
            Text = title(),
            Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush")),
            FontWeight = FontWeights.Bold,
            FontSize = DesignMetrics.TransportGroupLabelFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, DesignMetrics.TransportGroupLabelGap, 0),
        };
        _groupLabels.Add((label, title));
        group.Children.Add(label);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        group.Children.Add(buttons);
        return (group, buttons);
    }
}
