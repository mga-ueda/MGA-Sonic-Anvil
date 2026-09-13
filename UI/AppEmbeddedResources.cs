using System.IO;
using MgaSonicAnvil.Domain;

namespace MgaSonicAnvil.UI;

internal static class AppEmbeddedResources
{
    private const string LogoDarkName = "MgaSonicAnvil.Branding.MiyabiGameAudio.png";
    private const string LogoLightName = "MgaSonicAnvil.Branding.MiyabiGameAudio.Light.png";

    public static Stream? OpenLogo() => OpenLogo(UiThemeService.Current);

    public static Stream? OpenLogo(UiTheme theme)
    {
        var assembly = typeof(AppEmbeddedResources).Assembly;
        if (theme == UiTheme.Light)
        {
            return assembly.GetManifestResourceStream(LogoLightName)
                ?? assembly.GetManifestResourceStream(LogoDarkName);
        }

        return assembly.GetManifestResourceStream(LogoDarkName);
    }
}
