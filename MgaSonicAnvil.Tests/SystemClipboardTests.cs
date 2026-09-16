using System.Runtime.InteropServices;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SystemClipboardTests
{
    [Fact]
    public void TryCompleteSet_SucceedsWhenSetAndPersistWork()
    {
        var set = 0;
        var persist = 0;
        Assert.True(SystemClipboard.TryCompleteSet(() => set++, () => persist++));
        Assert.Equal(1, set);
        Assert.Equal(1, persist);
    }

    [Fact]
    public void TryCompleteSet_IgnoresPersistBusy()
    {
        var set = 0;
        var persist = 0;
        var ok = SystemClipboard.TryCompleteSet(
            () => set++,
            () =>
            {
                persist++;
                throw new COMException("busy", SystemClipboard.ClipbrdECantOpen);
            });
        Assert.True(ok);
        Assert.Equal(1, set);
        Assert.Equal(1, persist);
    }

    [Fact]
    public void TryCompleteSet_FailsWhenSetBusy()
    {
        var persist = 0;
        var ok = SystemClipboard.TryCompleteSet(
            () => throw new COMException("busy", SystemClipboard.ClipbrdECantOpen),
            () => persist++);
        Assert.False(ok);
        Assert.Equal(0, persist);
    }

    [Fact]
    public void ErrorClipboardBusy_HasJaAndEn()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            var ja = UiStrings.ErrorClipboardBusy;
            UiStrings.SetLanguage(UiLanguage.English);
            var en = UiStrings.ErrorClipboardBusy;
            Assert.Contains("クリップボード", ja);
            Assert.Contains("clipboard", en, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(ja, en);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }
}
