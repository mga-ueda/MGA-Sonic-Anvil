using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class TransportBar : UserControl
{
    private readonly TransportIconButton _play;
    private readonly Dictionary<TransportCommand, TransportIconButton> _buttons = new();

    public event EventHandler<TransportCommand>? CommandInvoked;

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
        AddGap();
        Add(TransportCommand.FadeIn, TransportIcon.FadeIn, UiStrings.TipFadeIn);
        Add(TransportCommand.FadeOut, TransportIcon.FadeOut, UiStrings.TipFadeOut);
        Add(TransportCommand.Normalize, TransportIcon.Normalize, UiStrings.TipNormalize);
        Add(TransportCommand.Delete, TransportIcon.Delete, UiStrings.TipDelete);
        Add(TransportCommand.Save, TransportIcon.Save, UiStrings.TipSave);
    }

    public void SetPlaying(bool playing)
    {
        _play.IsPlaying = playing;
        _play.InvalidateVisual();
    }

    public void SetPosition(double seconds)
    {
        PositionText.Text = UiStrings.FormatDuration(seconds);
    }

    public FrameworkElement? ButtonFor(TransportCommand command) =>
        _buttons.TryGetValue(command, out var button) ? button : null;

    public void SetCommandsEnabled(bool enabled)
    {
        foreach (var button in _buttons.Values)
        {
            button.IsEnabled = enabled;
        }
    }

    private TransportIconButton Add(TransportCommand command, TransportIcon icon, string tip)
    {
        var button = new TransportIconButton
        {
            CommandKind = command,
            Icon = icon,
            ToolTip = tip,
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        button.Click += (_, _) => CommandInvoked?.Invoke(this, command);
        _buttons[command] = button;
        ButtonsHost.Children.Add(button);
        return button;
    }

    private void AddGap()
    {
        ButtonsHost.Children.Add(new Border { Width = DesignMetrics.TransportGroupGap });
    }
}
