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

    /// <summary>画面に塗っている配色。プレイヤー中は設定がライトでもダーク。</summary>
    public static UiTheme Painted { get; private set; } = UiTheme.Dark;

    private static bool _playerForcesDark;

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

    /// <summary>プレイヤー表示中は、設定がライトでもダークの色を塗る。</summary>
    public static void SetPlayerForcesDark(bool player)
    {
        if (_playerForcesDark == player)
        {
            return;
        }

        _playerForcesDark = player;
        Paint();
    }

    public static void Apply(UiTheme theme, bool force = false)
    {
        var painted = ResolvePainted(theme);
        if (!force && _applied && theme == Current && painted == Painted)
        {
            return;
        }

        Current = theme;
        Paint();
    }

    private static UiTheme ResolvePainted(UiTheme theme) =>
        _playerForcesDark ? UiTheme.Dark : theme;

    private static void Paint()
    {
        var painted = ResolvePainted(Current);
        Painted = painted;
        _applied = true;
        UiThemePalette.Apply(painted);
        UiColors.ApplySaved(painted);
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
