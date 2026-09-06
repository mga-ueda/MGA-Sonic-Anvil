using System.Globalization;
using System.Reflection;
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

    public static int QueryCurrentSampleRate(AudioOutputSettings settings)
    {
        try
        {
            return settings.Api switch
            {
                AudioOutputApi.Asio => QueryAsioSampleRate(settings.DeviceId),
                AudioOutputApi.Wasapi => QueryWasapiMixRate(settings.DeviceId),
                _ => QueryWasapiMixRate(deviceId: null),
            };
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Init 前の ASIO ドライバが報告している現在クロック。NAudio 2.2 の AsioOut に公開プロパティはない。
    /// </summary>
    public static int ReadLiveSampleRate(IWavePlayer output)
    {
        if (output is AsioOut asio)
        {
            return ReadAsioDriverRate(asio);
        }

        return 0;
    }

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

    private static int QueryWasapiMixRate(string? deviceId)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = string.IsNullOrWhiteSpace(deviceId)
            ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)
            : enumerator.GetDevice(deviceId);
        var rate = device.AudioClient.MixFormat.SampleRate;
        return rate >= 1000 ? rate : 0;
    }

    private static int QueryAsioSampleRate(string? driverName)
    {
        // ASIO は排他オープンなので、再生用 AsioOut の外からドライバを開いて聞かない。
        _ = driverName;
        return 0;
    }

    private static int ReadAsioDriverRate(AsioOut asio)
    {
        try
        {
            if (GetAsioDriverExt(asio) is not { } ext)
            {
                return 0;
            }

            var live = NormalizeSampleRate(ext.Driver.GetSampleRate());
            if (live >= 1000)
            {
                return live;
            }

            return NormalizeSampleRate(ext.Capabilities.SampleRate);
        }
        catch
        {
            return 0;
        }
    }

    private static AsioDriverExt? GetAsioDriverExt(AsioOut asio)
    {
        var field = typeof(AsioOut).GetField("driver", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(asio) is AsioDriverExt named)
        {
            return named;
        }

        return typeof(AsioOut)
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .FirstOrDefault(candidate => typeof(AsioDriverExt).IsAssignableFrom(candidate.FieldType))
            ?.GetValue(asio) as AsioDriverExt;
    }

    private static int NormalizeSampleRate(double sampleRate)
    {
        var rate = (int)Math.Round(sampleRate);
        return rate >= 1000 ? rate : 0;
    }

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
