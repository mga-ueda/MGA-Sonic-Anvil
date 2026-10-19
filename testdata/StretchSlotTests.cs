using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class StretchSlotTests
{
    [Fact]
    public void LongText_DoesNotWidenStack()
    {
        RunSta(() =>
        {
            var path = new TextBox { Text = new string('W', 240), FontSize = 12 };
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new StretchSlot { Child = path });
            var button = new Border { Width = 64, Height = 22, Margin = new Thickness(8, 0, 0, 0) };
            Grid.SetColumn(button, 1);
            row.Children.Add(button);

            var videoColumn = new Border { Width = 140, Height = 80 };
            var associations = new Grid();
            associations.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            associations.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            associations.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            Grid.SetColumn(videoColumn, 2);
            associations.Children.Add(videoColumn);

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
            stack.Children.Add(row);
            stack.Children.Add(associations);
            stack.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            stack.Arrange(new Rect(stack.DesiredSize));

            Assert.InRange(stack.DesiredSize.Width, 296, 320);
            var videoX = videoColumn.TranslatePoint(new Point(0, 0), stack).X;
            Assert.InRange(videoX, 150, 180);
        });
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
