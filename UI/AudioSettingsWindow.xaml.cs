using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal partial class AudioSettingsWindow : Window
{
    public AudioOutputSettings SelectedSettings { get; private set; }

    public AudioSettingsWindow(AudioOutputSettings current)
    {
        SelectedSettings = current;
        InitializeComponent();
        DarkWindowChrome.ApplyImmersiveDarkTitleBar(this);
        Title = UiStrings.DialogSettingsTitle;

        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.WaveOut, UiStrings.LabelAudioApiWaveOut));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Wasapi, UiStrings.LabelAudioApiWasapi));
        ApiCombo.Items.Add(new ApiItem(AudioOutputApi.Asio, UiStrings.LabelAudioApiAsio));

        ActionButtonLooks.ApplyAccent(OkButton);
        ActionButtonLooks.ApplyClear(CancelButton);

        SelectApi(current.Api);
        ReloadDevices(current.DeviceId);
        Loaded += (_, _) =>
        {
            if (Owner is { Topmost: true })
            {
                Topmost = true;
            }
        };
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
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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
        var devices = AudioOutputFactory.EnumerateDevices(api);
        DeviceItem? selected = null;
        foreach (var device in devices)
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

    private sealed record ApiItem(AudioOutputApi Api, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record DeviceItem(string Id, string Label)
    {
        public override string ToString() => Label;
    }
}
