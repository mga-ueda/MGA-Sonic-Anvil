using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace MgaSonicAnvil.UI;

/// <summary>
/// コントロールホバー時の説明文を WAAPI 上の Tips 枠へ出す。
/// Time Caster と同じく、初回は即表示、差し替え／消去は 300ms 待ち。見出し／本文クリックでロック。
/// </summary>
internal static class TipService
{
    private static readonly ConditionalWeakTable<FrameworkElement, TipBinding> Bindings = new();
    private static readonly ConditionalWeakTable<FrameworkElement, object> WiredParents = new();

    /// <summary>通過中の差し替え／消去だけ遅らせる（Time Caster HoverTipCommitMs）。</summary>
    private static readonly TimeSpan HoverTipCommit = TimeSpan.FromMilliseconds(300);

    private static TextBlock? _display;
    private static FrameworkElement? _host;
    private static ScrollViewer? _scroll;
    private static object? _activeSource;
    private static string? _currentText;
    private static int _suspendCount;
    private static bool _enabled = true;
    private static bool _pinned;
    private static bool _hostWired;
    private static DispatcherTimer? _commitTimer;
    private static string? _pendingTip;
    private static object? _pendingSource;
    private static bool _pendingClear;

    public static event EventHandler? PinChanged;

    public static bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;
            if (!_enabled)
            {
                SetPinned(false);
                ClearImmediate();
            }

