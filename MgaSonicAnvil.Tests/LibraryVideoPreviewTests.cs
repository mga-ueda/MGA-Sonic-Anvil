using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class LibraryVideoPreviewTests
{
    [Fact]
    public void CandidatePositions_StartsAtZeroAndStaysInsideDuration()
    {
        var list = LibraryVideoPreview.CandidatePositions(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.Zero, list[0]);
        Assert.Contains(TimeSpan.FromSeconds(1), list);
        Assert.All(list, t => Assert.True(t >= TimeSpan.Zero && t < TimeSpan.FromSeconds(20)));
    }

    [Fact]
    public void FrameLooksVisible_RejectsNearBlack()
    {
        var pixels = new byte[16 * 4];
        Assert.False(LibraryVideoPreview.FrameLooksVisible(pixels, 16));
    }

    [Fact]
    public void FrameLooksVisible_AcceptsBrightFrame()
    {
        var pixels = new byte[16 * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 40;
            pixels[i + 1] = 40;
            pixels[i + 2] = 40;
            pixels[i + 3] = 255;
        }

        Assert.True(LibraryVideoPreview.FrameLooksVisible(pixels, 16));
    }
}
