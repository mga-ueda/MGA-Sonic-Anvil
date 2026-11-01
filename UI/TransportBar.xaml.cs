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
    private readonly List<Border> _separators = [];
    private string[]? _appliedLayout;

    public event EventHandler<TransportCommand>? CommandInvoked;

    public TransportBar()
    {
        InitializeComponent();
        _play = BuildFromCatalog();
        ApplyLocalizedText();
        ApplyLayoutFromSettings(AppStorage.Settings);
    }

    public void ApplyLocalizedText()
    {
        foreach (var item in TransportToolbarCatalog.All)
        {
            SetTooltip(item.Command, TransportToolbarCatalog.Describe(item));
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

    public void SetMaximizeMode(WaveformMaximizeMode mode, WindowLayoutSlot? activeSlot = null)
    {
        var f9 = activeSlot == WindowLayoutSlot.F9;
        var f10 = activeSlot == WindowLayoutSlot.F10;
        var f11 = activeSlot == WindowLayoutSlot.F11;
        var f12 = activeSlot == WindowLayoutSlot.F12;
        if (activeSlot is null)
        {
            f10 = mode == WaveformMaximizeMode.Library;
            f11 = mode == WaveformMaximizeMode.Waveform;
            f12 = mode == WaveformMaximizeMode.Analyzers;
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleMinimalChrome, out var minimal))
        {
            minimal.IsLatched = f9;
            TransportToolTip.Attach(minimal, UiStrings.TooltipMinimalChrome);
            minimal.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleLibraryMaximize, out var library))
        {
            library.Icon = f10 ? TransportIcon.EditorMode : TransportIcon.PlayerMode;
            library.IsLatched = f10;
            TransportToolTip.Attach(library, UiStrings.TooltipLibraryMaximize);
            library.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.MaximizeWaveform, out var waveform))
        {
            waveform.Icon = f11 ? TransportIcon.WaveformRestore : TransportIcon.WaveformMaximize;
            waveform.IsLatched = f11;
            TransportToolTip.Attach(waveform, UiStrings.TooltipWaveformMaximize);
            waveform.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleAnalyzerMaximize, out var analyzers))
        {
            analyzers.Icon = f12 ? TransportIcon.AnalyzerRestore : TransportIcon.AnalyzerMaximize;
            analyzers.IsLatched = f12;
            TransportToolTip.Attach(analyzers, UiStrings.TooltipAnalyzerMaximize);
            analyzers.InvalidateVisual();
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

        foreach (var separator in _separators)
        {
            separator.SetResourceReference(BackgroundProperty, "TransportToolbarSeparatorBrush");
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
                or TransportCommand.ToggleMinimalChrome
                or TransportCommand.MaximizeWaveform
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

    /// <summary>設定の並び／セパレータに従い、ツールバーを組み直す。</summary>
    public void ApplyLayoutFromSettings(AppSettings settings)
    {
        var layout = TransportToolbarCatalog.ResolveLayout(settings);
        // 再生／停止のクローム同期からも呼ばれる。並びが同じなら組み直さない（全ボタンが一瞬消えて点滅する）。
        if (_appliedLayout is { } applied && applied.SequenceEqual(layout, StringComparer.Ordinal))
        {
            return;
        }

        _appliedLayout = layout;
        ItemsHost.Children.Clear();
        _separators.Clear();

        var anyVisible = false;
        var firstInSegment = true;
        foreach (var entry in layout)
        {
            if (TransportToolbarCatalog.IsSeparator(entry))
            {
                if (!anyVisible)
                {
                    continue;
                }

                var separator = CreateSeparator();
                _separators.Add(separator);
                ItemsHost.Children.Add(separator);
                firstInSegment = true;
                continue;
            }

            if (!TransportToolbarCatalog.TryGetItem(entry, out var item)
                || !_buttons.TryGetValue(item.Command, out var button))
            {
                continue;
            }

            button.Visibility = Visibility.Visible;
            button.Margin = new Thickness(
                firstInSegment ? 0 : 0,
                0,
                DesignMetrics.TransportButtonGap,
                0);
            ItemsHost.Children.Add(button);
            firstInSegment = false;
            anyVisible = true;
        }

        foreach (var (command, button) in _buttons)
        {
            if (!TransportToolbarCatalog.IsCommandInLayout(layout, command))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }

        // 末尾直後のセパレータは出さない（Normalize 済みだが防御）。
        while (ItemsHost.Children.Count > 0
               && ItemsHost.Children[^1] is Border)
        {
            ItemsHost.Children.RemoveAt(ItemsHost.Children.Count - 1);
        }

        Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>互換。並び適用に委譲。</summary>
    public void ApplyVisibilityFromSettings(AppSettings settings) =>
        ApplyLayoutFromSettings(settings);

    /// <summary>いま表示中のコマンドがあるか（領域自体の表示判定用）。</summary>
    public bool HasVisibleCommands =>
        _buttons.Values.Any(button => button.Visibility == Visibility.Visible
            && ItemsHost.Children.Contains(button));

    private TransportIconButton BuildFromCatalog()
    {
        TransportIconButton? play = null;
        foreach (var item in TransportToolbarCatalog.All)
        {
            var button = new TransportIconButton
            {
                CommandKind = item.Command,
                Icon = item.Icon,
                Margin = new Thickness(0, 0, DesignMetrics.TransportButtonGap, 0),
                Visibility = Visibility.Collapsed,
            };
            TransportToolTip.Attach(button, item.Tooltip());
            button.Click += (_, _) => CommandInvoked?.Invoke(this, item.Command);
            _buttons[item.Command] = button;
            if (item.Command == TransportCommand.TogglePlayback)
            {
                play = button;
            }
        }

        return play!;
    }

    private static Border CreateSeparator()
    {
        var separator = new Border
        {
            Width = 1,
            Margin = new Thickness(
                DesignMetrics.TransportGroupGap * 0.5,
                DesignMetrics.From96(8),
                DesignMetrics.TransportGroupGap * 0.5,
                DesignMetrics.From96(8)),
            VerticalAlignment = VerticalAlignment.Stretch,
            MinHeight = DesignMetrics.TransportButtonSide * 0.55,
            SnapsToDevicePixels = true,
        };
        separator.SetResourceReference(BackgroundProperty, "TransportToolbarSeparatorBrush");
        return separator;
    }
}
