using System.IO;
using NAudio.Wave;

namespace MgaSonicAnvil.Audio;

/// <summary>範囲等分の確認用クリック。Low が拍、High が小節頭。</summary>
internal static class RangeClickSample
{
    internal const string LowResourceName = "MgaSonicAnvil.Assets.Click.Low.wav";
    internal const string HighResourceName = "MgaSonicAnvil.Assets.Click.High.wav";

    public static (float[] Samples, int SampleRate) LoadLow() =>
        LoadNamed(LowResourceName, "Range click Low.wav is missing.");

    public static (float[] Samples, int SampleRate) LoadHigh() =>
        LoadNamed(HighResourceName, "Range click High.wav is missing.");

    private static (float[] Samples, int SampleRate) LoadNamed(string name, string missing)
    {
        using var stream = typeof(RangeClickSample).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(missing);
        return LoadMono(stream);
    }

    public static (float[] Samples, int SampleRate) LoadMono(Stream stream)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        copy.Position = 0;
        using var reader = new WaveFileReader(copy);
        var rate = Math.Max(1, reader.WaveFormat.SampleRate);
        var provider = reader.ToSampleProvider();
        var channels = Math.Max(1, provider.WaveFormat.Channels);
        var buffer = new float[Math.Max(channels, rate / 4)];
        var list = new List<float>();
        int read;
        while ((read = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            var frames = read / channels;
            for (var frame = 0; frame < frames; frame++)
            {
                if (channels == 1)
                {
                    list.Add(buffer[frame]);
                    continue;
                }

                ChannelMix.Downmix(buffer.AsSpan(frame * channels, channels), out var left, out var right);
                list.Add((left + right) * 0.5f);
            }
        }

        return (list.ToArray(), rate);
    }
}
