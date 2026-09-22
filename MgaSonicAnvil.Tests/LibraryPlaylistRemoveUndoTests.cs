using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryPlaylistRemoveUndoTests
{
    [Fact]
    public void RestoreInto_InsertsAtOriginalIndices()
    {
        var a = Session("a");
        var b = Session("b");
        var c = Session("c");
        var d = Session("d");
        var e = Session("e");
        var list = new List<DocumentSession> { a, c, e };
        LibraryPlaylistRemoveUndo.RestoreInto(list, [b, d], [1, 3]);
        Assert.Equal(new[] { a, b, c, d, e }, list);
    }

    [Fact]
    public void PushPop_RestoresInOrder()
    {
        var undo = new LibraryPlaylistRemoveUndo();
        var a = Session("a");
        var b = Session("b");
        var c = Session("c");
        var list = new List<DocumentSession> { a, c };
        undo.Push([b], [1]);
        Assert.True(undo.CanUndo);
        Assert.True(undo.TryPop(out var sessions, out var indices));
        LibraryPlaylistRemoveUndo.RestoreInto(list, sessions, indices);
        Assert.Equal(new[] { a, b, c }, list);
        Assert.False(undo.CanUndo);
    }

    [Fact]
    public void Clear_DropsHistory()
    {
        var undo = new LibraryPlaylistRemoveUndo();
        undo.Push([Session("x")], [0]);
        undo.Clear();
        Assert.False(undo.CanUndo);
        Assert.False(undo.TryPop(out _, out _));
    }

    private static DocumentSession Session(string name) =>
        new(AudioDocument.CreateDeferred(name + ".wav"));
}
