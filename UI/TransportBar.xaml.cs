using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class TransportBar : UserControl
{
    private readonly TransportIconButton _play;
    private readonly TransportIconButton _waapiToggle;
    private readonly Dictionary<TransportCommand, TransportIconButton> _buttons = new();
    private double _currentSeconds;
    private double _totalSeconds;
    private bool _editing;
    private bool _committing;
    private bool _selectAllOnFocus;

    public event EventHandler<TransportCommand>? CommandInvoked;

    public event EventHandler<double>? PositionSeeked;

    public event EventHandler? RequestWaveformFocus;

    public bool IsPositionFocused => CurrentTimeBox.IsKeyboardFocusWithin;

    public bool IsEditingPosition => _editing || CurrentTimeBox.IsKeyboardFocusWithin;

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
        AddGap();
        // WAAPI トグルはドキュメント非依存のため _buttons（SetCommandsEnabled 対象）へ入れない。
        _waapiToggle = new TransportIconButton
        {
            CommandKind = TransportCommand.ToggleWaapi,
            Icon = TransportIcon.Waapi,
            Width = DesignMetrics.TransportWaapiButtonWidth,
            ToolTip = UiStrings.TipWaapiToggle,
            Margin = new Thickness(DesignMetrics.TransportButtonGap, 0, DesignMetrics.TransportButtonGap, 0),
        };
        _waapiToggle.Click += (_, _) => CommandInvoked?.Invoke(this, TransportCommand.ToggleWaapi);
        ButtonsHost.Children.Add(_waapiToggle);
    }

    public void SetWaapiLatched(bool latched)
    {
        _waapiToggle.IsLatched = latched;
        _waapiToggle.InvalidateVisual();
    }

    public void SetPlaying(bool playing)
    {
        _play.IsPlaying = playing;
        _play.InvalidateVisual();
    }

    public void SetPosition(double seconds, double totalSeconds)
    {
        _currentSeconds = seconds;
        _totalSeconds = totalSeconds;
        if (!IsEditingPosition)
        {
            CurrentTimeBox.Text = UiStrings.FormatDuration(seconds);
        }

        TotalTimeText.Text = "/ " + UiStrings.FormatDuration(totalSeconds);
    }

    public void CancelPositionEdit()
    {
        EndEdit(commit: false);
    }

    public bool FocusCurrentTime()
    {
        if (!CurrentTimeBox.IsEnabled)
        {
            return false;
        }

        _selectAllOnFocus = true;
        return CurrentTimeBox.Focus();
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

        _waapiToggle.InvalidateVisual();
    }

    public void SetCommandsEnabled(bool enabled)
    {
        foreach (var button in _buttons.Values)
        {
            button.IsEnabled = enabled;
        }

        CurrentTimeBox.IsEnabled = enabled;
        if (!enabled)
        {
            CancelPositionEdit();
        }
    }

    private void CurrentTimeBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!CurrentTimeBox.IsEnabled)
        {
            return;
        }

        if (!CurrentTimeBox.IsKeyboardFocusWithin)
        {
            _selectAllOnFocus = true;
            CurrentTimeBox.Focus();
            e.Handled = true;
        }
    }

    private void CurrentTimeBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _editing = true;
        if (_selectAllOnFocus)
        {
            _selectAllOnFocus = false;
            CurrentTimeBox.Dispatcher.BeginInvoke(CurrentTimeBox.SelectAll);
        }
    }

    private void CurrentTimeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndEdit(commit: true);
            RequestWaveformFocus?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndEdit(commit: false);
            e.Handled = true;
            return;
        }

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control
            && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.None;
        if (ctrl && e.Key == Key.C)
        {
            CopyPosition();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.V)
        {
            PastePosition();
            e.Handled = true;
        }
    }

    private void CurrentTimeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editing && !_committing)
        {
            EndEdit(commit: true);
        }
    }

    private void CurrentTimeBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        PasteMenuItem.IsEnabled = CurrentTimeBox.IsEnabled && Clipboard.ContainsText();
    }

    private void CopyPosition_Click(object sender, RoutedEventArgs e) => CopyPosition();

    private void PastePosition_Click(object sender, RoutedEventArgs e) => PastePosition();

    private void EndEdit(bool commit)
    {
        if (!_editing)
        {
            CurrentTimeBox.Text = UiStrings.FormatDuration(_currentSeconds);
            return;
        }

        _committing = true;
        try
        {
            _editing = false;
            if (commit && UiStrings.TryParseDuration(CurrentTimeBox.Text, out var seconds))
            {
                seconds = Math.Clamp(seconds, 0, Math.Max(0, _totalSeconds));
                PositionSeeked?.Invoke(this, seconds);
            }
            else
            {
                CurrentTimeBox.Text = UiStrings.FormatDuration(_currentSeconds);
            }

            if (CurrentTimeBox.IsKeyboardFocusWithin && !commit)
            {
                Keyboard.ClearFocus();
            }
        }
        finally
        {
            _committing = false;
        }
    }

    private void CopyPosition()
    {
        var text = CurrentTimeBox.SelectionLength > 0
            ? CurrentTimeBox.SelectedText
            : UiStrings.FormatDuration(_currentSeconds);
        if (text.Length == 0)
        {
            return;
        }

        Clipboard.SetText(text);
    }

    private void PastePosition()
    {
        if (!CurrentTimeBox.IsEnabled || !Clipboard.ContainsText())
        {
            return;
        }

        var text = Clipboard.GetText();
        if (UiStrings.TryParseDuration(text, out var seconds) && !CurrentTimeBox.IsKeyboardFocusWithin)
        {
            seconds = Math.Clamp(seconds, 0, Math.Max(0, _totalSeconds));
            PositionSeeked?.Invoke(this, seconds);
            return;
        }

        _editing = true;
        CurrentTimeBox.Focus();
        CurrentTimeBox.SelectedText = text;
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
