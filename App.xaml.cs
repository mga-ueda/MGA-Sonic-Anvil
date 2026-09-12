using System.Windows;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        UiColors.Load();
        UiThemeService.Start();
        base.OnStartup(e);
    }
}
