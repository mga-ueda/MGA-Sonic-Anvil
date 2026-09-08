using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class AppVersionTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.0.0-beta", "1.0.0-beta")]
    [InlineData("  V2.0.0  ", "2.0.0")]
    public void NormalizeTag_StripsLeadingV(string input, string expected) =>
        Assert.Equal(expected, AppVersion.NormalizeTag(input));

    [Fact]
    public void CompareSemVer_ReleaseIsNewerThanPrerelease() =>
        Assert.True(AppVersion.CompareSemVer("1.0.0", "1.0.0-beta") > 0);

    [Fact]
    public void CompareSemVer_HigherPatchIsNewer() =>
        Assert.True(AppVersion.CompareSemVer("0.0.2-beta", "0.0.1-beta") > 0);
}
