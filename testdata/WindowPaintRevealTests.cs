using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
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
    public void Attach_AutoRevealFalse_KeepsPendingUntilManualReveal()
    {
        RunSta(() =>
        {
            var window = new Window { Opacity = 0 };
            var painted = 0;
            WindowPaintReveal.Attach(window, () => painted++, autoReveal: false);
            Assert.True(WindowPaintReveal.IsPending(window));
            // Loaded / ContentRendered 相当: コールバックだけ先に走る想定を手動で再現しないため、
            // ここでは Reveal 前に pending のままであることと、Reveal で一度だけ出ることを見る。
            Assert.Equal(0, painted);
            WindowPaintReveal.Reveal(window);
            Assert.Equal(1, window.Opacity);
            Assert.False(WindowPaintReveal.IsPending(window));
            Assert.Equal(1, painted);
        });
    }

    [Fact]
    public void PrepareBeforeShow_ResetsPendingAfterReveal()
    {
        RunSta(() =>
        {
            EnsureThemeResources();
            var window = new Window { Opacity = 0, Width = 120, Height = 80 };
            var painted = 0;
            WindowPaintReveal.Attach(window, () => painted++);
            WindowPaintReveal.Reveal(window);
            Assert.False(WindowPaintReveal.IsPending(window));
            Assert.Equal(1, painted);

            WindowPaintReveal.PrepareBeforeShow(window);
            Assert.True(WindowPaintReveal.IsPending(window));
            WindowPaintReveal.Reveal(window);
            Assert.False(WindowPaintReveal.IsPending(window));
            Assert.Equal(2, painted);
        });
    }

    private static void EnsureThemeResources()
    {
        if (Application.Current is null)
        {
            _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        }

        var resources = Application.Current!.Resources;
        void Ensure(string key, byte r, byte g, byte b)
        {
            if (resources.Contains(key))
            {
                return;
            }

            resources[key] = new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        Ensure("SurfaceBackBrush", 0x1A, 0x1B, 0x26);
        Ensure("ChromeBorderBrush", 0x3A, 0x3A, 0x3E);
        Ensure("PrimaryForeBrush", 0xEB, 0xEB, 0xEB);
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
