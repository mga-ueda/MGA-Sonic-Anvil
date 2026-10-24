using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class EffeTuneBpmEstimatorTests
{
    [Fact]
    public void Create_Prepare48k_DoesNotThrow()
    {
        using var estimator = new EffeTuneBpmEstimator();
        estimator.Process(new float[480], 48000);
        Assert.Null(estimator.Bpm);
    }

    [Fact]
    public void Process_ClickTrain_EventuallyReportsFiniteBpmOrStaysNull()
    {
        using var estimator = new EffeTuneBpmEstimator();
        const int rate = 48000;
        const double bpm = 120;
        var period = (int)Math.Round(60.0 / bpm * rate);
        // EffeTune は数秒のロック待ちがある。十分なクリック列を流す。
        var total = rate * 6;
        var chunk = new float[512];
        for (var written = 0; written < total; written += chunk.Length)
        {
            Array.Clear(chunk);
            for (var i = 0; i < chunk.Length && written + i < total; i++)
            {
                var t = written + i;
                if (t % period < 48)
                {
                    chunk[i] = (t % period) < 6 ? 0.85f : -0.15f;
                }
            }

            estimator.Process(chunk, rate);
        }

        if (estimator.Bpm is { } value)
        {
            Assert.InRange(value, 55f, 250f);
        }
    }
}
