using System.Windows;
using Microsoft.Win32;
using MgaSonicAnvil.Config;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

/// <summary>設定と OS 配色から背景／文字を入れ替える。</summary>
internal static class UiThemeService
{
    private static bool _started;
    private static bool _applied;

    public static event EventHandler? Changed;

    public static UiTheme Current { get; private set; } = UiTheme.Dark;

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        ApplyFromSettings(force: true);
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        if (Application.Current is { } app)
        {
            app.Exit += (_, _) => Stop();
        }
    }

    public static void Stop()
    {
        if (!_started)
        {
            return;
        }

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _started = false;
    }

    public static void ApplyFromSettings(bool force = false)
    {
        var choice = UiThemes.ParseChoice(AppStorage.Settings.UiTheme);
        Apply(UiThemes.Resolve(choice, OsUsesLightTheme()), force);
    }

    public static void ToggleDarkLight()
    {
        var choice = UiThemes.ToggledChoice(Current);
        AppStorage.Settings.UiTheme = UiThemes.ToStoredValue(choice);
        AppStorage.Save();
        Apply(UiThemes.Resolve(choice, OsUsesLightTheme()), force: true);
    }

    public static void Apply(UiTheme theme, bool force = false)
    {
        if (!force && _applied && theme == Current)
        {
            return;
        }

        Current = theme;
        _applied = true;
        UiThemePalette.Apply(theme);
        RefreshWindowChrome();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool OsUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int flag && flag != 0
                || value is uint unsigned && unsigned != 0;
        }
        catch
        {
            return false;
        }
    }

    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not (UserPreferenceCategory.General or UserPreferenceCategory.Color))
        {
            return;
        }

        var app = Application.Current;
        if (app is null)
        {
            return;
        }

        if (app.Dispatcher.CheckAccess())
        {
            ApplyFromSettings();
            return;
        }

        _ = app.Dispatcher.BeginInvoke(static () => ApplyFromSettings());
    }

    private static void RefreshWindowChrome()
    {
        if (Application.Current is null)
        {
            return;
        }

        foreach (Window window in Application.Current.Windows)
        {
            DarkWindowChrome.ApplyImmersiveDarkTitleBar(window);
        }
    }
}
