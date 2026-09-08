using System.Windows;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class PopupCursorAvoidanceTests
{
    [Fact]
    public void Place_KeepsCenterWhenCursorMissesMenu()
    {
        var popup = new Size(200, 160);
        var target = new Size(1000, 800);
        var placed = PopupCursorAvoidance.Place(popup, target, new Point(20, 20));

        Assert.Equal(400, placed.X);
        Assert.Equal(320, placed.Y);
    }

    [Fact]
    public void Place_NudgeDownWhenCursorIsNearTop()
    {
        var popup = new Size(200, 160);
        var target = new Size(1000, 800);
        var cursor = new Point(500, 330);
        var placed = PopupCursorAvoidance.Place(popup, target, cursor);

        Assert.Equal(400, placed.X);
        Assert.Equal(cursor.Y + PopupCursorAvoidance.Gap, placed.Y);
        Assert.True(placed.Y > 320);
    }

    [Fact]
    public void Place_NudgeRightWhenCursorIsNearLeft()
    {
        var popup = new Size(200, 160);
        var target = new Size(1000, 800);
        var cursor = new Point(410, 400);
        var placed = PopupCursorAvoidance.Place(popup, target, cursor);

        Assert.Equal(cursor.X + PopupCursorAvoidance.Gap, placed.X);
        Assert.Equal(320, placed.Y);
    }

    [Fact]
    public void Place_StaysInsideTargetAndClearsCursor()
    {
        var popup = new Size(200, 200);
        var target = new Size(1000, 400);
        var cursor = new Point(500, 280);
        var placed = PopupCursorAvoidance.Place(popup, target, cursor);

        Assert.InRange(placed.X, 0, target.Width - popup.Width);
        Assert.InRange(placed.Y, 0, target.Height - popup.Height);
        Assert.True(cursor.Y < placed.Y || cursor.Y > placed.Y + popup.Height);
    }
}
