using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class VstBakeTailTests
{
    [Fact]
    public void BakeVstInserts_LongerAfter_ExtendsDocument()
    {
        var before = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var document = new AudioDocument(before, 48000, 1, 24, AudioFileKind.Wave, null);
        var after = new float[] { 0.5f, 0.6f, 0.7f, 0.8f, 0.25f, 0.125f };

        var bypassed = false;
        var command = ProcessEdits.BakeVstInserts(
            document,
            after,
            () => bypassed = true,
            () => bypassed = false);
        Assert.NotNull(command);

        command!.Apply(document);
        Assert.True(bypassed);
        Assert.Equal(6, document.FrameCount);
        Assert.Equal(after, document.Interleaved);

        command.Revert(document);
        Assert.False(bypassed);
        Assert.Equal(4, document.FrameCount);
        Assert.Equal(before, document.Interleaved);
    }

    [Fact]
    public void BakeVstInserts_SameLength_ReplacesInPlace()
    {
        var before = new float[] { 0.1f, 0.2f, 0.3f, 0.4f };
        var document = new AudioDocument((float[])before.Clone(), 48000, 1, 24, AudioFileKind.Wave, null);
        var after = new float[] { 0.9f, 0.8f, 0.7f, 0.6f };

        var command = ProcessEdits.BakeVstInserts(
            document,
            after,
            static () => { },
            static () => { });
        Assert.NotNull(command);
        command!.Apply(document);
        Assert.Equal(4, document.FrameCount);
        Assert.Equal(after, document.Interleaved);
    }
}
