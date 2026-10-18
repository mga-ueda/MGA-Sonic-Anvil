using System.IO;
using MgaSonicAnvil.Wwise;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WwiseProjectActivatorTests
{
    [Fact]
    public void TryFindProjectFileNearDirectory_FindsWprojAboveOriginals()
    {
        var root = Path.Combine(Path.GetTempPath(), "anvil-wproj-" + Guid.NewGuid().ToString("N"));
        var originals = Path.Combine(root, "Originals", "SFX");
        Directory.CreateDirectory(originals);
        var wproj = Path.Combine(root, "MyGame.wproj");
        File.WriteAllText(wproj, "<WwiseDocument />");
        try
        {
            var found = WwiseProjectActivator.TryFindProjectFileNearDirectory(originals);
            Assert.Equal(wproj, found);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TryFindProjectFileNearDirectory_ReturnsEmptyWhenMissingOrAmbiguous()
    {
        Assert.Equal(string.Empty, WwiseProjectActivator.TryFindProjectFileNearDirectory(null));
        Assert.Equal(string.Empty, WwiseProjectActivator.TryFindProjectFileNearDirectory(""));

        var root = Path.Combine(Path.GetTempPath(), "anvil-wproj-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "A.wproj"), "<WwiseDocument />");
        File.WriteAllText(Path.Combine(root, "B.wproj"), "<WwiseDocument />");
        try
        {
            Assert.Equal(string.Empty, WwiseProjectActivator.TryFindProjectFileNearDirectory(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
