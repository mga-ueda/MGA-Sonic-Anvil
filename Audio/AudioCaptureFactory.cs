using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace MgaSonicAnvil.Audio;

internal static class AudioCaptureFactory
{
    public static IReadOnlyList<AudioOutputDeviceInfo> EnumerateDevices(AudioOutputApi api) =>
        api switch
        {
            AudioOutputApi.Wasapi => EnumerateWasapiCapture(),
            AudioOutputApi.Asio => AudioOutputFactory.EnumerateDevices(AudioOutputApi.Asio),
            _ => EnumerateWaveIn(),
        };

    public static string ResolveRecordDeviceId(AudioOutputApi api, string? playbackDeviceId)
    {
        if (api == AudioOutputApi.Asio)
        {
            return playbackDeviceId ?? string.Empty;
        }

        string? playbackName = null;
        foreach (var device in AudioOutputFactory.EnumerateDevices(api))
        {
            if (string.Equals(device.Id, playbackDeviceId, StringComparison.OrdinalIgnoreCase))
            {
                playbackName = device.DisplayName;
                break;
            }
        }

        return MatchRecordDeviceId(playbackName, EnumerateDevices(api));
    }

    internal static string MatchRecordDeviceId(
        string? playbackName,
        IReadOnlyList<AudioOutputDeviceInfo> captures)
    {
        if (captures.Count == 0)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(playbackName))
        {
            foreach (var device in captures)
            {
                if (string.Equals(device.DisplayName, playbackName, StringComparison.OrdinalIgnoreCase))
                {
                    return device.Id;
                }
            }

            var key = HardwareKey(playbackName);
            if (key.Length > 0)
            {
                foreach (var device in captures)
                {
                    if (string.Equals(HardwareKey(device.DisplayName), key, StringComparison.OrdinalIgnoreCase))
                    {
                        return device.Id;
                    }
                }
            }
        }

        return captures[0].Id;
    }

    internal static string HardwareKey(string name)
    {
        const string defaultSuffix = " (Default)";
        if (name.EndsWith(defaultSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^defaultSuffix.Length];
        }

        var open = name.IndexOf('(');
        var close = name.LastIndexOf(')');
        return open >= 0 && close > open
            ? name[(open + 1)..close].Trim()
            : name.Trim();
    }

    public static int QueryInputChannelCount(AudioOutputApi api, string? deviceId)
    {
        try
        {
            return api switch
            {
                AudioOutputApi.Wasapi => QueryWasapiCaptureChannels(deviceId),
                AudioOutputApi.Asio => 0,
                _ => QueryWaveInChannels(deviceId),
            };
        }
        catch
        {
            return 0;
        }
    }

    public static int QueryOutputChannelCount(AudioOutputSettings settings)
    {
        try
        {
            return settings.Api switch
            {
                AudioOutputApi.Wasapi => QueryWasapiRenderChannels(settings.DeviceId),
                AudioOutputApi.Asio => 0,
                _ => QueryWaveOutChannels(settings.DeviceId),
            };
        }
        catch
        {
            return 2;
        }
    }

    public static string[] PortNames(int count, bool input) =>
        DevicePortNames.Numbered(count, input);

    public static string[] QueryPortNames(AudioOutputApi api, string? deviceId, bool input)
    {
        try
        {
            return api switch
            {
                AudioOutputApi.Wasapi => QueryWasapiPortNames(deviceId, input),
                AudioOutputApi.Asio => QueryAsioPortNames(deviceId, input),
                _ => QueryWavePortNames(deviceId, input),
            };
        }
        catch
        {
            return [];
        }
    }

    public static string[] QueryPortNames(AudioOutputSettings settings, bool input) =>
        QueryPortNames(settings.Api, settings.DeviceId, input);

    private static List<AudioOutputDeviceInfo> EnumerateWaveIn()
    {
        var list = new List<AudioOutputDeviceInfo>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var caps = WaveIn.GetCapabilities(i);
            list.Add(new(
                i.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(caps.ProductName) ? $"Input {i}" : caps.ProductName));
        }

        return list;
    }

    private static List<AudioOutputDeviceInfo> EnumerateWasapiCapture()
    {
        var list = new List<AudioOutputDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia).ID;
        }
        catch
        {
            // 既定が取れなくても列挙は続ける。
        }

        list.Add(new(string.Empty, "Default"));
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            using (device)
            {
                var name = device.FriendlyName;
                if (!string.IsNullOrEmpty(defaultId)
                    && string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase))
                {
                    name += " (Default)";
                }

                list.Add(new(device.ID, name));
            }
        }

        return list;
    }

    private static int QueryWaveInChannels(string? deviceId)
    {
        var index = ParseIndex(deviceId);
        if (index < 0 || index >= WaveIn.DeviceCount)
        {
            return WaveIn.DeviceCount > 0 ? WaveIn.GetCapabilities(0).Channels : 0;
        }

        return WaveIn.GetCapabilities(index).Channels;
    }

    private static int QueryWaveOutChannels(string? deviceId)
    {
        var index = ParseIndex(deviceId);
        if (index < 0)
        {
            return 2;
        }

        return index < WaveOut.DeviceCount ? WaveOut.GetCapabilities(index).Channels : 2;
    }

    private static int QueryWasapiCaptureChannels(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        return Math.Max(0, device.AudioClient.MixFormat.Channels);
    }

    private static int QueryWasapiRenderChannels(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        return Math.Max(1, device.AudioClient.MixFormat.Channels);
    }

    private static string[] QueryWasapiPortNames(string? deviceId, bool input)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(input ? DataFlow.Capture : DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        return DevicePortNames.FromWaveFormat(device.AudioClient.MixFormat);
    }

    private static string[] QueryAsioPortNames(string? driverName, bool input)
    {
        var names = AsioDriver.GetAsioDriverNames();
        if (names.Length == 0)
        {
            return [];
        }

        var selected = string.IsNullOrWhiteSpace(driverName)
            ? names[0]
            : names.FirstOrDefault(name => name.Equals(driverName, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            return [];
        }

        using var asio = new AsioOut(selected) { AutoStop = false };
        return DevicePortNames.FromAsio(asio, input);
    }

    private static string[] QueryWavePortNames(string? deviceId, bool input)
    {
        var count = input ? QueryWaveInChannels(deviceId) : QueryWaveOutChannels(deviceId);
        return count < 1 ? [] : DevicePortNames.FromChannelCount(count);
    }

    internal static int ParseIndex(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return 0;
        }

        return int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : 0;
    }
}
