using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// 選択タブのシアン下線を、隣のタブからスライドさせて移す。
/// </summary>
internal sealed class SlidingTabAccent
{
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(SlidingTabAccent),
        typeof(SlidingTabAccent));

    private static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(180);
    private static readonly List<WeakReference<SlidingTabAccent>> Live = [];
    private static int _installed;

    private readonly TabControl _tabs;
    private Canvas? _host;
    private Border? _accent;
    private bool _placed;

    private SlidingTabAccent(TabControl tabs) => _tabs = tabs;

    /// <summary>全 TabControl に下線スライドを付ける。</summary>
    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        EventManager.RegisterClassHandler(
            typeof(TabControl),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler((_, e) =>
            {
                if (e.Source is TabControl tabs)
                {
                    Attach(tabs);
                }
            }));
        UiThemeService.Changed += (_, _) => RefreshAllBrushes();
    }

    public static void Attach(TabControl tabs)
    {
        if (tabs.GetValue(StateProperty) is SlidingTabAccent)
        {
            return;
        }

        var state = new SlidingTabAccent(tabs);
        tabs.SetValue(StateProperty, state);
        lock (Live)
        {
            Live.Add(new WeakReference<SlidingTabAccent>(state));
        }

        tabs.Loaded += (_, _) => state.OnLoaded();
        tabs.SelectionChanged += (_, _) => state.Move(animate: true);
        tabs.SizeChanged += (_, _) => state.Move(animate: false);
        if (tabs.IsLoaded)
        {
            state.OnLoaded();
        }
    }

    public static void RefreshAllBrushes()
    {
        lock (Live)
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].TryGetTarget(out var state))
                {
                    Live.RemoveAt(i);
                    continue;
                }

                state.RefreshBrushCore();
            }
        }
    }

    private void RefreshBrushCore()
    {
        if (_accent is null)
        {
            return;
        }

        _accent.Background = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush"));
    }

    private void OnLoaded()
    {
        if (!EnsureHost())
        {
            _tabs.Dispatcher.BeginInvoke(OnLoaded, System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        Move(animate: false);
    }

    private bool EnsureHost()
    {
        if (_host is not null && _accent is not null)
        {
            return true;
        }

        var panel = FindDescendant<TabPanel>(_tabs);
        if (panel is null || VisualTreeHelper.GetParent(panel) is not Grid grid)
        {
            return false;
        }

        var host = new Canvas
        {
            IsHitTestVisible = false,
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        Grid.SetRow(host, Grid.GetRow(panel));
        Panel.SetZIndex(host, 1);
        grid.Children.Add(host);

        var accent = new Border
        {
            Height = 2,
            SnapsToDevicePixels = true,
            Background = WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush")),
            Width = 0,
        };
        host.Children.Add(accent);

        _host = host;
        _accent = accent;
        panel.SizeChanged += (_, _) => Move(animate: false);
        return true;
    }

    private void Move(bool animate)
    {
        if (_host is null || _accent is null || !EnsureHost())
        {
            return;
        }

        if (_tabs.SelectedIndex < 0
            || _tabs.ItemContainerGenerator.ContainerFromIndex(_tabs.SelectedIndex) is not TabItem item
            || item.ActualWidth <= 0)
        {
            _accent.BeginAnimation(Canvas.LeftProperty, null);
            _accent.BeginAnimation(FrameworkElement.WidthProperty, null);
            _accent.Width = 0;
            _placed = false;
            return;
        }

        Point origin;
        Point bottom;
        try
        {
            origin = item.TranslatePoint(new Point(0, 0), _host);
            bottom = item.TranslatePoint(new Point(0, item.ActualHeight), _host);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // ホスト高さがまだ 0 の初回でも、タブ下端基準で置く（ActualHeight だと上端に張り付く）。
        if (item.ActualHeight < 1 || bottom.Y <= origin.Y)
        {
            _tabs.Dispatcher.BeginInvoke(() => Move(animate: false), System.Windows.Threading.DispatcherPriority.Loaded);
            return;
        }

        var left = origin.X;
        var width = item.ActualWidth;
        var top = bottom.Y - _accent.Height;
        Canvas.SetTop(_accent, top);

        if (!animate || !_placed || double.IsNaN(Canvas.GetLeft(_accent)))
        {
            _accent.BeginAnimation(Canvas.LeftProperty, null);
            _accent.BeginAnimation(FrameworkElement.WidthProperty, null);
            Canvas.SetLeft(_accent, left);
            _accent.Width = width;
            _placed = true;
            return;
        }

        Animate(_accent, Canvas.LeftProperty, Canvas.GetLeft(_accent), left);
        Animate(_accent, FrameworkElement.WidthProperty, _accent.Width, width);
        _placed = true;
    }

    private static void Animate(UIElement target, DependencyProperty property, double from, double to)
    {
        if (Math.Abs(from - to) < 0.5)
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
            return;
        }

        var anim = new DoubleAnimation(from, to, SlideDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) =>
        {
            target.BeginAnimation(property, null);
            target.SetValue(property, to);
        };
        target.BeginAnimation(property, anim);
    }

    private static T? FindDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
