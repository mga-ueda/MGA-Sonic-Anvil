using System.Runtime.ExceptionServices;
using System.Windows;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class WindowPaintRevealTests
{
    [Fact]
    public void Reveal_SetsOpacityOnce()
    {
        RunSta(() =>
        {
            var window = new Window { Opacity = 0 };
            var revealed = 0;
            WindowPaintReveal.Attach(window, () => revealed++);
            Assert.True(WindowPaintReveal.IsPending(window));
            WindowPaintReveal.Reveal(window);
            Assert.Equal(1, window.Opacity);
            Assert.False(WindowPaintReveal.IsPending(window));
            Assert.Equal(1, revealed);
            WindowPaintReveal.Reveal(window);
            Assert.Equal(1, revealed);
        });
    }

    [Fact]
    public void Attach_IsIdempotent()
    {
        RunSta(() =>
        {
            var window = new Window { Opacity = 0 };
            WindowPaintReveal.Attach(window);
            WindowPaintReveal.Attach(window);
            WindowPaintReveal.Reveal(window);
            Assert.Equal(1, window.Opacity);
            Assert.False(WindowPaintReveal.IsPending(window));
        });
    }

    [Fact]
    public void TabTimeCopy_HasNoAccessKeyMarker()
    {
        var previous = UiStrings.Language;
        try
        {
            UiStrings.SetLanguage(UiLanguage.Japanese);
            Assert.Equal("コピー", UiStrings.TabTimeCopy);
            Assert.DoesNotContain("(_C)", UiStrings.TabTimeCopy);
            UiStrings.SetLanguage(UiLanguage.English);
            Assert.Equal("Copy", UiStrings.TabTimeCopy);
            Assert.DoesNotContain("_", UiStrings.TabTimeCopy);
        }
        finally
        {
            UiStrings.SetLanguage(previous);
        }
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
