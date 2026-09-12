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
            (TransportCommand.TogglePlayback, TransportIcon.PlayPause, UiStrings.TipPlay, UiStrings.TooltipPlay),
            (TransportCommand.Record, TransportIcon.Record, UiStrings.TipRecord, UiStrings.TooltipRecord));

        AddGroup(
            bottomRow: false,
            () => UiStrings.LabelEditGroup,
            (TransportCommand.FadeIn, TransportIcon.FadeIn, UiStrings.TipFadeIn, UiStrings.TooltipFadeIn),
            (TransportCommand.FadeOut, TransportIcon.FadeOut, UiStrings.TipFadeOut, UiStrings.TooltipFadeOut),
            (TransportCommand.FadeAround, TransportIcon.FadeAround, UiStrings.TipFadeAround, UiStrings.TooltipFadeAround),
            (TransportCommand.Normalize, TransportIcon.Normalize, UiStrings.TipNormalize, UiStrings.TooltipNormalize),
            (TransportCommand.Volume, TransportIcon.Volume, UiStrings.TipVolume, UiStrings.TooltipVolume),
            (TransportCommand.Pitch, TransportIcon.Pitch, UiStrings.TipPitch, UiStrings.TooltipPitch),
            (TransportCommand.TimeStretch, TransportIcon.TimeStretch, UiStrings.TipTimeStretch, UiStrings.TooltipTimeStretch),
            (TransportCommand.Reverse, TransportIcon.Reverse, UiStrings.TipReverse, UiStrings.TooltipReverse),
            (TransportCommand.Delete, TransportIcon.Delete, UiStrings.TipDelete, UiStrings.TooltipDelete));

        AddGroup(
            bottomRow: false,
            () => UiStrings.LabelFileGroup,
            (TransportCommand.Open, TransportIcon.Folder, UiStrings.TipOpen, UiStrings.TooltipOpen),
            (TransportCommand.Save, TransportIcon.Save, UiStrings.TipSave, UiStrings.TooltipSave),
            (TransportCommand.SaveAs, TransportIcon.SaveAs, UiStrings.TipSaveAs, UiStrings.TooltipSaveAs),
            (TransportCommand.SaveMp3, TransportIcon.SaveMp3, UiStrings.TipSaveMp3, UiStrings.TooltipSaveMp3));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelMarkerGroup,
            (TransportCommand.AddMarker, TransportIcon.AddMarker, UiStrings.TipAddMarker, UiStrings.TooltipAddMarker),
            (TransportCommand.SetLoop, TransportIcon.SetLoop, UiStrings.TipSetLoop, UiStrings.TooltipSetLoop),
            (TransportCommand.SetRegion, TransportIcon.SetRegion, UiStrings.TipSetRegion, UiStrings.TooltipSetRegion));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelViewGroup,
            (TransportCommand.ToggleSpectrogram, TransportIcon.Analysis, UiStrings.TipSpectrogramView, UiStrings.TooltipSpectrogramView),
            (TransportCommand.ToggleLoudnessView, TransportIcon.Loudness, UiStrings.TipLoudnessView, UiStrings.TooltipLoudnessView),
            (TransportCommand.CenterPlayhead, TransportIcon.Center, UiStrings.TipCenterPlayhead, UiStrings.TooltipCenterPlayhead),
            (TransportCommand.History, TransportIcon.History, UiStrings.TipEditHistory, UiStrings.TooltipHistory),
            (TransportCommand.ToggleUiTheme, TransportIcon.ThemeMoon, UiStrings.TipUiThemeToggle, UiStrings.TooltipUiThemeToggle));

        AddGroup(
            bottomRow: true,
            () => UiStrings.LabelHelpGroup,
            (TransportCommand.OpenSettings, TransportIcon.Settings, UiStrings.TipAudioSettings, UiStrings.TooltipSettings),
            (TransportCommand.ToggleTips, TransportIcon.Tips, UiStrings.TipTipsToggle, UiStrings.TooltipTipsToggle),
            (TransportCommand.OpenManual, TransportIcon.Help, UiStrings.TipManualHelp, UiStrings.TooltipManualHelp));

        ApplyLocalizedTips();
        SetUiTheme(UiThemeService.Current);
    }

    public void ApplyLocalizedTips()
    {
        SetTip(TransportCommand.TogglePlayback, UiStrings.TipPlay, UiStrings.TooltipPlay);
        SetTip(TransportCommand.Record, UiStrings.TipRecord, UiStrings.TooltipRecord);
        SetTip(TransportCommand.FadeIn, UiStrings.TipFadeIn, UiStrings.TooltipFadeIn);
        SetTip(TransportCommand.FadeOut, UiStrings.TipFadeOut, UiStrings.TooltipFadeOut);
        SetTip(TransportCommand.FadeAround, UiStrings.TipFadeAround, UiStrings.TooltipFadeAround);
        SetTip(TransportCommand.Normalize, UiStrings.TipNormalize, UiStrings.TooltipNormalize);
        SetTip(TransportCommand.Volume, UiStrings.TipVolume, UiStrings.TooltipVolume);
        SetTip(TransportCommand.Pitch, UiStrings.TipPitch, UiStrings.TooltipPitch);
        SetTip(TransportCommand.TimeStretch, UiStrings.TipTimeStretch, UiStrings.TooltipTimeStretch);
        SetTip(TransportCommand.Reverse, UiStrings.TipReverse, UiStrings.TooltipReverse);
        SetTip(TransportCommand.Delete, UiStrings.TipDelete, UiStrings.TooltipDelete);
        SetTip(TransportCommand.AddMarker, UiStrings.TipAddMarker, UiStrings.TooltipAddMarker);
        SetTip(TransportCommand.SetLoop, UiStrings.TipSetLoop, UiStrings.TooltipSetLoop);
        SetTip(TransportCommand.SetRegion, UiStrings.TipSetRegion, UiStrings.TooltipSetRegion);
        SetTip(TransportCommand.Open, UiStrings.TipOpen, UiStrings.TooltipOpen);
        SetTip(TransportCommand.Save, UiStrings.TipSave, UiStrings.TooltipSave);
        SetTip(TransportCommand.SaveAs, UiStrings.TipSaveAs, UiStrings.TooltipSaveAs);
        SetTip(TransportCommand.SaveMp3, UiStrings.TipSaveMp3, UiStrings.TooltipSaveMp3);
        SetTip(TransportCommand.ToggleSpectrogram, UiStrings.TipSpectrogramView, UiStrings.TooltipSpectrogramView);
        SetTip(TransportCommand.ToggleLoudnessView, UiStrings.TipLoudnessView, UiStrings.TooltipLoudnessView);
        SetTip(TransportCommand.CenterPlayhead, UiStrings.TipCenterPlayhead, UiStrings.TooltipCenterPlayhead);
        SetTip(TransportCommand.History, UiStrings.TipEditHistory, UiStrings.TooltipHistory);
        SetTip(TransportCommand.ToggleUiTheme, UiStrings.TipUiThemeToggle, UiStrings.TooltipUiThemeToggle, respectsEnabled: false);
        SetTip(TransportCommand.OpenSettings, UiStrings.TipAudioSettings, UiStrings.TooltipSettings, respectsEnabled: false);
        SetTip(TransportCommand.ToggleTips, UiStrings.TipTipsToggle, UiStrings.TooltipTipsToggle, respectsEnabled: false);
        SetTip(TransportCommand.OpenManual, UiStrings.TipManualHelp, UiStrings.TooltipManualHelp, respectsEnabled: false);
        foreach (var (label, title) in _groupLabels)
        {
            label.Text = title();
        }
    }

    public void SetWaveformHeightScale(int scale)
    {
        if (_buttons.TryGetValue(TransportCommand.CycleWaveformHeight, out var button))
        {
            button.WaveformHeightScale = scale;
        }
    }

    private void SetTip(TransportCommand command, string tip, string tooltip, bool respectsEnabled = true)
    {
        if (_buttons.TryGetValue(command, out var button))
        {
            TipService.Set(button, tip, respectsEnabled);
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

    public void SetTipsVisible(bool visible)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleTips, out var button))
        {
            button.IsLatched = visible;
            button.InvalidateVisual();
        }
    }

    public void SetUiTheme(UiTheme theme)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleUiTheme, out var button))
        {
            button.Icon = theme == UiTheme.Light ? TransportIcon.ThemeSun : TransportIcon.ThemeMoon;
            button.IsLatched = theme == UiTheme.Light;
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

    public FrameworkElement? ButtonFor(TransportCommand command) =>
        _buttons.TryGetValue(command, out var button) ? button : null;

    public void RefreshAppearance()
    {
        SetUiTheme(UiThemeService.Current);
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

    public void SetCommandsEnabled(bool enabled)
    {
        foreach (var (command, button) in _buttons)
        {
            button.IsEnabled = enabled
                || command is TransportCommand.Open
                    or TransportCommand.Record
                    or TransportCommand.OpenSettings
                    or TransportCommand.ToggleUiTheme
                    or TransportCommand.ToggleTips
                    or TransportCommand.OpenManual;
        }
    }

    private TransportIconButton AddGroup(
        bool bottomRow,
        Func<string> title,
        params (TransportCommand Command, TransportIcon Icon, string Tip, string Tooltip)[] items)
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
            TipService.Set(button, item.Tip);
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