            RelayoutHost();
        }
    }

    public static bool Pinned => _pinned;

    public static void BindDisplay(TextBlock display, FrameworkElement host, ScrollViewer? scroll = null)
    {
        _display = display;
        _host = host;
        _scroll = scroll ?? display.Parent as ScrollViewer;
        display.TextWrapping = TextWrapping.Wrap;
        display.TextTrimming = TextTrimming.None;
        EnsureHostWired(host);
        SetDisplayText(null);
    }

    public static void TogglePinned()
    {
        if (!_enabled)
        {
            return;
        }

        SetPinned(!_pinned);
        CancelPending();
        if (!_pinned)
        {
            RefreshFromMouse();
        }
    }

    public static void Set(FrameworkElement control, string? tip, bool respectsEnabled = true)
    {
        control.ToolTip = null;
        var binding = Bindings.GetOrCreateValue(control);
        binding.Text = tip ?? string.Empty;
        binding.RespectsEnabled = respectsEnabled;
        EnsureWired(control, binding);
    }

    public static void Show(string? text, object source, bool respectsEnabled = true)
    {
        if (_suspendCount > 0)
        {
            return;
        }

        if (!_enabled && respectsEnabled)
        {
            return;
        }

        if (IsHoverLocked())
        {
            CancelPending();
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            QueueHoverTipChange(null, source);
            return;
        }

        QueueHoverTipChange(text, source);
    }

    public static void Clear()
    {
        if (IsHoverLocked())
        {
            CancelPending();
            return;
        }

        QueueHoverTipChange(null, null);
    }

    public static void Clear(object source)
    {
        if (!ReferenceEquals(_activeSource, source))
        {
            return;
        }

        if (IsHoverLocked())
        {
            CancelPending();
            return;
        }

        QueueHoverTipChange(null, source);
    }

    public static void Suspend()
    {
        _suspendCount++;
        if (_suspendCount == 1)
        {
            ClearImmediate();
        }
    }

    public static void Resume()
    {
        if (_suspendCount > 0)
        {
            _suspendCount--;
        }
    }

    private static void SetPinned(bool pinned)
    {
        if (_pinned == pinned)
        {
            return;
        }

        _pinned = pinned;
        UpdateLockChrome();
        PinChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsHoverLocked() =>
        _pinned
        || _host is { IsMouseOver: true }
        || _scroll is { IsMouseCaptureWithin: true };

    private static void EnsureHostWired(FrameworkElement host)
    {
        if (_hostWired)
        {
            return;
        }

        _hostWired = true;
        host.MouseEnter += (_, _) => CancelPending();
        host.MouseLeave += (_, _) =>
        {
            if (IsHoverLocked())
            {
                CancelPending();
                return;
            }

            QueueHoverTipChange(null, null);
        };
    }

    private static void EnsureWired(FrameworkElement control, TipBinding binding)
    {
        if (!binding.Wired)
        {
            binding.Wired = true;
            control.MouseEnter += (_, _) =>
            {
                if (_suspendCount > 0)
                {
                    return;
                }

                if (!_enabled && binding.RespectsEnabled)
                {
                    return;
                }

                Show(binding.Text, control, binding.RespectsEnabled);
            };
            control.MouseLeave += (_, _) => Clear(control);
            control.IsEnabledChanged += (_, _) =>
            {
                if (!control.IsEnabled && ReferenceEquals(_activeSource, control))
                {
                    Clear(control);
                }
            };
            control.Unloaded += (_, _) =>
            {
                if (ReferenceEquals(_activeSource, control))
                {
                    ClearImmediateIfSource(control);
                }
            };
        }

        EnsureParentWired(control);
    }

    private static void QueueHoverTipChange(string? tip, object? source)
    {
        if (IsHoverLocked())
        {
            CancelPending();
            return;
        }

        var next = string.IsNullOrWhiteSpace(tip) ? null : tip.Trim();
        if (string.Equals(_currentText, next, StringComparison.Ordinal)
            || (next is null && _currentText is null))
        {
            if (next is not null)
            {
                _activeSource = source;
            }

            CancelPending();
            return;
        }

        // 初回表示はすぐ出す。通過中の差し替え／消去だけ遅らせる。
        if (_currentText is null && next is not null)
        {
            CancelPending();
            CommitHoverTip(next, source);
            return;
        }

        if (!_pendingClear && string.Equals(_pendingTip, next, StringComparison.Ordinal))
        {
            _pendingSource = source;
            return;
        }

        _pendingClear = next is null;
        _pendingTip = next;
        _pendingSource = source;
        var timer = EnsureCommitTimer();
        timer.Stop();
        timer.Start();
    }

    private static void CancelPending()
    {
        _pendingClear = false;
        _pendingTip = null;
        _pendingSource = null;
        _commitTimer?.Stop();
    }

    private static DispatcherTimer EnsureCommitTimer()
    {
        if (_commitTimer is not null)
        {
            return _commitTimer;
        }

        _commitTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = HoverTipCommit,
        };
        _commitTimer.Tick += (_, _) =>
        {
            _commitTimer.Stop();
            var clear = _pendingClear;
            var tip = _pendingTip;
            var source = _pendingSource;
            CancelPending();
            if (IsHoverLocked())
            {
                return;
            }

            CommitHoverTip(clear ? null : tip, source);
        };
        return _commitTimer;
    }

    private static void CommitHoverTip(string? tip, object? source)
    {
        if (string.IsNullOrEmpty(tip))
        {
            _activeSource = null;
            SetDisplayText(null);
            return;
        }

        _activeSource = source;
        SetDisplayText(tip);
    }

    private static void ClearImmediate()
    {
        CancelPending();
        _activeSource = null;
        SetDisplayText(null);
    }

    private static void ClearImmediateIfSource(object? source)
    {
        if (source is null || !ReferenceEquals(_activeSource, source))
        {
            CancelPending();
            return;
        }

        ClearImmediate();
    }

    private static void RefreshFromMouse()
    {
        if (_host is null || IsHoverLocked())
        {
            return;
        }

        var window = Window.GetWindow(_host);
        if (window is null)
        {
            QueueHoverTipChange(null, null);
            return;
        }

        var pos = Mouse.GetPosition(window);
        if (TryFindTip(window.InputHitTest(pos) as DependencyObject, out var owner, out var tip))
        {
            QueueHoverTipChange(tip, owner);
            return;
        }

        QueueHoverTipChange(null, null);
    }

    private static bool TryFindTip(DependencyObject? start, out FrameworkElement? owner, out string? tip)
    {
        for (var current = start; current is not null; current = GetVisualOrContentParent(current))
        {
            if (current is FrameworkElement element && Bindings.TryGetValue(element, out var binding)
                && !string.IsNullOrWhiteSpace(binding.Text))
            {
                if (!_enabled && binding.RespectsEnabled)
                {
                    continue;
                }

                owner = element;
                tip = binding.Text;
                return true;
            }
        }

        owner = null;
        tip = null;
        return false;
    }

    private static void EnsureParentWired(FrameworkElement control)
    {
        if (control.Parent is not FrameworkElement parent)
        {
            return;
        }

        _ = WiredParents.GetValue(parent, static p =>
        {
            p.PreviewMouseMove += Parent_PreviewMouseMove;
            p.MouseLeave += Parent_MouseLeave;
            return new object();
        });
    }

    private static void Parent_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement parent || _suspendCount > 0)
        {
            return;
        }

        if (IsHoverLocked())
        {
            CancelPending();
            return;
        }

        var hit = FindDisabledTipElement(parent, e.GetPosition(parent));
        if (hit is not null && Bindings.TryGetValue(hit, out var binding))
        {
            if (!_enabled && binding.RespectsEnabled)
            {
                return;
            }

            Show(binding.Text, hit, binding.RespectsEnabled);
            return;
        }

        if (_activeSource is FrameworkElement { IsEnabled: false } active
            && ReferenceEquals(GetParent(active), parent))
        {
            Clear(active);
        }
    }

    private static void Parent_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement parent)
        {
            return;
        }

        if (_activeSource is not FrameworkElement { IsEnabled: false } active
            || !ReferenceEquals(GetParent(active), parent))
        {
            return;
        }

        if (!parent.IsMouseOver)
        {
            Clear(active);
        }
    }

    private static FrameworkElement? GetParent(FrameworkElement element) =>
        element.Parent as FrameworkElement;

    private static FrameworkElement? FindDisabledTipElement(FrameworkElement parent, Point parentPoint)
    {
        var hit = parent.InputHitTest(parentPoint) as DependencyObject;
        while (hit is not null)
        {
            if (hit is FrameworkElement element
                && !element.IsEnabled
                && Bindings.TryGetValue(element, out _))
            {
                return element;
            }

            hit = GetVisualOrContentParent(hit);
        }

        return null;
    }

    private static DependencyObject? GetVisualOrContentParent(DependencyObject current)
    {
        if (current is Visual)
        {
            return VisualTreeHelper.GetParent(current);
        }

        if (current is ContentElement content)
        {
            var parent = ContentOperations.GetParent(content);
            if (parent is not null)
            {
                return parent;
            }

            return LogicalTreeHelper.GetParent(current);
        }

        return LogicalTreeHelper.GetParent(current);
    }

    private static void SetDisplayText(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        _currentText = value;
        if (_display is null)
        {
            return;
        }

        var shown = value ?? string.Empty;
        if (!_display.Dispatcher.CheckAccess())
        {
            _display.Dispatcher.BeginInvoke(() => ApplyDisplayText(shown));
            return;
        }

        ApplyDisplayText(shown);
    }

    private static void ApplyDisplayText(string value)
    {
        if (_display is null)
        {
            return;
        }

        if (!string.Equals(_display.Text, value, StringComparison.Ordinal))
        {
            _display.Text = value;
            ResetScroll();
        }

        RelayoutHost();
    }

    private static void ResetScroll()
    {
        (_scroll ?? _display?.Parent as ScrollViewer)?.ScrollToHome();
    }

    private static void RelayoutHost()
    {
        if (_host is null)
        {
            return;
        }

        if (!_enabled)
        {
            CollapseHost();
            return;
        }

        SetHostHeight(DesignMetrics.TipsPanelHeight);
        UpdateLockChrome();
    }

    private static void UpdateLockChrome()
    {
        if (_host is not Border border)
        {
            return;
        }

        if (_pinned)
        {
            border.SetValue(
                Border.BorderBrushProperty,
                WpfControlHelpers.FrozenBrush(Theme.Get("AccentCyanBrush")));
            return;
        }

        border.SetResourceReference(Border.BorderBrushProperty, "ChromeBorderBrush");
    }

    private static void CollapseHost()
    {
        if (_host is null)
        {
            return;
        }

        if (_host.Visibility != Visibility.Collapsed)
        {
            _host.Visibility = Visibility.Collapsed;
        }

        if (_host.Height != 0)
        {
            _host.Height = 0;
        }
    }

    private static void SetHostHeight(double height)
    {
        if (_host is null)
        {
            return;
        }

        if (_host.Visibility != Visibility.Visible)
        {
            _host.Visibility = Visibility.Visible;
        }

        if (Math.Abs(_host.Height - height) > 0.5)
        {
            _host.Height = height;
        }
    }

    private sealed class TipBinding
    {
        public string Text = string.Empty;
        public bool RespectsEnabled = true;
        public bool Wired;
    }
}
