using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstOfflineStartupGapTests
{
    [Fact]
    public void ExtraFrames_IsOutputOnsetMinusSourceOnset()
    {
        Assert.Equal(12, VstOfflineStartupGap.ExtraFrames(sourceOnset: 4, outputOnset: 16, maxExtra: 100));
        Assert.Equal(0, VstOfflineStartupGap.ExtraFrames(sourceOnset: 4, outputOnset: 4, maxExtra: 100));
        Assert.Equal(0, VstOfflineStartupGap.ExtraFrames(sourceOnset: 8, outputOnset: 3, maxExtra: 100));
    }

    [Fact]
    public void ExtraFrames_IgnoresGapsBeyondCap()
    {
        Assert.Equal(0, VstOfflineStartupGap.ExtraFrames(sourceOnset: 0, outputOnset: 50, maxExtra: 49));
        Assert.Equal(0, VstOfflineStartupGap.ExtraFrames(sourceOnset: -1, outputOnset: 4, maxExtra: 10));
    }

    [Fact]
    public void FindOnset_SkipsLeadingSilence()
    {
        var samples = new float[] { 0f, 0f, 0f, 0.02f };
        var threshold = SilentSkip.LinearFromDb(VstInsertEngine.OfflineTailThresholdDb);
        Assert.Equal(3, VstOfflineStartupGap.FindOnset(samples, 0, 4, 1, threshold));
        Assert.Equal(-1, VstOfflineStartupGap.FindOnset(new float[4], 0, 4, 1, threshold));
    }
}
