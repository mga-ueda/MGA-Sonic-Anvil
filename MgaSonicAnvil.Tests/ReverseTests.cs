using MgaSonicAnvil.Audio;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class ReverseTests
{
    [Fact]
    public void Apply_ReversesFramesPerChannel()
    {
        var source = new float[] { 1f, 10f, 2f, 20f, 3f, 30f };
        var after = Reverse.Apply(source, 2);
        Assert.Equal([3f, 30f, 2f, 20f, 1f, 10f], after);
        Assert.Equal(new float[] { 1f, 10f, 2f, 20f, 3f, 30f }, source);
    }
}
