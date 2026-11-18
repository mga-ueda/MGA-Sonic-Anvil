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

/// <summary>プレイヤーのスクロールバー。普段は隠し、端付近／ホバー／選択時だけ出す。</summary>
internal static class LibraryScrollReveal
{
    public static readonly DependencyProperty RevealedProperty = DependencyProperty.RegisterAttached(
        "Revealed",
        typeof(bool),
        typeof(LibraryScrollReveal),
        new PropertyMetadata(false));

    private static readonly DependencyProperty HoverRevealProperty = DependencyProperty.RegisterAttached(
        "HoverReveal",
        typeof(bool),
        typeof(LibraryScrollReveal),
        new PropertyMetadata(false));

    /// <summary>ホイール／キー等で出したあいだ、端から外れても消さない。</summary>
    private static readonly DependencyProperty TemporaryRevealProperty = DependencyProperty.RegisterAttached(
        "TemporaryReveal",
        typeof(bool),
        typeof(LibraryScrollReveal),
        new PropertyMetadata(false));

    private static readonly DependencyProperty ScrollRevealHookedProperty = DependencyProperty.RegisterAttached(
        "ScrollRevealHooked",
        typeof(bool),
        typeof(LibraryScrollReveal),
        new PropertyMetadata(false));

    public static void SetRevealed(DependencyObject element, bool value) =>
        element.SetValue(RevealedProperty, value);

    public static bool GetRevealed(DependencyObject element) =>
        (bool)element.GetValue(RevealedProperty);

    /// <summary>
    /// 一時表示中／ホバー常時表示なら両軸。それ以外は端判定どおり。
    /// 連続ホイールで内容が動いても点滅しないようにする。
    /// </summary>
    public static void ResolveRevealAxes(
        bool temporary,
        bool hoverReveal,
        bool nearVertical,
        bool nearHorizontal,
        out bool vertical,
        out bool horizontal)
    {
        if (temporary || hoverReveal)
        {
            vertical = true;
            horizontal = true;
            return;
        }

        vertical = nearVertical;
        horizontal = nearHorizontal;
    }

    /// <summary>実スクロール（キー／ホイール）のとき一時表示する。</summary>
    public static bool ShouldRevealOnScrollChange(double verticalChange, double horizontalChange) =>
        verticalChange != 0 || horizontalChange != 0;

    /// <summary>
    /// ↑↓／Page／Home／End／←→。スクロールが始まっていなくても、溢れていればバーを出す。
    /// Alt 付きはルート並び替え等の別用途（実スクロールは ScrollChanged 側）。
    /// </summary>
    public static bool IsScrollRevealNavigationKey(Key key, ModifierKeys modifiers) =>
        (modifiers & ModifierKeys.Alt) == 0
        && key is Key.Up or Key.Down or Key.PageUp or Key.PageDown
            or Key.Home or Key.End or Key.Left or Key.Right;

    /// <summary>縦または横に溢れているときだけスクロールバーを出す。</summary>
    public static bool NeedsScrollBars(double scrollableHeight, double scrollableWidth) =>
        scrollableHeight > 0.5 || scrollableWidth > 0.5;

    /// <summary>Scrollable／Extent／Computed のいずれかで溢れを判定する。</summary>
    public static bool NeedsScrollBars(ScrollViewer viewer) =>
        NeedsScrollBars(viewer.ScrollableHeight, viewer.ScrollableWidth)
        || viewer.ExtentHeight > viewer.ViewportHeight + 0.5
        || viewer.ExtentWidth > viewer.ViewportWidth + 0.5
        || viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible
        || viewer.ComputedHorizontalScrollBarVisibility == Visibility.Visible;

    /// <summary>端付近に近いときだけ出す（ツリー／リストなど）。</summary>
    public static void Attach(FrameworkElement host)
    {
        host.SetValue(HoverRevealProperty, false);
        host.PreviewMouseMove += Host_PreviewMouseMove;
        host.MouseLeave += Host_MouseLeave;
        AttachScrollReveal(host);
    }

    /// <summary>ホスト上にカーソルがあるあいだ出す（情報エリア）。</summary>
    public static void AttachOnHover(FrameworkElement host)
    {
        host.SetValue(HoverRevealProperty, true);
        host.PreviewMouseMove += Host_PreviewMouseMove;
        host.MouseLeave += Host_MouseLeave;
        AttachScrollReveal(host);
    }

    /// <summary>
    /// 子孫 ScrollViewer の実スクロールと、ナビキーでも一時表示（ツリー／お気に入り／リスト）。
    /// 端にマウスが無く、まだスクロールしていなくても、溢れていればバーを出す。
    /// </summary>
    private static void AttachScrollReveal(FrameworkElement host)
    {
        if ((bool)host.GetValue(ScrollRevealHookedProperty))
        {
            return;
        }

        host.SetValue(ScrollRevealHookedProperty, true);
        host.AddHandler(
            ScrollViewer.ScrollChangedEvent,
            new ScrollChangedEventHandler(Host_ScrollChanged),
            handledEventsToo: true);
        // MainWindow／ツリー側が先に Handled しても届くようにする。
        host.AddHandler(
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(Host_PreviewKeyDown),
            handledEventsToo: true);
    }

