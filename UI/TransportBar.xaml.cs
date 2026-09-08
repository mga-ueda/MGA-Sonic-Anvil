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
        _play = Add(TransportCommand.TogglePlayback, TransportIcon.PlayPause, UiStrings.TipPlay);
        Add(TransportCommand.Stop, TransportIcon.Stop, UiStrings.TipStop);
        Add(TransportCommand.GoToStart, TransportIcon.GoToStart, UiStrings.TipGoToStart);
        Add(TransportCommand.GoToEnd, TransportIcon.GoToEnd, UiStrings.TipGoToEnd);
        AddGap();
        Add(TransportCommand.TimeZoomIn, TransportIcon.TimeZoomIn, UiStrings.TipTimeZoomIn);
        Add(TransportCommand.TimeZoomOut, TransportIcon.TimeZoomOut, UiStrings.TipTimeZoomOut);
        Add(TransportCommand.TimeZoomMax, TransportIcon.TimeZoomMax, UiStrings.TipTimeZoomMax);
        Add(TransportCommand.TimeZoomReset, TransportIcon.TimeZoomReset, UiStrings.TipTimeZoomReset);
        AddGap();
        Add(TransportCommand.AmpZoomIn, TransportIcon.AmpZoomIn, UiStrings.TipAmpZoomIn);
        Add(TransportCommand.AmpZoomOut, TransportIcon.AmpZoomOut, UiStrings.TipAmpZoomOut);
        Add(TransportCommand.AmpZoomMax, TransportIcon.AmpZoomMax, UiStrings.TipAmpZoomMax);
        Add(TransportCommand.AmpZoomReset, TransportIcon.AmpZoomReset, UiStrings.TipAmpZoomReset);
        Add(EditButtonsHost, TransportCommand.FadeIn, TransportIcon.FadeIn, UiStrings.TipFadeIn);
        Add(EditButtonsHost, TransportCommand.FadeOut, TransportIcon.FadeOut, UiStrings.TipFadeOut);
        Add(EditButtonsHost, TransportCommand.Normalize, TransportIcon.Normalize, UiStrings.TipNormalize);
        Add(EditButtonsHost, TransportCommand.Delete, TransportIcon.Delete, UiStrings.TipDelete);
        Add(EditButtonsHost, TransportCommand.Save, TransportIcon.Save, UiStrings.TipSave);
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
        SetTip(TransportCommand.TogglePlayback, UiStrings.TipPlay);
        SetTip(TransportCommand.Stop, UiStrings.TipStop);
        SetTip(TransportCommand.GoToStart, UiStrings.TipGoToStart);
        SetTip(TransportCommand.GoToEnd, UiStrings.TipGoToEnd);
        SetTip(TransportCommand.TimeZoomIn, UiStrings.TipTimeZoomIn);
        SetTip(TransportCommand.TimeZoomOut, UiStrings.TipTimeZoomOut);
        SetTip(TransportCommand.TimeZoomMax, UiStrings.TipTimeZoomMax);
        SetTip(TransportCommand.TimeZoomReset, UiStrings.TipTimeZoomReset);
        SetTip(TransportCommand.AmpZoomIn, UiStrings.TipAmpZoomIn);
        SetTip(TransportCommand.AmpZoomOut, UiStrings.TipAmpZoomOut);
        SetTip(TransportCommand.AmpZoomMax, UiStrings.TipAmpZoomMax);
        SetTip(TransportCommand.AmpZoomReset, UiStrings.TipAmpZoomReset);
        SetTip(TransportCommand.FadeIn, UiStrings.TipFadeIn);
        SetTip(TransportCommand.FadeOut, UiStrings.TipFadeOut);
        SetTip(TransportCommand.Normalize, UiStrings.TipNormalize);
        SetTip(TransportCommand.Delete, UiStrings.TipDelete);
        SetTip(TransportCommand.Save, UiStrings.TipSave);
        TipService.Set(_tips, UiStrings.TipTipsToggle, respectsEnabled: false);
        TipService.Set(_manual, UiStrings.TipManualHelp);
        _tips.RefreshAppearance();
        _manual.RefreshAppearance();
    }

    public void SetTipsEnabled(bool enabled)
    {
        _tips.Checked = enabled;
    }

    private void SetTip(TransportCommand command, string tip)
    {
        if (_buttons.TryGetValue(command, out var button))
        {
            TipService.Set(button, tip);
        }
    }

    public void SetPlaying(bool playing)
    {
        _play.IsPlaying = playing;
        _play.InvalidateVisual();
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

    private TransportIconButton Add(TransportCommand command, TransportIcon icon, string tip) =>
        Add(ButtonsHost, command, icon, tip);

    private TransportIconButton Add(Panel host, TransportCommand command, TransportIcon icon, string tip)
    {
        var button = new TransportIconButton
        {
            CommandKind = command,
            Icon = icon,
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        TipService.Set(button, tip);
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
