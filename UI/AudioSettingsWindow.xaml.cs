using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;

namespace MgaSonicAnvil.UI;

internal partial class AudioSettingsWindow : Window
{
    private readonly FadeCurveRow _fadeInRow;
    private readonly FadeCurveRow _fadeOutRow;
    private ContextMenu? _fadeCurveMenu;

    public AudioOutputSettings SelectedSettings { get; private set; }

    public FadeShape FadeInCurve => _fadeInRow.Curve;

    public FadeShape FadeOutCurve => _fadeOutRow.Curve;

    public UiLanguageChoice SelectedLanguage { get; private set; }

    public double SelectedLoudnessTargetLufs { get; private set; }

    public int SelectedMp3BitRate { get; private set; }

    public string SelectedLameExePath { get; private set; } = string.Empty;

    public string SelectedLameOptions { get; private set; } = Mp3Encode.DefaultLameOptions;

    public int SelectedExportParallelism { get; private set; }

    public AudioSettingsWindow(
        AudioOutputSettings current,
        FadeShape fadeIn,
        FadeShape fadeOut,
        UiLanguageChoice language,
        double loudnessTargetLufs,
        int mp3BitRate,
        string lameExePath,
        string lameOptions,
        int exportParallelism)
    {
        SelectedSettings = current;
        SelectedLanguage = language;
        SelectedLoudnessTargetLufs = LoudnessMeterEngine.ClampTargetLufs(loudnessTargetLufs);
        SelectedMp3BitRate = Mp3Encode.ClampWindowsBitRate(mp3BitRate);
        SelectedLameExePath = lameExePath ?? string.Empty;
        SelectedLameOptions = lameOptions ?? string.Empty;
        SelectedExportParallelism = exportParallelism;
        InitializeComponent();
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        Title = UiStrings.DialogSettingsTitle;

        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Auto, UiStrings.LabelLanguageAuto));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.Japanese, UiStrings.LabelLanguageJapanese));
        LanguageCombo.Items.Add(new LanguageItem(UiLanguageChoice.English, UiStrings.LabelLanguageEnglish));
        SelectLanguage(language);

        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.WaveOut, UiStrings.LabelAudioApiWaveOut));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Wasapi, UiStrings.LabelAudioApiWasapi));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Asio, UiStrings.LabelAudioApiAsio));

        ActionButtonLooks.ApplyAccent(OkButton);
        ActionButtonLooks.ApplyClear(CancelButton);
        ActionButtonLooks.ApplyClear(LameBrowseButton);

        _fadeInRow = CreateFadeRow(UiStrings.LabelDefaultFadeIn, fadeIn, isFadeIn: true);
        _fadeOutRow = CreateFadeRow(UiStrings.LabelDefaultFadeOut, fadeOut, isFadeIn: false);
        FadeRowsHost.Children.Add(_fadeInRow.Host);
        FadeRowsHost.Children.Add(_fadeOutRow.Host);

        SelectApi(current.Api);
        ReloadDevices(current.DeviceId);
        LoudnessTargetBox.Text = SelectedLoudnessTargetLufs.ToString("0.#", CultureInfo.InvariantCulture);
        FillWindowsBitRates(SelectedMp3BitRate);
        LamePathBox.Text = SelectedLameExePath;
        LameOptionsBox.Text = Mp3Encode.ResolveLameOptions(SelectedLameOptions);
        FillExportParallelism(SelectedExportParallelism);
        ApplyTips();
        Loaded += (_, _) =>
        {
            if (Owner is { Topmost: true })
            {
                Topmost = true;
            }
        };
    }

    private void ApplyTips()
    {
        TipService.Set(LanguageLabel, UiStrings.TipUiLanguage);
        TipService.Set(LanguageCombo, UiStrings.TipUiLanguage);
        TipService.Set(ApiLabel, UiStrings.TipAudioApi);
        TipService.Set(ApiCombo, UiStrings.TipAudioApi);
        TipService.Set(DeviceLabel, UiStrings.TipAudioDevice);
        TipService.Set(DeviceCombo, UiStrings.TipAudioDevice);
        TipService.Set(LoudnessTargetLabel, UiStrings.TipLoudnessTarget);
        TipService.Set(LoudnessTargetBox, UiStrings.TipLoudnessTarget);
        TipService.Set(LoudnessTargetUnit, UiStrings.TipLoudnessTarget);
        TipService.Set(FadeDefaultsHeader, UiStrings.TipFadeCurveDefaults);
        TipService.Set(Mp3Header, UiStrings.TipMp3Encode);
        TipService.Set(WindowsBitRateLabel, UiStrings.TipWindowsMp3BitRate);
        TipService.Set(WindowsBitRateCombo, UiStrings.TipWindowsMp3BitRate);
        TipService.Set(LamePathLabel, UiStrings.TipLamePath);
        TipService.Set(LamePathBox, UiStrings.TipLamePath);
        TipService.Set(LameBrowseButton, UiStrings.TipLameBrowse);
        TipService.Set(LameOptionsLabel, UiStrings.TipLameOptions);
        TipService.Set(LameOptionsBox, UiStrings.TipLameOptions);
        TipService.Set(ExportParallelLabel, UiStrings.TipExportParallel);
        TipService.Set(ExportParallelCombo, UiStrings.TipExportParallel);
        TipService.Set(OkButton, UiStrings.TipSettingsOk);
        TipService.Set(CancelButton, UiStrings.TipSettingsCancel);
    }

    private void ApiCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ReloadDevices(preferredDeviceId: null);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (ApiCombo.SelectedItem is not ApiItem api)
        {
            DialogResult = false;
            return;
        }

        var deviceId = DeviceCombo.SelectedItem is DeviceItem device
            ? device.Id
            : string.Empty;
        SelectedSettings = new AudioOutputSettings(api.Api, deviceId);
        SelectedLanguage = LanguageCombo.SelectedItem is LanguageItem item
            ? item.Choice
            : UiLanguageChoice.Auto;
        if (!LoudnessMeterEngine.TryParseTargetLufs(LoudnessTargetBox.Text, out var target))
        {
            OwnerCenteredMessageBox.Show(
                this,
                UiStrings.ErrorLoudnessTargetRange,
                UiStrings.DialogSettingsTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            LoudnessTargetBox.Focus();
            LoudnessTargetBox.SelectAll();
            return;
        }

        SelectedLoudnessTargetLufs = target;
        SelectedMp3BitRate = WindowsBitRateCombo.SelectedItem is BitRateItem bitRate
            ? bitRate.Kbps
            : Mp3Encode.DefaultWindowsBitRateKbps;
        SelectedLameExePath = LamePathBox.Text.Trim();
        SelectedLameOptions = Mp3Encode.ResolveLameOptions(LameOptionsBox.Text);
        SelectedExportParallelism = ExportParallelCombo.SelectedItem is ParallelismItem parallel
            ? parallel.Value
            : AudioExport.AutoParallelism;
        DialogResult = true;
    }

    private void LameBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = UiStrings.FilterLameExe,
            Title = UiStrings.LabelLamePath,
            CheckFileExists = true,
        };
        var current = LamePathBox.Text.Trim().Trim('"');
        var dir = Path.GetDirectoryName(current);
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            dialog.InitialDirectory = dir;
        }

        if (dialog.ShowDialog(this) == true)
        {
            LamePathBox.Text = dialog.FileName;
        }
    }

    private void FillWindowsBitRates(int currentKbps)
    {
        WindowsBitRateCombo.Items.Clear();
        BitRateItem? selected = null;
        var seen = new HashSet<int>();
        foreach (var kbps in Mp3Encode.WindowsBitRates)
        {
            var item = new BitRateItem(kbps);
            WindowsBitRateCombo.Items.Add(item);
            seen.Add(kbps);
            if (kbps == currentKbps)
            {
                selected = item;
            }
        }

        if (!seen.Contains(currentKbps))
        {
            var extra = new BitRateItem(currentKbps);
            WindowsBitRateCombo.Items.Add(extra);
            selected = extra;
        }

        WindowsBitRateCombo.SelectedItem = selected ?? WindowsBitRateCombo.Items[0];
    }

    private void FillExportParallelism(int current)
    {
        var cores = Environment.ProcessorCount;
        var max = AudioExport.MaxWorkers(cores);
        var stored = current <= AudioExport.AutoParallelism
            ? AudioExport.AutoParallelism
            : Math.Clamp(current, 1, max);
        ExportParallelCombo.Items.Clear();
        ParallelismItem? selected = null;
        var auto = new ParallelismItem(
            AudioExport.AutoParallelism,
            UiStrings.LabelExportParallelAuto(AudioExport.AutoWorkers(cores)));
        ExportParallelCombo.Items.Add(auto);
        if (stored == AudioExport.AutoParallelism)
        {
            selected = auto;
        }

        for (var n = 1; n <= max; n++)
        {
            var item = new ParallelismItem(n, n.ToString(CultureInfo.InvariantCulture));
            ExportParallelCombo.Items.Add(item);
            if (n == stored)
            {
                selected = item;
            }
        }

        ExportParallelCombo.SelectedItem = selected ?? auto;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        if (_fadeCurveMenu is { IsOpen: true })
        {
            _fadeCurveMenu.IsOpen = false;
            e.Handled = true;
            return;
        }

        DialogResult = false;
        Close();
        e.Handled = true;
    }

    private void SelectLanguage(UiLanguageChoice language)
    {
        foreach (LanguageItem item in LanguageCombo.Items)
        {
            if (item.Choice == language)
            {
                LanguageCombo.SelectedItem = item;
                return;
            }
        }

        LanguageCombo.SelectedIndex = 0;
    }

    private void SelectApi(AudioOutputApi api)
    {
        foreach (ApiItem item in ApiCombo.Items)
        {
            if (item.Api == api)
            {
                ApiCombo.SelectedItem = item;
                return;
            }
        }

        ApiCombo.SelectedIndex = 0;
    }

    private void ReloadDevices(string? preferredDeviceId)
    {
        var api = ApiCombo.SelectedItem is ApiItem item ? item.Api : AudioOutputApi.WaveOut;
        DeviceCombo.Items.Clear();
        DeviceItem? selected = null;
        foreach (var device in AudioOutputFactory.EnumerateDevices(api))
        {
            var entry = new DeviceItem(device.Id, device.DisplayName);
            DeviceCombo.Items.Add(entry);
            if (preferredDeviceId is not null
                && string.Equals(device.Id, preferredDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                selected = entry;
            }
        }

        DeviceCombo.SelectedItem = selected ?? (DeviceCombo.Items.Count > 0 ? DeviceCombo.Items[0] : null);
    }

    private FadeCurveRow CreateFadeRow(string labelText, FadeShape curve, bool isFadeIn)
    {
        var rowHeight = DesignMetrics.FadeOptionRowHeight;
        var iconSide = FadeCurveIcons.WidthFor((int)Math.Round(rowHeight));

        var host = new Grid
        {
            Height = rowHeight,
            Margin = new Thickness(0, 0, 0, 2),
        };
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(iconSide) });

        var label = new TextBlock
        {
            Text = labelText,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = WpfControlHelpers.FrozenBrush(Theme.Get("PrimaryForeBrush")),
        };

        var iconHost = new Border
        {
            Width = iconSide,
            Height = rowHeight,
            Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush")),
            BorderBrush = WpfControlHelpers.FrozenBrush(Theme.Get("ChromeBorderBrush")),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
        };

        var icon = new Image
        {
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        iconHost.Child = icon;
        iconHost.MouseEnter += (_, _) =>
            iconHost.Background = WpfControlHelpers.FrozenBrush(Theme.Get("TransportHoverBackBrush"));
        iconHost.MouseLeave += (_, _) =>
            iconHost.Background = WpfControlHelpers.FrozenBrush(Theme.Get("DialogInputBackBrush"));

        host.Children.Add(label);
        host.Children.Add(iconHost);
        Grid.SetColumn(iconHost, 1);

        var row = new FadeCurveRow(host, label, icon, iconHost, curve, isFadeIn);
        RefreshFadeRowIcon(row);
        iconHost.MouseLeftButtonUp += (_, _) => ShowFadeCurvePicker(row);
        return row;
    }

    private void ShowFadeCurvePicker(FadeCurveRow row)
    {
        FadeCurveIcons.ShowPicker(
            row.IconHost,
            new Point(0, row.IconHost.ActualHeight),
            row.Curve,
            row.IsFadeIn,
            kind =>
            {
                row.Curve = kind;
                RefreshFadeRowIcon(row);
            },
            ref _fadeCurveMenu);
    }

    private static void RefreshFadeRowIcon(FadeCurveRow row)
    {
        row.Icon.Source = FadeCurveIcons.Create(row.Curve, row.IsFadeIn);
        TipService.Set(row.IconHost, UiStrings.LabelFadeCurve((int)row.Curve));
        TipService.Set(row.Label, UiStrings.LabelFadeCurve((int)row.Curve));
    }

    protected override void OnClosed(EventArgs e)
    {
        _fadeCurveMenu = null;
        base.OnClosed(e);
    }

    private sealed class FadeCurveRow(
        Grid host,
        TextBlock label,
        Image icon,
        Border iconHost,
        FadeShape curve,
        bool isFadeIn)
    {
        public Grid Host { get; } = host;

        public TextBlock Label { get; } = label;

        public Image Icon { get; } = icon;

        public Border IconHost { get; } = iconHost;

        public FadeShape Curve { get; set; } = curve;

        public bool IsFadeIn { get; } = isFadeIn;
    }

    private sealed record LanguageItem(UiLanguageChoice Choice, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ApiItem(AudioOutputApi Api, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DeviceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record BitRateItem(int Kbps)
    {
        public override string ToString() => $"{Kbps} {UiStrings.LabelKbps}";
    }

    private sealed record ParallelismItem(int Value, string Label)
    {
        public override string ToString() => Label;
    }
}