    private static void Host_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!ShouldRevealOnScrollChange(e.VerticalChange, e.HorizontalChange)
            || sender is not FrameworkElement host)
        {
            return;
        }

        RevealTemporarily(host);
    }

    private static void Host_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not FrameworkElement host)
        {
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (!IsScrollRevealNavigationKey(key, Keyboard.Modifiers))
        {
            return;
        }

        RevealTemporarilyIfScrollable(host);
    }

    /// <summary>溢れているときだけ一時表示。収まっているリストでは出さない。</summary>
    public static void RevealTemporarilyIfScrollable(FrameworkElement host)
    {
        var viewer = FindScrollViewer(host);
        if (viewer is null || !NeedsScrollBars(viewer))
        {
            return;
        }

        RevealTemporarily(host, viewer);
    }

    /// <summary>選択などで一時的に出す。端付近でなくてもよい。動かなければ約 3 秒で消える。</summary>
    public static void RevealTemporarily(FrameworkElement host) =>
        RevealTemporarily(host, FindScrollViewer(host));

    private static void RevealTemporarily(FrameworkElement host, ScrollViewer? viewer)
    {
        if (viewer is null)
        {
            return;
        }

        host.SetValue(TemporaryRevealProperty, true);
        Apply(viewer, vertical: true, horizontal: true, idle: false);
        LibraryHoverIdle.Arm(host, () => OnScrollIdle(host));
    }

    public static bool IsNearEdge(double position, double length, double distance) =>
        IsNearEnd(position, length, distance, outsideSlop: 0);

    /// <summary>
    /// ビューの終端付近。スプリッターがはみ出す外側だけ余裕を見る。
    /// </summary>
    public static bool IsNearEnd(double position, double length, double distance, double outsideSlop)
    {
        if (length <= 0)
        {
            return false;
        }

        return position >= length - distance && position <= length + outsideSlop;
    }

    /// <summary>縦バー。右端付近かつ、そのビューの高さの中。</summary>
    public static bool IsOverVerticalBar(
        double x,
        double y,
        double width,
        double height,
        double distance,
        double slop) =>
        IsNearEnd(x, width, distance, slop) && y >= -slop && y <= height + slop;

    /// <summary>横バー。下端付近かつ、そのビューの幅の中。隣ペインでは出さない。</summary>
    public static bool IsOverHorizontalBar(
        double x,
        double y,
        double width,
        double height,
        double distance,
        double slop) =>
        IsNearEnd(y, height, distance, slop) && x >= -slop && x <= width + slop;

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

        var temporary = (bool)host.GetValue(TemporaryRevealProperty);
        var hoverReveal = (bool)host.GetValue(HoverRevealProperty);
        var nearVertical = false;
        var nearHorizontal = false;
        if (!temporary && !hoverReveal)
        {
            var pos = e.GetPosition(viewer);
            var distance = DesignMetrics.LibraryScrollRevealDistance;
            var slop = DesignMetrics.LibrarySplitterHitThickness;
            nearVertical = IsOverVerticalBar(
                pos.X, pos.Y, viewer.ActualWidth, viewer.ActualHeight, distance, slop);
            nearHorizontal = IsOverHorizontalBar(
                pos.X, pos.Y, viewer.ActualWidth, viewer.ActualHeight, distance, slop);
        }

        ResolveRevealAxes(
            temporary,
            hoverReveal,
            nearVertical,
            nearHorizontal,
            out var vertical,
            out var horizontal);
        Apply(viewer, vertical, horizontal, idle: false);
        LibraryHoverIdle.Arm(host, () => OnScrollIdle(host));
    }

    private static void OnScrollIdle(FrameworkElement host)
    {
        host.SetValue(TemporaryRevealProperty, false);
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

        host.SetValue(TemporaryRevealProperty, false);
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
        RowDefinition explorerRow,
        GridSplitter? structureVertical = null,
        ColumnDefinition? structureColumn = null)
    {
        root.PreviewMouseMove += (_, e) =>
        {
            Update(
                root,
                e,
                vertical,
                horizontal,
                treeColumn,
                explorerRow,
                structureVertical,
                structureColumn,
                idle: false);
            LibraryHoverIdle.Arm(
                root,
                () => OnSplitterIdle(root, vertical, horizontal, structureVertical));
        };
        root.MouseLeave += (_, _) =>
        {
            LibraryHoverIdle.Disarm(root);
            Reveal(vertical, show: false, idle: true);
            Reveal(horizontal, show: false, idle: true);
            if (structureVertical is not null)
            {
                Reveal(structureVertical, show: false, idle: true);
            }
        };
    }

    private static void OnSplitterIdle(
        FrameworkElement root,
        GridSplitter vertical,
        GridSplitter horizontal,
        GridSplitter? structureVertical)
    {
        Reveal(vertical, show: false, idle: true);
        Reveal(horizontal, show: false, idle: true);
        if (structureVertical is not null)
        {
            Reveal(structureVertical, show: false, idle: true);
        }

        var dragging = vertical.IsDragging
            || horizontal.IsDragging
            || (structureVertical?.IsDragging ?? false);
        if (dragging)
        {
            LibraryHoverIdle.Arm(
                root,
                () => OnSplitterIdle(root, vertical, horizontal, structureVertical));
        }
    }

    private static void Update(
        FrameworkElement root,
        MouseEventArgs e,
        GridSplitter vertical,
        GridSplitter horizontal,
        ColumnDefinition treeColumn,
        RowDefinition explorerRow,
        GridSplitter? structureVertical,
        ColumnDefinition? structureColumn,
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
        if (structureVertical is not null && structureColumn is not null)
        {
            var structureLine = root.ActualWidth - structureColumn.ActualWidth;
            Reveal(structureVertical, IsNearLine(pos.X, structureLine, distance), idle);
        }
    }

    private static void Reveal(GridSplitter splitter, bool show, bool idle) =>
        LibraryScrollReveal.SetRevealed(
            splitter,
            LibraryHoverIdle.ShouldReveal(show, splitter.IsDragging, idle));
}
