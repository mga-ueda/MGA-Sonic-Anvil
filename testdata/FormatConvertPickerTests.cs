using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class FormatConvertPickerTests
{
    [Fact]
    public void ResolveHighlightedItem_PrefersCurrentWhenTwoItemsStayHighlighted()
    {
        RunSta(() =>
        {
            var previous = new MenuItem { Tag = 16 };
            var current = new MenuItem { Tag = 24 };
            SetHighlighted(previous, true);
            SetHighlighted(current, true);

            // 切替中は両方 true のまま残る。先頭（1つ前）ではなく今の選択を返す。
            Assert.Same(current, FormatConvertPicker.ResolveHighlightedItem([previous, current], current));
            Assert.Same(previous, FormatConvertPicker.ResolveHighlightedItem([previous, current], previous));
        });
    }

    [Fact]
    public void ResolveHighlightedItem_FallsBackToFirstHighlighted()
    {
        RunSta(() =>
        {
            var first = new MenuItem { Tag = 8 };
            var second = new MenuItem { Tag = 16 };
            SetHighlighted(first, true);

            Assert.Same(first, FormatConvertPicker.ResolveHighlightedItem([first, second], preferred: null));
        });
    }

    private static void SetHighlighted(MenuItem item, bool highlighted)
    {
        var key = typeof(MenuItem)
            .GetField("IsHighlightedPropertyKey", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetValue(null) as DependencyPropertyKey;
        Assert.NotNull(key);
        item.SetValue(key, highlighted);
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
