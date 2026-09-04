using System.Globalization;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace MgaSonicAnvil.Audio;

internal static class AudioOutputFactory
{
    internal const int WasapiLatencyMs = 100;

    public static IReadOnlyList<AudioOutputDeviceInfo> EnumerateDevices(AudioOutputApi api) =>
        api switch
        {
            AudioOutputApi.Wasapi => EnumerateWasapiDevices(),
            AudioOutputApi.Asio => EnumerateAsioDevices(),
            _ => EnumerateWaveOutDevices(),
        };

    public static IWavePlayer Create(AudioOutputSettings settings, out string? fallbackMessage)
    {
        fallbackMessage = null;
        try
        {
            return CreateCore(settings);
        }
        catch (Exception ex) when (settings.Api != AudioOutputApi.WaveOut
            || !string.IsNullOrWhiteSpace(settings.DeviceId))
        {
            fallbackMessage =
                $"Requested {AudioOutputSettings.ToStoredValue(settings.Api)}"
                + $" device '{settings.DeviceId}' failed ({ex.Message}); falling back to WaveOut default.";
            return CreateWaveOut(deviceNumber: -1);
        }
    }

    private static IWavePlayer CreateCore(AudioOutputSettings settings) =>
        settings.Api switch
        {
            AudioOutputApi.Wasapi => CreateWasapi(settings.DeviceId),
            AudioOutputApi.Asio => CreateAsio(settings.DeviceId),
            _ => CreateWaveOut(ParseWaveOutDeviceNumber(settings.DeviceId)),
        };

    private static IWavePlayer CreateWaveOut(int deviceNumber) =>
        new WaveOutEvent { DeviceNumber = deviceNumber };

    private static IWavePlayer CreateWasapi(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        return new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: false, WasapiLatencyMs);
    }

    private static IWavePlayer CreateAsio(string? driverName)
    {
        var names = AsioDriver.GetAsioDriverNames();
        if (names.Length == 0)
        {
            throw new InvalidOperationException("No ASIO drivers are installed.");
        }

        var selected = string.IsNullOrWhiteSpace(driverName)
            ? names[0]
            : names.FirstOrDefault(n => n.Equals(driverName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"ASIO driver '{driverName}' was not found.");

        return new AsioOut(selected)
        {
            AutoStop = false,
        };
    }

    private static List<AudioOutputDeviceInfo> EnumerateWaveOutDevices()
    {
        var list = new List<AudioOutputDeviceInfo>
        {
            new("-1", "Wave Mapper (Default)"),
        };

        for (var i = 0; i < WaveOut.DeviceCount; i++)
        {
            var caps = WaveOut.GetCapabilities(i);
            list.Add(new(
                i.ToString(CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(caps.ProductName) ? $"Device {i}" : caps.ProductName));
        }

        return list;
    }

    private static List<AudioOutputDeviceInfo> EnumerateWasapiDevices()
    {
        var list = new List<AudioOutputDeviceInfo>();
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            defaultId = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia).ID;
        }
        catch
        {
            // 既定デバイスが取れなくても列挙は続行する。
        }

        list.Add(new(string.Empty, "Default"));
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
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

    private static List<AudioOutputDeviceInfo> EnumerateAsioDevices()
    {
        try
        {
            return AsioDriver.GetAsioDriverNames()
                .Select(name => new AudioOutputDeviceInfo(name, name))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static int ParseWaveOutDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return -1;
        }

        return int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : -1;
    }
}
