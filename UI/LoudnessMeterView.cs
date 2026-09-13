using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>スペアナ左のラウドネス。再生中は出力のリアルタイム、停止中は波形全体。</summary>
internal sealed class LoudnessMeterView : Grid
{
    private static readonly FontFamily Mono = new("Consolas");
    private const double LineFontSize = 12;

    private readonly LoudnessMeterEngine _engine = new();
    private readonly float[] _left = new float[8192];
    private readonly float[] _right = new float[8192];
    private readonly TextBlock _shortCaption;
    private readonly TextBlock _integratedCaption;
    private readonly TextBlock _momentaryCaption;
    private readonly TextBlock _lraCaption;
    private readonly TextBlock _truePeakCaption;
    private readonly TextBlock _shortUnit;
    private readonly TextBlock _integratedUnit;
    private readonly TextBlock _momentaryUnit;
    private readonly TextBlock _lraUnit;
    private readonly TextBlock _truePeakUnit;
    private readonly Border _shortValueBox;
    private readonly Border _integratedValueBox;
    private readonly Border _momentaryValueBox;
    private readonly Border _lraValueBox;
    private readonly Border _truePeakValueBox;
    private readonly TextBlock _shortValue;
    private readonly TextBlock _integratedValue;
    private readonly TextBlock _momentaryValue;
    private readonly TextBlock _lraValue;
    private readonly TextBlock _truePeakValue;
    private readonly object _offlineGate = new();
    private LoudnessSnapshot _lastSnap = LoudnessSnapshot.Idle;
    private LoudnessSnapshot? _offline;
    private object? _offlineSamples;
    private int _offlineGen;
    private bool _offlineMode = true;
    private AudioDocument? _document;
    private Brush _safeBrush = Brushes.Transparent;
    private Brush _cautionBrush = Brushes.Transparent;
    private Brush _dangerBrush = Brushes.Transparent;
    private Brush _idleBrush = Brushes.Transparent;
    private Brush _chipFore = Brushes.Black;
    private Brush _safeChipBrush = Brushes.Transparent;
    private Brush _cautionChipBrush = Brushes.Transparent;
    private Brush _dangerChipBrush = Brushes.Transparent;

    public LoudnessMeterView()
    {
        Width = DesignMetrics.LoudnessMeterWidth;
        MinWidth = DesignMetrics.LoudnessMeterWidth;
        Height = DesignMetrics.SpectrumHeight;
        VerticalAlignment = VerticalAlignment.Stretch;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = false;

        (_shortValueBox, _shortValue) = MetricValue();
        (_integratedValueBox, _integratedValue) = MetricValue();
        (_momentaryValueBox, _momentaryValue) = MetricValue();
        (_lraValueBox, _lraValue) = MetricValue();
        (_truePeakValueBox, _truePeakValue) = MetricValue();
        _shortCaption = Chrome();
        _integratedCaption = Chrome();
        _momentaryCaption = Chrome();
        _lraCaption = Chrome();
        _truePeakCaption = Chrome();
        _shortUnit = Chrome();
        _integratedUnit = Chrome();
        _momentaryUnit = Chrome();
        _lraUnit = Chrome();
        _truePeakUnit = Chrome();

        var metrics = new Grid
        {
            Margin = new Thickness(4, 2, 6, 2),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++)
        {
            metrics.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        }

        AddMetric(metrics, 0, _shortCaption, _shortValueBox, _shortUnit);
        AddMetric(metrics, 1, _integratedCaption, _integratedValueBox, _integratedUnit);
        AddMetric(metrics, 2, _momentaryCaption, _momentaryValueBox, _momentaryUnit);
        AddMetric(metrics, 3, _lraCaption, _lraValueBox, _lraUnit);
        AddMetric(metrics, 4, _truePeakCaption, _truePeakValueBox, _truePeakUnit);
        Children.Add(metrics);

        ApplyLocalizedText();
        ApplyTargetFromSettings();
        ApplySnapshot(LoudnessSnapshot.Idle, offline: true);
    }

    public AudioPlayer? Player { get; set; }

