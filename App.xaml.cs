using System.Windows;
using MgaSonicAnvil.UI;

namespace MgaSonicAnvil;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UiColors.Load();
    }
}
