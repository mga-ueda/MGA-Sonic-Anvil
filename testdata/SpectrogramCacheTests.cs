using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpectrogramCacheTests
{
    [Fact]
    public void Ensure_WritesTempCacheAndServesColors()
    {
        var samples = new float[SpectrogramEngine.Hop * 4];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 48000);
        }

        var document = new AudioDocument(samples, 48000, 1, 16, AudioFileKind.Wave, null);
        using var cache = new SpectrogramCache();
        using var done = new ManualResetEventSlim(false);
        cache.Ensure(document, () =>
        {
            if (cache.IsReady)
            {
                done.Set();
            }
        });

        Assert.True(done.Wait(TimeSpan.FromSeconds(10)));
        Assert.True(cache.TryColor(0, 10, out var color));
        Assert.NotEqual(0, color);
        Assert.True(cache.TryColor(SpectrogramEngine.Hop / 2, 10, out var mid));
        Assert.NotEqual(0, mid);
        Assert.True(cache.TryColor(0, 10, 24f, out var boosted));
        static int Luma(int bgra) =>
            (bgra & 0xFF) + ((bgra >> 8) & 0xFF) + ((bgra >> 16) & 0xFF);
        Assert.True(Luma(boosted) >= Luma(color));
    }
}
