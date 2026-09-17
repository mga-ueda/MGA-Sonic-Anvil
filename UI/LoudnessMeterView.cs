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
    private int _offlineRevision = int.MinValue;
    private int _offlineSampleCount = -1;
    private int _offlineGen;
    private bool _offlineMode = true;
    private float _previewLinear = 1f;
    private AudioDocument? _displayDocument;
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
            Margin = new Thickness(DesignMetrics.LoudnessMeterCharWidth, 0, DesignMetrics.LoudnessMeterCharWidth, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 5; i++)
        {
            metrics.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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
            _previewLinear = 1f;
            InvalidateOffline();
            RefreshOffline();
        }
    }

    /// <summary>停止中メーターを今の波形で測り直す。同じバッファへの上書きも SampleRevision で検知する。</summary>
    public void RefreshOffline()
    {
        if (IsLivePlayback())
        {
            return;
        }

        RefreshStopped();
    }

    /// <summary>V プレビュー用。再生中は出力側がゲイン済みなので、停止中の塗りだけ動かす。</summary>
    public void SetPreviewLinearGain(float linear)
    {
        if (!float.IsFinite(linear) || linear < 0f)
        {
            linear = 0f;
        }

        if (Math.Abs(_previewLinear - linear) < 1e-7f)
        {
            return;
        }

        _previewLinear = linear;
        if (!IsLivePlayback())
        {
            PaintSnapshot(_lastSnap, _offlineMode);
        }
    }

    /// <summary>V 確定直前。プレビューを表示値に焼き、再計測が終わるまで塗りを戻さない。</summary>
    public void CommitPreview()
    {
        if (Math.Abs(_previewLinear - 1f) < 1e-7f)
        {
            return;
        }

        _lastSnap = LoudnessMeterEngine.ApplyLinearGain(_lastSnap, _previewLinear);
        _previewLinear = 1f;
        if (!IsLivePlayback())
        {
            PaintSnapshot(_lastSnap, _offlineMode);
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
        PaintSnapshot(_lastSnap, _offlineMode);
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
        var chrome = MutedFore();
        foreach (var block in ChromeBlocks())
        {
            block.Foreground = chrome;
        }

        PaintSnapshot(_lastSnap, _offlineMode);
    }

    public void ResetLive()
    {
        _engine.Reset();
    }

    public void Reset()
    {
        ResetLive();
        _previewLinear = 1f;
        InvalidateOffline();
        RefreshOffline();
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

    internal static bool CanReuseOffline(
        object? cachedSamples,
        int cachedRevision,
        int cachedCount,
        object? samples,
        int revision,
        int count) =>
        ReferenceEquals(cachedSamples, samples)
        && cachedRevision == revision
        && cachedCount == count;

    internal static Color ChipTextColor(UiTheme theme) =>
        theme == UiTheme.Light ? Colors.White : Colors.Black;

    internal static Color ShadeChipFill(Color color, UiTheme theme)
    {
        var toward = theme == UiTheme.Light ? Colors.White : Colors.Black;
        return Mix(color, toward, 0.22);
    }

    private bool IsLivePlayback() =>
        Player is { IsPlaying: true, IsScrubbing: false };

    private void RefreshStopped()
    {
        var document = _document;
        if (document is null)
        {
            InvalidateOffline();
            _previewLinear = 1f;
            ApplySnapshot(LoudnessSnapshot.Idle, offline: true);
            return;
        }

        EnsureOffline();
        LoudnessSnapshot? snap;
        lock (_offlineGate)
        {
            snap = _offline;
        }

        if (snap is { } ready)
        {
            ApplySnapshot(ready, offline: true);
            return;
        }

        if (!ReferenceEquals(_displayDocument, document))
        {
            ApplySnapshot(LoudnessSnapshot.Idle, offline: true);
        }
    }

    private void InvalidateOffline()
    {
        lock (_offlineGate)
        {
            _offlineGen++;
            _offline = null;
            _offlineSamples = null;
            _offlineRevision = int.MinValue;
            _offlineSampleCount = -1;
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
        int used;
        int rate;
        int channels;
        double target;
        lock (_offlineGate)
        {
            samples = document.Interleaved;
            used = Math.Clamp(document.SampleCount, 0, samples.Length);
            if (CanReuseOffline(
                    _offlineSamples,
                    _offlineRevision,
                    _offlineSampleCount,
                    samples,
                    document.SampleRevision,
                    used))
            {
                return;
            }

            gen = ++_offlineGen;
            rate = document.SampleRate;
            channels = document.Channels;
            target = _engine.TargetLufs;
            _offlineSamples = samples;
            _offlineRevision = document.SampleRevision;
            _offlineSampleCount = used;
            _offline = null;
        }

        Task.Run(() =>
        {
            var snap = LoudnessMeterEngine.MeasureFile(samples, channels, rate, target, used);
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
        _displayDocument = _document;
        PaintSnapshot(snap, offline);
    }

    private void PaintSnapshot(LoudnessSnapshot snap, bool offline)
    {
        var shown = offline ? LoudnessMeterEngine.ApplyLinearGain(snap, _previewLinear) : snap;
        _shortValue.Text = FormatLufs(shown.ShortTermLufs);
        _integratedValue.Text = FormatLufs(shown.IntegratedLufs);
        _momentaryValue.Text = FormatLufs(shown.MomentaryMaxLufs);
        _lraValue.Text = FormatLu(shown.LoudnessRangeLu);
        _truePeakValue.Text = FormatLufs(shown.TruePeakDb);
        PaintValues(shown);
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
            FontWeight = FontWeights.Normal,
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
        var theme = UiThemeService.Current;
        try
        {
            return WpfControlHelpers.FrozenBrush(Theme.Get("MutedForeBrush"));
        }
        catch (InvalidOperationException)
        {
            return Freeze(
                theme == UiTheme.Light
                    ? Color.FromRgb(0x3F, 0x3F, 0x42)
                    : Color.FromRgb(0x96, 0x96, 0x96));
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
