using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class HistoryPasteBusyProgressTests
{
    [Fact]
    public void Capture_Empty_IsZero()
    {
        var snap = HistoryPasteBusyProgress.Capture([], [], []);
        Assert.Equal(0, snap.Overall);
        Assert.Equal(0, snap.Finished);
        Assert.Empty(snap.Jobs);
    }

    [Fact]
    public void Capture_WeightsByFrameCount()
    {
        var snap = HistoryPasteBusyProgress.Capture(
            ["short", "long"],
            [1, 0],
            [100, 900]);
        Assert.Equal(0.1, snap.Overall, 5);
        Assert.Equal(1, snap.Finished);
        Assert.Equal(2, snap.Jobs.Count);
        Assert.Equal("short", snap.Jobs[0].Name);
        Assert.Equal(1, snap.Jobs[0].Progress);
        Assert.Equal(0, snap.Jobs[1].Progress);
    }

    [Fact]
    public void OverlayPasteHistory_HasJaAndEn()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var ja = UiStrings.OverlayPasteHistory;
            UiStrings.SetLanguage(UiLanguage.English);
            var en = UiStrings.OverlayPasteHistory;
            Assert.Contains("編集履歴", ja);
            Assert.Contains("history", en, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(ja, en);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }
}
