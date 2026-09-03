using MgaSonicAnvil.Audio;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.Editing;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class MarkerTests
{
    [Fact]
    public void Add_AssignsIdsInStartOrder()
    {
        var document = MakeDocument(frames: 1000);
        Assert.True(document.TryAddMarker(800));
        Assert.True(document.TryAddMarker(100));
        Assert.True(document.TryAddMarker(400));

        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 100, 400, 800 }, Frames(document));
    }

    [Fact]
    public void AddInMiddle_RemapsIdsToStartOrder()
    {
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(100);
        document.TryAddMarker(300);
        document.TryAddMarker(200);

        Assert.Equal(1, document.Markers[0].Id);
        Assert.Equal(100, document.Markers[0].Frame);
        Assert.Equal(2, document.Markers[1].Id);
        Assert.Equal(200, document.Markers[1].Frame);
        Assert.Equal(3, document.Markers[2].Id);
        Assert.Equal(300, document.Markers[2].Frame);
    }

    [Fact]
    public void AddDuplicate_IsRejected()
    {
        var document = MakeDocument(frames: 100);
        Assert.True(document.TryAddMarker(10));
        Assert.False(document.TryAddMarker(10));
        Assert.Single(document.Markers);
        Assert.Equal(1, document.Markers[0].Id);
    }

    [Fact]
    public void DeleteRange_RemovesAndShiftsThenRenumbers()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(10);
        document.TryAddMarker(25);
        document.TryAddMarker(80);
        document.Selection = new WaveSelection(20, 40);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Delete(document, document.Selection));

        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 10, 60 }, Frames(document));
        Assert.Equal(1, document.Markers[0].Id);
        Assert.Equal(2, document.Markers[1].Id);
    }

    [Fact]
    public void UndoAdd_RestoresPreviousIds()
    {
        var document = MakeDocument(frames: 100);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.AddMarker(document, 70));
        history.Do(document, ProcessEdits.AddMarker(document, 20));
        Assert.Equal(new[] { 1, 2 }, document.Markers.Select(m => m.Id).ToArray());
        Assert.True(history.Undo(document));
        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 70 }, Frames(document));
        Assert.Equal(1, document.Markers[0].Id);
    }

    [Fact]
    public void UndoDelete_RestoresMarkerIds()
    {
        var document = MakeDocument(frames: 80);
        document.TryAddMarker(10);
        document.TryAddMarker(30);
        document.TryAddMarker(50);
        document.Selection = new WaveSelection(20, 40);
        var history = new EditHistory();
        history.Do(document, ProcessEdits.Delete(document, document.Selection));
        Assert.True(history.Undo(document));
        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 10, 30, 50 }, Frames(document));
        Assert.Equal(new[] { 1, 2, 3 }, document.Markers.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void AdjacentMarker_JumpsPreviousNextOrDocumentEdge()
    {
        var document = MakeDocument(frames: 100);
        Assert.Equal(0, document.AdjacentMarkerFrame(40, -1));
        Assert.Equal(100, document.AdjacentMarkerFrame(40, 1));

        document.TryAddMarker(20);
        document.TryAddMarker(60);
        Assert.Equal(20, document.AdjacentMarkerFrame(40, -1));
        Assert.Equal(60, document.AdjacentMarkerFrame(40, 1));
        Assert.Equal(0, document.AdjacentMarkerFrame(20, -1));
        Assert.Equal(60, document.AdjacentMarkerFrame(20, 1));
        Assert.Equal(20, document.AdjacentMarkerFrame(60, -1));
        Assert.Equal(100, document.AdjacentMarkerFrame(60, 1));
    }

    [Fact]
    public void TryGetRoleSpan_PrefersContainingThenNextThenFirst()
    {
        var document = MakeDocument(frames: 48000 * 10);
        document.TryAddMarker(1000);
        document.TrySetMarkerComment(1000, "-A");
        document.TryAddMarker(5000);
        document.TrySetMarkerComment(5000, "-L");
        document.TryAddMarker(20000);
        document.TryAddMarker(30000);
        document.TrySetMarkerComment(30000, "-L");
        document.TryAddMarker(40000);

        Assert.True(document.TryGetRoleSpan(MarkerRole.Loop, 8000, out var inside));
        Assert.Equal(new WaveSelection(5000, 20000), inside);
        Assert.True(document.TryGetRoleSpan(MarkerRole.Loop, 25000, out var next));
        Assert.Equal(new WaveSelection(30000, 40000), next);
        Assert.True(document.TryGetRoleSpan(MarkerRole.Loop, 45000, out var wrap));
        Assert.Equal(new WaveSelection(5000, 20000), wrap);
        Assert.True(document.TryGetRoleSpan(MarkerRole.Loop, 0, out var fromHead));
        Assert.Equal(new WaveSelection(5000, 20000), fromHead);
    }

    [Fact]
    public void TryGetRoleSpan_ReturnsFalseWithoutLoopMarker()
    {
        var document = MakeDocument(frames: 1000);
        document.TryAddMarker(100);
        document.TrySetMarkerComment(100, "-A");
        Assert.False(document.TryGetRoleSpan(MarkerRole.Loop, 50, out var range));
        Assert.True(range.IsEmpty);
    }

    [Fact]
    public void MarkerSpanAt_SelectsBetweenMarkersOrEdges()
    {
        var document = MakeDocument(frames: 100);
        Assert.Equal(new WaveSelection(0, 100), document.MarkerSpanAt(40));

        document.TryAddMarker(20);
        document.TryAddMarker(60);
        Assert.Equal(new WaveSelection(0, 20), document.MarkerSpanAt(10));
        Assert.Equal(new WaveSelection(20, 60), document.MarkerSpanAt(20));
        Assert.Equal(new WaveSelection(20, 60), document.MarkerSpanAt(40));
        Assert.Equal(new WaveSelection(60, 100), document.MarkerSpanAt(60));
        Assert.Equal(new WaveSelection(60, 100), document.MarkerSpanAt(80));
    }

    [Fact]
    public void MarkerComment_StaysWithFrameAfterRenumber()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(80);
        document.TryAddMarker(20);
        Assert.True(document.TrySetMarkerComment(80, "tail"));
        Assert.Equal("tail", document.Markers[1].Comment);
        Assert.Equal(2, document.Markers[1].Id);

        document.TryAddMarker(10);
        Assert.Equal(3, document.Markers[2].Id);
        Assert.Equal(80, document.Markers[2].Frame);
        Assert.Equal("tail", document.Markers[2].Comment);
    }

    [Theory]
    [InlineData("-A", "Anacrusis")]
    [InlineData("-a", "Anacrusis")]
    [InlineData("ーL", "Loop")]
    [InlineData("loop -L", "Loop")]
    [InlineData("-E", "Exit")]
    [InlineData("-e tail", "Exit")]
    [InlineData("-R", "Remove")]
    [InlineData("remove -r", "Remove")]
    [InlineData("", "None")]
    [InlineData("AREA", "None")]
    [InlineData("-ALL", "None")]
    public void MarkerRoles_ParseReservedWordsIgnoreCase(string comment, string expected)
    {
        Assert.Equal(Enum.Parse<MarkerRole>(expected), MarkerRoles.FromComment(comment));
    }

    [Theory]
    [InlineData("-a", "-A")]
    [InlineData("-l", "-L")]
    [InlineData("-e tail", "-E tail")]
    [InlineData("loop -r", "loop -R")]
    [InlineData("ーl", "ーL")]
    [InlineData("-a foo -l", "-A foo -L")]
    [InlineData("-A", "-A")]
    [InlineData("-ALL", "-ALL")]
    [InlineData("AREA", "AREA")]
    [InlineData("  -e  ", "-E")]
    public void MarkerRoles_NormalizeUppercasesReservedTags(string comment, string expected)
    {
        Assert.Equal(expected, MarkerRoles.Normalize(comment));
    }

    [Fact]
    public void TrySetMarkerComment_UppercasesReservedTag()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(20);
        Assert.True(document.TrySetMarkerComment(20, "-a pickup"));
        Assert.Equal("-A pickup", document.Markers[0].Comment);
    }

    [Fact]
    public void ResolveGroupDelta_StopsBeforeOccupiedAndKeepsGap()
    {
        var moving = new long[] { 40, 60 };
        var occupied = new HashSet<long> { 10, 80 };
        Assert.Equal(19, MarkerMoves.ResolveGroupDelta(moving, 20, occupied, 200));
        Assert.Equal(30, MarkerMoves.ResolveGroupDelta(moving, 30, occupied, 200));
        Assert.Equal(-29, MarkerMoves.ResolveGroupDelta(moving, -30, occupied, 200));
    }

    [Fact]
    public void ResolveGroupDelta_ClampsToDocument()
    {
        var moving = new long[] { 10, 25 };
        Assert.Equal(-10, MarkerMoves.ResolveGroupDelta(moving, -40, [], 100));
        Assert.Equal(75, MarkerMoves.ResolveGroupDelta(moving, 200, [], 100));
    }

    [Fact]
    public void TryMoveMarkers_MovesCommentAndRenumbers()
    {
        var document = MakeDocument(frames: 200);
        document.TryAddMarker(20);
        document.TryAddMarker(80);
        document.TrySetMarkerComment(80, "tail");

        Assert.True(document.TryMoveMarkers([80], 10, out var applied));
        Assert.Equal(10, applied);
        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 20, 90 }, Frames(document));
        Assert.Equal("tail", document.Markers[1].Comment);
        Assert.Equal(2, document.Markers[1].Id);
    }

    [Fact]
    public void TryMoveMarkers_PairKeepsRelativeGap()
    {
        var document = MakeDocument(frames: 200);
        document.TryAddMarker(30);
        document.TryAddMarker(50);
        document.TryAddMarker(90);

        Assert.True(document.TryMoveMarkers([50, 90], -10, out var applied));
        Assert.Equal(-10, applied);
        Assert.Equal(new long[] { 30, 40, 80 }, Frames(document));
    }

    [Fact]
    public void TryMoveMarkers_DoesNotLandOnAnotherMarker()
    {
        var document = MakeDocument(frames: 200);
        document.TryAddMarker(10);
        document.TryAddMarker(40);
        Assert.True(document.TryMoveMarkers([10], 30, out var applied));
        Assert.Equal(29, applied);
        Assert.Equal(new long[] { 39, 40 }, Frames(document));
    }

    [Fact]
    public void TryRemoveMarkers_RenumbersRemaining()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(10);
        document.TryAddMarker(30);
        document.TryAddMarker(50);
        document.TrySetMarkerComment(50, "keep");

        Assert.True(document.TryRemoveMarkers([10, 30]));
        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 50 }, Frames(document));
        Assert.Equal(1, document.Markers[0].Id);
        Assert.Equal("keep", document.Markers[0].Comment);
    }

    [Fact]
    public void UndoMoveMarkers_RestoresFramesAndComments()
    {
        var document = MakeDocument(frames: 200);
        document.TryAddMarker(20);
        document.TryAddMarker(80);
        document.TrySetMarkerComment(20, "head");
        var history = new EditHistory();
        var command = ProcessEdits.MoveMarkers(document, [20, 80], 15, out var applied);
        Assert.NotNull(command);
        Assert.Equal(15, applied);
        history.Do(document, command);
        Assert.Equal(new long[] { 35, 95 }, Frames(document));
        Assert.Equal("head", document.Markers[0].Comment);

        Assert.True(history.Undo(document));
        Assert.Equal(new long[] { 20, 80 }, Frames(document));
        Assert.Equal("head", document.Markers[0].Comment);
        Assert.Equal(string.Empty, document.Markers[1].Comment);
    }

    [Fact]
    public void UndoRemoveMarkers_RestoresIds()
    {
        var document = MakeDocument(frames: 100);
        document.TryAddMarker(10);
        document.TryAddMarker(30);
        document.TryAddMarker(50);
        var history = new EditHistory();
        var command = ProcessEdits.RemoveMarkers(document, [10, 50]);
        Assert.NotNull(command);
        history.Do(document, command);
        Assert.Equal(new long[] { 30 }, Frames(document));
        Assert.Equal(1, document.Markers[0].Id);

        Assert.True(history.Undo(document));
        AssertIdsMatchOrder(document);
        Assert.Equal(new long[] { 10, 30, 50 }, Frames(document));
    }

    private static void AssertIdsMatchOrder(AudioDocument document)
    {
        for (var i = 0; i < document.Markers.Count; i++)
        {
            Assert.Equal(i + 1, document.Markers[i].Id);
        }
    }

    private static long[] Frames(AudioDocument document) =>
        document.Markers.Select(marker => marker.Frame).ToArray();

    private static AudioDocument MakeDocument(int frames)
    {
        return new AudioDocument(new float[frames * 2], 48000, 2, 24, AudioFileKind.Wave, null);
    }
}
