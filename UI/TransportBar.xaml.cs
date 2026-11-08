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
    private readonly List<Border> _groupVeils = [];
    private string[]? _appliedLayout;
    private double _appliedIconScale = 1;
    private bool _sampleLoopPresent;

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

        ApplySampleLoopButton();
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

    public void SetRangeClick(bool enabled)
    {
        if (_buttons.TryGetValue(TransportCommand.RangeClick, out var button))
        {
            button.IsLatched = enabled;
            button.InvalidateVisual();
        }
    }

    public void SetSilentSkip(bool enabled) => SetLatched(TransportCommand.SilentSkip, enabled);

    public void SetAutoLoop(bool enabled) => SetLatched(TransportCommand.AutoLoop, enabled);

    public void SetAlwaysOnTop(bool enabled) => SetLatched(TransportCommand.AlwaysOnTop, enabled);

    public void SetCenterLock(bool enabled) => SetLatched(TransportCommand.CenterLock, enabled);

    private void SetLatched(TransportCommand command, bool enabled)
    {
        if (_buttons.TryGetValue(command, out var button))
        {
            button.IsLatched = enabled;
            button.InvalidateVisual();
        }
    }

    public void SetRecording(bool recording)
    {
        if (_buttons.TryGetValue(TransportCommand.Record, out var button))
        {
            button.IsLatched = recording;
            button.InvalidateVisual();
        }
    }

    /// <summary>サンプルループの有無で SetLoop ボタンの絵柄・ツールチップを切り替える。</summary>
    public void SetSampleLoopPresent(bool present)
    {
        _sampleLoopPresent = present;
        ApplySampleLoopButton();
    }

    private void ApplySampleLoopButton()
    {
        if (!_buttons.TryGetValue(TransportCommand.SetLoop, out var button))
        {
            return;
        }

        button.Icon = _sampleLoopPresent ? TransportIcon.ClearLoop : TransportIcon.SetLoop;
        button.IsLatched = _sampleLoopPresent;
        TransportToolTip.Attach(
            button,
            _sampleLoopPresent ? UiStrings.TooltipClearLoop : UiStrings.TooltipSetLoop);
        button.InvalidateVisual();
    }

    public void SetAnalysisView(WaveformAnalysisView view)
    {
        if (_buttons.TryGetValue(TransportCommand.ToggleSpectrogram, out var spectrogram))
        {
            // オン／オフは LatchToggle 面。Overlay 時も絵柄は差し替えない。
            spectrogram.IsLatched = view is WaveformAnalysisView.Spectrogram or WaveformAnalysisView.Overlay;
            spectrogram.Icon = TransportIcon.Analysis;
            spectrogram.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleLoudnessView, out var loudness))
        {
            loudness.IsLatched = view == WaveformAnalysisView.Loudness;
            loudness.InvalidateVisual();
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
            // プレイヤー／エディタの絵柄差し替えはせず、LatchToggle 面でオンを示す。
            library.Icon = TransportIcon.PlayerMode;
            library.IsLatched = f10;
            TransportToolTip.Attach(library, UiStrings.TooltipLibraryMaximize);
            library.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.MaximizeWaveform, out var waveform))
        {
            waveform.Icon = TransportIcon.WaveformMaximize;
            waveform.IsLatched = f11;
            TransportToolTip.Attach(waveform, UiStrings.TooltipWaveformMaximize);
            waveform.InvalidateVisual();
        }

        if (_buttons.TryGetValue(TransportCommand.ToggleAnalyzerMaximize, out var analyzers))
        {
            analyzers.Icon = TransportIcon.AnalyzerMaximize;
            analyzers.IsLatched = f12;
            TransportToolTip.Attach(analyzers, UiStrings.TooltipAnalyzerMaximize);
            analyzers.InvalidateVisual();
        }
    }

    public void SetItemsAlignment(LayoutTransportToolbarAlign align) =>
        ItemsHost.HorizontalAlignment = align switch
        {
            LayoutTransportToolbarAlign.Left => HorizontalAlignment.Left,
            LayoutTransportToolbarAlign.Right => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Center,
        };

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

        foreach (var veil in _groupVeils)
        {
            veil.SetResourceReference(BackgroundProperty, "TransportToolbarGroupVeilBrush");
            if (veil.Child is Panel panel)
            {
                foreach (var line in panel.Children.OfType<Border>())
                {
                    line.SetResourceReference(BackgroundProperty, "TransportToolbarSectionSeparatorBrush");
                }
            }
        }
    }

    /// <summary>ドキュメント有無以外の、いま実行できるか（リージョン無しで削除など）。</summary>
    public readonly record struct CommandAvailability(
        bool HasMarkers = true,
        bool HasRegions = true,
        bool HasSelectedRegions = true,
        bool HasSampleLoop = true,
        bool HasSelection = true,
        bool AllowsRegionsAndLoops = true,
        bool CanBounce = true,
        bool CanMergeTabs = true);

    public void SetCommandsEnabled(
        bool enabled,
        bool allowEdit = true,
        CommandAvailability availability = default)
    {
        // default は全 false。未指定時は従来どおりマーカー以外は通す。
        if (availability.Equals(default(CommandAvailability)))
        {
            availability = new CommandAvailability();
        }

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

            button.IsEnabled = enabled && IsCommandAvailable(command, availability);
        }

        RefreshGroupVisibility();
        Visibility = HasVisibleCommands ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// ボタンの有効状態を、同じコマンドのメニュー項目に合わせる。
    /// プレイヤーではメニューの実行拒否（<see cref="LibraryPlayerMode.BlocksWaveMenu"/>）も重ねる。
    /// </summary>
    public void ApplyMenuEnabled(IReadOnlyList<WaveMenuEntry> menu, bool playerMode)
    {
        ArgumentNullException.ThrowIfNull(menu);
        foreach (var (command, button) in _buttons)
        {
            if (!TryMenuEnabled(menu, command, playerMode, out var menuEnabled))
            {
                continue;
            }

            button.IsEnabled = menuEnabled;
        }

        RefreshGroupVisibility();
        Visibility = HasVisibleCommands ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>メニュー項目の Enabled。プレイヤーでは実行拒否のコマンドをグレーにする。</summary>
    public static bool TryMenuEnabled(
        IReadOnlyList<WaveMenuEntry> menu,
        TransportCommand command,
        bool playerMode,
        out bool enabled)
    {
        enabled = false;
        if (!TransportToolbarCatalog.TryGetItem(command, out var item)
            || !WaveformContextMenuBuilder.TryGetCommandEnabled(menu, item.MenuCommand, out enabled))
        {
            return false;
        }

        if (playerMode && LibraryPlayerMode.BlocksWaveMenu(item.MenuCommand))
        {
            enabled = false;
        }

        return true;
    }

    private static bool IsCommandAvailable(TransportCommand command, CommandAvailability a) =>
        command switch
        {
            TransportCommand.DeleteMarkers
                or TransportCommand.DeleteAllMarkers => a.HasMarkers,
            TransportCommand.DeleteRegions => a.HasSelectedRegions && a.AllowsRegionsAndLoops,
            TransportCommand.DeleteAllRegions => a.HasRegions && a.AllowsRegionsAndLoops,
            TransportCommand.SetRegion => a.HasSelection && a.AllowsRegionsAndLoops,
            // ループ無し＝選択から設定、ループ有り＝解除。どちらかできれば押せる。
            TransportCommand.SetLoop =>
                a.AllowsRegionsAndLoops && (a.HasSelection || a.HasSampleLoop),
            TransportCommand.Bounce => a.CanBounce,
            TransportCommand.MergeTabs => a.CanMergeTabs,
            _ => true,
        };

    /// <summary>設定の並びに従い、グループ単位のベール付きでツールバーを組み直す。</summary>
    public void ApplyLayoutFromSettings(AppSettings settings)
    {
        var layout = TransportToolbarCatalog.ResolveLayout(settings);
        var iconScale = settings.ResolvedTransportToolbarIconScale();
        // 再生／停止のクローム同期からも呼ばれる。並びと倍率が同じなら組み直さない（全ボタンが一瞬消えて点滅する）。
        if (_appliedLayout is { } applied
            && applied.SequenceEqual(layout, StringComparer.Ordinal)
            && _appliedIconScale == iconScale)
        {
            return;
        }

        _appliedLayout = layout;
        _appliedIconScale = iconScale;
        ApplyIconScale(iconScale);
        ItemsHost.Children.Clear();
        _groupVeils.Clear();
        // 旧グループの StackPanel に残ったボタンを外してから付け直す。
        foreach (var button in _buttons.Values)
        {
            if (button.Parent is Panel oldParent)
            {
                oldParent.Children.Remove(button);
            }
        }

        StackPanel? groupButtons = null;
        foreach (var entry in layout)
        {
            if (TransportToolbarCatalog.IsSectionSeparator(entry))
            {
                groupButtons?.Children.Add(CreateSectionSeparator(iconScale));
                continue;
            }

            if (TransportToolbarCatalog.IsSeparator(entry))
            {
                groupButtons = null;
                continue;
            }

            if (!TransportToolbarCatalog.TryGetItem(entry, out var item)
                || !_buttons.TryGetValue(item.Command, out var button))
            {
                continue;
            }

            if (groupButtons is null)
            {
                groupButtons = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var veil = CreateGroupVeil(groupButtons);
                _groupVeils.Add(veil);
                ItemsHost.Children.Add(veil);
            }

            button.Visibility = Visibility.Visible;
            button.Margin = new Thickness(0, 0, DesignMetrics.TransportButtonGap, 0);
            groupButtons.Children.Add(button);
        }

        foreach (var (command, button) in _buttons)
        {
            if (!TransportToolbarCatalog.IsCommandInLayout(layout, command))
            {
                button.Visibility = Visibility.Collapsed;
            }
        }

        // グループ末尾ボタンの右余白はベールの Padding に任せる。
        foreach (var veil in _groupVeils)
        {
            if (veil.Child is Panel { Children.Count: > 0 } panel
                && panel.Children[^1] is FrameworkElement last)
            {
                last.Margin = new Thickness(0);
            }
        }

        RefreshGroupVisibility();
        Visibility = HasVisibleCommands ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 押せるボタンが一つもないグループは畳む。
    /// 中央揃えで右余白が偏らないよう、最後に見えるグループだけ右マージンを外す。
    /// </summary>
    private void RefreshGroupVisibility()
    {
        Border? lastVisible = null;
        var gap = DesignMetrics.TransportGroupGap;
        var bottom = DesignMetrics.From96(2);
        foreach (var veil in _groupVeils)
        {
            var anyPressable = false;
            if (veil.Child is Panel panel)
            {
                foreach (UIElement child in panel.Children)
                {
                    if (child is TransportIconButton && child.IsEnabled && child.Visibility == Visibility.Visible)
                    {
                        anyPressable = true;
                        break;
                    }
                }
            }

            veil.Visibility = anyPressable ? Visibility.Visible : Visibility.Collapsed;
            veil.Margin = new Thickness(0, 0, gap, bottom);
            if (anyPressable)
            {
                lastVisible = veil;
            }
        }

        if (lastVisible is not null)
        {
            lastVisible.Margin = new Thickness(0, 0, 0, bottom);
        }
    }

    /// <summary>押せるコマンドがグループとして見えているか（領域自体の表示判定用）。</summary>
    public bool HasVisibleCommands =>
        _groupVeils.Any(veil => veil.Visibility == Visibility.Visible);

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

    private void ApplyIconScale(double iconScale)
    {
        var side = DesignMetrics.TransportButtonSideAt(iconScale);
        MinHeight = DesignMetrics.TransportRowHeightAt(iconScale);
        foreach (var button in _buttons.Values)
        {
            button.Width = side;
            button.Height = side;
        }
    }

    private static Border CreateGroupVeil(UIElement content)
    {
        var veil = DesignMetrics.BindCorner(new Border
        {
            Child = content,
            Padding = new Thickness(
                DesignMetrics.From96(4),
                DesignMetrics.From96(2),
                DesignMetrics.From96(4),
                DesignMetrics.From96(2)),
            Margin = new Thickness(
                0,
                0,
                DesignMetrics.TransportGroupGap,
                DesignMetrics.From96(1)),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
        });
        veil.SetResourceReference(BackgroundProperty, "TransportToolbarGroupVeilBrush");
        return veil;
    }

    /// <summary>グループ内の区切り線（メニューのセパレータ／サブメニュー境界）。</summary>
    private static Border CreateSectionSeparator(double iconScale)
    {
        var line = new Border
        {
            Width = DesignMetrics.From96(1),
            Height = DesignMetrics.From96(16) * Math.Max(1e-6, iconScale),
            Margin = new Thickness(
                DesignMetrics.From96(2),
                0,
                DesignMetrics.From96(2) + DesignMetrics.TransportButtonGap,
                0),
            VerticalAlignment = VerticalAlignment.Center,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false,
        };
        line.SetResourceReference(BackgroundProperty, "TransportToolbarSectionSeparatorBrush");
        return line;
    }
}
