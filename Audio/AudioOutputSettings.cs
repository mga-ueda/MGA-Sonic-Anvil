namespace MgaSonicAnvil.Audio;

internal enum AudioOutputApi
{
    WaveOut,
    Wasapi,
    Asio,
}

internal readonly record struct AudioOutputSettings(AudioOutputApi Api, string DeviceId)
{
    public static AudioOutputSettings Default { get; } = new(AudioOutputApi.WaveOut, string.Empty);

    public static AudioOutputApi ParseApi(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AudioOutputApi.WaveOut;
        }

        if (text.Equals("Wasapi", StringComparison.OrdinalIgnoreCase)
            || text.Equals("WASAPI", StringComparison.OrdinalIgnoreCase))
        {
            return AudioOutputApi.Wasapi;
        }

        if (text.Equals("Asio", StringComparison.OrdinalIgnoreCase)
            || text.Equals("ASIO", StringComparison.OrdinalIgnoreCase))
        {
            return AudioOutputApi.Asio;
        }

        return AudioOutputApi.WaveOut;
    }

    public static string ToStoredValue(AudioOutputApi api) => api switch
    {
        AudioOutputApi.Wasapi => "Wasapi",
        AudioOutputApi.Asio => "Asio",
        _ => "WaveOut",
    };
}

internal readonly record struct AudioOutputDeviceInfo(string Id, string DisplayName);
