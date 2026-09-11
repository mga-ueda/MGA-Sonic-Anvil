using NAudio.CoreAudioApi;

namespace MgaSonicAnvil.Audio;

/// <summary>
/// waveOutGetDevCaps / waveInGetDevCaps の名前は 31 文字で切れる。
/// WASAPI の FriendlyName で足りない分を補う。
/// </summary>
internal static class WaveDeviceNames
{
    public const int CapsNameLimit = 31;

    public static string Resolve(string? productName, IReadOnlyList<string> friendlyNames)
    {
        var available = new List<string>(friendlyNames.Count);
        foreach (var name in friendlyNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                available.Add(name.Trim());
            }
        }

        return Take(productName, available);
    }

    public static string[] ResolveAll(IReadOnlyList<string> productNames, IReadOnlyList<string> friendlyNames)
    {
        var available = new List<string>(friendlyNames.Count);
        foreach (var name in friendlyNames)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                available.Add(name.Trim());
            }
        }

        var result = new string[productNames.Count];
        for (var i = 0; i < productNames.Count; i++)
        {
            result[i] = Take(productNames[i], available);
        }

        return result;
    }

    public static List<string> QueryWasapiFriendlyNames(DataFlow flow)
    {
        var names = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    if (!string.IsNullOrWhiteSpace(device.FriendlyName))
                    {
                        names.Add(device.FriendlyName.Trim());
                    }
                }
            }
        }
        catch
        {
            return names;
        }

        return names;
    }

    private static string Take(string? productName, List<string> available)
    {
        var shortName = (productName ?? string.Empty).Trim();
        if (shortName.Length == 0)
        {
            return shortName;
        }

        for (var i = 0; i < available.Count; i++)
        {
            if (available[i].Equals(shortName, StringComparison.OrdinalIgnoreCase))
            {
                return TakeAt(available, i);
            }
        }

        if (LooksComplete(shortName))
        {
            return shortName;
        }

        for (var i = 0; i < available.Count; i++)
        {
            if (available[i].StartsWith(shortName, StringComparison.OrdinalIgnoreCase))
            {
                return TakeAt(available, i);
            }
        }

        // waveOut は空白を落とすことがある（HighDefin / High Definition）。
        var compact = Compact(shortName);
        for (var i = 0; i < available.Count; i++)
        {
            if (Compact(available[i]).StartsWith(compact, StringComparison.OrdinalIgnoreCase))
            {
                return TakeAt(available, i);
            }
        }

        var key = HardwareKey(shortName);
        var compactKey = Compact(key);
        if (compactKey.Length >= 8)
        {
            for (var i = 0; i < available.Count; i++)
            {
                var inner = Compact(HardwareKey(available[i]));
                if (inner.StartsWith(compactKey, StringComparison.OrdinalIgnoreCase))
                {
                    return TakeAt(available, i);
                }
            }
        }

        return shortName;
    }

    private static string TakeAt(List<string> available, int index)
    {
        var name = available[index];
        available.RemoveAt(index);
        return name;
    }

    private static bool LooksComplete(string name)
    {
        if (name.Length >= CapsNameLimit)
        {
            return false;
        }

        var open = name.IndexOf('(');
        return open < 0 || name.LastIndexOf(')') > open;
    }

    private static string HardwareKey(string name)
    {
        var open = name.IndexOf('(');
        if (open < 0)
        {
            return name.Trim();
        }

        var close = name.LastIndexOf(')');
        var inner = close > open ? name[(open + 1)..close] : name[(open + 1)..];
        return inner.Trim();
    }

    private static string Compact(string name)
    {
        var buffer = new char[name.Length];
        var n = 0;
        foreach (var c in name)
        {
            if (!char.IsWhiteSpace(c))
            {
                buffer[n++] = c;
            }
        }

        return n == name.Length ? name : new string(buffer, 0, n);
    }
}
