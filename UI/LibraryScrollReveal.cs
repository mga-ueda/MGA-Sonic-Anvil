using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>プレイヤーのホバー表示。動かないと約 3 秒で消す。</summary>
internal static class LibraryHoverIdle
{
    public const double HideAfterSeconds = 3;

    public static TimeSpan HideAfter { get; } = TimeSpan.FromSeconds(HideAfterSeconds);

    /// <summary>ドラッグ中は出す。止まっていれば近くても出さない。</summary>
    public static bool ShouldReveal(bool near, bool held, bool idle) =>
        held || (near && !idle);

    public static void Arm(FrameworkElement host, Action onIdle)
    {
        host.SetValue(IdleCallbackProperty, onIdle);
        var timer = (DispatcherTimer?)host.GetValue(IdleTimerProperty);
        if (timer is null)
        {
            timer = new DispatcherTimer { Interval = HideAfter };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                (host.GetValue(IdleCallbackProperty) as Action)?.Invoke();
            };
            host.SetValue(IdleTimerProperty, timer);
            host.Unloaded += (_, _) => timer.Stop();
        }

        timer.Stop();
        timer.Start();
    }

    public static void Disarm(FrameworkElement host)
    {
        if (host.GetValue(IdleTimerProperty) is DispatcherTimer timer)
        {
            timer.Stop();
        }
    }

    private static readonly DependencyProperty IdleTimerProperty = DependencyProperty.RegisterAttached(
        "IdleTimer",
        typeof(DispatcherTimer),
        typeof(LibraryHoverIdle));

    private static readonly DependencyProperty IdleCallbackProperty = DependencyProperty.RegisterAttached(
        "IdleCallback",
        typeof(Action),
        typeof(LibraryHoverIdle));
}

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
        if (sender is not FrameworkElement host)
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
            IsNearEdge(pos.Y, viewer.ActualHeight, distance),
            idle: false);
        LibraryHoverIdle.Arm(host, () => OnScrollIdle(host));
    }

    private static void OnScrollIdle(FrameworkElement host)
    {
        var viewer = FindScrollViewer(host);
        Apply(viewer, vertical: false, horizontal: false, idle: true);
        if (viewer is not null && EnumerateScrollBars(viewer).Any(IsHeld))
        {
            LibraryHoverIdle.Arm(host, () => OnScrollIdle(host));
        }
    }

    private static void Host_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement host)
        {
            return;
        }

        LibraryHoverIdle.Disarm(host);
        Apply(FindScrollViewer(host), vertical: false, horizontal: false, idle: true);
    }

    private static void Apply(ScrollViewer? viewer, bool vertical, bool horizontal, bool idle)
    {
        if (viewer is null)
        {
            return;
        }

        foreach (var bar in EnumerateScrollBars(viewer))
        {
            var near = bar.Orientation == Orientation.Vertical ? vertical : horizontal;
            SetRevealed(bar, LibraryHoverIdle.ShouldReveal(near, IsHeld(bar), idle));
        }
    }

    private static bool IsHeld(ScrollBar bar) =>
        IsThumbDragging(bar);

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
        root.PreviewMouseMove += (_, e) =>
        {
            Update(root, e, vertical, horizontal, treeColumn, explorerRow, idle: false);
            LibraryHoverIdle.Arm(root, () => OnSplitterIdle(root, vertical, horizontal));
        };
        root.MouseLeave += (_, _) =>
        {
            LibraryHoverIdle.Disarm(root);
            Reveal(vertical, show: false, idle: true);
            Reveal(horizontal, show: false, idle: true);
        };
    }

    private static void OnSplitterIdle(FrameworkElement root, GridSplitter vertical, GridSplitter horizontal)
    {
        Reveal(vertical, show: false, idle: true);
        Reveal(horizontal, show: false, idle: true);
        if (vertical.IsDragging || horizontal.IsDragging)
        {
            LibraryHoverIdle.Arm(root, () => OnSplitterIdle(root, vertical, horizontal));
        }
    }

    private static void Update(
        FrameworkElement root,
        MouseEventArgs e,
        GridSplitter vertical,
        GridSplitter horizontal,
        ColumnDefinition treeColumn,
        RowDefinition explorerRow,
        bool idle)
    {
        var pos = e.GetPosition(root);
        var distance = DesignMetrics.LibrarySplitterRevealDistance;
        var treeWidth = treeColumn.ActualWidth;
        Reveal(vertical, IsNearLine(pos.X, treeWidth, distance), idle);
        Reveal(
            horizontal,
            pos.X <= treeWidth + distance && IsNearLine(pos.Y, explorerRow.ActualHeight, distance),
            idle);
    }

    private static void Reveal(GridSplitter splitter, bool show, bool idle) =>
        LibraryScrollReveal.SetRevealed(
            splitter,
            LibraryHoverIdle.ShouldReveal(show, splitter.IsDragging, idle));
}
