using MgaSonicAnvil.Domain;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class TabTimeListTests
{
    [Fact]
    public void FormatLine_JoinsFileNameTabAndTimecode()
    {
        Assert.Equal("kick.wav\t00:01.000", TabTimeList.FormatLine("kick.wav", 48000, 48000));
        Assert.Equal("untitled\t00:00.000", TabTimeList.FormatLine("untitled", 0, 48000));
    }

    [Fact]
    public void Format_JoinsRowsWithCrlf()
    {
        var text = TabTimeList.Format(
        [
            ("a.wav", 48000, 48000),
            ("b.wav", 96000, 48000),
        ]);
        Assert.Equal("a.wav\t00:01.000\r\nb.wav\t00:02.000", text);
    }

    [Fact]
    public void Format_EmptyTabs_IsEmpty()
    {
        Assert.Equal(string.Empty, TabTimeList.Format([]));
    }
}
