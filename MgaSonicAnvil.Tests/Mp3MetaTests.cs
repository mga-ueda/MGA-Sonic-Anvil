using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class Mp3MetaTests
{
    [Fact]
    public void Mp3_RejectsRegionAndSampleLoop_AllowsMarker()
    {
        var document = new AudioDocument(new float[480], 48000, 1, 16, AudioFileKind.Mp3, "a.mp3");
        Assert.False(document.AllowsRegionsAndLoops);
        Assert.Null(ProcessEdits.SetRegion(document, new WaveSelection(10, 80)));
        Assert.Null(ProcessEdits.SetSampleLoop(document, new WaveSelection(10, 80)));
        document.SetRegion(new WaveSelection(10, 80));
        document.SetSampleLoop(new WaveSelection(10, 80));
        Assert.True(document.Region.IsEmpty);
        Assert.True(document.SampleLoop.IsEmpty);

        Assert.True(document.TryAddMarker(24));
        Assert.Single(document.Markers);
        Assert.Equal(24, document.Markers[0].Frame);
    }
}
