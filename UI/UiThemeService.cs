using System.Windows;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>配色を適用する。実行時はダーク固定。</summary>
internal static class UiThemeService
{
    private static bool _started;
    private static bool _applied;

    public static event EventHandler? Changed;

    public static UiTheme Current { get; private set; } = UiTheme.Dark;

    /// <summary>画面に塗っている配色。実行時はダーク固定。</summary>
    public static UiTheme Painted { get; private set; } = UiTheme.Dark;

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        ApplyFromSettings(force: true);
        if (Application.Current is { } app)
        {
            app.Exit += (_, _) => Stop();
        }
    }

    public static void Stop()
    {
        _started = false;
    }

    public static void ApplyFromSettings(bool force = false)
    {
        Apply(UiTheme.Dark, force);
    }

    public static void Apply(UiTheme theme, bool force = false)
    {
        if (!force && _applied && theme == Current && theme == Painted)
        {
            return;
        }

        Current = theme;
        Paint();
    }

    private static void Paint()
    {
        Painted = Current;
        _applied = true;
        UiThemePalette.Apply(Painted);
        UiColors.ApplySaved(Painted);
        RefreshWindowChrome();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void RefreshWindowChrome()
    {
        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        var dispatcher = app.Dispatcher;
        if (!dispatcher.CheckAccess())
        {
            if (!dispatcher.Thread.IsAlive || dispatcher.HasShutdownStarted)
            {
                return;
            }

            dispatcher.Invoke(RefreshWindowChrome);
            return;
        }

        foreach (Window window in app.Windows)
        {
            DarkWindowChrome.ApplyImmersiveDarkTitleBar(window);
        }
    }
}
