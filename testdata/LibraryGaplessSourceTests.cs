using System.IO;
using System.Reflection;
using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryGaplessSourceTests
{
    [Fact]
    public void GaplessOpen_KeepsLoadedPcmAndPeaks()
    {
        var path = Path.Combine(Path.GetTempPath(), "mga-gapless-keep-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            WriteTone(path);
            var loaded = AudioCodec.Load(path);
            Assert.False(loaded.Peaks.IsEmpty);
            var samples = loaded.Interleaved.Length;
            var session = new DocumentSession(loaded);
            var opened = Open(session);
            opened?.Dispose();

            Assert.False(loaded.IsStreamPlayback);
            Assert.Equal(samples, loaded.Interleaved.Length);
            Assert.False(loaded.Peaks.IsEmpty);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // テスト終了後に消える。
            }
        }
    }

    private static AudioStreamSource? Open(DocumentSession session)
    {
        var method = typeof(MainWindow).GetMethod(
            "OpenLibraryGaplessSource",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (AudioStreamSource?)method!.Invoke(null, [session]);
    }

    private static void WriteTone(string path)
    {
        const int rate = 48000;
        var format = new NAudio.Wave.WaveFormat(rate, 16, 2);
        using var writer = new NAudio.Wave.WaveFileWriter(path, format);
        var frame = new float[2];
        for (var i = 0; i < rate; i++)
        {
            var sample = (float)Math.Sin(i * 2 * Math.PI * 440 / rate);
            frame[0] = sample;
            frame[1] = sample * 0.5f;
            writer.WriteSamples(frame, 0, 2);
        }
    }
}
