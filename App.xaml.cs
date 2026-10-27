using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 設定ウィンドウと同じタイトルバー色を、アプリ内の全 Window に適用する。
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ApplyDarkTitleBarOnWindowLoaded));
        EditorContextMenuTheme.Apply(Resources);
        AcrylicPopup.Install();
        SlidingTabAccent.Install();
        ImeComposition.Install(this);
        UiColors.Load();
        UiThemeService.Start();
        UiScaleService.Start();
        base.OnStartup(e);
    }

    private static void ApplyDarkTitleBarOnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            DarkWindowChrome.ApplyImmersiveDarkTitleBar(window);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ActivationClassBeacon.Stop();
        base.OnExit(e);
    }
}
