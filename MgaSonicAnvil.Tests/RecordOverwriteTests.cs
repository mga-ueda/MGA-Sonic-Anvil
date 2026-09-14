using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class RecordOverwriteTests
{
    [Fact]
    public void Overwrite_ReplacesFromPlayheadAndKeepsTail()
    {
        var document = new AudioDocument(
            [1f, 1f, 2f, 2f, 3f, 3f, 4f, 4f],
            48000,
            2,
            24,
            AudioFileKind.Wave,
            null);
        var command = ProcessEdits.RecordOverwrite(document, 1, [9f, 9f, 8f, 8f]);
        Assert.NotNull(command);
        command!.Apply(document);

        Assert.Equal(4, document.FrameCount);
        Assert.Equal([1f, 1f, 9f, 9f, 8f, 8f, 4f, 4f], document.Interleaved);
        command.Revert(document);
        Assert.Equal([1f, 1f, 2f, 2f, 3f, 3f, 4f, 4f], document.Interleaved);
    }

    [Fact]
    public void Overwrite_ExtendsWhenTakeIsLonger()
    {
        var document = new AudioDocument(
            [1f, 1f, 2f, 2f],
            48000,
            2,
            24,
            AudioFileKind.Wave,
            null);
        var command = ProcessEdits.RecordOverwrite(document, 1, [7f, 7f, 8f, 8f, 9f, 9f]);
        Assert.NotNull(command);
        command!.Apply(document);

        Assert.Equal(4, document.FrameCount);
        Assert.Equal([1f, 1f, 7f, 7f, 8f, 8f, 9f, 9f], document.Interleaved);
        command.Revert(document);
        Assert.Equal(2, document.FrameCount);
        Assert.Equal([1f, 1f, 2f, 2f], document.Interleaved);
    }

    [Fact]
    public void WriteLiveFrom_KeepsPrefixAndReplacesTail()
    {
        var document = new AudioDocument(
            [1f, 1f, 2f, 2f, 3f, 3f],
            48000,
            2,
            24,
            AudioFileKind.Wave,
            null);
        document.WriteLiveFrom(1, [8f, 8f, 9f, 9f]);
        Assert.Equal(3, document.FrameCount);
        Assert.Equal([1f, 1f, 8f, 8f, 9f, 9f], document.Interleaved[..document.SampleCount]);
        document.CommitLiveSamples(rebuildPeaks: true);
        Assert.Equal(3, document.FrameCount);
    }

    [Fact]
    public void History_AcceptApplied_UndoRestores()
    {
        var document = new AudioDocument(
            [1f, 1f, 2f, 2f, 3f, 3f],
            48000,
            2,
            24,
            AudioFileKind.Wave,
            null);
        var before = document.CopyRange(1, 2);
        var take = new[] { 8f, 8f };
        var after = ProcessEdits.CombineTakeAndLeftover(take, before, 2);
        document.WriteLiveFrom(1, after);
        document.CommitLiveSamples(rebuildPeaks: true);
        var command = ProcessEdits.RecordOverwrite(document, 1, before, take);
        Assert.NotNull(command);
        var history = new EditHistory();
        history.AcceptApplied(document, command!);
        Assert.True(history.CanUndo);
        Assert.True(history.Undo(document));
        Assert.Equal([1f, 1f, 2f, 2f, 3f, 3f], document.Interleaved);
    }
}
