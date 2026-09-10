using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class TransportBar : UserControl
{
    private readonly TransportIconButton _play;
    private readonly TransportTipsToggleButton _tips;
    private readonly TransportManualButton _manual;
    private readonly Dictionary<TransportCommand, TransportIconButton> _buttons = new();

    public event EventHandler<TransportCommand>? CommandInvoked;

    public event EventHandler? TipsToggleRequested;

    public event EventHandler? ManualHelpRequested;

    public TransportBar()
    {
        InitializeComponent();
        _play = Add(TransportCommand.TogglePlayback, TransportIcon.PlayPause, UiStrings.TipPlay, UiStrings.TooltipPlay);
        Add(TransportCommand.Stop, TransportIcon.Stop, UiStrings.TipStop, UiStrings.TooltipStop);
        Add(TransportCommand.Record, TransportIcon.Record, UiStrings.TipRecord, UiStrings.TooltipRecord);
        Add(TransportCommand.GoToStart, TransportIcon.GoToStart, UiStrings.TipGoToStart, UiStrings.TooltipGoToStart);
        Add(TransportCommand.GoToEnd, TransportIcon.GoToEnd, UiStrings.TipGoToEnd, UiStrings.TooltipGoToEnd);
        AddGap();
        Add(TransportCommand.TimeZoomIn, TransportIcon.TimeZoomIn, UiStrings.TipTimeZoomIn, UiStrings.TooltipTimeZoomIn);
        Add(TransportCommand.TimeZoomOut, TransportIcon.TimeZoomOut, UiStrings.TipTimeZoomOut, UiStrings.TooltipTimeZoomOut);
        Add(TransportCommand.TimeZoomMax, TransportIcon.TimeZoomMax, UiStrings.TipTimeZoomMax, UiStrings.TooltipTimeZoomMax);
        Add(TransportCommand.TimeZoomReset, TransportIcon.TimeZoomReset, UiStrings.TipTimeZoomReset, UiStrings.TooltipTimeZoomReset);
        AddGap();
        Add(TransportCommand.AmpZoomIn, TransportIcon.AmpZoomIn, UiStrings.TipAmpZoomIn, UiStrings.TooltipAmpZoomIn);
        Add(TransportCommand.AmpZoomOut, TransportIcon.AmpZoomOut, UiStrings.TipAmpZoomOut, UiStrings.TooltipAmpZoomOut);
        Add(TransportCommand.AmpZoomMax, TransportIcon.AmpZoomMax, UiStrings.TipAmpZoomMax, UiStrings.TooltipAmpZoomMax);
        Add(TransportCommand.AmpZoomReset, TransportIcon.AmpZoomReset, UiStrings.TipAmpZoomReset, UiStrings.TooltipAmpZoomReset);
        Add(EditButtonsHost, TransportCommand.FadeIn, TransportIcon.FadeIn, UiStrings.TipFadeIn, UiStrings.TooltipFadeIn);
        Add(EditButtonsHost, TransportCommand.FadeOut, TransportIcon.FadeOut, UiStrings.TipFadeOut, UiStrings.TooltipFadeOut);
        Add(EditButtonsHost, TransportCommand.Normalize, TransportIcon.Normalize, UiStrings.TipNormalize, UiStrings.TooltipNormalize);
        Add(EditButtonsHost, TransportCommand.Delete, TransportIcon.Delete, UiStrings.TipDelete, UiStrings.TooltipDelete);
        Add(EditButtonsHost, TransportCommand.Save, TransportIcon.Save, UiStrings.TipSave, UiStrings.TooltipSave);
        Add(EditButtonsHost, TransportCommand.SaveMp3, TransportIcon.SaveMp3, UiStrings.TipSaveMp3, UiStrings.TooltipSaveMp3);
        AddGap(EditButtonsHost);
        _tips = new TransportTipsToggleButton
        {
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        _tips.Click += (_, _) => TipsToggleRequested?.Invoke(this, EventArgs.Empty);
        EditButtonsHost.Children.Add(_tips);
        _manual = new TransportManualButton
        {
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        _manual.Click += (_, _) => ManualHelpRequested?.Invoke(this, EventArgs.Empty);
        EditButtonsHost.Children.Add(_manual);
        ApplyLocalizedTips();
    }

    public void ApplyLocalizedTips()
    {
        SetTip(TransportCommand.TogglePlayback, UiStrings.TipPlay, UiStrings.TooltipPlay);
        SetTip(TransportCommand.Stop, UiStrings.TipStop, UiStrings.TooltipStop);
        SetTip(TransportCommand.Record, UiStrings.TipRecord, UiStrings.TooltipRecord);
        SetTip(TransportCommand.GoToStart, UiStrings.TipGoToStart, UiStrings.TooltipGoToStart);
        SetTip(TransportCommand.GoToEnd, UiStrings.TipGoToEnd, UiStrings.TooltipGoToEnd);
        SetTip(TransportCommand.TimeZoomIn, UiStrings.TipTimeZoomIn, UiStrings.TooltipTimeZoomIn);
        SetTip(TransportCommand.TimeZoomOut, UiStrings.TipTimeZoomOut, UiStrings.TooltipTimeZoomOut);
        SetTip(TransportCommand.TimeZoomMax, UiStrings.TipTimeZoomMax, UiStrings.TooltipTimeZoomMax);
        SetTip(TransportCommand.TimeZoomReset, UiStrings.TipTimeZoomReset, UiStrings.TooltipTimeZoomReset);
        SetTip(TransportCommand.AmpZoomIn, UiStrings.TipAmpZoomIn, UiStrings.TooltipAmpZoomIn);
        SetTip(TransportCommand.AmpZoomOut, UiStrings.TipAmpZoomOut, UiStrings.TooltipAmpZoomOut);
        SetTip(TransportCommand.AmpZoomMax, UiStrings.TipAmpZoomMax, UiStrings.TooltipAmpZoomMax);
        SetTip(TransportCommand.AmpZoomReset, UiStrings.TipAmpZoomReset, UiStrings.TooltipAmpZoomReset);
        SetTip(TransportCommand.FadeIn, UiStrings.TipFadeIn, UiStrings.TooltipFadeIn);
        SetTip(TransportCommand.FadeOut, UiStrings.TipFadeOut, UiStrings.TooltipFadeOut);
        SetTip(TransportCommand.Normalize, UiStrings.TipNormalize, UiStrings.TooltipNormalize);
        SetTip(TransportCommand.Delete, UiStrings.TipDelete, UiStrings.TooltipDelete);
        SetTip(TransportCommand.Save, UiStrings.TipSave, UiStrings.TooltipSave);
        SetTip(TransportCommand.SaveMp3, UiStrings.TipSaveMp3, UiStrings.TooltipSaveMp3);
        TipService.Set(_tips, UiStrings.TipTipsToggle, respectsEnabled: false);
        TransportToolTip.Attach(_tips, UiStrings.TooltipTipsToggle);
        TipService.Set(_manual, UiStrings.TipManualHelp);
        TransportToolTip.Attach(_manual, UiStrings.TooltipManualHelp);
        _tips.RefreshAppearance();
        _manual.RefreshAppearance();
    }

    public void SetTipsEnabled(bool enabled)
    {
        _tips.Checked = enabled;
    }

    private void SetTip(TransportCommand command, string tip, string tooltip)
    {
        if (_buttons.TryGetValue(command, out var button))
        {
            TipService.Set(button, tip);
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

    public FrameworkElement? ButtonFor(TransportCommand command) =>
        _buttons.TryGetValue(command, out var button) ? button : null;

    public void RefreshAppearance()
    {
        InvalidateVisual();
        foreach (var button in _buttons.Values)
        {
            button.InvalidateVisual();
        }

        _tips.InvalidateVisual();
        _manual.InvalidateVisual();
    }

    public void SetCommandsEnabled(bool enabled)
    {
        foreach (var button in _buttons.Values)
        {
            button.IsEnabled = enabled;
        }
    }

    private TransportIconButton Add(TransportCommand command, TransportIcon icon, string tip, string tooltip) =>
        Add(ButtonsHost, command, icon, tip, tooltip);

    private TransportIconButton Add(Panel host, TransportCommand command, TransportIcon icon, string tip, string tooltip)
    {
        var button = new TransportIconButton
        {
            CommandKind = command,
            Icon = icon,
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        TipService.Set(button, tip);
        TransportToolTip.Attach(button, tooltip);
        button.Click += (_, _) => CommandInvoked?.Invoke(this, command);
        _buttons[command] = button;
        host.Children.Add(button);
        return button;
    }

    private void AddGap() => AddGap(ButtonsHost);

    private void AddGap(Panel host)
    {
        host.Children.Add(new Border { Width = DesignMetrics.TransportGroupGap });
    }
}
