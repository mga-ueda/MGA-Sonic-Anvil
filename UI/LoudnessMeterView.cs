using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>スペアナ左のラウドネス表示。再生出力を BS.1770 で積む。</summary>
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
    private readonly TextBlock _shortValue;
    private readonly TextBlock _integratedValue;
    private readonly TextBlock _momentaryValue;
    private readonly TextBlock _lraValue;
    private readonly TextBlock _truePeakValue;
    private LoudnessSnapshot _lastSnap = LoudnessSnapshot.Idle;
    private Brush _safeBrush = Brushes.Transparent;
    private Brush _cautionBrush = Brushes.Transparent;
    private Brush _dangerBrush = Brushes.Transparent;
    private Brush _idleBrush = Brushes.Transparent;
    private bool _idle = true;

    public LoudnessMeterView()
    {
        Width = DesignMetrics.LoudnessMeterWidth;
        MinWidth = DesignMetrics.LoudnessMeterWidth;
        Height = DesignMetrics.SpectrumHeight;
        VerticalAlignment = VerticalAlignment.Stretch;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        ClipToBounds = false;

        _shortValue = MetricValue();
        _integratedValue = MetricValue();
        _momentaryValue = MetricValue();
        _lraValue = MetricValue();
        _truePeakValue = MetricValue();
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

        AddMetric(metrics, 0, _shortCaption, _shortValue, _shortUnit);
        AddMetric(metrics, 1, _integratedCaption, _integratedValue, _integratedUnit);
        AddMetric(metrics, 2, _momentaryCaption, _momentaryValue, _momentaryUnit);
        AddMetric(metrics, 3, _lraCaption, _lraValue, _lraUnit);
        AddMetric(metrics, 4, _truePeakCaption, _truePeakValue, _truePeakUnit);
        Children.Add(metrics);

        ApplyLocalizedText();
        ApplyTargetFromSettings();
        ApplySnapshot(LoudnessSnapshot.Idle);
    }

    public AudioPlayer? Player { get; set; }

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
        PaintValues(_lastSnap);
    }

    public void Reset()
    {
        _engine.Reset();
        _idle = true;
        ApplySnapshot(LoudnessSnapshot.Idle);
    }

    public void Tick()
    {
        var player = Player;
        if (player is { IsPlaying: true, IsScrubbing: false })
        {
            var frames = player.TakeLoudnessFrames(_left, _right);
            if (frames > 0)
            {
                ApplySnapshot(_engine.Process(_left, _right, frames, player.OutputSampleRate));
                _idle = false;
            }

            return;
        }

        if (_idle)
        {
            return;
        }

        ApplySnapshot(_engine.Snapshot);
        _idle = true;
    }

    private void ApplySnapshot(LoudnessSnapshot snap)
    {
        _lastSnap = snap;
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
        _shortValue.Foreground = TrafficBrush(LoudnessTrafficLight.ForLufs(snap.ShortTermLufs, target));
        _integratedValue.Foreground = TrafficBrush(LoudnessTrafficLight.ForLufs(snap.IntegratedLufs, target));
        _momentaryValue.Foreground = TrafficBrush(LoudnessTrafficLight.ForLufs(snap.MomentaryMaxLufs, target));
        _lraValue.Foreground = TrafficBrush(LoudnessTrafficLight.ForLra(snap.LoudnessRangeLu));
        _truePeakValue.Foreground = TrafficBrush(LoudnessTrafficLight.ForTruePeak(snap.TruePeakDb));
    }

    private Brush TrafficBrush(LoudnessTraffic traffic) => traffic switch
    {
        LoudnessTraffic.Safe => _safeBrush,
        LoudnessTraffic.Caution => _cautionBrush,
        LoudnessTraffic.Danger => _dangerBrush,
        _ => _idleBrush,
    };

    private static string FormatLufs(float value) => FormatReading(value);

    private static string FormatLu(float value) => FormatReading(value);

    private static string FormatReading(float value)
    {
        var text = float.IsInfinity(value) || float.IsNaN(value)
            ? "--.-"
            : value.ToString("0.0", CultureInfo.InvariantCulture);
        return text.PadLeft(5);
    }

    private static void AddMetric(Grid metrics, int row, TextBlock caption, TextBlock value, TextBlock unit)
    {
        caption.HorizontalAlignment = HorizontalAlignment.Right;
        caption.TextAlignment = TextAlignment.Right;
        value.HorizontalAlignment = HorizontalAlignment.Right;
        value.TextAlignment = TextAlignment.Right;
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

    private static TextBlock MetricValue() =>
        new()
        {
            FontFamily = Mono,
            FontSize = LineFontSize,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 4, 0),
        };

    private static TextBlock Chrome() =>
        new()
        {
            FontFamily = Mono,
            FontSize = LineFontSize,
            FontWeight = FontWeights.Normal,
            Foreground = MutedFore(),
            VerticalAlignment = VerticalAlignment.Center,
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
