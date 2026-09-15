using MgaSonicAnvil.Wwise;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WwiseTrackTimingTests
{
    [Fact]
    public void Clamp_KeepsZeroToTenThousand()
    {
        Assert.Equal(500, WwiseTrackTiming.DefaultPrefetchLengthMs);
        Assert.Equal(500, WwiseTrackTiming.DefaultLookAheadTimeMs);
        Assert.Equal(50, WwiseTrackTiming.FirstSegmentLookAheadMs);
        Assert.Equal(0, WwiseTrackTiming.ClampPrefetchLengthMs(-1));
        Assert.Equal(500, WwiseTrackTiming.ClampPrefetchLengthMs(500));
        Assert.Equal(10000, WwiseTrackTiming.ClampPrefetchLengthMs(20000));
        Assert.Equal(0, WwiseTrackTiming.ClampLookAheadTimeMs(-3));
        Assert.Equal(10000, WwiseTrackTiming.ClampLookAheadTimeMs(10001));
    }

    [Fact]
    public void TryParse_AcceptsRangeAndRejectsEmpty()
    {
        Assert.True(WwiseTrackTiming.TryParsePrefetchLengthMs("500", out var prefetch));
        Assert.Equal(500, prefetch);
        Assert.True(WwiseTrackTiming.TryParsePrefetchLengthMs("0", out prefetch));
        Assert.Equal(0, prefetch);
        Assert.True(WwiseTrackTiming.TryParseLookAheadTimeMs("10000", out var lookAhead));
        Assert.Equal(10000, lookAhead);
        Assert.False(WwiseTrackTiming.TryParsePrefetchLengthMs("", out _));
        Assert.False(WwiseTrackTiming.TryParsePrefetchLengthMs("x", out _));
        Assert.False(WwiseTrackTiming.TryParseLookAheadTimeMs("-1", out _));
        Assert.False(WwiseTrackTiming.TryParseLookAheadTimeMs("10001", out _));
    }

    [Fact]
    public void WriteTrackProperties_FirstSegmentUsesPrefetchAndFixedLookAhead()
    {
        var track = new Dictionary<string, object?>();
        WwiseTrackTiming.WriteTrackProperties(track, isFirst: true, prefetchLengthMs: 800, lookAheadTimeMs: 1200);

        Assert.Equal(true, track["@IsStreamingEnabled"]);
        Assert.Equal(true, track["@IsZeroLatency"]);
        Assert.Equal(50, track["@LookAheadTime"]);
        Assert.Equal(800, track["@PreFetchLength"]);
    }

    [Fact]
    public void WriteTrackProperties_LaterSegmentsUseLookAheadOnly()
    {
        var track = new Dictionary<string, object?>();
        WwiseTrackTiming.WriteTrackProperties(track, isFirst: false, prefetchLengthMs: 800, lookAheadTimeMs: 1200);

        Assert.Equal(true, track["@IsStreamingEnabled"]);
        Assert.Equal(false, track["@IsZeroLatency"]);
        Assert.Equal(1200, track["@LookAheadTime"]);
        Assert.False(track.ContainsKey("@PreFetchLength"));
    }
}
