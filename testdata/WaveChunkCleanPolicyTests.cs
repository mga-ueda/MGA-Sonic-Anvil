using MgaSonicAnvil.Audio;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WaveChunkCleanPolicyTests
{
    [Theory]
    [InlineData("fmt ", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("data", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("COMM", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("SSND", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("FVER", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("cue ", WaveChunkCleanVerdict.Protected)]
    [InlineData("smpl", WaveChunkCleanVerdict.Protected)]
    [InlineData("CHAN", WaveChunkCleanVerdict.Protected)]
    [InlineData("chna", WaveChunkCleanVerdict.Protected)]
    [InlineData("bext", WaveChunkCleanVerdict.Caution)]
    [InlineData("iXML", WaveChunkCleanVerdict.Caution)]
    [InlineData("fact", WaveChunkCleanVerdict.Caution)]
    [InlineData("JUNK", WaveChunkCleanVerdict.Removable)]
    [InlineData("ID3 ", WaveChunkCleanVerdict.Removable)]
    [InlineData("SMED", WaveChunkCleanVerdict.Removable)]
    [InlineData("zzzz", WaveChunkCleanVerdict.Removable)]
    [InlineData("", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("\0\0\0\0", WaveChunkCleanVerdict.Forbidden)]
    [InlineData("    ", WaveChunkCleanVerdict.Forbidden)]
    internal void Classify_MatchesPolicy(string id, WaveChunkCleanVerdict expected) =>
        Assert.Equal(expected, WaveChunkCleanPolicy.Classify(id));

    [Fact]
    public void BlankFourCc_CannotDelete()
    {
        Assert.False(WaveChunkCleanPolicy.CanDelete("\0\0\0\0"));
        Assert.False(WaveChunkCleanPolicy.CanDelete("    "));
        Assert.Equal(WaveChunkCleanVerdict.Forbidden, WaveChunkCleanPolicy.Classify("\0\0\0\0"));
        Assert.Contains("****", WaveChunkCleanPolicy.Describe("\0\0\0\0"), StringComparison.Ordinal);
    }

    [Fact]
    public void HasDeletableNodes_GraysOutForbiddenOnly()
    {
        Assert.False(WaveChunkDeleteUi.HasDeletableNodes([Node("fmt "), Node("\0\0\0\0")]));
        Assert.True(WaveChunkDeleteUi.HasDeletableNodes([Node("fmt "), Node("JUNK")]));
        Assert.True(WaveChunkDeleteUi.HasDeletableNodes([Node("bext")]));
    }

    [Fact]
    public void FormatConfirmMessage_AppendsFactMayReturnWhenFactSelected()
    {
        var withFact = new WaveChunkDeleteUi.Plan(
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["a.wav"] = new HashSet<string>(StringComparer.Ordinal) { "fact" },
            },
            Array.Empty<string>(),
            [
                new WaveChunkDeleteUi.ChunkEntry(
                    "fact",
                    WaveChunkCleanVerdict.Caution,
                    WaveChunkCleanPolicy.Describe("fact")),
            ]);
        var message = WaveChunkDeleteUi.FormatConfirmMessage(withFact);
        Assert.Contains(UiStrings.WaveChunkDeleteFactMayReturn, message, StringComparison.Ordinal);

        var withoutFact = new WaveChunkDeleteUi.Plan(
            new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["a.wav"] = new HashSet<string>(StringComparer.Ordinal) { "bext" },
            },
            Array.Empty<string>(),
            [
                new WaveChunkDeleteUi.ChunkEntry(
                    "bext",
                    WaveChunkCleanVerdict.Caution,
                    WaveChunkCleanPolicy.Describe("bext")),
            ]);
        var other = WaveChunkDeleteUi.FormatConfirmMessage(withoutFact);
        Assert.DoesNotContain(UiStrings.WaveChunkDeleteFactMayReturn, other, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveForeBrushKey_UsesPolicyColors()
    {
        var removable = Node("JUNK");
        Assert.Equal(
            WaveChunkCleanPolicy.RemovableForeBrushKey,
            WaveChunkCleanPolicy.ResolveForeBrushKey(removable));

        var caution = Node("bext");
        Assert.Equal(
            WaveChunkCleanPolicy.CautionForeBrushKey,
            WaveChunkCleanPolicy.ResolveForeBrushKey(caution));

        var protectedNode = Node("cue ");
        Assert.Equal(
            WaveChunkCleanPolicy.ProtectedForeBrushKey,
            WaveChunkCleanPolicy.ResolveForeBrushKey(protectedNode));

        var forbidden = Node("fmt ");
        Assert.Equal(
            WaveChunkCleanPolicy.ForbiddenForeBrushKey,
            WaveChunkCleanPolicy.ResolveForeBrushKey(forbidden));
    }

    private static WaveStructureNode Node(string id) =>
        new(
            id,
            id,
            offset: 0,
            size: 0,
            WaveStructureContentKind.Empty,
            content: string.Empty,
            contentTruncated: false,
            isInvalid: false,
            error: null);
}
