using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class DurationFormatTests
{
    [Theory]
    [InlineData("00:00.000", 0)]
    [InlineData("00:01.250", 1.25)]
    [InlineData("01:03.000", 63)]
    [InlineData("1:03.5", 63.5)]
    [InlineData("12.345", 12.345)]
    [InlineData("1:02:03.004", 3723.004)]
    public void TryParseDuration_AcceptsCommonForms(string text, double expected)
    {
        Assert.True(UiStrings.TryParseDuration(text, out var seconds));
        Assert.Equal(expected, seconds, 3);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("00:60.000")]
    [InlineData("-1.0")]
    public void TryParseDuration_RejectsInvalid(string text)
    {
        Assert.False(UiStrings.TryParseDuration(text, out _));
    }

    [Fact]
    public void FormatDuration_ThenParse_RoundTrips()
    {
        Assert.True(UiStrings.TryParseDuration(UiStrings.FormatDuration(125.5), out var seconds));
        Assert.Equal(125.5, seconds, 3);
    }
}
