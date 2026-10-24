using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>
/// ラウドネスとスペアナのあいだ：BPM（上にテンポ）と MONO / DIM / LC モニターボタン。
/// BPM はオフ／オンの 2 段（オン時は表示＋クリック。セッションのみ）。DIM／LC 量は設定で保持。
/// </summary>
internal sealed class MonitorToggleColumn : StackPanel
{
    private static readonly Color MonoOn = Color.FromRgb(0x3D, 0x8E, 0xE8);
    private static readonly Color DimOn = Color.FromRgb(0xE2, 0x4B, 0x4A);
    private static readonly Color LowCutOn = Color.FromRgb(0xE0, 0x8A, 0x2E);
    /// <summary>BPM オン時のラッチ色（MONO 青・LC オレンジと区別するティール）。</summary>
    private static readonly Color BpmOn = Color.FromRgb(0x2E, 0xB8, 0xA0);

    private readonly BpmDisplayLock _bpmLock = new();
    private readonly DispatcherTimer _tickTimer;
    private readonly DispatcherTimer _displayTimer;
    private readonly TextBlock _bpmValue;
    private readonly MonitorLatchButton _bpm;
    private readonly MonitorLatchButton _mono;
    private readonly MonitorLatchButton _dim;
    private readonly MonitorLatchButton _lowCut;
    private AudioPlayer? _player;
    private int _bpmPlayerGeneration = int.MinValue;
    private BpmMonitorMode _bpmMode = BpmMonitorMode.Off;
    private bool _washThrough;
    private bool _videoOverlayLabels;

    public MonitorToggleColumn()
    {
        Orientation = Orientation.Vertical;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;

        _bpmValue = new TextBlock
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = DesignMetrics.MonitorBpmValueFontSize,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Text = "—",
            Margin = new Thickness(0, 0, 0, 1),
            IsHitTestVisible = false,
        };
        _bpm = CreateButton("BPM", BpmOn, UiStrings.TooltipMonitorBpm, Bpm_Click);
        _bpm.Margin = new Thickness(0, 0, 0, DesignMetrics.TransportButtonGap);

        _mono = CreateButton(UiStrings.LabelMonitorMono, MonoOn, UiStrings.TooltipMonitorMono, Mono_Click);
        _dim = CreateButton(
            UiStrings.LabelMonitorDim,
            DimOn,
            UiStrings.TooltipMonitorDim(AppStorage.Settings.ResolvedMonitorDimDb()),
            Dim_Click);
        _dim.Margin = new Thickness(0, DesignMetrics.TransportButtonGap, 0, 0);
        _lowCut = CreateButton(
            UiStrings.LabelMonitorLowCut,
            LowCutOn,
            UiStrings.TooltipMonitorLowCut(AppStorage.Settings.ResolvedMonitorLowCutHz()),
            LowCut_Click);
        _lowCut.Margin = new Thickness(0, DesignMetrics.TransportButtonGap, 0, 0);

        Children.Add(_bpmValue);
        Children.Add(_bpm);
        Children.Add(_mono);
        Children.Add(_dim);
        Children.Add(_lowCut);
        ApplyBpmChrome();
        ApplyUniformButtonWidth();

        _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _tickTimer.Tick += (_, _) => OnAnalyzeTick();
        _tickTimer.Start();

