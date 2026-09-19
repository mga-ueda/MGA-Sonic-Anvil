using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace MgaSonicAnvil.UI;

/// <summary>プレイヤーのスクロールバー。普段は隠し、端に近いときだけ出す。</summary>
internal static class LibraryScrollReveal
{
    public static readonly DependencyProperty RevealedProperty = DependencyProperty.RegisterAttached(
        "Revealed",
        typeof(bool),
        typeof(LibraryScrollReveal),
        new PropertyMetadata(false));

    public static void SetRevealed(DependencyObject element, bool value) =>
        element.SetValue(RevealedProperty, value);

    public static bool GetRevealed(DependencyObject element) =>
        (bool)element.GetValue(RevealedProperty);

    public static void Attach(FrameworkElement host)
    {
        host.PreviewMouseMove += Host_PreviewMouseMove;
        host.MouseLeave += Host_MouseLeave;
    }

    public static bool IsNearEdge(double position, double length, double distance) =>
        length > 0 && position >= length - distance;

    private static void Host_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not DependencyObject host)
        {
            return;
        }

        var viewer = FindScrollViewer(host);
        if (viewer is null)
        {
            return;
        }

        var pos = e.GetPosition(viewer);
        var distance = DesignMetrics.LibraryScrollRevealDistance;
        Apply(
            viewer,
            IsNearEdge(pos.X, viewer.ActualWidth, distance),
            IsNearEdge(pos.Y, viewer.ActualHeight, distance));
    }

    private static void Host_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is DependencyObject host)
        {
            Apply(FindScrollViewer(host), vertical: false, horizontal: false);
        }
    }

    private static void Apply(ScrollViewer? viewer, bool vertical, bool horizontal)
    {
        if (viewer is null)
        {
            return;
        }

        foreach (var bar in EnumerateScrollBars(viewer))
        {
            var show = IsHeld(bar)
                || (bar.Orientation == Orientation.Vertical ? vertical : horizontal);
            SetRevealed(bar, show);
        }
    }

    private static bool IsHeld(ScrollBar bar) =>
        bar.IsMouseOver || IsThumbDragging(bar);

    private static bool IsThumbDragging(DependencyObject root)
    {
        if (root is Thumb { IsDragging: true })
        {
            return true;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (IsThumbDragging(VisualTreeHelper.GetChild(root, i)))
            {
                return true;
            }
        }

        return false;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer)
        {
            return viewer;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<ScrollBar> EnumerateScrollBars(DependencyObject root)
    {
        if (root is ScrollBar bar)
        {
            yield return bar;
            yield break;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in EnumerateScrollBars(VisualTreeHelper.GetChild(root, i)))
            {
                yield return child;
            }
        }
    }
}

/// <summary>スプリッターの線。掴みは細く、表示だけ広く判定する。</summary>
internal static class LibrarySplitterReveal
{
    public static bool IsNearLine(double position, double line, double distance) =>
        Math.Abs(position - line) <= distance;

    public static void Attach(
        FrameworkElement root,
        GridSplitter vertical,
        GridSplitter horizontal,
        ColumnDefinition treeColumn,
        RowDefinition explorerRow)
    {
        root.PreviewMouseMove += (_, e) => Update(root, e, vertical, horizontal, treeColumn, explorerRow);
        root.MouseLeave += (_, _) =>
        {
            Reveal(vertical, show: false);
            Reveal(horizontal, show: false);
        };
    }

    private static void Update(
        FrameworkElement root,
        MouseEventArgs e,
        GridSplitter vertical,
        GridSplitter horizontal,
        ColumnDefinition treeColumn,
        RowDefinition explorerRow)
    {
        var pos = e.GetPosition(root);
        var distance = DesignMetrics.LibrarySplitterRevealDistance;
        var treeWidth = treeColumn.ActualWidth;
        Reveal(vertical, IsNearLine(pos.X, treeWidth, distance));
        Reveal(
            horizontal,
            pos.X <= treeWidth + distance && IsNearLine(pos.Y, explorerRow.ActualHeight, distance));
    }

    private static void Reveal(GridSplitter splitter, bool show)
    {
        LibraryScrollReveal.SetRevealed(splitter, show || splitter.IsDragging);
    }
}
