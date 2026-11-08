using System.Windows;
using System.Windows.Controls;
using MgaSonicAnvil.Domain;
using MgaSonicAnvil.UI;
using Xunit;

namespace MgaSonicAnvil.Tests;

public sealed class SelectionBandCornersTests
{
    [Theory]
    [InlineData(0, 1, (byte)SelectionBandEdge.Solo)]
    [InlineData(0, 3, (byte)SelectionBandEdge.First)]
    [InlineData(1, 3, (byte)SelectionBandEdge.Middle)]
    [InlineData(2, 3, (byte)SelectionBandEdge.Last)]
    [InlineData(0, 2, (byte)SelectionBandEdge.First)]
    [InlineData(1, 2, (byte)SelectionBandEdge.Last)]
    public void EdgeAt_MapsIndexInRun(int index, int count, byte expected) =>
        Assert.Equal((SelectionBandEdge)expected, SelectionBandCorners.EdgeAt(index, count));

    [Fact]
    public void Of_RoundsOnlyExposedEnds()
    {
        const double radius = 5;
        Assert.Equal(new CornerRadius(radius), SelectionBandCorners.Of(SelectionBandEdge.Solo, radius));
        Assert.Equal(new CornerRadius(radius, radius, 0, 0), SelectionBandCorners.Of(SelectionBandEdge.First, radius));
        Assert.Equal(default, SelectionBandCorners.Of(SelectionBandEdge.Middle, radius));
        Assert.Equal(new CornerRadius(0, 0, radius, radius), SelectionBandCorners.Of(SelectionBandEdge.Last, radius));
        Assert.Equal(default, SelectionBandCorners.Of(SelectionBandEdge.Middle, 0));
    }

    [Fact]
    public void TreeBandBleed_ExtendsLeftByIndentDepth()
    {
        Assert.Equal(new Thickness(0), LibraryBrowserView.TreeBandBleedMargin(0));
        Assert.Equal(
            new Thickness(-LibraryBrowserView.TreeChildIndent, 0, 0, 0),
            LibraryBrowserView.TreeBandBleedMargin(1));
        Assert.Equal(
            new Thickness(-2 * LibraryBrowserView.TreeChildIndent, 0, 0, 0),
            LibraryBrowserView.TreeBandBleedMargin(2));

        var padding = new Thickness(2, 1, 4, 1);
        Assert.Equal(padding, LibraryBrowserView.TreeBandBleedPadding(padding, 0));
        Assert.Equal(
            new Thickness(2 + LibraryBrowserView.TreeChildIndent, 1, 4, 1),
            LibraryBrowserView.TreeBandBleedPadding(padding, 1));
    }

    [Fact]
    public void MeasureTreeIndentDepth_CountsAncestors()
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }

                var tree = new TreeView();
                var root = new TreeViewItem();
                var child = new TreeViewItem();
                var grand = new TreeViewItem();
                tree.Items.Add(root);
                root.Items.Add(child);
                child.Items.Add(grand);
                Assert.Equal(0, LibraryBrowserView.MeasureTreeIndentDepth(root));
                Assert.Equal(1, LibraryBrowserView.MeasureTreeIndentDepth(child));
                Assert.Equal(2, LibraryBrowserView.MeasureTreeIndentDepth(grand));
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        Assert.Null(error?.ToString());
    }
}