        _displayTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _displayTimer.Tick += (_, _) => FlushDisplayBpm();
        _displayTimer.Start();
    }

    public AudioPlayer? Player
    {
        get => _player;
        set
        {
            _player = value;
            if (_player is not null)
            {
                _player.SetBpmMonitorMode(_bpmMode);
            }

            SyncFromPlayer();
        }
    }

    internal bool WashThrough
    {
        get => _washThrough;
        set
        {
            if (_washThrough == value)
            {
                return;
            }

            _washThrough = value;
            _bpm.WashThrough = value;
            _mono.WashThrough = value;
            _dim.WashThrough = value;
            _lowCut.WashThrough = value;
            _bpm.InvalidateVisual();
            _mono.InvalidateVisual();
            _dim.InvalidateVisual();
            _lowCut.InvalidateVisual();
        }
    }

    internal bool VideoOverlayLabels
    {
        get => _videoOverlayLabels;
        set
        {
            if (_videoOverlayLabels == value)
            {
                return;
            }

            _videoOverlayLabels = value;
            ApplyBpmChrome();
        }
    }

    public void ApplyLocalizedText()
    {
        _mono.LatchLabel = UiStrings.LabelMonitorMono;
        _dim.LatchLabel = UiStrings.LabelMonitorDim;
        _lowCut.LatchLabel = UiStrings.LabelMonitorLowCut;
        TransportToolTip.Attach(_bpm, UiStrings.TooltipMonitorBpm);
        TransportToolTip.Attach(_mono, UiStrings.TooltipMonitorMono);
        TransportToolTip.Attach(
            _dim,
            UiStrings.TooltipMonitorDim(AppStorage.Settings.ResolvedMonitorDimDb()));
        TransportToolTip.Attach(
            _lowCut,
            UiStrings.TooltipMonitorLowCut(AppStorage.Settings.ResolvedMonitorLowCutHz()));
        ApplyUniformButtonWidth();
        ApplyBpmChrome();
        _bpm.InvalidateVisual();
        _mono.InvalidateVisual();
        _dim.InvalidateVisual();
        _lowCut.InvalidateVisual();
    }

    public void StopTicks()
    {
        _tickTimer.Stop();
        _displayTimer.Stop();
        _player?.SetBpmMonitorMode(BpmMonitorMode.Off);
    }

    private void ApplyUniformButtonWidth()
    {
        var width = Math.Max(
            DesignMetrics.MonitorToggleWidth,
            Math.Max(
                _bpm.MeasureLabelWidth(),
                Math.Max(
                    _mono.MeasureLabelWidth(),
                    Math.Max(_dim.MeasureLabelWidth(), _lowCut.MeasureLabelWidth()))));

        Width = width;
        _bpm.Width = width;
        _mono.Width = width;
        _dim.Width = width;
        _lowCut.Width = width;
    }

    private static MonitorLatchButton CreateButton(
        string label,
        Color latchColor,
        string tooltip,
        RoutedEventHandler click)
    {
        var button = new MonitorLatchButton
        {
            LatchLabel = label,
            LatchColor = latchColor,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        TransportToolTip.Attach(button, tooltip);
        button.Click += click;
        return button;
    }

    private void SyncFromPlayer()
    {
        if (_player is null)
        {
            return;
        }

        _mono.IsLatched = _player.MonitorMono;
        _dim.IsLatched = _player.MonitorDim;
        _lowCut.IsLatched = _player.MonitorLowCut;
    }

    /// <summary>MONO 切替（ボタン／Alt+M）。</summary>
    public void ToggleMono() => Mono_Click(_mono, new RoutedEventArgs());

    /// <summary>DIM 切替（ボタン／Alt+D）。</summary>
    public void ToggleDim() => Dim_Click(_dim, new RoutedEventArgs());

    /// <summary>LC 切替（ボタン／Alt+C）。</summary>
    public void ToggleLowCut() => LowCut_Click(_lowCut, new RoutedEventArgs());

    /// <summary>BPM オフ／オン切替（ボタン／Alt+B）。オン時は表示＋クリック。</summary>
    public void CycleBpmMode() => Bpm_Click(_bpm, new RoutedEventArgs());

    private void Mono_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        _player.SetMonitorMono(!_player.MonitorMono);
        _mono.IsLatched = _player.MonitorMono;
    }

    private void Dim_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        _player.SetMonitorDim(!_player.MonitorDim);
        _dim.IsLatched = _player.MonitorDim;
    }

    private void LowCut_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        _player.SetMonitorLowCut(!_player.MonitorLowCut);
        _lowCut.IsLatched = _player.MonitorLowCut;
    }

    private void Bpm_Click(object sender, RoutedEventArgs e)
    {
        _bpmMode = _bpmMode == BpmMonitorMode.Off
            ? BpmMonitorMode.On
            : BpmMonitorMode.Off;

        if (_bpmMode == BpmMonitorMode.Off)
        {
            ClearBpmState(resetGeneration: true);
        }

        _player?.SetBpmMonitorMode(_bpmMode);
        ApplyBpmChrome();
    }

    private void OnAnalyzeTick()
    {
        if (_bpmMode == BpmMonitorMode.Off || _player is null)
        {
            return;
        }

        if (!IsVisible || Opacity < 0.05 || !_player.IsPlaying)
        {
            if (_bpmLock.DisplayBpm is not null || _bpmValue.Text != "—")
            {
                ClearBpmState(resetGeneration: true);
                _player.SetBpmMonitorMode(_bpmMode);
            }

            return;
        }

        var generation = _player.Generation;
        if (generation != _bpmPlayerGeneration)
        {
            ClearBpmState(resetGeneration: false);
            _bpmPlayerGeneration = generation;
            _player.ResetBpmMonitor();
        }

        if (_player.TryGetBpmMonitorBpm(out var bpm))
        {
            _bpmLock.Push(bpm);
        }
    }

    private void FlushDisplayBpm()
    {
        if (_bpmMode == BpmMonitorMode.Off)
        {
            _bpmValue.Text = "—";
            return;
        }

        if (_bpmLock.DisplayBpm is not { } bpm)
        {
            if (_player is not { IsPlaying: true })
            {
                _bpmValue.Text = "—";
            }

            return;
        }

        _bpmValue.Text = Math.Round(bpm).ToString("0", CultureInfo.InvariantCulture);
    }

    private void ClearBpmState(bool resetGeneration)
    {
        _bpmLock.Reset();
        if (resetGeneration)
        {
            _bpmPlayerGeneration = int.MinValue;
        }

        _bpmValue.Text = "—";
    }

    private void ApplyBpmChrome()
    {
        var on = _bpmMode == BpmMonitorMode.On;
        _bpm.LatchLabel = "BPM";
        _bpm.IsLatched = on;
        _bpm.LatchColor = BpmOn;
        _bpm.InvalidateVisual();

        Color valueColor;
        if (_videoOverlayLabels)
        {
            valueColor = on ? Colors.White : LibraryPlayerMode.VideoOverlayLabelColor;
        }
        else
        {
            valueColor = on ? BpmOn : Theme.Get("MutedForeBrush");
        }

        _bpmValue.Foreground = WpfControlHelpers.FrozenBrush(valueColor);
    }
}