    public AudioDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value))
            {
                return;
            }

            _document = value;
            InvalidateOffline();
            if (!IsLivePlayback())
            {
                RefreshStopped();
            }
        }
    }

    public void ApplyLocalizedText()
    {
        _shortCaption.Text = UiStrings.LabelLoudnessShortTerm;
        _integratedCaption.Text = UiStrings.LabelLoudnessIntegrated;
        _momentaryCaption.Text = UiStrings.LabelLoudnessMomentary;
        _shortUnit.Text = UiStrings.LabelLufs;
        _integratedUnit.Text = UiStrings.LabelLufs;
        _momentaryUnit.Text = UiStrings.LabelMaxLufs;
        _lraCaption.Text = UiStrings.LabelLra;
        _truePeakCaption.Text = UiStrings.LabelTruePeak;
        _lraUnit.Text = UiStrings.LabelLu;
        _truePeakUnit.Text = UiStrings.LabelDb;
        ApplyValueColors();
    }

    public void ApplyTargetFromSettings()
    {
        _engine.TargetLufs = AppStorage.Settings.ResolvedLoudnessTargetLufs();
        PaintValues(_lastSnap);
    }

    public void ApplyValueColors()
    {
        _safeBrush = ThemeBrush("VectorScopeTraceBrush", LoudnessTrafficLight.SafeR, LoudnessTrafficLight.SafeG, LoudnessTrafficLight.SafeB);
        _cautionBrush = ThemeBrush("MarkerBrush", LoudnessTrafficLight.CautionR, LoudnessTrafficLight.CautionG, LoudnessTrafficLight.CautionB);
        _dangerBrush = ThemeBrush("StatusBarErrorDetailForeBrush", LoudnessTrafficLight.DangerR, LoudnessTrafficLight.DangerG, LoudnessTrafficLight.DangerB);
        _idleBrush = MutedFore();
        _chipFore = ChipFore();
        _safeChipBrush = ShadeBrush(_safeBrush);
        _cautionChipBrush = ShadeBrush(_cautionBrush);
        _dangerChipBrush = ShadeBrush(_dangerBrush);
        var chrome = ChromeFore();
        foreach (var block in ChromeBlocks())
        {
            block.Foreground = chrome;
        }

        PaintValues(_lastSnap);
    }

    public void ResetLive()
    {
        _engine.Reset();
    }

    public void Reset()
    {
        ResetLive();
        InvalidateOffline();
        if (!IsLivePlayback())
        {
            RefreshStopped();
        }
    }

    public void Tick()
    {
        if (IsLivePlayback())
        {
            var frames = Player!.TakeLoudnessFrames(_left, _right);
            if (frames > 0)
            {
                ApplySnapshot(_engine.Process(_left, _right, frames, Player.OutputSampleRate), offline: false);
            }

            return;
        }

        RefreshStopped();
    }

    internal static bool UsesFillChip(bool offline, LoudnessTraffic traffic) =>
        offline && traffic is not LoudnessTraffic.Idle;

    internal static Color ChipTextColor(UiTheme theme) =>
        theme == UiTheme.Light ? Colors.White : Colors.Black;

    internal static string ChromeForeKey(UiTheme theme) =>
        theme == UiTheme.Light ? "MutedForeBrush" : "SpectrogramScaleForeBrush";

    internal static Color ShadeChipFill(Color color, UiTheme theme)
    {
        var toward = theme == UiTheme.Light ? Colors.White : Colors.Black;
        return Mix(color, toward, 0.22);
    }

    private bool IsLivePlayback() =>
        Player is { IsPlaying: true, IsScrubbing: false };

    private void RefreshStopped()
    {
        EnsureOffline();
        ApplySnapshot(_offline ?? LoudnessSnapshot.Idle, offline: true);
    }

    private void InvalidateOffline()
    {
        lock (_offlineGate)
        {
            _offlineGen++;
            _offline = null;
            _offlineSamples = null;
        }
    }

    private void EnsureOffline()
    {
        var document = _document;
        if (document is null)
        {
            InvalidateOffline();
            return;
        }

        int gen;
        float[] samples;
        int rate;
        int channels;
        double target;
        lock (_offlineGate)
        {
            if (ReferenceEquals(_offlineSamples, document.Interleaved))
            {
                return;
            }

            gen = ++_offlineGen;
            samples = document.Interleaved;
            rate = document.SampleRate;
            channels = document.Channels;
            target = _engine.TargetLufs;
            _offlineSamples = samples;
            _offline = null;
        }

        Task.Run(() =>
        {
            var snap = LoudnessMeterEngine.MeasureFile(samples, channels, rate, target);
            Dispatcher.BeginInvoke(
                () =>
                {
                    lock (_offlineGate)
                    {
                        if (gen != _offlineGen)
                        {
                            return;
                        }

                        _offline = snap;
                    }

                    if (!IsLivePlayback())
                    {
                        ApplySnapshot(snap, offline: true);
                    }
                },
                DispatcherPriority.Background);
        });
    }

    private void ApplySnapshot(LoudnessSnapshot snap, bool offline)
    {
        _lastSnap = snap;
        _offlineMode = offline;
        _shortValue.Text = FormatLufs(snap.ShortTermLufs);
        _integratedValue.Text = FormatLufs(snap.IntegratedLufs);
        _momentaryValue.Text = FormatLufs(snap.MomentaryMaxLufs);
        _lraValue.Text = FormatLu(snap.LoudnessRangeLu);
        _truePeakValue.Text = FormatLufs(snap.TruePeakDb);
        PaintValues(snap);
    }

    private void PaintValues(LoudnessSnapshot snap)
    {
        var target = _engine.TargetLufs;
        PaintReading(_shortValueBox, _shortValue, LoudnessTrafficLight.ForLufs(snap.ShortTermLufs, target));
        PaintReading(_integratedValueBox, _integratedValue, LoudnessTrafficLight.ForLufs(snap.IntegratedLufs, target));
        PaintReading(_momentaryValueBox, _momentaryValue, LoudnessTrafficLight.ForLufs(snap.MomentaryMaxLufs, target));
        PaintReading(_lraValueBox, _lraValue, LoudnessTrafficLight.ForLra(snap.LoudnessRangeLu));
        PaintReading(_truePeakValueBox, _truePeakValue, LoudnessTrafficLight.ForTruePeak(snap.TruePeakDb));
    }

    private void PaintReading(Border box, TextBlock value, LoudnessTraffic traffic)
    {
        var fill = TrafficBrush(traffic);
        if (UsesFillChip(_offlineMode, traffic))
        {
            box.Background = ChipFill(traffic);
            value.Foreground = _chipFore;
            return;
        }

        box.Background = Brushes.Transparent;
        value.Foreground = fill;
    }

    private Brush TrafficBrush(LoudnessTraffic traffic) => traffic switch
    {
        LoudnessTraffic.Safe => _safeBrush,
        LoudnessTraffic.Caution => _cautionBrush,
        LoudnessTraffic.Danger => _dangerBrush,
        _ => _idleBrush,
    };

    private Brush ChipFill(LoudnessTraffic traffic) => traffic switch
    {
        LoudnessTraffic.Safe => _safeChipBrush,
        LoudnessTraffic.Caution => _cautionChipBrush,
        LoudnessTraffic.Danger => _dangerChipBrush,
        _ => _idleBrush,
    };

    private IEnumerable<TextBlock> ChromeBlocks() =>
    [
        _shortCaption,
        _integratedCaption,
        _momentaryCaption,
        _lraCaption,
        _truePeakCaption,
        _shortUnit,
        _integratedUnit,
        _momentaryUnit,
        _lraUnit,
        _truePeakUnit,
    ];

    private static string FormatLufs(float value) => FormatReading(value);

    private static string FormatLu(float value) => FormatReading(value);

    private static string FormatReading(float value)
    {
        var text = float.IsInfinity(value) || float.IsNaN(value)
            ? "--.-"
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return text.PadLeft(5);
    }

    private static void AddMetric(Grid metrics, int row, TextBlock caption, FrameworkElement value, TextBlock unit)
    {
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        caption.TextAlignment = TextAlignment.Right;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        unit.HorizontalAlignment = HorizontalAlignment.Left;
        unit.TextAlignment = TextAlignment.Left;
        SetRow(caption, row);
        SetColumn(caption, 0);
        SetRow(value, row);
        SetColumn(value, 1);
        SetRow(unit, row);
        SetColumn(unit, 2);
        metrics.Children.Add(caption);
        metrics.Children.Add(value);
        metrics.Children.Add(unit);
    }

    private static (Border Box, TextBlock Value) MetricValue()
    {
        var value = new TextBlock
        {
            FontFamily = Mono,
            FontSize = LineFontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
        };
        var box = new Border
        {
            Child = value,
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(3, 1, 3, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
            Background = Brushes.Transparent,
        };
        return (box, value);
    }

    private static TextBlock Chrome() =>
        new()
        {
            FontFamily = Mono,
            FontSize = LineFontSize,
            FontWeight = FontWeights.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap,
        };

    private static Brush ThemeBrush(string key, byte r, byte g, byte b)
    {
        try
        {
            return WpfControlHelpers.FrozenBrush(Theme.Get(key));
        }
        catch (InvalidOperationException)
        {
            return Freeze(Color.FromRgb(r, g, b));
        }
    }

    private static Brush ChipFore() => Freeze(ChipTextColor(UiThemeService.Current));

    private static Brush ChromeFore()
    {
        var theme = UiThemeService.Current;
        try
        {
            return WpfControlHelpers.FrozenBrush(Theme.Get(ChromeForeKey(theme)));
        }
        catch (InvalidOperationException)
        {
            return theme == UiTheme.Light
                ? Freeze(Color.FromRgb(0x3F, 0x3F, 0x42))
                : Freeze(Color.FromRgb(0xEB, 0xEB, 0xEB));
        }
    }

    private static Brush ShadeBrush(Brush source)
    {
        var color = source is SolidColorBrush solid ? solid.Color : Colors.Gray;
        return Freeze(ShadeChipFill(color, UiThemeService.Current));
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromRgb(
            (byte)Math.Round(from.R + (to.R - from.R) * amount),
            (byte)Math.Round(from.G + (to.G - from.G) * amount),
            (byte)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static Brush MutedFore()
    {
        try
        {
            return WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        }
        catch (InvalidOperationException)
        {
            return Freeze(Color.FromRgb(0x96, 0x96, 0x96));
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
