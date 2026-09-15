using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SpeakerChannelExportTests
{
    [Fact]
    public void Plan_UsesSpeakerLabelsNotFileChannelCount()
    {
        var lanes = SpeakerChannelExport.Plan(6, ChannelLayout.Parse("Stereo"), null);
        Assert.Equal(2, lanes.Length);
        Assert.Equal("L", lanes[0].Suffix);
        Assert.Equal(0, lanes[0].FileChannel);
        Assert.Equal("R", lanes[1].Suffix);
        Assert.Equal(1, lanes[1].FileChannel);
    }

    [Fact]
    public void Plan_FollowsFileChannelMap()
    {
        var lanes = SpeakerChannelExport.Plan(6, ChannelLayout.Parse("5.1"), [2, 0, 1, 3, 4, 5]);
        Assert.Equal(6, lanes.Length);
        Assert.Equal(["L", "R", "C", "LFE", "Ls", "Rs"], lanes.Select(lane => lane.Suffix).ToArray());
        Assert.Equal([2, 0, 1, 3, 4, 5], lanes.Select(lane => lane.FileChannel).ToArray());
    }

    [Fact]
    public void Plan_SkipsUnassignedSpeakers()
    {
        var lanes = SpeakerChannelExport.Plan(2, ChannelLayout.Parse("5.1"), null);
        Assert.Equal(2, lanes.Length);
        Assert.Equal("L", lanes[0].Suffix);
        Assert.Equal("R", lanes[1].Suffix);
    }

    [Fact]
    public void FileBaseName_AppendsSpeakerSuffix()
    {
        Assert.Equal("tone_L", SpeakerChannelExport.FileBaseName("tone", "L"));
        Assert.Equal("tone_LFE", SpeakerChannelExport.FileBaseName("tone", "LFE"));
    }

    [Fact]
    public void ExtractMono_TakesMappedFileLane()
    {
        var samples = new float[] { 0.10f, 0.20f, 0.80f, 0f, 0f, 0f, 0.11f, 0.21f, 0.81f, 0f, 0f, 0f };
        var document = new AudioDocument(samples, 48000, 6, 24, AudioFileKind.Wave, null);
        var center = SpeakerChannelExport.ExtractMono(document, 2);
        Assert.Equal(1, center.Channels);
        Assert.Equal(2, center.FrameCount);
        Assert.Equal(0.80f, center.Interleaved[0]);
        Assert.Equal(0.81f, center.Interleaved[1]);
        Assert.Equal(24, center.BitsPerSample);
    }
}
